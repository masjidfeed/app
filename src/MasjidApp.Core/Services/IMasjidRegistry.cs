using MasjidApp.Core.Models;

namespace MasjidApp.Core.Services;

// API-10, FR-ST-01, FR-ST-06: persisted list of added masjids.

/// <summary>
/// Manages the persisted list of masjids the user has added and which one is active.
/// </summary>
public interface IMasjidRegistry
{
    /// <summary>All masjids the user has added (order preserved).</summary>
    IReadOnlyList<MasjidStoredEntry> All { get; }

    /// <summary>The active masjid, or <c>null</c> if none has been selected.</summary>
    MasjidStoredEntry? Active { get; }

    /// <summary>
    /// Adds <paramref name="entry"/> to the registry (replacing any entry with the same ID)
    /// and persists the change.
    /// </summary>
    void Add(MasjidStoredEntry entry);

    /// <summary>
    /// Removes the masjid with <paramref name="id"/> from the registry.
    /// If the removed masjid was active, the active selection is cleared.
    /// </summary>
    void Remove(string id);

    /// <summary>
    /// Sets <paramref name="id"/> as the active masjid and persists the choice.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <paramref name="id"/> is not in <see cref="All"/>.
    /// </exception>
    void SetActive(string id);
}
