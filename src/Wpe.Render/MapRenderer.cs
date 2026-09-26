using SkiaSharp;
using Wpe.Core.Definition;
using Wpe.Core.Model;

namespace Wpe.Render;

/// <summary>
/// Standalone map renderer for quick generation / verification (`wpe overlay`):
/// renders a hex grid (terrain/rivers/victory/hex numbers) or a point-to-point map
/// (nodes/roads/rivers) plus optional counter chips to a PNG-ready bitmap, without
/// any interactive GUI. Game-specific look is passed in via the faction/node-type palettes.
/// </summary>
public static class MapRenderer
{
    public static SKBitmap Render(IMap map, IReadOnlyList<CounterState>? counters = null,
        bool showHexNumbers = true, int targetWidth = 1600,
        IReadOnlyDictionary<string, FactionDef>? factions = null,
        IReadOnlyDictionary<string, NodeTypeDef>? nodeTypes = null,
        IReadOnlyDictionary<string, string>? territory = null)
    {
        var area = map.TotalArea;
        float scale = Math.Clamp(targetWidth / area.Width, 0.02f, 2f);
        int W = Math.Max(2, (int)(area.Width * scale));
        int H = Math.Max(2, (int)(area.Height * scale));
        var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(18, 22, 20));
        canvas.Scale(scale);
        canvas.Translate(-area.Left, -area.Top);

        if (map is GridMap grid)
        {
            GridMapPainter.DrawGrid(canvas, grid, showHexNumbers);
            if (counters != null)
            {
                var size = map.CellRadius * 1.15f;
                var half = size / 2f;
                foreach (var c in counters.Where(c => c.OnBoard))
                {
                    var center = map.CenterOf(c.Hex);
                    var rect = new SKRect(center.X - half, center.Y - half, center.X + half, center.Y + half);
                    using var chip = CounterRenderer.Render(c, new CounterRenderOptions { Width = 96, Height = 96 }, back: c.IsBack);
                    canvas.DrawBitmap(chip, rect);
                }
            }
        }
        else if (map is SpaceMap space)
        {
            SpaceMapPainter.Draw(canvas, space, counters, territory, factions, nodeTypes);
            if (counters != null)
                SpaceMapPainter.DrawCounters(canvas, space, counters, space.NodeRadius * 1.15f);
        }
        return bmp;
    }
}
