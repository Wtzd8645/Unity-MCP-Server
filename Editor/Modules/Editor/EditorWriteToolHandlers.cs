using System;
using System.Reflection;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class EditorWriteToolHandlers
    {
        public static ControlToolCallResponse HandleClearConsole(UnityControlLogStore logStore)
        {
            logStore.Clear();

            bool clearedEditorConsole = TryClearEditorConsole();
            var payload = new ClearConsoleResult
            {
                cleared = true,
                clearedEditorConsole = clearedEditorConsole,
            };

            return ControlResponses.Success("unity_editor_clear_console completed.", payload);
        }

        private static bool TryClearEditorConsole()
        {
            try
            {
                Type logEntries = Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
                if (logEntries == null)
                {
                    logEntries = Type.GetType("UnityEditorInternal.LogEntries, UnityEditor.dll");
                }

                if (logEntries == null)
                {
                    return false;
                }

                MethodInfo clear = logEntries.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (clear == null)
                {
                    return false;
                }

                clear.Invoke(null, null);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
