using OpenSkyAtc.Core.Random;

namespace OpenSkyAtc.Core.Tests;

public class SeededRandomTests
{
    [Fact]
    public void SameSeed_GivesSameSequence()
    {
        var a = new SeededRandom(123);
        var b = new SeededRandom(123);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.Next(0, 1000), b.Next(0, 1000));
    }

    [Fact]
    public void KnownSeed_GivesFixedSequence()
    {
        // Pins the algorithm: if this changes, every approved transcript changes too.
        var rng = new SeededRandom(42);
        Assert.Equal([13, 91, 58], new[] { rng.Next(0, 100), rng.Next(0, 100), rng.Next(0, 100) });
    }

    [Fact]
    public void Next_StaysInRange_AndCoversIt()
    {
        var rng = new SeededRandom(7);
        var seen = new HashSet<int>();
        for (int i = 0; i < 2000; i++)
        {
            int v = rng.Next(-3, 4);
            Assert.InRange(v, -3, 3);
            seen.Add(v);
        }
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void NextDouble_IsInUnitInterval()
    {
        var rng = new SeededRandom(9);
        for (int i = 0; i < 1000; i++) Assert.InRange(rng.NextDouble(), 0.0, 0.9999999999);
    }

    [Fact]
    public void Next_RejectsEmptyRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeededRandom(1).Next(5, 5));
    }
}
