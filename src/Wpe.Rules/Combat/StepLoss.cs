using System.Globalization;
using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Modules;

namespace Wpe.Rules.Combat;

/// <summary>
/// combat / stepLoss — a two-sided step-loss CRT. Table cells hold a result like "2/1"
/// (attacker loses 2 steps, defender 1). The variant writes the numeric losses into
/// ctx.Vars["attLoss"]/["defLoss"] (and the raw code into resultVar) so rule effects can
/// apply them to stepped counters. A bare number means attacker-only loss.
/// </summary>
public sealed class StepLoss : RuleVariantBase, ICombatModule
{
    private readonly Dictionary<string, CombatDef> _combatDefs = new();

    public override VariantInfo Info => new()
    {
        Subsystem = "combat",
        Id = "stepLoss",
        Description = "双方步损 CRT：结果码 '攻/守' → attLoss/defLoss 数值",
        Requires = new[] { "dice" },
        Provides = new[] { "combatResolution", "stepLoss" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        using var doc = JsonDocument.Parse(configJson);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var cbt = prop.Value.Deserialize<CombatDef>(JsonOpts);
            if (cbt != null && !string.IsNullOrEmpty(prop.Name))
                _combatDefs[prop.Name] = cbt;
        }
    }

    public override void Register(ModuleHost host)
    {
        host.Combat = this;
        foreach (var (k, v) in _combatDefs) host.CombatDefs[k] = v;
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        foreach (var (id, cbt) in _combatDefs)
        {
            if (string.IsNullOrWhiteSpace(cbt.RowExpr)) issues.Add($"[combat] {id}: 缺少 rowExpr");
            if (string.IsNullOrWhiteSpace(cbt.ColExpr)) issues.Add($"[combat] {id}: 缺少 colExpr");
            if (cbt.Table == null) { issues.Add($"[combat] {id}: 缺少 table"); continue; }
            foreach (var r in cbt.Table.Rows)
                foreach (var c in r.Columns)
                    if (!ParseLosses(c.Result, out _, out _))
                        issues.Add($"[combat] {id}: 结果码 '{c.Result}' 不是 '攻/守' 步损格式");
        }
        foreach (var m in def.Moves.Values)
            if (!string.IsNullOrEmpty(m.Combat) && !_combatDefs.ContainsKey(m.Combat))
                issues.Add($"[combat] 行动 '{m.Id}' 引用了不存在的战斗表 '{m.Combat}'");
    }

    public string Resolve(RuleContext ctx, CombatDef cbt)
    {
        if (cbt.Table == null)
            throw new InvalidOperationException("Combat definition has no table");
        var row = ctx.Engine.Compile(cbt.RowExpr).EvalNumber(ctx);
        var col = ctx.Engine.Compile(cbt.ColExpr).EvalNumber(ctx);
        var result = TableLookup.Find(cbt.Table, row, col);
        ParseLosses(result, out var attLoss, out var defLoss);
        ctx.Vars["attLoss"] = (double)attLoss;
        ctx.Vars["defLoss"] = (double)defLoss;
        ctx.Vars[cbt.ResultVar] = result;
        ctx.State.LogMessage($"查表 {cbt.Table.Id}: 行={row:F2} 列={col:F2} → 攻损{attLoss}/守损{defLoss}");
        return result;
    }

    /// <summary>Parse "a/d" (attacker/defender steps); a bare number is attacker-only.</summary>
    private static bool ParseLosses(string text, out int attLoss, out int defLoss)
    {
        attLoss = 0; defLoss = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('/', 2);
        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out attLoss)) return false;
        if (parts.Length == 2)
            return int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out defLoss);
        return true;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
