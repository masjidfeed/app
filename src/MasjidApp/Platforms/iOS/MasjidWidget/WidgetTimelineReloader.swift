import WidgetKit

@_cdecl("MasjidAppReloadAllWidgetTimelines")
public func MasjidAppReloadAllWidgetTimelines() {
    WidgetCenter.shared.reloadAllTimelines()
}