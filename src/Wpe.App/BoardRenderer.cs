using SkiaSharp;
using Wpe.Core.Model;
using Wpe.Render;

namespace Wpe.App;

/// <summary>
/// Renders the battlefield for the interactive GUI: the data-driven hex grid map
/// (drawn by the shared Wpe.Render.GridMapPainter) with whiteboard counters on top,
/// plus pan/zoom, selection/acted highlighting and the terrain legend.
/// </summary>
public sealed class BoardRenderer
{
    private readonly GameState _state;
    private readonly Dictionary<(int id, bool back), SKBitmap> _chips = new();

    public float Scale { get; set; } = 0.05f;
    public float PanX { get; set; }
    public float PanY { get; set; }
    public bool ShowHexGrid { get; set; } = true;
    public bool ShowHexNumbers { get; set; }

    public BoardRenderer(GameState state) => _state = state;

    /// <summary>On-screen-board size of a counter (square, relative to the hex).</summary>
    public float CounterSize => (_state.Map?.HexRadius ?? 80f) * 1.15f;

    private SKBitmap Chip(CounterState c)
    {
        var key = (c.Id, c.IsBack);
        if (_chips.TryGetValue(key, out var b)) return b;
        b = CounterRenderer.Render(c, new CounterRenderOptions { Width = 96, Height = 96 }, back: c.IsBack);
        _chips[key] = b;
        return b;
    }

    // ---------- draw ----------

    public void Draw(SKCanvas canvas, int viewW, int viewH)
    {
        canvas.Clear(new SKColor(18, 22, 20));
        canvas.Save();
        canvas.Scale(Scale);
        canvas.Translate(-PanX, -PanY);

        var map = _state.Map;
        if (map != null)
            GridMapPainter.DrawGrid(canvas, map, ShowHexNumbers, 1f / Scale, 1f / Scale);
        DrawCounters(canvas);

        canvas.Restore();
        if (map != null) DrawLegend(canvas, viewW, viewH);
    }

    private void DrawCounters(SKCanvas canvas)
    {
        if (_state.Map == null) return;
        var size = CounterSize;
        var half = size / 2f;
        using var edge = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 3f / Scale, Color = new SKColor(255, 200, 0) };
        using var selEdge = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 4f / Scale, Color = new SKColor(80, 220, 255) };
        using var shadow = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 70) };

        foreach (var c in _state.CountersOnBoard())
        {
            var center = _state.Map.CenterOf(c.Hex);
            var rect = new SKRect(center.X - half, center.Y - half, center.X + half, center.Y + half);

            canvas.Save();
            if (c.RotationDeg != 0) canvas.RotateDegrees(c.RotationDeg, center.X, center.Y);

            canvas.DrawOval(new SKRect(center.X - half * 0.5f, center.Y + half * 0.9f, center.X + half * 0.5f, center.Y + half * 1.1f), shadow);
            var bmp = Chip(c); // cached — do NOT dispose
            canvas.DrawBitmap(bmp, rect, new SKPaint { FilterQuality = SKFilterQuality.Medium });

            if (c.AttributeInt("acted", 0) == 1) canvas.DrawRect(rect, edge);
            if (c.AttributeInt("selected", 0) == 1) canvas.DrawRect(rect, selEdge);
            canvas.Restore();
        }
    }

    private void DrawLegend(SKCanvas canvas, int w, int h)
    {
        int x = w - 130, y = h - 120;
        var items = new (string code, string label, SKColor color)[]
        {
            ("P", "平地", new SKColor(0xB2C98A)),
            ("F", "森林", new SKColor(0x57824A)),
            ("M", "山地", new SKColor(0x9A8C74)),
            ("R", "河流", new SKColor(0x6FA8DC)),
            ("T", "城镇", new SKColor(0xDCBE94)),
        };
        using var bg = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 150) };
        canvas.DrawRoundRect(new SKRect(x - 8, y - 8, w - 8, h - 2), 8, 8, bg);
        using var text = new SKPaint { Color = SKColors.White, TextSize = 12, IsAntialias = true };
        canvas.DrawText("图例", x, y, text);
        int yy = y + 16;
        foreach (var it in items)
        {
            using var sw = new SKPaint { Style = SKPaintStyle.Fill, Color = it.color };
            canvas.DrawRect(new SKRect(x, yy, x + 14, yy + 11), sw);
            canvas.DrawText(it.label, x + 18, yy + 11, text);
            yy += 16;
        }
        canvas.DrawText("★ 胜利点", x, yy + 4, text);
    }

    /// <summary>Screen -> board units.</summary>
    public PointF ScreenToBoard(float sx, float sy)
        => new((sx / Scale) + PanX, (sy / Scale) + PanY);

    /// <summary>Board units -> screen.</summary>
    public (float x, float y) BoardToScreen(float bx, float by)
        => ((bx - PanX) * Scale, (by - PanY) * Scale);
}
