using SkiaSharp;
using System.Drawing;
using Wpe.Core.Model;

namespace Wpe.Render;

/// <summary>
/// Paints a data-driven hex grid map (terrain, forest/mountain/town features, rivers,
/// victory markers, hex numbers) onto a canvas in board coordinates. Shared by the GUI
/// (interactive pan/zoom) and the `wpe overlay` quick-generation renderer.
/// </summary>
public static class GridMapPainter
{
    /// <summary>
    /// Draw the grid in board coordinates. The caller sets canvas.Scale/Translate.
    /// `lineScale`/`textScale` keep strokes/text a constant screen size under zoom
    /// (GUI passes 1/Scale; the overlay renderer passes 1).
    /// </summary>
    public static void DrawGrid(SKCanvas canvas, GridMap map, bool showHexNumbers = false, float lineScale = 1f, float textScale = 1f)
    {
        float R = map.HexRadius;
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f * lineScale, Color = new SKColor(35, 48, 35, 190) };
        using var fill = new SKPaint { Style = SKPaintStyle.Fill };

        for (int r = 0; r < map.Rows; r++)
        {
            for (int q = 0; q < map.Columns; q++)
            {
                var hex = new HexCoord(q, r);
                var c = map.CenterOf(hex);
                var path = HexPath(c.X, c.Y, R, map.PointyTop);
                fill.Color = TerrainColor(map.TerrainAt(hex), q, r);
                canvas.DrawPath(path, fill);

                if (showHexNumbers)
                {
                    using var halo = new SKPaint { Color = new SKColor(0, 0, 0, 200), TextSize = 10f * textScale, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center };
                    using var num = new SKPaint { Color = new SKColor(255, 255, 255, 240), TextSize = 10f * textScale, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center };
                    var label = (r * map.Columns + q).ToString();
                    var ny = c.Y + R * 0.62f;
                    canvas.DrawText(label, c.X + 1, ny + 1, halo);
                    canvas.DrawText(label, c.X, ny, num);
                }

                if (map.IsVictoryHex(hex))
                {
                    using var gold = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 215, 90, 70) };
                    canvas.DrawPath(path, gold);
                }

