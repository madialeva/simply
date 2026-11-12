# AGENTS.md — Simply

## Proyecto

Simply es un motor de inferencia local para modelos de lenguaje (LLM) al estilo
de `llama.cpp`, desarrollado **desde cero** con fines de aprendizaje y
profundización técnica en sistemas de inferencia de bajo nivel.

El proyecto se construye exclusivamente para **Linux (`linux-x64`)** con
**C# 14 / .NET 10** (`net10.0`) para la orquestación, el parseo de formatos, la
tokenización y la gestión de memoria sin pausas de recolector de basura
(*Zero-GC*), y con **CUDA 13.x** (CUDA Runtime `libcudart.so`, `cuBLAS`
`libcublas.so`, CUDA Driver API `libcuda.so` y kernels `.cu` propios compilados
con `nvcc` a `.ptx` / `.cubin`) para la aceleración por hardware en GPU. No
añadir rutas de código, condicionales ni *workarounds* para Windows, macOS u
otras versiones de .NET o CUDA.

La hoja de ruta técnica y los hitos de verificación se encuentran en
`docs/engine-inferencia-c#.md` y estructuran el desarrollo en cuatro fases:

1. **Fase 1 — Ingesta Zero-Copy (GGUF) y Tokenización:** lectura de archivos
   `.gguf` mediante `MemoryMappedFile` (`TensorView` sobre punteros nativos sin
   copiar tensores al *Managed Heap*), tokenizador BPE/SentencePiece libre de
   alojamientos temporales y pipeline de *sampling* (*Temperature*, *Top-K*,
   *Top-P*, *Min-P*, penalización por repetición).
2. **Fase 2 — Transformer en CPU (`FP32` / `FP16`):** asignador lineal sobre
   memoria nativa (`NativeArena` con `NativeMemory.AlignedAlloc` alineado a 64
   bytes) y operadores matemáticos fundamentales (Embedding Lookup, `RMSNorm`,
   `RoPE`, `GEMV`/`GEMM`, `GQA` + *KV Cache*, `SwiGLU`) vectorizados con
   `TensorPrimitives` y `System.Runtime.Intrinsics` (AVX2 / AVX-512).
3. **Fase 3 — Salto a GPU con Backends Estándar (`cudart` + `cuBLAS`):**
   abstracción `IComputeBackend` e interoperabilidad con CUDA Runtime y `cuBLAS`
   en precisión estándar (`FP16` / `BF16`).
4. **Fase 4 — Cuantización Propia y Kernels CUDA a Medida:** formatos de
   cuantización por bloques (`BlockQ4_Custom` con `[InlineArray]`) y kernels
   CUDA propios (`.cu` -> `.ptx` / `.cubin`) con des-cuantización al vuelo y
   reducción *warp-level* (`__shfl_xor_sync`), cargados y ejecutados mediante el
   CUDA Driver API.

No se deben introducir librerías externas de inferencia o *machine learning* de
alto nivel (como `LLamaSharp`, `ONNX Runtime`, `TorchSharp` o `ML.NET`) en el
código de producción: el objetivo del proyecto es implementar y comprender cada
pieza desde cero. No añadir dependencias de producción nuevas sin un cambio de
OpenSpec aprobado.

## Principios arquitectónicos y convenciones de C# / .NET 10

1. **Zero-GC en el bucle de inferencia:** el Garbage Collector de .NET no debe
   intervenir durante la generación de tokens. Todos los tensores (pesos,
   activaciones y *KV Cache*) se alojan en memoria no administrada
   (`NativeMemory.AlignedAlloc`), archivos mapeados en memoria
   (`MemoryMappedFile`) o directamente en la VRAM de la GPU.
2. **Representación plana de datos:** uso intensivo de `Span<T>`,
   `ReadOnlySpan<T>`, `ref struct`, punteros crudos (`unsafe`), `[InlineArray]`
   y `[StructLayout(LayoutKind.Sequential, Pack = 1)]` para mapear bloques
   binarios sin serialización ni copias intermedias. En las rutas calientes y en
   el tokenizador, evitar alojar instancias de `string` o arrays temporales en
   el *heap* (apoyarse en `Span<char>`, UTF-8 directo y `AlternateLookup` en
   diccionarios).
3. **Interop de coste cero:** usar siempre `[LibraryImport]` (generadores de
   código en tiempo de compilación, nunca `[DllImport]`) junto con
   `[SuppressGCTransition]` en llamadas frecuentes a las APIs nativas de CUDA en
   Linux (`libcudart.so`, `libcublas.so`, `libcuda.so`).
4. **Configuración de compilación:** todos los proyectos C# apuntan a `net10.0`
   con `LangVersion` en C# 14, `<Nullable>enable</Nullable>`,
   `<ImplicitUsings>enable</ImplicitUsings>` y
   `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` donde se manipulen punteros o
   bloques nativos. El código debe compilar sin advertencias (`dotnet build
   -warnaserror`).
