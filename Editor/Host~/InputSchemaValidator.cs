using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Blanketmen.UnityMcpServer.Host;

internal static class InputSchemaValidator
{
    private const int MaxDepth = 128;

    public static bool TryValidate(JsonNode schema, JsonNode? value, out string error)
    {
        if (schema is null)
        {
            error = "Schema is null.";
            return false;
        }

        return ValidateNode(schema, value, schema, "$", 0, out error);
    }

    private static bool ValidateNode(
        JsonNode schemaNode,
        JsonNode? value,
        JsonNode rootSchema,
        string path,
        int depth,
        out string error)
    {
        error = string.Empty;

        if (depth > MaxDepth)
        {
            error = $"Validation depth exceeded at '{path}'.";
            return false;
        }

        if (schemaNode is JsonValue schemaValue &&
            schemaValue.TryGetValue<bool>(out bool boolSchema))
        {
            if (!boolSchema)
            {
                error = $"'{path}' is not allowed by schema.";
                return false;
            }

            return true;
        }

        if (schemaNode is not JsonObject schema)
        {
            return true;
        }

        if (schema.TryGetPropertyValue("$ref", out JsonNode? refNode) &&
            refNode is JsonValue refValue &&
            refValue.TryGetValue<string>(out string? refPath) &&
            !string.IsNullOrWhiteSpace(refPath))
        {
            if (!TryResolveRef(rootSchema, refPath, out JsonNode? resolvedSchema))
            {
                error = $"Unable to resolve schema reference '{refPath}' at '{path}'.";
                return false;
            }

            if (resolvedSchema is null)
            {
                error = $"Resolved schema reference '{refPath}' is null at '{path}'.";
                return false;
            }

            if (!ValidateNode(resolvedSchema, value, rootSchema, path, depth + 1, out error))
            {
                return false;
            }
        }

        if (schema.TryGetPropertyValue("allOf", out JsonNode? allOfNode) &&
            allOfNode is JsonArray allOfSchemas)
        {
            for (int i = 0; i < allOfSchemas.Count; i++)
            {
                JsonNode? allOfSchema = allOfSchemas[i];
                if (allOfSchema is null)
                {
                    continue;
                }

                if (!ValidateNode(allOfSchema, value, rootSchema, path, depth + 1, out error))
                {
                    return false;
                }
            }
        }

        if (schema.TryGetPropertyValue("oneOf", out JsonNode? oneOfNode) &&
            oneOfNode is JsonArray oneOfSchemas)
        {
            int passed = 0;
            string? firstSubError = null;

            for (int i = 0; i < oneOfSchemas.Count; i++)
            {
                JsonNode? oneOfSchema = oneOfSchemas[i];
                if (oneOfSchema is null)
                {
                    continue;
                }

                if (ValidateNode(oneOfSchema, value, rootSchema, path, depth + 1, out string subError))
                {
                    passed++;
                }
                else if (firstSubError is null)
                {
                    firstSubError = subError;
                }
            }

            if (passed != 1)
            {
                if (passed == 0)
                {
                    error = $"'{path}' must match exactly one schema in oneOf. {firstSubError}";
                }
                else
                {
                    error = $"'{path}' matches multiple schemas in oneOf.";
                }

                return false;
            }
        }

        if (schema.TryGetPropertyValue("const", out JsonNode? constNode) &&
            !JsonEquals(value, constNode))
        {
            error = $"'{path}' must equal const value.";
            return false;
        }

        if (schema.TryGetPropertyValue("enum", out JsonNode? enumNode) &&
            enumNode is JsonArray enumValues)
        {
            bool matched = false;
            for (int i = 0; i < enumValues.Count; i++)
            {
                if (JsonEquals(value, enumValues[i]))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                error = $"'{path}' is not an allowed enum value.";
                return false;
            }
        }

        if (schema.TryGetPropertyValue("type", out JsonNode? typeNode) &&
            typeNode is not null &&
            !ValidateType(path, value, typeNode, out error))
        {
            return false;
        }

        if (value is JsonObject valueObject)
        {
            if (!ValidateObject(schema, valueObject, rootSchema, path, depth + 1, out error))
            {
                return false;
            }
        }

        if (value is JsonArray valueArray)
        {
            if (!ValidateArray(schema, valueArray, rootSchema, path, depth + 1, out error))
            {
                return false;
            }
        }

        if (TryGetString(value, out string? textValue))
        {
            if (!ValidateString(schema, path, textValue ?? string.Empty, out error))
            {
                return false;
            }
        }

        if (TryGetNumber(value, out double numberValue))
        {
            if (!ValidateNumber(schema, path, numberValue, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateObject(
        JsonObject schema,
        JsonObject value,
        JsonNode rootSchema,
        string path,
        int depth,
        out string error)
    {
        error = string.Empty;

        JsonObject? properties = null;
        if (schema.TryGetPropertyValue("properties", out JsonNode? propertiesNode) &&
            propertiesNode is JsonObject propertiesObject)
        {
            properties = propertiesObject;
        }

        if (schema.TryGetPropertyValue("required", out JsonNode? requiredNode) &&
            requiredNode is JsonArray requiredArray)
        {
            for (int i = 0; i < requiredArray.Count; i++)
            {
                if (requiredArray[i] is not JsonValue requiredValue ||
                    !requiredValue.TryGetValue<string>(out string? requiredName) ||
                    string.IsNullOrWhiteSpace(requiredName))
                {
                    continue;
                }

                if (!value.ContainsKey(requiredName))
                {
                    error = $"Missing required property '{path}.{requiredName}'.";
                    return false;
                }
            }
        }

        foreach (KeyValuePair<string, JsonNode?> entry in value)
        {
            string propertyName = entry.Key;
            JsonNode? propertyValue = entry.Value;
            string propertyPath = path + "." + propertyName;

            if (properties is not null && properties.TryGetPropertyValue(propertyName, out JsonNode? propertySchema) && propertySchema is not null)
            {
                if (!ValidateNode(propertySchema, propertyValue, rootSchema, propertyPath, depth + 1, out error))
                {
                    return false;
                }

                continue;
            }

            if (schema.TryGetPropertyValue("additionalProperties", out JsonNode? additionalNode))
            {
                if (additionalNode is JsonValue additionalValue &&
                    additionalValue.TryGetValue<bool>(out bool allowAdditional) &&
                    !allowAdditional)
                {
                    error = $"Additional property '{propertyPath}' is not allowed.";
                    return false;
                }

                if (additionalNode is JsonObject additionalSchema)
                {
                    if (!ValidateNode(additionalSchema, propertyValue, rootSchema, propertyPath, depth + 1, out error))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool ValidateArray(
        JsonObject schema,
        JsonArray value,
        JsonNode rootSchema,
        string path,
        int depth,
        out string error)
    {
        error = string.Empty;

        if (TryGetInt(schema, "minItems", out int minItems) && value.Count < minItems)
        {
            error = $"'{path}' must contain at least {minItems} items.";
            return false;
        }

        if (TryGetInt(schema, "maxItems", out int maxItems) && value.Count > maxItems)
        {
            error = $"'{path}' must contain at most {maxItems} items.";
            return false;
        }

        if (schema.TryGetPropertyValue("items", out JsonNode? itemsSchema) && itemsSchema is not null)
        {
            for (int i = 0; i < value.Count; i++)
            {
                if (!ValidateNode(itemsSchema, value[i], rootSchema, $"{path}[{i}]", depth + 1, out error))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateString(JsonObject schema, string path, string value, out string error)
    {
        error = string.Empty;

        if (TryGetInt(schema, "minLength", out int minLength) && value.Length < minLength)
        {
            error = $"'{path}' must be at least {minLength} characters.";
            return false;
        }

        if (TryGetInt(schema, "maxLength", out int maxLength) && value.Length > maxLength)
        {
            error = $"'{path}' must be at most {maxLength} characters.";
            return false;
        }

        if (schema.TryGetPropertyValue("pattern", out JsonNode? patternNode) &&
            patternNode is JsonValue patternValue &&
            patternValue.TryGetValue<string>(out string? pattern) &&
            !string.IsNullOrWhiteSpace(pattern))
        {
            if (!Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant))
            {
                error = $"'{path}' does not match pattern '{pattern}'.";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateNumber(JsonObject schema, string path, double value, out string error)
    {
        error = string.Empty;

        if (TryGetDouble(schema, "minimum", out double minimum) && value < minimum)
        {
            error = $"'{path}' must be >= {minimum.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        if (TryGetDouble(schema, "maximum", out double maximum) && value > maximum)
        {
            error = $"'{path}' must be <= {maximum.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        if (TryGetDouble(schema, "exclusiveMinimum", out double exclusiveMinimum) && value <= exclusiveMinimum)
        {
            error = $"'{path}' must be > {exclusiveMinimum.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        if (TryGetDouble(schema, "exclusiveMaximum", out double exclusiveMaximum) && value >= exclusiveMaximum)
        {
            error = $"'{path}' must be < {exclusiveMaximum.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        return true;
    }

    private static bool ValidateType(string path, JsonNode? value, JsonNode typeNode, out string error)
    {
        error = string.Empty;

        if (typeNode is JsonValue typeValue &&
            typeValue.TryGetValue<string>(out string? singleType) &&
            !string.IsNullOrWhiteSpace(singleType))
        {
            if (!IsTypeMatch(singleType, value))
            {
                error = $"'{path}' must be of type '{singleType}'.";
                return false;
            }

            return true;
        }

        if (typeNode is JsonArray typeArray)
        {
            for (int i = 0; i < typeArray.Count; i++)
            {
                if (typeArray[i] is JsonValue candidateValue &&
                    candidateValue.TryGetValue<string>(out string? candidateType) &&
                    !string.IsNullOrWhiteSpace(candidateType) &&
                    IsTypeMatch(candidateType, value))
                {
                    return true;
                }
            }

            error = $"'{path}' does not match any allowed type.";
            return false;
        }

        return true;
    }

    private static bool IsTypeMatch(string schemaType, JsonNode? value)
    {
        switch (schemaType)
        {
            case "null":
                return value is null;
            case "object":
                return value is JsonObject;
            case "array":
                return value is JsonArray;
            case "string":
                return TryGetString(value, out _);
            case "boolean":
                return TryGetBoolean(value, out _);
            case "number":
                return TryGetNumber(value, out _);
            case "integer":
                if (!TryGetNumber(value, out double numericValue))
                {
                    return false;
                }

                return Math.Abs(numericValue % 1) < double.Epsilon;
            default:
                return false;
        }
    }

    private static bool TryResolveRef(JsonNode rootSchema, string refPath, out JsonNode? resolved)
    {
        resolved = null;
        if (!refPath.StartsWith("#/", StringComparison.Ordinal))
        {
            return false;
        }

        JsonNode? current = rootSchema;
        string[] segments = refPath[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = DecodePointerSegment(segments[i]);
            if (current is JsonObject currentObject)
            {
                if (!currentObject.TryGetPropertyValue(segment, out current))
                {
                    return false;
                }
            }
            else if (current is JsonArray currentArray)
            {
                if (!int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                    index < 0 ||
                    index >= currentArray.Count)
                {
                    return false;
                }

                current = currentArray[index];
            }
            else
            {
                return false;
            }
        }

        resolved = current;
        return true;
    }

    private static string DecodePointerSegment(string segment)
    {
        return segment
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }

    private static bool JsonEquals(JsonNode? left, JsonNode? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(
            left.ToJsonString(),
            right.ToJsonString(),
            StringComparison.Ordinal);
    }

    private static bool TryGetBoolean(JsonNode? node, out bool value)
    {
        value = default;
        return node is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }

    private static bool TryGetString(JsonNode? node, out string? value)
    {
        value = default;
        return node is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }

    private static bool TryGetNumber(JsonNode? node, out double value)
    {
        value = default;
        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue(out double doubleValue))
        {
            value = doubleValue;
            return true;
        }

        if (jsonValue.TryGetValue(out long longValue))
        {
            value = longValue;
            return true;
        }

        if (jsonValue.TryGetValue(out int intValue))
        {
            value = intValue;
            return true;
        }

        return false;
    }

    private static bool TryGetInt(JsonObject schema, string propertyName, out int value)
    {
        value = default;
        if (!schema.TryGetPropertyValue(propertyName, out JsonNode? propertyValue) || propertyValue is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue(out int intValue))
        {
            value = intValue;
            return true;
        }

        if (jsonValue.TryGetValue(out long longValue) && longValue >= int.MinValue && longValue <= int.MaxValue)
        {
            value = (int)longValue;
            return true;
        }

        return false;
    }

    private static bool TryGetDouble(JsonObject schema, string propertyName, out double value)
    {
        value = default;
        if (!schema.TryGetPropertyValue(propertyName, out JsonNode? propertyValue))
        {
            return false;
        }

        return TryGetNumber(propertyValue, out value);
    }
}

