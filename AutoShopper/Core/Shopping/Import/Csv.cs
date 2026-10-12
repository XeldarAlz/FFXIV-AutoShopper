using System.Text;

namespace AutoShopper.Core.Shopping.Import;

// RFC 4180 fields on a single line: a field may be wrapped in double quotes, and a doubled quote inside one is a
// literal quote. A quoted field never spans lines here; an unclosed quote runs to the end of the line.
internal static class Csv
{
    public const char Comma = ',';
    public const char Semicolon = ';';
    public const char Tab = '\t';

    private const char Quote = '"';

    // Spreadsheets in locales that write decimal commas export with semicolons, and copied cells arrive with tabs.
    public static char DetectDelimiter(ReadOnlySpan<char> header)
    {
        if (header.Contains(Tab))
        {
            return Tab;
        }

        return header.Contains(Semicolon) && !header.Contains(Comma) ? Semicolon : Comma;
    }

    public static void Split(ReadOnlySpan<char> line, char delimiter, List<string> fields, StringBuilder scratch)
    {
        fields.Clear();
        var index = 0;
        while (true)
        {
            scratch.Clear();
            while (index < line.Length && line[index] == ' ')
            {
                index++;
            }

            if (index < line.Length && line[index] == Quote)
            {
                index = ReadQuoted(line, index + 1, scratch);
                while (index < line.Length && line[index] != delimiter)
                {
                    index++;
                }
            }
            else
            {
                var end = line[index..].IndexOf(delimiter);
                var length = end < 0 ? line.Length - index : end;
                scratch.Append(line.Slice(index, length));
                index += length;
            }

            fields.Add(scratch.ToString().Trim());
            if (index >= line.Length)
            {
                return;
            }

            index++;
        }
    }

    public static void AppendField(StringBuilder builder, string value)
    {
        if (value.AsSpan().IndexOfAny(",;\t\"\r\n") < 0)
        {
            builder.Append(value);
            return;
        }

        builder.Append(Quote);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == Quote)
            {
                builder.Append(Quote);
            }

            builder.Append(character);
        }

        builder.Append(Quote);
    }

    private static int ReadQuoted(ReadOnlySpan<char> line, int index, StringBuilder scratch)
    {
        while (index < line.Length)
        {
            var character = line[index];
            if (character != Quote)
            {
                scratch.Append(character);
                index++;
                continue;
            }

            if (index + 1 < line.Length && line[index + 1] == Quote)
            {
                scratch.Append(Quote);
                index += 2;
                continue;
            }

            return index + 1;
        }

        return index;
    }
}
