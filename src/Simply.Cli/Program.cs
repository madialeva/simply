namespace Simply.Cli;

/// <summary>
/// Entry point for the <c>simply</c> command-line tool.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => SimplyCommand.Create().Parse(args).Invoke();
}
