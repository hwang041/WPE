using System.Text.Json;
using System.Text.Json.Serialization;
using Wpe.Core.Loading;
using Wpe.Core.Modules;

namespace Wpe.Rules.Platform;

/// <summary>Declared metadata for one rule variant, read from its variant.json (the spec).</summary>
public sealed class VariantMeta
{
    public string Subsystem { get; init; } = "";
    public string Id { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    public string Status { get; init; } = "stable";
    public string Description { get; init; } = "";
    public string[] Requires { get; init; } = Array.Empty<string>();
    public string[] RequiresVariants { get; init; } = Array.Empty<string>();
    public string[] Provides { get; init; } = Array.Empty<string>();
    public string[] Overrides { get; init; } = Array.Empty<string>();
    public string? ConfigFile { get; init; }

    public string Key => $"{Subsystem}/{Id}";
}

/// <summary>
/// The rule library as data: reads rules/&lt;category&gt;/&lt;variant&gt;/variant.json (metadata)
/// and rules/families/&lt;name&gt;.json (presets). Together with <see cref="VariantRegistry"/>
/// (which finds the code) this makes the library discoverable without hardcoded lists.
/// </summary>
public sealed class RuleCatalog
{
    /// <summary>Stable ordering of the exclusive capabilities for Load/Register/Apply
    /// (map first: it builds the shared map data; scenario after counter; territory last
    /// among state builders). Contributions sort after all exclusive ones.</summary>
    public static readonly string[] CapabilityOrder =
        { "map", "counter", "scenario", "zoc", "territory", "cards", "movement", "supply", "combat", "dice", "turn" };

    public static int RankOf(IEnumerable<string> provides)
    {
        var caps = provides.Where(Capabilities.IsExclusive).ToList();
        if (caps.Count == 0) return int.MaxValue; // contributions (functions/effects)
        int best = int.MaxValue;
        foreach (var c in caps)
        {
            int i = Array.FindIndex(CapabilityOrder, x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && i < best) best = i;
        }
        return best;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string RootDir { get; }
    /// <summary>variant metadata keyed by "category/id".</summary>
    public Dictionary<string, VariantMeta> Variants { get; } = new();
    /// <summary>family preset name -> category -> variant ids (one or more per category).</summary>
    public Dictionary<string, Dictionary<string, List<string>>> Presets { get; } = new();

    public const string DefaultFamily = "default";

    private RuleCatalog(string rootDir) => RootDir = rootDir;

    public static RuleCatalog Load()
    {
        var root = FindRulesDir() ?? Path.Combine(AppContext.BaseDirectory, "rules");
        var catalog = new RuleCatalog(root);

        if (Directory.Exists(root))
        {
            foreach (var file in Directory.EnumerateFiles(root, "variant.json", SearchOption.AllDirectories))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<VariantMeta>(File.ReadAllText(file), JsonOpts);
                    if (meta != null && meta.Subsystem.Length > 0 && meta.Id.Length > 0)
                        catalog.Variants[meta.Key] = meta;
                }
                catch
                {
                    // surfaced by LoadIssues()
                    catalog._parseIssues.Add(file);
                }
            }

            var famDir = Path.Combine(root, "families");
            if (Directory.Exists(famDir))
                foreach (var file in Directory.EnumerateFiles(famDir, "*.json"))
                {
                    try
                    {
                        var family = JsonSerializer.Deserialize<FamilyFile>(File.ReadAllText(file), JsonOpts);
                        if (family != null && family.Family.Length > 0)
                            catalog.Presets[family.Family] = family.Variants;
                    }
                    catch
                    {
                        catalog._parseIssues.Add(file);
                    }
                }
        }

        return catalog;
    }

    private readonly List<string> _parseIssues = new();

    /// <summary>Reconcile the on-disk catalog against the code registry (1:1) plus parse issues.</summary>
    public List<string> Reconcile(VariantRegistry registry)
    {
        var issues = new List<string>();
        foreach (var f in _parseIssues)
            issues.Add($"[rules] 无法解析规则文件: {f}");

        foreach (var (key, meta) in Variants)
        {
            var info = registry.Create(meta.Subsystem, meta.Id)?.Info;
            if (info == null)
            {
                issues.Add($"[rules] variant.json 声明了 {key}，但代码中没有对应的变体实现");
                continue;
            }
            CompareDeclarations(key, meta, info, issues);
        }

        foreach (var (sub, id) in registry.Keys)
            if (!Variants.ContainsKey($"{sub}/{id}"))
                issues.Add($"[rules] 代码中有变体 {sub}/{id}，但 rules/{sub}/{id}/variant.json 缺失");

        return issues;
    }

    /// <summary>variant.json and code must declare the same requires / requiresVariants / provides / overrides.</summary>
    private static void CompareDeclarations(string key, VariantMeta meta, VariantInfo info, List<string> issues)
    {
        Compare("requires", meta.Requires, info.Requires, key, issues);
        Compare("requiresVariants", meta.RequiresVariants, info.RequiresVariants, key, issues);
        Compare("provides", meta.Provides, info.Provides, key, issues);
        Compare("overrides", meta.Overrides, info.Overrides, key, issues);
    }

    private static void Compare(string field, IEnumerable<string> disk, IEnumerable<string> code, string key, List<string> issues)
    {
        var d = new HashSet<string>(disk, StringComparer.Ordinal);
        var c = new HashSet<string>(code, StringComparer.Ordinal);
        if (!d.SetEquals(c))
            issues.Add($"[rules] {key} 的 {field} 声明不一致：variant.json=[{Join(d)}] 代码=[{Join(c)}]");
    }

    private static string Join(IEnumerable<string> xs) => string.Join(", ", xs.OrderBy(x => x, StringComparer.Ordinal));

    public bool TryGetPreset(string family, out Dictionary<string, List<string>> preset)
    {
        if (Presets.TryGetValue(family, out var p)) { preset = p; return true; }
        preset = new Dictionary<string, List<string>>();
        return false;
    }

    private sealed class FamilyFile
    {
        public string Family { get; set; } = "";

        [JsonConverter(typeof(StringOrArrayConverter))]
        public Dictionary<string, List<string>> Variants { get; set; } = new();
    }

    /// <summary>Accepts either "cat": "id" or "cat": ["id1","id2"].</summary>
    private sealed class StringOrArrayConverter : JsonConverter<Dictionary<string, List<string>>>
    {
        public override Dictionary<string, List<string>> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new Dictionary<string, List<string>>();
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var list = new List<string>();
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in prop.Value.EnumerateArray())
                        if (el.GetString() is { Length: > 0 } s) list.Add(s);
                }
                else if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is { Length: > 0 } single)
                {
                    list.Add(single);
                }
                result[prop.Name] = list;
            }
            return result;
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, List<string>> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (k, list) in value)
            {
                if (list.Count == 1) writer.WriteString(k, list[0]);
                else { writer.WritePropertyName(k); writer.WriteStartArray(); foreach (var v in list) writer.WriteStringValue(v); writer.WriteEndArray(); }
            }
            writer.WriteEndObject();
        }
    }

    /// <summary>Walk up from the exe to find the repo's rules/ folder.</summary>
    private static string? FindRulesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var rules = Path.Combine(dir.FullName, "rules");
            if (Directory.Exists(rules)) return rules;
            dir = dir.Parent;
        }
        return null;
    }
}
