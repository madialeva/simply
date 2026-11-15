// Checks on a real GPU the matrix-vector recipes quoted in docs/engine-inferencia-roadmap.md:
//
//   1. cuBLAS over a row-major matrix (the layout GGUF uses): FP32 with cublasSgemv, FP16 with cublasGemmEx.
//   2. A custom kernel that multiplies 4-bit block-quantized weights, dequantizing on the fly.
//
// Every result is compared with a CPU reference computed in double precision.
//
// Requirements: an NVIDIA GPU with its driver, and the CUDA Toolkit (nvcc, cuBLAS).
// Run from the repository root:
//
//   nvcc -ptx -O3 docs/ejemplos/GpuGemvRecipes/kernels.cu -o docs/ejemplos/GpuGemvRecipes/kernels.ptx
//   dotnet run -c Release --project docs/ejemplos/GpuGemvRecipes -- docs/ejemplos/GpuGemvRecipes/kernels.ptx
//
// Timings come from a single run and are meant for order-of-magnitude comparisons,
// not as a rigorous benchmark. The project is not part of Simply.slnx.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Simply.Examples.GpuGemvRecipes;

internal static unsafe class Program
{
    // Shape of the feed-forward up-projection of Qwen2.5-0.5B: y = W x with W of n_out x n_in.
    private const int Rows = 4864;
    private const int Cols = 896;
    private const int BlockWeights = 32;
    private const int TimingIterations = 2000;

