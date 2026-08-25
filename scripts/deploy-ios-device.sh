#!/usr/bin/env bash
# Build and deploy the app to a locally connected, physical iOS device (DV-02).
#
# One-time device/host setup:
#   - Connect the device via USB (or Wi-Fi pairing), unlock it, and accept the
#     "Trust This Computer?" prompt.
#   - Device deploys need a real signing certificate + provisioning profiles, unlike simulator
#     runs which use ad-hoc signing (see MasjidApp.csproj). Two App IDs must be provisioned,
#     both with the App Group capability group.com.goodsoftware.masjidapp:
#         com.goodsoftware.masjidapp                          (the app)
#         com.goodsoftware.masjidapp.MasjidWidgetExtension    (the embedded WidgetKit .appex)
#     `dotnet build` does NOT generate an .xcodeproj for the app target, so profiles have to be
#     created by Xcode: (1) Xcode ▸ Settings ▸ Accounts ▸ add your Apple ID ▸ Manage Certificates
#     ▸ + ▸ Apple Development; (2) create a throwaway Xcode iOS App project outside this repo,
#     set the two bundle identifiers above on its app + Widget Extension targets, add App Groups
#     (group.com.goodsoftware.masjidapp) to both, tick "Automatically manage signing", select the connected
#     device as the run destination, and ⌘B. Xcode registers the device/App IDs and installs the
#     profiles; the throwaway project can then be deleted.
#     Verify with: ls ~/Library/Developer/Xcode/UserData/Provisioning\ Profiles \
#                     ~/Library/MobileDevice/Provisioning\ Profiles
#     Personal (free) Apple ID teams work; their profiles expire after ~7 days.
#
# Usage:
#   scripts/deploy-ios-device.sh [UDID]
#
# If UDID is omitted, connected physical devices are listed via `devicectl`; when exactly one
# is found it is used automatically, otherwise you're prompted to pass one explicitly.
#
# Uses `xcrun devicectl` (not `dotnet build -t:Run`/mlaunch) to install and launch, since
# mlaunch's older lockdown-based pairing can hang indefinitely on "Please connect the device"
# after a fresh trust/pairing, whereas devicectl reports clear errors instead.
set -euo pipefail

UDID="${1:-}"

if [ -z "$UDID" ]; then
  echo "Looking for connected iOS devices..."
  DEVICES_JSON="$(mktemp)"
  trap 'rm -f "$DEVICES_JSON"' EXIT
  xcrun devicectl list devices --filter "hardwareProperties.platform == 'iOS'" --json-output "$DEVICES_JSON" >/dev/null

  COUNT=$(python3 -c "import json,sys; print(len(json.load(open(sys.argv[1]))['result']['devices']))" "$DEVICES_JSON")

  if [ "$COUNT" -eq 0 ]; then
    echo "No connected iOS devices found. Plug in a device, unlock it, and accept 'Trust This Computer'." >&2
    exit 1
  elif [ "$COUNT" -gt 1 ]; then
    echo "Multiple devices found - re-run with one of these UDIDs:" >&2
    python3 -c "
import json, sys
devices = json.load(open(sys.argv[1]))['result']['devices']
for d in devices:
    print(f\"  {d['deviceProperties']['name']}: {d['hardwareProperties']['udid']}\")
" "$DEVICES_JSON" >&2
    exit 1
  fi

  UDID=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['result']['devices'][0]['hardwareProperties']['udid'])" "$DEVICES_JSON")
  NAME=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['result']['devices'][0]['deviceProperties']['name'])" "$DEVICES_JSON")
  echo "Found device: $NAME ($UDID)"
fi

echo "Building for device $UDID..."
dotnet build -f net10.0-ios -p:RuntimeIdentifier=ios-arm64 src/MasjidApp/MasjidApp.csproj

APP_PATH="src/MasjidApp/bin/Debug/net10.0-ios/ios-arm64/MasjidApp.app"
BUNDLE_ID="com.goodsoftware.masjidapp"

echo "Installing to device $UDID..."
xcrun devicectl device install app --device "$UDID" "$APP_PATH"

echo "Launching $BUNDLE_ID on device $UDID..."
xcrun devicectl device process launch --device "$UDID" "$BUNDLE_ID"
