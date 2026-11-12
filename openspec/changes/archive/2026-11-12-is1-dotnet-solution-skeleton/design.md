# Design

## Context

El repositorio cuenta actualmente con los convenios del proyecto en `AGENTS.md`, la hoja de ruta técnica en `docs/engine-inferencia-c#.md`, `.gitignore` y la configuración de OpenSpec en `openspec/config.yaml`, pero carece aún de la estructura de solución .NET. Consultar `proposal.md` para la motivación general. El entorno local dispone del SDK de .NET `10.0.301` y CUDA `13.3` sobre Linux (`linux-x64`), y tomamos como referencia de organización en C# / .NET 10 las convenciones probadas en `vagalume`.

## Goals / Non-Goals

**Goals:**
- Fijar el SDK de .NET 10 en `global.json` (`10.0.301`, `rollForward: latestFeature`) y el manifiesto de herramientas locales en `dotnet-tools.json`.
- Establecer las reglas de formato y estilo de código en `.editorconfig`, verificables con `dotnet format --verify-no-changes` y durante la compilación.
- Crear una solución limpia en formato `.slnx` (`Simply.slnx`) con las carpetas `/src/` y `/tests/`.
- Centralizar las propiedades del compilador y analizadores en `Directory.Build.props` (`<Version>1.0.0</Version>`, `net10.0`, C# 14, `Nullable`, `ImplicitUsings`, `AllowUnsafeBlocks`, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` y `AnalysisLevel` en `latest-recommended`).
- Habilitar *Central Package Management* (CPM) en `Directory.Packages.props` para fijar en un único archivo las versiones de `xunit.v3`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` y `BenchmarkDotNet`.
- Crear los directorios estándar `eng/` (para futuros scripts y `.targets` de ingeniería, como la compilación de kernels CUDA o gestión de fixtures) y `artifacts/` (ignorado en git, para salidas de compilación y binarios descargados), junto con `src/Simply.Core`, `src/Simply.Cli`, `src/Simply.Kernels` y `tests/Simply.Core.Tests`.

**Non-Goals:**
- Implementar la lectura de cabeceras GGUF, `MemoryMappedFile`, `TensorView`, tokenización o *sampling* (se abordarán en los siguientes cambios de la Fase 1).
- Implementar `NativeArena` u operadores SIMD (Fase 2) ni interop o compilación de kernels CUDA (Fases 3 y 4).
- Configurar el workflow de integración continua en GitHub Actions (`.github/workflows/`), que se abordará en su propio cambio.

## Decisions

### 1. Archivos JSON de raíz (`global.json` y `dotnet-tools.json`)
- **Decisión:** Añadir en la raíz:
  - `global.json` con `"sdk": { "version": "10.0.301", "rollForward": "latestFeature" }`.
  - `dotnet-tools.json` con `"version": 1, "isRoot": true, "tools": {}`.
- **Motivo:** Sigue la estructura estándar de `vagalume`: `global.json` garantiza que cualquier ejecución de `dotnet` use el SDK de .NET 10 esperado sin sorpresas, y `dotnet-tools.json` deja preparado el manifiesto raíz por si más adelante se incorpora alguna herramienta CLI local.

### 2. Formato de solución `.slnx` y directorios `eng/` y `artifacts/`
- **Decisión:** Utilizar `Simply.slnx` agrupando `/src/` y `/tests/`, y crear en la raíz los directorios `eng/` (versionado en git) y `artifacts/` (ignorado en `.gitignore`).
- **Motivo:** En proyectos .NET modernos (incluido el ecosistema de `dotnet/runtime` y `vagalume`), `eng/` concentra los scripts de soporte y archivos MSBuild `.targets` (que en `Simply` servirán para compilar `.cu` a `.ptx`/`.cubin` con `nvcc` o preparar fixtures de prueba), mientras que `artifacts/` centraliza binarios generados o descargados fuera del árbol fuente sin ensuciar git.

### 3. Propiedades compartidas en `Directory.Build.props` y estilo en `.editorconfig`
- **Decisión:** Definir en `Directory.Build.props`:
  - `<Version>1.0.0</Version>`
  - `<TargetFramework>net10.0</TargetFramework>`
  - `<LangVersion>14.0</LangVersion>`
  - `<Nullable>enable</Nullable>`
  - `<ImplicitUsings>enable</ImplicitUsings>`
  - `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
  - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  - `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`
  - `<AnalysisLevel>latest-recommended</AnalysisLevel>`
  Junto con un `.editorconfig` en la raíz que exija *file-scoped namespaces*, directivas `using` fuera del namespace con `System.*` primero, campos `readonly` y 2 espacios en archivos XML/JSON (`.csproj`, `.props`, `.targets`, `.slnx`, `.json`, `.yaml`).
- **Motivo:** Combina los requisitos de bajo nivel de `Simply` (`AllowUnsafeBlocks`, C# 14) con el rigor de análisis estático y formato de `vagalume`, permitiendo validar el estilo con `dotnet format --verify-no-changes`.

### 4. Gestión centralizada de paquetes (`Directory.Packages.props`) y uso exclusivo de la BCL en producción
- **Decisión:** Activar `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` en `Directory.Packages.props`. Los proyectos de producción (`Simply.Core` y `Simply.Cli`) no declararán ningún `<PackageReference>` de terceros; toda la funcionalidad estándar de la Fase 1 (`MemoryMappedFile`, `BinaryPrimitives`, `Span<T>`, `Unsafe`, `NativeMemory`, `CollectionsMarshal`, `AlternateLookup`, `Stopwatch`) forma parte de `Microsoft.NETCore.App` en .NET 10. En `Directory.Packages.props` se registrarán bajo `<ItemGroup Label="Testing">` únicamente los paquetes para `tests/Simply.Core.Tests`:
  - `Microsoft.NET.Test.Sdk`
  - `xunit.v3`
  - `xunit.runner.visualstudio`
  - `BenchmarkDotNet`
- **Motivo:** Cumple la directriz de construir el motor desde cero apoyándose exclusivamente en lo que proporciona .NET, reservando NuGet para el corredor de tests (`xUnit v3`) y el arnés de microbenchmarks (`BenchmarkDotNet`).

### 5. Organización interna inicial de `src/Simply.Core` y `src/Simply.Kernels`
- **Decisión:** Crear las subcarpetas de `src/Simply.Core` previstas en `AGENTS.md` (`Gguf/`, `Memory/`, `Tokenization/`, `Tensors/`, `Compute/`, `Interop/`, `Models/`), el directorio `src/Simply.Kernels/` y `eng/` (con `.gitkeep` donde aún no existan archivos) además del directorio local `artifacts/`. En `Simply.Core` se expondrá una clase estática mínima `EngineInfo` con su comentario XML `/// <summary>` verificable desde `Simply.Core.Tests`, y `src/Simply.Cli/Program.cs` tendrá un punto de entrada mínimo vacío (`no-op`).

## Risks / Trade-offs

- **[Risk] Analizadores con `AnalysisLevel=latest-recommended` y `TreatWarningsAsErrors=true` pueden marcar reglas como `CA1707` (guiones bajos en nombres de tests) en el proyecto de pruebas** → **Mitigation:** Configurar en `.editorconfig` la sección `[tests/**.cs]` con `dotnet_diagnostic.CA1707.severity = none` y `dotnet_diagnostic.CA1305.severity = none`, igual que en `vagalume`.
- **[Risk] Carpetas vacías en Git no se versionan por sí solas** → **Mitigation:** Incluir archivos `.gitkeep` en `eng/`, `src/Simply.Kernels/` y las subcarpetas de `src/Simply.Core/` que aún no contengan código fuente, retirándolos a medida que incorporen archivos reales.
