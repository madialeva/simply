# Simply

Simply is a large language model inference engine written from scratch in C# and CUDA, in the spirit of [llama.cpp](https://github.com/ggml-org/llama.cpp). It is a learning project: the goal is to understand every piece of an inference engine by building it, from reading a model file to generating text on the GPU with custom quantization kernels.

> **Status:** early development. The solution skeleton is in place, and no model runs yet.

## What the project covers

The work is split into phases. Each one ends with a milestone checked against llama.cpp running the same model.

| Phase | What gets built |
|---|---|
| 0. Test bench | CI, llama.cpp as the reference oracle, downloads of the reference models, measured memory bandwidth ceilings |
| 1. From file to tokens | Zero-copy GGUF reader over memory-mapped files, model inspector, byte-level BPE tokenizer |
| 2. Transformer on the CPU | FP32 forward pass (RMSNorm, RoPE, grouped-query attention with a KV cache, SwiGLU), sampling and chat, SIMD and multithreading with no allocations per token |
| 3. GPU with cuBLAS | FP16 CUDA backend: cuBLAS for the matrix products and simple custom kernels for the rest |
| 4. Quantization and custom kernels | Block quantization (GGML's Q8_0 and Q4_0 first, then custom variants) and CUDA kernels that dequantize on the fly |

The reference model is [Qwen2.5-0.5B-Instruct](https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF) in GGUF format. The detailed roadmap, written in Spanish, is in [docs/engine-inferencia-roadmap.md](docs/engine-inferencia-roadmap.md).

## Tech stack

| Area | Technology | Used for |
|---|---|---|
| Platform | Linux x64 | The only supported target |
| Language and runtime | C# 14 on .NET 10 (`net10.0`) | Orchestration, model formats, tokenizer, memory management and CPU operators |
| Memory and data layout | `NativeMemory`, `MemoryMappedFile`, `Span<T>`, unsafe pointers, `[InlineArray]`, `[StructLayout]` | Zero-copy weights and no allocations in the token loop |
| CPU math | `System.Numerics.Tensors` (`TensorPrimitives`) and hardware intrinsics (`System.Runtime.Intrinsics`, AVX2 and FMA) | Vectorized operators |
| Text processing | Source-generated regular expressions, UTF-8 spans, dictionary `AlternateLookup` | Allocation-free tokenizer |
| Native interop | Source-generated `[LibraryImport]` bindings | Calls into the CUDA libraries |
| GPU | CUDA 13: Driver API (`libcuda`) and cuBLAS (`libcublas`) | Device memory, kernel launches and FP16 matrix products |
| GPU kernels | CUDA C++ compiled with `nvcc` to PTX and cubin | Normalization, RoPE, attention and quantized matrix-vector products |
| Testing | xUnit v3 | Unit tests and numerical checks |
| Performance | BenchmarkDotNet, Nsight Systems (`nsys`), Nsight Compute (`ncu`) | CPU micro-benchmarks, GPU timelines and per-kernel metrics |
| Models | GGUF files from Hugging Face | Weights, tokenizer and model metadata |
| Reference | llama.cpp (`llama-tokenize`, `llama-eval-callback`, `llama-perplexity`, `llama-bench`) | Oracle for tokens, intermediate tensors, perplexity and speed; never a dependency |
| Build | .NET SDK pinned in `global.json`, `Simply.slnx`, Central Package Management, `Directory.Build.props`, `.editorconfig` with analyzers | Reproducible builds with warnings treated as errors |
| Specs | [OpenSpec](https://github.com/Fission-AI/OpenSpec) | Spec-driven changes, stored in `openspec/` |
| Project management | GitHub issues, milestones, Projects and pull requests; GitHub Actions (planned) | Planning, review and continuous integration |
| AI agents | [AGENTS.md](AGENTS.md) | Conventions for AI coding assistants |

Production code only takes NuGet packages that provide generic infrastructure. Packages that implement pieces of the engine, such as inference runtimes, GGUF readers, tokenizers, quantizers or kernels, are deliberately left out.

## Requirements

- Linux x64.
- .NET 10 SDK; the exact version is pinned in `global.json`.
- From Phase 3 on: CUDA Toolkit 13.x, an NVIDIA driver that supports it, and a GPU with compute capability 7.5 (Turing) or newer. Development happens on an RTX 3050 Laptop GPU (Ampere, 4 GB).
- Optional: llama.cpp built locally, for the tests that compare against it, and Node.js with the OpenSpec CLI for the spec workflow.

## Build and test

```bash
dotnet build Simply.slnx -warnaserror
dotnet test Simply.slnx
dotnet format Simply.slnx --verify-no-changes
```

Build outputs go to `artifacts/`, which git ignores.

## Repository layout

```text
src/Simply.Core/      engine core: GGUF, memory, tokenizer, tensors, compute backends, interop and models
src/Simply.Kernels/   CUDA kernel sources (.cu)
src/Simply.Cli/       command-line tool for inspection, benchmarks and inference
tests/                xUnit tests
docs/                 roadmap and reviews (in Spanish), plus standalone examples
openspec/             specs and change history
eng/                  engineering scripts and MSBuild targets
artifacts/            build outputs and downloaded models, not versioned
```

## Working with OpenSpec

Install the CLI with `npm install -g @fission-ai/openspec` and check the specs with `openspec validate --all --strict`. The instruction files that OpenSpec generates for each AI assistant (`.agents/`, `.claude/`…) are local and ignored by git. After cloning, `openspec init --tools <tool>` generates them again while keeping `openspec/config.yaml`, and `openspec update` keeps them current.
