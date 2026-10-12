using FFXIVClientStructs.FFXIV.Client.UI;

namespace AutoShopper.Core.Game;

internal static unsafe class TextInputFocus
{
    // The game's UI routes keystrokes to one text input at a time, and this holds while any has them: the chat line,
    // and the other text boxes of the game's own windows too.
    public static bool Active()
    {
        var module = RaptureAtkModule.Instance();
        return module is not null && module->IsTextInputActive();
    }
}
