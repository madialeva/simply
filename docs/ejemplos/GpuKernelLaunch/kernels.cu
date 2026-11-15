// Minimal kernels used by Program.cs. Compile them to PTX from the repository root before running the example:
//
//   nvcc -ptx -O3 docs/ejemplos/GpuKernelLaunch/kernels.cu -o docs/ejemplos/GpuKernelLaunch/kernels.ptx
//
// extern "C" keeps the entry point names unmangled so cuModuleGetFunction can find them by name.

// Empty kernel: measures the fixed cost of a launch.
extern "C" __global__ void noop(float* data, int count)
{
}

// y[i] = a * x[i] + y[i]. One thread per element; the bounds check covers a grid larger than count.
extern "C" __global__ void saxpy(float* y, const float* x, float a, int count)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < count)
    {
        y[i] = a * x[i] + y[i];
    }
}
