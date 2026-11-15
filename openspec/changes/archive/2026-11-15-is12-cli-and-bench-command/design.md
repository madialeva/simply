# Design

## Context

Ver `proposal.md` (*Why*) para la motivación. Punto de partida:

- `Simply.Cli` es un `Main` vacío que solo referencia a `Simply.Core`.
- `Simply.Core.Tests` referencia `BenchmarkDotNet` 0.15.8 sin usarlo. Según el punto 1.4 de la revisión inicial, eso multiplica por cuatro el tamaño de la salida de tests (43 MB frente a 11 MB).
- La compilación trata los avisos como errores, aplica los analizadores `latest-recommended` y envía toda la salida a `artifacts/`.
- La política de dependencias permite paquetes de infraestructura genérica, pero cada dependencia de producción nueva necesita un cambio aprobado. Este cambio aprueba dos: `System.CommandLine` y `BenchmarkDotNet`.

Las decisiones de este documento se validaron con un prototipo desechable: una copia del repositorio con la CLI, `Simply.Benchmarks` y `Simply.Cli.Tests`, compilada con las reglas del proyecto. Los datos marcados como *comprobado* salen de ese prototipo, ejecutado en la máquina de desarrollo (Ryzen 5 5625U, SDK 10.0.301).

## Goals / Non-Goals

**Goals:**

- Fijar la forma de la herramienta (`simply <comando> [subcomando] [opciones]`) y la manera de añadir comandos, para que las fases siguientes solo tengan que colgar los suyos.
- Tener un sitio para los benchmarks (`Simply.Benchmarks`) y una forma de lanzarlos (`simply bench <suite>`) que funcione igual en el repositorio y con la herramienta publicada.
- Medir ya el primer techo de la hoja de ruta, el ancho de banda de memoria de la CPU.

**Non-Goals:**

- Los comandos del motor (`simply run`, `simply inspect`, `simply tokenize`). Llegarán con las fases que los implementan: la ayuda solo enumera lo que funciona.
- El ancho de banda de VRAM. Necesita los enlaces con la Driver API de CUDA, que son de la Fase 3. Queda pendiente para un issue de seguimiento, porque el Hito 0 también lo pide.
- `simply bench model`, al estilo de `llama-bench` (*prefill* y *decode* en tokens/s), y `simply bench ops`, con microbenchmarks de operadores. Dependen de código que aún no existe.
- Ejecutar benchmarks en procesos aparte, por ejemplo para comparar *runtimes* o variables de entorno por *job*.
- Distribuir `simply` (herramienta global de `dotnet`, fichero único o NativeAOT).
- Mover o reescribir los ejemplos de `docs/ejemplos/` (ver la decisión 8).

## Decisions

### 1. System.CommandLine para la línea de comandos

Se usa `System.CommandLine` 3.0.0, publicado el 10 de noviembre de 2026, para `net10.0` y sin dependencias (comprobado en NuGet).

- **Por qué.** Es la biblioteca de Microsoft y la que usa la propia CLI de `dotnet`. Es estable desde la 2.0.0 (noviembre de 2025), ofrece subcomandos, opciones tipadas, ayuda generada, sugerencias ante errores de escritura y autocompletado, y es compatible con *trimming* y AOT. Su API es explícita: el árbol de comandos se construye con objetos (`RootCommand`, `Command`, `Option<T>`, `Argument<T>`) y cada comando registra su acción con `SetAction`. Así se ve qué opciones existen y cómo se enlazan, sin reflexión.
- **Comprobado en el prototipo.** Compila sin avisos con las reglas del repositorio. `simply --help` y `simply bench memory --help` generan la ayuda, localizada al español según el sistema. `simply bench memroy` responde «'memroy' no se encontró. ¿Quiso decir uno de los siguientes? memory» con código de salida 1. `simply` sin argumentos informa de que falta el comando y termina con 1 (en cuanto el árbol tiene algún subcomando; sin ninguno no hace nada y termina con 0), y `simply --version` imprime `1.0.0` seguido de `+` y el commit, que el SDK añade a la versión informativa al compilar desde un clon de git.
- **Alternativas.**
  - *Spectre.Console.Cli* (0.57.2): es el equivalente más cercano a picocli, con comandos como clases y opciones como propiedades con atributos, y produce tablas y barras de progreso muy cuidadas. Se descarta por depender de reflexión y por seguir en la serie 0.x, con la 1.0 en alfa.
  - *CommandLineParser* (2.9.1): también funciona con atributos, como picocli. Es muy usada, BenchmarkDotNet la usa internamente, pero apenas evoluciona.
  - *ConsoleAppFramework* (5.8.1): usa un generador de código y no aloja memoria. Es muy rápida, pero menos común, y la velocidad del análisis de argumentos no importa aquí.
  - *Analizar los argumentos a mano*: no es una pieza del motor, así que no aporta aprendizaje y obligaría a reimplementar la ayuda y los errores.

