#!/usr/bin/env bash
# Builds, signs, verifies, and optionally uploads MasjidApp to Google Play.
# One-time Play Console setup and the first manual AAB upload are documented in
# docs/PUBLISHING.md. After that bootstrap upload, this script can publish to any track.
#
# Usage:
#   scripts/publish-google-play.sh
#
# Configuration (all values can be overridden with environment variables):
#   ANDROID_KEYSTORE              Upload keystore path.
#   ANDROID_KEY_ALIAS             Upload key alias (default: masjidapp-upload).
#   ANDROID_SIGNING_PASSWORD_FILE File containing the keystore/key password.
#   GOOGLE_PLAY_JSON_KEY          Google Play service-account JSON path. When omitted,
#                                 the script builds and verifies but does not upload.
#   GOOGLE_PLAY_TRACK             internal, alpha, beta, or production (default: internal).
#   GOOGLE_PLAY_RELEASE_STATUS    draft or completed (default: draft).
#   GOOGLE_MAPS_API_KEY           Required production-restricted Android Maps key.
#   BUILD_NUMBER                  New ApplicationVersion. Defaults to current + 1.
#   APP_VERSION                   Optional new ApplicationDisplayVersion.
#   PUBLISH_ARTIFACTS_DIR         Optional output directory for the AAB and safe diagnostics.
#   NATIVE_SYMBOLS_ZIP            Optional output path for Play native debug symbols.
#   PUBLISHING_KEY_VAULT_NAME     Azure Key Vault holding release credentials. Defaults to
#                                 masjidfeed-publish; set to empty to use local credentials.
set -euo pipefail

cd "$(dirname "$0")/.."

CSPROJ="src/MasjidApp/MasjidApp.csproj"
WIDGET_INFO_PLIST="src/MasjidApp/Platforms/iOS/MasjidWidget/Info.plist"
PUBLISH_DIR="src/MasjidApp/bin/Release/net10.0-android/publish"
MERGED_MANIFEST="src/MasjidApp/obj/Release/net10.0-android/android/AndroidManifest.xml"

ANDROID_KEYSTORE="${ANDROID_KEYSTORE:-$HOME/.config/masjidapp/masjidapp-upload.keystore}"
ANDROID_KEY_ALIAS="${ANDROID_KEY_ALIAS:-masjidapp-upload}"
ANDROID_SIGNING_PASSWORD_FILE="${ANDROID_SIGNING_PASSWORD_FILE:-$HOME/.config/masjidapp/android-signing-password}"
GOOGLE_PLAY_JSON_KEY="${GOOGLE_PLAY_JSON_KEY:-$HOME/.config/masjidapp/google-play-service-account.json}"
GOOGLE_PLAY_TRACK="${GOOGLE_PLAY_TRACK:-internal}"
GOOGLE_PLAY_RELEASE_STATUS="${GOOGLE_PLAY_RELEASE_STATUS:-draft}"
PUBLISHING_KEY_VAULT_NAME="${PUBLISHING_KEY_VAULT_NAME:-masjidfeed-publish}"
KEY_VAULT_TEMP=""

export FASTLANE_OPT_OUT_USAGE=1
export FASTLANE_SKIP_UPDATE_CHECK=1
export FASTLANE_DISABLE_ANIMATION=1

fail() {
  echo "$*" >&2
  exit 1
}

require_file() {
  [ -f "$1" ] || fail "Required file not found: $1"
}

cleanup_key_vault_credentials() {
  [ -n "$KEY_VAULT_TEMP" ] && rm -rf "$KEY_VAULT_TEMP"
}

download_key_vault_secret() {
  local secret_name="$1" destination="$2"
  az keyvault secret download \
    --vault-name "$PUBLISHING_KEY_VAULT_NAME" \
    --name "$secret_name" \
    --file "$destination" \
    --only-show-errors
}

decode_base64() {
  local source="$1" destination="$2"
  if base64 --decode < "$source" > "$destination" 2>/dev/null; then
    return 0
  fi
  base64 -D < "$source" > "$destination"
}

