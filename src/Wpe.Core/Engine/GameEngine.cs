using Wpe.Core.Definition;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Core.Engine;

/// <summary>
/// The generic game interpreter ("中台"). It knows nothing about any specific wargame:
/// everything it does is driven by the GameDefinition, the selected rule variants
/// (via ModuleHost) and the current GameState.
/// </summary>
public sealed class GameEngine
{
    public GameDefinition Def { get; }
    public GameState State { get; }
    public ModuleHost Host { get; }
    public IHook? Hook { get; }

    private readonly Dictionary<string, Expr> _exprCache = new();
    private int _rollCounter;

    public GameEngine(GameDefinition def, GameState state, ModuleHost host, IHook? hook = null)
    {
        Def = def;
        State = state;
        Host = host;
        Hook = hook;
        if (string.IsNullOrEmpty(State.CurrentPhase) && Def.PhaseOrder.Count > 0)
            State.CurrentPhase = Def.PhaseOrder[0];
        SeedCoreEffects();
        ApplyTurnReset();
    }

    public bool GameOver => State.GameOver;
    public string? ResultMessage => State.ResultMessage;
    public bool IsTurn(int player) => !State.GameOver && State.ActivePlayer == player;

    public void ResetRuntime()
    {
        State.GameOver = false;
        State.ResultMessage = null;
        _rollCounter = 0;
    }

    // ---- expressions ----

    public Expr Compile(string source)
        => _exprCache.TryGetValue(source, out var e) ? e : (_exprCache[source] = Expr.Compile(source));

    private Expr GetExpr(string? source) => source == null ? null! : Compile(source);

    public RuleContext MakeContext(CounterState? counter, HexCoord? pos, CounterState? target)
    {
        var ctx = new RuleContext(State, Host, this)
        {
            Counter = counter,
            TargetPos = pos,
            TargetCounter = target
        };
        ctx.Vars["me"] = (double)State.ActivePlayer;
        return ctx;
    }

    // ---- legal moves ----

    public List<MoveDef> LegalMovesForCounter(CounterState counter)
    {
        var result = new List<MoveDef>();
        if (GameOver || !IsTurn(State.ActivePlayer)) return result;
        if (!counter.OnBoard) return result;

        foreach (var move in Def.Moves.Values)
        {
            if (!move.NeedsCounter) continue;
            if (!string.IsNullOrEmpty(move.Phase) && move.Phase != State.CurrentPhase) continue;
            var ctx = MakeContext(counter, null, null);
            bool ok = true;
            if (!string.IsNullOrEmpty(move.CounterFilter) && !GetExpr(move.CounterFilter).EvalBool(ctx)) ok = false;
            if (ok)
                foreach (var v in move.Validators)
                {
                    if (ReferencesPositionOrTarget(v)) continue;
                    if (!GetExpr(v).EvalBool(ctx)) { ok = false; break; }
                }
            if (ok) result.Add(move);
        }
        return result;
    }

    public List<MoveDef> GlobalLegalMoves()
    {
        var result = new List<MoveDef>();
        if (GameOver || !IsTurn(State.ActivePlayer)) return result;
        foreach (var move in Def.Moves.Values)
        {
            if (move.NeedsCounter) continue;
            if (!string.IsNullOrEmpty(move.Phase) && move.Phase != State.CurrentPhase) continue;
            var ctx = MakeContext(null, null, null);
            bool ok = true;
            foreach (var v in move.Validators) if (!GetExpr(v).EvalBool(ctx)) { ok = false; break; }
            if (ok) result.Add(move);
        }
        return result;
    }

    // ---- reachability / targets (delegated to the movement / combat seams) ----

    public List<HexCoord> ReachablePositions(CounterState unit)
    {
        var result = new List<HexCoord>();
        if (!unit.OnBoard) return result;
        if (unit.AttributeInt("acted", 0) == 1) return result;
        if (unit.IsBack) return result;
        if (Host.Movement == null) return result;
        var reachable = Host.Movement.Reachable(unit, this);
        result.AddRange(reachable);
        return result;
    }