### 2. Un ejecutable `simply` con un comando por clase

- `Simply.Cli` fija `<AssemblyName>simply</AssemblyName>`. El proyecto conserva su nombre y el ejecutable queda en `artifacts/bin/Simply.Cli/<configuración>/simply`.
- `Program.Main` se limita a `SimplyCommand.Create().Parse(args).Invoke()`. `SimplyCommand.Create()` construye el árbol, y cada comando vive en su propia clase bajo `src/Simply.Cli/Commands/` (por ejemplo, `BenchCommand`). Las fases siguientes añaden sus comandos con una clase nueva y una línea en `SimplyCommand`.
- `Simply.Cli` expone sus tipos internos a `Simply.Cli.Tests` con `InternalsVisibleTo`, para probar el árbol sin hacerlo público.
- **Códigos de salida.** 0 si todo va bien; distinto de 0 ante errores de análisis (System.CommandLine usa 1), validaciones fallidas o benchmarks que no terminan.
- **Sin comandos de relleno.** `simply run` no se añade como esqueleto que responda «todavía no»: la ayuda enumera solo comandos que funcionan.

### 3. `Simply.Benchmarks`, una biblioteca en `src/`

- `src/Simply.Benchmarks` es una biblioteca de clases. Referencia a `Simply.Core` y a `BenchmarkDotNet`, y la referencia `Simply.Cli`.
- La revisión proponía un proyecto de consola aparte (`benchmarks/Simply.Benchmarks`). Se descarta porque los benchmarks se lanzan desde `simply bench`, y un segundo ejecutable volvería al modelo de llama.cpp que se quiere evitar.
- **Reparto del trabajo.**
  - `Simply.Cli` solo analiza la línea de comandos y traduce las opciones a una petición.
  - `Simply.Benchmarks` contiene las clases de benchmark y un punto de entrada por suite (por ejemplo, `MemorySuite.Run(options)`) que monta la configuración de BenchmarkDotNet, ejecuta la suite e interpreta el resultado.
  - Así, `Simply.Cli` no depende de los tipos de BenchmarkDotNet.

### 4. Ejecución dentro del proceso

Las suites usan el *toolchain* en proceso de BenchmarkDotNet (`InProcessEmitToolchain`).

- **Por qué (comprobado).** Con el *toolchain* por defecto, BenchmarkDotNet genera un proyecto, lo compila con el SDK y lo lanza en otro proceso. Desde `dotnet run -c Release` dentro del repositorio funciona. Con `simply` publicado y lanzado desde otro directorio falla con «Unable to find Simply.Benchmarks in … and its subfolders». En proceso, la misma suite funciona en los dos casos.
- **Resultados equivalentes (comprobado, `--quick`).** El bloque de 256 KiB tarda 2,85 µs con los dos *toolchains*. El de 1 GiB tarda de 39,9 a 43,4 ms, dentro del margen de error de una ejecución corta.
- **Lo que se pierde.** El aislamiento entre benchmarks y la posibilidad de definir *jobs* con otro *runtime* u otras variables de entorno. Para comparar rutas SIMD basta con lanzar todo el proceso con la variable. Con `DOTNET_EnableAVX2=0 simply bench memory --quick`, BenchmarkDotNet informa de `VectorSize=128`. El bloque de 256 KiB pasa de 2,85 a 8,75 µs, porque el cálculo pesa cuando los datos están en caché, mientras que el de 1 GiB apenas cambia (de 39,9 a 47,4 ms), porque la RAM es el límite (comprobado).

