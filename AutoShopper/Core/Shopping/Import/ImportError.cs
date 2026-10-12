namespace AutoShopper.Core.Shopping.Import;

public enum ImportError : byte
{
    None,
    Empty,
    TooLarge,
    TooManyRows,
    MalformedJson,
    WrongFormat,
    UnsupportedVersion,
    MissingColumns,
}
