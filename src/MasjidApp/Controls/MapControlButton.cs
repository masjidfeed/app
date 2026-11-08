namespace MasjidApp.Controls;

// The floating round map buttons (Apple-Maps-style control column) reused by
// the parking guide page: dark circular button with a white SVG glyph so the
// controls stay legible on both the light street map and satellite imagery.

/// <summary>Round overlay button shown on top of the native map.</summary>
public partial class MapControlButton : ContentView
{
    /// <summary>Name of the bundled SVG glyph shown inside the circle.</summary>
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph),
        typeof(string),
        typeof(MapControlButton),
        string.Empty,
        propertyChanged: OnGlyphPropertyChanged);

    /// <summary>Localized accessibility description for the whole button.</summary>
    public static readonly BindableProperty SemanticDescriptionProperty = BindableProperty.Create(
        nameof(SemanticDescription),
        typeof(string),
        typeof(MapControlButton),
        string.Empty);

    /// <summary>Short badge (e.g. the compass "N") drawn above the glyph inside the circle.</summary>
    public static readonly BindableProperty BadgeProperty = BindableProperty.Create(
        nameof(Badge),
        typeof(string),
        typeof(MapControlButton),
        string.Empty);

    /// <summary>Raised when the button is tapped.</summary>
    public event EventHandler? Tapped;

    /// <summary>Creates the round button.</summary>
    public MapControlButton()
    {
        InitializeComponent();
    }

    /// <summary>Name of the bundled SVG glyph shown inside the circle.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Localized accessibility description for the whole button.</summary>
    public string SemanticDescription
    {
        get => (string)GetValue(SemanticDescriptionProperty);
        set => SetValue(SemanticDescriptionProperty, value);
    }

    /// <summary>Short badge (e.g. the compass "N") drawn above the glyph inside the circle.</summary>
    public string Badge
    {
        get => (string)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    private static void OnGlyphPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is MapControlButton button && newValue is string { Length: > 0 } source)
        {
            // Set in code: a compiled binding through x:Reference leaves the image blank.
            button.GlyphImage.Source = ImageSource.FromFile(source);
        }
    }

    private void OnTapped(object? sender, EventArgs e) => Tapped?.Invoke(this, e);
}
