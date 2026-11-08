#!/usr/bin/env bash
# Start the masjid_test Android AVD, wait for full boot, then run the app.
# Safe to run when the emulator is already running (skips launch in that case).
#
# The embedded Google map on Android needs a build-time API key. It is pulled
# from the same Key Vault secret the release build uses and cached locally so
# the dev loop does not hit the vault on every run (REFRESH_MAPS_KEY=1 forces
# a re-fetch). When the vault is unreachable the build continues keyless and
# the app shows its branded address fallback instead of the map.
#
# Configuration:
#   PUBLISHING_KEY_VAULT_NAME   Azure Key Vault holding the key (default: masjidfeed-publish).
#   GOOGLE_MAPS_API_KEY         Skip the vault entirely and use this key.
#   GOOGLE_MAPS_API_KEY_FILE    Override the local cache path.
#   REFRESH_MAPS_KEY            Set to 1 to re-download the cached key.
set -euo pipefail

ADB="$HOME/Library/Android/sdk/platform-tools/adb"
EMU="$HOME/Library/Android/sdk/emulator/emulator"

MAPS_KEY_VAULT="${PUBLISHING_KEY_VAULT_NAME:-masjidfeed-publish}"
MAPS_KEY_CACHE="${GOOGLE_MAPS_API_KEY_FILE:-$HOME/.config/masjidapp/google-maps-api-key}"

fetch_maps_api_key() {
  if [ -n "${GOOGLE_MAPS_API_KEY:-}" ]; then
    return
  fi
  if [ "${REFRESH_MAPS_KEY:-0}" = "1" ] || [ ! -s "$MAPS_KEY_CACHE" ]; then
    if ! command -v az >/dev/null 2>&1; then
      echo "warning: az CLI not found - building without the Google Maps key (address fallback shown)." >&2
      return
    fi
    echo "==> Fetching Google Maps API key from Key Vault $MAPS_KEY_VAULT"
    mkdir -p "$(dirname "$MAPS_KEY_CACHE")"
    if ! az keyvault secret download \
        --vault-name "$MAPS_KEY_VAULT" \
        --name google-maps-api-key \
        --file "$MAPS_KEY_CACHE" \
        --only-show-errors; then
      rm -f "$MAPS_KEY_CACHE"
      echo "warning: could not fetch the Google Maps key from Key Vault $MAPS_KEY_VAULT - building keyless." >&2
      return
    fi
    chmod 600 "$MAPS_KEY_CACHE"
  fi
  GOOGLE_MAPS_API_KEY=$(tr -d '\r\n' < "$MAPS_KEY_CACHE")
  export GOOGLE_MAPS_API_KEY
}

fetch_maps_api_key

if "$ADB" devices | grep -q emulator; then
  echo "Android emulator already running - skipping launch."
else
  echo "Starting masjid_test AVD..."
  nohup "$EMU" -avd masjid_test -no-audio -no-boot-anim -gpu swiftshader_indirect >> /tmp/masjid_emu.log 2>&1 &
fi

echo "Waiting for device to come online..."
"$ADB" wait-for-device
echo "Waiting for full boot..."
until [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do sleep 1; done
echo "Android emulator ready."

dotnet build -f net10.0-android src/MasjidApp/MasjidApp.csproj
dotnet build -t:Run -f net10.0-android src/MasjidApp/MasjidApp.csproj
