# Proposal

## Why

Antes de implementar el parser GGUF, el tokenizador y los operadores del motor de inferencia (Fase 1), el repositorio necesita el esqueleto compilable de la solución en C# 14 y .NET 10 (`net10.0`) para Linux (`linux-x64`). Establecer desde el primer momento la fijación del SDK (`global.json`), el manifiesto de herramientas (`dotnet-tools.json`), las reglas de formato y análisis (`.editorconfig` y `Directory.Build.props`), la gestión centralizada de paquetes (`Directory.Packages.props`), los directorios estándar de ingeniería (`eng/` y `artifacts/`), el archivo de solución moderno `.slnx` y el proyecto de tests garantiza que cada hito posterior se construya sobre reglas estrictas (`unsafe`, anulabilidad, cero advertencias y cero dependencias de terceros en producción).

## What Changes

- Creación de `global.json` fijando el SDK de .NET 10 (`10.0.301` con `rollForward` en `latestFeature`) y de `dotnet-tools.json` como manifiesto raíz de herramientas locales de .NET.
- Creación de `.editorconfig` con las reglas de formato e inspección de código C# (`file_scoped` namespaces, `outside_namespace` usings, `readonly` fields, indentación y supresiones para `tests/`).
- Creación del archivo de solución moderno en formato XML `Simply.slnx` en la raíz del repositorio.
- Creación de `Directory.Build.props` en la raíz como fuente única de verdad para la versión (`<Version>1.0.0</Version>`), el *target framework* (`net10.0`), `LangVersion` (`14.0` / `latest`), `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` y `<AnalysisLevel>latest-recommended</AnalysisLevel>`.
- Creación de `Directory.Packages.props` con `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` definiendo exclusivamente las versiones de los paquetes de testing y benchmarking (`xunit.v3`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` y `BenchmarkDotNet`).
- Creación de los directorios estándar de repositorio .NET `eng/` (para scripts de build, archivos `.targets` MSBuild y lockfiles) y `artifacts/` (directorio local ignorado en git para salidas de compilación o artefactos descargados).
- Creación del proyecto de librería principal `src/Simply.Core/Simply.Core.csproj` con la estructura inicial de carpetas por responsabilidad (`Gguf/`, `Memory/`, `Tokenization/`, `Tensors/`, `Compute/`, `Interop/`, `Models/`) y sin ninguna dependencia externa de NuGet (apoyándose únicamente en la BCL de .NET 10).
- Creación del directorio `src/Simply.Kernels/` reservado para los futuros archivos `.cu` de CUDA.
- Creación del ejecutable de consola `src/Simply.Cli/Simply.Cli.csproj` (con referencia a `Simply.Core`) y un `Program.cs` mínimo que no realiza ninguna operación (`no-op`).
- Creación del proyecto de pruebas `tests/Simply.Core.Tests/Simply.Core.Tests.csproj` (con referencia a `Simply.Core` y a los paquetes de `xUnit v3` y `BenchmarkDotNet`) incluyendo un test inicial de humo que verifique la compilación, el formato (`dotnet format --verify-no-changes`) y la ejecución de `dotnet test`.

## Capabilities

### New Capabilities
- `build-and-solution-layout`: Estructura del repositorio (`Simply.slnx`, `eng/`, `artifacts/`, `src/`, `tests/`), fijación de SDK y herramientas (`global.json`, `dotnet-tools.json`), estilo y propiedades globales de compilación C# 14 / .NET 10 (`.editorconfig`, `Directory.Build.props`), gestión centralizada de paquetes (`Directory.Packages.props`) sin dependencias de terceros en producción, ejecutable `Simply.Cli` inicial y suite de pruebas en `Simply.Core.Tests`.

### Modified Capabilities

## Impact

- Añade `global.json`, `dotnet-tools.json`, `.editorconfig`, `Simply.slnx`, `Directory.Build.props` y `Directory.Packages.props` en la raíz del repositorio.
- Añade los directorios `eng/`, `artifacts/` (ignorado en git), `src/Simply.Core/`, `src/Simply.Cli/`, `src/Simply.Kernels/` y `tests/Simply.Core.Tests/`.
- Permite ejecutar `dotnet format --verify-no-changes`, `dotnet build -warnaserror` y `dotnet test` limpiamente desde la raíz del proyecto como requisito de validación para todos los cambios futuros.