if [ -n "$PUBLISHING_KEY_VAULT_NAME" ]; then
  command -v az >/dev/null 2>&1 ||
    fail "Azure CLI (az) is required when PUBLISHING_KEY_VAULT_NAME is set."
  command -v base64 >/dev/null 2>&1 ||
    fail "base64 is required when PUBLISHING_KEY_VAULT_NAME is set."

  KEY_VAULT_TEMP=$(mktemp -d)
  trap cleanup_key_vault_credentials EXIT

  echo "==> Downloading Google Play credentials from Key Vault $PUBLISHING_KEY_VAULT_NAME"
  download_key_vault_secret android-upload-keystore-base64 "$KEY_VAULT_TEMP/keystore.base64"
  decode_base64 "$KEY_VAULT_TEMP/keystore.base64" "$KEY_VAULT_TEMP/masjidapp-upload.keystore"
  download_key_vault_secret android-upload-keystore-password "$KEY_VAULT_TEMP/android-signing-password"
  download_key_vault_secret google-play-service-account-json-base64 "$KEY_VAULT_TEMP/service-account.base64"
  decode_base64 "$KEY_VAULT_TEMP/service-account.base64" "$KEY_VAULT_TEMP/google-play-service-account.json"
  download_key_vault_secret google-maps-api-key "$KEY_VAULT_TEMP/google-maps-api-key"
  rm -f "$KEY_VAULT_TEMP/keystore.base64" "$KEY_VAULT_TEMP/service-account.base64"
  chmod 600 "$KEY_VAULT_TEMP"/*

  ANDROID_KEYSTORE="$KEY_VAULT_TEMP/masjidapp-upload.keystore"
  ANDROID_SIGNING_PASSWORD_FILE="$KEY_VAULT_TEMP/android-signing-password"
  GOOGLE_PLAY_JSON_KEY="$KEY_VAULT_TEMP/google-play-service-account.json"
  GOOGLE_MAPS_API_KEY_FILE="$KEY_VAULT_TEMP/google-maps-api-key"
fi

if [ -n "${GOOGLE_MAPS_API_KEY_FILE:-}" ]; then
  [ -s "$GOOGLE_MAPS_API_KEY_FILE" ] ||
    fail "GOOGLE_MAPS_API_KEY_FILE is empty or missing: $GOOGLE_MAPS_API_KEY_FILE"
  GOOGLE_MAPS_API_KEY=$(tr -d '\r\n' < "$GOOGLE_MAPS_API_KEY_FILE")
fi

case "$GOOGLE_PLAY_TRACK" in
  internal|alpha|beta|production) ;;
  *) fail "GOOGLE_PLAY_TRACK must be internal, alpha, beta, or production." ;;
esac

case "$GOOGLE_PLAY_RELEASE_STATUS" in
  draft|completed) ;;
  *) fail "GOOGLE_PLAY_RELEASE_STATUS must be draft or completed." ;;
esac

for command in dotnet jarsigner keytool perl unzip zip; do
  command -v "$command" >/dev/null 2>&1 || fail "Required command not found: $command"
done

require_file "$ANDROID_KEYSTORE"
require_file "$ANDROID_SIGNING_PASSWORD_FILE"
[ -s "$ANDROID_SIGNING_PASSWORD_FILE" ] || fail "Signing password file is empty: $ANDROID_SIGNING_PASSWORD_FILE"
[ -n "${GOOGLE_MAPS_API_KEY:-}" ] ||
  fail "GOOGLE_MAPS_API_KEY is required for Google Play publish builds."

PACKAGE_NAME=$(sed -n 's:.*<ApplicationId>\([^<]*\)</ApplicationId>.*:\1:p' "$CSPROJ" | head -1)
[ "$PACKAGE_NAME" = "com.goodsoftware.masjidapp" ] ||
  fail "Unexpected ApplicationId '$PACKAGE_NAME'; expected com.goodsoftware.masjidapp."

echo "==> Step 1: resolve signing inputs"
echo "Package: $PACKAGE_NAME"
echo "Upload keystore: $ANDROID_KEYSTORE"
echo "Upload alias: $ANDROID_KEY_ALIAS"
echo "Google Play track: $GOOGLE_PLAY_TRACK ($GOOGLE_PLAY_RELEASE_STATUS)"

keytool -list \
  -keystore "$ANDROID_KEYSTORE" \
  -alias "$ANDROID_KEY_ALIAS" \
  -storepass:file "$ANDROID_SIGNING_PASSWORD_FILE" >/dev/null

echo "==> Step 2: version bump"
CURRENT_BUILD=$(sed -n 's:.*<ApplicationVersion>\([0-9][0-9]*\)</ApplicationVersion>.*:\1:p' "$CSPROJ" | head -1)
[ -n "$CURRENT_BUILD" ] || fail "Could not read ApplicationVersion from $CSPROJ."
NEW_BUILD="${BUILD_NUMBER:-$((CURRENT_BUILD + 1))}"
case "$NEW_BUILD" in
  ''|*[!0-9]*) fail "BUILD_NUMBER must be a positive integer." ;;
esac
[ "$NEW_BUILD" -gt 0 ] || fail "BUILD_NUMBER must be a positive integer."

CURRENT_BUILD="$CURRENT_BUILD" NEW_BUILD="$NEW_BUILD" perl -0pi -e '
  s{<ApplicationVersion>\Q$ENV{CURRENT_BUILD}\E</ApplicationVersion>}
   {<ApplicationVersion>$ENV{NEW_BUILD}</ApplicationVersion>}g
' "$CSPROJ"

# ApplicationVersion is shared by iOS and Android. Keep the native widget extension in
# sync so an Android release does not leave the next App Store build inconsistent.
NEW_BUILD="$NEW_BUILD" perl -0pi -e '
  s{(<key>CFBundleVersion</key>\s*<string>)[^<]*(</string>)}
   {$1$ENV{NEW_BUILD}$2}g
' "$WIDGET_INFO_PLIST"
echo "Build number: $CURRENT_BUILD -> $NEW_BUILD"

if [ -n "${APP_VERSION:-}" ]; then
  case "$APP_VERSION" in
    *[!0-9A-Za-z._+-]*) fail "APP_VERSION contains unsupported characters." ;;
  esac
  CURRENT_DISPLAY=$(sed -n 's:.*<ApplicationDisplayVersion>\([^<]*\)</ApplicationDisplayVersion>.*:\1:p' "$CSPROJ" | head -1)
  [ -n "$CURRENT_DISPLAY" ] || fail "Could not read ApplicationDisplayVersion from $CSPROJ."

  CURRENT_DISPLAY="$CURRENT_DISPLAY" APP_VERSION="$APP_VERSION" perl -0pi -e '
    s{<ApplicationDisplayVersion>\Q$ENV{CURRENT_DISPLAY}\E</ApplicationDisplayVersion>}
     {<ApplicationDisplayVersion>$ENV{APP_VERSION}</ApplicationDisplayVersion>}g
  ' "$CSPROJ"
  APP_VERSION="$APP_VERSION" perl -0pi -e '
    s{(<key>CFBundleShortVersionString</key>\s*<string>)[^<]*(</string>)}
     {$1$ENV{APP_VERSION}$2}g
  ' "$WIDGET_INFO_PLIST"
  echo "App version: $CURRENT_DISPLAY -> $APP_VERSION"
fi

echo "==> Step 3: build signed Android App Bundle"
rm -rf "$PUBLISH_DIR"

publish_args=(
  "$CSPROJ"
  -f net10.0-android
  -c Release
  -p:AndroidPackageFormats=aab
  -p:AndroidKeyStore=true
  "-p:AndroidSigningKeyStore=$ANDROID_KEYSTORE"
  "-p:AndroidSigningKeyAlias=$ANDROID_KEY_ALIAS"
  "-p:AndroidSigningKeyPass=file:$ANDROID_SIGNING_PASSWORD_FILE"
  "-p:AndroidSigningStorePass=file:$ANDROID_SIGNING_PASSWORD_FILE"
)

publish_args+=("-p:GoogleMapsApiKey=$GOOGLE_MAPS_API_KEY")

if [ -n "${PUBLISH_ARTIFACTS_DIR:-}" ]; then
  mkdir -p "$PUBLISH_ARTIFACTS_DIR"
fi

dotnet publish "${publish_args[@]}"

AAB_COUNT=$(find "$PUBLISH_DIR" -maxdepth 1 -type f -name '*-Signed.aab' | wc -l | tr -d '[:space:]')
[ "$AAB_COUNT" -eq 1 ] || fail "Expected one signed AAB in $PUBLISH_DIR, found $AAB_COUNT."
AAB=$(find "$PUBLISH_DIR" -maxdepth 1 -type f -name '*-Signed.aab' -print)
echo "Bundle: $AAB"

NATIVE_SYMBOLS_ZIP="${NATIVE_SYMBOLS_ZIP:-$PUBLISH_DIR/native-debug-symbols.zip}"
SYMBOLS_STAGING=$(mktemp -d)
if ! unzip -qq "$AAB" 'base/lib/*/*.so' -d "$SYMBOLS_STAGING" ||
   [ -z "$(find "$SYMBOLS_STAGING/base/lib" -type f -name '*.so' -print -quit)" ]; then
  rm -rf "$SYMBOLS_STAGING"
  fail "No native libraries found in $AAB for Play symbol upload."
