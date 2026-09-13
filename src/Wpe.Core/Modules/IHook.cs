using Wpe.Core.Expressions;

namespace Wpe.Core.Modules;

/// <summary>
/// Per-game C# extension point for rules that are awkward to express declaratively.
/// A game package may reference an implementation via game.json `hookType`
/// (assembly-qualified type name).
/// </summary>
public interface IHook
{
    /// <summary>Additional validation. Return false (with reason) to reject an action.</summary>
    bool Validate(RuleContext ctx, string moveId, out string? error);

    void OnBeforeMove(RuleContext ctx, string moveId);
    void OnAfterMove(RuleContext ctx, string moveId);
}
