namespace Wpe.Core.Model;

/// <summary>Axial hex coordinates (cube projection).</summary>
public readonly record struct HexCoord(int Q, int R)
{
    public static HexCoord Of(object? q, object? r)
        => new(Convert.ToInt32(q), Convert.ToInt32(r));
}

public static class HexMath
{
    /// <summary>Grid constant used everywhere (CenterOf + corners) so a vertex shared by
    /// adjacent hexes is computed to the same float value from any of them.</summary>
    public static readonly float SQRT3 = MathF.Sqrt(3);

    public static readonly (int dq, int dr)[] PointyDirections =
        { (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1) };

    public static readonly (int dq, int dr)[] FlatDirections =
        { (1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1) };

    /// <summary>
    /// The six corner offsets (relative to a hex center), derived from the same grid
    /// basis as <c>CenterOf</c> — NOT from trig — so a corner that belongs to two
    /// adjacent hexes is bit-identical whichever hex computes it.
    /// </summary>
    public static (float x, float y)[] CornerOffsets(float R, bool pointyTop)
    {
        if (pointyTop)
        {
            var a = R * SQRT3;
            return new (float, float)[]
            {
                ( a / 2f,  R / 2f),   // 30°
                (    0f,        R),   // 90°
                (-a / 2f,  R / 2f),   // 150°
                (-a / 2f, -R / 2f),   // 210°
                (    0f,       -R),   // 270°
                ( a / 2f, -R / 2f),   // 330°
            };
        }
        var b = R * SQRT3;
        return new (float, float)[]
        {
            (    R,     0f),   // 0°
            ( R / 2f,  b / 2f),   // 60°
            (-R / 2f,  b / 2f),   // 120°
            (   -R,     0f),   // 180°
            (-R / 2f, -b / 2f),   // 240°
            ( R / 2f, -b / 2f),   // 300°
        };
    }

    public static int Distance(HexCoord a, HexCoord b)
    {
        var dq = a.Q - b.Q;
        var dr = a.R - b.R;
        var ds = -a.Q - a.R + b.Q + b.R;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(ds)) / 2;
    }

    public static IEnumerable<HexCoord> Neighbors(HexCoord a, bool pointyTop)
    {
        foreach (var (dq, dr) in pointyTop ? PointyDirections : FlatDirections)
            yield return new HexCoord(a.Q + dq, a.R + dr);
    }
}