fi
rm -f "$NATIVE_SYMBOLS_ZIP"
if ! (
  cd "$SYMBOLS_STAGING/base"
  zip -q -r "$NATIVE_SYMBOLS_ZIP" lib
); then
  rm -rf "$SYMBOLS_STAGING"
  fail "Could not create Play native debug symbols at $NATIVE_SYMBOLS_ZIP."
fi
rm -rf "$SYMBOLS_STAGING"
echo "Native debug symbols: $NATIVE_SYMBOLS_ZIP"

echo "==> Step 4: verify bundle"
jarsigner -verify -verbose -certs "$AAB" >/dev/null
unzip -tq "$NATIVE_SYMBOLS_ZIP" >/dev/null

KEYSTORE_SHA256=$(keytool -list -v \
  -keystore "$ANDROID_KEYSTORE" \
  -alias "$ANDROID_KEY_ALIAS" \
  -storepass:file "$ANDROID_SIGNING_PASSWORD_FILE" |
  sed -n 's/^[[:space:]]*SHA256:[[:space:]]*//p' | head -1 | tr -d ':[:space:]')
AAB_SHA256=$(keytool -printcert -jarfile "$AAB" |
  sed -n 's/^[[:space:]]*SHA256:[[:space:]]*//p' | head -1 | tr -d ':[:space:]')