    public List<CounterState> TargetCountersForMove(MoveDef move, CounterState counter)
    {
        var result = new List<CounterState>();
        if (!counter.OnBoard) return result;
        foreach (var cand in State.CountersOnBoard())
        {
            if (ReferenceEquals(cand, counter)) continue;
            var ctx = MakeContext(counter, null, cand);
            bool ok = true;
            if (!string.IsNullOrEmpty(move.CounterFilter) && !GetExpr(move.CounterFilter).EvalBool(ctx)) ok = false;
            if (ok && !string.IsNullOrEmpty(move.TargetFilter)) ok = GetExpr(move.TargetFilter).EvalBool(ctx);
            if (ok)
                foreach (var v in move.Validators)
                    if (!GetExpr(v).EvalBool(ctx)) { ok = false; break; }
            if (ok) result.Add(cand);
        }
        return result;
    }

    private static bool ReferencesPositionOrTarget(string src)
    {
        var s = src.ToLowerInvariant();
        return s.Contains("target") || s.Contains("dist(") || s.Contains("adjacent")
            || s.Contains("occupied") || s.Contains("movecost") || s.Contains("rivercost")
            || s.Contains("inbounds") || s.Contains(" pos") || s.StartsWith("pos");
    }

    // ---- validation & application ----

    public bool CanApply(string moveId, CounterState? counter, HexCoord? pos, CounterState? target, out string? error)
    {
        error = null;
        if (GameOver) { error = "游戏已结束"; return false; }
        if (!IsTurn(State.ActivePlayer)) { error = "不是你的回合"; return false; }
        if (!Def.Moves.TryGetValue(moveId, out var move)) { error = $"未知行动 {moveId}"; return false; }
        if (!string.IsNullOrEmpty(move.Phase) && move.Phase != State.CurrentPhase)
        {
            error = $"该行动只能在阶段“{move.Phase}”进行（当前：{State.CurrentPhase}）";
            return false;
        }
        if (move.NeedsCounter && counter == null) { error = "需要选择算子"; return false; }
        if (move.NeedsPosition && pos == null) { error = "需要目标位置"; return false; }
        if (move.NeedsTargetCounter && target == null) { error = "需要目标算子"; return false; }

        var ctx = MakeContext(counter, pos, target);
        if (!string.IsNullOrEmpty(move.CounterFilter) && counter != null && !GetExpr(move.CounterFilter).EvalBool(ctx))
        { error = "该算子不满足行动条件"; return false; }
        if (!string.IsNullOrEmpty(move.TargetFilter) && target != null && !GetExpr(move.TargetFilter).EvalBool(ctx))
        { error = "目标不满足条件"; return false; }
        foreach (var v in move.Validators)
            if (!GetExpr(v).EvalBool(ctx)) { error = $"条件不满足: {v}"; return false; }
        if (Hook != null && !Hook.Validate(ctx, moveId, out error)) return false;
        return true;
    }

    public bool Apply(string moveId, CounterState? counter, HexCoord? pos, CounterState? target)
    {
        if (!CanApply(moveId, counter, pos, target, out var error))
        {
            State.LogMessage($"被拒绝: {error}");
            return false;
        }
        var move = Def.Moves[moveId];
        var ctx = MakeContext(counter, pos, target);
        Hook?.OnBeforeMove(ctx, moveId);

        if (move.Roll != null) ExecuteRoll(ctx, move.Roll);
        string? tableResult = null;
        if (move.Combat != null)
        {
            if (Host.CombatDefs.TryGetValue(move.Combat, out var cbt))
                tableResult = Host.Combat != null
                    ? Host.Combat.Resolve(ctx, cbt)
                    : null;
            else
                State.LogMessage($"缺少战斗表定义: {move.Combat}");
        }

        ExecuteEffects(ctx, move.Effects);
        if (tableResult != null && move.ResultEffects.TryGetValue(tableResult, out var effects))
            ExecuteEffects(ctx, effects);

        Hook?.OnAfterMove(ctx, moveId);
        FireTriggers("moveApplied", move.Id);

        State.LogMessage($"{move.Label}{(counter != null ? " [" + counter.Name + "]" : "")}{(target != null ? " → " + target.Name : "")}");

        // auto-end the action: if the actor can neither move nor attack any more, mark it acted.
        if (Def.AutoActWhenExhausted && counter != null && counter.AttributeInt("acted", 0) == 0 &&
            !HasRemainingAction(counter))
        {
            counter.Attributes["acted"] = 1;
            State.LogMessage($"{counter.Name} 移动力/攻击用尽，行动结束");
        }

        CheckEndConditions();
        return true;
    }

