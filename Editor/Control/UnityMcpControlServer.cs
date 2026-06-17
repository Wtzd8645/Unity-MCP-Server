using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Editor.Control
{
    [InitializeOnLoad]
    public static class UnityMcpControlServer
    {
        private const string ControlVersion = "0.1.0";
        private const string DefaultHttpPrefix = "http://127.0.0.1:38110/";
        private const string DefaultPipeName = "unity-mcp-control";
        private const int MainThreadTimeoutMs = 5000;
        private const int MaxLogBufferSize = 5000;
        private const int PipeStartupTimeoutMs = 3000;

        private static readonly ConcurrentQueue<Action> MainThreadActions = new ConcurrentQueue<Action>();
        private static readonly UnityControlLogStore LogStore = new UnityControlLogStore(MaxLogBufferSize);

        private static string httpPrefix = DefaultHttpPrefix;
        private static string pipeName = DefaultPipeName;

        private static HttpListener httpListener;
        private static Thread httpThread;
        private static HttpServerState httpServerState;
        private static Thread pipeThread;
        private static PipeServerState pipeServerState;
        private static bool autoStartChecked;
        private static volatile bool isRunning;

        public static event Action StatusChanged;

        public static bool IsRunning
        {
            get { return isRunning; }
            private set
            {
                if (isRunning == value)
                {
                    return;
                }

                isRunning = value;
                PublishStatusChanged();
            }
        }

        static UnityMcpControlServer()
        {
            EditorApplication.update += FlushMainThreadActions;
            EditorApplication.update += TryAutoStartControl;
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            Application.logMessageReceivedThreaded += OnUnityLogReceived;
            UnityMcpGatewayHost.StatusChanged += PublishStatusChanged;
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
            if (settings.AutoStartControlOnLoad)
            {
                Start(settings.AutoStartGatewayWithControl);
            }
        }

        public static void Start()
        {
            Start(startGatewayWithControl: true);
        }

        public static void Start(bool startGatewayWithControl)
        {
            UnityMcpGatewaySettings settings = UnityMcpGatewaySettings.Instance;
            settings.EnsureDefaults();
            if (!TryValidateHttpTopology(settings, out string topologyError))
            {
                Debug.LogError("[UnityMcpControl] Start failed: " + topologyError);
                return;
            }

            if (IsRunning)
            {
                if (startGatewayWithControl && !UnityMcpGatewayHost.IsRunning)
                {
                    if (!UnityMcpGatewayHost.Start(settings, out string gatewayError))
                    {
                        Debug.LogError("[UnityMcpControl] Gateway start failed: " + gatewayError);
                    }
                    else
                    {
                        Debug.Log("[UnityMcpControl] Gateway started.");
                    }
                }
                else
                {
                    Debug.Log("[UnityMcpControl] Already running.");
                }
                return;
            }

            IsRunning = true;
            if (!StartControlTransport(out ControlTransportKind transport, out string startError))
            {
                IsRunning = false;
                StopHttpServer();
                StopPipeServer();
                Debug.LogError("[UnityMcpControl] Start failed: " + startError);
                return;
            }

            if (transport == ControlTransportKind.Pipe)
            {
                Debug.Log($"[UnityMcpControl] Started. Pipe: {pipeName}");
            }
            else
            {
                Debug.Log($"[UnityMcpControl] Started. HTTP: {httpPrefix}");
            }

            if (!startGatewayWithControl)
            {
                return;
            }

            if (!UnityMcpGatewayHost.Start(settings, out string gatewayStartError))
            {
                Debug.LogError("[UnityMcpControl] Gateway start failed: " + gatewayStartError);
                StopInternal(stopGateway: false);
                return;
            }

            Debug.Log("[UnityMcpControl] Gateway started.");
        }

        private static bool TryValidateHttpTopology(UnityMcpGatewaySettings settings, out string error)
        {
            error = string.Empty;
            if (settings.ControlTransport != ControlTransportKind.Http)
            {
                return true;
            }

            if (!Uri.TryCreate(settings.GatewayHttpUrl, UriKind.Absolute, out Uri gatewayUri) ||
                !Uri.TryCreate(settings.ControlHttpUrl, UriKind.Absolute, out Uri controlUri))
            {
                return true;
            }

            if (!string.Equals(
                    gatewayUri.GetLeftPart(UriPartial.Authority),
                    controlUri.GetLeftPart(UriPartial.Authority),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            error =
                "Gateway HTTP URL and Control HTTP URL cannot use the same host and port. " +
                "Use the default Named Pipe control transport or configure different HTTP ports.";
            return false;
        }

        public static void Stop()
        {
            if (IsRunning)
            {
                StopInternal(stopGateway: true);
                GatewayStatusSnapshot gatewaySnapshot = UnityMcpGatewayHost.GetStatusSnapshot();
                if (gatewaySnapshot.ManagedPid.HasValue)
                {
                    Debug.LogWarning(
                        "[UnityMcpControl] Control stopped but Gateway process is still alive (PID " +
                        gatewaySnapshot.ManagedPid.Value + ").");
                }
                else
                {
                    Debug.Log("[UnityMcpControl] Stopped.");
                }
            }
            else if (UnityMcpGatewayHost.IsRunning || UnityMcpGatewayHost.ManagedPid.HasValue)
            {
                UnityMcpGatewayHost.Stop();
                GatewayStatusSnapshot gatewaySnapshot = UnityMcpGatewayHost.GetStatusSnapshot();
                if (gatewaySnapshot.ManagedPid.HasValue)
                {
                    Debug.LogWarning(
                        "[UnityMcpControl] Gateway stop requested but process is still alive (PID " +
                        gatewaySnapshot.ManagedPid.Value + ").");
                }
                else
                {
                    Debug.Log("[UnityMcpControl] Gateway stopped.");
                }
            }
        }

        public static void RestartGateway()
        {
            if (!IsRunning)
            {
                Debug.LogWarning("[UnityMcpControl] Cannot restart Gateway while Control is stopped.");
                return;
            }

            if (!UnityMcpGatewayHost.Restart(UnityMcpGatewaySettings.Instance, out string error))
            {
                Debug.LogError("[UnityMcpControl] Gateway restart failed: " + error);
                return;
            }

            Debug.Log("[UnityMcpControl] Gateway restarted.");
        }

        private static bool StartControlTransport(out ControlTransportKind transport, out string error)
        {
            transport = ResolveTransport();
            error = string.Empty;
            httpPrefix = ResolveHttpPrefix();
            pipeName = ResolvePipeName();

            return transport == ControlTransportKind.Pipe
                ? StartPipeServer(out error)
                : StartHttpServer(out error);
        }

        private static void StopInternal(bool stopGateway)
        {
            IsRunning = false;
            StopHttpServer();
            StopPipeServer();

            if (stopGateway)
            {
                UnityMcpGatewayHost.Stop();
            }
        }

        private static void OnEditorQuitting()
        {
            StopInternal(stopGateway: true);
        }

        private static void OnBeforeAssemblyReload()
        {
            UnityMcpGatewayHost.PrepareForAssemblyReload();
            StopInternal(stopGateway: false);
        }

        private static bool StartHttpServer(out string error)
        {
            error = string.Empty;
            HttpListener listener = null;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add(httpPrefix);
                listener.Start();

                var serverState = new HttpServerState(listener);
                httpListener = listener;
                httpServerState = serverState;
                httpThread = new Thread(() => HttpServerLoop(serverState))
                {
                    IsBackground = true,
                    Name = "UnityMcpControl.Http",
                };
                httpThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                error = "HTTP start failed: " + ex.Message;
                try
                {
                    listener?.Close();
                    httpListener?.Close();
                    httpListener = null;
                    httpServerState = null;
                }
                catch
                {
                    // Best effort cleanup only.
                }

                return false;
            }
        }

        private static void StopHttpServer()
        {
            try
            {
                HttpServerState serverState = httpServerState;
                if (serverState != null)
                {
                    serverState.StopRequested = true;
                    serverState.CloseListener();
                }

                if (serverState == null && httpListener != null)
                {
                    httpListener.Stop();
                    httpListener.Close();
                }

                httpListener = null;

                Thread thread = httpThread;
                if (thread != null && thread.IsAlive)
                {
                    try
                    {
                        thread.Join(200);
                    }
                    catch
                    {
                        // Best effort only.
                    }
                }

                httpThread = null;
                httpServerState = null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityMcpControl] HTTP stop failed: " + ex.Message);
            }
        }

        private static void HttpServerLoop(HttpServerState serverState)
        {
            HttpListener listener = serverState.Listener;
            while (!serverState.StopRequested && listener.IsListening)
            {
                HttpListenerContext context = null;
                try
                {
                    context = listener.GetContext();
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
                    if (!serverState.StopRequested)
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
                if (!(ex is IOException) && !(ex is ObjectDisposedException))
                {
                    Debug.LogWarning("[UnityMcpControl] HTTP response write failed: " + ex.Message);
                }
            }
        }

        private static bool StartPipeServer(out string error)
        {
            error = string.Empty;
            var startupProbe = new PipeStartupProbe();
            var serverState = new PipeServerState(pipeName);
            try
            {
                pipeServerState = serverState;
                pipeThread = new Thread(() => PipeServerLoop(serverState, startupProbe))
                {
                    IsBackground = true,
                    Name = "UnityMcpControl.Pipe",
                };
                pipeThread.Start();
            }
            catch (Exception ex)
            {
                pipeServerState = null;
                pipeThread = null;
                error = "Pipe start failed: " + ex.Message;
                return false;
            }

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(PipeStartupTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (startupProbe.Completed)
                {
                    if (startupProbe.Succeeded)
                    {
                        return true;
                    }

                    error = string.IsNullOrWhiteSpace(startupProbe.Error)
                        ? "Pipe start failed."
                        : startupProbe.Error;
                    return false;
                }

                Thread.Sleep(10);
            }

            error = $"Pipe start timed out after {PipeStartupTimeoutMs}ms.";
            return false;
        }

        private static void StopPipeServer()
        {
            PipeServerState serverState = pipeServerState;
            if (serverState != null)
            {
                serverState.StopRequested = true;
            }

            try
            {
                string breakerPipeName = serverState != null ? serverState.PipeName : pipeName;
                using var breaker = new NamedPipeClientStream(".", breakerPipeName, PipeDirection.Out);
                breaker.Connect(100);
            }
            catch
            {
                // Best effort: only used to unblock WaitForConnection.
            }

            serverState?.DisposeActiveServer();

            Thread thread = pipeThread;
            if (thread != null && thread.IsAlive)
            {
                try
                {
                    thread.Join(200);
                }
                catch
                {
                    // Best effort only.
                }
            }

            pipeThread = null;
            pipeServerState = null;
        }

        private static void PipeServerLoop(PipeServerState serverState, PipeStartupProbe startupProbe)
        {
            while (!serverState.StopRequested)
            {
                NamedPipeServerStream server;
                try
                {
                    server = new NamedPipeServerStream(serverState.PipeName, PipeDirection.InOut, 1);
                    if (!serverState.TrySetActiveServer(server))
                    {
                        server.Dispose();
                        break;
                    }

                    if (!startupProbe.Completed)
                    {
                        startupProbe.Succeeded = true;
                        startupProbe.Error = string.Empty;
                        startupProbe.Completed = true;
                    }
                }
                catch (Exception ex)
                {
                    if (!startupProbe.Completed)
                    {
                        startupProbe.Succeeded = false;
                        startupProbe.Error = "Pipe server create failed: " + ex.Message;
                        startupProbe.Completed = true;
                        Debug.LogWarning("[UnityMcpControl] " + startupProbe.Error);
                        return;
                    }

                    if (!serverState.StopRequested)
                    {
                        Debug.LogWarning("[UnityMcpControl] Pipe server recreate failed: " + ex.Message);
                        Thread.Sleep(200);
                    }

                    continue;
                }

                try
                {
                    server.WaitForConnection();
                    if (serverState.StopRequested)
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
                }
                catch (IOException)
                {
                    // Clients may disconnect at any point. Recreate the server stream for the next request.
                }
                catch (Exception ex)
                {
                    if (!serverState.StopRequested)
                    {
                        Debug.LogWarning("[UnityMcpControl] Pipe loop failed: " + ex.Message);
                    }
                }
                finally
                {
                    serverState.ClearActiveServer(server);
                    try { if (server.IsConnected) server.Disconnect(); } catch { }
                    try { server.Dispose(); } catch { }
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
                    Debug.LogWarning($"[UnityMcpControl] Main-thread action failed: {ex.Message}");
                }
            }
        }

        private static ControlToolCallResponse ExecuteOnMainThreadWithTimeout(ControlToolCallRequest request, int timeoutMs)
        {
            if (string.Equals(request.name, "unity_project_run_tests", StringComparison.Ordinal) ||
                string.Equals(request.name, "unity_project_switch_build_target", StringComparison.Ordinal) ||
                string.Equals(request.name, "unity_project_build_player", StringComparison.Ordinal) ||
                string.Equals(request.name, "unity_runtime_start_playmode", StringComparison.Ordinal) ||
                string.Equals(request.name, "unity_runtime_stop_playmode", StringComparison.Ordinal))
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
            int shouldRun = 1;

            using (var done = new ManualResetEvent(false))
            {
                MainThreadActions.Enqueue(() =>
                {
                    if (Interlocked.CompareExchange(ref shouldRun, 1, 1) == 0)
                    {
                        return;
                    }

                    if (!IsRunning)
                    {
                        error = new InvalidOperationException("Control stopped before main-thread execution.");
                        done.Set();
                        return;
                    }

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
                    Interlocked.Exchange(ref shouldRun, 0);
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

        private static void PublishStatusChanged()
        {
            StatusChanged?.Invoke();
        }

        private sealed class PipeStartupProbe
        {
            public volatile bool Completed;
            public bool Succeeded;
            public string Error = string.Empty;
        }

        private sealed class HttpServerState
        {
            public readonly HttpListener Listener;
            public volatile bool StopRequested;

            public HttpServerState(HttpListener listener)
            {
                Listener = listener;
            }

            public void CloseListener()
            {
                try { Listener.Stop(); } catch { }
                try { Listener.Close(); } catch { }
            }
        }

        private sealed class PipeServerState
        {
            private readonly object serverLock = new object();
            private NamedPipeServerStream activeServer;

            public readonly string PipeName;
            public volatile bool StopRequested;

            public PipeServerState(string pipeName)
            {
                PipeName = pipeName;
            }

            public bool TrySetActiveServer(NamedPipeServerStream server)
            {
                lock (serverLock)
                {
                    if (StopRequested)
                    {
                        return false;
                    }

                    activeServer = server;
                    return true;
                }
            }

            public void ClearActiveServer(NamedPipeServerStream server)
            {
                lock (serverLock)
                {
                    if (ReferenceEquals(activeServer, server))
                    {
                        activeServer = null;
                    }
                }
            }

            public void DisposeActiveServer()
            {
                lock (serverLock)
                {
                    try { activeServer?.Dispose(); } catch { }
                    activeServer = null;
                }
            }
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
            int shouldRun = 1;
            using (var done = new ManualResetEvent(false))
            {
                MainThreadActions.Enqueue(() =>
                {
                    if (Interlocked.CompareExchange(ref shouldRun, 1, 1) == 0)
                    {
                        return;
                    }

                    if (!IsRunning)
                    {
                        actionError = new InvalidOperationException("Control stopped before main-thread execution.");
                        done.Set();
                        return;
                    }

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
                    Interlocked.Exchange(ref shouldRun, 0);
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
