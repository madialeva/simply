# build-and-solution-layout Specification

## Purpose

Define la estructura de solución `.slnx`, los directorios estándar (`eng/`, `artifacts/`, `src/`, `tests/`), los manifiestos JSON de SDK y herramientas (`global.json`, `dotnet-tools.json`), las propiedades globales de compilación y estilo (`.editorconfig`, `Directory.Build.props`), la gestión centralizada de paquetes (`Directory.Packages.props`) junto con la política de dependencias de producción, y la plataforma sobre la que se ejecutan los tests.

## Requirements

### Requirement: Pinned .NET 10 SDK and local tool manifest
El repositorio SHALL incluir en su raíz los archivos `global.json` y `dotnet-tools.json` para fijar el SDK de .NET 10 y declarar el manifiesto local de herramientas de .NET.

#### Scenario: SDK version resolution from global.json
- **WHEN** se ejecuta `dotnet --version` en la raíz del repositorio
- **THEN** el CLI de .NET resuelve un SDK de la familia `10.0.x` a partir de `global.json` con política `rollForward` en `latestFeature`

#### Scenario: Local tool manifest is valid
- **WHEN** se ejecuta `dotnet tool restore` en la raíz del repositorio
- **THEN** el comando procesa `dotnet-tools.json` (`isRoot: true`) y finaliza con código de salida `0`

### Requirement: Modern XML solution and repository directory structure
El repositorio SHALL organizarse en torno a `Simply.slnx` en la raíz (agrupando `src/Simply.Core`, `src/Simply.Benchmarks`, `src/Simply.Cli`, `tests/Simply.Core.Tests` y `tests/Simply.Cli.Tests`), manteniendo además los directorios estándar `eng/` (versionado) y `src/Simply.Kernels/` (versionado), y reservando `artifacts/` para las salidas de compilación y herramientas y las descargas locales, que nunca se versionan.

#### Scenario: Solution builds all projects cleanly
- **WHEN** el desarrollador ejecuta `dotnet build Simply.slnx -warnaserror` en la raíz del repositorio
- **THEN** MSBuild compila `Simply.Core`, `Simply.Benchmarks`, `Simply.Cli`, `Simply.Core.Tests` y `Simply.Cli.Tests` con código de salida `0` y sin emitir advertencias

#### Scenario: Dependency direction between CLI and Core
- **WHEN** se inspeccionan las referencias de proyecto en `src/`
- **THEN** las referencias siguen la dirección `Simply.Cli` → `Simply.Benchmarks` → `Simply.Core` (`Simply.Cli` MUST referenciar a `Simply.Core` y a `Simply.Benchmarks`), mientras que `Simply.Core` SHALL NOT tener referencias a `Simply.Benchmarks`, a `Simply.Cli` ni a ningún otro proyecto de interfaz, y `Simply.Benchmarks` SHALL NOT referenciar a `Simply.Cli`

#### Scenario: Engineering directories exist and artifacts are ignored
- **WHEN** se inspecciona el árbol de directorios de un clon del repositorio
- **THEN** existen `eng/` y `src/Simply.Kernels/`, y `.gitignore` excluye `artifacts/` del control de versiones, exista o no ese directorio

### Requirement: Centralized compiler settings, code style, and version source of truth
La configuración común de compilación y la versión del proyecto SHALL residir en `Directory.Build.props`, acompañada de `.editorconfig` en la raíz para hacer cumplir el estilo de código en compilación y con `dotnet format`.

#### Scenario: Target framework, C# 14 flags, and analyzers applied globally
- **WHEN** se compila cualquier proyecto de la solución sin redefinir propiedades en su `.csproj`
- **THEN** el proyecto MUST heredar `TargetFramework` igual a `net10.0`, `LangVersion` en C# 14 (`14.0` o `latest`), `Nullable` en `enable`, `ImplicitUsings` en `enable`, `AllowUnsafeBlocks` en `true`, `TreatWarningsAsErrors` en `true`, `EnforceCodeStyleInBuild` en `true`, `AnalysisLevel` en `latest-recommended` y `Version` igual al sufijo de la rama `develop/vX.Y.Z` activa (por ejemplo, `1.0.0` en `develop/v1.0.0`)

