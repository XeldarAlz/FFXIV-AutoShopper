namespace AutoShopper.Core.Execution.Model;

internal static class ChatCommandBlock
{
    public static string[] Lines(string commandBlock)
    {
        if (string.IsNullOrWhiteSpace(commandBlock))
        {
            return [];
        }

        return commandBlock.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    // A line without the slash would be said in chat, so only slash lines count as commands.
    public static bool IsCommand(string line) => line.StartsWith('/');
}
