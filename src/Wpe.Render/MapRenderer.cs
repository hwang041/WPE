using SkiaSharp;
using Wpe.Core.Model;

namespace Wpe.Render;

/// <summary>
/// Standalone map renderer for quick generation / verification (`wpe overlay`):
/// renders the full hex grid (terrain/rivers/victory/hex numbers) plus optional
/// counter chips to a PNG-ready bitmap, without any interactive GUI.
/// </summary>
public static class MapRenderer
{
    public static SKBitmap Render(GridMap map, IReadOnlyList<CounterState>? counters = null,
        bool showHexNumbers = true, int targetWidth = 1600)
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

        GridMapPainter.DrawGrid(canvas, map, showHexNumbers);

        if (counters != null)
        {
            var size = map.HexRadius * 1.15f;
            var half = size / 2f;
            foreach (var c in counters.Where(c => c.OnBoard))
            {
                var center = map.CenterOf(c.Hex);
                var rect = new SKRect(center.X - half, center.Y - half, center.X + half, center.Y + half);
                using var chip = CounterRenderer.Render(c, new CounterRenderOptions { Width = 96, Height = 96 }, back: c.IsBack);
                canvas.DrawBitmap(chip, rect);
            }
        }
        return bmp;
    }
}
