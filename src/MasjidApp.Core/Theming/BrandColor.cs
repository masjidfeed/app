using System.Globalization;

namespace MasjidApp.Core.Theming;

/// <summary>Validates and normalizes tenant-provided RGB color values.</summary>
public static class BrandColor
{
    /// <summary>Returns a normalized <c>#RRGGBB</c> value, or <c>null</c> when invalid.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hex = value.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(static character => new string(character, 2)));
        }

        return hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? $"#{hex.ToUpperInvariant()}"
            : null;
    }
}
