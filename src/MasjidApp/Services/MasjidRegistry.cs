using System.Text.Json;
using MasjidApp.Core.Models;
using MasjidApp.Core.Serialization;
using MasjidApp.Core.Services;
using Microsoft.Maui.Storage;

namespace MasjidApp.Services;

// API-10, FR-ST-01: persisted multi-masjid registry backed by IPreferences.

/// <summary>
/// Persists the list of added masjids and the active selection in
/// <see cref="IPreferences"/>.  The list is stored as a single JSON array.
/// </summary>
public sealed class MasjidRegistry : IMasjidRegistry
{
    internal const string ListPreferenceKey = "masjidapp_registry_list";
    internal const string ActiveIdPreferenceKey = "masjidapp_registry_active_id";

    private readonly IPreferences _preferences;
    private List<MasjidStoredEntry> _entries;

    public MasjidRegistry(IPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _preferences = preferences;
        _entries = LoadEntries(preferences);
    }

    /// <inheritdoc/>
    public IReadOnlyList<MasjidStoredEntry> All => _entries;

    /// <inheritdoc/>
    public MasjidStoredEntry? Active
    {
        get
        {
            var activeId = _preferences.Get<string?>(ActiveIdPreferenceKey, null);
            return activeId is null
                ? _entries.Count > 0 ? _entries[0] : null
                : _entries.FirstOrDefault(e => e.Id == activeId);
        }
    }

    /// <inheritdoc/>
    public void Add(MasjidStoredEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.RemoveAll(e => e.Id == entry.Id);
        _entries.Add(entry);
        Save();

        // Auto-select if this is the first masjid.
        if (_entries.Count == 1)
        {
            SetActive(entry.Id);
        }
    }

    /// <inheritdoc/>
    public void Remove(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _entries.RemoveAll(e => e.Id == id);

        var currentActiveId = _preferences.Get<string?>(ActiveIdPreferenceKey, null);
        if (currentActiveId == id)
        {
            _preferences.Remove(ActiveIdPreferenceKey);
        }

        Save();
    }

    /// <inheritdoc/>
    public void SetActive(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!_entries.Any(e => e.Id == id))
        {
            throw new InvalidOperationException($"Masjid '{id}' is not in the registry.");
        }

        _preferences.Set(ActiveIdPreferenceKey, id);
    }

    // ── Persistence ────────────────────────────────────────────────────────────

    private void Save()
    {
        var json = JsonSerializer.Serialize(_entries, MasjidCoreJsonContext.Default.ListMasjidStoredEntry);
        _preferences.Set(ListPreferenceKey, json);
    }

    private static List<MasjidStoredEntry> LoadEntries(IPreferences preferences)
    {
        var json = preferences.Get<string?>(ListPreferenceKey, null);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, MasjidCoreJsonContext.Default.ListMasjidStoredEntry)
                   ?? [];
        }
        catch (JsonException)
        {
            // NFR-RL-01: tolerate corrupted preference data - start fresh.
            return [];
        }
    }
}
