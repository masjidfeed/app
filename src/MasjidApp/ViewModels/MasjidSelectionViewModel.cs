using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Models;
using MasjidApp.Core.Services;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

// FR-ST-01, API-10: ViewModel for the masjid selection screen.

/// <summary>
/// ViewModel for the masjid-selection screen.  Shows the list of added masjids,
/// lets the user switch the active one, or navigate to the "Add Masjid" screen.
/// </summary>
public sealed partial class MasjidSelectionViewModel : ViewModelBase
{
    private readonly IMasjidRegistry _registry;
    private readonly IMasjidContext _context;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private MasjidStoredEntry? _activeMasjid;

    public ObservableCollection<MasjidStoredEntry> Masjids { get; } = [];

    public MasjidSelectionViewModel(
        IMasjidRegistry registry,
        IMasjidContext context,
        INavigationService navigation)
    {
        _registry = registry;
        _context = context;
        _navigation = navigation;
        Refresh();
    }

    /// <summary>Reload the list from the registry (call after adding / removing).</summary>
    public void Refresh()
    {
        Masjids.Clear();
        foreach (var entry in _registry.All)
        {
            Masjids.Add(entry);
        }

        ActiveMasjid = _registry.Active;
    }

    [RelayCommand]
    private void SelectMasjid(MasjidStoredEntry entry)
    {
        _context.SetActive(entry);
        ActiveMasjid = entry;
    }

    [RelayCommand]
    private void RemoveMasjid(MasjidStoredEntry entry)
    {
        _registry.Remove(entry.Id);
        Refresh();
    }

    [RelayCommand]
    private async Task AddMasjidAsync()
    {
        await _navigation.GoToAsync(Routes.AddMasjid);
    }
}
