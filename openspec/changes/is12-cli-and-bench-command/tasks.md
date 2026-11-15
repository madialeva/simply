# Tasks

## 1. The simply command-line tool

- [x] 1.1 Añadir `System.CommandLine` 3.0.0 en un grupo `Production` de `Directory.Packages.props`, referenciarlo desde `Simply.Cli` y fijar `<AssemblyName>simply</AssemblyName>`; verificar que `dotnet build Simply.slnx -warnaserror` termina sin avisos y que el ejecutable se llama `simply`
- [x] 1.2 Crear `SimplyCommand.Create()` con el comando raíz (descripción, `--help`, `--version`) y reducir `Program.Main` a analizar e invocar; verificar que `dotnet run --project src/Simply.Cli -- --help` termina con `0` y que `-- --version` imprime `1.0.0+<commit>`
- [x] 1.3 Crear `tests/Simply.Cli.Tests` (`xunit.v3` sobre Microsoft.Testing.Platform, como `Simply.Core.Tests`), añadirlo a `Simply.slnx`, dar acceso con `InternalsVisibleTo` y probar que el árbol acepta `--version` y rechaza un comando desconocido; verificar con `dotnet test Simply.slnx`
- [x] 1.4 Documentar la herramienta: `AGENTS.md` (organización del código y `Simply.Cli.Tests`) y `README.md` (la línea de comandos en el stack, `simply --help` y la estructura del repositorio); verificar que los enlaces resuelven y que `dotnet format Simply.slnx --verify-no-changes` pasa

## 2. Simply.Benchmarks and the memory suite

- [ ] 2.1 Crear `src/Simply.Benchmarks` (biblioteca que referencia a `Simply.Core` y a `BenchmarkDotNet`), añadirlo a `Simply.slnx`, referenciarlo desde `Simply.Cli`, mover `BenchmarkDotNet` al grupo `Production` y quitarlo de `Simply.Core.Tests`; verificar la compilación sin avisos y la dirección `Simply.Cli` → `Simply.Benchmarks` → `Simply.Core`
- [ ] 2.2 Implementar `MemoryReadBenchmarks` (bloque nativo alineado a 64 bytes, lectura con `Vector256` y dos acumuladores, tamaños de L2, L3 y RAM, uno y todos los hilos) y la columna de GB/s; probar en `Simply.Cli.Tests` que una pasada sobre un bloque conocido devuelve la suma esperada, también con un tamaño que deja resto respecto al ancho del vector
- [ ] 2.3 Implementar el punto de entrada de la suite (*toolchain* en proceso, `ShortRun` con `--quick`, directorio de informes, fallos de validación convertidos en un mensaje que pide Release y en un código de salida distinto de `0`); verificar que en Debug `bench memory` se niega a medir y termina con un código distinto de `0`
- [ ] 2.4 Añadir `simply bench` con el subcomando `memory` y sus opciones `--quick` y `--artifacts`, y probar su análisis en `Simply.Cli.Tests` (`bench memory --quick --artifacts x` sin errores; `bench memroy` y la línea vacía con error); verificar que `simply` sin argumentos termina con un código distinto de `0`, que `dotnet run -c Release --project src/Simply.Cli -- bench memory --quick` muestra la tabla con GB/s en menos de un minuto, que `--artifacts` cambia el directorio de informes y que la salida de `dotnet publish -c Release`, copiada fuera del repositorio, ejecuta la suite
- [ ] 2.5 Documentar los benchmarks: `AGENTS.md` (principio de *testing* y *benchmarking*, dependencias entre proyectos), `README.md` (uso de `simply bench`) y la hoja de ruta (`simply bench memory` en la Fase 0 y el apartado de rendimiento de la estrategia de pruebas); verificar `dotnet build Simply.slnx -warnaserror`, `dotnet test Simply.slnx`, `dotnet format Simply.slnx --verify-no-changes` y `openspec validate --all --strict`

## 3. Initial review bookkeeping

- [ ] 3.1 Marcar en la lista de acciones de `docs/revision-inicial.md` el proyecto de benchmarks del punto 1.4 como hecho, con el commit del grupo 2, y añadir la nota de seguimiento en la cabecera; verificar que los enlaces del documento resuelven

## Workflow follow-up

- Archivar el cambio tras la validación del usuario: sincronizar los *delta specs* en `openspec/specs/` y moverlo a `openspec/changes/archive/YYYY-MM-DD-is12-cli-and-bench-command/`.
- Crear issues de seguimiento para los *Non-Goals* con trabajo pendiente: el ancho de banda de VRAM (Hito 0), `simply bench model`, `simply bench ops` y la distribución de `simply`.
