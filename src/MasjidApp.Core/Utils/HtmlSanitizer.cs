using System.Net;
using System.Text.RegularExpressions;
using GanssHtmlSanitizer = Ganss.Xss.HtmlSanitizer;

namespace MasjidApp.Core.Utils;

// NFR-SC-02: all remote HTML MUST be sanitized before rendering to prevent XSS.

/// <summary>
/// Static helper that sanitizes remote HTML using a strict allowlist of safe tags
/// and attributes, stripping all scripts, event handlers, and unsafe elements.
/// </summary>
public static class HtmlSanitizer
{
    private static readonly GanssHtmlSanitizer _sanitizer = BuildSanitizer();

    private static GanssHtmlSanitizer BuildSanitizer()
    {
        // Inline the tag list here to avoid static field initialization ordering issues.
        string[] allowedTags =
        [
            "p", "br", "strong", "b", "em", "i",
            "ul", "ol", "li",
            "a",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "blockquote",
        ];

        var s = new GanssHtmlSanitizer();

        // Start from an empty allowlist so we only permit what we explicitly need.
        s.AllowedTags.Clear();
        foreach (var tag in allowedTags)
        {
            s.AllowedTags.Add(tag);
        }

        s.AllowedAttributes.Clear();
        // Only allow href on <a> - all other attributes (style, class, onclick, etc.) are stripped.
        s.AllowedAttributes.Add("href");

        s.AllowedSchemes.Clear();
        // Permit only http/https for hrefs; javascript: and data: are stripped.
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");

        s.AllowedCssProperties.Clear();

        return s;
    }

    /// <summary>
    /// Sanitizes <paramref name="html"/>, stripping all scripts, event handlers,
    /// and any element not in the safe allowlist.
    /// </summary>
    /// <param name="html">Raw HTML string from the API; may be <c>null</c>.</param>
    /// <returns>
    /// Sanitized HTML safe for rendering, or <see cref="string.Empty"/> when
    /// <paramref name="html"/> is <c>null</c> or empty.
    /// </returns>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        return _sanitizer.Sanitize(html);
    }

    /// <summary>
    /// Converts <paramref name="html"/> to a clean, single-paragraph plain-text string -
    /// stripping all tags and decoding HTML entities - suitable for truncated previews
    /// where rich HTML rendering is not possible (e.g. multi-line excerpt cards).
    /// </summary>
    /// <param name="html">Raw HTML string from the API; may be <c>null</c>.</param>
    /// <returns>
    /// Plain text with collapsed whitespace, or <see cref="string.Empty"/> when
    /// <paramref name="html"/> is <c>null</c> or empty.
    /// </returns>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        // Sanitize first so scripts/styles and their text content are removed (NFR-SC-02).
        var sanitized = _sanitizer.Sanitize(html);

        // Strip remaining tags, decode entities, collapse whitespace, then tidy
        // up any spaces left in front of punctuation by the tag removal.
        var withoutTags = Regex.Replace(sanitized, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsed = Regex.Replace(decoded, @"\s+", " ").Trim();
        return Regex.Replace(collapsed, @"\s+([.,;:!?])", "$1");
    }
}
