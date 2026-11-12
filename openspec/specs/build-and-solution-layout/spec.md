# build-and-solution-layout Specification

## Purpose

Define la estructura de solución `.slnx`, los directorios estándar (`eng/`, `artifacts/`, `src/`, `tests/`), los manifiestos JSON de SDK y herramientas (`global.json`, `dotnet-tools.json`), las propiedades globales de compilación y estilo (`.editorconfig`, `Directory.Build.props`), y la gestión centralizada de paquetes (`Directory.Packages.props`) sin dependencias de terceros en producción.

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
El repositorio SHALL organizarse en torno a `Simply.slnx` en la raíz (agrupando `src/Simply.Core`, `src/Simply.Cli` y `tests/Simply.Core.Tests`), manteniendo además los directorios estándar `eng/` (versionado), `src/Simply.Kernels/` (versionado) y `artifacts/` (ignorado en git).

#### Scenario: Solution builds all projects cleanly
- **WHEN** el desarrollador ejecuta `dotnet build Simply.slnx -warnaserror` en la raíz del repositorio
- **THEN** MSBuild compila `Simply.Core`, `Simply.Cli` y `Simply.Core.Tests` con código de salida `0` y sin emitir advertencias

#### Scenario: Dependency direction between CLI and Core
- **WHEN** se inspeccionan las referencias de proyecto en `src/`
- **THEN** `Simply.Cli` MUST referenciar a `Simply.Core`, mientras que `Simply.Core` SHALL NOT tener referencias a `Simply.Cli` ni a ningún otro proyecto de interfaz

#### Scenario: Engineering and artifacts directories exist
- **WHEN** se inspecciona el árbol de directorios del repositorio
- **THEN** existen `eng/`, `src/Simply.Kernels/` y `artifacts/`, estando `artifacts/` excluido del control de versiones mediante `.gitignore`

### Requirement: Centralized compiler settings, code style, and version source of truth
La configuración común de compilación y la versión del proyecto SHALL residir en `Directory.Build.props`, acompañada de `.editorconfig` en la raíz para hacer cumplir el estilo de código en compilación y con `dotnet format`.

#### Scenario: Target framework, C# 14 flags, and analyzers applied globally
- **WHEN** se compila cualquier proyecto de la solución sin redefinir propiedades en su `.csproj`
- **THEN** el proyecto MUST heredar `TargetFramework` igual a `net10.0`, `LangVersion` en C# 14 (`14.0` o `latest`), `Nullable` en `enable`, `ImplicitUsings` en `enable`, `AllowUnsafeBlocks` en `true`, `TreatWarningsAsErrors` en `true`, `EnforceCodeStyleInBuild` en `true`, `AnalysisLevel` en `latest-recommended` y `Version` igual a `1.0.0`

#### Scenario: Code formatting verification passes
- **WHEN** se ejecuta `dotnet format Simply.slnx --verify-no-changes`
- **THEN** el comando finaliza con código de salida `0` confirmando que todos los archivos cumplen `.editorconfig`

### Requirement: Central Package Management with zero third-party production dependencies
La gestión de versiones de paquetes NuGet SHALL realizarse de forma centralizada mediante `Directory.Packages.props` (`ManagePackageVersionsCentrally` en `true`), y los proyectos de producción bajo `src/` MUST NOT depender de paquetes NuGet de terceros.

#### Scenario: Production projects rely strictly on .NET 10 BCL
- **WHEN** se inspeccionan `src/Simply.Core/Simply.Core.csproj` y `src/Simply.Cli/Simply.Cli.csproj`
- **THEN** ninguno de los dos proyectos contiene elementos `<PackageReference>` a librerías externas de terceros

#### Scenario: Test and benchmark packages managed centrally without inline versions
- **WHEN** `tests/Simply.Core.Tests/Simply.Core.Tests.csproj` declara sus referencias a `xunit.v3`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` y `BenchmarkDotNet`
- **THEN** ningún elemento `<PackageReference>` en el `.csproj` especifica el atributo `Version`, resolviéndose todas las versiones desde `<PackageVersion>` en `Directory.Packages.props`

### Requirement: Minimal CLI entry point and test execution
El proyecto `Simply.Cli` SHALL incluir un `Program.cs` inicial mínimo que finalice sin errores, y `Simply.Core.Tests` SHALL ejecutar su suite de pruebas mediante `dotnet test`.

#### Scenario: Running the initial CLI executable
- **WHEN** se ejecuta `dotnet run --project src/Simply.Cli`
- **THEN** el proceso termina inmediatamente con código de salida `0` sin realizar operaciones ni lanzar excepciones

#### Scenario: Running the unit test suite
- **WHEN** se ejecuta `dotnet test Simply.slnx`
- **THEN** el corredor de `xUnit v3` descubre y ejecuta los tests de `Simply.Core.Tests` finalizando con `0` fallos
