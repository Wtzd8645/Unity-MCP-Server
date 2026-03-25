using UnityEditor;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class RuntimeReadToolHandlers
    {
        public static ControlToolCallResponse HandlePlaymodeStatus()
        {
            var payload = new PlaymodeStatusStructuredContent
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isChangingPlaymode = EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying,
            };

            return ControlResponses.Success("unity_runtime_get_playmode_status completed.", payload);
        }
    }
}