    private static int Main(string[] args)
    {
        string ptxPath = args.Length > 0 ? args[0] : "kernels.ptx";
        if (!File.Exists(ptxPath))
        {
            Console.Error.WriteLine($"PTX file not found: {ptxPath}. Compile kernels.cu with nvcc first (see the header of this file).");
            return 1;
        }

        Check(CudaDriver.cuInit(0));
        Check(CudaDriver.cuDeviceGet(out int device, 0));
        Check(CudaDriver.cuDevicePrimaryCtxRetain(out nint context, device));
        Check(CudaDriver.cuCtxSetCurrent(context));
        Check(CudaDriver.cuModuleLoad(out nint module, ptxPath));
        Check(CudaDriver.cuModuleGetFunction(out nint gemvQ4, module, "gemv_q4"));
        Check(Cublas.cublasCreate_v2(out nint cublas));

        // Row-major weights, as stored in a GGUF file: element (row, col) lives at row * Cols + col.
        var random = new Random(1234);
        float[] weights = new float[Rows * Cols];
        float[] input = new float[Cols];
        FillGaussian(random, weights, 0.02f);
        FillGaussian(random, input, 1f);

        Half[] weightsHalf = Array.ConvertAll(weights, value => (Half)value);
        Half[] inputHalf = Array.ConvertAll(input, value => (Half)value);
        float[] weightsFromHalf = Array.ConvertAll(weightsHalf, value => (float)value);
        float[] inputFromHalf = Array.ConvertAll(inputHalf, value => (float)value);

        var blocks = new BlockQ4[Rows * Cols / BlockWeights];
        QuantizeQ4(weights, blocks);
        float[] weightsFromQ4 = new float[Rows * Cols];
        DequantizeQ4(blocks, weightsFromQ4);

        double[] referenceFp32 = ReferenceGemv(weights, input);
        double[] referenceFp16 = ReferenceGemv(weightsFromHalf, inputFromHalf);
        double[] referenceQ4 = ReferenceGemv(weightsFromQ4, inputFromHalf);

        ulong deviceWeights32 = Upload<float>(weights);
        ulong deviceInput32 = Upload<float>(input);
        ulong deviceWeights16 = Upload<Half>(weightsHalf);
        ulong deviceInput16 = Upload<Half>(inputHalf);
        ulong deviceBlocks = Upload<BlockQ4>(blocks);
        ulong deviceOutput32 = Allocate(Rows * sizeof(float));
        ulong deviceOutput16 = Allocate((nuint)(Rows * sizeof(Half)));

        float one = 1f;
        float zero = 0f;
        Half halfOne = (Half)1f;
        Half halfZero = (Half)0f;

        // cuBLAS is column-major. A row-major n_out x n_in buffer read column-major is the transpose
        // (n_in x n_out, leading dimension n_in), so every call below asks for CUBLAS_OP_T.
        Check(Cublas.cublasSgemv_v2(cublas, Cublas.OpTranspose, Cols, Rows, &one, deviceWeights32, Cols, deviceInput32, 1, &zero, deviceOutput32, 1));
        Report("cublasSgemv FP32", Download<float>(deviceOutput32, Rows), value => value, referenceFp32);

        Check(Cublas.cublasGemmEx(
            cublas, Cublas.OpTranspose, Cublas.OpNone, Rows, 1, Cols,
            &one, deviceWeights16, Cublas.DataHalf, Cols, deviceInput16, Cublas.DataHalf, Cols,
            &zero, deviceOutput32, Cublas.DataSingle, Rows, Cublas.ComputeSingle, Cublas.GemmDefault));
        Report("cublasGemmEx FP16 x FP16 -> FP32", Download<float>(deviceOutput32, Rows), value => value, referenceFp16);

        Check(Cublas.cublasGemmEx(
            cublas, Cublas.OpTranspose, Cublas.OpNone, Rows, 1, Cols,
            &one, deviceWeights16, Cublas.DataHalf, Cols, deviceInput16, Cublas.DataHalf, Cols,
            &zero, deviceOutput16, Cublas.DataHalf, Rows, Cublas.ComputeSingle, Cublas.GemmDefault));
        Report("cublasGemmEx FP16 x FP16 -> FP16", Download<Half>(deviceOutput16, Rows), value => (double)value, referenceFp16);

        Check(Cublas.cublasHgemm(
            cublas, Cublas.OpTranspose, Cublas.OpNone, Rows, 1, Cols,
            &halfOne, deviceWeights16, Cols, deviceInput16, Cols, &halfZero, deviceOutput16, Rows));
        Report("cublasHgemm FP16", Download<Half>(deviceOutput16, Rows), value => (double)value, referenceFp16);

        // Custom kernel: one thread block per row, one warp per block.
        int blocksPerRow = Cols / BlockWeights;
        void** arguments = stackalloc void*[4];
        arguments[0] = &deviceBlocks;
        arguments[1] = &deviceInput16;
        arguments[2] = &deviceOutput16;
        arguments[3] = &blocksPerRow;
        Check(CudaDriver.cuLaunchKernel(gemvQ4, Rows, 1, 1, 32, 1, 1, 0, 0, arguments, null));
        Check(CudaDriver.cuCtxSynchronize());
        Report("gemv_q4 kernel vs CPU on the same blocks", Download<Half>(deviceOutput16, Rows), value => (double)value, referenceQ4);

        Console.WriteLine($"[quantization] Q4 vs FP16 weights, relative L2 error of the output: {RelativeL2(referenceQ4, referenceFp16):E2}");
        Console.WriteLine($"[quantization] FP16 vs FP32 weights, relative L2 error of the output: {RelativeL2(referenceFp16, referenceFp32):E2}");
        Console.WriteLine($"[layout] sizeof(BlockQ4) = {sizeof(BlockQ4)} bytes for {BlockWeights} weights");

        // Throughput: bytes of weights read per second.
        int status = 0;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < TimingIterations; i++)
        {
            status |= Cublas.cublasGemmEx(
                cublas, Cublas.OpTranspose, Cublas.OpNone, Rows, 1, Cols,
                &one, deviceWeights16, Cublas.DataHalf, Cols, deviceInput16, Cublas.DataHalf, Cols,
                &zero, deviceOutput16, Cublas.DataHalf, Rows, Cublas.ComputeSingle, Cublas.GemmDefault);
        }

