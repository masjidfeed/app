using CommunityToolkit.Mvvm.ComponentModel;

namespace MasjidApp.ViewModels;

/// <summary>
/// Base class for all ViewModels. Built on <see cref="ObservableObject"/> from
/// CommunityToolkit.Mvvm so derived types use <c>[ObservableProperty]</c>/<c>[RelayCommand]</c>
/// instead of hand-written change notification.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>Indicates a long-running operation is in progress (e.g. to show a spinner).</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The page/section title bound by Views.</summary>
    [ObservableProperty]
    private string _title = string.Empty;
}
