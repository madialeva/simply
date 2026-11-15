# Spec Delta

## MODIFIED Requirements

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

### Requirement: Minimal CLI entry point and test execution
El proyecto `Simply.Cli` SHALL producir el ejecutable `simply`, cuyo comportamiento define la capacidad `command-line-interface`, y los proyectos de tests SHALL ejecutar su suite de pruebas mediante `dotnet test`.

#### Scenario: Running the initial CLI executable
- **WHEN** se ejecuta `dotnet run --project src/Simply.Cli -- --help`
- **THEN** el ejecutable `simply` muestra su ayuda y el proceso termina con código de salida `0` sin lanzar excepciones

#### Scenario: Running the unit test suite
- **WHEN** se ejecuta `dotnet test Simply.slnx`
- **THEN** el corredor de `xUnit v3` descubre y ejecuta los tests de `Simply.Core.Tests` y `Simply.Cli.Tests` finalizando con `0` fallos
