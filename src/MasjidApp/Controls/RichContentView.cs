using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MasjidApp.Core.Utils;

namespace MasjidApp.Controls;

/// <summary>Displays sanitized remote HTML as native formatted text with safe links.</summary>
public sealed class RichContentView : ContentView
{
    public static readonly BindableProperty HtmlProperty = BindableProperty.Create(
        nameof(Html),
        typeof(string),
        typeof(RichContentView),
        string.Empty,
        propertyChanged: static (bindable, _, newValue) =>
            ((RichContentView)bindable).Render(newValue as string));

    private readonly Label _label = new()
    {
        FontFamily = "Outfit",
        FontSize = 15,
        LineHeight = 1.5,
        LineBreakMode = LineBreakMode.WordWrap,
    };

    public RichContentView()
    {
        Content = _label;
    }

    public string Html
    {
        get => (string)GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    private void Render(string? html)
    {
        var sanitized = HtmlSanitizer.Sanitize(html);
        IsVisible = !string.IsNullOrWhiteSpace(sanitized);
        if (!IsVisible)
        {
            _label.FormattedText = null;
            return;
        }

        var document = new HtmlParser().ParseDocument($"<body>{sanitized}</body>");
        var formatted = new FormattedString();
        AppendChildren(document.Body!, formatted, TextStyle.Default, listDepth: 0);
        TrimTrailingWhitespace(formatted);
        _label.FormattedText = formatted;
    }

    private static void AppendChildren(
        INode parent,
        FormattedString formatted,
        TextStyle style,
        int listDepth)
    {
        foreach (var child in parent.ChildNodes)
        {
            AppendNode(child, formatted, style, listDepth);
        }
    }

    private static void AppendNode(
        INode node,
        FormattedString formatted,
        TextStyle style,
        int listDepth)
    {
        if (node is AngleSharp.Dom.IText text)
        {
            AppendText(formatted, WebUtility.HtmlDecode(text.Data), style);
            return;
        }

        if (node is not AngleSharp.Dom.IElement element)
        {
            return;
        }

        var tag = element.LocalName;
        if (tag == "br")
        {
            AppendText(formatted, "\n", style);
            return;
        }

        var childStyle = tag switch
        {
            "strong" or "b" => style with { Bold = true },
            "em" or "i" => style with { Italic = true },
            "h1" or "h2" => style with { Bold = true, FontSize = 22 },
            "h3" or "h4" or "h5" or "h6" => style with { Bold = true, FontSize = 18 },
            "a" => style with { Link = ExternalLinkPolicy.GetSafeUri(element.GetAttribute("href")) },
            _ => style,
        };

        var isBlock = tag is "p" or "blockquote" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
        if (isBlock)
        {
            EnsureLineBreak(formatted);
        }

        if (tag == "li")
        {
            EnsureLineBreak(formatted);
            AppendText(formatted, $"{new string(' ', listDepth * 2)}• ", style);
        }

        AppendChildren(
            element,
            formatted,
            childStyle,
            tag is "ul" or "ol" ? listDepth + 1 : listDepth);

        if (isBlock || tag is "li" or "ul" or "ol")
        {
            EnsureLineBreak(formatted);
        }
    }

    private static void AppendText(FormattedString formatted, string value, TextStyle style)
    {
        var normalized = NormalizeWhitespace(value);
        if (normalized.Length == 0)
        {
            return;
        }

        var span = new Span
        {
            Text = normalized,
            FontAttributes = (style.Bold ? FontAttributes.Bold : FontAttributes.None) |
                (style.Italic ? FontAttributes.Italic : FontAttributes.None),
        };
        if (style.FontSize is { } fontSize)
        {
            span.FontSize = fontSize;
        }

        if (style.Link is { } uri)
        {
            span.TextColor = Color.FromArgb("#1B7F5C");
            span.TextDecorations = TextDecorations.Underline;
            span.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () =>
                    await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred)),
            });
        }

        formatted.Spans.Add(span);
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var hasLeadingWhitespace = char.IsWhiteSpace(value[0]);
        var hasTrailingWhitespace = char.IsWhiteSpace(value[^1]);
        var collapsed = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"{(hasLeadingWhitespace ? " " : string.Empty)}{collapsed}{(hasTrailingWhitespace ? " " : string.Empty)}";
    }

    private static void EnsureLineBreak(FormattedString formatted)
    {
        if (formatted.Spans.Count == 0 || formatted.Spans[^1].Text.EndsWith('\n'))
        {
            return;
        }

        formatted.Spans.Add(new Span { Text = "\n" });
    }

    private static void TrimTrailingWhitespace(FormattedString formatted)
    {
        while (formatted.Spans.Count > 0 &&
            string.IsNullOrWhiteSpace(formatted.Spans[^1].Text))
        {
            formatted.Spans.RemoveAt(formatted.Spans.Count - 1);
        }
    }

    private sealed record TextStyle(
        bool Bold,
        bool Italic,
        double? FontSize,
        Uri? Link)
    {
        public static readonly TextStyle Default = new(false, false, null, null);
    }
}