    public bool HasRemainingAction(CounterState unit)
    {
        if (unit.AttributeInt("acted", 0) == 1) return false;
        if (!unit.OnBoard) return false;
        foreach (var move in Def.Moves.Values)
        {
            if (!move.NeedsCounter) continue;
            if (move.IsEndAction) continue;
            if (!string.IsNullOrEmpty(move.Phase) && move.Phase != State.CurrentPhase) continue;
            if (move.NeedsPosition)
            {
                if (move.Kind == "movement" && Host.Movement != null)
                {
                    if (Host.Movement.Reachable(unit, this).Count > 0) return true;
                    continue;
                }
                var gm = State.Map;
                if (gm != null)
                    foreach (var n in HexMath.Neighbors(unit.Hex, gm.PointyTop))
                    {
                        if (!gm.InBounds(n)) continue;
                        if (CanApply(move.Id, unit, n, null, out _)) return true;
                    }
            }
            else if (move.NeedsTargetCounter)
            {
                if (TargetCountersForMove(move, unit).Count > 0) return true;
            }
            else if (CanApply(move.Id, unit, null, null, out _))
            {
                return true;
            }
        }
        return false;
    }

    // ---- dice & combat resolution ----

    private void ExecuteRoll(RuleContext ctx, RollSpec roll)
    {
        _rollCounter++;
        var seed = (int)(Environment.TickCount ^ (_rollCounter * 2654435761L));
        var values = Host.Dice != null
            ? Host.Dice.Roll(roll, seed)
            : new SeededRandom(seed).RollDice(roll.Count, roll.Sides);
        double sum = values.Sum();
        if (!string.IsNullOrEmpty(roll.Drm)) sum += GetExpr(roll.Drm).EvalNumber(ctx);

        State.LastDice.Clear();
        for (int i = 0; i < values.Length; i++)
            State.LastDice.Add(new DieResult(roll.Hand, i, roll.Sides, values[i]));
        ctx.Vars[roll.Var] = sum;
        ctx.Vars["raw"] = (double)values.Sum();
        State.LogMessage($"掷骰 {roll.Count}d{roll.Sides}{(string.IsNullOrEmpty(roll.Drm) ? "" : "+修正")} = {sum} ({string.Join(",", values)})");
    }

    // ---- effects ----

    /// <summary>Evaluate every effect's `when` gate BEFORE applying, so mutually-exclusive conditions resolve correctly.</summary>
    private void ExecuteEffects(RuleContext ctx, List<EffectDef> effects)
    {
        var gated = effects
            .Select(e => (e, gate: string.IsNullOrEmpty(e.When) || GetExpr(e.When).EvalBool(ctx)))
            .ToList();
        foreach (var (effect, gate) in gated)
            if (gate) Host.TryApplyEffect(ctx, effect);
    }

