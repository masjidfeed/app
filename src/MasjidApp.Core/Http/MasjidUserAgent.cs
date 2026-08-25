using System.Net.Http.Headers;

namespace MasjidApp.Core.Http;

/// <summary>Creates the product header used to identify Masjid App API requests.</summary>
public static class MasjidUserAgent
{
    private const string ApplicationName = "MasjidApp";
    private const int MaximumComponentLength = 100;

    /// <summary>Creates a <c>MasjidApp-{platform}/{version}</c> product value.</summary>
    public static ProductInfoHeaderValue Create(string platform, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var productName = $"{ApplicationName}-{platform}";
        if (productName.Length > MaximumComponentLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(platform),
                $"The application name must not exceed {MaximumComponentLength} characters.");
        }

        if (version.Length > MaximumComponentLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                $"The version must not exceed {MaximumComponentLength} characters.");
        }

        if (!char.IsAsciiLetterOrDigit(version[0]))
        {
            throw new ArgumentException("The version must begin with a letter or digit.", nameof(version));
        }

        return new ProductInfoHeaderValue(productName, version);
    }
}