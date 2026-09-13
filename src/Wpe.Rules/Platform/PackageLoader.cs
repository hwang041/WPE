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
/// files), assembles the selected rule variants, validates everything, and hands back
/// a running engine. This is where "fill tables -> play" happens.
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

        // 2) resolve variant selection (family preset + per-subsystem overrides)
        var selection = ResolveSelection(def);

        // 3) instantiate variants
        var host = new ModuleHost();
        CoreFunctions.Seed(host);
        foreach (var subsystem in new[] { "map", "counter", "movement", "combat", "dice", "turn", "victory", "scenario" })
        {
            if (!selection.TryGetValue(subsystem, out var sel)) continue;
            var variant = DefaultFamilies.Create(subsystem, sel.Variant);
            if (variant == null)
            {
                result.Errors.Add($"[rules] 未知变体 '{sel.Variant}'（子系统 {subsystem}）");
                continue;
            }
            host.AddVariant(variant);
            try
            {
                variant.Load(ReadConfig(sel, gameDir, result), def);
                variant.Register(host);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"[{subsystem}/{sel.Variant}] 配置加载失败: {ex.Message}");
            }
        }

        // 4) composition checks
        CheckComposition(def, selection, host, result);

        // 5) per-variant validation
        foreach (var v in host.AllVariants)
        {
            var sub = v.Info.Subsystem;
            try { v.Validate(def, result.Errors); }
            catch (Exception ex) { result.Errors.Add($"[{sub}/{v.Info.Id}] 校验异常: {ex.Message}"); }
        }

        // 6) expression validation (all moves/triggers/endconditions + combat tables)
        ValidateExpressions(def, host, result);

        if (result.Errors.Count > 0) return result;

        // 7) build state
        try
        {
            var state = new GameState(def) { Map = host.MapData };
            foreach (var sub in DefaultFamilies.ApplyOrder)
                host.GetVariant(sub)?.Apply(state, null);

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

    private static Dictionary<string, VariantSelection> ResolveSelection(GameDefinition def)
    {
        var preset = DefaultFamilies.Presets.TryGetValue(def.Family, out var p)
            ? p
            : DefaultFamilies.Presets[DefaultFamilies.Default];
        var selection = new Dictionary<string, VariantSelection>();
        foreach (var (sub, id) in preset)
            selection[sub] = new VariantSelection { Variant = id };
        foreach (var (sub, sel) in def.Rules)
            selection[sub] = sel;
        return selection;
    }

    private static string? ReadConfig(VariantSelection sel, string gameDir, PackageResult result)
    {
        if (sel.Config != null)
            return JsonSerializer.Serialize(sel.Config, JsonUtil.Opts);
        if (string.IsNullOrEmpty(sel.File)) return null;
        var path = Path.Combine(gameDir, sel.File);
        if (!File.Exists(path))
        {
            result.Errors.Add($"找不到配置文件 {sel.File}");
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

    private static void CheckComposition(GameDefinition def, Dictionary<string, VariantSelection> selection,
        ModuleHost host, PackageResult result)
    {
        foreach (var (sub, sel) in selection)
        {
            var v = host.GetVariant(sub);
            if (v == null) continue;
            foreach (var need in v.Info.Requires)
                if (!selection.ContainsKey(need))
                    result.Errors.Add($"[rules] 变体 {sub}/{v.Info.Id} 依赖子系统 '{need}'，但未启用");
        }

        if (def.Moves.Values.Any(m => m.Roll != null) && host.Dice == null)
            result.Errors.Add("[rules] 存在掷骰行动但未启用 dice 子系统");
        if (def.Moves.Values.Any(m => !string.IsNullOrEmpty(m.Combat)) && host.Combat == null)
            result.Errors.Add("[rules] 存在战斗行动但未启用 combat 子系统");
        if (def.Moves.Values.Any(m => m.Kind == "movement") && host.Movement == null)
            result.Errors.Add("[rules] 存在移动行动但未启用 movement 子系统");
        if (def.EndConditions.Count > 0 && host.Victory == null)
            result.Warnings.Add("[rules] 配置了 endConditions 但未启用 victory 子系统（使用内核回退）");
    }

    private static void ValidateExpressions(GameDefinition def, ModuleHost host, PackageResult result)
    {
        void Check(string where, string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return;
            try
            {
                var node = ExpressionParser.Parse(src);
                foreach (var fn in ExpressionParser.CollectCallNames(node))
                    if (!host.HasFunction(fn))
                        result.Errors.Add($"[expr] {where}: 使用了未知函数 '{fn}'");
            }
            catch (ExprException ex)
            {
                result.Errors.Add($"[expr] {where}: {ex.Message}");
            }
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
        foreach (var e in def.EndConditions)
            Check($"endConditions", e.When);
        foreach (var r in def.TurnReset)
            if (r.Value.StartsWith("expr:")) Check($"turnReset.{r.Attr}", r.Value.Substring(5));
        foreach (var (id, cbt) in host.CombatDefs)
        {
            Check($"combat.{id}.rowExpr", cbt.RowExpr);
            Check($"combat.{id}.colExpr", cbt.ColExpr);
        }
    }

    private static void CheckEffects(string where, List<EffectDef> effects, Action<string, string> check)
    {
        foreach (var e in effects)
        {
            check($"{where}.when", e.When);
            check($"{where}.value", e.Value);
            check($"{where}.x", e.X);
            check($"{where}.y", e.Y);
            check($"{where}.text", e.Text);
        }
    }
}
