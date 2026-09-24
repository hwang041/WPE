using Wpe.Core.Modules;

namespace Wpe.Rules.Platform;

/// <summary>
/// Discovers every rule variant implementation in the Wpe.Rules assembly by reflection,
/// keyed by (subsystem, id). This replaces the old hardcoded switch: adding a variant is
/// just adding a class — no core edits. The disk side (rules/**/variant.json) is reconciled
/// against this by <see cref="RuleCatalog"/>.
/// </summary>
public sealed class VariantRegistry
{
    private readonly Dictionary<(string Subsystem, string Id), Type> _types = new();

    public VariantRegistry()
    {
        foreach (var t in typeof(VariantRegistry).Assembly.GetTypes())
        {
            if (t.IsAbstract || t.IsInterface || !typeof(IRuleVariant).IsAssignableFrom(t)) continue;
            if (Activator.CreateInstance(t) is not IRuleVariant v) continue;
            _types[(v.Info.Subsystem, v.Info.Id)] = t;
        }
    }

    public bool Contains(string subsystem, string id) => _types.ContainsKey((subsystem, id));

    /// <summary>Fresh instance per call — variants hold per-package config state.</summary>
    public IRuleVariant? Create(string subsystem, string id)
        => _types.TryGetValue((subsystem, id), out var t)
            ? (IRuleVariant)Activator.CreateInstance(t)!
            : null;

    public IEnumerable<(string Subsystem, string Id)> Keys => _types.Keys;

    public IEnumerable<VariantInfo> AllInfos()
    {
        foreach (var t in _types.Values)
            if (Activator.CreateInstance(t) is IRuleVariant v)
                yield return v.Info;
    }
}
