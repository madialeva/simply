using System.CommandLine;
using Simply.Benchmarks;
using Simply.Benchmarks.Memory;

namespace Simply.Cli.Commands;

/// <summary>
/// The <c>simply bench</c> command, with one subcommand per benchmark suite.
/// </summary>
internal static class BenchCommand
{
    /// <summary>
    /// Creates the command and its suites.
    /// </summary>
    /// <returns>The <c>bench</c> command.</returns>
    internal static Command Create()
    {
        var bench = new Command("bench", "Measure the engine and the machine it runs on. Requires a Release build.");
        bench.Subcommands.Add(CreateSuite(
            "memory",
            "Measure how fast the CPU reads memory that fits in L2, in L3 or only in RAM, in GB/s.",
            MemorySuite.Run));
        return bench;
    }

    private static Command CreateSuite(string name, string description, Func<SuiteOptions, int> run)
    {
        var quick = new Option<bool>("--quick")
        {
            Description = "Run fewer iterations for a quick estimate with a wider error margin.",
        };
        var artifacts = new Option<DirectoryInfo?>("--artifacts")
        {
            Description = "Directory for the reports. Defaults to BenchmarkDotNet.Artifacts in the current directory.",
        };
        var suite = new Command(name, description) { quick, artifacts };
        suite.SetAction(parseResult => run(new SuiteOptions(
            parseResult.GetValue(quick),
            parseResult.GetValue(artifacts)?.FullName)));
        return suite;
    }
}
