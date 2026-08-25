namespace MasjidApp.Services;

/// <summary>Presents the shared camera scanner and returns one decoded URL.</summary>
public interface IQrScannerService
{
    Task<string?> ScanAsync(Page host, CancellationToken ct = default);
}
