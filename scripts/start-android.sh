#!/usr/bin/env bash
# Start the masjid_test Android AVD, wait for full boot, then run the app.
# Safe to run when the emulator is already running (skips launch in that case).
set -euo pipefail

ADB="$HOME/Library/Android/sdk/platform-tools/adb"
EMU="$HOME/Library/Android/sdk/emulator/emulator"

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
