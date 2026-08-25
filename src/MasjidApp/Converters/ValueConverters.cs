using System.Globalization;
using MasjidApp.Core.Utils;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MasjidApp.Converters;

/// <summary>
/// Sanitizes a raw remote HTML string so it can be safely rendered by a
/// <c>Label</c> with <c>TextType="Html"</c> (NFR-SC-02). All scripts, event
/// handlers, and unsafe elements are stripped via <see cref="HtmlSanitizer"/>.
/// </summary>
public sealed class SanitizedHtmlConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        HtmlSanitizer.Sanitize(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a raw remote HTML string to clean plain text (tags stripped, entities
/// decoded) for use in truncated preview labels, where <c>TextType="Html"</c> cannot
/// be combined with line-break truncation on Android (dotnet/maui#24298).
/// </summary>
public sealed class HtmlToPlainTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        HtmlSanitizer.ToPlainText(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts a date value to its day-of-month number.</summary>
public sealed class DayOfMonthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            DateTimeOffset date => date.Day.ToString(culture),
            DateTime date => date.Day.ToString(culture),
            _ => string.Empty,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps the current prayer name to a high-contrast sky gradient for countdown cards (FR-PT-02).</summary>
public sealed class PrayerSkyGradientConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var prayerName = value as string ?? string.Empty;

        if (prayerName.Contains("Fajr", StringComparison.OrdinalIgnoreCase))
        {
            return CreateVerticalGradient("#0E1840", "#35285C", "#8B3D1A");
        }

        if (prayerName.Contains("Sunrise", StringComparison.OrdinalIgnoreCase))
        {
            return CreateVerticalGradient("#1B4D8E", "#C47B1A", "#E8AE24");
        }

        if (prayerName.Contains("Dhuhr", StringComparison.OrdinalIgnoreCase))
        {
            return CreateVerticalGradient("#0B66B0", "#1E8FD4", "#7DC2E0");
        }

        if (prayerName.Contains("Asr", StringComparison.OrdinalIgnoreCase))
        {
            return CreateVerticalGradient("#1B72A8", "#4A9EBC", "#C8A848");
        }

        if (prayerName.Contains("Maghrib", StringComparison.OrdinalIgnoreCase))
        {
            return CreateVerticalGradient("#1A2C5A", "#4A3B68", "#A34A1F");
        }

        return CreateVerticalGradient("#05101E", "#0B1C38", "#162A48");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static LinearGradientBrush CreateVerticalGradient(params string[] colors)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };

        for (var i = 0; i < colors.Length; i++)
        {
            var offset = colors.Length == 1 ? 0 : (float)i / (colors.Length - 1);
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(colors[i]), offset));
        }

        return brush;
    }
}

/// <summary>Returns <c>true</c> when the bound value is not <c>null</c>.</summary>
public sealed class IsNotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Returns <c>true</c> when the bound string is not null or empty.</summary>
public sealed class IsNotNullOrEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a <see cref="bool"/> binding.</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>Maps a push notification type to its event or announcement icon.</summary>
public sealed class NotificationTypeIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, "events", StringComparison.OrdinalIgnoreCase)
            ? "notification_event.png"
            : "notification_announcement.png";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
