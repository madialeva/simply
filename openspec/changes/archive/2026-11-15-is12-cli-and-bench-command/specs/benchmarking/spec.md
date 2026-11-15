# Spec Delta

## Purpose

Define cómo mide Simply su rendimiento y el de la máquina: el comando `simply bench`, sus suites y las condiciones que hacen que una medición sea fiable y reproducible.

## ADDED Requirements

### Requirement: Benchmark suites under simply bench
Los benchmarks SHALL ejecutarse como subcomandos de `simply bench`, uno por suite, cada uno con sus propias opciones.

#### Scenario: Listing the suites
- **WHEN** se ejecuta `simply bench --help`
- **THEN** la salida enumera las suites disponibles, entre ellas `memory`, cada una con una descripción

### Requirement: Memory read bandwidth suite
La suite `simply bench memory` SHALL medir la velocidad a la que la CPU lee un bloque de memoria cuyo tamaño cabe en L2, en L3 o solo en la RAM, con un hilo y con todos los hilos lógicos, e informar de cada caso en GB/s.

#### Scenario: Bandwidth table
- **WHEN** se ejecuta `simply bench memory` desde una compilación Release
- **THEN** la salida incluye una tabla con una fila por combinación de tamaño y número de hilos, con su tiempo medio y su ancho de banda en GB/s, y el proceso termina con código de salida `0`

#### Scenario: Every byte of the block is read
- **WHEN** se ejecuta una vez la lectura sobre un bloque relleno con valores conocidos
- **THEN** el resultado de la lectura depende de todos los elementos del bloque, de modo que el compilador no puede omitir ninguna lectura

### Requirement: Benchmarks run inside the tool process
Las suites SHALL ejecutarse dentro del proceso de `simply`, sin generar ni compilar proyectos auxiliares, para que funcionen con la herramienta publicada en una máquina sin el código fuente ni el SDK de .NET.

#### Scenario: Published tool outside the repository
- **WHEN** se ejecuta `simply bench memory --quick` desde la salida de `dotnet publish -c Release` copiada a un directorio sin el código fuente
- **THEN** la suite se completa y el proceso termina con código de salida `0`

### Requirement: Optimized builds only
`simply bench` MUST NOT medir código compilado sin optimizaciones: si la herramienta o el código medido no están optimizados, SHALL explicar que hace falta una compilación Release y terminar con un código de salida distinto de `0`.

#### Scenario: Debug build
- **WHEN** se ejecuta `dotnet run --project src/Simply.Cli -- bench memory` con la configuración Debug
- **THEN** la salida indica que el código no está optimizado y que debe compilarse en Release, no se informa de ninguna medición, y el proceso termina con un código de salida distinto de `0`

### Requirement: Quick mode
Cada suite SHALL aceptar la opción `--quick`, que reduce el número de iteraciones para obtener una estimación rápida a costa de un margen de error mayor.

#### Scenario: Quick memory run
- **WHEN** se ejecuta `simply bench memory --quick` en la máquina de desarrollo
- **THEN** la suite termina en menos de un minuto e informa de los mismos casos que sin la opción

### Requirement: Results directory
Cada suite SHALL escribir sus informes en el directorio indicado con `--artifacts` o, si no se indica, en un directorio de resultados dentro del directorio actual.

#### Scenario: Custom results directory
- **WHEN** se ejecuta `simply bench memory --quick --artifacts <dir>`
- **THEN** los informes de la ejecución quedan dentro de `<dir>`
