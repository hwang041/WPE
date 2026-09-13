using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wpe.Core.Model;
using Wpe.Render;
using Wpe.Rules;
using Wpe.Rules.Platform;

namespace Wpe.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "play" => Play(args),
            "verify" => Verify(args),
            "rule" => Rule(args),
            "counters" => Counters(args),
            "overlay" => Overlay(args),
            "river" => River(args),
            "new" => NewGame(args),
            "help" or "--help" or "-h" => Help(),
            _ => Unknown(args)
        };
    }

    private static int Help()
    {
        PrintUsage();
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("WPE — Wargame Pipeline Engine");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  wpe verify <游戏包目录>   校验游戏包（配置/表达式/变体组合），报错带位置");
        Console.WriteLine("  wpe play   <游戏包目录>   无头跑一局（双方自动行动），回归冒烟");
        Console.WriteLine("  wpe counters <游戏包目录> [输出目录]   批量生成白板算子 PNG（正面+受损面+总表）");
        Console.WriteLine("  wpe overlay <游戏包目录> [输出.png] [--nums]   渲染地图（地形/河流/胜利点/算子）PNG");
        Console.WriteLine("  wpe river  <游戏包目录> <\"q,r\" q,r ...> [--fords 0,2] [--append]   按六角格路径生成河流边");
        Console.WriteLine("  wpe rule list             列出规则库中可用的变体");
        Console.WriteLine("  wpe new    <名字>         在 games/ 下生成一个可填写的游戏包模板");
    }

    private static int Unknown(string[] args)
    {
        Console.Error.WriteLine($"未知命令: {args[0]}");
        PrintUsage();
        return 1;
    }

    // ---- verify ----

    private static int Verify(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("verify 需要一个游戏包目录"); return 1; }
        var dir = Path.GetFullPath(args[1]);
        var result = PackageLoader.Load(dir);

        Console.WriteLine($"== verify {dir} ==");
        if (result.Errors.Count == 0)
            Console.WriteLine("  配置校验: 通过");
        else
            foreach (var e in result.Errors) Console.WriteLine($"  [error] {e}");
        foreach (var w in result.Warnings) Console.WriteLine($"  [warn ] {w}");

        if (result.Game != null)
        {
            var g = result.Game;
            Console.WriteLine($"  游戏      : {g.Def.Name}");
            Console.WriteLine($"  变体      : {string.Join(", ", g.Host.AllVariants.Select(v => $"{v.Info.Subsystem}={v.Info.Id}"))}");
            Console.WriteLine($"  算子      : {g.State.Counters.Count} (战场上 {g.State.CountersOnBoard().Count()})");
        }
        return result.Ok ? 0 : 1;
    }

    // ---- play ----

    private static int Play(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("play 需要一个游戏包目录"); return 1; }
        var dir = Path.GetFullPath(args[1]);
        var result = PackageLoader.Load(dir);
        if (!result.Ok)
        {
            Console.Error.WriteLine($"游戏包校验失败: {dir}");
            foreach (var e in result.Errors) Console.Error.WriteLine($"  [error] {e}");
            return 1;
        }

        var game = result.Game!;
        var engine = game.Engine;
        var state = game.State;
        Console.WriteLine($"== {game.Def.Name} == phase={state.CurrentPhase} player={state.ActivePlayer + 1} turn={state.TurnNumber}");
        foreach (var c in state.CountersOnBoard().OrderBy(c => c.AttributeInt("owner", -1)).ThenBy(c => c.Id))
            Console.WriteLine($"  {c}");

        // scripted auto-play: attack when possible, else move toward the nearest enemy, else pass
        int guard = 0;
        while (!state.GameOver && state.TurnNumber <= 12 && guard < 600)
        {
            guard++;
            if (engine.GameOver) break;
            if (state.CurrentPhase != "action")
            {
                engine.Apply("endphase", null, null, null);
                continue;
            }
            var units = state.CountersOnBoard()
                .Where(c => c.AttributeInt("owner", -1) == state.ActivePlayer && !c.IsBack && c.AttributeInt("acted", 0) == 0)
                .ToList();
            var enemies = state.CountersOnBoard().Where(c => c.AttributeInt("owner", -1) != state.ActivePlayer).ToList();
            bool acted = false;
            foreach (var u in units)
            {
                var moves = engine.LegalMovesForCounter(u);
                var atk = moves.FirstOrDefault(m => m.Kind == "combat");
                if (atk != null)
                {
                    var targets = engine.TargetCountersForMove(atk, u);
                    if (targets.Count > 0)
                    {
                        engine.Apply(atk.Id, u, null, targets[0]);
                        acted = true; break;
                    }
                }
                var mv = moves.FirstOrDefault(m => m.Kind == "movement");
                if (mv != null)
                {
                    // step one hex at a time toward the nearest enemy (action economy: each Apply = one hex)
                    var map = state.Map;
                    var candidates = map == null
                        ? new List<HexCoord>()
                        : HexMath.Neighbors(u.Hex, map.PointyTop)
                            .Where(n => engine.CanApply(mv.Id, u, n, null, out _))
                            .ToList();
                    if (candidates.Count > 0)
                    {
                        var pick = enemies.Count > 0
                            ? candidates.OrderBy(h => enemies.Min(e => HexMath.Distance(h, e.Hex))).First()
                            : candidates[0];
                        engine.Apply(mv.Id, u, pick, null);
                        acted = true; break;
                    }
                }
                var pass = moves.FirstOrDefault(m => m.Kind == "pass");
                if (pass != null)
                {
                    engine.Apply(pass.Id, u, null, null);
                    acted = true; break;
                }
            }
            if (!acted) engine.Apply("endphase", null, null, null);
        }

        Console.WriteLine();
        Console.WriteLine($"结果: {(state.GameOver ? "游戏结束 - " + state.ResultMessage : $"达到上限 T{state.TurnNumber}，未决出胜负")}");
        Console.WriteLine($"回合 {state.TurnNumber} | 场上 {state.CountersOnBoard().Count()} 算子");
        Console.WriteLine();
        Console.WriteLine("--- LOG (末尾 30 条) ---");
        foreach (var l in state.Log.TakeLast(30)) Console.WriteLine("  " + l);
        return 0;
    }

    // ---- rule ----

    private static int Rule(string[] args)
    {
        if (args.Length < 2 || args[1].ToLowerInvariant() != "list")
        {
            Console.WriteLine("用法: wpe rule list");
            return 1;
        }
        Console.WriteLine("规则库变体 (子系统/变体/状态/说明):");
        foreach (var info in DefaultFamilies.List())
            Console.WriteLine($"  {info.Subsystem,-10} {info.Id,-14} {info.Status,-8} {info.Description}");
        return 0;
    }

    // ---- new ----

    private static int NewGame(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("new 需要一个游戏名"); return 1; }
        var name = args[1];
        var gamesDir = ResolveGamesDir();
        if (gamesDir == null) { Console.Error.WriteLine("找不到 games/ 目录（在 exe 上级目录中查找）"); return 1; }
        var dir = Path.Combine(gamesDir, name);
        if (Directory.Exists(dir)) { Console.Error.WriteLine($"目录已存在: {dir}"); return 1; }
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "game.json"), Template.GameJson(name), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "map.json"), Template.MapJson, Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "movement.json"), Template.MovementJson, Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "combat.json"), Template.CombatJson, Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "units.json"), Template.UnitsJson, Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "scenario.json"), Template.ScenarioJson, Encoding.UTF8);

        var result = PackageLoader.Load(dir);
        Console.WriteLine($"已生成游戏包: {dir}");
        Console.WriteLine(result.Ok
            ? "  模板校验: 通过 — 开始填表吧。"
            : "  模板校验发现问题:");
        foreach (var e in result.Errors) Console.WriteLine($"    [error] {e}");
        return result.Ok ? 0 : 1;
    }

    // ---- counters (batch whiteboard art production) ----

    private static int Counters(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("counters 需要一个游戏包目录"); return 1; }
        var dir = Path.GetFullPath(args[1]);
        var result = PackageLoader.Load(dir);
        if (!result.Ok)
        {
            foreach (var e in result.Errors) Console.Error.WriteLine($"  [error] {e}");
            return 1;
        }
        var game = result.Game!;
        var outDir = args.Length > 2 ? Path.GetFullPath(args[2]) : Path.Combine(game.GameDir, "art");
        Directory.CreateDirectory(outDir);

        var opts = new CounterRenderOptions();
        var counters = game.State.Counters;
        var sides = new List<(CounterState c, bool back)>();
        int n = 0;
        foreach (var c in counters)
        {
            var key = Sanitize(c.AttributeStr("key", c.Id.ToString()));
            CounterRenderer.SavePng(CounterRenderer.Render(c, opts), Path.Combine(outDir, $"{key}.png"));
            CounterRenderer.SavePng(CounterRenderer.Render(c, opts, back: true), Path.Combine(outDir, $"{key}_b.png"));
            sides.Add((c, false));
            sides.Add((c, true));
            n++;
        }
        CounterRenderer.SavePng(CounterRenderer.BuildSheet(sides, opts), Path.Combine(outDir, "sheet.png"));

        Console.WriteLine($"生成 {n} 个算子 × 正反面 = {n * 2} 张 PNG");
        Console.WriteLine($"目录: {outDir}");
        Console.WriteLine($"总表: {Path.Combine(outDir, "sheet.png")}");
        return 0;
    }

    private static string Sanitize(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return name;
    }

    // ---- overlay (quick map render for demo/verification) ----

    private static int Overlay(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("overlay 需要一个游戏包目录"); return 1; }
        var dir = Path.GetFullPath(args[1]);
        var result = PackageLoader.Load(dir);
        if (!result.Ok)
        {
            foreach (var e in result.Errors) Console.Error.WriteLine($"  [error] {e}");
            return 1;
        }
        var game = result.Game!;
        var map = game.State.Map;
        if (map == null) { Console.Error.WriteLine("该游戏没有网格地图"); return 1; }
        var outPath = args.Length > 2 ? Path.GetFullPath(args[2]) : Path.Combine(game.GameDir, "map-overlay.png");
        var nums = args.Contains("--nums");
        using var bmp = MapRenderer.Render(map, game.State.CountersOnBoard().ToList(), showHexNumbers: nums);
        CounterRenderer.SavePng(bmp, outPath);
        var sizes = GridMapPainter.RiverComponentSizes(map);
        Console.WriteLine($"overlay saved: {outPath} ({bmp.Width}x{bmp.Height}) riverEdges={map.RiverEdgeList().Count()} riverChains={sizes.Count} sizes=[{string.Join(",", sizes)}]");
        return 0;
    }

    // ---- river (quick generation of connected river edges from a hex path) ----

    private static int River(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("用法: wpe river <游戏包> <\"q,r\" q,r ...> [--fords 0,2] [--append]");
            return 1;
        }
        var dir = Path.GetFullPath(args[1]);
        var mapPath = Path.Combine(dir, "map.json");
        if (!File.Exists(mapPath)) { Console.Error.WriteLine($"缺少 map.json: {mapPath}"); return 1; }

        var toks = new List<string>();
        string? fordsArg = null;
        bool append = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--fords" && i + 1 < args.Length) fordsArg = args[++i];
            else if (args[i] == "--append") append = true;
            else toks.Add(args[i]);
        }

        var path = RiverPathGenerator.ParsePath(string.Join(" ", toks));
        if (path.Count < 2) { Console.Error.WriteLine("无法解析河流路径（需要至少两个 q,r 格）"); return 1; }

        var fords = new List<int>();
        if (fordsArg != null)
            foreach (var t in fordsArg.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(t, out var n)) fords.Add(n);

        List<RiverSegmentDef> segs;
        try { segs = RiverPathGenerator.Build(path, fords); }
        catch (InvalidDataException ex) { Console.Error.WriteLine(ex.Message); return 1; }

        var doc = JsonNode.Parse(File.ReadAllText(mapPath))!.AsObject();
        var newRivers = new JsonArray(new JsonObject { ["segments"] = SerializeSegments(segs) });
        if (append && doc["rivers"] is JsonArray existing)
            foreach (var r in existing) newRivers.Add(r!.DeepClone());
        doc["rivers"] = newRivers;
        File.WriteAllText(mapPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var result = PackageLoader.Load(dir);
        Console.WriteLine($"已生成河流（{path.Count} 格路径 → {segs.Count} 条边）: {mapPath}");
        if (!result.Ok)
        {
            Console.WriteLine("  生成后校验失败:");
            foreach (var e in result.Errors) Console.WriteLine("    [error] " + e);
            return 1;
        }
        Console.WriteLine("  校验: 通过");
        return 0;
    }

    private static JsonArray SerializeSegments(List<RiverSegmentDef> segs)
    {
        var arr = new JsonArray();
        foreach (var s in segs)
        {
            arr.Add(new JsonObject
            {
                ["a"] = new JsonArray(s.A[0], s.A[1]),
                ["b"] = new JsonArray(s.B[0], s.B[1]),
                ["ford"] = s.Ford
            });
        }
        return arr;
    }

    /// <summary>Walk up from the exe to find the repo's games/ folder.</summary>
    private static string? ResolveGamesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var games = Path.Combine(dir.FullName, "games");
            if (Directory.Exists(games)) return games;
            dir = dir.Parent;
        }
        return null;
    }
}
