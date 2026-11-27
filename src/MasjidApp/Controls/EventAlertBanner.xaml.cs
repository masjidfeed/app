using MasjidApp.Core.Events;

namespace MasjidApp.Controls;

/// <summary>Displays an event alert with a warning or cancellation icon.</summary>
public partial class EventAlertBanner : Border
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(EventAlertBanner),
        propertyChanged: static (bindable, _, value) => ((EventAlertBanner)bindable).UpdateText((string?)value));

    public static readonly BindableProperty TextStyleProperty = BindableProperty.Create(
        nameof(TextStyle),
        typeof(Style),
        typeof(EventAlertBanner),
        propertyChanged: static (bindable, _, value) => ((EventAlertBanner)bindable).AlertMessage.Style = (Style?)value);

    public EventAlertBanner()
    {
        InitializeComponent();
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Style? TextStyle
    {
        get => (Style?)GetValue(TextStyleProperty);
        set => SetValue(TextStyleProperty, value);
    }

    private void UpdateText(string? text)
    {
        AlertMessage.Text = text;
        VisualStateManager.GoToState(this, EventAlertText.IndicatesCancellation(text) ? "Cancellation" : "Normal");
    }
}
