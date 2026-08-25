using MasjidApp.Services;
using MasjidApp.ViewModels;

namespace MasjidApp.Views;

/// <summary>
/// "Add Masjid" page - lets the user enter a URL or scan a QR code (FR-ST-06).
/// QR scanning uses the device camera; a plain URL fallback is always available.
/// </summary>
public partial class AddMasjidPage : ContentPage
{
    private readonly AddMasjidViewModel _viewModel;
    private readonly IQrScannerService _qrScanner;

    public AddMasjidPage(AddMasjidViewModel viewModel, IQrScannerService qrScanner)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _qrScanner = qrScanner;
    }

    /// <summary>
    /// Opens the device camera for QR scanning.  On success, the decoded URL is placed in
    /// the URL entry so the user can review before connecting (FR-ST-06).
    /// Requires camera permission; degrades gracefully if denied (FR-CM-03).
    /// </summary>
    private async void OnScanQrClicked(object? sender, EventArgs e)
    {
        var value = await _qrScanner.ScanAsync(this);
        if (!string.IsNullOrWhiteSpace(value))
        {
            _viewModel.Url = value;
        }
    }
}
