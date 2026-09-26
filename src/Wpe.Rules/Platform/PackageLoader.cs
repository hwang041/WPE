using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Loading;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Platform;

public sealed class LoadedGame
{
    public required GameDefinition Def { get; init; }
    public required GameState State { get; init; }
    public required ModuleHost Host { get; init; }
    public required GameEngine Engine { get; init; }
    public required string GameDir { get; init; }
}

public sealed class PackageResult
{
    public bool Ok => Errors.Count == 0 && Game != null;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public LoadedGame? Game { get; set; }
}

/// <summary>
/// The middle platform's package loader: reads a game folder (game.json + the table
/// files), assembles the selected rules (a set of orthogonal rules, possibly several
/// per category), validates them by capability, and hands back a running engine.
/// </summary>
public static class PackageLoader
{
    public static PackageResult Load(string gameDir)
    {
        var result = new PackageResult();
        string gameJsonPath = Path.Combine(gameDir, "game.json");
        if (!File.Exists(gameJsonPath))
        {
            result.Errors.Add($"缺少 game.json: {gameJsonPath}");
            return result;
        }

        // 1) main rules
        GameDefinition def;
        try
        {
            def = JsonSerializer.Deserialize<GameDefinition>(File.ReadAllText(gameJsonPath), JsonUtil.Opts)
                ?? throw new InvalidDataException("解析结果为空");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"game.json 解析失败: {ex.Message}");
            return result;
        }
        if (def.Moves.Count == 0)
            result.Errors.Add("[game.json] 未定义任何行动 (moves)");
        if (def.PhaseOrder.Count == 0)
            result.Errors.Add("[game.json] 未定义阶段序列 (phaseOrder)");

        // 2) discover the rule library (disk catalog + code registry) and reconcile them
        var registry = new VariantRegistry();
        var catalog = RuleCatalog.Load();
        result.Errors.AddRange(catalog.Reconcile(registry));

        // 3) resolve rule selection (family preset + per-category overrides)
        var selection = ResolveSelection(def, catalog, result);

        // 4) instantiate rules, ordered by capability (map first, contributions last)
        var host = new ModuleHost();
        CoreFunctions.Seed(host, def);