[ -n "$KEYSTORE_SHA256" ] && [ "$KEYSTORE_SHA256" = "$AAB_SHA256" ] ||
  fail "The signed AAB certificate does not match the configured upload key."
echo "  OK   AAB signature matches the upload key ($AAB_SHA256)"

require_file "$MERGED_MANIFEST"
grep -q "package=\"$PACKAGE_NAME\"" "$MERGED_MANIFEST" ||
  fail "Merged manifest package does not match $PACKAGE_NAME."
grep -q "android:versionCode=\"$NEW_BUILD\"" "$MERGED_MANIFEST" ||
  fail "Merged manifest versionCode does not match $NEW_BUILD."
echo "  OK   package and versionCode match the project"

if [ -n "${PUBLISH_ARTIFACTS_DIR:-}" ]; then
  cp "$AAB" "$PUBLISH_ARTIFACTS_DIR/"
  cp "$NATIVE_SYMBOLS_ZIP" "$PUBLISH_ARTIFACTS_DIR/"
  cp "$MERGED_MANIFEST" "$PUBLISH_ARTIFACTS_DIR/AndroidManifest.xml"
  keytool -printcert -jarfile "$AAB" > "$PUBLISH_ARTIFACTS_DIR/upload-certificate.txt"
  grep -o 'android.permission.[^" ]*' "$MERGED_MANIFEST" |
    sort -u > "$PUBLISH_ARTIFACTS_DIR/permissions.txt"
fi

echo "==> Step 5: upload"
if [ -f "$GOOGLE_PLAY_JSON_KEY" ]; then
  command -v fastlane >/dev/null 2>&1 ||
    fail "fastlane is required when GOOGLE_PLAY_JSON_KEY is configured."

  fastlane supply \
    --aab "$AAB" \
    --mapping "$NATIVE_SYMBOLS_ZIP" \
    --package_name "$PACKAGE_NAME" \
    --json_key "$GOOGLE_PLAY_JSON_KEY" \
    --track "$GOOGLE_PLAY_TRACK" \
    --release_status "$GOOGLE_PLAY_RELEASE_STATUS" \
    --skip_upload_metadata true \
    --skip_upload_changelogs true \
    --skip_upload_images true \
    --skip_upload_screenshots true \
    --timeout 600
  echo "Uploaded build $NEW_BUILD to Google Play track '$GOOGLE_PLAY_TRACK' ($GOOGLE_PLAY_RELEASE_STATUS)."
else
  echo "No Google Play service-account key found at $GOOGLE_PLAY_JSON_KEY."
  echo "Build and verification completed without upload. Upload this bundle manually:"
  echo "  $AAB"
fi