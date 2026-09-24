using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;

namespace Wpe.Core.Modules;

/// <summary>
/// Convenience base for rule variants: implement <see cref="Info"/> and override only the
/// lifecycle steps you actually need. Keeps new variants free of boilerplate — the platform
/// still talks to the <see cref="IRuleVariant"/> interface.
/// </summary>
public abstract class RuleVariantBase : IRuleVariant
{
    public abstract VariantInfo Info { get; }

    public virtual void Load(string? configJson, GameDefinition def) { }

    public virtual void Register(ModuleHost host) { }

    public virtual void Apply(GameState state, GameEngine? engine) { }

    public virtual void Validate(GameDefinition def, List<string> issues) { }

    /// <summary>Register one expression function under several aliases (e.g. camel + snake case).</summary>
    protected static void Fn(ModuleHost host, ModuleHost.ExprFunc f, params string[] names)
        => host.AddFunctionAliases(f, names);
}
