using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Simply.Benchmarks.Memory;

/// <summary>
/// The work measured by the memory suite: one pass that reads every element of a block.
/// </summary>
public static class MemoryRead
{
    /// <summary>
    /// Sums every element of a block with 256-bit vectors.
    /// </summary>
    /// <remarks>
    /// Two independent accumulators let consecutive loads overlap instead of waiting for the previous addition.
    /// The result depends on every element, so the JIT cannot drop any load.
    /// </remarks>
    /// <param name="block">The block to read.</param>
    /// <returns>The sum of the elements.</returns>
    public static float Sum(ReadOnlySpan<float> block)
    {
        ref float start = ref MemoryMarshal.GetReference(block);
        nuint length = (nuint)block.Length;
        nuint step = (nuint)Vector256<float>.Count * 2;
        var first = Vector256<float>.Zero;
        var second = Vector256<float>.Zero;
        nuint i = 0;
        for (; i + step <= length; i += step)
        {
            first += Vector256.LoadUnsafe(ref start, i);
            second += Vector256.LoadUnsafe(ref start, i + (nuint)Vector256<float>.Count);
        }

        float sum = Vector256.Sum(first + second);
        for (; i < length; i++)
        {
            sum += block[(int)i];
        }

        return sum;
    }
}
