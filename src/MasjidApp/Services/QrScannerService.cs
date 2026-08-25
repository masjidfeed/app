using MasjidApp.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MasjidApp.Services;

/// <inheritdoc/>
public sealed class QrScannerService(IServiceProvider services) : IQrScannerService
{
    /// <inheritdoc/>
    public async Task<string?> ScanAsync(Page host, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        var scanner = services.GetRequiredService<QrScannerPage>();
        var resultTask = scanner.WaitForResultAsync(ct);
        await host.Navigation.PushModalAsync(scanner);
        return await resultTask;
    }
}