        status |= CudaDriver.cuCtxSynchronize();
        ReportThroughput("cublasGemmEx FP16", start, (long)Rows * Cols * sizeof(Half));

        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < TimingIterations; i++)
        {
            status |= CudaDriver.cuLaunchKernel(gemvQ4, Rows, 1, 1, 32, 1, 1, 0, 0, arguments, null);
        }

        status |= CudaDriver.cuCtxSynchronize();
        ReportThroughput("gemv_q4 kernel", start, (long)blocks.Length * sizeof(BlockQ4));
        Check(status);

        foreach (ulong pointer in (ulong[])[deviceWeights32, deviceInput32, deviceWeights16, deviceInput16, deviceBlocks, deviceOutput32, deviceOutput16])
        {
            Check(CudaDriver.cuMemFree(pointer));
        }

        Check(Cublas.cublasDestroy_v2(cublas));
        Check(CudaDriver.cuModuleUnload(module));
        Check(CudaDriver.cuDevicePrimaryCtxRelease(device));
        return 0;
    }

    /// <summary>
    /// Port of GGML's quantize_row_q4_0_ref: the scale maps the largest-magnitude weight of the block to -8.
    /// </summary>
    private static void QuantizeQ4(ReadOnlySpan<float> source, Span<BlockQ4> destination)
    {
        for (int block = 0; block < destination.Length; block++)
        {
            ReadOnlySpan<float> values = source.Slice(block * BlockWeights, BlockWeights);
            float absoluteMax = 0f;
            float signedMax = 0f;
            foreach (float value in values)
            {
                if (absoluteMax < MathF.Abs(value))
                {
                    absoluteMax = MathF.Abs(value);
                    signedMax = value;
                }
            }

            float scale = signedMax / -8f;
            float inverse = scale != 0f ? 1f / scale : 0f;
            destination[block].Scale = (Half)scale;
            for (int i = 0; i < BlockWeights / 2; i++)
            {
                int low = Math.Min(15, (int)((values[i] * inverse) + 8.5f));
                int high = Math.Min(15, (int)((values[i + (BlockWeights / 2)] * inverse) + 8.5f));
                destination[block].Quants[i] = (byte)(low | (high << 4));
            }
        }
    }

    private static void DequantizeQ4(ReadOnlySpan<BlockQ4> source, Span<float> destination)
    {
        for (int block = 0; block < source.Length; block++)
        {
            float scale = (float)source[block].Scale;
            Span<float> values = destination.Slice(block * BlockWeights, BlockWeights);
            for (int i = 0; i < BlockWeights / 2; i++)
            {
                byte packed = source[block].Quants[i];
                values[i] = ((packed & 0x0F) - 8) * scale;
                values[i + (BlockWeights / 2)] = ((packed >> 4) - 8) * scale;
            }
        }
    }

    private static double[] ReferenceGemv(ReadOnlySpan<float> weights, ReadOnlySpan<float> input)
    {
        double[] output = new double[Rows];
        for (int row = 0; row < Rows; row++)
        {
            ReadOnlySpan<float> rowWeights = weights.Slice(row * Cols, Cols);
            double sum = 0;
            for (int col = 0; col < Cols; col++)
            {
                sum += (double)rowWeights[col] * input[col];
            }

            output[row] = sum;
        }

        return output;
    }

    private static double RelativeL2(ReadOnlySpan<double> actual, ReadOnlySpan<double> expected)
    {
        double errorSquared = 0;
        double normSquared = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            double difference = actual[i] - expected[i];
            errorSquared += difference * difference;
            normSquared += expected[i] * expected[i];
        }

        return Math.Sqrt(errorSquared / normSquared);
    }

    private static void Report<T>(string name, T[] actual, Func<T, double> toDouble, double[] expected)
    {
        double[] converted = Array.ConvertAll(actual, value => toDouble(value));
        Console.WriteLine($"[check] {name}: relative L2 error {RelativeL2(converted, expected):E2}");
    }

    private static void ReportThroughput(string name, long start, long weightBytes)
    {
        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds / TimingIterations;
        Console.WriteLine($"[speed] {name}: {seconds * 1e6:F0} us per product, {weightBytes / seconds / 1e9:F1} GB/s of weights read");
    }

    private static void FillGaussian(Random random, Span<float> destination, float standardDeviation)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            // Box-Muller transform.
            double radius = Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble()));
            destination[i] = (float)(radius * Math.Cos(2.0 * Math.PI * random.NextDouble())) * standardDeviation;
        }
    }

    private static ulong Allocate(nuint bytes)
    {
        Check(CudaDriver.cuMemAlloc(out ulong pointer, bytes));
        return pointer;
    }

    private static ulong Upload<T>(ReadOnlySpan<T> source)
        where T : unmanaged
    {
        nuint bytes = (nuint)source.Length * (nuint)sizeof(T);
        ulong pointer = Allocate(bytes);
        fixed (T* host = source)
        {
            Check(CudaDriver.cuMemcpyHtoD(pointer, host, bytes));
        }

        return pointer;
    }

    private static T[] Download<T>(ulong pointer, int count)
        where T : unmanaged
    {
        var result = new T[count];
        fixed (T* host = result)
        {
            Check(CudaDriver.cuMemcpyDtoH(host, pointer, (nuint)count * (nuint)sizeof(T)));
        }

        return result;
    }

    private static void Check(int result, [CallerArgumentExpression(nameof(result))] string? call = null)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"CUDA call failed with code {result}: {call}");
        }
    }
}

