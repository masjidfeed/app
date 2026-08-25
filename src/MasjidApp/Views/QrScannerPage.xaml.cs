using MasjidApp.Core.Configuration;
using ZXing.Net.Maui;

namespace MasjidApp.Views;

public partial class QrScannerPage : ContentPage
{
    private TaskCompletionSource<string?>? _completion;
    private int _handled;

    public QrScannerPage()
    {
        InitializeComponent();
        CameraView.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.TwoDimensional,
            AutoRotate = true,
            Multiple = false,
        };
    }

    public Task<string?> WaitForResultAsync(CancellationToken ct)
    {
        _completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => _completion.TrySetCanceled(ct));
        return _completion.Task;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _handled = 0;
        var status = await Permissions.RequestAsync<Permissions.Camera>();
        CameraView.IsDetecting = status == PermissionStatus.Granted;
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlertAsync(
                "Camera Permission",
                "Camera access is needed to scan QR codes. You can still type the URL manually.",
                "OK");
            await CompleteAsync(null);
        }
    }

    protected override void OnDisappearing()
    {
        CameraView.IsDetecting = false;
        base.OnDisappearing();
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results.FirstOrDefault()?.Value;
        var configUrl = MasjidConfigUrl.Resolve(value);
        if (configUrl is null || !MasjidConfigUrl.IsAllowed(configUrl) || Interlocked.Exchange(ref _handled, 1) != 0)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            CameraView.IsDetecting = false;
            await CompleteAsync(value);
        });
    }

    private async void OnCancelClicked(object? sender, EventArgs e) =>
        await CompleteAsync(null);

    private async Task CompleteAsync(string? value)
    {
        _completion?.TrySetResult(value);
        await Navigation.PopModalAsync();
    }
}
