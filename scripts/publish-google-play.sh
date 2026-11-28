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
#   GOOGLE_PLAY_TRACK             internal, alpha, beta, or production (default: production).
#   GOOGLE_PLAY_RELEASE_STATUS    draft or completed (default: completed).
#   GOOGLE_MAPS_API_KEY           Required production-restricted Android Maps key.
#   BUILD_NUMBER                  New ApplicationVersion. Defaults to current + 1.
#   APP_VERSION                   New ApplicationDisplayVersion (marketing version, e.g.
#                                 "1.1"). Defaults to the first line of
#                                 src/MasjidApp/app-version.txt, the same file the project
#                                 builds from.
#   PUBLISH_ARTIFACTS_DIR         Optional output directory for the AAB and safe diagnostics.
#   PUBLISHING_KEY_VAULT_NAME     Azure Key Vault holding release credentials. Defaults to
#                                 masjidfeed-publish; set to empty to use local credentials.
set -euo pipefail

cd "$(dirname "$0")/.."

CSPROJ="src/MasjidApp/MasjidApp.csproj"
WIDGET_INFO_PLIST="src/MasjidApp/Platforms/iOS/MasjidWidget/Info.plist"
VERSION_FILE="src/MasjidApp/app-version.txt"
PUBLISH_DIR="src/MasjidApp/bin/Release/net10.0-android/publish"
MERGED_MANIFEST="src/MasjidApp/obj/Release/net10.0-android/android/AndroidManifest.xml"

ANDROID_KEYSTORE="${ANDROID_KEYSTORE:-$HOME/.config/masjidapp/masjidapp-upload.keystore}"
ANDROID_KEY_ALIAS="${ANDROID_KEY_ALIAS:-masjidapp-upload}"
ANDROID_SIGNING_PASSWORD_FILE="${ANDROID_SIGNING_PASSWORD_FILE:-$HOME/.config/masjidapp/android-signing-password}"
GOOGLE_PLAY_JSON_KEY="${GOOGLE_PLAY_JSON_KEY:-$HOME/.config/masjidapp/google-play-service-account.json}"
GOOGLE_PLAY_TRACK="${GOOGLE_PLAY_TRACK:-production}"
GOOGLE_PLAY_RELEASE_STATUS="${GOOGLE_PLAY_RELEASE_STATUS:-completed}"
PUBLISHING_KEY_VAULT_NAME="${PUBLISHING_KEY_VAULT_NAME:-masjidfeed-publish}"
KEY_VAULT_TEMP=""
PLAY_API="https://androidpublisher.googleapis.com"
ACCESS_TOKEN=""
EDIT_ID=""

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

# Play allows only one in-progress edit per app; discard an uncommitted edit so a
# failed upload never blocks the next run. Always chained with the Key Vault cleanup.
cleanup_play_edit() {
  if [ -n "$EDIT_ID" ]; then
    curl -fsS --max-time 60 -X DELETE \
      -H "Authorization: Bearer $ACCESS_TOKEN" \
      "$PLAY_API/androidpublisher/v3/applications/$PACKAGE_NAME/edits/$EDIT_ID" >/dev/null 2>&1 || true
    EDIT_ID=""
  fi
}

cleanup() {
  cleanup_play_edit
  cleanup_key_vault_credentials
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
  trap cleanup EXIT

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

for command in dotnet jarsigner keytool perl jq openssl curl; do
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

# The marketing version comes from app-version.txt (the csproj reads the same file at
# build time); APP_VERSION is only a one-off override. Keep the iOS widget extension in
# sync so an Android release does not leave the next App Store build inconsistent.
if [ -z "${APP_VERSION:-}" ]; then
  [ -f "$VERSION_FILE" ] || fail "APP_VERSION not set and $VERSION_FILE not found."
  APP_VERSION=$(head -1 "$VERSION_FILE" | tr -d '[:space:]')
  [ -n "$APP_VERSION" ] || fail "$VERSION_FILE does not contain a version."
fi
case "$APP_VERSION" in
  *[!0-9A-Za-z._+-]*) fail "APP_VERSION contains unsupported characters." ;;
