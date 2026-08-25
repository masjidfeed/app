#!/usr/bin/env bash
# Resize and center-crop screenshots for the App Store without modifying the originals.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUTPUT_DIR="${1:-$SCRIPT_DIR/formatted}"
TARGET_WIDTH=1242
TARGET_HEIGHT=2688
APP_ICON_SIZE=512

command -v sips >/dev/null 2>&1 || {
  echo "This script requires the macOS 'sips' command." >&2
  exit 1
}

mkdir -p "$OUTPUT_DIR"
shopt -s nullglob nocaseglob
images=("$SCRIPT_DIR"/*.png "$SCRIPT_DIR"/*.jpg "$SCRIPT_DIR"/*.jpeg)

if [ "${#images[@]}" -eq 0 ]; then
  echo "No PNG or JPEG images found in $SCRIPT_DIR" >&2
  exit 1
fi

for image in "${images[@]}"; do
  filename="$(basename "$image")"
  output="$OUTPUT_DIR/$filename"

  if [[ "$filename" == "appicon.png" ]]; then
    sips --resampleHeightWidth "$APP_ICON_SIZE" "$APP_ICON_SIZE" "$image" --out "$output" >/dev/null
    echo "Formatted $filename -> $output"
    continue
  fi

  source_width="$(sips -g pixelWidth "$image" | awk '/pixelWidth:/ { print $2 }')"
  source_height="$(sips -g pixelHeight "$image" | awk '/pixelHeight:/ { print $2 }')"

  if [ $((source_width * TARGET_HEIGHT)) -lt $((source_height * TARGET_WIDTH)) ]; then
    sips --resampleWidth "$TARGET_WIDTH" "$image" --out "$output" >/dev/null
  else
    sips --resampleHeight "$TARGET_HEIGHT" "$image" --out "$output" >/dev/null
  fi

  sips --cropToHeightWidth "$TARGET_HEIGHT" "$TARGET_WIDTH" "$output" >/dev/null
  echo "Formatted $filename -> $output"
done