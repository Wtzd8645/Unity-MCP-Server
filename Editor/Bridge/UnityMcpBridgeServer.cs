#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcpBridge.Editor
{
    [InitializeOnLoad]
    public static class UnityMcpBridgeServer
    {
        private const string BridgeVersion = "0.1.0";
        private const string DefaultHttpPrefix = "http://127.0.0.1:38100/";
        private const string DefaultPipeName = "unity-mcp-bridge";
        private const int MainThreadTimeoutMs = 5000;
        private const int MaxLogBufferSize = 5000;

        private static readonly ConcurrentQueue<Action> MainThreadActions = new ConcurrentQueue<Action>();
        private static readonly UnityBridgeLogStore LogStore = new UnityBridgeLogStore(MaxLogBufferSize);

        private static bool _running;
        public static bool IsRunning { get { return _running; } }
        private static string _httpPrefix = DefaultHttpPrefix;
        private static string _pipeName = DefaultPipeName;

        private static HttpListener _httpListener;
        private static Thread _httpThread;
        private static Thread _pipeThread;

        static UnityMcpBridgeServer()
        {
            EditorApplication.update += FlushMainThreadActions;
            EditorApplication.quitting += OnEditorQuitting;
            Application.logMessageReceivedThreaded += OnUnityLogReceived;
            _running = false;
        }

        [MenuItem("Tools/Unity MCP Bridge/Start")]
        public static void Start()
        {
            if (_running)
            {
                Debug.Log("[Unity MCP Bridge] Already running.");
                return;
            }

            _httpPrefix = ResolveHttpPrefix();
            _pipeName = ResolvePipeName();
            _running = true;

            StartHttpServer();
            StartPipeServer();

            Debug.Log("[Unity MCP Bridge] Started. HTTP=" + _httpPrefix + ", Pipe=" + _pipeName);
        }

        [MenuItem("Tools/Unity MCP Bridge/Stop")]
        public static void Stop()
        {
            if (!_running)
            {
                Debug.Log("[Unity MCP Bridge] Not running.");
                return;
            }

            StopInternal();
            Debug.Log("[Unity MCP Bridge] Stopped.");
        }

        [MenuItem("Tools/Unity MCP Bridge/Start", true)]
        private static bool CanStart()
        {
            return !_running;
        }

        [MenuItem("Tools/Unity MCP Bridge/Stop", true)]
        private static bool CanStop()
        {
            return _running;
        }

        private static void StopInternal()
        {
            _running = false;
            StopHttpServer();
            StopPipeServer();
        }

        private static void OnEditorQuitting()
        {
            StopInternal();
        }

        private static void StartHttpServer()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add(_httpPrefix);
                _httpListener.Start();

                _httpThread = new Thread(HttpServerLoop)
                {
                    IsBackground = true,
                    Name = "UnityMcpBridge.Http",
                };
                _httpThread.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Unity MCP Bridge] HTTP start failed: " + ex.Message);
            }
        }

        private static void StopHttpServer()
        {
            try
            {
                if (_httpListener != null)
                {
                    _httpListener.Stop();
                    _httpListener.Close();
                    _httpListener = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Unity MCP Bridge] HTTP stop failed: " + ex.Message);
            }
        }

        private static void HttpServerLoop()
        {
            while (_running && _httpListener != null && _httpListener.IsListening)
            {
                HttpListenerContext context = null;
                try
                {
                    context = _httpListener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        Debug.LogWarning("[Unity MCP Bridge] HTTP accept failed: " + ex.Message);
                    }

                    continue;
                }

                try
                {
                    HandleHttpContext(context);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Unity MCP Bridge] HTTP request failed: " + ex.Message);
                    var errorResponse = BridgeResponses.Error("Internal bridge error.", "internal_error", null);
                    TryWriteHttpResponse(context, 500, JsonUtility.ToJson(errorResponse));
                }
            }
        }

        private static void HandleHttpContext(HttpListenerContext context)
        {
            if (context == null || context.Request == null || context.Response == null)
            {
                return;
            }

            if (context.Request.HttpMethod != "POST" ||
                context.Request.Url == null ||
                context.Request.Url.AbsolutePath != "/mcp/tool/call")
            {
                var routeError = BridgeResponses.Error("Route not found.", "route_not_found", null);
                TryWriteHttpResponse(context, 404, JsonUtility.ToJson(routeError));
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            BridgeToolCallRequest request;
            try
            {
                request = JsonUtility.FromJson<BridgeToolCallRequest>(body);
            }
            catch (Exception ex)
            {
                var jsonError = BridgeResponses.Error("Invalid JSON: " + ex.Message, "invalid_json", null);
                TryWriteHttpResponse(context, 400, JsonUtility.ToJson(jsonError));
                return;
            }

            if (request == null || string.IsNullOrEmpty(request.name))
            {
                var requestError = BridgeResponses.Error("Missing request.name.", "invalid_request", null);
                TryWriteHttpResponse(context, 400, JsonUtility.ToJson(requestError));
                return;
            }

            BridgeToolCallResponse response = ExecuteOnMainThreadWithTimeout(request, MainThreadTimeoutMs);
            TryWriteHttpResponse(context, 200, JsonUtility.ToJson(response));
        }

        private static void TryWriteHttpResponse(HttpListenerContext context, int statusCode, string jsonBody)
        {
            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentEncoding = Encoding.UTF8;
                context.Response.ContentLength64 = payload.Length;
                using (Stream output = context.Response.OutputStream)
                {
                    output.Write(payload, 0, payload.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Unity MCP Bridge] HTTP response write failed: " + ex.Message);
            }
        }

        private static void StartPipeServer()
        {
            _pipeThread = new Thread(PipeServerLoop)
            {
                IsBackground = true,
                Name = "UnityMcpBridge.Pipe",
            };
            _pipeThread.Start();
        }

        private static void StopPipeServer()
        {
            try
            {
                using (var breaker = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out))
                {
                    breaker.Connect(100);
                }
            }
            catch
            {
                // Best effort: only used to unblock WaitForConnection.
            }
        }

        private static void PipeServerLoop()
        {
            while (_running)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1))
                    {
                        server.WaitForConnection();
                        if (!_running)
                        {
                            break;
                        }

                        using (var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true))
                        using (var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true))
                        {
                            writer.AutoFlush = true;
                            string line = reader.ReadLine();
                            BridgeToolCallResponse response;

                            if (string.IsNullOrEmpty(line))
                            {
                                response = BridgeResponses.Error("Empty pipe request.", "invalid_request", null);
                            }
                            else
                            {
                                BridgeToolCallRequest request = null;
                                try
                                {
                                    request = JsonUtility.FromJson<BridgeToolCallRequest>(line);
                                }
                                catch (Exception ex)
                                {
                                    response = BridgeResponses.Error("Invalid JSON: " + ex.Message, "invalid_json", null);
                                    writer.WriteLine(JsonUtility.ToJson(response));
                                    continue;
                                }

                                if (request == null || string.IsNullOrEmpty(request.name))
                                {
                                    response = BridgeResponses.Error("Missing request.name.", "invalid_request", null);
                                }
                                else
                                {
                                    response = ExecuteOnMainThreadWithTimeout(request, MainThreadTimeoutMs);
                                }
                            }

                            writer.WriteLine(JsonUtility.ToJson(response));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        Debug.LogWarning("[Unity MCP Bridge] Pipe loop failed: " + ex.Message);
                    }

                    Thread.Sleep(200);
                }
            }
        }

        private static void FlushMainThreadActions()
        {
            while (MainThreadActions.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Unity MCP Bridge] Main-thread action failed: " + ex.Message);
                }
            }
        }

        private static BridgeToolCallResponse ExecuteOnMainThreadWithTimeout(BridgeToolCallRequest request, int timeoutMs)
        {
            if (string.Equals(request.name, "unity.run_tests", StringComparison.Ordinal) ||
                string.Equals(request.name, "unity.playmode_start", StringComparison.Ordinal) ||
                string.Equals(request.name, "unity.playmode_stop", StringComparison.Ordinal))
            {
                try
                {
                    return ExecuteTool(request);
                }
                catch (Exception ex)
                {
                    return BridgeResponses.Error("Tool execution failed: " + ex.Message, "tool_exception", request.name);
                }
            }

            BridgeToolCallResponse response = null;
            Exception error = null;

            using (var done = new ManualResetEvent(false))
            {
                MainThreadActions.Enqueue(() =>
                {
                    try
                    {
                        response = ExecuteTool(request);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                if (!done.WaitOne(timeoutMs))
                {
                    return BridgeResponses.Error("Main-thread execution timed out.", "tool_timeout", request.name);
                }
            }

            if (error != null)
            {
                return BridgeResponses.Error("Tool execution failed: " + error.Message, "tool_exception", request.name);
            }

            return response ?? BridgeResponses.Error("Tool execution produced no response.", "tool_no_response", request.name);
        }

        private static BridgeToolCallResponse ExecuteTool(BridgeToolCallRequest request)
        {
            return BridgeToolDispatcher.Dispatch(request, LogStore, BridgeVersion, TryInvokeOnMainThread);
        }

        private static void OnUnityLogReceived(string condition, string stackTrace, LogType type)
        {
            LogStore.Add(condition, stackTrace, type);
        }

        private static string ResolveHttpPrefix()
        {
            string envUrl = Environment.GetEnvironmentVariable("UNITY_MCP_BRIDGE_HTTP_URL");
            if (string.IsNullOrEmpty(envUrl))
            {
                return DefaultHttpPrefix;
            }

            if (!envUrl.EndsWith("/", StringComparison.Ordinal))
            {
                envUrl += "/";
            }

            return envUrl;
        }

        private static string ResolvePipeName()
        {
            string envName = Environment.GetEnvironmentVariable("UNITY_MCP_BRIDGE_PIPE_NAME");
            return string.IsNullOrEmpty(envName) ? DefaultPipeName : envName;
        }

        private static bool TryInvokeOnMainThread(Action action, int timeoutMs, out string error)
        {
            error = null;
            if (action == null)
            {
                error = "Main-thread action is null.";
                return false;
            }

            Exception actionError = null;
            using (var done = new ManualResetEvent(false))
            {
                MainThreadActions.Enqueue(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        actionError = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                if (!done.WaitOne(timeoutMs))
                {
                    error = "Main-thread action timed out.";
                    return false;
                }
            }

            if (actionError != null)
            {
                error = actionError.Message;
                return false;
            }

            return true;
        }
    }
}
#endif