### 5. Solo compilaciones optimizadas

- BenchmarkDotNet se niega a medir un ensamblado sin optimizar (comprobado en Debug: «Assembly Simply.Benchmarks which defines benchmarks is non-optimized»).
- Se mantiene esa validación en lugar de desactivarla o de forzar `<Optimize>` en `Simply.Benchmarks`. Lo que se mide está en `Simply.Core`, y medir su versión Debug no tiene sentido.
- La suite detecta el fallo de validación y devuelve un código de salida distinto de 0, con un mensaje que pide compilar en Release: `dotnet run -c Release --project src/Simply.Cli -- bench memory`.

### 6. La suite `memory`

- **Benchmark.** `MemoryReadBenchmarks` suma un bloque de `float` con `Vector256` y dos acumuladores independientes, para no encadenar cada suma con la anterior. Devolver la suma impide que el JIT elimine las lecturas.
- **Memoria.** El bloque se reserva fuera del *heap* gestionado con `NativeMemory.AlignedAlloc`, alineado a 64 bytes (una línea de caché), en `[GlobalSetup]`, y se libera en `[GlobalCleanup]`. El bucle medido no aloja nada.
- **Parámetros.**
  - Tamaño: 256 KiB, que cabe en la L2 de 512 KiB por núcleo; 8 MiB, que cabe en la L3 de 16 MiB; y 1 GiB, que solo cabe en la RAM.
  - Hilos: 1 y `Environment.ProcessorCount`. Con varios hilos, cada uno suma un tramo contiguo del bloque.
  - La cifra de 1 GiB con todos los hilos es la que se compara con el techo de RAM de la hoja de ruta.
- **Primeras cifras (comprobado con la suite terminada y `--quick`).**
  - Con un hilo: 92 GB/s en L2, 84 GB/s en L3 y 26 GB/s en RAM.
  - Con los 12 hilos: 65 GB/s con 256 KiB, 253 GB/s con 8 MiB y 35 GB/s con 1 GiB.
  - Un solo hilo no satura la RAM: con todos se acerca al techo de 36,5 GB/s de la hoja de ruta.
  - Con 256 KiB, repartir entre 12 hilos es más lento que con uno, porque el coste de sincronizar `Parallel.For` (unos microsegundos) supera al de leer el bloque. La misma cuenta habrá que hacerla al repartir los GEMV entre hilos en la Fase 2.
- **Salida.** Una columna propia de BenchmarkDotNet (`IColumn`) calcula los GB/s a partir del tamaño y del tiempo medio.
- **Opciones.**
  - `--quick` usa el *job* corto de BenchmarkDotNet (`ShortRun`, 3 iteraciones). En el prototipo, los tres tamaños con un hilo tardaron 21 s, frente a varios minutos con el *job* por defecto.
  - `--artifacts <dir>` cambia el directorio de informes. Por defecto es `BenchmarkDotNet.Artifacts/` dentro del directorio actual, que `.gitignore` ya excluye.
- **Prueba sin medir.** `Simply.Cli.Tests` comprueba que una pasada por un bloque conocido devuelve la suma esperada, incluido un tamaño con resto respecto al ancho del vector.

### 7. `Simply.Cli.Tests`, un proyecto de tests aparte

- Prueba que el árbol de comandos analiza sin errores las líneas válidas (`bench memory --quick --artifacts x`) y rechaza las inválidas (`bench memroy`, ningún comando), sin ejecutar acciones. También prueba el núcleo de la suite `memory` (decisión 6).
- **Comprobado.** Un proyecto de tests de `xunit.v3` puede referenciar el ejecutable `Simply.Cli`, y la solución compila sin avisos y pasa los tests.
- **Alternativa descartada.** Meter estas pruebas en `Simply.Core.Tests` obligaría al proyecto de tests del núcleo a depender de la interfaz.
- `Simply.Core.Tests` pierde la referencia a `BenchmarkDotNet`.

