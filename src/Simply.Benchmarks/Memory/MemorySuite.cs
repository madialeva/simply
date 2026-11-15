using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Simply.Benchmarks.Memory;

/// <summary>
/// Entry point of the memory suite, behind <c>simply bench memory</c>.
/// </summary>
public static class MemorySuite
{
    /// <summary>
    /// Runs the memory read benchmarks and prints their results table.
    /// </summary>
    /// <param name="options">How to run the suite.</param>
    /// <returns><c>0</c> when every case was measured; <c>1</c> otherwise.</returns>
    public static int Run(SuiteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // In-process: no auxiliary project is generated or built, so the published tool works without the sources or the SDK.
        Job job = (options.Quick ? Job.ShortRun : Job.Default).WithToolchain(InProcessEmitToolchain.Instance);
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(job.AsDefault())
            .AddColumn(new BandwidthColumn());
        if (options.ArtifactsPath is not null)
        {
            config = config.WithArtifactsPath(options.ArtifactsPath);
        }

        var summary = BenchmarkRunner.Run<MemoryReadBenchmarks>(config);
        if (summary.HasCriticalValidationErrors)
        {
            Console.Error.WriteLine(
                "simply bench only measures optimized code. Build in Release, for example: "
                + "dotnet run -c Release --project src/Simply.Cli -- bench memory");
            return 1;
        }

        return summary.Reports.Length > 0 && summary.Reports.All(report => report.Success) ? 0 : 1;
    }
}
