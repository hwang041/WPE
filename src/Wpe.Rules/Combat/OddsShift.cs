using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Modules;

namespace Wpe.Rules.Combat;

/// <summary>
/// combat / oddsShift — odds-ratio CRT with column shifts. The row is a strength ratio,
/// the column is the die plus every expression listed in `shifts` (terrain, combined arms,
/// supply, flanking...), clamped by the table's column ranges. Result code as usual.
/// </summary>
public sealed class OddsShift : RuleVariantBase, ICombatModule
{
    private readonly Dictionary<string, CombatDef> _combatDefs = new();

    public override VariantInfo Info => new()
    {
        Subsystem = "combat",
        Id = "oddsShift",
        Description = "战力比 + 列移位 CRT：骰值加修正列后再查表",
        Requires = new[] { "dice" },
        Provides = new[] { "combatResolution", "combatShifts" },
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
            {
                if (r.Min > r.Max) issues.Add($"[combat] {id}: 行区间无效 ({r.Min},{r.Max})");
                if (r.Columns.Count == 0) issues.Add($"[combat] {id}: 行区间 ({r.Min},{r.Max}) 无列定义");
                foreach (var c in r.Columns)
                    if (c.Min > c.Max) issues.Add($"[combat] {id}: 列区间无效 ({c.Min},{c.Max})");
            }
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
        double col = ctx.Engine.Compile(cbt.ColExpr).EvalNumber(ctx);
        foreach (var shift in cbt.Shifts)
            col += ctx.Engine.Compile(shift).EvalNumber(ctx);

        var result = TableLookup.Find(cbt.Table, row, col);
        ctx.Vars[cbt.ResultVar] = result;
        ctx.State.LogMessage($"查表 {cbt.Table.Id}: 行={row:F2} 列={col:F2} → {(string.IsNullOrEmpty(result) ? "无匹配" : result)}");
        return result;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
