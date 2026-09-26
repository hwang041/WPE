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

    /// <summary>On-screen-board size of a counter (square, relative to the cell).</summary>
    public float CounterSize => (_state.Map?.CellRadius ?? 80f) * 1.15f;

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
        if (map is GridMap grid)
            GridMapPainter.DrawGrid(canvas, grid, ShowHexNumbers, 1f / Scale, 1f / Scale);
        else if (map is SpaceMap space)
            SpaceMapPainter.Draw(canvas, space, _state.CountersOnBoard().ToList(), _state.Territory,
                _state.Def.Factions, _state.Def.NodeTypes);
        DrawCounters(canvas);

        canvas.Restore();
        if (map is GridMap) DrawLegend(canvas, viewW, viewH);
        else if (map is SpaceMap) DrawSpaceLegend(canvas, viewW, viewH);
    }

    private void DrawCounters(SKCanvas canvas)
    {
        if (_state.Map == null) return;
        var size = CounterSize;
        var half = size / 2f;
        using var edge = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 3f / Scale, Color = new SKColor(255, 200, 0) };
        using var selEdge = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 4f / Scale, Color = new SKColor(80, 220, 255) };
        using var shadow = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 70) };

        var stackIndex = new Dictionary<HexCoord, int>();
        foreach (var c in _state.CountersOnBoard().OrderBy(c => c.Id))
        {
            stackIndex.TryGetValue(c.Hex, out var i);
            stackIndex[c.Hex] = i + 1;
            var center = _state.Map.CenterOf(c.Hex);
            float ox = 0, oy = 0;
            if (_state.Map is SpaceMap) { ox = i * size * 0.20f; oy = i * size * 0.32f; }
            var rect = new SKRect(center.X - half + ox, center.Y - half + oy, center.X + half + ox, center.Y + half + oy);

            canvas.Save();
            if (c.RotationDeg != 0) canvas.RotateDegrees(c.RotationDeg, center.X + ox, center.Y + oy);

            canvas.DrawOval(new SKRect(center.X - half * 0.5f + ox, center.Y + half * 0.9f + oy, center.X + half * 0.5f + ox, center.Y + half * 1.1f + oy), shadow);
            var bmp = Chip(c); // cached — do NOT dispose
            canvas.DrawBitmap(bmp, rect, new SKPaint { FilterQuality = SKFilterQuality.Medium });

            if (c.AttributeInt(_state.Def.ActedAttr, 0) == 1) canvas.DrawRect(rect, edge);
            if (c.AttributeInt(MainForm.SelectedAttr, 0) == 1) canvas.DrawRect(rect, selEdge);
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
        using var text = new SKPaint { Color = SKColors.White, TextSize = 12, IsAntialias = true, Typeface = CounterRenderer.CjkTypeface() };
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

    private void DrawSpaceLegend(SKCanvas canvas, int w, int h)
    {
        int x = w - 150, y = h - 150;
        using var bg = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 150) };
        canvas.DrawRoundRect(new SKRect(x - 8, y - 8, w - 8, h - 2), 8, 8, bg);
        using var text = new SKPaint { Color = SKColors.White, TextSize = 12, IsAntialias = true, Typeface = CounterRenderer.CjkTypeface() };
        canvas.DrawText("图例", x, y, text);
        int yy = y + 16;
        foreach (var (label, color) in SpaceMapPainter.BuildLegend(_state.Def.Factions))
        {
            using var sw = new SKPaint { Style = SKPaintStyle.Fill, Color = color };
            canvas.DrawCircle(x + 7, yy + 5, 6, sw);
            canvas.DrawText(label, x + 18, yy + 10, text);
            yy += 16;
        }
    }

    /// <summary>Screen -> board units.</summary>
    public PointF ScreenToBoard(float sx, float sy)
        => new((sx / Scale) + PanX, (sy / Scale) + PanY);

    /// <summary>Board units -> screen.</summary>
    public (float x, float y) BoardToScreen(float bx, float by)
        => ((bx - PanX) * Scale, (by - PanY) * Scale);
}
