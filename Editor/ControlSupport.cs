using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Blanketmen.UnityMcp.Control.Editor
{
    internal static class ControlResponses
    {
        public static ControlToolCallResponse Success(string contentText, object structuredContent)
        {
            return new ControlToolCallResponse
            {
                isError = false,
                contentText = contentText,
                structuredContentJson = structuredContent == null ? null : JsonUtility.ToJson(structuredContent),
            };
        }

        public static ControlToolCallResponse Error(string message, string status, string toolName)
        {
            var payload = new ErrorStructuredContent
            {
                status = status,
                message = message,
                tool = toolName,
            };

            return new ControlToolCallResponse
            {
                isError = true,
                contentText = message,
                structuredContentJson = JsonUtility.ToJson(payload),
            };
        }

        public static ControlToolCallResponse NotImplemented(string toolName)
        {
            var payload = new NotImplementedStructuredContent
            {
                status = "not_implemented",
                tool = toolName,
            };

            return Success("Tool '" + toolName + "' is not implemented in Unity control yet.", payload);
        }
    }

    internal static class ControlJson
    {
        public static T ParseArgs<T>(string rawJson, T defaults) where T : class
        {
            if (defaults == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(rawJson))
            {
                return defaults;
            }

            try
            {
                JsonUtility.FromJsonOverwrite(rawJson, defaults);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Unity MCP Control] Failed to parse arguments JSON: " + ex.Message);
            }

            return defaults;
        }

        public static bool RawJsonContainsProperty(string rawJson, string propertyName)
        {
            if (string.IsNullOrEmpty(rawJson) || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            string needle = "\"" + propertyName + "\"";
            return rawJson.IndexOf(needle, StringComparison.Ordinal) >= 0;
        }
    }

    internal static class ControlUtil
    {
        public static int Clamp(int value, int min, int max, int defaultValue)
        {
            if (value <= 0)
            {
                return defaultValue;
            }

            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        public static long ParseLong(string value, long defaultValue)
        {
            long parsed;
            if (!long.TryParse(value, out parsed))
            {
                return defaultValue;
            }

            return parsed;
        }

        public static PaginationRange BuildPaginationRange(int total, int offset, int limit, int maxLimit)
        {
            int safeOffset = offset < 0 ? 0 : offset;
            int safeLimit = limit <= 0 ? Math.Min(total, maxLimit) : Math.Min(limit, maxLimit);
            if (safeLimit <= 0)
            {
                safeLimit = maxLimit;
            }

            if (safeOffset > total)
            {
                safeOffset = total;
            }

            return new PaginationRange
            {
                offset = safeOffset,
                limit = safeLimit,
            };
        }

        public static string GetProjectRootPath()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        public static string[] BuildSearchFolders(string[] pathPrefixes, bool includePackages)
        {
            if (pathPrefixes != null && pathPrefixes.Length > 0)
            {
                return pathPrefixes
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            if (includePackages)
            {
                return null;
            }

            return new[] { "Assets" };
        }
    }
}
