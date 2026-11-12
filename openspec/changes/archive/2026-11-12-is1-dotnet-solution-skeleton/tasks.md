# Tasks

## 1. Configuración raíz (.NET 10 SDK, estilo, MSBuild y directorios base)

- [x] 1.1 Crear `global.json` (fijando el SDK `10.0.301` con `rollForward: latestFeature`) y `dotnet-tools.json` (`version: 1`, `isRoot: true`), verificando que `dotnet --version` y `dotnet tool restore` funcionan sin errores
- [x] 1.2 Crear `.editorconfig` y `Directory.Build.props` (`Version` `1.0.0`, `net10.0`, `LangVersion` `14.0`, `Nullable`, `ImplicitUsings`, `AllowUnsafeBlocks`, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel` `latest-recommended`) junto con `Directory.Packages.props` (`ManagePackageVersionsCentrally` con `Microsoft.NET.Test.Sdk`, `xunit.v3`, `xunit.runner.visualstudio` y `BenchmarkDotNet`), verificando la validez de los archivos XML
- [x] 1.3 Crear los directorios estándar `eng/` (con `.gitkeep`), `artifacts/` (verificando que queda ignorado por `.gitignore`), `src/Simply.Kernels/` (con `.gitkeep`), `src/Simply.Core/Simply.Core.csproj` (con sus subcarpetas `Gguf/`, `Memory/`, `Tokenization/`, `Tensors/`, `Compute/`, `Interop/`, `Models/` y la clase `EngineInfo`) y `src/Simply.Cli/Simply.Cli.csproj` (con `Program.cs` no-op), verificando que `dotnet build src/Simply.Cli -warnaserror` y `dotnet run --project src/Simply.Cli` finalizan con código `0`

## 2. Solución `.slnx`, proyecto `Simply.Core.Tests` y validación integral

- [x] 2.1 Crear `tests/Simply.Core.Tests/Simply.Core.Tests.csproj` (referenciando a `Simply.Core` y a los paquetes `Microsoft.NET.Test.Sdk`, `xunit.v3`, `xunit.runner.visualstudio` y `BenchmarkDotNet` sin atributo `Version` inline) y un test unitario inicial en `EngineInfoTests.cs` que valide `EngineInfo` y el soporte de bloques `unsafe`, verificando que compila sin advertencias
- [x] 2.2 Crear `Simply.slnx` en la raíz agrupando `/src/` y `/tests/`, y verificar que `dotnet format Simply.slnx --verify-no-changes`, `dotnet build Simply.slnx -warnaserror` y `dotnet test Simply.slnx` pasan con `0` errores y `0` advertencias
