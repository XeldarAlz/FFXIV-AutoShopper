using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Tests;

public sealed class ChatCommandBlockTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\n  \r\n")]
    public void ABlankBlockHasNoLines(string commandBlock)
    {
        Assert.Empty(ChatCommandBlock.Lines(commandBlock));
    }

    [Fact]
    public void LinesAreTrimmedAndBlankOnesDropped()
    {
        var lines = ChatCommandBlock.Lines("  /li auto  \r\n\r\n/ays m\n   \n\t/echo done\t");

        Assert.Equal(["/li auto", "/ays m", "/echo done"], lines);
    }

    [Fact]
    public void LinesKeepTheirOrder()
    {
        var lines = ChatCommandBlock.Lines("/second\n/first\n/third");

        Assert.Equal(["/second", "/first", "/third"], lines);
    }

    [Theory]
    [InlineData("/li auto", true)]
    [InlineData("/ays m", true)]
    [InlineData("li auto", false)]
    [InlineData("hello everyone", false)]
    [InlineData("\\li auto", false)]
    public void OnlySlashLinesAreCommands(string line, bool expected)
    {
        Assert.Equal(expected, ChatCommandBlock.IsCommand(line));
    }

    [Fact]
    public void APlainChatLineIsKeptSoItCanBeReportedAsSkipped()
    {
        var lines = ChatCommandBlock.Lines("say hi\n/li auto");

        Assert.Equal(2, lines.Length);
        Assert.False(ChatCommandBlock.IsCommand(lines[0]));
        Assert.True(ChatCommandBlock.IsCommand(lines[1]));
    }
}
