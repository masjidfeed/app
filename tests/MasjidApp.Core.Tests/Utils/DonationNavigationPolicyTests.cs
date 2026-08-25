using FluentAssertions;
using MasjidApp.Core.Utils;

namespace MasjidApp.Core.Tests.Utils;

public sealed class DonationNavigationPolicyTests
{
    [Theory]
    [InlineData("https://example.com/donate")]
    [InlineData("https://example.com:8443/donate")]
    public void GetSafeDonationUri_HttpsUrl_ReturnsUri(string value)
    {
        DonationNavigationPolicy.GetSafeDonationUri(value).Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/donate")]
    [InlineData("http://example.com/donate")]
    [InlineData("https://user:password@example.com/donate")]
    public void GetSafeDonationUri_UnsafeUrl_ReturnsNull(string? value)
    {
        DonationNavigationPolicy.GetSafeDonationUri(value).Should().BeNull();
    }

    [Theory]
    [InlineData("https://example.com/donate/checkout")]
    [InlineData("https://example.com:443/donate#zakat")]
    public void GetNavigationAction_SameOriginHttpsUrl_OpensInWebView(string navigationUrl)
    {
        var donationUri = new Uri("https://example.com/donate");

        DonationNavigationPolicy.GetNavigationAction(donationUri, navigationUrl)
            .Should().Be(DonationNavigationAction.OpenInWebView);
    }

    [Theory]
    [InlineData("https://payments.example.com/checkout")]
    [InlineData("https://example.org/checkout")]
    [InlineData("https://example.com:8443/checkout")]
    public void GetNavigationAction_DifferentOriginHttpsUrl_OpensInSystemBrowser(
        string navigationUrl)
    {
        var donationUri = new Uri("https://example.com/donate");

        DonationNavigationPolicy.GetNavigationAction(donationUri, navigationUrl)
            .Should().Be(DonationNavigationAction.OpenInSystemBrowser);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("http://example.com/checkout")]
    [InlineData("mailto:donate@example.com")]
    public void GetNavigationAction_UnsafeNavigation_Blocks(string? navigationUrl)
    {
        var donationUri = new Uri("https://example.com/donate");

        DonationNavigationPolicy.GetNavigationAction(donationUri, navigationUrl)
            .Should().Be(DonationNavigationAction.Block);
    }

    [Fact]
    public void GetNavigationAction_UnsafeDonationUri_Blocks()
    {
        var donationUri = new Uri("http://example.com/donate");

        DonationNavigationPolicy.GetNavigationAction(
                donationUri,
                "https://example.com/checkout")
            .Should().Be(DonationNavigationAction.Block);
    }
}