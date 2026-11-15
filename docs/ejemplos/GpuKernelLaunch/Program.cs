// Launches CUDA kernels from C# through the Driver API (libcuda.so) and times the per-call cost.
// Backs section 2.6 and appendix A.3 of docs/revision-inicial.md.
//
// Requirements: an NVIDIA GPU with its driver, and the CUDA Toolkit (nvcc).
// Run from the repository root:
//
//   nvcc -ptx -O3 docs/ejemplos/GpuKernelLaunch/kernels.cu -o docs/ejemplos/GpuKernelLaunch/kernels.ptx
//   dotnet run -c Release --project docs/ejemplos/GpuKernelLaunch -- docs/ejemplos/GpuKernelLaunch/kernels.ptx
//
// Timings come from a single run and are meant for order-of-magnitude comparisons,
// not as a rigorous benchmark. The project is not part of Simply.slnx.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Simply.Examples.GpuKernelLaunch;

internal static unsafe class Program
{
    private const int HostToDevice = 1;
    private const int DeviceToHost = 2;
    private const int InvalidContext = 201;

    private static int Main(string[] args)
    {
        string ptxPath = args.Length > 0 ? args[0] : "kernels.ptx";
        if (!File.Exists(ptxPath))
        {
            Console.Error.WriteLine($"PTX file not found: {ptxPath}. Compile kernels.cu with nvcc first (see the header of this file).");
            return 1;
        }

        // The primary context is the one the Runtime API (libcudart) uses, so memory from cudaMalloc
        // is valid in Driver API calls made under it.
        long start = Stopwatch.GetTimestamp();
        Check(CudaDriver.cuInit(0));
        Check(CudaDriver.cuDeviceGet(out int device, 0));
        Check(CudaDriver.cuDevicePrimaryCtxRetain(out nint context, device));
        Check(CudaDriver.cuCtxSetCurrent(context));
        Console.WriteLine($"[init] cuInit + primary context: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms");

        // The driver compiles the PTX for the actual GPU when the module is loaded.
        start = Stopwatch.GetTimestamp();
        Check(CudaDriver.cuModuleLoad(out nint module, ptxPath));
        Check(CudaDriver.cuModuleGetFunction(out nint noop, module, "noop"));
        Check(CudaDriver.cuModuleGetFunction(out nint saxpy, module, "saxpy"));
        Console.WriteLine($"[init] cuModuleLoad + 2x cuModuleGetFunction: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");

        ProbeRuntimeInterop(saxpy);
        ProbeLaunchCost(noop);
        ProbeThreadContext(context);

        Check(CudaDriver.cuModuleUnload(module));
        Check(CudaDriver.cuDevicePrimaryCtxRelease(device));
        return 0;
    }

    /// <summary>
    /// Runs saxpy through the Driver API on buffers allocated and filled with the Runtime API.
    /// </summary>
    private static void ProbeRuntimeInterop(nint saxpy)
    {
        const int Count = 1 << 20;
        const int BlockSize = 256;
        nuint bytes = (nuint)Count * sizeof(float);
        float* hostX = (float*)NativeMemory.AlignedAlloc(bytes, 64);
        float* hostY = (float*)NativeMemory.AlignedAlloc(bytes, 64);
        for (int i = 0; i < Count; i++)
        {
            hostX[i] = i;
            hostY[i] = 1f;
        }

        // The first Runtime API calls also pay for the lazy initialization of libcudart.
        long start = Stopwatch.GetTimestamp();
        Check(CudaRuntime.cudaMalloc(out nint deviceX, bytes));
        Check(CudaRuntime.cudaMalloc(out nint deviceY, bytes));
        double mallocMicroseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds;

        start = Stopwatch.GetTimestamp();
        Check(CudaRuntime.cudaMemcpy(deviceX, (nint)hostX, bytes, HostToDevice));
        Check(CudaRuntime.cudaMemcpy(deviceY, (nint)hostY, bytes, HostToDevice));
        double copyMicroseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        Console.WriteLine(
            $"[cudart] 2x cudaMalloc(4 MiB): {mallocMicroseconds:F0} us in total | " +
            $"2x cudaMemcpy host to device (4 MiB): {copyMicroseconds:F0} us in total");

        // cuLaunchKernel takes an array with one pointer per kernel argument.
        float a = 2f;
        int count = Count;
        void** arguments = stackalloc void*[4];
        arguments[0] = &deviceY;
        arguments[1] = &deviceX;
        arguments[2] = &a;
        arguments[3] = &count;
        Check(CudaDriver.cuLaunchKernel(saxpy, Count / BlockSize, 1, 1, BlockSize, 1, 1, 0, 0, arguments, null));
        Check(CudaDriver.cuCtxSynchronize());
        Check(CudaRuntime.cudaMemcpy((nint)hostY, deviceY, bytes, DeviceToHost));

        bool correct = true;
        for (int i = 0; i < Count; i++)
        {
            correct &= hostY[i] == (2f * i) + 1f;
        }

        Console.WriteLine($"[interop] saxpy through the Driver API on cudaMalloc buffers is correct: {correct}");

        Check(CudaRuntime.cudaFree(deviceX));
        Check(CudaRuntime.cudaFree(deviceY));
        NativeMemory.AlignedFree(hostX);
        NativeMemory.AlignedFree(hostY);
    }

