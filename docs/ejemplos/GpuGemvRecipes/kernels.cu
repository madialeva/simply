// Matrix-vector product over 4-bit block-quantized weights, dequantized on the fly.
// Compile to PTX from the repository root before running the example:
//
//   nvcc -ptx -O3 docs/ejemplos/GpuGemvRecipes/kernels.cu -o docs/ejemplos/GpuGemvRecipes/kernels.ptx

#include <cuda_fp16.h>
#include <cstdint>

// Same 18-byte layout as GGML's block_q4_0: one FP16 scale and 32 weights packed as 4-bit values.
// Element i of the block lives in the low nibble of qs[i]; element i + 16 in the high nibble.
struct block_q4
{
    __half d;
    uint8_t qs[16];
};

static_assert(sizeof(block_q4) == 18, "block_q4 must be 18 bytes");

// Launch with one thread block per output row and 32 threads (one warp) per block.
// Lane l handles blocks l, l + 32, l + 64, ... of its row; the warp then adds the 32 partial sums.
extern "C" __global__ void gemv_q4(
    const block_q4* __restrict__ weights,
    const __half* __restrict__ input,
    __half* __restrict__ output,
    int blocks_per_row)
{
    const int row = blockIdx.x;
    const int lane = threadIdx.x;
    const block_q4* row_blocks = weights + (size_t)row * blocks_per_row;

    // Accumulate in FP32 even though the data is FP16: FP16 sums lose precision and overflow early.
    float sum = 0.0f;
    for (int b = lane; b < blocks_per_row; b += 32)
    {
        const __half* x = input + b * 32;
        float partial = 0.0f;
        for (int i = 0; i < 16; ++i)
        {
            const uint8_t packed = row_blocks[b].qs[i];
            const int q0 = (packed & 0x0F) - 8;
            const int q1 = (packed >> 4) - 8;
            partial += q0 * __half2float(x[i]) + q1 * __half2float(x[i + 16]);
        }

        sum += __half2float(row_blocks[b].d) * partial;
    }

    // Warp-level reduction: after five exchanges every lane holds the full sum.
    for (int offset = 16; offset > 0; offset >>= 1)
    {
        sum += __shfl_xor_sync(0xffffffff, sum, offset);
    }

    if (lane == 0)
    {
        output[row] = __float2half(sum);
    }
}
