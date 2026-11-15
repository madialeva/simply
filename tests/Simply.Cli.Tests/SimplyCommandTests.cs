namespace Simply.Cli.Tests;

public class SimplyCommandTests
{
    [Fact]
    public void Version_ParsesWithoutErrors()
    {
        var result = SimplyCommand.Create().Parse("--version");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void UnknownCommand_ReportsAnError()
    {
        var result = SimplyCommand.Create().Parse("frobnicate");

        Assert.NotEmpty(result.Errors);
    }
}
