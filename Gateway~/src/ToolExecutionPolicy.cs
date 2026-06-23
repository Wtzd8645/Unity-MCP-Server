using System;
using System.Collections.Generic;

namespace Blanketmen.UnityMcp.Gateway
{
    public enum ToolRecoveryStrategy
    {
        NoRecovery = 0,
        ReadOnlyReplayable = 1,
        ConvergentState = 2,
        IdempotentMutation = 3,
        StatefulLongRunning = 4,
        NonReplayableMutation = 5,
    }

    public sealed record ToolExecutionPolicy(
        ToolRecoveryStrategy Strategy,
        bool Durable,
        bool CanReplayAfterUnknown,
        bool QueryRequestLedger);

    public static class ToolExecutionPolicyRegistry
    {
        private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
        {
            "unity_project_ping",
            "unity_project_get_info",
            "unity_project_get_build_settings",
            "unity_project_list_build_scenes",
            "unity_project_get_player_settings",
            "unity_project_get_project_settings",
            "unity_editor_get_console_logs",
            "unity_editor_get_selection",
            "unity_runtime_get_playmode_status",
            "unity_scene_list",
            "unity_scene_list_loaded",
            "unity_scene_get_active",
            "unity_gameobject_find",
            "unity_gameobject_get",
            "unity_gameobject_list_components",
            "unity_gameobject_get_component_fields",
            "unity_gameobject_get_component_fields_batch",
            "unity_prefab_asset_get",
            "unity_prefab_instance_get",
            "unity_prefab_instance_get_overrides",
            "unity_prefab_asset_find_gameobjects",
            "unity_prefab_asset_get_gameobject",
            "unity_prefab_asset_get_component_fields",
            "unity_asset_find",
            "unity_asset_get",
            "unity_asset_get_references",
        };

        private static readonly HashSet<string> ConvergentTools = new(StringComparer.Ordinal)
        {
            "unity_project_switch_build_target",
            "unity_editor_set_selection",
            "unity_editor_frame_selection",
            "unity_editor_clear_console",
            "unity_runtime_start_playmode",
            "unity_runtime_stop_playmode",
        };

        private static readonly HashSet<string> IdempotentMutationTools = new(StringComparer.Ordinal)
        {
            "unity_asset_reimport",
            "unity_asset_import",
            "unity_asset_set_labels",
        };

        public static ToolExecutionPolicy Resolve(string toolName)
        {
            if (string.Equals(toolName, "unity_project_run_tests", StringComparison.Ordinal))
            {
                return new ToolExecutionPolicy(
                    ToolRecoveryStrategy.StatefulLongRunning,
                    Durable: true,
                    CanReplayAfterUnknown: false,
                    QueryRequestLedger: true);
            }

            if (ReadOnlyTools.Contains(toolName))
            {
                return new ToolExecutionPolicy(
                    ToolRecoveryStrategy.ReadOnlyReplayable,
                    Durable: false,
                    CanReplayAfterUnknown: true,
                    QueryRequestLedger: false);
            }

            if (ConvergentTools.Contains(toolName))
            {
                return new ToolExecutionPolicy(
                    ToolRecoveryStrategy.ConvergentState,
                    Durable: true,
                    CanReplayAfterUnknown: true,
                    QueryRequestLedger: true);
            }

            if (IdempotentMutationTools.Contains(toolName))
            {
                return new ToolExecutionPolicy(
                    ToolRecoveryStrategy.IdempotentMutation,
                    Durable: true,
                    CanReplayAfterUnknown: true,
                    QueryRequestLedger: true);
            }

            return new ToolExecutionPolicy(
                ToolRecoveryStrategy.NonReplayableMutation,
                Durable: true,
                CanReplayAfterUnknown: false,
                QueryRequestLedger: true);
        }
    }
}