    private void SeedCoreEffects()
    {
        Host.AddEffect("move", MoveEffect);
        Host.AddEffect("movecounter", MoveEffect);
        Host.AddEffect("flip", (ctx, e) =>
        {
            var c = Select(ctx, e);
            if (c != null) c.Side = c.Side == Side.Front ? Side.Back : Side.Front;
        });
        Host.AddEffect("remove", (ctx, e) =>
        {
            var c = Select(ctx, e);
            if (c != null) c.Position = null;
        });
        Host.AddEffect("retreat", RetreatEffect);
        Host.AddEffect("setattr", (ctx, e) =>
        {
            var c = Select(ctx, e);
            if (c == null) return;
            c.Attributes[e.Key] = !string.IsNullOrEmpty(e.Value) ? GetExpr(e.Value).Eval(ctx) ?? "" : "";
        });
        Host.AddEffect("setside", (ctx, e) =>
        {
            var c = Select(ctx, e);
            if (c == null) return;
            c.Side = e.Value.Trim().ToLowerInvariant() is "back" or "散乱" ? Side.Back : Side.Front;
        });
        Host.AddEffect("setvar", (ctx, e) =>
            State.Vars[e.Key] = !string.IsNullOrEmpty(e.Value) ? GetExpr(e.Value).Eval(ctx) ?? "" : "");
        Host.AddEffect("addvar", (ctx, e) =>
        {
            var delta = GetExpr(e.Value).EvalNumber(ctx);
            var cur = State.Vars.TryGetValue(e.Key, out var v) ? ValueAccessor.AsNumber(v) : 0;
            State.Vars[e.Key] = cur + delta;
        });
        Host.AddEffect("log", (ctx, e) =>
            State.LogMessage(string.IsNullOrEmpty(e.Text) ? "" : GetExpr(e.Text).EvalString(ctx)));
        Host.AddEffect("endphase", (ctx, e) => EndPhase());
        Host.AddEffect("pass", (ctx, e) => Pass());
        Host.AddEffect("endturn", (ctx, e) => Pass());
        Host.AddEffect("reveal", (ctx, e) => { });
    }

    private void MoveEffect(RuleContext ctx, EffectDef e)
    {
        var c = Select(ctx, e);
        if (c == null) return;
        if (e.To == "pos" && ctx.TargetPos.HasValue) c.Position = ctx.TargetPos.Value;
        else if (!string.IsNullOrEmpty(e.X) && !string.IsNullOrEmpty(e.Y))
            c.Position = new HexCoord((int)GetExpr(e.X).EvalNumber(ctx), (int)GetExpr(e.Y).EvalNumber(ctx));
    }

    private void RetreatEffect(RuleContext ctx, EffectDef e)
    {
        var c = Select(ctx, e);
        if (c == null || !c.OnBoard) return;
        var gm = State.Map;
        if (gm == null) return;
        // reference is the other side: the retreating counter moves away from its opponent
        var refCounter = Select(ctx, e.Counter == "target" ? "counter" : "target");
        if (refCounter == null || !refCounter.OnBoard) return;
        var refHex = refCounter.Hex;

        var steps = Math.Max(1, e.Hexes);
        bool moved = false;
        for (int s = 0; s < steps && c.OnBoard; s++)
        {
            var here = c.Hex;
            var hereDist = HexMath.Distance(here, refHex);
            var candidates = HexMath.Neighbors(here, gm.PointyTop)
                .Where(n => gm.InBounds(n)
                         && HexMath.Distance(n, refHex) > hereDist
                         && !State.CountersOnBoard().Any(x => !ReferenceEquals(x, c) && x.Hex == n))
                .OrderByDescending(n => HexMath.Distance(n, refHex))
                .ThenBy(n => n.Q).ThenBy(n => n.R)
                .ToList();
            if (candidates.Count == 0) break;
            c.Position = candidates[0];
            moved = true;
        }

        if (moved)
        {
            State.LogMessage($"{c.Name} 撤退至 ({c.Hex.Q},{c.Hex.R})");
            return;
        }

        var fail = e.OnFail?.Trim().ToLowerInvariant();
        if (fail is "flip" or "remove")
        {
            if (fail == "remove" || c.Side == Side.Back)
            {
                c.Position = null;
                State.LogMessage($"{c.Name} 无路可退，被歼灭！");
            }
            else
            {
                c.Side = Side.Back;
                if (c.Attributes.TryGetValue("moveLeft", out var ml) &&
                    double.TryParse(ml?.ToString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var cur))
                    c.Attributes["moveLeft"] = cur - Def.Damage.MovePenalty;
                State.LogMessage($"{c.Name} 无路可退，受创");
            }
        }
        else
        {
            State.LogMessage($"{c.Name} 无路可退，原地坚守");
        }
    }

    private CounterState? Select(RuleContext ctx, EffectDef e) => Select(ctx, e.Counter);
    private CounterState? Select(RuleContext ctx, string which)
        => which == "target" ? ctx.TargetCounter : ctx.Counter;

