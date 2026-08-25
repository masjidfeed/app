using FluentAssertions;
using MasjidApp.Core.Utils;

namespace MasjidApp.Core.Tests.Utils;

// QA-02, NFR-SC-02: unit tests for the HTML sanitizer.

/// <summary>
/// Unit tests for <see cref="HtmlSanitizer"/>.
/// Verifies that malicious HTML is neutralized before display.
/// </summary>
public sealed class HtmlSanitizerTests
{
    [Fact]
    public void Sanitize_NullInput_ReturnsEmptyString()
    {
        var result = HtmlSanitizer.Sanitize(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_EmptyString_ReturnsEmptyString()
    {
        var result = HtmlSanitizer.Sanitize(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_ScriptTag_IsStripped()
    {
        var html = "<p>Hello</p><script>alert('xss')</script>";

        var result = HtmlSanitizer.Sanitize(html);

        result.Should().NotContain("<script");
        result.Should().NotContain("alert");
        result.Should().Contain("<p>Hello</p>");
    }

    [Fact]
    public void Sanitize_InlineEventHandler_IsStripped()
    {
        var html = "<p onclick=\"alert('xss')\">Click me</p>";

        var result = HtmlSanitizer.Sanitize(html);

        result.Should().NotContain("onclick");
        result.Should().NotContain("alert");
        result.Should().Contain("Click me");
    }

    [Fact]
    public void Sanitize_JavascriptHref_IsStripped()
    {
        var html = "<a href=\"javascript:alert('xss')\">Click</a>";

        var result = HtmlSanitizer.Sanitize(html);

        result.Should().NotContain("javascript:");
        result.Should().NotContain("alert");
    }

    [Fact]
    public void Sanitize_AllowedTags_ArePreserved()
    {
        var html = "<p>Normal <strong>bold</strong> and <em>italic</em> text.</p>";

        var result = HtmlSanitizer.Sanitize(html);

        result.Should().Contain("<p>");
        result.Should().Contain("<strong>");
        result.Should().Contain("<em>");
        result.Should().Contain("bold");
        result.Should().Contain("italic");
    }

    [Fact]
    public void Sanitize_SafeHttpsHref_IsPreserved()
    {
        var html = "<a href=\"https://example.com\">Visit</a>";

        var result = HtmlSanitizer.Sanitize(html);

        result.Should().Contain("https://example.com");
        result.Should().Contain("Visit");
    }

    [Fact]
    public void Sanitize_EncodedUnicodeCharacter_RemainsDisplayable()
    {
        var result = HtmlSanitizer.Sanitize("<p>Community prayer &#x1F64F;</p>");

        System.Net.WebUtility.HtmlDecode(result).Should().Contain("🙏");
    }

    [Fact]
    public void Sanitize_IframeTag_IsStripped()
    {
        var html = "<p>Text</p><iframe src=\"https://evil.com\"></iframe>";

        var result = HtmlSanitizer.Sanitize(html);

        result.Should().NotContain("<iframe");
        result.Should().Contain("Text");
    }

    [Fact]
    public void Sanitize_StyleAttribute_IsStripped()
    {
        var html = "<p style=\"color:red;background:url(javascript:alert(1))\">Styled</p>";

        var result = HtmlSanitizer.Sanitize(html);

        result.Should().NotContain("style=");
        result.Should().NotContain("javascript:");
        result.Should().Contain("Styled");
    }

    // ── ToPlainText ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToPlainText_NullInput_ReturnsEmptyString()
    {
        var result = HtmlSanitizer.ToPlainText(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ToPlainText_EmptyString_ReturnsEmptyString()
    {
        var result = HtmlSanitizer.ToPlainText(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ToPlainText_StripsTags_AndKeepsText()
    {
        var html = "<p>Eid prayer will be held at <strong>7:30 AM</strong> and <strong>9:00 AM</strong>.</p>";

        var result = HtmlSanitizer.ToPlainText(html);

        result.Should().NotContain("<");
        result.Should().NotContain(">");
        result.Should().Be("Eid prayer will be held at 7:30 AM and 9:00 AM.");
    }

    [Fact]
    public void ToPlainText_DecodesHtmlEntities()
    {
        var html = "<p>Doors open at 5 &amp; close at 9 &mdash; be early.</p>";

        var result = HtmlSanitizer.ToPlainText(html);

        result.Should().Be("Doors open at 5 & close at 9 \u2014 be early.");
    }

    [Fact]
    public void ToPlainText_CollapsesWhitespace()
    {
        var html = "<p>Line one</p>\n\n<p>   Line   two   </p>";

        var result = HtmlSanitizer.ToPlainText(html);

        result.Should().Be("Line one Line two");
    }

    [Fact]
    public void ToPlainText_RemovesScriptContent()
    {
        var html = "<p>Safe</p><script>alert('xss')</script>";

        var result = HtmlSanitizer.ToPlainText(html);

        result.Should().Be("Safe");
        result.Should().NotContain("alert");
    }
}
