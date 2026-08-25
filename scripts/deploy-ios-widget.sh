#!/usr/bin/env bash
# Incrementally rebuild and deploy the native WidgetKit extension without rebuilding MAUI.
set -euo pipefail

UDID="${IOS_SIMULATOR_UDID:-80A8D4F0-2036-4960-9ABB-579ABB05004B}"
APP_ID="com.goodsoftware.masjidapp"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_BUNDLE="$ROOT/src/MasjidApp/bin/Debug/net10.0-ios/iossimulator-arm64/MasjidApp.app"
WIDGET_DIR="$ROOT/src/MasjidApp/Platforms/iOS/MasjidWidget"
DERIVED_DATA="$ROOT/src/MasjidApp/obj/Debug/net10.0-ios/iossimulator-arm64/widget-extension"
APPEX="$DERIVED_DATA/Build/Products/Debug-iphonesimulator/MasjidWidgetExtension.appex"
APP_ENTITLEMENTS="$ROOT/src/MasjidApp/Platforms/iOS/Entitlements.Simulator.plist"
WIDGET_ENTITLEMENTS="$WIDGET_DIR/MasjidWidgetExtension.entitlements"

if [[ ! -d "$APP_BUNDLE" ]]; then
    echo "Host app not found: $APP_BUNDLE" >&2
    echo "Run scripts/start-ios.sh once before using this incremental widget deploy." >&2
    exit 1
fi

xcrun simctl boot "$UDID" 2>/dev/null || true
xcrun simctl bootstatus "$UDID" -b

xcodebuild \
    -project "$WIDGET_DIR/MasjidWidgetExtension.xcodeproj" \
    -scheme MasjidWidgetExtension \
    -configuration Debug \
    -sdk iphonesimulator \
    -destination "generic/platform=iOS Simulator" \
    -derivedDataPath "$DERIVED_DATA" \
    CODE_SIGNING_ALLOWED=NO \
    ENABLE_DEBUG_DYLIB=NO \
    build

if [[ ! -d "$APPEX" ]]; then
    echo "Widget build produced no extension at: $APPEX" >&2
    exit 1
fi

WIDGET_DESTINATION="$APP_BUNDLE/PlugIns/MasjidWidgetExtension.appex"
rm -rf "$WIDGET_DESTINATION"
mkdir -p "$(dirname "$WIDGET_DESTINATION")"
ditto "$APPEX" "$WIDGET_DESTINATION"

codesign --force --timestamp=none --sign - \
    --entitlements "$WIDGET_ENTITLEMENTS" \
    "$WIDGET_DESTINATION"

codesign --force --timestamp=none --sign - \
    --entitlements "$APP_ENTITLEMENTS" \
    "$APP_BUNDLE"

xcrun simctl terminate "$UDID" "$APP_ID" 2>/dev/null || true
xcrun simctl install "$UDID" "$APP_BUNDLE"
xcrun simctl launch "$UDID" "$APP_ID"