    /// <summary>
    /// Times the launch of an empty kernel: asynchronous, with SuppressGCTransition, and followed by a synchronize.
    /// </summary>
    private static void ProbeLaunchCost(nint noop)
    {
        const int Launches = 20_000;
        const int Rounds = 3;
        nint data = 0;
        int count = 0;
        void** arguments = stackalloc void*[2];
        arguments[0] = &data;
        arguments[1] = &count;

        int status = 0;
        for (int i = 0; i < 2000; i++)
        {
            status |= CudaDriver.cuLaunchKernel(noop, 1, 1, 1, 32, 1, 1, 0, 0, arguments, null);
            status |= CudaDriver.cuLaunchKernelNoGcTransition(noop, 1, 1, 1, 32, 1, 1, 0, 0, arguments, null);
        }

        status |= CudaDriver.cuCtxSynchronize();

        for (int round = 0; round < Rounds; round++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < Launches; i++)
            {
                status |= CudaDriver.cuLaunchKernel(noop, 1, 1, 1, 32, 1, 1, 0, 0, arguments, null);
            }

            double regular = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Launches;
            status |= CudaDriver.cuCtxSynchronize();

            start = Stopwatch.GetTimestamp();
            for (int i = 0; i < Launches; i++)
            {
                status |= CudaDriver.cuLaunchKernelNoGcTransition(noop, 1, 1, 1, 32, 1, 1, 0, 0, arguments, null);
            }

            double suppressed = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Launches;
            status |= CudaDriver.cuCtxSynchronize();

            start = Stopwatch.GetTimestamp();
            for (int i = 0; i < Launches; i++)
            {
                status |= CudaDriver.cuLaunchKernel(noop, 1, 1, 1, 32, 1, 1, 0, 0, arguments, null);
                status |= CudaDriver.cuCtxSynchronize();
            }

            double synchronous = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Launches;

            Console.WriteLine(
                $"[launch] round {round}: cuLaunchKernel {regular:F2} us | with SuppressGCTransition {suppressed:F2} us | " +
                $"launch + cuCtxSynchronize {synchronous:F2} us");
        }

        Check(status);
    }

    /// <summary>
    /// The current context is per-thread state: a new thread has none until it calls cuCtxSetCurrent.
    /// </summary>
    private static void ProbeThreadContext(nint context)
    {
        int before = -1;
        int setCurrent = -1;
        int after = -1;
        var thread = new Thread(() =>
        {
            before = CudaDriver.cuCtxSynchronize();
            setCurrent = CudaDriver.cuCtxSetCurrent(context);
            after = CudaDriver.cuCtxSynchronize();
        });
        thread.Start();
        thread.Join();

        Check(setCurrent);
        Console.WriteLine(
            $"[threads] cuCtxSynchronize on a new thread: {before} ({InvalidContext} = CUDA_ERROR_INVALID_CONTEXT) | " +
            $"after cuCtxSetCurrent: {after}");
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
/// CUDA Driver API (libcuda.so). Method names follow cuda.h. Where the header redefines a name to a
/// versioned symbol, EntryPoint carries the real one: binding the unsuffixed name would call the legacy ABI.
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

    /// <summary>
    /// Same native function as <see cref="cuLaunchKernel"/>, bound again only to measure the attribute's effect.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "cuLaunchKernel")]
    [SuppressGCTransition]
    internal static partial int cuLaunchKernelNoGcTransition(
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
/// CUDA Runtime API (libcudart.so), used here only to show that it interoperates with the Driver API.
/// </summary>
internal static partial class CudaRuntime
{
    private const string Library = "cudart";

    [LibraryImport(Library)]
    internal static partial int cudaMalloc(out nint devicePointer, nuint size);

    [LibraryImport(Library)]
    internal static partial int cudaFree(nint devicePointer);

    [LibraryImport(Library)]
    internal static partial int cudaMemcpy(nint destination, nint source, nuint count, int kind);
}
