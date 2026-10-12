namespace AutoShopper.Core.Shopping.Import;

internal static class TextLines
{
    // Long enough to recognise a row in the preview, short enough that a pasted wall of text stays small.
    private const int MaximumSourceTextLength = 160;

    public static bool Next(string text, ref int position, out ReadOnlySpan<char> line)
    {
        if (position >= text.Length)
        {
            line = default;
            return false;
        }

        var start = position;
        var relativeEnd = text.AsSpan(start).IndexOfAny('\r', '\n');
        if (relativeEnd < 0)
        {
            line = text.AsSpan(start);
            position = text.Length;
            return true;
        }

        var end = start + relativeEnd;
        line = text.AsSpan(start, relativeEnd);
        position = end + 1;
        if (text[end] == '\r' && position < text.Length && text[position] == '\n')
        {
            position++;
        }

        return true;
    }

    public static string Clip(ReadOnlySpan<char> line)
        => line.Length <= MaximumSourceTextLength ? line.ToString() : line[..MaximumSourceTextLength].ToString();
}
