namespace Simply.Benchmarks.Memory;

/// <summary>
/// A block size for the memory suite, labeled with the memory level it is meant to fit in.
/// </summary>
/// <param name="Bytes">The size of the block in bytes.</param>
/// <param name="Label">The name shown in the results table.</param>
public readonly record struct BlockSize(long Bytes, string Label) : IComparable<BlockSize>, IComparable
{
    /// <summary>
    /// Fits in the 512 KiB L2 cache of one core of the development machine.
    /// </summary>
    public static BlockSize L2 { get; } = new(256L * 1024, "256 KiB (L2)");

    /// <summary>
    /// Fits in the 16 MiB L3 cache shared by all cores of the development machine.
    /// </summary>
    public static BlockSize L3 { get; } = new(8L * 1024 * 1024, "8 MiB (L3)");

    /// <summary>
    /// Only fits in main memory.
    /// </summary>
    public static BlockSize Ram { get; } = new(1024L * 1024 * 1024, "1 GiB (RAM)");

    /// <summary>
    /// Orders block sizes from smallest to largest, so the results table goes from L2 to RAM.
    /// </summary>
    /// <param name="other">The block size to compare with.</param>
    /// <returns>A negative number, zero or a positive number when this block is smaller, equal or larger.</returns>
    public int CompareTo(BlockSize other) => Bytes.CompareTo(other.Bytes);

    /// <inheritdoc />
    public int CompareTo(object? obj) => obj is BlockSize other ? CompareTo(other) : 1;

    /// <summary>Compares two block sizes by their size in bytes.</summary>
    /// <param name="left">The first block size.</param>
    /// <param name="right">The second block size.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is smaller.</returns>
    public static bool operator <(BlockSize left, BlockSize right) => left.CompareTo(right) < 0;

    /// <summary>Compares two block sizes by their size in bytes.</summary>
    /// <param name="left">The first block size.</param>
    /// <param name="right">The second block size.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is smaller or equal.</returns>
    public static bool operator <=(BlockSize left, BlockSize right) => left.CompareTo(right) <= 0;

    /// <summary>Compares two block sizes by their size in bytes.</summary>
    /// <param name="left">The first block size.</param>
    /// <param name="right">The second block size.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is larger.</returns>
    public static bool operator >(BlockSize left, BlockSize right) => left.CompareTo(right) > 0;

    /// <summary>Compares two block sizes by their size in bytes.</summary>
    /// <param name="left">The first block size.</param>
    /// <param name="right">The second block size.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is larger or equal.</returns>
    public static bool operator >=(BlockSize left, BlockSize right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => Label;
}
