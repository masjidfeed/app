using FluentAssertions;
using MasjidApp.Core.Push;

namespace MasjidApp.Core.Tests.Push;

public sealed class FirebaseAppCheckHeaderTests
{
    [Fact]
    public void Add_ValidToken_AddsMandatoryHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.org/push/registrations");

        FirebaseAppCheckHeader.Add(request, "current-app-check-token");

        request.Headers.GetValues("X-Firebase-AppCheck")
            .Should().ContainSingle().Which.Should().Be("current-app-check-token");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_MissingToken_Throws(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "https://example.org/push/registrations");

        var act = () => FirebaseAppCheckHeader.Add(request, token);

        act.Should().Throw<ArgumentException>();
        request.Headers.Contains(FirebaseAppCheckHeader.Name).Should().BeFalse();
    }
}