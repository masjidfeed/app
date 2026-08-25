using System.Runtime.InteropServices;

namespace MasjidApp.Widgets;

internal static class WidgetTimelineReloader
{
    [DllImport("__Internal", EntryPoint = "MasjidAppReloadAllWidgetTimelines")]
    private static extern void ReloadAllWidgetTimelines();

    internal static void ReloadAll() => ReloadAllWidgetTimelines();
}
