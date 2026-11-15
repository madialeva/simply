using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Simply.Benchmarks.Memory;

/// <summary>
/// Results column with the read bandwidth of each case: the block size divided by the mean time.
/// </summary>
public sealed class BandwidthColumn : IColumn
{
    /// <inheritdoc />
    public string Id => nameof(BandwidthColumn);

    /// <inheritdoc />
    public string ColumnName => "GB/s";

    /// <inheritdoc />
    public bool AlwaysShow => true;

    /// <inheritdoc />
    public ColumnCategory Category => ColumnCategory.Custom;

    /// <inheritdoc />
    public int PriorityInCategory => 0;

    /// <inheritdoc />
    public bool IsNumeric => true;

    /// <inheritdoc />
    public UnitType UnitType => UnitType.Dimensionless;

    /// <inheritdoc />
    public string Legend => "Bytes read per second, in units of 10^9 bytes";

    /// <inheritdoc />
    public bool IsAvailable(Summary summary) => true;

    /// <inheritdoc />
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    /// <inheritdoc />
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => GetValue(summary, benchmarkCase, summary.Style);

    /// <inheritdoc />
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(benchmarkCase);
        double? meanNanoseconds = summary[benchmarkCase]?.ResultStatistics?.Mean;
        if (meanNanoseconds is not > 0 || benchmarkCase.Parameters[nameof(MemoryReadBenchmarks.Block)] is not BlockSize block)
        {
            return "NA";
        }

        // One byte per nanosecond is exactly 10^9 bytes per second.
        return (block.Bytes / meanNanoseconds.Value).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override string ToString() => ColumnName;
}
