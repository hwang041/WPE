using SkiaSharp;
using Wpe.Core.Definition;
using Wpe.Core.Model;

namespace Wpe.Render;

/// <summary>
/// Paints a point-to-point map (spacemap.json) onto a canvas in board coordinates:
/// land, decorative lines (rivers), roads (major solid / minor dashed) and nodes
/// coloured by their controlling faction with type glyphs and defense badges.
/// All game-specific look comes from data (game.json `factions` / `nodeTypes`), so
/// this renderer stays game-agnostic. Shares the dark-board palette and halo-text
/// treatment of the hex grid painter.
/// </summary>
public static class SpaceMapPainter
{
    private static readonly SKColor Neutral = new(0xEC, 0xEC, 0xEC);

    /// <summary>Build the colour legend from the game's faction palette (plus a neutral entry).</summary>
    public static IReadOnlyList<(string label, SKColor color)> BuildLegend(IReadOnlyDictionary<string, FactionDef>? factions)
    {
        var list = new List<(string, SKColor)>();
        if (factions != null)
            foreach (var (key, fd) in factions)
                list.Add((string.IsNullOrEmpty(fd.Label) ? key : fd.Label, ParseColor(fd.Color, Neutral)));
        list.Add(("中立", Neutral));
        return list;
    }

    public static void Draw(SKCanvas canvas, SpaceMap map, IReadOnlyList<CounterState>? counters = null,
        IReadOnlyDictionary<string, string>? control = null,
        IReadOnlyDictionary<string, FactionDef>? factions = null,
        IReadOnlyDictionary<string, NodeTypeDef>? nodeTypes = null,
        bool showNames = true)
    {
        var area = map.TotalArea;
        var skArea = new SKRect(area.Left, area.Top, area.Right, area.Bottom);
        float R = map.NodeRadius;
        using var typeface = CounterRenderer.CjkTypeface();

        using (var land = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(30, 42, 34) })
            canvas.DrawRoundRect(skArea, R * 0.6f, R * 0.6f, land);
        var inner = new SKRect(skArea.Left + 10, skArea.Top + 10, skArea.Right - 10, skArea.Bottom - 10);
        using (var plate = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, skArea.Top), new SKPoint(0, skArea.Bottom),
                new[] { new SKColor(44, 60, 46), new SKColor(32, 46, 36) },
                null, SKShaderTileMode.Clamp)
        })
            canvas.DrawRoundRect(inner, R * 0.5f, R * 0.5f, plate);
        using (var edge = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 3f, Color = new SKColor(20, 30, 24) })
            canvas.DrawRoundRect(skArea, R * 0.6f, R * 0.6f, edge);

        DrawLines(canvas, map);
        DrawRoads(canvas, map);
        DrawNodes(canvas, map, counters, control, factions, nodeTypes, typeface, showNames);
    }

    private static void DrawLines(SKCanvas canvas, SpaceMap map)
    {
        foreach (var line in map.Lines)
        {
            if (line.Points.Count < 2) continue;
            using var path = new SKPath();
            for (int i = 0; i < line.Points.Count; i++)
            {
                var p = line.Points[i];
                if (p.Length < 2) continue;
                if (i == 0) path.MoveTo(p[0], p[1]);
                else path.LineTo(p[0], p[1]);
            }
            if (line.Style == "river")
            {
                using var outer = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = line.Width, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = new SKColor(38, 78, 120, 235), IsAntialias = true };
                canvas.DrawPath(path, outer);
                using var inner = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = line.Width * 0.55f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = new SKColor(70, 140, 210, 235), IsAntialias = true };
                canvas.DrawPath(path, inner);
            }
            else
            {
                using var pen = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = line.Width, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = new SKColor(120, 120, 120, 200), IsAntialias = true };
                canvas.DrawPath(path, pen);
            }
        }
    }

    private static void DrawRoads(SKCanvas canvas, SpaceMap map)
    {
        float R = map.NodeRadius;
        using var outer = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = R * 0.30f, StrokeCap = SKStrokeCap.Round, Color = new SKColor(58, 48, 34) };
        foreach (var e in map.Edges)
        {
            var a = map.CenterOf(e.A);
            var b = map.CenterOf(e.B);
            canvas.DrawLine(a.X, a.Y, b.X, b.Y, outer);
        }
        foreach (var e in map.Edges)
        {
            var a = map.CenterOf(e.A);
            var b = map.CenterOf(e.B);
            bool major = e.Type == "major";
            using var inner = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = major ? R * 0.16f : R * 0.09f,
                StrokeCap = SKStrokeCap.Round,
                Color = major ? new SKColor(0xDC, 0xBE, 0x94) : new SKColor(0xC0, 0xAE, 0x86, 220)
            };
            if (!major) inner.PathEffect = SKPathEffect.CreateDash(new[] { R * 0.30f, R * 0.22f }, 0);
            canvas.DrawLine(a.X, a.Y, b.X, b.Y, inner);
        }
    }

    private static void DrawNodes(SKCanvas canvas, SpaceMap map, IReadOnlyList<CounterState>? counters,
        IReadOnlyDictionary<string, string>? control, IReadOnlyDictionary<string, FactionDef>? factions,
        IReadOnlyDictionary<string, NodeTypeDef>? nodeTypes, SKTypeface typeface, bool showNames)
    {
        float R = map.NodeRadius;
        using var glyph = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 255, 255, 235), IsAntialias = true };
        using var glyphStroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = R * 0.10f, StrokeCap = SKStrokeCap.Round, Color = new SKColor(255, 255, 255, 235), IsAntialias = true };
        using var namePaint = new SKPaint { Color = new SKColor(255, 255, 255, 240), TextSize = R * 0.42f, IsAntialias = true, Typeface = typeface, TextAlign = SKTextAlign.Center, FakeBoldText = true };
        using var halo = new SKPaint { Color = new SKColor(0, 0, 0, 210), TextSize = R * 0.42f, IsAntialias = true, Typeface = typeface, TextAlign = SKTextAlign.Center, FakeBoldText = true };
        using var badgeText = new SKPaint { Color = new SKColor(20, 20, 20), TextSize = R * 0.34f, IsAntialias = true, Typeface = typeface, TextAlign = SKTextAlign.Center, FakeBoldText = true };

        for (int i = 0; i < map.Nodes.Count; i++)
        {
            var n = map.Nodes[i];
            var cell = new HexCoord(i, 0);
            var type = n.AttrStr("type", "");
            var style = nodeTypes != null && nodeTypes.TryGetValue(type, out var st) ? st : new NodeTypeDef();
            var def = n.AttrInt("defense", 0);
            float nr = R * (style.Scale > 0 ? style.Scale : 1f);
            var center = new SKPoint(n.X, n.Y);
            var fill = NodeColor(map, cell, counters, control, factions);

            using (var shadow = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 90) })
                canvas.DrawCircle(center.X + 2, center.Y + 3, nr, shadow);

            var top = Lighten(fill, 0.28f);
            var bottom = Darken(fill, 0.30f);
            using (var body = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(center.X, center.Y - nr), new SKPoint(center.X, center.Y + nr),
                    new[] { top, bottom }, null, SKShaderTileMode.Clamp)
            })
            using (var ring = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = MathF.Max(2f, R * 0.10f), Color = new SKColor(26, 30, 26), IsAntialias = true })
            using (var gloss = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = MathF.Max(1f, R * 0.05f), Color = new SKColor(255, 255, 255, 60), IsAntialias = true })
            {
                if (style.Shape == "gate")
                {
                    var path = DiamondPath(center.X, center.Y, nr);
                    canvas.DrawPath(path, body);
                    canvas.DrawPath(path, ring);
                }
                else
                {
                    canvas.DrawCircle(center, nr, body);
                    canvas.DrawCircle(center, nr, ring);
                    canvas.DrawArc(new SKRect(center.X - nr * 0.72f, center.Y - nr * 0.72f, center.X + nr * 0.72f, center.Y + nr * 0.72f), 200, 140, false, gloss);
                }
            }

            DrawGlyph(canvas, style.Shape, center, nr, glyph, glyphStroke);

            if (def > 0)
            {
                var bx = center.X + nr * 0.78f;
                var by = center.Y - nr * 0.78f;
                using var badgeBg = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 236, 170), IsAntialias = true };
                using var badgeRing = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = new SKColor(60, 50, 20), IsAntialias = true };
                canvas.DrawCircle(bx, by, R * 0.32f, badgeBg);
                canvas.DrawCircle(bx, by, R * 0.32f, badgeRing);
                canvas.DrawText(def.ToString(), bx, by + R * 0.12f, badgeText);
            }

            if (showNames)
            {
                var text = string.IsNullOrEmpty(style.Short) ? n.Name : $"{n.Name} {style.Short}";
                var ny = center.Y + nr + R * 0.48f;
                canvas.DrawText(text, center.X + 1, ny + 1, halo);
                canvas.DrawText(text, center.X, ny, namePaint);
            }
        }
    }

    private static void DrawGlyph(SKCanvas canvas, string shape, SKPoint c, float nr, SKPaint fill, SKPaint stroke)
    {
        float s = nr * 0.5f;
        switch (shape)
        {
            case "castle":
            {
                using var p = new SKPath();
                p.MoveTo(c.X - s, c.Y + s);
                p.LineTo(c.X - s, c.Y - s * 0.2f);
                p.LineTo(c.X - s * 0.45f, c.Y - s * 0.2f);
                p.LineTo(c.X - s * 0.45f, c.Y - s * 0.9f);
                p.LineTo(c.X + s * 0.45f, c.Y - s * 0.9f);
                p.LineTo(c.X + s * 0.45f, c.Y - s * 0.2f);
                p.LineTo(c.X + s, c.Y - s * 0.2f);
                p.LineTo(c.X + s, c.Y + s);
                p.Close();
                canvas.DrawPath(p, fill);
                break;
            }
            case "gate":
            {
                using var p = new SKPath();
                p.MoveTo(c.X - s * 0.8f, c.Y + s);
                p.LineTo(c.X - s * 0.8f, c.Y - s * 0.3f);
                p.QuadTo(c.X, c.Y - s * 1.3f, c.X + s * 0.8f, c.Y - s * 0.3f);
                p.LineTo(c.X + s * 0.8f, c.Y + s);
                canvas.DrawPath(p, stroke);
                break;
            }
            case "anchor":
            {
                canvas.DrawLine(c.X, c.Y - s, c.X, c.Y + s, stroke);
                canvas.DrawLine(c.X - s * 0.7f, c.Y - s * 0.5f, c.X + s * 0.7f, c.Y - s * 0.5f, stroke);
                canvas.DrawArc(new SKRect(c.X - s * 0.9f, c.Y - s * 0.1f, c.X + s * 0.9f, c.Y + s * 1.1f), 20, 140, false, stroke);
                break;
            }
            default:
                canvas.DrawCircle(c.X, c.Y, nr * 0.16f, fill);
                break;
        }
    }

    /// <summary>Colour of a node: a unit on it wins (its resolved `color` / faction palette), else the persistent controller's palette colour, else neutral.</summary>
    private static SKColor NodeColor(SpaceMap map, HexCoord cell, IReadOnlyList<CounterState>? counters,
        IReadOnlyDictionary<string, string>? control, IReadOnlyDictionary<string, FactionDef>? factions)
    {
        if (counters != null)
            foreach (var c in counters)
                if (c.OnBoard && c.Hex == cell)
                {
                    var hex = c.AttributeStr("color", "");
                    if (!string.IsNullOrEmpty(hex) && SKColor.TryParse(hex, out var col)) return col;
                    var cf = c.AttributeStr("faction", "");
                    if (factions != null && factions.TryGetValue(cf, out var cfd)) return ParseColor(cfd.Color, Neutral);
                    return Neutral;
                }

        var id = map.NodeIdOf(cell);
        if (control != null && control.TryGetValue(id, out var f) && factions != null && factions.TryGetValue(f, out var fd))
            return ParseColor(fd.Color, Neutral);
        return Neutral;
    }

    /// <summary>Draw stacked counters fanned out per node (matches the GUI hit-testing offsets).</summary>
    public static void DrawCounters(SKCanvas canvas, SpaceMap map, IReadOnlyList<CounterState>? counters,
        float cellSize, float scale = 1f)
    {
        if (counters == null) return;
        float half = cellSize / 2f;
        var index = new Dictionary<HexCoord, int>();
        foreach (var c in counters.Where(c => c.OnBoard).OrderBy(c => c.Id))
        {
            index.TryGetValue(c.Hex, out var i);
            index[c.Hex] = i + 1;
            var center = map.CenterOf(c.Hex);
            var ox = i * cellSize * 0.20f;
            var oy = i * cellSize * 0.32f;
            var rect = new SKRect(center.X - half + ox, center.Y - half + oy, center.X + half + ox, center.Y + half + oy);
            using var chip = CounterRenderer.Render(c, new CounterRenderOptions { Width = 96, Height = 96 }, back: c.IsBack);
            canvas.DrawBitmap(chip, rect);
        }
    }

    private static SKColor ParseColor(string hex, SKColor fallback)
        => !string.IsNullOrEmpty(hex) && SKColor.TryParse(hex, out var c) ? c : fallback;

    private static SKPath DiamondPath(float cx, float cy, float r)
    {
        var p = new SKPath();
        p.MoveTo(cx, cy - r);
        p.LineTo(cx + r, cy);
        p.LineTo(cx, cy + r);
        p.LineTo(cx - r, cy);
        p.Close();
        return p;
    }

    private static SKColor Lighten(SKColor c, float t) => new(
        (byte)Math.Clamp(c.Red + (255 - c.Red) * t, 0, 255),
        (byte)Math.Clamp(c.Green + (255 - c.Green) * t, 0, 255),
        (byte)Math.Clamp(c.Blue + (255 - c.Blue) * t, 0, 255), c.Alpha);

    private static SKColor Darken(SKColor c, float t) => new(
        (byte)(c.Red * (1 - t)), (byte)(c.Green * (1 - t)), (byte)(c.Blue * (1 - t)), c.Alpha);
}
