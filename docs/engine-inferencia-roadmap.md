# Simply — Hoja de ruta

Simply es un motor de inferencia de modelos de lenguaje escrito desde cero en C# 14 / .NET 10 y CUDA 13, al estilo de llama.cpp. No pretende competir con llama.cpp, sino entender cada pieza de un motor construyéndola: cómo se guarda un modelo en disco, cómo se convierte el texto en tokens, qué cálculos produce cada token nuevo, por qué la velocidad la marca la memoria y no la aritmética, y cómo se escriben kernels de GPU que aprovechen esa memoria.

El recorrido va del fichero al texto generado, primero en CPU y después en GPU, y termina con una cuantización y unos kernels propios. Cada fase acaba en un hito que se comprueba contra un oráculo, llama.cpp ejecutando el mismo modelo, para no construir nunca sobre una base que no se sabe correcta.

> Los datos del modelo, las cifras del equipo y las recetas de cuBLAS y CUDA de este documento se comprobaron el 2026-11-15 sobre los ficheros y el hardware reales. Lo que no se ha comprobado se marca como *sin verificar*. Los programas que respaldan las mediciones están en [ejemplos/](ejemplos/), y el razonamiento detrás de muchas decisiones, en la [revisión inicial](revision-inicial.md).

## Mapa

| Fase | Qué construyes | Qué aprendes | Hito |
|---|---|---|---|
| 0. Banco de pruebas | CI, llama.cpp como oráculo, descarga de modelos y medición del hardware | Cómo se valida un motor; el modelo *roofline* | Sabes qué resultado es correcto y cuál es el techo de velocidad |
| 1. Del fichero a los tokens | Lector GGUF con `mmap`, inspector de modelos y tokenizador BPE | Formato de los modelos, disposición de los tensores, tokenización a nivel de bytes | Tokens idénticos a los de llama.cpp |
| 2. Transformer en CPU | Paso hacia delante en FP32, caché KV, bucle de generación y chat | Arquitectura del transformer, atención con GQA, RoPE, *sampling* | Logits y generación *greedy* idénticos a llama.cpp, sin alojamientos por token |
| 3. GPU con cuBLAS | Backend CUDA en FP16: cuBLAS para los productos de matrices y kernels sencillos para el resto | Modelo de ejecución de la GPU, interop, precisión mixta | El mismo resultado que en CPU, a más de 100 tokens/s |
| 4. Cuantización y kernels propios | Cuantizador por bloques y GEMV de 4 bits con decuantización al vuelo | Cuantización, coalescencia de memoria, reducciones dentro de un *warp* | Cuantizador idéntico al de llama.cpp y kernel más rápido que cuBLAS en FP16 |

Cada fase sigue el mismo esquema:

- **Objetivo**: qué funciona al terminar.
- **Conceptos**: lo que vas a entender por el camino.
- **Construcción**: los pasos, en el orden recomendado.
- **Hito**: criterios comprobables, con casillas para seguir el progreso.
- **Trampas**: errores conocidos; casi todos se han comprobado en este equipo.
- **Para profundizar**: opcional.

Cada fase se reparte en varios cambios de OpenSpec, y los criterios de su hito sirven como escenarios para las specs.

## Principios