        var instances = new List<(RuleSelection sel, IRuleVariant variant)>();
        foreach (var sel in selection)
        {
            var variant = registry.Create(sel.Category, sel.Variant);
            if (variant == null)
            {
                result.Errors.Add($"[rules] 未知变体 '{sel.Variant}'（分类 {sel.Category}）");
                continue;
            }
            instances.Add((sel, variant));
        }
        foreach (var (sel, variant) in instances
                     .OrderBy(x => RuleCatalog.RankOf(x.variant.Info.Provides))
                     .ThenBy(x => x.sel.Category, StringComparer.Ordinal)
                     .ThenBy(x => x.sel.Variant, StringComparer.Ordinal))
        {
            host.AddRule(variant);
            host.BeginRule($"{sel.Category}/{sel.Variant}", variant.Info.Overrides);
            try
            {
                variant.Load(ReadConfig(sel, gameDir, result), def);
                variant.Register(host);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"[{sel.Category}/{sel.Variant}] 配置加载失败: {ex.Message}");
            }
        }
        host.BeginRule("");
        result.Errors.AddRange(host.NameConflicts);

        // 5) composition checks (capability based)
        CheckComposition(def, host, result);

        // 6) per-variant validation
        foreach (var v in host.AllVariants)
        {
            var sub = v.Info.Subsystem;
            try { v.Validate(def, result.Errors); }
            catch (Exception ex) { result.Errors.Add($"[{sub}/{v.Info.Id}] 校验异常: {ex.Message}"); }
        }

        // 7) name contract + expression validation (functions = error, names = warning)
        var contract = NameContract.Create();
        contract.ContributeFromGame(def);
        foreach (var v in host.AllVariants)
            if (v is INameContributor nc) nc.ContributeNames(contract);
        foreach (var cdef in host.Cards.Values)
            contract.ContributeEffects(cdef.Effects);

        ValidateExpressions(def, host, result, contract);

        if (result.Errors.Count > 0) return result;

        // 8) build state; Apply in the same capability order
        try
        {
            var state = new GameState(def) { Map = host.MapData };
            foreach (var v in host.AllVariants)
                v.Apply(state, null);

            // resolve each counter's faction colour from the game's palette (renderers stay game-agnostic)
            foreach (var c in state.Counters)
            {
                var fac = c.AttributeStr("faction", "");
                if (!string.IsNullOrEmpty(fac) && !c.Attributes.ContainsKey("color") &&
                    def.Factions.TryGetValue(fac, out var fd) && !string.IsNullOrEmpty(fd.Color))
                    c.Attributes["color"] = fd.Color;
            }

            // damaged-side penalties (contract: strength/move drop by these when flipped)
            foreach (var c in state.Counters)
            {
                if (!c.Attributes.ContainsKey("penaltyStrength"))
                    c.Attributes["penaltyStrength"] = def.Damage.StrengthPenalty;
                if (!c.Attributes.ContainsKey("penaltyMove"))
                    c.Attributes["penaltyMove"] = def.Damage.MovePenalty;
            }

            var hook = ResolveHook(def, result);
            var engine = new GameEngine(def, state, host, hook);
            result.Game = new LoadedGame
            {
                Def = def,
                State = state,
                Host = host,
                Engine = engine,
                GameDir = gameDir
            };
        }
        catch (Exception ex)
        {
            result.Errors.Add($"引擎构建失败: {ex.Message}");
        }
        return result;
    }

    // ---- resolution / config ----

    private static List<RuleSelection> ResolveSelection(GameDefinition def, RuleCatalog catalog, PackageResult result)
    {
        if (!catalog.TryGetPreset(def.Family, out var preset))
        {
            result.Warnings.Add($"[rules] 未知家族预设 '{def.Family}'，回退到 '{RuleCatalog.DefaultFamily}'");
            catalog.TryGetPreset(RuleCatalog.DefaultFamily, out preset);
        }

        var byCategory = new Dictionary<string, List<RuleSelection>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, ids) in preset)
            byCategory[category] = ids.Select(id => new RuleSelection { Category = category, Variant = id }).ToList();
        // a game override replaces the whole list for that category
        foreach (var (category, sels) in def.Rules)
            byCategory[category] = sels
                .Select(s => new RuleSelection { Category = category, Variant = s.Variant, File = s.File, Config = s.Config })
                .ToList();

        return byCategory.Values.SelectMany(x => x).ToList();
    }

    private static string? ReadConfig(RuleSelection sel, string gameDir, PackageResult result)
    {
        if (sel.Config != null)
            return JsonSerializer.Serialize(sel.Config, JsonUtil.Opts);
        if (string.IsNullOrEmpty(sel.File)) return null;
        var path = Path.Combine(gameDir, sel.File);
        if (!File.Exists(path))
        {
            result.Errors.Add($"找不到配置文件 {sel.File}（分类 {sel.Category}）");
            return null;
        }
        return File.ReadAllText(path);
    }

    private static IHook? ResolveHook(GameDefinition def, PackageResult result)
    {
        if (string.IsNullOrEmpty(def.HookType)) return null;
        var t = Type.GetType(def.HookType);
        if (t == null || !typeof(IHook).IsAssignableFrom(t))
        {
            result.Errors.Add($"[hook] 无法解析 hookType: {def.HookType}");
            return null;
        }
        return (IHook)Activator.CreateInstance(t)!;
    }

    // ---- validation ----

    private static void CheckComposition(GameDefinition def, ModuleHost host, PackageResult result)
    {
        var provided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in host.AllVariants)
            foreach (var p in v.Info.Provides) provided.Add(p);

        // every required capability must be provided by some selected rule
        foreach (var v in host.AllVariants)
            foreach (var req in v.Info.Requires)
                if (!provided.Contains(req))
                    result.Errors.Add($"[rules] 规则 {v.Info.Subsystem}/{v.Info.Id} 依赖能力 '{req}'，但没有任何已选规则提供它");

        // an exclusive capability may have at most one provider
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in host.AllVariants)
            foreach (var cap in v.Info.Provides.Where(Capabilities.IsExclusive))
            {
                var who = $"{v.Info.Subsystem}/{v.Info.Id}";
                if (owners.TryGetValue(cap, out var prev))
                    result.Errors.Add($"[rules] 独占能力 '{cap}' 被 {prev} 与 {who} 同时提供（同能力只能选一个）");
                else owners[cap] = who;
            }

        // specific variant pins, e.g. "map:hexGrid"
        foreach (var v in host.AllVariants)
            foreach (var pin in v.Info.RequiresVariants)
            {
                var parts = pin.Split(':', 2);
                if (parts.Length != 2) continue;
                if (!host.HasRule(parts[0], parts[1]))
                    result.Errors.Add($"[rules] 规则 {v.Info.Subsystem}/{v.Info.Id} 需要 {pin}，但未选择该变体");
            }

        // runtime safety: features in use must have their seam present
        if (def.Moves.Values.Any(m => m.Roll != null) && host.Dice == null)
            result.Errors.Add("[rules] 存在掷骰行动但未启用 dice 能力");
        if (def.Moves.Values.Any(m => !string.IsNullOrEmpty(m.Combat)) && host.Combat == null)
            result.Errors.Add("[rules] 存在战斗行动但未启用 combat 能力");
        if (def.Moves.Values.Any(m => m.Kind == "movement") && host.Movement == null)
            result.Errors.Add("[rules] 存在移动行动但未启用 movement 能力");
        if (def.Moves.Values.Any(m => m.NeedsCard) && !host.AllVariants.Any(v => v.Info.Provides.Contains("cards")))
            result.Errors.Add("[rules] 存在出牌行动 (needsCard) 但未启用 cards 能力");
    }

    private static void ValidateExpressions(GameDefinition def, ModuleHost host, PackageResult result, NameContract contract)
    {
        void Check(string where, string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return;
            ExpressionParser.INode node;
            try
            {
                node = ExpressionParser.Parse(src);
                foreach (var fn in ExpressionParser.CollectCallNames(node))
                    if (!host.HasFunction(fn))
                        result.Errors.Add($"[expr] {where}: 使用了未知函数 '{fn}'");
                foreach (var lint in ExpressionParser.Lint(node))
                    result.Warnings.Add($"[expr] {where}: {lint}");
            }
            catch (ExprException ex)
            {
                result.Errors.Add($"[expr] {where}: {ex.Message}");
                return;
            }
            contract.Check(ExpressionParser.CollectRefs(node), where, result.Warnings);
        }

        foreach (var (id, m) in def.Moves)
        {
            Check($"{id}.counterFilter", m.CounterFilter!);
            Check($"{id}.targetFilter", m.TargetFilter!);
            foreach (var v in m.Validators) Check($"{id}.validators", v);
            if (m.Roll != null) Check($"{id}.roll.drm", m.Roll.Drm!);
            CheckEffects($"{id}.effects", m.Effects, Check);
            foreach (var (code, effs) in m.ResultEffects)
                CheckEffects($"{id}.resultEffects[{code}]", effs, Check);
        }
        foreach (var t in def.Triggers.Values)
            CheckEffects($"triggers[{t.On}]", t.Do, Check);
        CheckEffects("setup", def.Setup, Check);
        foreach (var (id, cdef) in host.Cards)
            CheckEffects($"card.{id}.effects", cdef.Effects, Check);
        foreach (var e in def.EndConditions)
            Check($"endConditions", e.When);
        foreach (var r in def.TurnReset)
            if (r.Value.StartsWith("expr:")) Check($"turnReset.{r.Attr}", r.Value.Substring(5));
        foreach (var (id, cbt) in host.CombatDefs)
        {
            Check($"combat.{id}.rowExpr", cbt.RowExpr);
            Check($"combat.{id}.colExpr", cbt.ColExpr);
            foreach (var s in cbt.Shifts) Check($"combat.{id}.shift", s);
        }
    }

    private static void CheckEffects(string where, List<EffectDef> effects, Action<string, string> check)
    {
        foreach (var e in effects)
        {
            check($"{where}.when", e.When);
            // e.Value is only an expression for these effects; setside stores a keyword ("front"/"back").
            if (e.Effect is "setattr" or "setvar" or "addvar" or "control" or "capture"
                or "steploss" or "damage" or "exit")
                check($"{where}.value", e.Value);
            check($"{where}.x", e.X);
            check($"{where}.y", e.Y);
            check($"{where}.text", e.Text);
            check($"{where}.player", e.Player);
        }
    }
}
