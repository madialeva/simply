using System.CommandLine;
using Simply.Cli.Commands;

namespace Simply.Cli;

/// <summary>
/// Builds the command tree of the <c>simply</c> tool.
/// </summary>
/// <remarks>
/// Each command lives in its own class under <c>Commands/</c> and is added here.
/// Only commands that work are added, so the help never lists placeholders.
/// </remarks>
internal static class SimplyCommand
{
    /// <summary>
    /// Creates the root command with every available subcommand.
    /// </summary>
    /// <returns>The root command, ready to parse a command line.</returns>
    internal static RootCommand Create() => new("Simply, an LLM inference engine written from scratch in C# and CUDA.")
    {
        BenchCommand.Create(),
    };
}
