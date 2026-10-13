using System.Diagnostics.CodeAnalysis;

namespace AutoShopper.Core.Api.Model;

internal static class ApiIds
{
    public static string Format(Guid id) => id.ToString("D");

    public static bool TryParse([NotNullWhen(true)] string? text, out Guid id) => Guid.TryParse(text, out id) && id != Guid.Empty;
}