5. **Testing y benchmarking:** los tests unitarios y de verificación matemática
   se escriben con **xUnit** (`xunit.v3`) en `tests/Simply.Core.Tests`. Para
   microbenchmarks de operadores, tokenizador y kernels se emplea
   **BenchmarkDotNet** (con `[MemoryDiagnoser]` para auditar `0 B` alojados en
   el *Managed Heap* y `[DisassemblyDiagnoser]` para inspeccionar el ensamblador
   SIMD emitido por el JIT), complementado con mediciones de *throughput*
   (*prefill* y *decode* en tokens/s y ancho de banda efectivo en GB/s) mediante
   `Stopwatch.GetTimestamp()` en `Simply.Cli`.

## Organización del código

El código fuente bajo `src/` y las pruebas bajo `tests/` se organizan por
responsabilidad:

```text
src/
├── Simply.Core/             núcleo del motor: GGUF, memoria nativa, tensores,
│                            tokenizador, sampling, operadores CPU y backends
│   ├── Gguf/                parser de cabeceras, metadatos y mmap (TensorView)
│   ├── Memory/              NativeArena y gestión de memoria no administrada
│   ├── Tokenization/        tokenizador BPE/SentencePiece y pipeline de sampling
│   ├── Tensors/             primitivas matemáticas, tipos cuantizados (BlockQ4_Custom)
│   ├── Compute/             IComputeBackend, backend CPU (SIMD) y backend CUDA
│   ├── Interop/             enlaces [LibraryImport] para libcudart, libcublas y libcuda
│   └── Models/              arquitecturas Transformer, capas, GQA y KV Cache
├── Simply.Kernels/          archivos fuente CUDA (.cu) y scripts/targets de compilación a .ptx
└── Simply.Cli/              ejecutable de consola para inspección GGUF, benchmarks e inferencia
tests/
└── Simply.Core.Tests/       tests unitarios (xUnit) y de verificación matemática por fase/hito
```

Las dependencias fluyen `Simply.Cli -> Simply.Core`. `Simply.Core` no depende de
la interfaz de consola para que toda la lógica de parseo, tokenización, memoria
y operadores matemáticos pueda probarse de forma aislada desde
`Simply.Core.Tests`.

## OpenSpec

OpenSpec es el flujo de trabajo por defecto para los cambios. Para cada cambio:
crear `proposal.md` y `design.md` (más `tasks.md` y los deltas necesarios en
`specs/`), y **DETENERSE** hasta que el usuario apruebe la propuesta y el
diseño. No empezar a implementar las tareas hasta entonces. Dejar una
compilación funcional y con los tests pasando para su verificación, y archivar
únicamente tras la confirmación del usuario.

Esto no es obligatorio. Si el programador indica explícitamente en una petición
de cambio que no se debe usar OpenSpec (o que el cambio debe realizarse
directamente), omitir el flujo de OpenSpec e implementar el cambio directamente.

Idioma de los artefactos: la prosa en `proposal.md`, `design.md`, `tasks.md` y
`specs/*.md` (motivación, decisiones, descripciones de requisitos y escenarios,
etc.) se escribe en **español**, excepto las etiquetas fijas impuestas por las
skills de OpenSpec, que se mantienen en inglés tal cual (`Why`, `What Changes`,
`Capabilities`, `Impact`, `Context`, `Goals`/`Non-Goals`, `Decisions`,
`Risks / Trade-offs`, `Migration Plan`, `Open Questions`, `Purpose`,
`ADDED`/`MODIFIED`/`REMOVED`/`RENAMED Requirements`, `Requirement:`,
`Scenario:`, `WHEN`/`THEN`/`AND`, `SHALL`/`MUST`). Los identificadores de código
(clases, structs, métodos, propiedades, paquetes) también se mantienen en inglés
dentro del texto en español. Esto es independiente del idioma de los issues de
GitHub (siempre en inglés, ver más abajo).

## Idioma y ficheros

