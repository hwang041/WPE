using System.Text;
using Wpe.Render;
using Wpe.Rules.Platform;

namespace Wpe.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string? shotPath = null, uitestPath = null;
        var positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--shot" && i + 1 < args.Length) shotPath = args[++i];
            else if (args[i] == "--uitest" && i + 1 < args.Length) uitestPath = args[++i];
            else positional.Add(args[i]);
        }

        var gameDir = positional.Count > 0
            ? Path.GetFullPath(positional[0])
            : ResolveGameDir();

        if (!Directory.Exists(gameDir))
        {
            MessageBox.Show($"找不到游戏包目录: {gameDir}\n用法: Wpe.App <games/xxx> [--shot out.png] [--uitest out.txt]", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var result = PackageLoader.Load(gameDir);
            if (!result.Ok)
            {
                MessageBox.Show(string.Join("\n", result.Errors), "游戏包校验失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var game = result.Game!;

            if (shotPath != null) { Shoot(game, shotPath); return; }
            if (uitestPath != null)
            {
                using var form = new MainForm(game.State, game.Engine, game.GameDir, headless: true);
                File.WriteAllText(Path.GetFullPath(uitestPath), form.SmokeTest(), Encoding.UTF8);
                return;
            }

            Application.Run(new MainForm(game.State, game.Engine, game.GameDir));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Headless board render to PNG (grid or point-to-point).</summary>
    private static void Shoot(LoadedGame game, string outPath)
    {
        var map = game.State.Map ?? throw new InvalidOperationException("该游戏没有地图");
        using var bmp = MapRenderer.Render(map, game.State.CountersOnBoard().ToList(), showHexNumbers: false,
            factions: game.Def.Factions, nodeTypes: game.Def.NodeTypes, territory: game.State.Territory);
        CounterRenderer.SavePng(bmp, Path.GetFullPath(outPath));
        Console.WriteLine($"shot saved: {Path.GetFullPath(outPath)} ({bmp.Width}x{bmp.Height})");
    }

    private static string ResolveGameDir()
    {
        var games = FindGamePackages();
        if (games.Count == 0) return Path.Combine(Directory.GetCurrentDirectory(), "games");
        if (games.Count == 1) return games[0];
        return PickGame(games);
    }

    /// <summary>Walk up from the exe directory to find the repo's games/ folder, then list playable packages.</summary>
    private static List<string> FindGamePackages()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var gamesDir = Path.Combine(dir.FullName, "games");
            if (Directory.Exists(gamesDir))
            {
                var list = Directory.GetDirectories(gamesDir)
                    .Where(g => File.Exists(Path.Combine(g, "game.json")))
                    .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (list.Count > 0) return list;
            }
            dir = dir.Parent;
        }
        return new List<string>();
    }

    private static string PickGame(List<string> games)
    {
        using var form = new Form
        {
            Text = "WPE — 选择游戏包",
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(380, 260),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };
        var list = new ListBox { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 11f) };
        foreach (var g in games) list.Items.Add(Path.GetFileName(g));
        list.SelectedIndex = 0;
        list.DoubleClick += (s, e) => { form.DialogResult = DialogResult.OK; };

        var ok = new Button { Text = "启动", Dock = DockStyle.Bottom, Height = 40, Font = new Font("Microsoft YaHei UI", 11f) };
        ok.Click += (s, e) => { form.DialogResult = DialogResult.OK; };
        form.Controls.Add(list);
        form.Controls.Add(ok);
        form.AcceptButton = ok;

        var idx = form.ShowDialog() == DialogResult.OK ? list.SelectedIndex : 0;
        return idx >= 0 && idx < games.Count ? games[idx] : games[0];
    }
}
