using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Combat;

/// <summary>
/// combat / crTable — Combat Results Table: strength ratio row × dice column → result
/// code, which the move's resultEffects then turn into effects. Row/column indices are
/// computed by expressions (e.g. "effStr(counter) / effStr(target)" and "roll").
/// </summary>
public sealed class CrTable : IRuleVariant, ICombatModule
{
    public VariantInfo Info => new()
    {
        Subsystem = "combat",
        Id = "crTable",
        Description = "战力比 CRT：战力比行 × 骰子列 → 结果码",
        Requires = new[] { "dice" },
        Status = "stable"
    };

    public void Load(string? configJson, GameDefinition def)
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

    private readonly Dictionary<string, CombatDef> _combatDefs = new();

    public void Register(ModuleHost host)
    {
        host.Combat = this;
        foreach (var (k, v) in _combatDefs) host.CombatDefs[k] = v;
    }

    public void Apply(GameState state, GameEngine? engine) { }

    public void Validate(GameDefinition def, List<string> issues)
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

    // ---- ICombatModule ----

    public string Resolve(RuleContext ctx, CombatDef cbt)
    {
        if (cbt.Table == null)
            throw new InvalidOperationException("Combat definition has no table");
        var row = ctx.Engine.Compile(cbt.RowExpr).EvalNumber(ctx);
        var col = ctx.Engine.Compile(cbt.ColExpr).EvalNumber(ctx);
        foreach (var r in cbt.Table.Rows)
        {
            if (row < r.Min || row > r.Max) continue;
            foreach (var c in r.Columns)
            {
                if (col < c.Min || col > c.Max) continue;
                ctx.Vars[cbt.ResultVar] = c.Result;
                ctx.State.LogMessage($"查表 {cbt.Table.Id}: 行={row:F2} 列={col} → {c.Result}");
                return c.Result;
            }
        }
        ctx.State.LogMessage($"查表 {cbt.Table.Id}: 无匹配 (行={row:F2} 列={col})");
        ctx.Vars[cbt.ResultVar] = "";
        return "";
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
