namespace MasjidApp.Core.Models;

// API-10: each stored masjid entry - lightweight identifier persisted in app preferences.

/// <summary>
/// Lightweight descriptor for a masjid stored in the local registry.
/// Contains enough information to fetch the full config document on demand.
/// </summary>
/// <param name="Id">Stable identifier from the config document (<c>masjid.id</c>).</param>
/// <param name="Name">Display name from the config document (<c>masjid.name</c>).</param>
/// <param name="ConfigUrl">Full URL to the masjid's <c>/config</c> endpoint.</param>
public sealed record MasjidStoredEntry(string Id, string Name, string ConfigUrl);