Usar **inglés** para el código fuente (C# y CUDA), identificadores, comentarios
en código, archivos de solución/proyecto (`.slnx`, `.csproj`,
`Directory.Build.props`), scripts, mensajes de commit y pull requests. Títulos y
descripciones de issues y milestones en GitHub: siempre en inglés. Los
artefactos de OpenSpec, la documentación de diseño y apuntes matemáticos en
`docs/` y la conversación con el usuario se mantienen en **español**.

Los archivos de texto usan finales de línea LF (`\n`); nunca normalizar finales
de línea en archivos binarios como modelos `.gguf`, binarios CUDA (`.cubin`) o
fixtures de prueba binarios. No versionar archivos `.gguf` pesados en el
repositorio; para los tests unitarios se usarán cabeceras sintéticas en memoria
o micro-fixtures binarios generados por código.

## Preferencias de trabajo

- Conversación con el usuario en español.
- Priorizar el contexto duradero en ficheros del repositorio (este `AGENTS.md`,
  `docs/` y `openspec/`) frente a la memoria interna del agente, para que
  sobreviva a clonados o traslados del repositorio.
- Este fichero (`AGENTS.md`), `docs/` y `openspec/` se versionan en el
  repositorio como contexto duradero. Las carpetas de herramientas e IAs
  (`.agents/`, `.opencode/`, `.claude/`, `.vscode/`, etc.) se mantienen como
  configuración local: se ignoran en git (`.gitignore`) y **no** se publican.

## Flujo de trabajo en GitHub (issues, ramas, PRs)

No existe `main`/`master`. La rama por defecto y de larga vida es
`develop/vX.Y.Z` (comenzando en `develop/v1.0.0`), que contiene la versión
actualmente en desarrollo. Al publicar una versión se corta `release/vX.Y.Z`
desde ella y se etiqueta `vX.Y.Z`; los *hotfixes* incrementan el dígito de
parche. Cuando comienza un nuevo ciclo, se crea `develop/vX.Y.Z` a partir del
tag publicado y pasa a ser la rama por defecto. La versión del programa vive
como única fuente de verdad en `Directory.Build.props` (`<Version>`) y debe
coincidir con el sufijo de la rama. El workflow de CI en GitHub Actions se
ejecuta únicamente al abrir o actualizar un Pull Request (`pull_request`) o bajo
demanda (`workflow_dispatch`), **nunca** en el `push` posterior al hacer merge
sobre `develop/vX.Y.Z`.

Cada cambio de OpenSpec se sigue en GitHub con este ciclo:

1. Propuesta de OpenSpec aprobada por el usuario.
2. Issue en GitHub para el cambio, enlazando su carpeta
   `openspec/changes/<name>/` (más su milestone cuando existan milestones; el
   tablero de Projects se mantiene ligero: *Todo* / *In progress* / *Done*).
   Mientras está activo, la carpeta del cambio se nombra `is<n>-<slug>` según su
   issue; el prefijo de fecha se añade únicamente al archivar el cambio.
3. Rama creada a partir del issue (panel *Development* → *"Create a branch"*;
   nombre como `change/is<n>-<slug>`) partiendo de `develop/v1.0.0` (o la rama
   `develop/vX.Y.Z` activa).
4. Implementación en la rama + push (los pushes los realiza el usuario; el
   agente no tiene acceso SSH a `origin` desde su shell). Los commits son
   parciales, uno por cada grupo de tareas de `tasks.md`, para que ningún commit
   sea gigante y el historial de la rama siga las tareas:
   - El agente hace commit de un grupo tan pronto como todas sus tareas están
     terminadas y `dotnet test` pasa; el commit incluye el código, los tests,
     la documentación del grupo y la actualización correspondiente en
     `openspec/` (`tasks.md` y artefactos del cambio).
   - El título comienza con el issue como `[is<n>]`, para que los commits de un
     issue sean fáciles de seguir después, seguido de un resumen en inglés del
     grupo y su número, por ejemplo:
     `[is12] Implement GGUF header parser and mmap TensorView (group 1)`.
     El cuerpo termina con `Refs #<n>` para que GitHub enlace el commit al
     issue.
   - No se hace commit de nada fuera de los grupos de tareas sin confirmación
     del usuario, y el cambio no se archiva ni se envía a un PR hasta que el
     usuario lo valide.
   - Los commits y PRs no llevan coautoría ni atribución de IA: sin trailers
     `Co-Authored-By` ni líneas *"Generated with"*.
5. Una vez que el usuario valida el cambio, y antes de abrir cualquier PR, el
   agente lo archiva en la misma rama: sincroniza sus *delta specs* en
   `openspec/specs/` y lo mueve a
   `openspec/changes/archive/YYYY-MM-DD-is<n>-<slug>/`, registrando el archivado
   en el último commit de la rama (`[is<n>] Archive the <slug> change`). A
   continuación, revisa los *Non-Goals* del cambio: por cada punto que deje
   trabajo pendiente útil, crea un issue de seguimiento explicando el alcance
   pendiente, enlazando el cambio archivado y manteniendo la relación con el
   *Non-Goal* original.
6. El usuario hace push y abre el PR hacia `develop/vX.Y.Z` con `Closes #<n>` en
   la descripción → el CI valida el PR (sin volver a ejecutarse tras el merge) →
   el usuario revisa el diff.
7. *Merge commit* como norma (sin *squash*): los commits por grupo aterrizan en
   la rama de desarrollo tal cual, de modo que su historial sigue reflejando las
   tareas de cada cambio, y el *merge commit* marca dónde termina el cambio. PR
   fusionado = issue cerrado (automáticamente vía `Closes`) = cambio archivado.