esac
APP_VERSION="$APP_VERSION" perl -0pi -e '
  s{(<key>CFBundleShortVersionString</key>\s*<string>)[^<]*(</string>)}
   {$1$ENV{APP_VERSION}$2}g
' "$WIDGET_INFO_PLIST"
echo "App version: $APP_VERSION"

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

MAPPING_FILE="$PUBLISH_DIR/mapping.txt"
[ -s "$MAPPING_FILE" ] || MAPPING_FILE=""
[ -n "$MAPPING_FILE" ] && echo "ProGuard mapping: $MAPPING_FILE"

echo "==> Step 4: verify bundle"
jarsigner -verify -verbose -certs "$AAB" >/dev/null

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
  [ -n "$MAPPING_FILE" ] && cp "$MAPPING_FILE" "$PUBLISH_ARTIFACTS_DIR/"
  cp "$MERGED_MANIFEST" "$PUBLISH_ARTIFACTS_DIR/AndroidManifest.xml"
  keytool -printcert -jarfile "$AAB" > "$PUBLISH_ARTIFACTS_DIR/upload-certificate.txt"
  grep -o 'android.permission.[^" ]*' "$MERGED_MANIFEST" |
    sort -u > "$PUBLISH_ARTIFACTS_DIR/permissions.txt"
fi

b64url() {
  openssl base64 -A | tr '+/' '-_' | tr -d '=\n'
}