                DrawTerrainFeatures(canvas, map, hex, c.X, c.Y, R);
                canvas.DrawPath(path, stroke);
            }
        }

        DrawRivers(canvas, map);

        using var starPaint = new SKPaint { IsAntialias = true };
        foreach (var v in map.VictoryHexes)
        {
            var c = map.CenterOf(new HexCoord(v.Q, v.R));
            DrawVictoryMarker(canvas, c.X, c.Y, R * 0.62f);
        }
    }

    public static SKPath HexPath(float cx, float cy, float R, bool pointy)
    {
        var off = HexMath.CornerOffsets(R, pointy);
        var path = new SKPath();
        for (int i = 0; i < 6; i++)
        {
            var x = cx + off[i].x;
            var y = cy + off[i].y;
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
        path.Close();
        return path;
    }

    /// <summary>
    /// Draw rivers as the actual hex-grid edges: each river edge is the hex side shared
    /// by its two hexes (<see cref="GridMap.SharedEdge"/>), stroked as a thick round-capped
    /// segment. Consecutive edges share a vertex, so rivers follow the grid exactly —
    /// continuous at every join, fork and confluence, with no gaps. Fords are overlaid
    /// as tan crossing marks.
    /// </summary>
    public static void DrawRivers(SKCanvas canvas, GridMap map)
    {
        var edges = map.RiverEdgeList().ToList();
        if (edges.Count == 0) return;

        using var river = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = map.HexRadius * 0.34f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = new SKColor(70, 140, 210, 230),
            IsAntialias = true
        };

        foreach (var e in edges)
        {
            var s = map.SharedEdge(new HexCoord(e.qA, e.rA), new HexCoord(e.qB, e.rB));
            if (s is null) continue;
            canvas.DrawLine(s.Value.P1.X, s.Value.P1.Y, s.Value.P2.X, s.Value.P2.Y, river);
        }

        // fords: tan crossing marks overlaid on the river edge
        foreach (var e in edges)
            if (e.ford)
                DrawFordTick(canvas, map, e);
    }

    /// <summary>Number of connected river components, by shared-vertex connectivity
    /// (1 = the whole map's water is one connected network).</summary>
    public static int RiverChainCount(GridMap map) => RiverComponentSizes(map).Count;

    /// <summary>Sizes of each connected river component (edges per network).</summary>
    public static List<int> RiverComponentSizes(GridMap map)
    {
        var edges = map.RiverEdgeList().ToList();
        if (edges.Count == 0) return new List<int>();

        var vToE = new Dictionary<(float, float), List<int>>();
        for (int i = 0; i < edges.Count; i++)
        {
            var s = map.SharedEdge(new HexCoord(edges[i].qA, edges[i].rA), new HexCoord(edges[i].qB, edges[i].rB));
            if (s is null) continue;
            AddVertex(vToE, s.Value.P1, i);
            AddVertex(vToE, s.Value.P2, i);
        }

        var adj = new List<HashSet<int>>(edges.Count);
        for (int i = 0; i < edges.Count; i++) adj.Add(new HashSet<int>());
        foreach (var l in vToE.Values)
            foreach (var a in l)
                foreach (var b in l)
                    if (a != b) adj[a].Add(b);

        var sizes = new List<int>();
        var seen = new bool[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            if (seen[i]) continue;
            int comp = 0;
            var stack = new Stack<int>();
            stack.Push(i);
            seen[i] = true;
            while (stack.Count > 0)
            {
                var u = stack.Pop();
                comp++;
                foreach (var v in adj[u])
                    if (!seen[v]) { seen[v] = true; stack.Push(v); }
            }
            sizes.Add(comp);
        }
        return sizes;
    }

    private static void AddVertex(Dictionary<(float, float), List<int>> map, PointF p, int idx)
    {
        // hex-grid vertices are HexRadius apart (>= 78u), so nearest-integer keys are exact
        // yet immune to float rounding between adjacent-hex computations.
        var key = (MathF.Round(p.X), MathF.Round(p.Y));
        if (!map.TryGetValue(key, out var l)) { l = new List<int>(); map[key] = l; }
        l.Add(idx);
    }

    /// <summary>Draw a ford as a short tan tick across the river edge at its midpoint.</summary>
    private static void DrawFordTick(SKCanvas canvas, GridMap map, (int qA, int rA, int qB, int rB, bool ford) e)
    {
        var a = map.CenterOf(new HexCoord(e.qA, e.rA));
        var b = map.CenterOf(new HexCoord(e.qB, e.rB));
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1f) return;
        var px = -dy / len; var py = dx / len;
        var mx = (a.X + b.X) / 2f; var my = (a.Y + b.Y) / 2f;
        var hw = map.HexRadius * 0.42f;
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = map.HexRadius * 0.40f,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(190, 150, 90, 235),
            IsAntialias = true
        };
        canvas.DrawLine(mx - px * hw, my - py * hw, mx + px * hw, my + py * hw, paint);
    }

    private static SKColor TerrainColor(string t, int q, int r)
    {
        var hash = (q * 31 + r * 17) % 11 - 5;
        return t switch
        {
            "F" => Shade(0x57824A, hash),
            "M" => Shade(0x9A8C74, hash),
            "R" => Shade(0x6FA8DC, hash),
            "T" => Shade(0xDCBE94, hash),
            "W" => Shade(0x4E7FAE, hash),
            _ => Shade(0xB2C98A, hash)
        };
    }

    private static SKColor Shade(int rgb, int d)
    {
        var r = ((rgb >> 16) & 0xFF) + d;
        var g = ((rgb >> 8) & 0xFF) + d;
        var b = (rgb & 0xFF) + d;
        return new SKColor((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
    }

    private static void DrawTerrainFeatures(SKCanvas canvas, GridMap map, HexCoord hex, float cx, float cy, float R)
    {
        switch (map.TerrainAt(hex))
        {
            case "F":
            {
                using var dot = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(38, 66, 34) };
                canvas.DrawCircle(cx - R * 0.28f, cy + R * 0.15f, R * 0.16f, dot);
                canvas.DrawCircle(cx + R * 0.25f, cy - R * 0.18f, R * 0.20f, dot);
                canvas.DrawCircle(cx + R * 0.05f, cy + R * 0.42f, R * 0.14f, dot);
                using var dot2 = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(74, 116, 66) };
                canvas.DrawCircle(cx - R * 0.08f, cy - R * 0.28f, R * 0.12f, dot2);
                break;
            }
            case "M":
            {
                using var peak = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(120, 106, 92) };
                canvas.DrawPath(Peak(cx - R * 0.22f, cy + R * 0.20f, R * 0.34f), peak);
                canvas.DrawPath(Peak(cx + R * 0.24f, cy + R * 0.10f, R * 0.42f), peak);
                using var snow = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(238, 234, 226) };
                canvas.DrawPath(Snow(cx - R * 0.22f, cy + R * 0.20f, R * 0.34f), snow);
                canvas.DrawPath(Snow(cx + R * 0.24f, cy + R * 0.10f, R * 0.42f), snow);
                break;
            }
            case "T":
            {
                using var building = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(160, 128, 88) };
                var s = R * 0.30f;
                canvas.DrawRect(new SKRect(cx - s, cy - s * 0.4f, cx, cy + s * 0.6f), building);
                canvas.DrawRect(new SKRect(cx + s * 0.05f, cy - s * 0.25f, cx + s, cy + s * 0.5f), building);
                using var roof = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(126, 88, 56) };
                canvas.DrawRect(new SKRect(cx - s, cy - s * 0.5f, cx + s, cy - s * 0.35f), roof);
                break;
            }
        }
    }

    private static SKPath Peak(float cx, float cy, float s)
    {
        var p = new SKPath();
        p.MoveTo(cx - s, cy + s * 0.7f);
        p.LineTo(cx, cy - s * 0.8f);
        p.LineTo(cx + s, cy + s * 0.7f);
        p.Close();
        return p;
    }

    private static SKPath Snow(float cx, float cy, float s)
    {
        var p = new SKPath();
        p.MoveTo(cx - s * 0.22f, cy - s * 0.05f);
        p.LineTo(cx, cy - s * 0.8f);
        p.LineTo(cx + s * 0.22f, cy - s * 0.05f);
        p.Close();
        return p;
    }

    private static void DrawVictoryMarker(SKCanvas canvas, float cx, float cy, float s)
    {
        using var star = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 205, 60), IsAntialias = true };
        var path = new SKPath();
        for (int i = 0; i < 10; i++)
        {
            double ang = Math.PI / 2 - i * Math.PI / 5;
            var rad = i % 2 == 0 ? s : s * 0.45f;
            var x = cx + rad * (float)Math.Cos(ang);
            var y = cy - rad * (float)Math.Sin(ang);
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
        path.Close();
        canvas.DrawPath(path, star);
        path.Dispose();
    }
}
