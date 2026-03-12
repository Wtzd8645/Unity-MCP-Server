using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Control.Editor
{
    [InitializeOnLoad]
    public static class UnityMcpControlServer
    {
        private const string ControlVersion = "0.1.0";
        private const string DefaultHttpPrefix = "http://127.0.0.1:38100/";
        private const string DefaultPipeName = "unity-mcp-control";
        private const int MainThreadTimeoutMs = 5000;
        private const int MaxLogBufferSize = 5000;

        private static readonly ConcurrentQueue<Action> MainThreadActions = new ConcurrentQueue<Action>();
        private static readonly UnityControlLogStore LogStore = new UnityControlLogStore(MaxLogBufferSize);

        private static string httpPrefix = DefaultHttpPrefix;
        private static string pipeName = DefaultPipeName;

        private static HttpListener httpListener;
        private static Thread httpThread;
        private static Thread pipeThread;
        private static bool autoStartChecked;

        public static bool IsRunning { get; private set; }

        static UnityMcpControlServer()
        {
            EditorApplication.update += FlushMainThreadActions;
            EditorApplication.update += TryAutoStartControl;
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += StopInternal;
            Application.logMessageReceivedThreaded += OnUnityLogReceived;
            IsRunning = false;
        }

        private static void TryAutoStartControl()
        {
            if (autoStartChecked)
            {
                EditorApplication.update -= TryAutoStartControl;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            autoStartChecked = true;
            EditorApplication.update -= TryAutoStartControl;

            UnityMcpGatewaySettings settings = UnityMcpGatewaySettings.Instance;
            if (settings.AutoStartGatewayOnLoad)
            {
                Start();
            }
        }

        public static void Start()
        {
            if (IsRunning)
            {
                Debug.Log("[UnityMcpControl] Already running.");
                return;
            }

            ControlTransportKind transport = ResolveTransport();
            httpPrefix = ResolveHttpPrefix();
            pipeName = ResolvePipeName();
            IsRunning = true;

            if (transport == ControlTransportKind.Pipe)
            {
                StartPipeServer();
                Debug.Log($"[UnityMcpControl] Started. Pipe: {pipeName}");
            }
            else
            {
                StartHttpServer();
                Debug.Log($"[UnityMcpControl] Started. HTTP: {httpPrefix}");
            }
        }

        public static void Stop()
        {
            if (IsRunning)
            {
                StopInternal();
                Debug.Log("[UnityMcpControl] Stopped.");
            }
        }

        private static void StopInternal()
        {
            IsRunning = false;
            StopHttpServer();
            StopPipeServer();
            // Both stop methods are safe to call regardless of which was started.
        }

        private static void OnEditorQuitting()
        {
            StopInternal();
        }

        private static void StartHttpServer()
        {
            try
            {
                httpListener = new HttpListener();
                httpListener.Prefixes.Add(httpPrefix);
                httpListener.Start();

                httpThread = new Thread(HttpServerLoop)
                {
                    IsBackground = true,
                    Name = "UnityMcpControl.Http",
                };
                httpThread.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError("[UnityMcpControl] HTTP start failed: " + ex.Message);
            }
        }

        private static void StopHttpServer()
        {
            try
            {
                if (httpListener != null)
                {
                    httpListener.Stop();
                    httpListener.Close();
                    httpListener = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityMcpControl] HTTP stop failed: " + ex.Message);
            }
        }

        private static void HttpServerLoop()
        {
            while (IsRunning && httpListener != null && httpListener.IsListening)
            {
                HttpListenerContext context = null;
                try
                {
                    context = httpListener.GetContext();
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
                    if (IsRunning)
                    {
                        Debug.LogWarning("[UnityMcpControl] HTTP accept failed: " + ex.Message);
                    }

                    continue;
                }

                try
                {
                    HandleHttpContext(context);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[UnityMcpControl] HTTP request failed: " + ex.Message);
                    var errorResponse = ControlResponses.Error("Internal control error.", "internal_error", null);
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
                var routeError = ControlResponses.Error("Route not found.", "route_not_found", null);
                TryWriteHttpResponse(context, 404, JsonUtility.ToJson(routeError));
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            ControlToolCallRequest request;
            try
            {
                request = JsonUtility.FromJson<ControlToolCallRequest>(body);
            }
            catch (Exception ex)
            {
                var jsonError = ControlResponses.Error("Invalid JSON: " + ex.Message, "invalid_json", null);
                TryWriteHttpResponse(context, 400, JsonUtility.ToJson(jsonError));
                return;
            }

            if (request == null || string.IsNullOrEmpty(request.name))
            {
                var requestError = ControlResponses.Error("Missing request.name.", "invalid_request", null);
                TryWriteHttpResponse(context, 400, JsonUtility.ToJson(requestError));
                return;
            }

            ControlToolCallResponse response = ExecuteOnMainThreadWithTimeout(request, MainThreadTimeoutMs);
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
                Debug.LogWarning("[UnityMcpControl] HTTP response write failed: " + ex.Message);
            }
        }

        private static void StartPipeServer()
        {
            pipeThread = new Thread(PipeServerLoop)
            {
                IsBackground = true,
                Name = "UnityMcpControl.Pipe",
            };
            pipeThread.Start();
        }

        private static void StopPipeServer()
        {
            try
            {
                using var breaker = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                breaker.Connect(100);
            }
            catch
            {
                // Best effort: only used to unblock WaitForConnection.
            }
        }

        private static void PipeServerLoop()
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityMcpControl] Pipe server create failed: " + ex.Message);
                return;
            }

            try
            {
                while (IsRunning)
                {
                    try
                    {
                        server.WaitForConnection();
                        if (!IsRunning)
                        {
                            break;
                        }

                        ControlToolCallResponse response;
                        using (var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true))
                        using (var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true))
                        {
                            writer.AutoFlush = true;
                            string line = reader.ReadLine();
                            if (string.IsNullOrEmpty(line))
                            {
                                response = ControlResponses.Error("Empty pipe request.", "invalid_request", null);
                            }
                            else
                            {
                                ControlToolCallRequest request = null;
                                try
                                {
                                    request = JsonUtility.FromJson<ControlToolCallRequest>(line);
                                }
                                catch (Exception ex)
                                {
                                    response = ControlResponses.Error("Invalid JSON: " + ex.Message, "invalid_json", null);
                                    writer.WriteLine(JsonUtility.ToJson(response));
                                    server.Disconnect();
                                    continue;
                                }

                                if (request == null || string.IsNullOrEmpty(request.name))
                                {
                                    response = ControlResponses.Error("Missing request.name.", "invalid_request", null);
                                }
                                else
                                {
                                    response = ExecuteOnMainThreadWithTimeout(request, MainThreadTimeoutMs);
                                }
                            }

                            writer.WriteLine(JsonUtility.ToJson(response));
                        }

                        server.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        if (IsRunning)
                        {
                            Debug.LogWarning("[UnityMcpControl] Pipe loop failed: " + ex.Message);
                        }

                        if (server.IsConnected)
                        {
                            try { server.Disconnect(); } catch { }
                        }

                        Thread.Sleep(200);
                    }
                }
            }
            finally
            {
                server.Dispose();
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
                    Debug.LogWarning($"[UnityMcpControl] Main-thread action failed: {ex.Message}");
                }
            }
        }

        private static ControlToolCallResponse ExecuteOnMainThreadWithTimeout(ControlToolCallRequest request, int timeoutMs)
        {
            if (string.Equals(request.name, "unity_run_tests", StringComparison.Ordinal) ||
                string.Equals(request.name, "unity_playmode_start", StringComparison.Ordinal) ||
                string.Equals(request.name, "unity_playmode_stop", StringComparison.Ordinal))
            {
                try
                {
                    return ExecuteTool(request);
                }
                catch (Exception ex)
                {
                    return ControlResponses.Error("Tool execution failed: " + ex.Message, "tool_exception", request.name);
                }
            }

            ControlToolCallResponse response = null;
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
                    return ControlResponses.Error("Main-thread execution timed out.", "tool_timeout", request.name);
                }
            }

            if (error != null)
            {
                return ControlResponses.Error("Tool execution failed: " + error.Message, "tool_exception", request.name);
            }

            return response ?? ControlResponses.Error("Tool execution produced no response.", "tool_no_response", request.name);
        }

        private static ControlToolCallResponse ExecuteTool(ControlToolCallRequest request)
        {
            return ControlToolDispatcher.Dispatch(request, LogStore, ControlVersion, TryInvokeOnMainThread);
        }

        private static void OnUnityLogReceived(string condition, string stackTrace, LogType type)
        {
            LogStore.Add(condition, stackTrace, type);
        }

        private static ControlTransportKind ResolveTransport()
        {
            string transport = Environment.GetEnvironmentVariable("UNITY_MCP_CONTROL_TRANSPORT");
            return string.IsNullOrWhiteSpace(transport)
                ? UnityMcpGatewaySettings.Instance.ControlTransport
                : string.Equals(transport, "pipe", StringComparison.OrdinalIgnoreCase) ? ControlTransportKind.Pipe : ControlTransportKind.Http;
        }

        private static string ResolveHttpPrefix()
        {
            string envUrl = Environment.GetEnvironmentVariable("UNITY_MCP_CONTROL_HTTP_URL");
            if (string.IsNullOrWhiteSpace(envUrl))
            {
                envUrl = UnityMcpGatewaySettings.Instance.ControlHttpUrl;
            }

            if (string.IsNullOrWhiteSpace(envUrl))
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
            string envName = Environment.GetEnvironmentVariable("UNITY_MCP_CONTROL_PIPE_NAME");
            if (string.IsNullOrWhiteSpace(envName))
            {
                envName = UnityMcpGatewaySettings.Instance.ControlPipeName;
            }

            return string.IsNullOrWhiteSpace(envName) ? DefaultPipeName : envName;
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

