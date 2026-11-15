# Revisión del montaje inicial

- **Fecha:** 2026-11-14.
- **Commit revisado:** `519a6316224e977f3ae623fcd3d60ce19a2441b6` en `develop/v1.0.0`, el *merge* del PR #2. Para ver qué ha cambiado desde la revisión: `git log --oneline 519a631..HEAD`.
- **Fuera del commit:** la primera versión de la hoja de ruta, que todavía no estaba versionada y se sustituyó después (ver *Seguimiento*). En esta revisión, «la hoja de ruta» es siempre esa primera versión.
- **Alcance:** solo el repositorio local. No se consultó GitHub (issues, protección de ramas, rama por defecto).
- **Método:** lectura completa y pruebas sobre copias desechables fuera del repositorio. Durante la revisión no se modificó ningún fichero del proyecto. Los programas de medición de CPU y GPU se guardaron después en [ejemplos/](ejemplos/) y se describen en el apéndice C.

Revisión del repositorio justo después del cambio `is1-dotnet-solution-skeleton`, hecha con un agente de IA a petición del autor. Recoge qué se encontró, con qué pruebas se comprobó y qué se recomienda cambiar, para poder decidir punto por punto más adelante sin repetir el análisis.

Cómo leer este documento:

- **Son recomendaciones pendientes de decisión, no convenciones del proyecto.** Si algo contradice a [AGENTS.md](../AGENTS.md), prevalece `AGENTS.md` hasta que un cambio aprobado diga lo contrario.
- **Es una foto de ese día.** Antes de actuar sobre un punto conviene comprobar que sigue vigente. La sección 5 anota qué se ha hecho desde entonces.
- **Seguimiento (2026-11-15, `72c5c8e`, #6).** La hoja de ruta se reescribió en [engine-inferencia-roadmap.md](engine-inferencia-roadmap.md) incorporando las recomendaciones de la sección 2, y la primera versión se eliminó. Además, se decidió permitir paquetes NuGet de infraestructura como `System.Numerics.Tensors` (punto 1.2), y la spec `build-and-solution-layout` se corrigió directamente, sin cambio de OpenSpec: recoge esa política, deja de fijar `Version` en `1.0.0` (3.2) y ya no exige que exista `artifacts/` (1.8).
- **Seguimiento (2026-11-15, `cb7d41e`, #8).** Se completaron los detalles menores del punto 1.8: `/models/` anclado en `.gitignore`, `.gitkeep` obsoletos eliminados, salida de compilación en `artifacts/`, `README.md` y las reglas que faltaban en los analizadores.
- **Cada dato indica su procedencia.** *Verificado* significa que se ejecutó una prueba en la máquina de desarrollo. *Sin verificar* significa que procede de conocimiento previo y conviene contrastarlo con la fuente antes de apoyarse en él.

## Veredicto

La base está bien montada y su arquitectura no necesita cambios. En el commit revisado, `dotnet build Simply.slnx -warnaserror` termina con 0 avisos, `dotnet format Simply.slnx --verify-no-changes` no encuentra nada, `dotnet test Simply.slnx` pasa los 2 tests y `openspec validate --all --strict` valida la spec (todo verificado).

Lo que conviene corregir se reparte en tres grupos:

1. **Andamiaje** (sección 1). Detalles baratos de arreglar ahora. Dos de ellos chocan con las reglas del propio proyecto en cuanto empiece la Fase 2.
2. **Hoja de ruta** (sección 2). Supuestos técnicos incorrectos o que no encajan con el hardware disponible. Es el grupo con más consecuencias, porque los agentes toman ese documento como referencia.
3. **Proceso** (sección 3). Opiniones sobre el peso del flujo de trabajo. Son preferencias, no errores.

La sección 4 recoge lo que está bien y la sección 5 ordena las acciones propuestas.

## Entorno de las pruebas

| Componente | Valor |
|---|---|
| Sistema | Linux x64, kernel 6.12 (EL9) |
| CPU | AMD Ryzen 5 5625U, 6 núcleos y 12 hilos; AVX2, FMA y F16C; sin AVX-512; L2 de 512 KiB por núcleo y L3 de 16 MiB |
| GPU | NVIDIA GeForce RTX 3050 Laptop, 4 GiB de VRAM, *compute capability* 8.6, driver 610.43.02 |
| CUDA | Toolkit 13.3 (`nvcc` 13.3.73) en `/usr/local/cuda` |
| .NET | SDK 10.0.301, runtime 10.0.9 |
| Paquetes | `xunit.v3` 4.0.3, `xunit.runner.visualstudio` 3.1.4, `Microsoft.NET.Test.Sdk` 17.14.1, `BenchmarkDotNet` 0.15.8 |
| OpenSpec | CLI 1.16.1 |

## 1. Andamiaje del repositorio

### 1.1 `docs/` no está versionado y el nombre de la hoja de ruta rompe enlaces

- **Situación.** `AGENTS.md` dice que `docs/` se versiona como contexto duradero, y el diseño del cambio `is1` da por hecho que la hoja de ruta ya está en el repositorio. Sin embargo `git status` muestra `?? docs/`: nunca se ha añadido (verificado). Un clon nuevo tendría un `AGENTS.md` que apunta a un fichero inexistente.
- **Nombre.** El nombre del fichero contiene `#`, que en una URL separa el fragmento: un enlace Markdown escrito a mano solo funciona si se codifica como `%23`.
- **Recomendación.** Renombrar el fichero (por ejemplo `docs/hoja-de-ruta.md`), actualizar la referencia en `AGENTS.md` y versionar `docs/`. La mención dentro del cambio archivado puede quedarse como historia.

### 1.2 `System.Numerics.Tensors` es un paquete NuGet, no parte de la BCL

- **Situación.** [openspec/config.yaml](../openspec/config.yaml) lo lista entre lo que proporciona la BCL de .NET 10. La spec [build-and-solution-layout](../openspec/specs/build-and-solution-layout/spec.md) exige que los proyectos de `src/` no tengan `<PackageReference>` a librerías de terceros. La hoja de ruta cuenta con `TensorPrimitives` para los operadores de CPU.
- **Evidencia (verificado).** `System.Numerics.Tensors.dll` no está en el *shared framework* `Microsoft.NETCore.App` 10.0.9 ni en su *reference pack*. Un `using System.Numerics.Tensors;` en `Simply.Core` da `CS0234` si no hay `PackageReference`. Con el paquete `System.Numerics.Tensors` 10.0.15 sí compila.
- **Por qué importa.** La Fase 2 chocará con la regla tal como está escrita, y «de terceros» no deja claro si un paquete de Microsoft cuenta.
- **Opciones.**
  - (a) Permitir explícitamente paquetes `System.*` de Microsoft en producción, ajustando `config.yaml` y la spec.
  - (b) Escribir los primitivos a mano con `Vector256<float>` y `Fma` de `System.Runtime.Intrinsics`, que sí viene en la BCL, y usar `TensorPrimitives` solo en el proyecto de tests como oráculo. La regla solo afecta a `src/`.
- **Recomendación.** La opción (b) encaja mejor con el objetivo de aprender. En cualquier caso, decidirlo antes de la Fase 2 y corregir la frase de `config.yaml`.

### 1.3 `BlockQ4_Custom` y los `struct` de datos planos chocan con los analizadores

- **Evidencia (verificado).** Al compilar tal cual los ejemplos de C# de la hoja de ruta dentro de `Simply.Core`, con la configuración del repositorio:
  - `CA1707` rechaza el nombre `BlockQ4_Custom` por el guion bajo. La supresión de [.editorconfig](../.editorconfig) solo cubre `tests/`.
  - `IDE0044` pide `readonly` en `_basePtr` y `_capacity` de `NativeArena`.
  - El resto compila, incluida la clase de interop con `[LibraryImport]`.
- **Evidencia adicional (verificado).** `CA1051` («no declarar campos de instancia visibles») es error en cualquier `struct` público con campos públicos, salvo que lleve `[StructLayout]`. Con `dotnet_code_quality.CA1051.exclude_structs = true` en `.editorconfig` deja de dispararse en todos los `struct`.
- **Recomendación.** Renombrar el tipo a `BlockQ4Custom` en `AGENTS.md` y en la hoja de ruta, o decidir una excepción explícita. Añadir la opción de `CA1051`, porque el estilo de representación plana de datos va a producir muchos `struct` con campos públicos.

### 1.4 BenchmarkDotNet dentro del proyecto de tests

- **Situación.** `Simply.Core.Tests` referencia `BenchmarkDotNet` aunque todavía no hay ningún benchmark.
- **Evidencia (verificado).** El directorio de salida de los tests ocupa 43 MB y 73 ficheros. Sin esa referencia baja a 11 MB y 45 ficheros. La diferencia es Roslyn, TraceEvent, Capstone y otras dependencias de BenchmarkDotNet.
- **Por qué importa (sin verificar).** BenchmarkDotNet necesita un ensamblado optimizado (Release) y arrancar desde un `Main` propio con `BenchmarkSwitcher` o `BenchmarkRunner`. Un proyecto de `xunit.v3` es un ejecutable con su punto de entrada generado y normalmente se compila en Debug.
- **Recomendación.** Crear un proyecto de consola aparte (`benchmarks/Simply.Benchmarks`) cuando llegue el primer benchmark y quitar la referencia del proyecto de tests. Afecta a la estructura descrita en `AGENTS.md` y al escenario de la spec que enumera los paquetes del proyecto de tests.

### 1.5 El objetivo «0 B» no está cubierto por ningún test

- **Situación.** `AGENTS.md` confía la auditoría de alojamientos a `[MemoryDiagnoser]`, que solo se mira cuando alguien lanza un benchmark.
- **Recomendación.** Añadir aserciones en los tests unitarios: medir antes y después del camino caliente y exigir diferencia cero. Así una regresión rompe `dotnet test`.

```csharp
RunHotPath(); // warm-up, so one-time initialization is not counted
long before = GC.GetTotalAllocatedBytes(precise: true);
RunHotPath();
long after = GC.GetTotalAllocatedBytes(precise: true);
Assert.Equal(0, after - before);
```

- **Notas.** `GC.GetTotalAllocatedBytes` cuenta todos los hilos; `GC.GetAllocatedBytesForCurrentThread` solo el actual. La técnica se usó en las mediciones del apéndice A, en [ejemplos/CpuProbes/Program.cs](ejemplos/CpuProbes/Program.cs): dio 0 B para un GEMV de un solo hilo y unos 4 KB por llamada para `Parallel.For` (verificado).

### 1.6 Los tests corren sobre VSTest gracias a un parche

- **Situación.** [Simply.Core.Tests.csproj](../tests/Simply.Core.Tests/Simply.Core.Tests.csproj) fija `IsTestingPlatformApplication` a `false`.
- **Evidencia (verificado).** Sin esa propiedad el proyecto compila, pero `dotnet test` falla con «Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later». La propiedad está ahí para seguir usando VSTest con un `xunit.v3` 4.x que ya se apoya en la plataforma nueva.
- **Alternativa probada (verificado).** Con estos tres cambios pasan los 2 tests con `dotnet test`, `dotnet test Simply.slnx` y `dotnet test --solution Simply.slnx`, y el ejecutable de tests también se puede lanzar directamente:
  1. Añadir a [global.json](../global.json) el bloque `"test": { "runner": "Microsoft.Testing.Platform" }`.
  2. Quitar `IsTestingPlatformApplication` del `.csproj`.
  3. Eliminar `Microsoft.NET.Test.Sdk` y `xunit.runner.visualstudio` del `.csproj` y de `Directory.Packages.props`.
- **Coste.** La spec enumera esos dos paquetes, así que haría falta un delta. No se comprobó la integración con el explorador de tests del editor.
- **Prioridad.** Baja: lo actual funciona. Es limpieza (dos paquetes y una propiedad menos).

### 1.7 No hay CI

- **Situación.** No existe `.github/workflows/`. Era un *Non-Goal* declarado de `is1`, pero el flujo de `AGENTS.md` descansa en que el CI valida cada PR, y el PR #2 se fusionó sin esa validación.
- **Recomendación.** Que sea el siguiente cambio, antes de empezar la Fase 1, con `dotnet format --verify-no-changes`, `dotnet build -warnaserror` y `dotnet test`.
- **A tener en cuenta desde el principio.**
  - Los *runners* alojados por GitHub no tienen GPU ni modelos (sin verificar). Conviene etiquetar los tests con *traits* (`RequiresGpu`, `RequiresModel`) y filtrar en CI.
  - Para cubrir las rutas sin SIMD en la misma máquina se puede repetir la suite con `DOTNET_EnableAVX2=0`. Con esa variable, `Vector256.IsHardwareAccelerated` y `Avx2.IsSupported` pasan a `False` (verificado).

### 1.8 Detalles menores

- **`models/` sin anclar en [.gitignore](../.gitignore).** En Linux no afecta. Con `core.ignorecase=true`, que es lo habitual en macOS y Windows, la regla ignora `src/Simply.Core/Models/` (verificado con `git check-ignore`). Basta escribir `/models/`.
- **`artifacts/` exigido pero inexistente.** La spec pide que el directorio exista, pero está ignorado y nada lo crea, así que un clon nuevo no cumple el escenario. Con `<UseArtifactsOutput>true</UseArtifactsOutput>` en `Directory.Build.props`, todo el build sale en `artifacts/bin/` y `artifacts/obj/`, fuera de `src/` y `tests/` (verificado).
- **Falta un README.** Debería recoger los requisitos (SDK, toolkit y driver de CUDA), los comandos de build y test, y cómo regenerar `.agents/` con OpenSpec. Esa carpeta está ignorada y la versión del CLI no está fijada en ningún sitio.
- **`.gitkeep` obsoletos** en `openspec/specs/` y `openspec/changes/archive/`, que ya tienen contenido.
- **Huecos en los analizadores (verificado).** La configuración actual detecta `using` dentro del `namespace`, campos que deberían ser `readonly`, guiones bajos en miembros públicos y campos públicos. No detecta nombres de campos privados fuera de convención, `using` sin usar, `if` sin llaves ni la falta de comentarios XML. Si se quiere uniformidad con agentes escribiendo código, conviene añadir reglas de nombres a `.editorconfig`.

## 2. Hoja de ruta técnica

### 2.1 `[SuppressGCTransition]` como principio general

- **Situación.** La hoja de ruta, `AGENTS.md` y `config.yaml` piden usar siempre `[SuppressGCTransition]` en las llamadas frecuentes a CUDA, y el ejemplo de interop de la hoja de ruta lo aplica a `cudaMalloc`.
- **Evidencia (verificado; cifras en el apéndice A).**
  - El atributo ahorra alrededor de 1 ns por llamada: de unos 2,5 ns a 1,4 ns en una función trivial de `libc`.
  - `cuLaunchKernel` con un kernel vacío cuesta unos 1,7 µs, igual con el atributo que sin él.
  - Las dos primeras llamadas a `cudaMalloc` tardaron entre 5 y 19 ms en total, según la ejecución.
- **Por qué es un error (sin verificar; documentación de `SuppressGCTransitionAttribute`).** El atributo está reservado a funciones que tardan menos de 1 µs, no bloquean, no toman locks y no vuelven a llamar al runtime. Mientras un hilo está dentro de una llamada así, un GC lanzado por otro hilo tiene que esperar a que vuelva. Casi ninguna función de CUDA cumple esas condiciones.
- **Magnitud.** Con unos 250 lanzamientos por token (estimación para un modelo de 24 capas), los lanzamientos suman alrededor de 0,4 ms por token. El ahorro del atributo sería de 0,3 µs.
- **Recomendación.** Quitar el «siempre» de los tres documentos y conservar `[LibraryImport]`. Como mucho, reservar el atributo para funciones triviales en las que una medición demuestre que compensa.

### 2.2 `Parallel.For` frente a Zero-GC

- **Situación.** La hoja de ruta propone paralelizar las filas de GEMV con `Parallel.For` y `TensorPrimitives.Dot`.
- **Evidencia (verificado).**

| Matriz FP32 | Un hilo | `Parallel.For` | Alojado por llamada con `Parallel.For` |
|---|---|---|---|
| 4864×896 | de 545 a 662 µs | de 125 a 163 µs | unos 4000 B |
| 128×896 | de 9,1 a 10,6 µs | de 7,1 a 9,7 µs | unos 3300 B |

- **Por qué importa.** Cada llamada aloja memoria gestionada, lo que contradice el principio de Zero-GC: en las 20 000 llamadas de la matriz pequeña hubo 7 recolecciones de generación 0. En la matriz grande acelera unas cuatro veces con 6 núcleos; en la pequeña, entre nada y un 30 %. Además, un `Span<T>` no se puede capturar en una lambda, así que el cuerpo tiene que trabajar con punteros.
- **Recomendación.** Vale como primer paso para ver el modelo funcionar. Para cumplir «0 B» hace falta un conjunto propio de hilos dedicados, creados al arrancar, con reparto estático de filas y un umbral por debajo del cual no se paraleliza. llama.cpp usa su propio pool de hilos (sin verificar).

### 2.3 Fallos del ejemplo `NativeArena`

Los tres se confirmaron ejecutando el ejemplo de la hoja de ruta copiado literalmente (verificado; se conserva como `FlawedArena` en [ejemplos/CpuProbes/Program.cs](ejemplos/CpuProbes/Program.cs)):

1. **Desbordamiento.** `count * sizeof(T)` se calcula en `int`. Con 600 millones de `float` el resultado es 18446744071814584320 bytes en lugar de 2400000000.
2. **Sin comprobación de capacidad.** Una arena de 1024 bytes devuelve un `Span<float>` de 100 000 elementos sin error. El aviso `IDE0044` sobre `_capacity` ya lo delata: el campo se asigna y nunca se lee.
3. **Copias.** Es un `struct` mutable. Tras `var copy = arena`, `arena.Alloc` y `copy.Alloc` devuelven la misma memoria. Una copia también permite un doble `Dispose`.

**Recomendación.**

- Convertirlo en `sealed class` (un único objeto creado al arrancar no incumple Zero-GC en el bucle), o mantener el `struct` y pasarlo siempre por `ref`.
- Calcular el tamaño como `(nuint)count * (nuint)sizeof(T)` y rechazar `count` negativo.
- Comprobar `alignedOffset + byteCount <= _capacity` y lanzar si no cabe.
- Anular el puntero tras `Dispose`.

### 2.4 `TensorView` con `ReadOnlySpan<long>`

- **Problema.** Un tipo con un campo `ReadOnlySpan<T>` tiene que ser `ref struct`, y un `ref struct` no puede ser elemento de un array, valor de un diccionario ni campo de una clase. No serviría para la tabla de tensores del modelo. Es una regla del lenguaje; no se compiló un ejemplo.
- **Recomendación.** Un `struct` normal con las dimensiones en un `[InlineArray(4)]` de `ulong` más el número de dimensiones. GGUF admite 4 como máximo (sin verificar).
- **Otros detalles del formato a tener presentes (sin verificar).**
  - GGUF guarda las dimensiones en orden inverso al de PyTorch.
  - Los datos de cada tensor se alinean a `general.alignment`, que por defecto son 32 bytes. Los punteros a pesos mapeados no están alineados a 64, así que hay que usar cargas no alineadas. La alineación a 64 solo vale para la memoria de `NativeArena`.
  - Un GGUF en F16 mezcla tipos: los tensores de una dimensión suelen quedarse en F32.
  - El puntero de `SafeMemoryMappedViewHandle.AcquirePointer` apunta al inicio de la página. Hay que sumarle el `PointerOffset` de la vista.

### 2.5 AVX-512 y FP16 en CPU

- **AVX-512.** `AGENTS.md` y la hoja de ruta lo citan como objetivo, pero la CPU de desarrollo no lo tiene: `Vector512.IsHardwareAccelerated` y `Avx512F.IsSupported` devuelven `False`, y `Vector<float>.Count` es 8 (verificado). Esa ruta no se podría ejecutar ni probar aquí. El objetivo real es `Vector256` con FMA.
- **FP16.** .NET 10 no expone las instrucciones F16C, aunque la CPU las tiene: entre las clases públicas de `System.Runtime.Intrinsics.X86` no hay ninguna para ellas (verificado; lista en el apéndice A). La conversión pasa por `TensorPrimitives.ConvertToSingle`, que procesó entre 2800 y 3500 millones de elementos por segundo, frente a unos 670 millones de la conversión escalar y entre 7400 y 8500 millones de un `Dot` en FP32 (verificado). Convertir cuesta entre dos y tres veces más por elemento que el producto en sí.
- **Recomendación.** Hacer la referencia de CPU solo en FP32, convirtiendo los pesos al cargar o usando un GGUF en F32, y dejar FP16 para la GPU.

### 2.6 Driver API de CUDA desde C#: funciona, con tres trampas

- **Prueba realizada (verificado).** Un programa de C# con `[LibraryImport("cuda")]` cargó un PTX generado con `nvcc -ptx`, lanzó un kernel `saxpy` con `cuLaunchKernel` sobre memoria reservada con `cudaMalloc` de `libcudart` y obtuvo el resultado correcto. Se usó el contexto primario (`cuDevicePrimaryCtxRetain` más `cuCtxSetCurrent`), que es el que comparte la Runtime API. El programa está en [ejemplos/GpuKernelLaunch/](ejemplos/GpuKernelLaunch/).
- **Resolución de librerías (verificado).** Los nombres `cuda` y `cudart` se resolvieron sin configuración. `ldconfig` conoce `libcuda.so`, `libcudart.so` y `libcublas.so`.
- **Trampa 1: símbolos versionados.** `cuda.h` redefine 141 nombres: `cuMemAlloc` pasa a `cuMemAlloc_v2`, `cuCtxCreate` a `cuCtxCreate_v4`, y así sucesivamente. `libcuda.so` sigue exportando los antiguos, y `cuMemAlloc` sin sufijo es la ABI antigua, con puntero de dispositivo y tamaño de 32 bits (verificado en la cabecera y con `nm`). Desde C# hay que indicar el símbolo real con `EntryPoint`. La lista está en el apéndice B.
- **Trampa 2: el contexto es estado por hilo.** En un hilo nuevo, `cuCtxSynchronize` devolvió 201 (`CUDA_ERROR_INVALID_CONTEXT`) hasta llamar a `cuCtxSetCurrent` (verificado). Con el pool de hilos de .NET conviene un hilo dedicado a la GPU, o fijar el contexto en cada hilo que llame a la Driver API.
- **Trampa 3: cuBLAS.** `libcublas.so.13` exporta `cublasHgemm` y `cublasGemmEx`, pero no existe `cublasHgemv` (verificado). El GEMV en FP16 se hace con un GEMM de una columna. cuBLAS trabaja en orden por columnas (sin verificar aquí), así que una matriz de GGUF guardada por filas se ve como su traspuesta.
- **Tiempos de arranque (verificado).**
  - Crear el contexto tardó entre 0,13 y 0,87 s. Los valores altos se dieron en la primera ejecución de cada tanda.
  - `nvcc` genera por defecto PTX para `sm_75` y el driver lo compila para la GPU real al cargarlo. Esa carga tardó 27 ms la primera vez, 11 ms con `CUDA_CACHE_DISABLE=1` y 0,4 ms cuando el driver ya lo tenía en su caché de compilación.
- **Simplificación posible (opinión).** Usar solo Driver API y cuBLAS y prescindir de `libcudart`. Es una librería menos que enlazar y un único modelo de contexto.

### 2.7 Los 4 GiB de VRAM dan sentido práctico a la Fase 4

- **Situación.** La GPU de desarrollo tiene 4 GiB de VRAM (verificado).
- **Cuentas.** El formato de la hoja de ruta ocupa 18 bytes por cada 32 pesos, es decir, 4,5 bits por peso.
  - Un modelo de 3 × 10⁹ parámetros ocupa unos 6 GB en FP16 y no cabe. En el formato de 4 bits ocupa unos 1,7 GB y sí cabe.
  - El modelo de 0,5 × 10⁹ parámetros previsto para las Fases 2 y 3 ocupa alrededor de 1 GB en FP16.
- **Consecuencia.** La cuantización deja de ser un ejercicio: es lo que permitirá cargar modelos de ese tamaño en este equipo.

### 2.8 Hitos de verificación demasiado blandos

- **Situación.** El Hito 1 se da por bueno con una sola frase («Hola mundo») y el Hito 2 con que el modelo responda de forma coherente.
- **Por qué importa.** Un RoPE mal rotado, un bias olvidado o una normalización con el épsilon equivocado siguen produciendo texto plausible, solo que peor. La coherencia no distingue un motor correcto de uno casi correcto.
- **Recomendación: usar llama.cpp como oráculo en cada nivel.**
  - **Hito 1.** Identificadores de tokens generados con llama.cpp sobre un corpus pequeño que incluya acentos, emoji, espacios repetidos, saltos de línea y tokens especiales, guardados como *fixtures* de texto junto con el hash del modelo.
  - **Hito 2.** Decodificación *greedy* idéntica token a token durante N tokens, y *logits* comparados con una tolerancia que habrá que calibrar.
  - **Hito 3.** La misma comparación entre la ruta de GPU y la referencia de CPU, además del objetivo de tokens por segundo.
  - **Hito 4.** Separar dos comprobaciones que ahora van mezcladas. La corrección del kernel se mide contra una referencia de CPU que descuantiza los mismos bloques, con tolerancia estricta. El error de cuantización frente a FP16 es otra cosa y debe ser relativo (por ejemplo ‖y_q − y‖ / ‖y‖), porque un MSE absoluto de `1e-3` depende de la escala de las activaciones.
- **Encaje con las reglas del proyecto.** Los modelos no se versionan, así que estos tests necesitan el *trait* `RequiresModel` del punto 1.7 y un modelo descargado en `artifacts/`.
- **Dos detalles del *sampling* (sin verificar).**
  - Los *logits* no son probabilidades, sino puntuaciones sin normalizar, aunque la hoja de ruta los describe como «vector de probabilidades».
  - El orden de los filtros afecta al resultado. Para reproducir a llama.cpp hay que aplicar los mismos filtros en su mismo orden.

### 2.9 Elegir un único modelo de referencia

- **Situación.** La hoja de ruta trata «BPE / SentencePiece» como un solo tokenizador y propone indistintamente Qwen 2.5 0.5B o TinyLlama 1.1B.
- **Diferencias que condicionan el trabajo (sin verificar).**
  - BPE y SentencePiece son algoritmos distintos. El BPE estilo GPT-2 usa la lista de *merges* y un pre-tokenizador por expresión regular. SentencePiece no tiene *merges* y decide con la puntuación de cada token.
  - Qwen 2.5 usa BPE, la plantilla de chat ChatML y bias en las proyecciones Q, K y V. Sin aplicar la plantilla, un modelo de instrucciones no responde de forma coherente.
  - TinyLlama es la arquitectura Llama sin bias, con SentencePiece.
  - La plantilla de chat viene en el GGUF como texto Jinja. Es más razonable escribir ChatML a mano que implementar Jinja.
- **Recomendación.** Elegir uno para las Fases 1 y 2 y anotar sus particularidades en la hoja de ruta. Para iterar rápido existe `stories260K.gguf`, de alrededor de 1 MB, que llama.cpp usa en sus propios tests (sin verificar).

### 2.10 Contradicciones con `AGENTS.md` y erratas

- **Contradicciones.** `AGENTS.md` limita el proyecto a Linux x64 y CUDA 13.x, pero la hoja de ruta cita `cudart64_12`, `cublas64_12`, `nvcuda.dll` y ARM NEON. Como los agentes leen ambos documentos, conviene alinearlos o declarar cuál manda.
- **Erratas.**
  - La primera línea contiene solo un espacio.
  - Un párrafo de la Fase 4 empieza a mitad de frase («como `cuBLAS` no sabe…»).
  - Llama «vector de probabilidades» a los *logits*.

## 3. Proceso de trabajo

Esta sección es opinión. Nada de lo que describe está roto.

### 3.1 El flujo pesa mucho para un proyecto personal

El cambio `is1` produjo 2197 palabras entre propuesta, diseño, tareas y spec para 173 líneas de configuración y código (verificado con `wc`). El flujo completo (propuesta, aprobación, issue, rama, commits por grupo, archivado, issues de seguimiento y PR) compensa cuando hay decisiones de diseño que discutir, como el parser de GGUF o la atención. Para cambios mecánicos, `AGENTS.md` ya prevé una vía directa; conviene usarla sin reparo.

### 3.2 La spec copia valores literales

La spec `build-and-solution-layout` exige, por ejemplo, que `Version` sea igual a `1.0.0`. Ese escenario caduca en el siguiente cambio de versión y obliga a un delta que no aporta nada. Las specs de este proyecto serán más útiles si describen invariantes y criterios numéricos, como la paridad con llama.cpp dentro de una tolerancia o cero alojamientos en el bucle, en lugar de reflejar el contenido de los ficheros.

### 3.3 Rama por defecto rotatoria

Con `develop/vX.Y.Z` como rama por defecto, cada ciclo obliga a cambiar la rama por defecto en GitHub, rehacer la protección de ramas y reorientar los PR abiertos. La versión ya vive en `Directory.Build.props`, y la regla de que coincida con el sufijo de la rama no la comprueba nada. Para un solo desarrollador, una rama fija con tags `vX.Y.Z` da el mismo resultado, y las ramas `release/` pueden crearse solo cuando haga falta un *hotfix*.

### 3.4 Qué se aprende y qué se delega

El objetivo declarado es aprender. Si el agente escribe la propuesta, el diseño, el código y los tests, el flujo optimiza la entrega y no el aprendizaje. Merece la pena fijar en `AGENTS.md` qué partes escribe el autor (por ejemplo los operadores y los kernels) y cuáles se delegan (*fixtures*, interop repetitivo, arnés de tests y revisión).

## 4. Lo que está bien y no hace falta tocar

- El orden de las fases, con una referencia en CPU antes de la GPU. Da una verdad contra la que comparar todo lo posterior.
- La higiene de build desde el primer día: SDK fijado, gestión central de paquetes, avisos como errores, `.editorconfig` y `.slnx`. Todo funciona tal como se describe (verificado).
- La separación entre `Simply.Core` y `Simply.Cli`, con los tests contra `Simply.Core`.
- No versionar modelos ni binarios de CUDA y apoyarse en *fixtures* sintéticos.
- Los commits por grupo de tareas con referencia al issue. El historial se lee bien.
- Las restricciones explícitas de alcance: un sistema operativo, un *target framework* y una versión de CUDA.
- `openspec/config.yaml` es válido para OpenSpec 1.16.1, incluida la clave `operations.<operación>.guidance`, que el CLI reconoce (verificado en su código).
- Los sufijos `:warning` de `.editorconfig` se aplican también en la compilación, no solo en el editor (verificado con `IDE0065`).

## 5. Lista de acciones propuestas

Las acciones marcadas están hechas, y su nota indica el commit que las incluye. Las pendientes no están aprobadas: las que afectan a specs o a `AGENTS.md` pasan por el flujo habitual del proyecto.

**Ahora (ediciones de minutos):**

- [x] Renombrar la hoja de ruta, actualizar `AGENTS.md` y versionar `docs/` (1.1). *Hecho en `72c5c8e` (#6): renombrada a `engine-inferencia-roadmap.md`, referenciada desde `AGENTS.md` y `docs/` versionado.*
- [x] Decidir la política sobre `System.Numerics.Tensors` y reflejarla en `config.yaml` y en la spec (1.2). *Hecho en `72c5c8e` (#6): se permiten paquetes de infraestructura, recogido en `AGENTS.md`, `config.yaml`, la hoja de ruta y la spec `build-and-solution-layout`.*
- [ ] Renombrar `BlockQ4_Custom` y añadir `exclude_structs` de `CA1051` (1.3). *En parte en `72c5c8e` (#6): renombrado a `BlockQ4` en `AGENTS.md` y la hoja de ruta; falta `.editorconfig`.*
- [x] Retirar el «siempre» de `[SuppressGCTransition]` en `AGENTS.md`, `config.yaml` y la hoja de ruta (2.1). *Hecho en `72c5c8e` (#6).*
- [x] Corregir en la hoja de ruta `NativeArena`, `TensorView`, AVX-512 y FP16, los nombres de librerías y las erratas (2.3, 2.4, 2.5, 2.10). *Hecho en `72c5c8e` (#6), con la reescritura.*
- [x] Anclar `/models/` en `.gitignore` y borrar los `.gitkeep` obsoletos (1.8). *Hecho en `cb7d41e` (#8).*

**Siguiente cambio:**

- [ ] CI con *traits* para GPU y modelos (1.7). *Pendiente en #3, del milestone «Phase 0: Test Bench».*
- [x] README con requisitos y arranque de herramientas (1.8). *Hecho en `cb7d41e` (#8): `README.md` con la visión general del proyecto, el stack tecnológico, los requisitos, la compilación y el uso de OpenSpec.*

**Antes de la fase correspondiente:**

- [x] Fase 1: fijar el modelo de referencia y redefinir los hitos con llama.cpp como oráculo (2.8, 2.9). *Hecho en `72c5c8e` (#6): Qwen2.5-0.5B-Instruct, con stories260K como banco de pruebas opcional.*
- [ ] Fase 2: test de «0 B» (1.5), proyecto de benchmarks aparte (1.4) y decisión sobre los hilos (2.2). *En parte en `72c5c8e` (#6): la hoja de ruta decide los hilos (propios, creados al arrancar) y hace del test de 0 B un criterio del Hito 2; falta decidir el proyecto de benchmarks.*
- [ ] Fase 3: enlaces con `EntryPoint` versionado, hilo dedicado a la GPU y GEMV mediante GEMM (2.6). *Recogido en `72c5c8e` (#6), en la hoja de ruta (3.1 y 3.3) y, lo del `EntryPoint`, también en `AGENTS.md`; se aplicará al implementar la Fase 3.*
- [x] Fase 4: separar en el Hito 4 la corrección del kernel y el error de cuantización (2.8). *Hecho en `72c5c8e` (#6), en la hoja de ruta.*

**Opcionales:**

- [ ] Pasar los tests a Microsoft.Testing.Platform (1.6).
- [x] Activar `UseArtifactsOutput` (1.8). *Hecho en `cb7d41e` (#8): toda la salida de compilación va a `artifacts/`.*
- [x] Añadir reglas de nombres a `.editorconfig` (1.8). *Hecho en `cb7d41e` (#8), junto con el resto de huecos de los analizadores: `using` sin usar (IDE0005), llaves (IDE0011) y comentarios XML en los miembros públicos del código de producción (CS1591).*
- [ ] Decidir sobre los puntos de proceso de la sección 3. *En parte en `72c5c8e` (#6): la spec ya no fija `Version` en `1.0.0` ni enumera los paquetes de test (3.2); quedan 3.1, 3.3 y 3.4.*

## Apéndice A. Mediciones

Todas las pruebas se hicieron en el entorno descrito arriba. Las de A.1 se hicieron sobre copias desechables del repositorio. Las de A.2 y A.3 proceden de los programas del apéndice C, compilados en Release. Sus tiempos son el rango observado en al menos cuatro ejecuciones: sirven para comparar órdenes de magnitud, no como benchmark riguroso.

### A.1 Compilación y herramientas

| Prueba | Resultado |
|---|---|
| `dotnet build Simply.slnx -warnaserror` en el commit revisado | 0 avisos, 0 errores |
| `dotnet format Simply.slnx --verify-no-changes` | sin cambios (código de salida 0) |
| `dotnet test Simply.slnx` | 2 tests correctos |
| `openspec validate --all --strict` | 1 spec válida |
| `using System.Numerics.Tensors;` en `Simply.Core` sin paquete | `CS0234` |
| Ejemplos de C# de la hoja de ruta dentro de `Simply.Core` | `CA1707` en `BlockQ4_Custom`; `IDE0044` en `_basePtr` y `_capacity` |
| `struct` público con campos públicos y sin `[StructLayout]` | `CA1051` |
| El mismo `struct` con `[StructLayout]`, o con `exclude_structs = true` | compila |
| Quitar `IsTestingPlatformApplication` | compila; `dotnet test` falla porque VSTest ya no está soportado |
| `"test": { "runner": "Microsoft.Testing.Platform" }` sin los paquetes de VSTest | 2 tests correctos |
| Salida de tests con y sin `BenchmarkDotNet` | 43 MB y 73 ficheros frente a 11 MB y 45 ficheros |
| `git -c core.ignorecase=true check-ignore -v src/Simply.Core/Models/Foo.cs` | coincide con `models/` de `.gitignore` |
| `<UseArtifactsOutput>true</UseArtifactsOutput>` | salida en `artifacts/bin/` y `artifacts/obj/` |
| `DOTNET_EnableAVX2=0` | `Vector256.IsHardwareAccelerated` y `Avx2.IsSupported` pasan a `False`; `Vector<float>.Count` pasa de 8 a 4 |

### A.2 CPU

| Medición | Resultado | Método |
|---|---|---|
| Llamada P/Invoke a `abs` de `libc` | de 2,34 a 2,56 ns normal; 1,39 ns con `[SuppressGCTransition]` | 200 millones de llamadas por variante |
| GEMV FP32 de 4864×896, un hilo | de 545 a 662 µs; 0 B alojados | `TensorPrimitives.Dot` por fila sobre memoria nativa; 300 repeticiones |
| GEMV FP32 de 4864×896, `Parallel.For` | de 125 a 163 µs; de 3892 a 4065 B alojados por llamada | el mismo cálculo con una lambda por fila |
| GEMV FP32 de 128×896, un hilo | de 9,1 a 10,6 µs; 0 B alojados | 20 000 repeticiones |
| GEMV FP32 de 128×896, `Parallel.For` | de 7,1 a 9,7 µs; de 3147 a 3433 B alojados por llamada; 7 recolecciones de generación 0 | 20 000 repeticiones |
| FP16 a FP32 con `TensorPrimitives.ConvertToSingle` | de 2779 a 3510 M de elementos por segundo | 4 M de elementos; 50 repeticiones |
| FP16 a FP32 escalar con `(float)half` | de 656 a 681 M de elementos por segundo | igual |
| `TensorPrimitives.Dot` en FP32 | de 7361 a 8511 M de elementos por segundo | igual |
| `NativeArena`: `(nuint)(600_000_000 * sizeof(float))` | 18446744071814584320 | código de la hoja de ruta copiado literalmente |
| `NativeArena`: `Alloc` sobre el original y sobre una copia | la misma dirección de memoria | igual |
| `NativeArena` de 1024 bytes: `Alloc<float>(100_000)` | `Span` de 100 000 elementos, sin error | igual |

Los alojamientos se midieron con `GC.GetTotalAllocatedBytes(true)` antes y después de cada bucle. `TensorPrimitives` procedía del paquete `System.Numerics.Tensors` 10.0.15.

La matriz de 4864×896 ocupa 17 MB y se relee en bucle con una L3 de 16 MiB. Los tiempos absolutos son optimistas frente a una pasada real por todo un modelo; lo relevante es la comparación entre variantes.

Clases públicas de `System.Runtime.Intrinsics.X86` en el runtime 10.0.9: `Aes`, `Avx`, `Avx10v1`, `Avx10v2`, `Avx2`, `Avx512BW`, `Avx512CD`, `Avx512DQ`, `Avx512F`, `Avx512Vbmi`, `Avx512Vbmi2`, `AvxVnni`, `AvxVnniInt16`, `AvxVnniInt8`, `Bmi1`, `Bmi2`, `Fma`, `Gfni`, `Lzcnt`, `Pclmulqdq`, `Popcnt`, `Sse`, `Sse2`, `Sse3`, `Sse41`, `Sse42`, `Ssse3`, `X86Base` y `X86Serialize`. No hay ninguna para F16C ni para AVX-512 FP16.

### A.3 GPU

| Medición | Resultado |
|---|---|
| `cuInit` más creación del contexto primario | de 127 a 869 ms; los valores altos, en la primera ejecución de cada tanda |
| `cuModuleLoad` de un PTX con dos kernels, más dos `cuModuleGetFunction` | 27 ms la primera vez; 11 ms con `CUDA_CACHE_DISABLE=1`; 0,4 ms con la caché de compilación del driver |
| Dos primeras llamadas a `cudaMalloc`, de 4 MiB cada una | de 5 a 19 ms en total |
| Dos `cudaMemcpy` de host a dispositivo, de 4 MiB cada una | de 1,3 a 1,4 ms en total |
| `cuLaunchKernel` de un kernel vacío | de 1,65 a 1,78 µs |
| Lo mismo con `[SuppressGCTransition]` | de 1,64 a 1,79 µs |
| `cuLaunchKernel` más `cuCtxSynchronize` | de 4,5 a 5,0 µs |
| `cuCtxSynchronize` en un hilo nuevo | 201 (`CUDA_ERROR_INVALID_CONTEXT`); 0 tras `cuCtxSetCurrent` |
| `saxpy` con Driver API sobre memoria de `cudaMalloc` | resultado correcto en 2²⁰ elementos |

Los kernels se compilaron con `nvcc -ptx -O3`. Cada variante se midió con 20 000 lanzamientos en tres rondas por ejecución, con una rejilla de 1 bloque y 32 hilos. Los enlaces usan `[LibraryImport]` contra `libcuda.so` y `libcudart.so`.

## Apéndice B. Símbolos versionados del Driver API

`cuda.h` de CUDA 13.3 redefine 141 nombres mediante `#define`. Desde C# no hay preprocesador, así que el `EntryPoint` de cada `[LibraryImport]` tiene que ser el símbolo de la derecha (verificado en la cabecera). Estos son los más probables en este proyecto:

| Nombre en la documentación | Símbolo real |
|---|---|
| `cuMemAlloc` | `cuMemAlloc_v2` |
| `cuMemFree` | `cuMemFree_v2` |
| `cuMemGetInfo` | `cuMemGetInfo_v2` |
| `cuMemcpyHtoD` | `cuMemcpyHtoD_v2` |
| `cuMemcpyDtoH` | `cuMemcpyDtoH_v2` |
| `cuMemcpyDtoD` | `cuMemcpyDtoD_v2` |
| `cuMemcpyHtoDAsync` | `cuMemcpyHtoDAsync_v2` |
| `cuMemcpyDtoHAsync` | `cuMemcpyDtoHAsync_v2` |
| `cuMemsetD8`, `cuMemsetD16`, `cuMemsetD32` | `cuMemsetD8_v2`, `cuMemsetD16_v2`, `cuMemsetD32_v2` |
| `cuCtxCreate` | `cuCtxCreate_v4` |
| `cuCtxDestroy` | `cuCtxDestroy_v2` |
| `cuCtxPushCurrent` | `cuCtxPushCurrent_v2` |
| `cuCtxPopCurrent` | `cuCtxPopCurrent_v2` |
| `cuDevicePrimaryCtxRelease` | `cuDevicePrimaryCtxRelease_v2` |
| `cuDevicePrimaryCtxReset` | `cuDevicePrimaryCtxReset_v2` |
| `cuDevicePrimaryCtxSetFlags` | `cuDevicePrimaryCtxSetFlags_v2` |
| `cuDeviceTotalMem` | `cuDeviceTotalMem_v2` |
| `cuModuleGetGlobal` | `cuModuleGetGlobal_v2` |
| `cuStreamDestroy` | `cuStreamDestroy_v2` |
| `cuEventDestroy` | `cuEventDestroy_v2` |

Para obtener la lista completa:

```bash
grep -n -E '^\s*#define\s+cu[A-Z]' /usr/local/cuda/include/cuda.h
```

Los símbolos sin sufijo siguen exportados por `libcuda.so` (comprobado con `nm -D` para `cuMemAlloc`, `cuCtxCreate` y `cuMemcpyHtoD`). Un enlace equivocado no falla al cargar: llama a la ABI antigua. En ella `cuMemAlloc` recibe un puntero de dispositivo y un tamaño de 32 bits, mientras que la actual usa un puntero de 64 bits y un `size_t`.

Ejemplo de enlace con símbolo versionado, tomado de [ejemplos/GpuKernelLaunch/Program.cs](ejemplos/GpuKernelLaunch/Program.cs):

```csharp
[LibraryImport("cuda", EntryPoint = "cuDevicePrimaryCtxRelease_v2")]
internal static partial int cuDevicePrimaryCtxRelease(int device);
```

`cuInit`, `cuDeviceGet`, `cuDevicePrimaryCtxRetain`, `cuCtxSetCurrent`, `cuCtxSynchronize`, `cuModuleLoad`, `cuModuleGetFunction`, `cuModuleUnload` y `cuLaunchKernel` se enlazaron con su nombre sin sufijo y funcionaron.

## Apéndice C. Ejemplos

Los programas de las mediciones de A.2 y A.3 están en [ejemplos/](ejemplos/). Son los que se usaron en la revisión, ordenados después para que cumplan las reglas del repositorio: compilan con `-warnaserror` y pasan `dotnet format --verify-no-changes` (verificado).

| Ejemplo | Qué demuestra | Secciones que respalda |
|---|---|---|
| [CpuProbes](ejemplos/CpuProbes/Program.cs) | Clases SIMD que expone el runtime, fallos del `NativeArena` de la hoja de ruta, GEMV con un hilo frente a `Parallel.For`, conversión de FP16 a FP32 y coste de la transición de GC en P/Invoke | 1.5, 2.1, 2.2, 2.3, 2.5 y A.2 |
| [GpuKernelLaunch](ejemplos/GpuKernelLaunch/Program.cs), con sus kernels en [kernels.cu](ejemplos/GpuKernelLaunch/kernels.cu) | Flujo completo de la Driver API desde C#: contexto primario, carga de PTX, `cuLaunchKernel`, interoperabilidad con `cudaMalloc`, coste por lanzamiento y contexto por hilo | 2.1, 2.6, A.3 y B |

Para ejecutarlos desde la raíz del repositorio:

```bash
dotnet run -c Release --project docs/ejemplos/CpuProbes

nvcc -ptx -O3 docs/ejemplos/GpuKernelLaunch/kernels.cu -o docs/ejemplos/GpuKernelLaunch/kernels.ptx
dotnet run -c Release --project docs/ejemplos/GpuKernelLaunch -- docs/ejemplos/GpuKernelLaunch/kernels.ptx
```

A tener en cuenta:

- **No forman parte de `Simply.slnx`.** No los compila la solución ni los cubrirá el CI, así que pueden quedarse atrás si cambian las reglas del repositorio.
- **Heredan `Directory.Build.props`.** Por eso sus `.csproj` son mínimos y solo funcionan dentro del repositorio.
- **`CpuProbes` usa el paquete `System.Numerics.Tensors` 10.0.15.** Desactiva la gestión central de paquetes solo en su proyecto, para no añadirlo a `Directory.Packages.props`. No es una dependencia de producción.
- **Generan ficheros que git ignora:** su compilación va a `artifacts/`, y `kernels.ptx` queda junto a su `.cu`.
- **El `FlawedArena` de `CpuProbes` es el `NativeArena` de la hoja de ruta, reproducido con sus fallos a propósito.** No debe reutilizarse: la versión corregida está en [engine-inferencia-roadmap.md](engine-inferencia-roadmap.md).
- **`GpuKernelLaunch` necesita una GPU de NVIDIA con su driver y el toolkit de CUDA.**
