#!/usr/bin/env bash
# Boot the iPhone 17 simulator, build, install, and launch without attaching a debugger.
set -euo pipefail

UDID="80A8D4F0-2036-4960-9ABB-579ABB05004B"
APP_ID="com.goodsoftware.masjidapp"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/MasjidApp/MasjidApp.csproj"
APP_BUNDLE="$ROOT/src/MasjidApp/bin/Debug/net10.0-ios/iossimulator-arm64/MasjidApp.app"
EMBED_WIDGET_EXTENSION="${EMBED_WIDGET_EXTENSION:-false}"

xcrun simctl boot "$UDID" 2>/dev/null || true
xcrun simctl bootstatus "$UDID" -b
open -a Simulator
echo "iOS Simulator ready."

if [[ ! -f "$ROOT/src/MasjidApp/obj/project.assets.json" ]]; then
	dotnet restore "$PROJECT"
fi

dotnet build "$PROJECT" -c Debug -f net10.0-ios --no-restore \
	-p:RuntimeIdentifier=iossimulator-arm64 \
	-p:EmbedWidgetExtension="$EMBED_WIDGET_EXTENSION"
xcrun simctl terminate "$UDID" "$APP_ID" 2>/dev/null || true
xcrun simctl install "$UDID" "$APP_BUNDLE"
xcrun simctl launch "$UDID" "$APP_ID"
