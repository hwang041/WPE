using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Turn;

/// <summary>
/// turn / phases — the classic IGO-UGO phase cycle. Per-turn attributes (acted,
/// moveLeft, ...) defined by game.json `turnReset` are re-applied to the acting
/// player's units when their action phase begins.
/// </summary>
public sealed class Phases : RuleVariantBase, ITurnModule
{
    public override VariantInfo Info => new()
    {
        Subsystem = "turn",
        Id = "phases",
        Description = "IGO-UGO 阶段回合：回合重置按 game.json turnReset",
        Provides = new[] { "turnReset" },
        Status = "stable"
    };

    public override void Register(ModuleHost host) => host.Turn = this;

    public override void Validate(GameDefinition def, List<string> issues)
    {
        if (def.TurnReset.Count > 0 && def.PhaseOrder.Contains(def.TurnResetPhase) == false)
            issues.Add($"[turn] 配置了 turnReset 但 phaseOrder 中没有 {def.TurnResetPhase} 阶段（重置可能不会触发）");
    }

    public void OnTurnStart(GameEngine engine)
    {
        SpawnReinforcements(engine);

        var def = engine.Def;
        if (def.TurnReset.Count == 0) return;
        foreach (var c in engine.State.Counters.Where(c => c.AttributeInt(ContractNames.Owner, -1) == engine.State.ActivePlayer))
        {
            foreach (var r in def.TurnReset)
            {
                if (r.Value.StartsWith("expr:"))
                {
                    var ctx = engine.MakeContext(c, null, null);
                    c.Attributes[r.Attr] = engine.Compile(r.Value.Substring(5)).EvalNumber(ctx);
                }
                else if (double.TryParse(r.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var num))
                {
                    c.Attributes[r.Attr] = num;
                }
                else if (c.Attributes.TryGetValue(r.Value, out var src))
                {
                    c.Attributes[r.Attr] = src;
                }
                else
                {
                    c.Attributes[r.Attr] = 0;
                }
            }
        }
    }

    /// <summary>Spawn reinforcements whose entryTurn == current turn at their entry hex
    /// (nearest free hex if it is occupied/out of bounds).</summary>
    private static void SpawnReinforcements(GameEngine engine)
    {
        var state = engine.State;
        var map = state.Map;
        if (map == null) return;
        var used = state.CountersOnBoard().Select(c => c.Hex).ToHashSet();

        foreach (var c in state.Counters)
        {
            if (c.AttributeInt(ContractNames.EntryTurn, -1) != state.TurnNumber) continue;
            if (c.OnBoard) continue;
            if (!c.Attributes.TryGetValue(ContractNames.EntryHex, out var h) || h is not int[] qr || qr.Length < 2) continue;

            var start = new HexCoord(qr[0], qr[1]);
            var pos = start;
            if (used.Contains(start) || !map.InBounds(start))
            {
                var queue = new Queue<HexCoord>();
                var visited = new HashSet<HexCoord>();
                queue.Enqueue(start); visited.Add(start);
                bool found = false;
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    if (!used.Contains(cur) && map.InBounds(cur))
                    {
                        pos = cur; found = true; break;
                    }
                    foreach (var n in map.Neighbors(cur))
                        if (map.InBounds(n) && visited.Add(n))
                            queue.Enqueue(n);
                }
                if (!found) continue;
            }

            c.Position = pos;
            used.Add(pos);
            state.LogMessage($"援军进场：{c.Name} 于 ({start.Q},{start.R})");
        }
    }
}
