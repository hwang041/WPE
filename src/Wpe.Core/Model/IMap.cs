using System.Drawing;

namespace Wpe.Core.Model;

/// <summary>
/// A battle map: a set of cells addressed by <see cref="HexCoord"/>. Both the hex grid
/// (<see cref="GridMap"/>) and the point-to-point node network (<see cref="SpaceMap"/>)
/// implement it, so the engine, expressions and renderers can stay map-agnostic.
/// For a space map a "cell" is a node encoded as (nodeIndex, 0).
/// </summary>
public interface IMap
{
    /// <summary>Nominal cell radius in board units (hex radius / node radius) — used for counter sizing and highlights.</summary>
    float CellRadius { get; }

    /// <summary>Bounding rectangle of all cells in board units (with margin).</summary>
    RectangleF TotalArea { get; }

    bool InBounds(HexCoord c);

    /// <summary>Adjacent cells (hex neighbors / road-connected nodes).</summary>
    IReadOnlyList<HexCoord> Neighbors(HexCoord c);

    /// <summary>Step distance between two cells (hex distance / road hop count).</summary>
    int Distance(HexCoord a, HexCoord b);

    string TerrainAt(HexCoord c);

    float TerrainCostAt(HexCoord c);

    float TerrainDefenseAt(HexCoord c);

    bool IsVictoryHex(HexCoord c);

    /// <summary>Center of a cell in board units.</summary>
    PointF CenterOf(HexCoord c);

    /// <summary>Nearest cell to a board point.</summary>
    HexCoord CellAt(PointF p);

    /// <summary>
    /// Resolve a named cell (e.g. a point-to-point node id) to its cell. Grid maps have no
    /// named cells and return false, so scenario/rule code can stay map-agnostic.
    /// </summary>
    bool TryResolveCell(string key, out HexCoord cell);

    /// <summary>
    /// Stable string key for a cell, used by subsystems that persist per-cell data
    /// (e.g. control/ownership). Hex grids use "q,r"; point-to-point maps use the node id.
    /// </summary>
    string CellKey(HexCoord cell);
}