### 8. Los ejemplos de `docs/ejemplos/` se quedan donde están

No se mueven a `Simply.Benchmarks` como embriones de suites, por tres razones:

1. **Son la evidencia de la revisión inicial.** Su apéndice C y varias secciones los citan por ruta, y moverlos rompería esa trazabilidad.
2. **Miden código que todavía no está en el motor.**
   - `CpuProbes` mide un GEMV, la conversión de FP16 y el coste de P/Invoke.
   - `GpuKernelLaunch` y `GpuGemvRecipes` incluyen sus propios enlaces con CUDA y sus propios kernels.

   Un benchmark debe medir el código de `Simply.Core`, no una copia. Moverlos ahora metería piezas del motor en el proyecto de benchmarks, con la dependencia al revés.
3. **Parte de su contenido no son benchmarks.** `FlawedArena` reproduce fallos a propósito, la detección de SIMD es informativa, y `GpuGemvRecipes` comprueba corrección frente a una referencia.

Su papel es el de catálogo de suites futuras, que se escribirán cuando exista la pieza correspondiente en `Simply.Core`:

| Ejemplo | Suite futura | Cuándo |
|---|---|---|
| `CpuProbes`: GEMV de un hilo frente a `Parallel.For` y conversión de FP16 | `simply bench ops` (GEMV, conversiones) | Fases 2 y 3 |
| `GpuKernelLaunch`: coste de `cuLaunchKernel` | `simply bench ops` en GPU (latencia de lanzamiento) | Fase 3 |
| `GpuGemvRecipes`: GEMV con cuBLAS y con Q4_0 en GB/s | `simply bench ops` en GPU (GEMV por formato) | Fases 3 y 4 |

La única medición que no depende del motor es el ancho de banda de memoria, y ningún ejemplo la mide por separado. Por eso es la primera suite.

## Risks / Trade-offs

- [System.CommandLine 3.0.0 lleva cinco días publicado] → Se ha comprobado su API en el prototipo. Si da problemas, la serie 2.0.x (2.0.15) sigue mantenida y su API es la misma en lo que se usa aquí.
- [BenchmarkDotNet añade unos 18 MB (Roslyn, TraceEvent, Capstone…) a los 33 MB de `simply` publicado (comprobado)] → Se acepta. Medir es parte de la herramienta, y el tamaño no importa en la máquina de desarrollo. Si más adelante se distribuye `simply`, se puede separar en un paquete aparte.
- [El *toolchain* en proceso usa `System.Reflection.Emit`, que NativeAOT no admite (sin verificar)] → Mientras `simply` incluya BenchmarkDotNet, no se publicará con NativeAOT. La distribución es un *Non-Goal* de este cambio.
- [Sin aislamiento entre procesos, un benchmark puede dejar efectos sobre el siguiente (JIT por niveles, caché, memoria)] → Las suites reservan y liberan su memoria en `[GlobalSetup]`/`[GlobalCleanup]`, y BenchmarkDotNet calienta cada caso antes de medir.
- [Las cifras dependen del estado de la máquina (frecuencia, energía, otras cargas)] → Los informes de BenchmarkDotNet registran el entorno (CPU, *runtime*, `HardwareIntrinsics`), y `--quick` se presenta como estimación.
- [La ayuda de System.CommandLine sale en el idioma del sistema] → Se acepta. Las descripciones propias se escriben en inglés, como el resto del código.

## Migration Plan

No hay usuarios ni datos que migrar. Quien use `dotnet run --project src/Simply.Cli` sin argumentos verá la ayuda y un código de salida 1, en lugar de un proceso que no hace nada.

## Open Questions

- Los tamaños de la suite `memory` están pensados para la máquina de desarrollo (L2 de 512 KiB, L3 de 16 MiB). Más adelante se podrían deducir de la caché detectada. No cambia la estructura ni las specs.