/// <summary>
/// Sixteen bytes holding thirty-two 4-bit weights.
/// </summary>
[InlineArray(16)]
internal struct PackedNibbles16
{
    private byte _element0;
}

/// <summary>
/// One quantized block: an FP16 scale plus 32 weights of 4 bits. Same 18-byte layout as GGML's block_q4_0.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct BlockQ4
{
    public Half Scale;
    public PackedNibbles16 Quants;
}

/// <summary>
/// CUDA Driver API (libcuda.so). Where cuda.h redefines a name to a versioned symbol, EntryPoint carries the real one.
/// </summary>
internal static unsafe partial class CudaDriver
{
    private const string Library = "cuda";

    [LibraryImport(Library)]
    internal static partial int cuInit(uint flags);

    [LibraryImport(Library)]
    internal static partial int cuDeviceGet(out int device, int ordinal);

    [LibraryImport(Library)]
    internal static partial int cuDevicePrimaryCtxRetain(out nint context, int device);

    [LibraryImport(Library, EntryPoint = "cuDevicePrimaryCtxRelease_v2")]
    internal static partial int cuDevicePrimaryCtxRelease(int device);

    [LibraryImport(Library)]
    internal static partial int cuCtxSetCurrent(nint context);

    [LibraryImport(Library)]
    internal static partial int cuCtxSynchronize();

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int cuModuleLoad(out nint module, string fileName);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int cuModuleGetFunction(out nint function, nint module, string name);

    [LibraryImport(Library)]
    internal static partial int cuModuleUnload(nint module);

    [LibraryImport(Library, EntryPoint = "cuMemAlloc_v2")]
    internal static partial int cuMemAlloc(out ulong devicePointer, nuint byteSize);

    [LibraryImport(Library, EntryPoint = "cuMemFree_v2")]
    internal static partial int cuMemFree(ulong devicePointer);

    [LibraryImport(Library, EntryPoint = "cuMemcpyHtoD_v2")]
    internal static partial int cuMemcpyHtoD(ulong destination, void* source, nuint byteCount);

    [LibraryImport(Library, EntryPoint = "cuMemcpyDtoH_v2")]
    internal static partial int cuMemcpyDtoH(void* destination, ulong source, nuint byteCount);

    [LibraryImport(Library)]
    internal static partial int cuLaunchKernel(
        nint function,
        uint gridDimX,
        uint gridDimY,
        uint gridDimZ,
        uint blockDimX,
        uint blockDimY,
        uint blockDimZ,
        uint sharedMemBytes,
        nint stream,
        void** kernelParams,
        void** extra);
}

/// <summary>
/// cuBLAS (libcublas.so). Device pointers are passed as the 64-bit values returned by cuMemAlloc.
/// </summary>
internal static unsafe partial class Cublas
{
    internal const int OpNone = 0;
    internal const int OpTranspose = 1;
    internal const int DataSingle = 0;
    internal const int DataHalf = 2;
    internal const int ComputeSingle = 68;
    internal const int GemmDefault = -1;

    private const string Library = "cublas";

    [LibraryImport(Library)]
    internal static partial int cublasCreate_v2(out nint handle);

    [LibraryImport(Library)]
    internal static partial int cublasDestroy_v2(nint handle);

    [LibraryImport(Library)]
    internal static partial int cublasSgemv_v2(
        nint handle,
        int trans,
        int m,
        int n,
        float* alpha,
        ulong a,
        int lda,
        ulong x,
        int incx,
        float* beta,
        ulong y,
        int incy);

    [LibraryImport(Library)]
    internal static partial int cublasHgemm(
        nint handle,
        int transa,
        int transb,
        int m,
        int n,
        int k,
        Half* alpha,
        ulong a,
        int lda,
        ulong b,
        int ldb,
        Half* beta,
        ulong c,
        int ldc);

    [LibraryImport(Library)]
    internal static partial int cublasGemmEx(
        nint handle,
        int transa,
        int transb,
        int m,
        int n,
        int k,
        void* alpha,
        ulong a,
        int aType,
        int lda,
        ulong b,
        int bType,
        int ldb,
        void* beta,
        ulong c,
        int cType,
        int ldc,
        int computeType,
        int algo);
}
