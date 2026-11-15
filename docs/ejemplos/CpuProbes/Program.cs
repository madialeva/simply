// CPU-side probes behind the figures in docs/revision-inicial.md (appendix A.2).
//
// Run from the repository root:
//
//   dotnet run -c Release --project docs/ejemplos/CpuProbes
//
// Timings come from a single run and are meant for order-of-magnitude comparisons,
// not as a rigorous benchmark. The project is not part of Simply.slnx.

using System.Diagnostics;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Simply.Examples.CpuProbes;

internal static unsafe partial class Program
{
    private const nuint CacheLineBytes = 64;

    private static void Main()
    {
        PrintSimdSurface();
        ProbeFlawedArena();
        ProbeGemv(rows: 4864, cols: 896, warmups: 50, iterations: 300);
        ProbeGemv(rows: 128, cols: 896, warmups: 2000, iterations: 20_000);
        ProbeHalfConversion();
        ProbeGcTransitionCost();
    }

    /// <summary>
    /// Lists the x86 intrinsic classes this runtime exposes and the vector widths accelerated on this CPU.
    /// </summary>
    private static void PrintSimdSurface()
    {
        IEnumerable<string> classes = typeof(Avx2).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(Avx2).Namespace && type is { IsPublic: true, IsClass: true })
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);

        Console.WriteLine($"[simd] x86 intrinsic classes: {string.Join(", ", classes)}");
        Console.WriteLine(
            $"[simd] Vector<float>.Count={Vector<float>.Count} " +
            $"Vector256={Vector256.IsHardwareAccelerated} Vector512={Vector512.IsHardwareAccelerated} " +
            $"Avx2={Avx2.IsSupported} Fma={Fma.IsSupported} Avx512F={Avx512F.IsSupported}");
    }

    /// <summary>
    /// Runs <see cref="FlawedArena"/> to show its three defects.
    /// </summary>
    private static void ProbeFlawedArena()
    {
        // 1. The byte count is computed in int arithmetic and overflows before the cast.
        int count = 600_000_000;
        nuint byteCount = (nuint)(count * sizeof(float));
        Console.WriteLine($"[arena] (nuint)(600_000_000 * sizeof(float)) = {byteCount} (expected 2400000000)");

        // 2. A by-value copy carries its own offset, so the original and the copy hand out the same memory.
        var arena = new FlawedArena(1024);
        FlawedArena copy = arena;
        Span<float> first = arena.Alloc<float>(16);
        Span<float> second = copy.Alloc<float>(16);
        Console.WriteLine($"[arena] original and copy return the same address: {Unsafe.AreSame(ref first[0], ref second[0])}");

        // 3. Nothing checks the capacity. The span points outside the arena, so it is never touched here.
        Span<float> outOfBounds = arena.Alloc<float>(100_000);
        Console.WriteLine($"[arena] 1024-byte arena, Alloc<float>(100_000) returned Length={outOfBounds.Length} without error");

        arena.Dispose();
    }

    /// <summary>
    /// Row-wise GEMV with TensorPrimitives.Dot: single thread versus Parallel.For, with managed bytes allocated per call.
    /// </summary>
    private static void ProbeGemv(int rows, int cols, int warmups, int iterations)
    {
        float* weights = (float*)NativeMemory.AlignedAlloc((nuint)rows * (nuint)cols * sizeof(float), CacheLineBytes);
        float* input = (float*)NativeMemory.AlignedAlloc((nuint)cols * sizeof(float), CacheLineBytes);
        float* output = (float*)NativeMemory.AlignedAlloc((nuint)rows * sizeof(float), CacheLineBytes);
        new Span<float>(weights, rows * cols).Fill(0.01f);
        new Span<float>(input, cols).Fill(0.5f);

        for (int i = 0; i < warmups; i++)
        {
            GemvSingleThread(weights, input, output, rows, cols);
            GemvParallelFor(weights, input, output, rows, cols);
        }

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            GemvSingleThread(weights, input, output, rows, cols);
        }

        double singleMicroseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
        double singleBytesPerCall = (GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / (double)iterations;

        int gen0Before = GC.CollectionCount(0);
        allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            GemvParallelFor(weights, input, output, rows, cols);
        }

        double parallelMicroseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
        double parallelBytesPerCall = (GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / (double)iterations;
        int gen0Collections = GC.CollectionCount(0) - gen0Before;

        Console.WriteLine(
            $"[gemv {rows}x{cols}] single thread: {singleMicroseconds:F1} us, {singleBytesPerCall:F0} B/call | " +
            $"Parallel.For: {parallelMicroseconds:F1} us, {parallelBytesPerCall:F0} B/call, {gen0Collections} gen0 GCs");

        NativeMemory.AlignedFree(weights);
        NativeMemory.AlignedFree(input);
        NativeMemory.AlignedFree(output);
    }

    private static void GemvSingleThread(float* weights, float* input, float* output, int rows, int cols)
    {
        var inputSpan = new ReadOnlySpan<float>(input, cols);
        for (int row = 0; row < rows; row++)
        {
            output[row] = TensorPrimitives.Dot(new ReadOnlySpan<float>(weights + ((nint)row * cols), cols), inputSpan);
        }
    }

    private static void GemvParallelFor(float* weights, float* input, float* output, int rows, int cols)
    {
        // A span cannot be captured by a lambda, so each row rebuilds its spans from the raw pointers.
        Parallel.For(0, rows, row =>
        {
            output[row] = TensorPrimitives.Dot(
                new ReadOnlySpan<float>(weights + ((nint)row * cols), cols),
                new ReadOnlySpan<float>(input, cols));
        });
    }

    /// <summary>
    /// FP16 to FP32 conversion throughput, next to an FP32 dot product for scale.
    /// </summary>
    private static void ProbeHalfConversion()
    {
        const int Count = 1 << 22;
        const int Iterations = 50;
        Half* halves = (Half*)NativeMemory.AlignedAlloc((nuint)Count * (nuint)sizeof(Half), CacheLineBytes);
        float* singles = (float*)NativeMemory.AlignedAlloc((nuint)Count * sizeof(float), CacheLineBytes);
        var halfSpan = new ReadOnlySpan<Half>(halves, Count);
        var singleSpan = new Span<float>(singles, Count);
        for (int i = 0; i < Count; i++)
        {
            halves[i] = (Half)(i % 97 * 0.01f);
        }

        for (int i = 0; i < 20; i++)
        {
            TensorPrimitives.ConvertToSingle(halfSpan, singleSpan);
            ConvertScalar(halves, singles, Count);
        }

        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Iterations; i++)
        {
            TensorPrimitives.ConvertToSingle(halfSpan, singleSpan);
        }

        double vectorized = MillionsPerSecond(Count, Iterations, start);

        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Iterations; i++)
        {
            ConvertScalar(halves, singles, Count);
        }

        double scalar = MillionsPerSecond(Count, Iterations, start);

        float sink = 0f;
        var readOnlySingles = new ReadOnlySpan<float>(singles, Count);
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Iterations; i++)
        {
            sink += TensorPrimitives.Dot(readOnlySingles, readOnlySingles);
        }

        double dot = MillionsPerSecond(Count, Iterations, start);

        Console.WriteLine(
            $"[fp16] ConvertToSingle: {vectorized:F0} M elements/s | scalar (float)Half: {scalar:F0} M elements/s | " +
            $"FP32 Dot: {dot:F0} M elements/s (sink {sink:E2})");

        NativeMemory.AlignedFree(halves);
        NativeMemory.AlignedFree(singles);
    }

    private static void ConvertScalar(Half* source, float* destination, int count)
    {
        for (int i = 0; i < count; i++)
        {
            destination[i] = (float)source[i];
        }
    }

    private static double MillionsPerSecond(int elements, int iterations, long start)
    {
        return elements / (Stopwatch.GetElapsedTime(start).TotalSeconds / iterations) / 1e6;
    }

    /// <summary>
    /// Cost of the GC transition on a trivial native call, with and without SuppressGCTransition.
    /// </summary>
    private static void ProbeGcTransitionCost()
    {
        const int Calls = 200_000_000;
        int sink = 0;
        for (int i = 0; i < 1_000_000; i++)
        {
            sink += Abs(-i);
            sink += AbsNoGcTransition(-i);
        }

        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Calls; i++)
        {
            sink += Abs(-i);
        }

        double regular = Stopwatch.GetElapsedTime(start).TotalNanoseconds / Calls;

        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Calls; i++)
        {
            sink += AbsNoGcTransition(-i);
        }

        double suppressed = Stopwatch.GetElapsedTime(start).TotalNanoseconds / Calls;

        Console.WriteLine(
            $"[pinvoke] abs(): {regular:F2} ns/call | with SuppressGCTransition: {suppressed:F2} ns/call | " +
            $"saved: {regular - suppressed:F2} ns (sink {sink})");
    }

    [LibraryImport("libc", EntryPoint = "abs")]
    private static partial int Abs(int value);

    [LibraryImport("libc", EntryPoint = "abs")]
    [SuppressGCTransition]
    private static partial int AbsNoGcTransition(int value);
}

/// <summary>
/// A first draft of NativeArena with three defects, kept unchanged on purpose so <see cref="Program"/> can show them
/// (docs/revision-inicial.md, section 2.3). Do not reuse it: the corrected version is in
/// docs/engine-inferencia-roadmap.md.
/// </summary>
internal unsafe struct FlawedArena : IDisposable
{
#pragma warning disable IDE0044 // Part of the flawed draft: these fields are not marked readonly.
    private byte* _basePtr;
    private nuint _capacity;
#pragma warning restore IDE0044
    private nuint _offset;

    public FlawedArena(nuint capacityBytes)
    {
        _basePtr = (byte*)NativeMemory.AlignedAlloc(capacityBytes, 64);
        _capacity = capacityBytes;
        _offset = 0;
    }

    public Span<T> Alloc<T>(int count)
        where T : unmanaged
    {
        nuint byteCount = (nuint)(count * sizeof(T));
        nuint alignedOffset = (_offset + 63) & ~((nuint)63);
        void* ptr = _basePtr + alignedOffset;
        _offset = alignedOffset + byteCount;
        return new Span<T>(ptr, count);
    }

    public void Reset() => _offset = 0;

    public void Dispose() => NativeMemory.AlignedFree(_basePtr);
}