    // ---- turn / phase machinery ----

    public void EndPhase()
    {
        if (Def.PhaseOrder.Count == 0) return;
        var idx = Def.PhaseOrder.IndexOf(State.CurrentPhase);
        var next = idx < 0 ? 0 : (idx + 1) % Def.PhaseOrder.Count;
        State.CurrentPhase = Def.PhaseOrder[next];
        if (next == 0)
        {
            State.ActivePlayer = (State.ActivePlayer + 1) % State.PlayerCount;
            if (State.ActivePlayer == 0) State.TurnNumber++;
            State.LogMessage($"轮到玩家{State.ActivePlayer + 1}，第{State.TurnNumber}回合");
        }
        else
        {
            State.LogMessage($"进入阶段: {State.CurrentPhase}");
        }
        if (State.CurrentPhase == "action") ApplyTurnReset();
        FireTriggers("phaseStart", State.CurrentPhase);
    }

    public void Pass()
    {
        State.ActivePlayer = (State.ActivePlayer + 1) % State.PlayerCount;
        if (State.ActivePlayer == 0) State.TurnNumber++;
        State.CurrentPhase = Def.PhaseOrder.Count > 0 ? Def.PhaseOrder[0] : State.CurrentPhase;
        State.LogMessage($"玩家{State.ActivePlayer + 1} 结束回合，进入其第{State.TurnNumber}回合");
        ApplyTurnReset();
        FireTriggers("turnStart", State.CurrentPhase);
    }

    /// <summary>Spawn reinforcements whose entryTurn == current turn at their entry hex (nearest free if occupied).</summary>
    public void SpawnReinforcements()
    {
        var gm = State.Map;
        if (gm == null) return;
        var used = State.CountersOnBoard().Select(c => c.Hex).ToHashSet();

        foreach (var c in State.Counters)
        {
            var et = c.AttributeInt("entryTurn", -1);
            if (et != State.TurnNumber) continue;
            if (c.OnBoard) continue;
            if (!c.Attributes.TryGetValue("entryHex", out var h) || h is not int[] qr || qr.Length < 2) continue;

            var start = new HexCoord(qr[0], qr[1]);
            var pos = start;
            if (used.Contains(start) || !gm.InBounds(start))
            {
                var queue = new Queue<HexCoord>();
                var visited = new HashSet<HexCoord>();
                queue.Enqueue(start); visited.Add(start);
                bool found = false;
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    if (!used.Contains(cur) && gm.InBounds(cur))
                    {
                        pos = cur; found = true; break;
                    }
                    foreach (var n in HexMath.Neighbors(cur, gm.PointyTop))
                        if (gm.InBounds(n) && visited.Add(n))
                            queue.Enqueue(n);
                }
                if (!found) continue;
            }

            c.Position = pos;
            used.Add(pos);
            State.LogMessage($"援军进场：{c.Name} 于 ({start.Q},{start.R})");
        }
    }

    private void ApplyTurnReset()
    {
        SpawnReinforcements();
        Host.Turn?.OnTurnStart(this);
    }

    // ---- triggers & victory ----

    private void FireTriggers(string onEvent, string? phase = null)
    {
        foreach (var t in Def.Triggers.Values)
        {
            if (t.On != onEvent) continue;
            if (!string.IsNullOrEmpty(t.Phase) && t.Phase != phase) continue;
            var ctx = MakeContext(null, null, null);
            ExecuteEffects(ctx, t.Do);
        }
    }

    private void CheckEndConditions()
    {
        if (Host.Victory != null)
        {
            Host.Victory.Check(this);
            return;
        }
        foreach (var end in Def.EndConditions)
        {
            var ctx = MakeContext(null, null, null);
            if (GetExpr(end.When).EvalBool(ctx))
            {
                State.GameOver = true;
                State.ResultMessage = end.Message;
                var winner = end.Winner is int w && w >= 0 ? w : State.ActivePlayer;
                State.LogMessage($"游戏结束: {end.Message} (玩家{winner + 1})");
                return;
            }
        }
    }
}