#### Scenario: Code formatting verification passes
- **WHEN** se ejecuta `dotnet format Simply.slnx --verify-no-changes`
- **THEN** el comando finaliza con código de salida `0` confirmando que todos los archivos cumplen `.editorconfig`

### Requirement: Central Package Management
La gestión de versiones de paquetes NuGet SHALL realizarse de forma centralizada mediante `Directory.Packages.props` (`ManagePackageVersionsCentrally` en `true`).

#### Scenario: Package versions resolved centrally
- **WHEN** cualquier proyecto de `Simply.slnx` declara un elemento `<PackageReference>`
- **THEN** el elemento no especifica el atributo `Version`, y la versión se resuelve desde `<PackageVersion>` en `Directory.Packages.props`

### Requirement: Production dependency policy
Los proyectos de producción bajo `src/` MAY depender de paquetes NuGet que aporten infraestructura genérica que no es objeto de aprendizaje, como `System.Numerics.Tensors`, pero MUST NOT depender de paquetes que implementen piezas del motor.

#### Scenario: Production packages are generic infrastructure
- **WHEN** se inspeccionan los elementos `<PackageReference>` de los proyectos bajo `src/`
- **THEN** cada paquete aporta infraestructura genérica, y ninguno es una librería de inferencia o *machine learning* (como `LLamaSharp`, `ONNX Runtime`, `TorchSharp` o `ML.NET`) ni implementa lectura de GGUF, tokenización, cuantización o kernels

### Requirement: Minimal CLI entry point and test execution
El proyecto `Simply.Cli` SHALL producir el ejecutable `simply`, cuyo comportamiento define la capacidad `command-line-interface`, y los proyectos de tests SHALL ejecutar su suite de pruebas mediante `dotnet test`.

#### Scenario: Running the initial CLI executable
- **WHEN** se ejecuta `dotnet run --project src/Simply.Cli -- --help`
- **THEN** el ejecutable `simply` muestra su ayuda y el proceso termina con código de salida `0` sin lanzar excepciones

#### Scenario: Running the unit test suite
- **WHEN** se ejecuta `dotnet test Simply.slnx`
- **THEN** el corredor de `xUnit v3` descubre y ejecuta los tests de `Simply.Core.Tests` y `Simply.Cli.Tests` finalizando con `0` fallos

### Requirement: Tests on Microsoft.Testing.Platform
Los proyectos de tests SHALL ejecutarse sobre Microsoft.Testing.Platform, que `global.json` selecciona como corredor de `dotnet test`, y MUST NOT depender de VSTest.

#### Scenario: dotnet test runs on Microsoft.Testing.Platform
- **WHEN** se ejecuta `dotnet test Simply.slnx` en la raíz del repositorio
- **THEN** el SDK aplica `"test": { "runner": "Microsoft.Testing.Platform" }` de `global.json` y ejecuta cada proyecto de tests como aplicación de Microsoft.Testing.Platform

#### Scenario: No VSTest integration
- **WHEN** se inspeccionan los proyectos de tests y `Directory.Packages.props`
- **THEN** no aparecen los paquetes de VSTest (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) ni `IsTestingPlatformApplication` en `false`

#### Scenario: Test project runs as a standalone executable
- **WHEN** se ejecuta `dotnet run --project tests/Simply.Core.Tests`
- **THEN** la suite se ejecuta sobre Microsoft.Testing.Platform sin pasar por `dotnet test` y termina con código de salida `0`, y el ejecutable acepta las mismas opciones que `dotnet test`, como `--list-tests` o `--filter-trait`
