using SkiaSharp;
using Wpe.Core.Model;

namespace Wpe.Render;

/// <summary>Rendering options for a whiteboard counter (square by default).</summary>
public sealed class CounterRenderOptions
{
    public int Width { get; set; } = 120;
    public int Height { get; set; } = 120;
    public float CornerRadius { get; set; } = 12f;
    public int SheetColumns { get; set; } = 6;
    public int SheetGap { get; set; } = 12;
}

/// <summary>
/// Whiteboard counter renderer: a faction-colored chip with the unit name and
/// its strength/move values — enough to play demos and mass-produce counters for
/// the testing stage, with no sheet artwork required. Shared by the GUI and the
/// `wpe counters` batch command.
/// </summary>
public static class CounterRenderer
{
    private static readonly Dictionary<string, SKColor> FactionColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = new SKColor(0xC0, 0x3A, 0x3A),
        ["blue"] = new SKColor(0x3A, 0x5C, 0xC0),
        ["green"] = new SKColor(0x3A, 0x8C, 0x4A),
        ["yellow"] = new SKColor(0xC9, 0xA2, 0x2E),
        ["purple"] = new SKColor(0x7A, 0x4A, 0xB2),
        ["orange"] = new SKColor(0xC8, 0x6A, 0x2E),
        ["teal"] = new SKColor(0x2E, 0x8C, 0x8C)
    };

    private static readonly SKColor[] OwnerPalette =
    {
        new(0xC0, 0x3A, 0x3A), new(0x3A, 0x5C, 0xC0), new(0x3A, 0x8C, 0x4A),
        new(0xC9, 0xA2, 0x2E), new(0x7A, 0x4A, 0xB2), new(0x2E, 0x8C, 0x8C)
    };

    public static SKColor FactionColor(CounterState c)
    {
        var f = c.AttributeStr("faction");
        if (FactionColors.TryGetValue(f, out var col)) return col;
        var owner = c.AttributeInt("owner", -1);
        return owner >= 0 && owner < OwnerPalette.Length
            ? OwnerPalette[owner]
            : new SKColor(0x8A, 0x8A, 0x8A);
    }

    /// <summary>Render one counter to a bitmap. `back` draws the damaged (reverse) side.</summary>
    public static SKBitmap Render(CounterState c, CounterRenderOptions? opts = null, bool back = false)
    {
        opts ??= new CounterRenderOptions();
        int W = opts.Width, H = opts.Height;
        var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        var baseColor = FactionColor(c);
        var bg = back ? Darken(Desaturate(baseColor, 0.4f), 0.5f) : baseColor;

        var rr = new SKRoundRect(new SKRect(1, 1, W - 1, H - 1), opts.CornerRadius, opts.CornerRadius);
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bg, IsAntialias = true };
        canvas.DrawRoundRect(rr, bgPaint);

        using var border = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = SKColors.White, IsAntialias = true };
        canvas.DrawRoundRect(rr, border);

        // damaged stripe (back side)
        if (back)
        {
            using var stripe = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 5, Color = new SKColor(0, 0, 0, 90), IsAntialias = true };
            canvas.DrawLine(W * 0.12f, H * 0.88f, W * 0.88f, H * 0.12f, stripe);
        }

        // name, top center (auto-shrink to fit)
        var name = c.Name;
        if (name.Length > 0)
        {
            var namePaint = TextPaint(17f, SKColors.White, bold: true);
            var nameY = H * 0.34f;
            FitText(canvas, namePaint, name, W - 14f, W * 0.5f, nameY);
        }

        // values: strength | move in two bottom boxes
        var strength = (int)Math.Round(c.AttributeFloat("strength"));
        var move = (int)Math.Round(c.AttributeFloat("move"));
        var boxH = 26f;
        var boxY = H - boxH - 8f;
        DrawValueBox(canvas, new SKRect(10, boxY, W / 2f - 4, boxY + boxH), strength);
        DrawValueBox(canvas, new SKRect(W / 2f + 4, boxY, W - 10, boxY + boxH), move);

        return bmp;
    }

    private static void DrawValueBox(SKCanvas canvas, SKRect rect, int value)
    {
        using var bg = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 255, 255, 215), IsAntialias = true };
        var rr = new SKRoundRect(rect, 5, 5);
        canvas.DrawRoundRect(rr, bg);

        var text = value.ToString();
        var paint = TextPaint(16f, new SKColor(0x20, 0x20, 0x20), bold: true);
        var w = paint.MeasureText(text);
        canvas.DrawText(text, rect.MidX - w / 2f, rect.MidY + paint.TextSize * 0.36f, paint);
    }

    /// <summary>Render all counters into one contact sheet (front sides).</summary>
    public static SKBitmap BuildSheet(IEnumerable<CounterState> counters, CounterRenderOptions? opts = null)
        => BuildSheet(counters.Select(c => (c, false)), opts);

    /// <summary>Render counter sides into one contact sheet.</summary>
    public static SKBitmap BuildSheet(IEnumerable<(CounterState c, bool back)> counters, CounterRenderOptions? opts = null)
    {
        opts ??= new CounterRenderOptions();
        var list = counters.ToList();
        if (list.Count == 0) return new SKBitmap(10, 10);

        int cols = Math.Max(1, opts.SheetColumns);
        int rows = (int)Math.Ceiling(list.Count / (double)cols);
        int W = cols * opts.Width + (cols + 1) * opts.SheetGap;
        int H = rows * opts.Height + (rows + 1) * opts.SheetGap;

        var sheet = new SKBitmap(W, H);
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(0x24, 0x2A, 0x28));
        for (int i = 0; i < list.Count; i++)
        {
            using var chip = Render(list[i].c, opts, list[i].back);
            int x = opts.SheetGap + (i % cols) * (opts.Width + opts.SheetGap);
            int y = opts.SheetGap + (i / cols) * (opts.Height + opts.SheetGap);
            canvas.DrawBitmap(chip, x, y);
        }
        return sheet;
    }

    /// <summary>Encode a bitmap as PNG bytes.</summary>
    public static byte[] EncodePng(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static void SavePng(SKBitmap bmp, string path)
        => File.WriteAllBytes(path, EncodePng(bmp));

    // ---- helpers ----

    private static SKTypeface? _cjk;

    private static SKTypeface CjkTypeface()
    {
        if (_cjk != null) return _cjk;
        foreach (var name in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Source Han Sans SC" })
        {
            var tf = SKTypeface.FromFamilyName(name);
            if (tf != null && !tf.FamilyName.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                _cjk = tf;
                return tf;
            }
        }
        _cjk = SKTypeface.Default;
        return _cjk;
    }

    private static SKPaint TextPaint(float size, SKColor color, bool bold)
        => new()
        {
            Typeface = CjkTypeface(),
            TextSize = size,
            Color = color,
            IsAntialias = true,
            FakeBoldText = bold
        };

    private static void FitText(SKCanvas canvas, SKPaint paint, string text, float maxWidth, float centerX, float baselineY)
    {
        var w = paint.MeasureText(text);
        if (w > maxWidth)
        {
            paint.TextSize *= maxWidth / w;
            w = paint.MeasureText(text);
        }
        canvas.DrawText(text, centerX - w / 2f, baselineY, paint);
    }

    private static SKColor Darken(SKColor c, float t) => new(
        (byte)(c.Red * (1 - t)), (byte)(c.Green * (1 - t)), (byte)(c.Blue * (1 - t)), c.Alpha);

    private static SKColor Desaturate(SKColor c, float t)
    {
        byte l = (byte)(c.Red * 0.3f + c.Green * 0.59f + c.Blue * 0.11f);
        return new SKColor(
            (byte)(c.Red + (l - c.Red) * t),
            (byte)(c.Green + (l - c.Green) * t),
            (byte)(c.Blue + (l - c.Blue) * t),
            c.Alpha);
    }
}