mint_google_play_access_token() {
  local client_email private_key now header claims signature response
  client_email=$(jq -r .client_email "$GOOGLE_PLAY_JSON_KEY")
  private_key=$(jq -r .private_key "$GOOGLE_PLAY_JSON_KEY")
  [ "$client_email" != "null" ] && [ -n "$client_email" ] ||
    fail "Service account JSON has no client_email: $GOOGLE_PLAY_JSON_KEY"
  [ "$private_key" != "null" ] && [ -n "$private_key" ] ||
    fail "Service account JSON has no private_key: $GOOGLE_PLAY_JSON_KEY"
  now=$(date +%s)
  header=$(printf '{"alg":"RS256","typ":"JWT"}' | b64url)
  claims=$(printf '{"iss":"%s","scope":"https://www.googleapis.com/auth/androidpublisher","aud":"https://oauth2.googleapis.com/token","exp":%s,"iat":%s}' \
    "$client_email" "$((now + 3600))" "$now" | b64url)
  signature=$(printf '%s.%s' "$header" "$claims" |
    openssl dgst -sha256 -sign <(printf '%s' "$private_key") -binary | b64url)
  response=$(curl -fsS --max-time 60 \
    -d 'grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer' \
    --data-urlencode "assertion=$header.$claims.$signature" \
    https://oauth2.googleapis.com/token) ||
    fail "Could not mint a Google Play access token from the service account."
  printf '%s' "$response" | jq -r .access_token
}

# Play only accepts deobfuscation files for a bundle inside the same edit that uploads
# the bundle, so the AAB and its R8 mapping.txt are uploaded into one edit here instead
# of via Fastlane (whose --mapping flag also rejects files when the bundle was already
# committed in an earlier edit). Native/AOT .so debug symbols are stripped by the .NET
# Android toolchain in Release, so there is nothing valid to upload for nativeCode.
upload_to_google_play() {
  local bundles_response bundle_version current_releases new_releases
  local commit_response commit_error

  ACCESS_TOKEN=$(mint_google_play_access_token)
  EDIT_ID=$(curl -fsS --max-time 60 -X POST \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H 'Content-Type: application/json' \
    -d '{}' \
    "$PLAY_API/androidpublisher/v3/applications/$PACKAGE_NAME/edits" | jq -r .id)
  [ -n "$EDIT_ID" ] && [ "$EDIT_ID" != "null" ] ||
    fail "Could not create a Google Play edit for the upload."

  echo "Uploading AAB (versionCode $NEW_BUILD)..."
  bundles_response=$(curl -fsS --max-time 900 -X POST \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H 'Content-Type: application/octet-stream' \
    --data-binary "@$AAB" \
    "$PLAY_API/upload/androidpublisher/v3/applications/$PACKAGE_NAME/edits/$EDIT_ID/bundles?uploadType=media") ||
    fail "Google Play rejected the AAB upload."
  bundle_version=$(printf '%s' "$bundles_response" | jq -r .versionCode)
  [ "$bundle_version" = "$NEW_BUILD" ] ||
    fail "Google Play assigned versionCode $bundle_version instead of $NEW_BUILD."

  if [ -n "$MAPPING_FILE" ]; then
    echo "Uploading ProGuard mapping for versionCode $NEW_BUILD..."
    curl -fsS --max-time 600 -X POST \
      -H "Authorization: Bearer $ACCESS_TOKEN" \
      -H 'Content-Type: application/octet-stream' \
      --data-binary "@$MAPPING_FILE" \
      "$PLAY_API/upload/androidpublisher/v3/applications/$PACKAGE_NAME/edits/$EDIT_ID/apks/$NEW_BUILD/deobfuscationFiles/proguard?uploadType=media" >/dev/null ||
      fail "Google Play rejected the ProGuard mapping upload."
    echo "  OK   ProGuard mapping attached to versionCode $NEW_BUILD"
  fi

  # Add the build to the configured track without dropping its other releases.
  current_releases=$(curl -fsS --max-time 60 \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    "$PLAY_API/androidpublisher/v3/applications/$PACKAGE_NAME/edits/$EDIT_ID/tracks/$GOOGLE_PLAY_TRACK" |
    jq -c '.releases // []')
  new_releases=$(printf '%s' "$current_releases" | jq -c --arg vc "$NEW_BUILD" --arg st "$GOOGLE_PLAY_RELEASE_STATUS" '
    if any(.[]; .status == $st)
    then map(if .status == $st then .versionCodes = ((.versionCodes // []) + [$vc] | unique) else . end)
    else . + [{"status": $st, "versionCodes": [$vc]}]
    end')
  curl -fsS --max-time 60 -X PUT \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H 'Content-Type: application/json' \
    -d "$(printf '{"track":"%s","releases":%s}' "$GOOGLE_PLAY_TRACK" "$new_releases")" \
    "$PLAY_API/androidpublisher/v3/applications/$PACKAGE_NAME/edits/$EDIT_ID/tracks/$GOOGLE_PLAY_TRACK" >/dev/null ||
    fail "Could not add versionCode $NEW_BUILD to the $GOOGLE_PLAY_TRACK track."

  if ! commit_response=$(curl --fail-with-body -sS --max-time 300 -X POST \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    "$PLAY_API/androidpublisher/v3/applications/$PACKAGE_NAME/edits/$EDIT_ID:commit"); then
    commit_error=$(jq -r '.error.message // empty' <<< "$commit_response" 2>/dev/null) || commit_error=""
    if [ -n "$commit_error" ]; then
      printf 'Google Play: %s\n' "$commit_error" >&2
    fi
    fail "Could not commit the Google Play edit for the '$GOOGLE_PLAY_TRACK' track."
  fi
  EDIT_ID=""
  echo "Uploaded build $NEW_BUILD to Google Play track '$GOOGLE_PLAY_TRACK' ($GOOGLE_PLAY_RELEASE_STATUS)."
}

echo "==> Step 5: upload"
if [ -f "$GOOGLE_PLAY_JSON_KEY" ]; then
  upload_to_google_play
else
  echo "No Google Play service-account key found at $GOOGLE_PLAY_JSON_KEY."
  echo "Build and verification completed without upload. Upload this bundle manually:"
  echo "  $AAB"
fi