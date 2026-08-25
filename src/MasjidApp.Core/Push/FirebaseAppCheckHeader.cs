namespace MasjidApp.Core.Push;

/// <summary>Applies the mandatory Firebase App Check token to push-registration requests.</summary>
public static class FirebaseAppCheckHeader
{
    /// <summary>The header required by the Masjid API push registration endpoint.</summary>
    public const string Name = "X-Firebase-AppCheck";

    /// <summary>Adds a non-empty App Check token to <paramref name="request"/>.</summary>
    public static void Add(HttpRequestMessage request, string token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        request.Headers.Add(Name, token);
    }
}