using UnityEditor;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    [InitializeOnLoad]
    internal static class UnityMcpServerCoordinator
    {
        private static bool autoStartChecked;

        static UnityMcpServerCoordinator()
        {
            EditorApplication.update += TryAutoStartServer;
        }

        internal static bool StartServer()
        {
            UnityMcpHostSettings settings = UnityMcpHostSettings.GetOrCreate();
            UnityMcpHostSupervisor.ApplyCurrentProcessEnvironment(settings);

            bool bridgeStartedNow = false;
            if (!UnityMcpBridgeServer.IsRunning)
            {
                UnityMcpBridgeServer.Start();
                bridgeStartedNow = true;
            }

            if (UnityMcpHostSupervisor.StartHostInternal(settings, runStartupProbe: true))
            {
                return true;
            }

            if (bridgeStartedNow && UnityMcpBridgeServer.IsRunning)
            {
                UnityMcpBridgeServer.Stop();
            }

            return false;
        }

        internal static void StopServer(string reason)
        {
            UnityMcpHostSupervisor.StopHostProcess(reason);
            UnityMcpBridgeServer.Stop();
        }

        private static void TryAutoStartServer()
        {
            if (autoStartChecked)
            {
                EditorApplication.update -= TryAutoStartServer;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            autoStartChecked = true;
            EditorApplication.update -= TryAutoStartServer;

            UnityMcpHostSettings settings = UnityMcpHostSettings.GetOrCreate();
            if (settings.AutoStartHostOnLoad)
            {
                StartServer();
            }
        }
    }
}
