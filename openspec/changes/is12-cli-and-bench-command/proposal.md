# Proposal

## Why

Medir es parte del motor, no un añadido: la Fase 0 de la hoja de ruta pide medir el ancho de banda de la máquina, y las fases siguientes miden operadores y tokens/s. Hoy `Simply.Cli` es un `Main` vacío y BenchmarkDotNet está referenciado, sin uso, desde el proyecto de tests (punto 1.4 de la revisión inicial). Conviene fijar ahora la forma de la herramienta, antes de que la Fase 1 añada sus primeros comandos.

A diferencia de llama.cpp, que reparte sus funciones en ejecutables sueltos (`llama-cli`, `llama-bench`…), Simply tendrá una sola herramienta, `simply`, con subcomandos al estilo de `docker`: `simply run`, `simply bench`… Cada subcomando tendrá sus propias opciones y ayuda.

## What Changes

- `Simply.Cli` genera el ejecutable `simply`. El análisis de la línea de comandos lo hace System.CommandLine, una nueva dependencia de producción que este cambio aprueba.
- Árbol de comandos con ayuda (`--help`) en cada nivel, `--version`, sugerencias ante comandos mal escritos y códigos de salida distintos de cero ante errores.
- Primer comando, `simply bench`, que agrupa las suites de benchmarks. Su primera suite, `simply bench memory`, mide el ancho de banda de lectura de memoria en L2, L3 y RAM, con uno y con todos los hilos, y lo expresa en GB/s.
- Nuevo proyecto `src/Simply.Benchmarks`, una biblioteca con los benchmarks de BenchmarkDotNet, referenciada por `Simply.Cli`. Los benchmarks se ejecutan dentro del proceso de `simply`, así que funcionan también con la herramienta publicada, fuera del repositorio.
- BenchmarkDotNet sale del proyecto de tests y pasa a ser dependencia de producción de `Simply.Benchmarks`.
- Nuevo proyecto `tests/Simply.Cli.Tests`, que prueba el árbol de comandos y el núcleo de la suite de memoria sin lanzar benchmarks.
- `simply run` y el resto de comandos del motor no se añaden todavía: llegarán con las fases que los implementan.

## Capabilities

### New Capabilities

- `command-line-interface`: la herramienta `simply`, con sus subcomandos, la ayuda, la versión y el tratamiento de errores de la línea de comandos.
- `benchmarking`: el comando `simply bench`, sus suites (empezando por la de memoria) y las condiciones en que se ejecutan.

### Modified Capabilities

- `build-and-solution-layout`: la solución incorpora `Simply.Benchmarks` y `Simply.Cli.Tests`, la dirección de dependencias pasa a ser `Simply.Cli` → `Simply.Benchmarks` → `Simply.Core`, y el escenario del ejecutable mínimo de `Simply.Cli` se sustituye por el de la herramienta `simply`.

## Impact

- **Código:** `src/Simply.Cli` (comandos), `src/Simply.Benchmarks` (nuevo), `tests/Simply.Cli.Tests` (nuevo), `tests/Simply.Core.Tests.csproj` (pierde BenchmarkDotNet) y `Simply.slnx`.
- **Dependencias:** `System.CommandLine` 3.0.0 y `BenchmarkDotNet` 0.15.8 pasan a ser de producción, fijadas en `Directory.Packages.props`. Las dos son infraestructura genérica, permitida por la política de dependencias.
- **Documentación:** `AGENTS.md` (organización del código y benchmarking), `README.md`, la hoja de ruta (Fase 0 y estrategia de pruebas) y la lista de acciones de la revisión inicial (punto 1.4).
- **Ejemplos de `docs/ejemplos/`:** no se mueven. El diseño explica por qué y cómo servirán de referencia para suites futuras.
