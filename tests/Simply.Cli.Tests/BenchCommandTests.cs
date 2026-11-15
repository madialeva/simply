namespace Simply.Cli.Tests;

public class BenchCommandTests
{
    [Theory]
    [InlineData("bench memory")]
    [InlineData("bench memory --quick --artifacts results")]
    [InlineData("bench --help")]
    public void ValidCommandLine_ParsesWithoutErrors(string commandLine)
    {
        var result = SimplyCommand.Create().Parse(commandLine);

        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bench")]
    [InlineData("bench memroy")]
    [InlineData("bench memory --fast")]
    public void InvalidCommandLine_ReportsAnError(string commandLine)
    {
        var result = SimplyCommand.Create().Parse(commandLine);

        Assert.NotEmpty(result.Errors);
    }
}
