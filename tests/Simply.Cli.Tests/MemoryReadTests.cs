using Simply.Benchmarks.Memory;

namespace Simply.Cli.Tests;

public class MemoryReadTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(1000)]
    [InlineData(4099)]
    public void Sum_ReadsEveryElement(int length)
    {
        // Small integers add up exactly in float, so the expected sum is exact.
        float[] block = Enumerable.Range(1, length).Select(i => (float)(i % 8)).ToArray();
        float expected = block.Sum();

        Assert.Equal(expected, MemoryRead.Sum(block));
    }

    [Fact]
    public void Sum_DependsOnTheLastElement()
    {
        float[] block = new float[1001];
        block[^1] = 5f;

        Assert.Equal(5f, MemoryRead.Sum(block));
    }
}
