using Wpe.Rules.Platform;

namespace Wpe.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var gameDir = args.Length > 0
            ? Path.GetFullPath(args[0])
            : ResolveGameDir();

        if (!Directory.Exists(gameDir))
        {
            MessageBox.Show($"找不到游戏包目录: {gameDir}\n用法: Wpe.App <games/xxx>", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            Application.Run(new MainForm(game.State, game.Engine, game.GameDir));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