1. **Construir lo que define un motor y delegar el resto.** Las piezas que son objeto de aprendizaje se escriben desde cero; la infraestructura genérica puede venir de paquetes (ver [Dependencias](#dependencias)).
2. **Primero correcto, después rápido.** La implementación en CPU y FP32 es la referencia: todo lo demás se compara con ella o con llama.cpp. Una optimización solo entra si un test garantiza que no cambia los resultados.
3. **Cero alojamientos por token.** Cargar el modelo puede crear `string`, diccionarios y objetos. El bucle de generación no aloja nada en el *heap* gestionado, y un test lo comprueba midiendo `GC.GetAllocatedBytesForCurrentThread()` (o `GC.GetTotalAllocatedBytes()` si hay varios hilos) alrededor de varios pasos.
4. **Medir contra el techo físico.** Generar un token lee todos los pesos una vez, así que la velocidad de decodificación la limita el ancho de banda de la memoria, no la capacidad de cálculo. Cada medición de tokens/s va acompañada de los GB/s efectivos y del porcentaje del techo.
5. **Interop explícita.** Las llamadas nativas usan siempre `[LibraryImport]`. `[SuppressGCTransition]` se reserva para funciones triviales, de menos de 1 µs y sin bloqueos, en las que una medición demuestre que compensa. En CUDA no compensa: un `cuLaunchKernel` cuesta 1,7 µs con o sin el atributo, y en una llamada que bloquea dejaría esperando al GC de todo el proceso.

## Dependencias

- **Se permiten** paquetes NuGet que aportan infraestructura genérica que no es objeto de aprendizaje. El caso principal es `System.Numerics.Tensors`, con `TensorPrimitives` (`Dot`, `Sum`, `Max`, `Exp`, `ConvertToSingle`…). No forma parte de la BCL: se añade como paquete.
- **No se permiten** en producción paquetes que implementen piezas del motor: librerías de inferencia (`LLamaSharp`, `ONNX Runtime`, `TorchSharp`, `ML.NET`), lectores de GGUF, tokenizadores, cuantizadores o kernels. Regla práctica: si esta hoja de ruta lo plantea como algo que construir, no se importa.
- **llama.cpp** es una herramienta externa, oráculo y referencia de rendimiento, nunca una dependencia.
- Cada paquete nuevo de producción se aprueba en un cambio de OpenSpec y fija su versión en `Directory.Packages.props`.

## Equipo de referencia y techos de velocidad

| Componente | Valor |
|---|---|
| CPU | AMD Ryzen 5 5625U: 6 núcleos y 12 hilos, AVX2 y FMA, **sin AVX-512**, L3 de 16 MiB |
| RAM | 64 GB; lectura medida de 27 GB/s con un hilo y 36,5 GB/s con 12 |
| GPU | NVIDIA GeForce RTX 3050 Laptop: Ampere (`sm_86`), 16 multiprocesadores, L2 de 1,5 MiB |
| VRAM | 4 GiB nominales y 3,68 GiB visibles para CUDA; pico teórico de 188 GB/s y lectura medida de 184 GB/s |
| Software | .NET SDK 10.0.301; CUDA 13.3 con `nvcc`, cuBLAS, `nsys` y `ncu` |

La lectura de RAM se midió sumando un búfer de 1 GiB con `TensorPrimitives.Sum`, repartido entre hilos dedicados. La de VRAM, con un kernel que lee 512 MiB en palabras de 16 bytes. Repetir ambas mediciones es el primer ejercicio de la Fase 0.

Para generar un token hay que leer todos los pesos salvo la tabla de embeddings, de la que solo se lee una fila. Ningún motor puede superar este techo con Qwen2.5-0.5B en este equipo:

| Pesos | Bytes por token | Techo en CPU (36,5 GB/s) | Techo en GPU (184 GB/s) |
|---|---|---|---|
| F32 | 1,98 GB | 18 tokens/s | — |
| F16 | 0,99 GB | 37 tokens/s ¹ | 186 tokens/s |
| Q8_0 | 0,53 GB | 70 tokens/s ¹ | 350 tokens/s |
| Q4_0 (fichero oficial) | 0,35 GB | 105 tokens/s ¹ | 530 tokens/s |

¹ Solo si convertir o decuantizar no pasa a ser el cuello de botella. En CPU, convertir de F16 a F32 cuesta entre dos y tres veces más por elemento que el propio producto escalar (comprobado).

Dos matices:

- **La caché KV también se lee en cada token**, y crece con el contexto: 12 KiB por posición en F16, es decir, 96 MiB con 8192 posiciones, un 10 % de los pesos en F16.
- **El *prefill* tiene otro techo.** El prompt se procesa en bloque (GEMM) y cada peso leído se reutiliza para todos sus tokens, así que el límite pasa a ser el cálculo. Por eso *prefill* y decodificación se miden por separado.

Como referencia de lo alcanzable, cuBLAS hace un GEMV en FP16 de 4864×896 a 167 GB/s, el 91 % de la lectura medida (comprobado).

## Modelos de referencia

### Qwen2.5-0.5B-Instruct, el modelo principal

Se usa el repositorio oficial `Qwen/Qwen2.5-0.5B-Instruct-GGUF`, con versiones `fp16` (1,18 GiB), `q8_0` (644 MiB) y `q4_0` (409 MiB). Esto es lo que declara su GGUF:

| Dato | Valor | Implicación |
|---|---|---|
| Arquitectura | `qwen2` | El grafo de llama.cpp está en `src/models/qwen2.cpp` |
| Capas | 24 | |
| Dimensión del modelo | 896 | |
| Cabezas de atención | 14 de Q y 2 de K/V, de 64 dimensiones | GQA: cada cabeza de K/V sirve a 7 de Q |
| FFN | 4864 | SwiGLU con matrices *gate*, *up* y *down* |
| Contexto | 8192 | El `config.json` de Hugging Face declara 32 768 |
| RoPE | base 1 000 000, variante NEOX | Ver la Fase 2 |
| RMSNorm | ε = 1e-6 | |
| *Biases* | En Q, K y V; no en la proyección de salida | 72 tensores de *bias* |
| Tokenizador | BPE a nivel de bytes (`gpt2`, pre-tokenizador `qwen2`); 151 387 *merges* | |
| Vocabulario | 151 936 entradas: 151 643 normales, 22 especiales y 271 de relleno sin uso | |
| Plantilla de chat | ChatML, guardada como Jinja en `tokenizer.chat_template` | |

Los tokens especiales que importan son `<|endoftext|>` (151643), `<|im_start|>` (151644) e `<|im_end|>` (151645), que marca el fin de secuencia. No hay token de inicio (`add_bos_token = false`).

En la versión `fp16`, las matrices están en F16 y los vectores (normalizaciones y *biases*) en F32.

**Embeddings atados.** Qwen2.5-0.5B comparte la matriz de embeddings de entrada y la de salida (`tie_word_embeddings`), pero el GGUF oficial guarda `output.weight` aparte; por eso declara 630 M parámetros en vez de 494 M. El cargador debe usar `output.weight` si existe y `token_embd.weight` si no, que es lo que hace llama.cpp.

**Versión F32 para la Fase 2.** Con un GGUF en F32 el motor mapea los pesos y calcula sobre ellos sin convertir nada, y llama.cpp puede usar exactamente el mismo fichero. `llama-quantize` admite F32 como tipo de salida (comprobado en su código; el comando no se ha ejecutado):

```bash
llama-quantize qwen2.5-0.5b-instruct-fp16.gguf qwen2.5-0.5b-instruct-f32.gguf F32
```

El resultado ocupa unos 2,5 GB.

### stories260K, un banco de pruebas mínimo (opcional)

`stories260K.gguf` (1,2 MB, en el repositorio `ggml-org/models`, carpeta `tinyllamas`) es un Llama diminuto que Andrej Karpathy entrenó sobre TinyStories para su proyecto llama2.c. Tiene 5 capas, dimensión 64, 8 cabezas de Q y 4 de K/V, FFN de 172, vocabulario de 512 y todos los tensores en F32.

Un paso completo cuesta unas 260 000 multiplicaciones, así que sirve para depurar el paso hacia delante capa a capa alimentándolo directamente con IDs de tokens. Se diferencia de Qwen en tres cosas: usa un tokenizador SentencePiece (`tokenizer.ggml.model = llama`), RoPE en variante NORM y no tiene *biases*.

### Modelos mayores para la Fase 4

| Modelo | F16 | Q8_0 | Q4_0 |
|---|---|---|---|
| Qwen2.5-1.5B-Instruct | 3,32 GiB: no cabe con margen en 3,68 GiB | 1,76 GiB | 0,99 GiB |
| Qwen2.5-3B-Instruct | más de 6 GB: no cabe (estimación) | 3,37 GiB | 1,86 GiB |

Los modelos se guardan en `artifacts/models/`, que git ignora, y los descarga un script de `eng/` que verifica su SHA-256.

## El oráculo: llama.cpp

| Herramienta | Para qué sirve |
|---|---|
| `llama-tokenize` | IDs de los tokens de un texto. Opciones: `-p`, `-f` o `--stdin` para la entrada, `--ids`, `--no-bos` y `--no-parse-special` para tratar los tokens especiales como texto |
| `llama-eval-callback` | Imprime cada tensor intermedio del grafo con su nombre (`attn_norm-0`, `Qcur-0`…), su forma, unos pocos valores de los extremos con 4 decimales y su suma. Basta para localizar la primera operación que se desvía |
| `llama-completion` y `llama-cli` | Generación; con temperatura 0 es *greedy* |
| `llama-perplexity` | Perplejidad sobre un texto: la medida de calidad de la cuantización |
| `llama-bench` | Velocidad de *prefill* y de decodificación: la referencia a la que acercarse |
| `llama-quantize` | Conversión entre tipos: F32, F16, Q8_0, Q4_0… |

Los nombres de las herramientas y sus opciones se han comprobado en el código fuente de llama.cpp, pero ninguna se ha ejecutado todavía en este equipo. Ejemplos orientativos:

```bash
llama-tokenize -m artifacts/models/qwen2.5-0.5b-instruct-fp16.gguf -p "Hola mundo" --ids
llama-eval-callback -m artifacts/models/qwen2.5-0.5b-instruct-f32.gguf -p "Hola mundo"
llama-perplexity -m artifacts/models/qwen2.5-0.5b-instruct-q4_0.gguf -f texto.txt
llama-bench -m artifacts/models/qwen2.5-0.5b-instruct-q4_0.gguf
```

Buenas prácticas:

- **Fija el commit de llama.cpp** y anótalo, junto con el SHA-256 del modelo, en cada fixture que generes con él.
- **Compílalo dos veces.** Una versión solo para CPU genera los fixtures numéricos sin que ninguna operación vaya a la GPU; otra con CUDA sirve de referencia de rendimiento.
- **Ten en cuenta cómo calcula.** En CPU, llama.cpp multiplica los pesos F16 por activaciones en F32, y para Q4_0 cuantiza antes las activaciones a Q8_0 (comprobado en `ggml/src/ggml-cpu/ggml-cpu.c`). Fija las tolerancias sabiéndolo.
- **Versiona las salidas, no los modelos.** Las salidas del oráculo se guardan como fixtures de texto pequeños en `tests/`.

## Fase 0 — Banco de pruebas

**Objetivo.** Antes de escribir el motor, tener con qué saber si un resultado es correcto y cuánto podría llegar a correr.

**Conceptos.** Integración continua, oráculos y fixtures, el modelo *roofline*.

**Construcción.**

1. **CI** en GitHub Actions (`pull_request` y `workflow_dispatch`) con `dotnet format --verify-no-changes`, `dotnet build -warnaserror` y `dotnet test`. Los *runners* no tienen modelos ni GPU, así que excluye los tests marcados con los *traits* `RequiresModel` y `RequiresGpu`.
2. **Script de modelos** en `eng/`, que descargue los modelos de referencia a `artifacts/models/` y verifique su SHA-256.
3. **llama.cpp** compilado en sus dos variantes, con el commit fijado.
4. **Medición del hardware**: ancho de banda de RAM y VRAM con microbenchmarks propios, y velocidad de llama.cpp con `llama-bench`.

**Hito 0.**

- [ ] El CI valida cada PR.
- [ ] Un solo comando descarga y verifica los modelos.
- [ ] Has medido tú mismo el ancho de banda de RAM y VRAM, y la tabla de techos de este documento está confirmada o corregida.
- [ ] Las cifras de `llama-bench` para Qwen2.5-0.5B en F16 y Q4_0, en CPU y en GPU, están anotadas.

**Para profundizar.** El modelo *roofline* (ver [Referencias](#referencias)).

## Fase 1 — Del fichero a los tokens

**Objetivo.** Abrir un GGUF real sin copiar los pesos, entender qué contiene y convertir texto en tokens, y tokens en texto, exactamente como lo hace el modelo.

**Conceptos.** Formato GGUF, ficheros mapeados en memoria, disposición de los tensores, tipos cuantizados, BPE a nivel de bytes, pre-tokenización, tokens especiales, decodificación incremental de UTF-8.

### 1.1 Lector GGUF

- **Cabecera:** la marca `GGUF`, la versión (3) y el número de tensores y de pares clave-valor, ambos u64. El formato es little-endian. Existen variantes big-endian, como `stories260K-be.gguf`: recházalas con un error claro.
- **Metadatos:** hay 13 tipos de valor: enteros de 8 a 64 bits, `f32`, `f64`, `bool`, cadenas y arrays. Una cadena es una longitud u64 seguida de UTF-8, sin terminador. Un array lleva su tipo de elemento y su longitud.
- **Descriptores de tensores:** nombre, número de dimensiones (como máximo 4), dimensiones u64, tipo `ggml_type` (u32) y desplazamiento u64 desde el inicio de la sección de datos.
- **Disposición:** `dims[0]` es la dimensión contigua. `blk.0.ffn_up.weight`, con dimensiones `[896, 4864]`, son 4864 filas de 896 valores: una fila por neurona de salida, de modo que `y[r] = dot(fila r, x)`. La tabla de embeddings, `[896, 151936]`, tiene una fila por token.
- **Alineación:** la sección de datos empieza en el siguiente múltiplo de `general.alignment` (32 por defecto), y cada tensor también. En el GGUF de Qwen empieza en el byte 5 947 744, múltiplo de 32 pero no de 64. Los pesos mapeados no están alineados a 64 bytes, así que las cargas SIMD sobre ellos deben ser no alineadas.
- **Mapeo:** `MemoryMappedFile.CreateFromFile`, `CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read)` y `SafeMemoryMappedViewHandle.AcquirePointer`, sumando el `PointerOffset` de la vista.

`TensorView` no puede llevar un `ReadOnlySpan<long>`: lo convertiría en `ref struct`, y un `ref struct` no se puede guardar en un array ni en un diccionario. Basta un `struct` normal con las dimensiones en un `[InlineArray]` (compila con las reglas del repositorio; comprobado):

```csharp
[InlineArray(4)]
public struct TensorDims
{
    private ulong _element0;
}

public readonly unsafe struct TensorView(string name, uint type, int rank, TensorDims dims, byte* data)
{
    public string Name { get; } = name;      // created while loading, outside the token loop
    public uint Type { get; } = type;        // ggml_type: 0 = F32, 1 = F16, 2 = Q4_0, 8 = Q8_0, 30 = BF16
    public int Rank { get; } = rank;         // 1 to 4
    public TensorDims Dims { get; } = dims;  // Dims[0] is the contiguous dimension
    public byte* Data { get; } = data;       // mapped base + data section start + tensor offset
}
```

### 1.2 Inspector

`simply inspect <modelo.gguf>` muestra la arquitectura, los hiperparámetros, el tokenizador y la tabla de tensores (nombre, tipo, forma y bytes). Es el primer resultado visible del proyecto y una herramienta de depuración para todo lo que viene después.

### 1.3 Tokenizador BPE

1. **Separa los tokens especiales.** Busca en el texto los 22 tokens especiales (`<|im_start|>`, `<|im_end|>`, `<tool_call>`…) antes que nada; lo que queda entre ellos se tokeniza como texto. Decide si el texto del usuario puede contenerlos: si puede, un usuario puede escribir `<|im_start|>system` y suplantar al sistema en el chat.
2. **Pre-tokeniza** cada trozo con la expresión regular de Qwen2, tal como la usa llama.cpp:

   ```text
   (?:'[sS]|'[tT]|'[rR][eE]|'[vV][eE]|'[mM]|'[lL][lL]|'[dD])|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+
   ```

   En .NET funciona con `[GeneratedRegex]` y `EnumerateMatches` sin alojar memoria (comprobado). Pero el motor de .NET trabaja con unidades UTF-16, y `\p{L}` no reconoce las letras que quedan fuera del plano básico de Unicode, porque ocupan dos unidades (comprobado con U+1D400). Inclúyelas en el corpus de pruebas.
3. **Pasa cada fragmento a bytes UTF-8** y cada byte a su carácter del alfabeto de GPT-2; por ejemplo, el espacio es `Ġ`. El vocabulario y los *merges* están escritos en ese alfabeto.
4. **Aplica los *merges*.** El rango de un par es su posición en `tokenizer.ggml.merges`. En cada fragmento se fusiona, una y otra vez, el par adyacente de menor rango.
5. **Decodifica** recorriendo el camino inverso: token, alfabeto de GPT-2, bytes y UTF-8. Un token puede acabar a mitad de un carácter (emojis, CJK), así que la salida en *streaming* debe guardar los bytes de un carácter incompleto hasta que llegue el resto.

Qwen no añade token de inicio. Para buscar en el vocabulario sin crear `string`, usa `Dictionary<string, int>.GetAlternateLookup<ReadOnlySpan<char>>()`.

**Hito 1.**

- [ ] El inspector muestra para Qwen2.5-0.5B los valores de la tabla de [modelos de referencia](#modelos-de-referencia).
- [ ] El tokenizador produce los mismos IDs que `llama-tokenize` sobre un corpus que incluye «Hola mundo», acentos, emojis, letras fuera del plano básico, CJK, espacios repetidos, saltos de línea, números, código, contracciones inglesas (`'s`, `'ll`) y tokens especiales.
- [ ] `decode(encode(texto)) == texto` en todo el corpus.

**Trampas.**

- La alineación es de 32 bytes, no de 64.
- `dims[0]` es la dimensión contigua: el orden es el inverso al de PyTorch.
- Las cadenas no llevan terminador.
- Hay ficheros big-endian.
- Los tokens especiales se separan antes de pre-tokenizar.
- `\p{L}` no reconoce letras fuera del plano básico.
- Los tipos de GGML llevan guion bajo (`Q4_0`, `Q8_0`) y la regla `CA1707` lo rechaza. Decide pronto una convención: supresión local en el `enum` o nombres como `Q40`.

**Para profundizar.** La especificación de GGUF, y el tokenizador SentencePiece de `stories260K`, que decide con puntuaciones en lugar de *merges* y recurre a tokens de byte (`<0x00>`…) para lo que no está en el vocabulario. Artículos de BPE y SentencePiece en [Referencias](#referencias).

## Fase 2 — El transformer en CPU

**Objetivo.** Obtener en CPU, en FP32, los mismos logits que llama.cpp para Qwen2.5-0.5B, y construir sobre ellos un bucle de generación con *sampling* y chat.

**Conceptos.** Transformer de solo decodificador, flujo residual, RMSNorm, atención causal con GQA, RoPE, caché KV, SwiGLU, logits, *prefill* frente a decodificación, estabilidad numérica, SIMD, paralelismo.

### 2.1 El paso hacia delante de Qwen2

Este es el grafo que construye llama.cpp en `src/models/qwen2.cpp`. A la derecha, los nombres que da a cada tensor intermedio y que muestra `llama-eval-callback` con el número de capa como sufijo:

```text
x = token_embd[token]                                     una fila de la tabla de embeddings
para cada capa l, de 0 a 23:
    h = RMSNorm(x) ⊙ attn_norm                                         attn_norm-l
    q = Wq·h + bq    k = Wk·h + bk    v = Wv·h + bv                 Qcur-l Kcur-l Vcur-l
    q, k = RoPE(q, k, posición)
    guardar k y v en la caché KV de la capa l
    a = softmax(q·Kᵀ / 8 + máscara causal)·V        por cabeza; 1 de K/V por cada 7 de Q
    x = x + Wo·a                                                         ffn_inp-l
    h = RMSNorm(x) ⊙ ffn_norm                                           ffn_norm-l
    x = x + Wdown·(SiLU(Wgate·h) ⊙ (Wup·h))                       ffn_out-l, l_out-l
logits = output·(RMSNorm(x) ⊙ output_norm)                   result_norm, result_output
```

Los detalles que deciden si el resultado coincide:

- **RMSNorm:** `y = x / sqrt(media(x²) + ε) ⊙ w`. llama.cpp acumula la suma de cuadrados en `double`.
- **RoPE:** para la posición `p` y el par `i`, el ángulo es `p · base^(−2i/64)`, con base 1 000 000. Hay dos variantes: NORM rota pares consecutivos, `(x[2i], x[2i+1])`, y NEOX rota `(x[i], x[i+32])`. Qwen2 usa NEOX; Llama, y por tanto stories260K, usa NORM. Con la variante equivocada el modelo produce texto plausible pero peor: es el error más típico.
- **Atención:** la escala es 1/√64 = 1/8. La cabeza `h` de Q usa la cabeza `h / 7` de K y V. En el *prefill* hace falta la máscara causal: cada posición solo ve las anteriores.
- **Softmax:** resta el máximo antes de exponenciar. `TensorPrimitives.SoftMax` no lo hace y devuelve NaN en cuanto alguna entrada pasa de unas 89 unidades (comprobado con `[100, 99]`).
- **Caché KV:** cada posición guarda 2 × 24 capas × 2 cabezas × 64 = 6144 valores, 24 KiB en F32. Se reserva al arrancar para el contexto máximo.

### 2.2 Construcción

1. **Memoria.** Una `NativeArena` para las activaciones de cada paso y la caché KV reservada al arrancar. La arena es una clase y no un `struct` mutable, porque una copia repartiría dos veces la misma memoria; calcula el tamaño en `nuint` y comprueba la capacidad. Compila con las reglas del repositorio y pasa sus pruebas (comprobado):

   ```csharp
   public sealed unsafe class NativeArena : IDisposable
   {
       private const nuint Alignment = 64;
       private readonly nuint _capacity;
       private byte* _base;
       private nuint _offset;

       public NativeArena(nuint capacityBytes)
       {
           _base = (byte*)NativeMemory.AlignedAlloc(capacityBytes, Alignment);
           _capacity = capacityBytes;
       }

       public Span<T> Alloc<T>(int count)
           where T : unmanaged
       {
           ObjectDisposedException.ThrowIf(_base is null, this);
           ArgumentOutOfRangeException.ThrowIfNegative(count);
           nuint start = (_offset + (Alignment - 1)) & ~(Alignment - 1);
           nuint bytes = (nuint)count * (nuint)sizeof(T); // computed in nuint: no int overflow
           if (start > _capacity || bytes > _capacity - start)
           {
               throw new InvalidOperationException("Arena capacity exceeded.");
           }

           _offset = start + bytes;
           return new Span<T>(_base + start, count);
       }

       public void Reset() => _offset = 0; // once per token

       public void Dispose()
       {
           NativeMemory.AlignedFree(_base);
           _base = null;
       }
   }
   ```

2. **Pesos.** El GGUF en F32 mapeado, sin copia, o el F16 convertido a F32 al cargar: 1,98 GB en RAM.
3. **Operadores.** Primero escalares y legibles; después con `TensorPrimitives`. Son el embedding, RMSNorm, GEMV, RoPE, la atención, SiLU, las sumas residuales y la proyección final a logits.
4. **Prefill.** Empieza procesando el prompt como una sucesión de pasos de decodificación, que es sencillo y correcto. Después, en bloque, como un GEMM.
5. **Generación.** Primero *greedy*: el token de mayor logit. Después, la cadena de *sampling* en el orden de llama.cpp: penalizaciones, top-k, top-p, min-p y temperatura, que va la última. llama.cpp intercala además DRY, top-n-σ, typical-p y XTC, desactivados por defecto. Sus valores por defecto son top-k 40, top-p 0,95, min-p 0,05, temperatura 0,8 y una penalización por repetición de 1,0 (desactivada) sobre los últimos 64 tokens. Recuerda que los logits son puntuaciones sin normalizar, no probabilidades.
6. **Chat.** La plantilla ChatML se puede escribir a mano; no hace falta un motor de Jinja. Con el mensaje de sistema por defecto de la plantilla de Qwen, un turno queda así:

   ```text
   <|im_start|>system
   You are Qwen, created by Alibaba Cloud. You are a helpful assistant.<|im_end|>
   <|im_start|>user
   {mensaje}<|im_end|>
   <|im_start|>assistant
   ```

   La generación se detiene en `<|im_end|>` o `<|endoftext|>`.
7. **Rendimiento.** Reparte el GEMV entre hilos propios, creados al arrancar: `Parallel.For` aloja entre 3 y 4 KB por llamada (comprobado). Añade el test de 0 B por token y mide tokens/s y GB/s.

**Hito 2.**

- [ ] Para un prompt corto, cada tensor intermedio coincide con `llama-eval-callback` (compilación solo para CPU y mismo modelo): valores impresos y sumas.
- [ ] Los logits del último token del prompt tienen un error relativo L2 menor que 1e-4 frente a llama.cpp. Es un umbral orientativo: calíbralo.
- [ ] La generación *greedy* coincide con la de llama.cpp durante 64 tokens en varios prompts. Si diverge, solo donde los dos mejores logits casi empatan.
- [ ] El chat responde con coherencia.
- [ ] Un test comprueba que se alojan 0 B por token.
- [ ] La velocidad de decodificación está medida y expresada como porcentaje del techo (18 tokens/s en F32).

**Trampas.**

- RoPE en variante NORM en lugar de NEOX.
- Softmax sin restar el máximo.
- Olvidar los *biases* de Q, K y V.
- Usar `token_embd` como proyección de salida cuando el fichero trae `output.weight`.
- Emparejar mal las cabezas en GQA.
- Posiciones que no empiezan en 0, o *prefill* sin máscara causal.
- `Parallel.For` en el bucle caliente.
- Esperar AVX-512: en este equipo `Vector512.IsHardwareAccelerated` es `false`.

**Para profundizar.**

- Escribe tu propio `Dot` con `Vector256` y `Fma` y compáralo con `TensorPrimitives`. Los intrínsecos de verdad hacen falta en la Fase 4, porque `TensorPrimitives` no sabe multiplicar bloques cuantizados.
- Prueba pesos en F16 convertidos al vuelo: la mitad de bytes a cambio de más cálculo. ¿Ganan a F32 con 6 núcleos? Mídelo.
- Lee `run.c` de llama2.c, un Llama completo en unas 700 líneas de C.
- [`ejemplos/CpuProbes`](ejemplos/CpuProbes/Program.cs) mide las variantes de GEMV, la conversión de F16 y el coste de la transición de GC.
- Artículos del transformer, RMSNorm, RoPE, GQA, SwiGLU y *sampling* en [Referencias](#referencias).

## Fase 3 — A la GPU con cuBLAS

**Objetivo.** El mismo resultado que en CPU, con los pesos en VRAM en FP16 y una velocidad cercana al techo de ancho de banda.

**Conceptos.** Modelo de ejecución de la GPU (rejillas, bloques, *warps*), jerarquía de memoria, ejecución asíncrona y *streams*, contextos, coste de lanzamiento, BLAS en orden por columnas, precisión mixta, coste de las copias por PCIe.

### 3.1 Interop con CUDA

- **Bastan el Driver API (`libcuda.so`) y cuBLAS (`libcublas.so`).** `libcublas.so` no depende de `libcudart.so`, y [`ejemplos/GpuGemvRecipes`](ejemplos/GpuGemvRecipes/Program.cs) reserva memoria con el Driver API y llama a cuBLAS sin tocar `libcudart` (comprobado).
- **Símbolos versionados.** `cuda.h` redefine 141 nombres, como `cuMemAlloc` → `cuMemAlloc_v2`, y el `EntryPoint` debe llevar el nombre real. La lista está en el apéndice B de la revisión; el flujo completo de un lanzamiento, en [`ejemplos/GpuKernelLaunch`](ejemplos/GpuKernelLaunch/Program.cs).
- **El contexto es estado por hilo.** Usa un hilo dedicado a la GPU, o llama a `cuCtxSetCurrent` en cada hilo que use CUDA.
- **Comprueba cada código de retorno.** Los errores de un kernel aparecen en la siguiente llamada que sincroniza.

### 3.2 Backend y pesos

- Extrae `IComputeBackend` cuando tengas dos implementaciones. La de CPU sigue siendo la referencia.
- Los pesos se suben una vez al cargar: los 1,26 GB de pesos en F16 caben en los 3,68 GiB visibles.
- Mantén en FP32 el flujo residual y las acumulaciones; usa FP16 para los pesos y, si acaso, para activaciones intermedias.

### 3.3 Productos de matrices con cuBLAS

cuBLAS trabaja en orden por columnas. Una matriz de GGUF (una fila por neurona de salida) leída por columnas es su traspuesta, así que todas las llamadas usan `CUBLAS_OP_T`. Estas recetas se comprobaron numéricamente en [`ejemplos/GpuGemvRecipes`](ejemplos/GpuGemvRecipes/Program.cs):

```text
GEMV FP32:  cublasSgemv_v2(handle, CUBLAS_OP_T, m = n_in, n = n_out, &alpha, W, lda = n_in, x, 1, &beta, y, 1)

GEMM FP16:  cublasGemmEx(handle, CUBLAS_OP_T, CUBLAS_OP_N, m = n_out, n = n_tokens, k = n_in,
                         &alpha, W, CUDA_R_16F, lda = n_in, X, CUDA_R_16F, ldb = n_in,
                         &beta, Y, CUDA_R_32F, ldc = n_out, CUBLAS_COMPUTE_32F, CUBLAS_GEMM_DEFAULT)
```

Con una matriz de 4864×896 y frente a una referencia en `double` con los mismos datos:

| Llamada | Error relativo L2 | Tiempo |
|---|---|---|
| `cublasSgemv_v2`, FP32 | 1,9e-7 | |
| `cublasGemmEx`, entradas FP16, cálculo y salida FP32 | 1,2e-7 | |
| `cublasGemmEx`, entradas y salida FP16, cálculo FP32 | 2,1e-4 | 52 µs (167 GB/s) |
| `cublasHgemm`, todo en FP16 | 1,5e-3 | |

Acumular en FP16 multiplica el error por siete. No existe `cublasHgemv`: el GEMV en FP16 es un GEMM con `n = 1`.

### 3.4 Kernels propios para el resto

- **Qué escribir:** RMSNorm, RoPE, la atención (puntuaciones, softmax y suma ponderada), la activación `SiLU(gate) ⊙ up`, la suma residual, la lectura de filas de embeddings y la suma de *biases*.
- **Cómo compilarlos:** `nvcc -cubin -arch=sm_86` genera código máquina para esta GPU, que el driver carga sin compilar (comprobado). Con PTX el código es portable, pero el driver lo compila al cargarlo: 11–27 ms la primera vez y después desde su caché (comprobado). Un *target* de MSBuild en `eng/` puede compilar `src/Simply.Kernels/*.cu` y dejar el resultado junto al ejecutable.
- **Primer experimento:** los productos de matrices en cuBLAS y el resto en CPU, copiando por PCIe. Mide cuánto cuesta: es la forma más directa de ver por qué todo el paso hacia delante tiene que vivir en la GPU. Después, pasa todo a la GPU.

### 3.5 El coste de lanzar kernels

Cada `cuLaunchKernel` cuesta unos 1,7 µs de CPU (comprobado). Con unos 450 lanzamientos por token (estimación: unos 18 por capa), suman 0,8 ms. Es un 15 % de los 5,4 ms que dura un token en el techo de F16, y un 40 % en Q4_0. Cuando la GPU termina un kernel antes de que la CPU encole el siguiente, el ritmo lo marca la CPU. Los remedios son fusionar kernels y usar CUDA Graphs.

**Hito 3.**

- [ ] Los logits en GPU tienen un error relativo L2 menor que 1e-3 frente a la referencia en CPU (orientativo), y la generación *greedy* coincide salvo en casi empates.
- [ ] La decodificación supera los 100 tokens/s, el 54 % del techo de 186, comparada con `llama-bench` en la misma GPU.
- [ ] Una traza de `nsys` muestra que dentro del bucle solo cruza el PCIe lo imprescindible.

**Trampas.**

- Enlazar el símbolo sin versión.
- Llamar a CUDA desde un hilo sin contexto.
- Olvidar el orden por columnas.
- Acumular en FP16.
- Errores asíncronos que aparecen lejos de su causa.
- Pasarse de los 3,68 GiB de VRAM.
- Copiar los logits en cada token: son 594 KiB. Elige el token en la GPU.

**Para profundizar.** CUDA Graphs; *streams* y solapamiento de copias con cálculo; memoria *pinned* para subir los pesos; FlashAttention; BF16, que Ampere soporta; *tensor cores* en el *prefill*. La traza de `nsys` y las métricas por kernel de `ncu` son las herramientas para todo ello.

## Fase 4 — Cuantización y kernels propios

**Objetivo.** Ejecutar con pesos de 4 bits y kernels propios, más rápido que en FP16 y con la pérdida de calidad medida. De paso, cargar modelos que en FP16 no caben.

**Conceptos.** Cuantización por bloques, escalas simétricas y asimétricas, error de cuantización frente a perplejidad, coalescencia de memoria, cargas vectorizadas, ocupación, reducciones con `__shfl_xor_sync`, productos de enteros con `__dp4a`, cuantización de activaciones.

### 4.1 Empieza por los formatos de GGML

El formato de 4 bits más sencillo, bloques de 32 pesos con una escala FP16 en 18 bytes, es el `block_q4_0` de GGML. Empezar por Q4_0 y Q8_0 enseña lo esencial de la cuantización por bloques y aporta un oráculo exacto; además, tus kernels podrán leer directamente los GGUF oficiales:

```csharp
[InlineArray(16)]
public struct PackedNibbles16
{
    private byte _element0;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BlockQ4 // 18 bytes, same layout as GGML's block_q4_0
{
    public Half Scale;
    public PackedNibbles16 Quants;
}
```

- **Orden de los *nibbles*:** el peso `i` va en el *nibble* bajo de `qs[i]` y el peso `i + 16` en el alto, no en pares consecutivos.
- **Cuantizador de referencia de GGML:** `d = max / −8`, donde `max` es el valor de mayor magnitud con su signo, y `q = min(15, (int)(x / d + 8.5))`. Conservar el signo hace que el extremo caiga exactamente en −8 y aprovecha los 16 niveles. La alternativa simétrica, `d = max_abs / 7`, solo usa 15 niveles.
- **Q8_0:** 32 enteros de 8 bits y una escala FP16, 34 bytes; `d = max_abs / 127` y `q = round(x / d)`.
- **Oráculo:** una traducción directa a Python de las funciones de referencia reprodujo byte a byte los ficheros oficiales de Qwen: 16 384 bloques Q4_0 de `token_embd` y 20 000 bloques Q8_0 de `output.weight` (comprobado). El fichero Q4_0 oficial deja `output.weight` en Q8_0 y los vectores en F32.

### 4.2 Tus propias variantes

Aquí empieza tu formato propio. Cuando Q4_0 sea exacto, experimenta: escala simétrica `max_abs / 7`, bloques de 64, escala y mínimo como en Q4_1, escalas en FP8… Mide la calidad con perplejidad y no con el error de los pesos: en un GEMV con pesos gaussianos, Q4_0 ya produce un 8 % de error relativo en la salida (comprobado), y aun así el modelo funciona. Escribe tu propia herramienta de perplejidad, la exponencial de la log-verosimilitud negativa media sobre un texto, y compárala con `llama-perplexity`.

### 4.3 El kernel GEMV de 4 bits

[`ejemplos/GpuGemvRecipes`](ejemplos/GpuGemvRecipes/kernels.cu) contiene un kernel ingenuo, el punto de partida del ejercicio: un *warp* por fila, cada hilo recorre bloques y al final se reduce con `__shfl_xor_sync`.

- **Es correcta:** 2e-4 de error frente a la CPU con los mismos bloques, debido al redondeo de la salida a FP16.
- **Es lenta:** 24–29 GB/s y 84–101 µs. Tarda más que cuBLAS en FP16 (52 µs) aunque lee 3,6 veces menos bytes (comprobado).

Ahí empieza el ejercicio. Algunas pistas:

- Hilos consecutivos deben leer direcciones consecutivas (coalescencia), en palabras de 4 o 16 bytes.
- Varias filas por bloque y más *warps* para ocultar la latencia de la memoria.
- `x` en registros o en memoria compartida.
- Cuantizar `x` a Q8 y usar `__dp4a`, que multiplica y suma cuatro pares de enteros de 8 bits.
- Medir con `ncu` el ancho de banda conseguido frente a los 184 GB/s.

La meta es pasar del 70 % del ancho de banda: unos 19 µs para esa matriz, casi tres veces más rápido que cuBLAS en FP16.

### 4.4 En CPU (opcional)

El producto Q4_0 × Q8_0 con AVX2. Aquí `TensorPrimitives` ya no ayuda y los intrínsecos de `System.Runtime.Intrinsics` son la herramienta. Es lo que hace llama.cpp, que cuantiza las activaciones a Q8_0 (comprobado en `ggml-cpu.c`).

**Hito 4.**

- [ ] El cuantizador Q4_0 y Q8_0 en C# reproduce byte a byte los ficheros oficiales.
- [ ] El kernel Q4 tiene un error relativo L2 menor que 1e-3 frente a la decuantización en CPU.
- [ ] El GEMV Q4 es más rápido que el GEMV de cuBLAS en FP16 sobre la misma matriz.
- [ ] Qwen2.5-0.5B en Q4_0 funciona entero en GPU, con una perplejidad muy cercana a la de `llama-perplexity` y una velocidad comparada con el techo (530 tokens/s) y con `llama-bench`.
- [ ] Qwen2.5-3B-Instruct en Q4_0 (1,86 GiB) funciona en la GPU de 4 GiB, donde en FP16 no cabría.

**Trampas.**

- El orden de los *nibbles*.
- El signo de la escala.
- Los bloques de 18 bytes no son múltiplo de 4, así que las cargas de 32 bits necesitan cuidado.
- La mayoría de los GGUF publicados usan K-quants (`Q4_K_M`), un formato distinto.

**Para profundizar.** Los K-quants (`Q4_K`, `Q6_K`) abren la puerta a casi cualquier GGUF publicado; la matriz de importancia de llama.cpp (`tools/imatrix`); GPTQ, AWQ y LLM.int8() en [Referencias](#referencias).

## Después de la Fase 4

- Servidor con una API compatible con la de OpenAI y respuestas en *streaming*.
- Varias peticiones a la vez: *batching* continuo y PagedAttention.
- Decodificación especulativa.
- FlashAttention para contextos largos.
- Otras arquitecturas (Llama 3, Qwen3, Gemma), escalado de RoPE para contextos largos y mezcla de expertos.

## Estrategia de pruebas

- **Unitarias, sin modelos:** GGUF sintéticos generados en el propio test, operadores frente a implementaciones escalares de referencia y ciclos de cuantización y decuantización.
- **Contra el oráculo** (*trait* `RequiresModel`): fixtures generados con llama.cpp, con su commit y el SHA-256 del modelo.
- **En GPU** (*trait* `RequiresGpu`): siempre frente al backend de CPU.
- **Alojamientos:** 0 B por token, comprobado en un test.
- **Rendimiento:** BenchmarkDotNet en Release para los operadores (la revisión, en su punto 1.4, propone un proyecto aparte) y `Stopwatch` en `Simply.Cli` para tokens/s y GB/s.
- **Comparaciones numéricas:** error relativo L2 (`‖a − b‖ / ‖b‖`) o similitud coseno, nunca igualdad exacta en coma flotante. La tabla de la sección 3.3 da órdenes de magnitud para un GEMV. En generación, las divergencias solo se aceptan en casi empates.

## Referencias

Código y especificaciones:

| Tema | Dónde | Fase |
|---|---|---|
| Formato GGUF | `docs/gguf.md` en `ggml-org/ggml` | 1 |
| Expresiones de pre-tokenización | `src/llama-vocab.cpp` en llama.cpp | 1 |
| Grafo de Qwen2 | `src/models/qwen2.cpp` en llama.cpp | 2 |
| RMSNorm y RoPE de referencia | `ggml/src/ggml-cpu/ops.cpp` en llama.cpp | 2 |
| Cadena de *sampling* y valores por defecto | `common/common.h` en llama.cpp | 2 |
| Inferencia mínima en C | `run.c` en `karpathy/llama2.c` | 2 |
| Tipos de las activaciones en CPU | `ggml/src/ggml-cpu/ggml-cpu.c` en llama.cpp | 2 y 4 |
| Formatos cuantizados | `ggml/src/ggml-common.h` y `ggml/src/ggml-quants.c` en llama.cpp | 4 |

Artículos:

| Tema | Título | Referencia | Fase |
|---|---|---|---|
| *Roofline* | Roofline: An Insightful Visual Performance Model for Multicore Architectures | *Communications of the ACM*, 2009 | 0 |
| BPE | Neural Machine Translation of Rare Words with Subword Units | arXiv 1508.07909 | 1 |
| SentencePiece | SentencePiece: A simple and language independent subword tokenizer and detokenizer for Neural Text Processing | arXiv 1808.06226 | 1 |
| Transformer | Attention Is All You Need | arXiv 1706.03762 | 2 |
| RMSNorm | Root Mean Square Layer Normalization | arXiv 1910.07467 | 2 |
| RoPE | RoFormer: Enhanced Transformer with Rotary Position Embedding | arXiv 2104.09864 | 2 |
| MQA | Fast Transformer Decoding: One Write-Head is All You Need | arXiv 1911.02150 | 2 |
| GQA | GQA: Training Generalized Multi-Query Transformer Models from Multi-Head Checkpoints | arXiv 2305.13245 | 2 |
| SwiGLU | GLU Variants Improve Transformer | arXiv 2002.05202 | 2 |
| Llama | LLaMA: Open and Efficient Foundation Language Models | arXiv 2302.13971 | 2 |
| Qwen2 | Qwen2 Technical Report | arXiv 2407.10671 | 2 |
| Qwen2.5 | Qwen2.5 Technical Report | arXiv 2412.15115 | 2 |
| Top-p | The Curious Case of Neural Text Degeneration | arXiv 1904.09751 | 2 |
| Min-p | Turning Up the Heat: Min-p Sampling for Creative and Coherent LLM Outputs | arXiv 2407.01082 | 2 |
| FlashAttention | FlashAttention: Fast and Memory-Efficient Exact Attention with IO-Awareness | arXiv 2205.14135 | 3 y después |
| Cuantización a 8 bits | LLM.int8(): 8-bit Matrix Multiplication for Transformers at Scale | arXiv 2208.07339 | 4 |
| GPTQ | GPTQ: Accurate Post-Training Quantization for Generative Pre-trained Transformers | arXiv 2210.17323 | 4 |
| AWQ | AWQ: Activation-aware Weight Quantization for LLM Compression and Acceleration | arXiv 2306.00978 | 4 |
| PagedAttention | Efficient Memory Management for Large Language Model Serving with PagedAttention | arXiv 2309.06180 | después |
| Decodificación especulativa | Fast Inference from Transformers via Speculative Decoding | arXiv 2211.17192 | después |
