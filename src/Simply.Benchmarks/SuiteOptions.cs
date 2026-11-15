namespace Simply.Benchmarks;

/// <summary>
/// Options shared by every benchmark suite.
/// </summary>
/// <param name="Quick">Run fewer iterations for a quick estimate with a wider error margin.</param>
/// <param name="ArtifactsPath">Directory for the reports, or <see langword="null"/> for the default one in the current directory.</param>
public sealed record SuiteOptions(bool Quick, string? ArtifactsPath);
