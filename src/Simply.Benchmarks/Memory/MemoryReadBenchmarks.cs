using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Simply.Benchmarks.Memory;

/// <summary>
/// Measures how fast the CPU reads a block that fits in L2, in L3 or only in RAM, with one thread and with all of them.
/// </summary>
public unsafe class MemoryReadBenchmarks
{
    private const nuint Alignment = 64;

    private float* _block;
    private int _length;
    private float[] _partialSums = [];

    /// <summary>
    /// Gets or sets the size of the block.
    /// </summary>
    [ParamsSource(nameof(BlockSizes))]
    public BlockSize Block { get; set; }

    /// <summary>
    /// Gets or sets how many threads read the block, each one a contiguous slice.
    /// </summary>
    [ParamsSource(nameof(ThreadCounts))]
    public int Threads { get; set; }

    /// <summary>
    /// Gets the block sizes to measure.
    /// </summary>
    public static IEnumerable<BlockSize> BlockSizes => [BlockSize.L2, BlockSize.L3, BlockSize.Ram];

    /// <summary>
    /// Gets the thread counts to measure: one, and every logical processor.
    /// </summary>
    public static IEnumerable<int> ThreadCounts => new[] { 1, Environment.ProcessorCount }.Distinct();

    /// <summary>
    /// Allocates the block outside the managed heap, aligned to a cache line, and fills it.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _length = checked((int)(Block.Bytes / sizeof(float)));
        _block = (float*)NativeMemory.AlignedAlloc((nuint)Block.Bytes, Alignment);
        new Span<float>(_block, _length).Fill(1f);
        _partialSums = new float[Threads];
    }

    /// <summary>
    /// Frees the block.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.AlignedFree(_block);
        _block = null;
    }

    /// <summary>
    /// Reads the whole block once.
    /// </summary>
    /// <returns>The sum of the block, so that no read can be dropped.</returns>
    [Benchmark]
    public float Read()
    {
        if (Threads == 1)
        {
            return MemoryRead.Sum(new ReadOnlySpan<float>(_block, _length));
        }

        Parallel.For(0, Threads, ReadSlice);
        float sum = 0;
        foreach (float partial in _partialSums)
        {
            sum += partial;
        }

        return sum;
    }

    private void ReadSlice(int thread)
    {
        int sliceLength = _length / Threads;
        int start = thread * sliceLength;
        int length = thread == Threads - 1 ? _length - start : sliceLength;
        _partialSums[thread] = MemoryRead.Sum(new ReadOnlySpan<float>(_block + start, length));
    }
}
