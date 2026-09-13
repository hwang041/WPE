namespace Wpe.Core.Model;

/// <summary>A single die result with its hand/dice index (for rendering and reporting).</summary>
public sealed record DieResult(int HandIndex, int DieIndex, int Sides, int Value);

/// <summary>Deterministic, seedable RNG so games can be reproduced and replayed.</summary>
public sealed class SeededRandom
{
    private readonly Random _rng;
    public int Seed { get; }

    public SeededRandom(int seed)
    {
        Seed = seed;
        _rng = new Random(seed);
    }

    public int RollDie(int sides) => _rng.Next(1, sides + 1);

    public int[] RollDice(int count, int sides)
    {
        var results = new int[count];
        for (int i = 0; i < count; i++) results[i] = RollDie(sides);
        return results;
    }
}
