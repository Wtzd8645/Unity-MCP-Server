using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Blanketmen.UnityMcp.Editor.Control
{
    internal static class ControlRequestLedger
    {
        private static readonly object SyncRoot = new object();

        public static string NormalizeRequestId(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return Guid.NewGuid().ToString("N");
            }

            var builder = new StringBuilder(requestId.Length);
            for (int i = 0; i < requestId.Length; i++)
            {
                char ch = requestId[i];
                if ((ch >= 'a' && ch <= 'z') ||
                    (ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9') ||
                    ch == '-' ||
                    ch == '_')
                {
                    builder.Append(ch);
                }
            }

            string normalized = builder.ToString();
            return string.IsNullOrEmpty(normalized) ? Guid.NewGuid().ToString("N") : normalized;
        }

        public static bool TryGetTerminalResponse(string requestId, out ControlToolCallResponse response)
        {
            response = null;
            RequestLedgerState state;
            if (!TryLoad(requestId, out state))
            {
                return false;
            }

            if (!IsTerminal(state.requestStatus) || string.IsNullOrEmpty(state.responseJson))
            {
                return false;
            }

            try
            {
                response = JsonUtility.FromJson<ControlToolCallResponse>(state.responseJson);
                return response != null;
            }
            catch
            {
                return false;
            }
        }

        public static void MarkReceived(ControlToolCallRequest request, string controlEpoch)
        {
            if (!ShouldTrack(request))
            {
                return;
            }

            string requestId = NormalizeRequestId(request.requestId);
            lock (SyncRoot)
            {
                RequestLedgerState state = LoadOrCreate(requestId);
                if (IsTerminal(state.requestStatus))
                {
                    return;
                }

                state.requestId = requestId;
                state.toolName = request.name;
                state.argumentsJson = request.argumentsJson;
                state.deadlineUtc = request.deadlineUtc;
                state.attempt = request.attempt;
                state.controlEpoch = controlEpoch;
                state.requestStatus = string.IsNullOrEmpty(state.requestStatus) ? "received" : state.requestStatus;
                state.message = "Request received.";
                Touch(state);
                Save(state);
            }
        }

        public static void MarkStatus(ControlToolCallRequest request, string status, string message, string controlEpoch)
        {
            if (!ShouldTrack(request))
            {
                return;
            }

            lock (SyncRoot)
            {
                RequestLedgerState state = LoadOrCreate(NormalizeRequestId(request.requestId));
                if (IsTerminal(state.requestStatus))
                {
                    return;
                }

                state.requestId = NormalizeRequestId(request.requestId);
                state.toolName = request.name;
                state.argumentsJson = request.argumentsJson;
                state.deadlineUtc = request.deadlineUtc;
                state.attempt = request.attempt;
                state.controlEpoch = controlEpoch;
                state.requestStatus = status;
                state.message = message;
                Touch(state);
                Save(state);
            }
        }

        public static void MarkFinal(ControlToolCallRequest request, ControlToolCallResponse response, string controlEpoch)
        {
            if (!ShouldTrack(request))
            {
                return;
            }

            lock (SyncRoot)
            {
                RequestLedgerState state = LoadOrCreate(NormalizeRequestId(request.requestId));
                state.requestId = NormalizeRequestId(request.requestId);
                state.toolName = request.name;
                state.argumentsJson = request.argumentsJson;
                state.deadlineUtc = request.deadlineUtc;
                state.attempt = request.attempt;
                state.controlEpoch = controlEpoch;
                state.requestStatus = response != null && response.isError ? "failed" : "completed";
                state.message = response == null ? "Request produced no response." : response.contentText;
                state.responseJson = response == null ? null : JsonUtility.ToJson(response);
                state.completedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                Touch(state);
                Save(state);
            }
        }

        public static ControlRequestStateResult Query(string requestId)
        {
            requestId = NormalizeRequestId(requestId);
            RequestLedgerState state;
            if (!TryLoad(requestId, out state))
            {
                return new ControlRequestStateResult
                {
                    status = "unknown",
                    requestStatus = "unknown",
                    requestId = requestId,
                    message = "Request state not found.",
                    statePath = GetStatePath(requestId),
                };
            }

            ExpireIfNeeded(state);
            return ToResult(state);
        }

        public static void MarkOpenRequestsUnknownAfterReload(string controlEpoch)
        {
            ReconcileOpenRequests(controlEpoch, markUnknown: true);
        }

        public static void ReconcileOpenRequests(string controlEpoch)
        {
            ReconcileOpenRequests(controlEpoch, markUnknown: false);
        }

        private static void ReconcileOpenRequests(string controlEpoch, bool markUnknown)
        {
            string root = GetRoot();
            if (!Directory.Exists(root))
            {
                return;
            }

            string[] stateFiles;
            try
            {
                stateFiles = Directory.GetFiles(root, "state.json", SearchOption.AllDirectories);
            }
            catch
            {
                return;
            }

            for (int i = 0; i < stateFiles.Length; i++)
            {
                try
                {
                    RequestLedgerState state = JsonUtility.FromJson<RequestLedgerState>(File.ReadAllText(stateFiles[i]));
                    if (state == null || IsTerminal(state.requestStatus))
                    {
                        continue;
                    }

                    if (IsExpired(state))
                    {
                        state.requestStatus = "expired";
                        state.message = "Request expired before Unity Control could report a final state.";
                    }
                    else if (markUnknown)
                    {
                        state.requestStatus = "unknown_after_reload";
                        state.message = "Unity domain reloaded before this request produced a final response.";
                    }
                    else
                    {
                        continue;
                    }

                    state.controlEpoch = controlEpoch;
                    Touch(state);
                    Save(state);
                }
                catch
                {
                }
            }
        }

        private static ControlRequestStateResult ToResult(RequestLedgerState state)
        {
            return new ControlRequestStateResult
            {
                status = state.requestStatus,
                requestStatus = state.requestStatus,
                requestId = state.requestId,
                toolName = state.toolName,
                message = state.message,
                statePath = GetStatePath(state.requestId),
                responseJson = state.responseJson,
                deadlineUtc = state.deadlineUtc,
                updatedUtc = state.updatedUtc,
                controlEpoch = state.controlEpoch,
            };
        }

        private static bool ShouldTrack(ControlToolCallRequest request)
        {
            return request != null && request.durable && !string.IsNullOrEmpty(request.requestId);
        }

        private static void ExpireIfNeeded(RequestLedgerState state)
        {
            if (state == null || IsTerminal(state.requestStatus) || !IsExpired(state))
            {
                return;
            }

            state.requestStatus = "expired";
            state.message = "Request deadline expired.";
            Touch(state);
            Save(state);
        }

        private static bool IsExpired(RequestLedgerState state)
        {
            if (state == null || string.IsNullOrEmpty(state.deadlineUtc))
            {
                return false;
            }

            DateTime deadline;
            if (!DateTime.TryParse(
                    state.deadlineUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out deadline))
            {
                return false;
            }

            return DateTime.UtcNow > deadline.ToUniversalTime();
        }

        private static bool IsTerminal(string status)
        {
            return string.Equals(status, "completed", StringComparison.Ordinal) ||
                   string.Equals(status, "failed", StringComparison.Ordinal) ||
                   string.Equals(status, "unknown_after_reload", StringComparison.Ordinal) ||
                   string.Equals(status, "expired", StringComparison.Ordinal);
        }

        private static void Touch(RequestLedgerState state)
        {
            if (string.IsNullOrEmpty(state.receivedUtc))
            {
                state.receivedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            }

            state.updatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        private static bool TryLoad(string requestId, out RequestLedgerState state)
        {
            state = null;
            string path = GetStatePath(requestId);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                state = JsonUtility.FromJson<RequestLedgerState>(File.ReadAllText(path));
                if (state == null)
                {
                    return false;
                }

                state.requestId = NormalizeRequestId(state.requestId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static RequestLedgerState LoadOrCreate(string requestId)
        {
            RequestLedgerState state;
            if (TryLoad(requestId, out state))
            {
                return state;
            }

            return new RequestLedgerState
            {
                requestId = NormalizeRequestId(requestId),
                requestStatus = "received",
            };
        }

        private static void Save(RequestLedgerState state)
        {
            try
            {
                string path = GetStatePath(state.requestId);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(state, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityMcpControl] Failed to write request ledger state: " + ex.Message);
            }
        }

        private static string GetRoot()
        {
            return Path.Combine(ControlUtil.GetProjectRootPath(), "Library", "McpReports", "control-requests");
        }

        private static string GetStatePath(string requestId)
        {
            return Path.Combine(GetRoot(), NormalizeRequestId(requestId), "state.json");
        }

        [Serializable]
        private sealed class RequestLedgerState
        {
            public string requestId;
            public string toolName;
            public string argumentsJson;
            public string deadlineUtc;
            public int attempt;
            public string requestStatus;
            public string message;
            public string receivedUtc;
            public string updatedUtc;
            public string completedUtc;
            public string responseJson;
            public string controlEpoch;
        }
    }
}
