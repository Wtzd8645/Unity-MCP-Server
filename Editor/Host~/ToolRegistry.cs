using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcpServer.Host;

public sealed class ToolRegistry
{
    private readonly IReadOnlyDictionary<string, ToolDefinition> _toolsByName;
    private readonly IReadOnlyDictionary<string, string> _toolToModuleByName;

    private ToolRegistry(
        IReadOnlyDictionary<string, ToolDefinition> toolsByName,
        IReadOnlyDictionary<string, string> toolToModuleByName,
        IReadOnlyList<string> enabledModules)
    {
        _toolsByName = toolsByName;
        _toolToModuleByName = toolToModuleByName;
        EnabledModules = enabledModules;
    }

    public IReadOnlyList<ToolDefinition> Tools => _toolsByName.Values.ToList();
    public IReadOnlyList<string> EnabledModules { get; }

    public bool TryGetTool(string name, out ToolDefinition tool)
    {
        return _toolsByName.TryGetValue(name, out tool!);
    }

    public string? GetModuleForTool(string toolName)
    {
        if (TryGetTool(toolName, out ToolDefinition tool) &&
            _toolToModuleByName.TryGetValue(tool.Name, out string? module))
        {
            return module;
        }

        return null;
    }

    public static ToolRegistry LoadFromSchemas(string mcpRoot, IReadOnlyCollection<string>? enabledModules)
    {
        string manifestPath = Path.Combine(mcpRoot, "schemas", "mcp-tool-modules.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"Cannot find module manifest at '{manifestPath}'.");
        }

        ModuleManifest manifest = LoadModuleManifest(manifestPath);

        HashSet<string> selectedModules = enabledModules is { Count: > 0 }
            ? new HashSet<string>(enabledModules, StringComparer.Ordinal)
            : manifest.Modules
                .Where(module => module.EnabledByDefault)
                .Select(module => module.Name)
                .ToHashSet(StringComparer.Ordinal);

        foreach (string moduleName in selectedModules)
        {
            bool exists = manifest.Modules.Any(m => m.Name.Equals(moduleName, StringComparison.Ordinal));
            if (!exists)
            {
                throw new InvalidOperationException(
                    $"Unknown module '{moduleName}'. Check schemas/mcp-tool-modules.json.");
            }
        }

        var toolsByName = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
        var toolToModuleByName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (ModuleDefinition module in manifest.Modules.Where(m => selectedModules.Contains(m.Name)))
        {
            string moduleFileName = $"mcp-tools-{module.Name.Replace('_', '-')}.input-schemas.json";
            string moduleSchemaPath = Path.Combine(mcpRoot, "schemas", moduleFileName);

            if (!File.Exists(moduleSchemaPath))
            {
                throw new FileNotFoundException(
                    $"Expected schema file for module '{module.Name}' not found: {moduleSchemaPath}");
            }

            foreach (ToolDefinition tool in LoadToolsFromSchemaFile(moduleSchemaPath))
            {
                if (!toolsByName.TryAdd(tool.Name, tool))
                {
                    continue;
                }

                toolToModuleByName[tool.Name] = module.Name;
            }
        }

        return new ToolRegistry(
            toolsByName,
            toolToModuleByName,
            selectedModules.OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    private static ModuleManifest LoadModuleManifest(string manifestPath)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        string raw = File.ReadAllText(manifestPath);
        ModuleManifest? manifest = JsonSerializer.Deserialize<ModuleManifest>(raw, options);
        if (manifest is null || manifest.Modules.Count == 0)
        {
            throw new InvalidOperationException(
                $"Invalid module manifest content in {manifestPath}");
        }

        return manifest;
    }

    private static IEnumerable<ToolDefinition> LoadToolsFromSchemaFile(string schemaPath)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        if (!doc.RootElement.TryGetProperty("tools", out JsonElement toolsElement) ||
            toolsElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement toolElement in toolsElement.EnumerateArray())
        {
            if (!toolElement.TryGetProperty("name", out JsonElement nameElement))
            {
                continue;
            }

            string? name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string description = toolElement.TryGetProperty("description", out JsonElement descriptionElement)
                ? descriptionElement.GetString() ?? string.Empty
                : string.Empty;

            JsonNode inputSchema = toolElement.TryGetProperty("inputSchema", out JsonElement inputSchemaElement)
                ? JsonNode.Parse(inputSchemaElement.GetRawText()) ?? new JsonObject()
                : new JsonObject();

            yield return new ToolDefinition(
                Name: name,
                Description: description,
                InputSchema: inputSchema);
        }
    }
}

public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonNode InputSchema);

public sealed record ModuleManifest(IReadOnlyList<ModuleDefinition> Modules);

public sealed record ModuleDefinition(
    string Name,
    string Risk,
    bool EnabledByDefault,
    IReadOnlyList<string> Tools);
