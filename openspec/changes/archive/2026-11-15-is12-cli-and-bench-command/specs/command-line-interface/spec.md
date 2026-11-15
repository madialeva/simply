# Spec Delta

## Purpose

Define la herramienta de línea de comandos `simply`, que reúne todas las funciones del motor como subcomandos con sus propias opciones, su ayuda y un tratamiento uniforme de los errores.

## ADDED Requirements

### Requirement: Single simply executable with subcommands
El proyecto `Simply.Cli` SHALL producir un único ejecutable llamado `simply` que organiza sus funciones como subcomandos (`simply <comando> [subcomando] [opciones]`), y cada comando y subcomando SHALL ofrecer su propia ayuda con `--help`.

#### Scenario: Root help lists the commands
- **WHEN** se ejecuta `simply --help`
- **THEN** la salida describe la herramienta y enumera los comandos disponibles, cada uno con una descripción, y el proceso termina con código de salida `0`

#### Scenario: Each command has its own help
- **WHEN** se ejecuta `simply bench --help` o `simply bench memory --help`
- **THEN** la salida muestra el uso de ese comando, sus subcomandos y sus opciones, y el proceso termina con código de salida `0`

### Requirement: Version from the single source of truth
La herramienta SHALL informar de su versión con `--version`. Esa versión MUST empezar por la que fija `Directory.Build.props` y, cuando se compila desde un clon de git, SHALL añadir `+` y el commit del que se compiló, para identificar la compilación que produjo cada resultado.

#### Scenario: Printing the version
- **WHEN** se ejecuta `simply --version` con `Version` igual a `1.0.0` en `Directory.Build.props`
- **THEN** la salida es `1.0.0` seguido de `+` y el hash completo del commit compilado (por ejemplo, `1.0.0+f7a5a16c…`), y el proceso termina con código de salida `0`

### Requirement: Invalid command lines are rejected
Ante una línea de comandos inválida, la herramienta SHALL explicar el error sin ejecutar ninguna acción y MUST terminar con un código de salida distinto de `0`.

#### Scenario: Unknown command with a suggestion
- **WHEN** se ejecuta `simply bench memroy`
- **THEN** la salida indica que `memroy` no se reconoce y sugiere `memory`, y el proceso termina con un código de salida distinto de `0`

#### Scenario: Missing command
- **WHEN** se ejecuta `simply` sin argumentos
- **THEN** la salida indica que falta un comando y muestra la ayuda, y el proceso termina con un código de salida distinto de `0`
