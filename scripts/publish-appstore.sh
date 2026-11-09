#!/usr/bin/env bash
# Automates docs/PUBLISHING.md Steps 2-6: repo prep, version bump, archive build,
# verification, and upload. Step 1 (Apple portal setup: App Group, App IDs, certificate,
# provisioning profiles) is one-time and manual - see docs/PUBLISHING.md.
#
# Usage:
#   scripts/publish-appstore.sh
#
# Everything is auto-detected from the keychain, the installed provisioning profiles, and
# ~/.appstoreconnect. Set any of the variables below only to override that detection.
#
#   CODESIGN_KEY               Signing identity. Defaults to the sole "Apple Distribution"
#                              identity in the keychain; errors if there are several.
#   CODESIGN_PROVISION         App Store profile NAME for <ApplicationId>. Defaults to the
#                              newest unexpired profile matching the app's bundle id with
#                              no ProvisionedDevices, preferring an explicitly named
#                              profile over Xcode's managed "iOS Team Store ..." one.
#   WIDGET_CODESIGN_PROVISION  Same, for <ApplicationId>.MasjidWidgetExtension. When found,
#                              its .mobileprovision is embedded into the built .appex and
#                              both bundles are re-signed with a real timestamp (required
#                              for App Store; WidgetExtension.targets only does a
#                              --timestamp=none dev signature). When no profile exists the
#                              widget is left OUT of the build (-p:EmbedWidgetExtension=false)
#                              so the archive still validates.
#   NOTIFICATION_SERVICE_CODESIGN_PROVISION
#                              App Store profile NAME for
#                              <ApplicationId>.NotificationServiceExtension.
#   API_KEY_ID                 App Store Connect API key id. Defaults to the sole
#                              AuthKey_<id>.p8 found in altool's search paths.
#   API_ISSUER_ID              Issuer ID. Not derivable from the .p8, so it defaults to the
#                              contents of ~/.appstoreconnect/issuer_id. Without both this
#                              and API_KEY_ID the script stops after building the archive
#                              and prints Xcode Organizer upload instructions.
#   XCODE_APP                  Name of the /Applications/*.app bundle to build with, without
#                              the .app suffix (useful when several Xcode versions are
#                              installed side by side). Defaults to "Xcode_26.5".
#   BUILD_NUMBER               New ApplicationVersion (build number). Defaults to the
#                              current value + 1.
#   APP_VERSION                New ApplicationDisplayVersion (marketing version, e.g.
#                              "1.1"). Defaults to the first line of
#                              src/MasjidApp/app-version.txt, the same file the project
#                              builds from.
#   PUBLISH_ARTIFACTS_DIR      Optional directory for crash-debugging outputs. When set,
#                              receives the exact uploaded IPA and the MSBuild binary log.
#   PUBLISHING_KEY_VAULT_NAME  Azure Key Vault holding release credentials. Defaults to
#                              masjidfeed-publish; set to empty to use local credentials.
set -euo pipefail

cd "$(dirname "$0")/.."

XCODE_APP="${XCODE_APP:-Xcode_26.5}"
DEVELOPER_DIR="/Applications/${XCODE_APP}.app/Contents/Developer"
[ -d "$DEVELOPER_DIR" ] || {
  echo "No Xcode found at /Applications/${XCODE_APP}.app (set XCODE_APP to override)." >&2
  exit 1
}
export DEVELOPER_DIR
echo "Using $(DEVELOPER_DIR="$DEVELOPER_DIR" xcodebuild -version | head -1) at $DEVELOPER_DIR"

APP_GROUP="group.com.goodsoftware.masjidapp"
CSPROJ="src/MasjidApp/MasjidApp.csproj"
IOS_INFO_PLIST="src/MasjidApp/Platforms/iOS/Info.plist"
WIDGET_INFO_PLIST="src/MasjidApp/Platforms/iOS/MasjidWidget/Info.plist"
NOTIFICATION_SERVICE_INFO_PLIST="src/MasjidApp/Platforms/iOS/NotificationServiceExtension/Info.plist"
VERSION_FILE="src/MasjidApp/app-version.txt"

BUNDLE_ID=$(grep -o '<ApplicationId>[^<]*</ApplicationId>' "$CSPROJ" | sed 's/<[^>]*>//g')
WIDGET_BUNDLE_ID="$BUNDLE_ID.MasjidWidgetExtension"
NOTIFICATION_SERVICE_BUNDLE_ID="$BUNDLE_ID.NotificationServiceExtension"
PUBLISHING_KEY_VAULT_NAME="${PUBLISHING_KEY_VAULT_NAME:-masjidfeed-publish}"
KEY_VAULT_TEMP=""
SIGNING_KEYCHAIN=""
ORIGINAL_KEYCHAINS=()

cleanup_key_vault_credentials() {
  if [ "${#ORIGINAL_KEYCHAINS[@]}" -gt 0 ]; then
    security list-keychains -d user -s "${ORIGINAL_KEYCHAINS[@]}" >/dev/null 2>&1 || true
  fi
  [ -n "$SIGNING_KEYCHAIN" ] && security delete-keychain "$SIGNING_KEYCHAIN" >/dev/null 2>&1 || true
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

install_key_vault_profile() {
  local secret_name="$1" label="$2" encoded profile uuid name
  encoded="$KEY_VAULT_TEMP/$secret_name.base64"
  profile="$KEY_VAULT_TEMP/$secret_name.mobileprovision"
  download_key_vault_secret "$secret_name" "$encoded"
  openssl base64 -d -A -in "$encoded" -out "$profile"
  uuid=$(security cms -D -i "$profile" | plutil -extract UUID raw -)
  name=$(security cms -D -i "$profile" | plutil -extract Name raw -)
  mkdir -p "$HOME/Library/MobileDevice/Provisioning Profiles" \
           "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles"
  install -m 600 "$profile" "$HOME/Library/MobileDevice/Provisioning Profiles/$uuid.mobileprovision"
  install -m 600 "$profile" "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles/$uuid.mobileprovision"
  echo "Installed Key Vault $label profile: $name ($uuid)"
}

if [ -n "$PUBLISHING_KEY_VAULT_NAME" ]; then
  command -v az >/dev/null 2>&1 || {
    echo "Azure CLI (az) is required when PUBLISHING_KEY_VAULT_NAME is set." >&2
    exit 1
  }
  command -v openssl >/dev/null 2>&1 || {
    echo "openssl is required when PUBLISHING_KEY_VAULT_NAME is set." >&2
    exit 1
  }

  KEY_VAULT_TEMP=$(mktemp -d)
  trap cleanup_key_vault_credentials EXIT
  umask 077

  while IFS= read -r keychain; do
    keychain=${keychain#\"}
    keychain=${keychain%\"}
    [ -n "$keychain" ] && ORIGINAL_KEYCHAINS+=("$keychain")
  done < <(security list-keychains -d user | sed 's/^[[:space:]]*//')

  echo "==> Downloading App Store credentials from Key Vault $PUBLISHING_KEY_VAULT_NAME"
  download_key_vault_secret ios-dist-cert-p12-base64 "$KEY_VAULT_TEMP/cert.base64"
  download_key_vault_secret ios-dist-cert-password "$KEY_VAULT_TEMP/cert-password"
  openssl base64 -d -A -in "$KEY_VAULT_TEMP/cert.base64" -out "$KEY_VAULT_TEMP/cert.p12"

  SIGNING_KEYCHAIN="$KEY_VAULT_TEMP/signing.keychain-db"
  keychain_password=$(openssl rand -base64 32)
  security create-keychain -p "$keychain_password" "$SIGNING_KEYCHAIN"
  security set-keychain-settings -lut 21600 "$SIGNING_KEYCHAIN"
  security unlock-keychain -p "$keychain_password" "$SIGNING_KEYCHAIN"
  security import "$KEY_VAULT_TEMP/cert.p12" -k "$SIGNING_KEYCHAIN" \
    -P "$(cat "$KEY_VAULT_TEMP/cert-password")" -A -t cert -f pkcs12
  security set-key-partition-list -S apple-tool:,apple:,codesign: \
    -s -k "$keychain_password" "$SIGNING_KEYCHAIN" >/dev/null
  security list-keychains -d user -s "$SIGNING_KEYCHAIN" "${ORIGINAL_KEYCHAINS[@]}"

  install_key_vault_profile ios-provisioning-profile-base64 app
  install_key_vault_profile ios-widget-provisioning-profile-base64 widget
  install_key_vault_profile ios-notification-service-profile-base64 "notification service"

  API_PRIVATE_KEYS_DIR="$KEY_VAULT_TEMP/private_keys"
  mkdir -p "$API_PRIVATE_KEYS_DIR"
  download_key_vault_secret appstore-api-key-id "$KEY_VAULT_TEMP/api-key-id"
  download_key_vault_secret appstore-api-issuer-id "$KEY_VAULT_TEMP/api-issuer-id"
  download_key_vault_secret appstore-api-private-key-base64 "$KEY_VAULT_TEMP/api-key.base64"
  API_KEY_ID=$(tr -d '[:space:]' < "$KEY_VAULT_TEMP/api-key-id")
  API_ISSUER_ID=$(tr -d '[:space:]' < "$KEY_VAULT_TEMP/api-issuer-id")
  openssl base64 -d -A -in "$KEY_VAULT_TEMP/api-key.base64" \
    -out "$API_PRIVATE_KEYS_DIR/AuthKey_$API_KEY_ID.p8"
  chmod 600 "$API_PRIVATE_KEYS_DIR/AuthKey_$API_KEY_ID.p8"
fi

# ── Step 1: resolve signing inputs ───────────────────────────────────────────
# Everything below is auto-detected so the script can be run with no arguments. Any
# value can still be overridden by exporting the matching environment variable.

echo "==> Step 1: resolve signing inputs"

# Signing identity: the single "Apple Distribution" identity in the keychain.
# (macOS ships bash 3.2, so this file avoids mapfile and empty-array expansion.)
if [ -z "${CODESIGN_KEY:-}" ]; then
  DIST_IDS=$(security find-identity -v -p codesigning ${SIGNING_KEYCHAIN:+"$SIGNING_KEYCHAIN"} |
    sed -n 's/.*"\(Apple Distribution: [^"]*\)".*/\1/p' | sort -u)
  DIST_COUNT=$(printf '%s' "$DIST_IDS" | grep -c . || true)
  if [ "$DIST_COUNT" -eq 0 ]; then
    echo "No 'Apple Distribution' identity in the keychain. See docs/PUBLISHING.md Step 1." >&2
    exit 1
  elif [ "$DIST_COUNT" -gt 1 ]; then
    echo "Multiple Apple Distribution identities found; set CODESIGN_KEY to one of:" >&2
    printf '  %s\n' "$DIST_IDS" >&2
    exit 1
  fi
  CODESIGN_KEY="$DIST_IDS"
fi
echo "Signing identity: $CODESIGN_KEY"

TEAM_ID=$(printf '%s' "$CODESIGN_KEY" | sed -n 's/.*(\([A-Z0-9]*\))$/\1/p')
[ -n "$TEAM_ID" ] || { echo "Could not parse a team ID out of CODESIGN_KEY." >&2; exit 1; }

# App Store profile for a bundle id: matching application-identifier, no ProvisionedDevices
# (development and ad-hoc profiles list devices), and not expired. Xcode silently generates
# its own "iOS Team Store Provisioning Profile: ..." duplicates, so prefer an explicitly
# named profile and fall back to the managed one. Ties break on the latest expiry.
find_appstore_profile() {
  local want="$TEAM_ID.$1" best="" best_exp="" managed="" managed_exp="" p name appid exp
  for f in ~/Library/MobileDevice/Provisioning\ Profiles/*.mobileprovision \
           ~/Library/Developer/Xcode/UserData/Provisioning\ Profiles/*.mobileprovision; do
    [ -e "$f" ] || continue
    p=$(security cms -D -i "$f" 2>/dev/null) || continue
    appid=$(printf '%s' "$p" | plutil -extract Entitlements.application-identifier raw - 2>/dev/null) || continue
    [ "$appid" = "$want" ] || continue
    printf '%s' "$p" | plutil -extract ProvisionedDevices raw - >/dev/null 2>&1 && continue
    exp=$(printf '%s' "$p" | plutil -extract ExpirationDate raw - 2>/dev/null || echo "")
    [ "$exp" \> "$(date -u +%Y-%m-%dT%H:%M:%SZ)" ] || continue
    name=$(printf '%s' "$p" | plutil -extract Name raw - 2>/dev/null) || continue
    case "$name" in
      "iOS Team "*)
        if [ -z "$managed_exp" ] || [ "$exp" \> "$managed_exp" ]; then
          managed="$name"; managed_exp="$exp"
        fi ;;
      *)
        if [ -z "$best_exp" ] || [ "$exp" \> "$best_exp" ]; then
          best="$name"; best_exp="$exp"
        fi ;;
    esac
  done
  [ -n "$best" ] && { printf '%s' "$best"; return 0; }
  [ -n "$managed" ] && { printf '%s' "$managed"; return 0; }
  return 1
}

if [ -z "${CODESIGN_PROVISION:-}" ]; then
  CODESIGN_PROVISION=$(find_appstore_profile "$BUNDLE_ID") || {
    echo "No valid App Store provisioning profile for $TEAM_ID.$BUNDLE_ID." >&2
    echo "Create one and download it in Xcode, or set CODESIGN_PROVISION. See docs/PUBLISHING.md Step 1." >&2
    exit 1
  }
fi
echo "App profile: $CODESIGN_PROVISION"

if [ -z "${WIDGET_CODESIGN_PROVISION:-}" ]; then
  WIDGET_CODESIGN_PROVISION=$(find_appstore_profile "$WIDGET_BUNDLE_ID") || WIDGET_CODESIGN_PROVISION=""
fi
if [ -n "$WIDGET_CODESIGN_PROVISION" ]; then
  echo "Widget profile: $WIDGET_CODESIGN_PROVISION"
else
  echo "Widget profile: none found - the widget will be left out of this build."
fi

if [ -z "${NOTIFICATION_SERVICE_CODESIGN_PROVISION:-}" ]; then
  NOTIFICATION_SERVICE_CODESIGN_PROVISION=$(find_appstore_profile "$NOTIFICATION_SERVICE_BUNDLE_ID") || NOTIFICATION_SERVICE_CODESIGN_PROVISION=""
fi
if [ -n "$NOTIFICATION_SERVICE_CODESIGN_PROVISION" ]; then
  echo "Notification service profile: $NOTIFICATION_SERVICE_CODESIGN_PROVISION"
else
  echo "Notification service profile: none found - rich push images will be left out of this build."
fi

# App Store Connect API key: the single AuthKey_<id>.p8 in altool's search paths.
if [ -z "${API_KEY_ID:-}" ]; then
  KEY_IDS=""
  for d in "./private_keys" "$HOME/private_keys" "$HOME/.private_keys" \
           "$HOME/.appstoreconnect/private_keys" "${API_PRIVATE_KEYS_DIR:-}"; do
    [ -n "$d" ] && [ -d "$d" ] || continue
    for k in "$d"/AuthKey_*.p8; do
      [ -e "$k" ] || continue
      k=${k##*/AuthKey_}
      KEY_IDS="$KEY_IDS${k%.p8}
"
    done
  done
  KEY_IDS=$(printf '%s' "$KEY_IDS" | grep . | sort -u || true)
  KEY_COUNT=$(printf '%s' "$KEY_IDS" | grep -c . || true)
  if [ "$KEY_COUNT" -gt 1 ]; then
    echo "Multiple AuthKey_*.p8 files found; set API_KEY_ID to one of:" >&2
    printf '  %s\n' "$KEY_IDS" >&2
    exit 1
  elif [ "$KEY_COUNT" -eq 1 ]; then
    API_KEY_ID="$KEY_IDS"
  fi
fi

# The issuer ID is not derivable from the .p8, so read it from a local untracked file.
ISSUER_FILE="$HOME/.appstoreconnect/issuer_id"
if [ -z "${API_ISSUER_ID:-}" ] && [ -f "$ISSUER_FILE" ]; then
  API_ISSUER_ID=$(tr -d '[:space:]' < "$ISSUER_FILE")
fi

if [ -n "${API_KEY_ID:-}" ] && [ -n "${API_ISSUER_ID:-}" ]; then
  echo "API key: $API_KEY_ID (issuer $API_ISSUER_ID)"
else
  echo "API key: not configured - the build will stop before uploading."
  echo "  See docs/PUBLISHING.md Step 6 to set one up."
fi

# ── Step 2: prepare the repo ─────────────────────────────────────────────────

echo "==> Step 2: export compliance"
if plutil -extract ITSAppUsesNonExemptEncryption raw "$IOS_INFO_PLIST" >/dev/null 2>&1; then
  echo "ITSAppUsesNonExemptEncryption already present, leaving as-is."
else
  # NOTE: do NOT use `plutil -insert` here. plutil re-serializes the whole file, which
  # alphabetically reorders every key and strips all XML comments - this file carries
  # requirement-ID comments (NFR-PR-01, FR-WG-04, FR-CM-02) that must survive. Insert the
  # key textually just before the closing </dict> instead.
  awk '
    /^<\/dict>/ && !done {
      print "\t<!-- App uses only standard HTTPS: exempt from export compliance. -->"
      print "\t<key>ITSAppUsesNonExemptEncryption</key>"
      print "\t<false/>"
      done = 1
    }
    { print }
  ' "$IOS_INFO_PLIST" > "$IOS_INFO_PLIST.tmp"
  plutil -lint "$IOS_INFO_PLIST.tmp" >/dev/null
  mv "$IOS_INFO_PLIST.tmp" "$IOS_INFO_PLIST"
  echo "Added ITSAppUsesNonExemptEncryption=false to $IOS_INFO_PLIST."
fi

# ── Step 3: bump the version ─────────────────────────────────────────────────

echo "==> Step 3: version bump"
CURRENT_BUILD=$(grep -o '<ApplicationVersion>[0-9]*</ApplicationVersion>' "$CSPROJ" | grep -o '[0-9]*')
NEW_BUILD="${BUILD_NUMBER:-$((CURRENT_BUILD + 1))}"

sed -i '' "s#<ApplicationVersion>$CURRENT_BUILD</ApplicationVersion>#<ApplicationVersion>$NEW_BUILD</ApplicationVersion>#" "$CSPROJ"
plutil -replace CFBundleVersion -string "$NEW_BUILD" "$WIDGET_INFO_PLIST"
plutil -replace CFBundleVersion -string "$NEW_BUILD" "$NOTIFICATION_SERVICE_INFO_PLIST"
echo "Build number: $CURRENT_BUILD -> $NEW_BUILD"

# The marketing version comes from app-version.txt (the csproj reads the same file at
# build time); APP_VERSION is only a one-off override. The extensions do not go through
# MSBuild, so their plists are synced here.
if [ -z "${APP_VERSION:-}" ]; then
  [ -f "$VERSION_FILE" ] || {
    echo "APP_VERSION not set and $VERSION_FILE not found." >&2
    exit 1
  }
  APP_VERSION=$(head -1 "$VERSION_FILE" | tr -d '[:space:]')
  [ -n "$APP_VERSION" ] || {
    echo "$VERSION_FILE does not contain a version." >&2
    exit 1
  }
fi
case "$APP_VERSION" in
  *[!0-9A-Za-z._+-]*) { echo "APP_VERSION contains unsupported characters." >&2; exit 1; } ;;
esac
plutil -replace CFBundleShortVersionString -string "$APP_VERSION" "$WIDGET_INFO_PLIST"
plutil -replace CFBundleShortVersionString -string "$APP_VERSION" "$NOTIFICATION_SERVICE_INFO_PLIST"
echo "Marketing version: $APP_VERSION"

# ── Step 4: build the archive ────────────────────────────────────────────────

echo "==> Step 4: build archive"

# Device AOT modules hard-bind the MVIDs of every referenced assembly. Reusing part of an
# incremental output after an SDK/workload change can produce a signed app that passes App
# Store validation but aborts before managed startup. Rebuild the complete device slice so
# its assemblies, AOT data, native objects, resources, and app bundle are one generation.
rm -rf \
  src/MasjidApp/bin/Release/net10.0-ios/ios-arm64 \
  src/MasjidApp/obj/Release/net10.0-ios/ios-arm64

EMBED_WIDGET=false
if [ -n "${WIDGET_CODESIGN_PROVISION:-}" ]; then
  EMBED_WIDGET=true
else
  echo "WIDGET_CODESIGN_PROVISION not set - building without the widget extension" \
       "(-p:EmbedWidgetExtension=false). See docs/PUBLISHING.md Step 2."
fi

EMBED_NOTIFICATION_SERVICE=false
if [ -n "${NOTIFICATION_SERVICE_CODESIGN_PROVISION:-}" ]; then
  EMBED_NOTIFICATION_SERVICE=true
else
  echo "NOTIFICATION_SERVICE_CODESIGN_PROVISION not set - building without rich push images" \
       "(-p:EmbedNotificationServiceExtension=false)."
fi

publish_app() {
  dotnet publish "$CSPROJ" \
    -f net10.0-ios -c Release \
    -p:RuntimeIdentifier=ios-arm64 \
    -p:ArchiveOnBuild=true \
    -p:EmbedWidgetExtension="$EMBED_WIDGET" \
    -p:EmbedNotificationServiceExtension="$EMBED_NOTIFICATION_SERVICE" \
    -p:CodesignKey="$CODESIGN_KEY" \
    -p:CodesignProvision="$CODESIGN_PROVISION" \
    "$@"
}

if [ -n "${PUBLISH_ARTIFACTS_DIR:-}" ]; then
  mkdir -p "$PUBLISH_ARTIFACTS_DIR"
  publish_app "-bl:$PUBLISH_ARTIFACTS_DIR/MasjidApp.binlog"
else
  publish_app
fi

ARCHIVE=$(ls -td ~/Library/Developer/Xcode/Archives/*/*.xcarchive | head -1)
APP="$ARCHIVE/Products/Applications/MasjidApp.app"
echo "Archive: $ARCHIVE"

# Embed each native extension's App Store profile and sign nested bundles before the app.
if [ "$EMBED_WIDGET" = true ] || [ "$EMBED_NOTIFICATION_SERVICE" = true ]; then
  find_installed_profile() {
    local wanted_name="$1" f name
    for f in ~/Library/MobileDevice/Provisioning\ Profiles/*.mobileprovision \
             ~/Library/Developer/Xcode/UserData/Provisioning\ Profiles/*.mobileprovision; do
      [ -e "$f" ] || continue
      name=$(security cms -D -i "$f" 2>/dev/null | plutil -extract Name raw - 2>/dev/null || true)
      if [ "$name" = "$wanted_name" ]; then
        printf '%s' "$f"
        return 0
      fi
    done
    return 1
  }

  APP_PROFILE=$(find_installed_profile "$CODESIGN_PROVISION") || {
    echo "Could not find an installed provisioning profile named $CODESIGN_PROVISION." >&2
    exit 1
  }
  ENTS_DIR=$(mktemp -d)
  security cms -D -i "$APP_PROFILE" | plutil -extract Entitlements xml1 -o "$ENTS_DIR/app.plist" -

  if [ "$EMBED_WIDGET" = true ]; then
    echo "Embedding widget provisioning profile '$WIDGET_CODESIGN_PROVISION'..."
    WIDGET_PROFILE=$(find_installed_profile "$WIDGET_CODESIGN_PROVISION") || {
      echo "Could not find an installed provisioning profile named '$WIDGET_CODESIGN_PROVISION'." >&2
      exit 1
    }
    security cms -D -i "$WIDGET_PROFILE" | plutil -extract Entitlements xml1 -o "$ENTS_DIR/widget.plist" -
    WIDGET_APPEX="$APP/PlugIns/MasjidWidgetExtension.appex"
    cp "$WIDGET_PROFILE" "$WIDGET_APPEX/embedded.mobileprovision"
    codesign --force --timestamp --sign "$CODESIGN_KEY" --entitlements "$ENTS_DIR/widget.plist" "$WIDGET_APPEX"
  fi

  if [ "$EMBED_NOTIFICATION_SERVICE" = true ]; then
    echo "Embedding notification service profile '$NOTIFICATION_SERVICE_CODESIGN_PROVISION'..."
    NOTIFICATION_SERVICE_PROFILE=$(find_installed_profile "$NOTIFICATION_SERVICE_CODESIGN_PROVISION") || {
      echo "Could not find an installed provisioning profile named '$NOTIFICATION_SERVICE_CODESIGN_PROVISION'." >&2
      exit 1
    }
    security cms -D -i "$NOTIFICATION_SERVICE_PROFILE" | plutil -extract Entitlements xml1 -o "$ENTS_DIR/notification-service.plist" -
    NOTIFICATION_SERVICE_APPEX="$APP/PlugIns/MasjidNotificationServiceExtension.appex"
    cp "$NOTIFICATION_SERVICE_PROFILE" "$NOTIFICATION_SERVICE_APPEX/embedded.mobileprovision"
    codesign --force --timestamp --sign "$CODESIGN_KEY" --entitlements "$ENTS_DIR/notification-service.plist" "$NOTIFICATION_SERVICE_APPEX"
  fi

  cp "$APP_PROFILE" "$APP/embedded.mobileprovision"
  /usr/libexec/PlistBuddy -c \
    "Delete :com.apple.developer.devicecheck.appattest-environment" \
    "$ENTS_DIR/app.plist"
  /usr/libexec/PlistBuddy -c \
    "Add :com.apple.developer.devicecheck.appattest-environment string production" \
    "$ENTS_DIR/app.plist"
  codesign --force --timestamp --sign "$CODESIGN_KEY" --entitlements "$ENTS_DIR/app.plist" "$APP"
  rm -rf "$ENTS_DIR"
fi

# ── Step 5: verify ────────────────────────────────────────────────────────────

echo "==> Step 5: verify"
FAILED=0

verify_aot_dependencies() {
  local app="$1" owners referenced stale aot_data module owner_guid refs guid
  owners=$(mktemp)
  referenced=$(mktemp)

  for aot_data in "$app"/*.aotdata.arm64; do
    [ -f "$aot_data" ] || continue
    module=${aot_data##*/}
    module=${module%.aotdata.arm64}
    owner_guid=$(
      strings -a "$aot_data" | awk -v module="$module" '
        !found && $0 == module { next_is_guid = 1; next }
        next_is_guid { print; found = 1; next_is_guid = 0 }
      '
    )
    if ! printf '%s\n' "$owner_guid" |
      grep -Eq '^[0-9A-F]{8}(-[0-9A-F]{4}){3}-[0-9A-F]{12}$'; then
      echo "  FAIL could not read the owner MVID from ${aot_data##*/}" >&2
      rm -f "$owners" "$referenced"
      return 1
    fi
    printf '%s\n' "$owner_guid" >> "$owners"
    strings -a "$aot_data" |
      grep -E '^[0-9A-F]{8}(-[0-9A-F]{4}){3}-[0-9A-F]{12}$' >> "$referenced" || true
  done

  if [ ! -s "$owners" ]; then
    echo "  FAIL app bundle contains no arm64 AOT metadata" >&2
    rm -f "$owners" "$referenced"
    return 1
  fi

  sort -u "$owners" -o "$owners"
  sort -u "$referenced" -o "$referenced"
  stale=$(comm -23 "$referenced" "$owners")
  if [ -n "$stale" ]; then
    echo "  FAIL AOT modules reference dependency MVIDs absent from this app bundle:" >&2
    while IFS= read -r guid; do
      refs=""
      for aot_data in "$app"/*.aotdata.arm64; do
        if strings -a "$aot_data" | grep -F "$guid" >/dev/null; then
          module=${aot_data##*/}
          refs="${refs:+$refs, }${module%.aotdata.arm64}"
        fi
      done
      echo "       $guid <- $refs" >&2
    done <<< "$stale"
    rm -f "$owners" "$referenced"
    return 1
  fi

  rm -f "$owners" "$referenced"
  echo "  OK   all AOT dependency MVIDs match packaged AOT modules"
}

if ! verify_aot_dependencies "$APP"; then
  FAILED=1
fi

# NOTE: do not use `plutil -extract com.apple.security.application-groups`. plutil treats
# dots as key-path separators, so that entitlement name is read as a nested path and the
# lookup always fails. Print the whole plist and grep it instead.
check_entitlements() {
  local target="$1" label="$2" ents
  ents=$(codesign -d --entitlements :- "$target" 2>/dev/null | plutil -p - 2>/dev/null || true)
  for key in "$APP_GROUP" "application-identifier" "com.apple.developer.team-identifier"; do
    if printf '%s' "$ents" | grep -q -- "$key"; then
      echo "  OK   $label has $key"
    else
      echo "  FAIL $label missing $key" >&2
      FAILED=1
    fi
  done
}

check_entitlement_value() {
  local target="$1" label="$2" key="$3" expected="$4" ents actual
  ents=$(codesign -d --entitlements :- "$target" 2>/dev/null |
    plutil -convert xml1 -o - - 2>/dev/null || true)
  actual=$(printf '%s' "$ents" | awk -v key="$key" '
    index($0, "<key>" key "</key>") {
      getline
      sub(/^[[:space:]]*<string>/, "")
      sub(/<\/string>[[:space:]]*$/, "")
      print
      exit
    }
  ')
  if [ "$actual" = "$expected" ]; then
    echo "  OK   $label has $key=$expected"
  else
    echo "  FAIL $label has $key=${actual:-missing}; expected $expected" >&2
    FAILED=1
  fi
}

check_entitlements "$APP" "app"
check_entitlement_value "$APP" "app" "aps-environment" "production"
check_entitlement_value \
  "$APP" \
  "app" \
  "com.apple.developer.devicecheck.appattest-environment" \
  "production"

if plutil -extract ITSAppUsesNonExemptEncryption raw "$APP/Info.plist" >/dev/null 2>&1; then
  echo "  OK   ITSAppUsesNonExemptEncryption present in built Info.plist"
else
  echo "  FAIL ITSAppUsesNonExemptEncryption missing from built Info.plist" >&2
  FAILED=1
fi

# Every name listed under CFBundleAlternateIcons must resolve to a PNG at the BUNDLE ROOT.
# A BundleResource without LogicalName keeps its source folder, which ships the icon at
# Platforms/iOS/AlternateIcons/<name>@2x.png and earns an ITMS-90032 rejection email
# hours after a "successful" upload. Catch it here instead.
ALT_ICONS=$(plutil -extract CFBundleIcons.CFBundleAlternateIcons xml1 -o - "$APP/Info.plist" 2>/dev/null |
  sed -n 's/.*<string>\(.*\)<\/string>.*/\1/p' | sort -u || true)
if [ -z "$ALT_ICONS" ]; then
  echo "  OK   no alternate icons declared"
else
  OLD_IFS=$IFS
  IFS='
'
  for icon in $ALT_ICONS; do
    IFS=$OLD_IFS
    [ -n "$icon" ] || continue
    if [ -f "$APP/$icon@2x.png" ] || [ -f "$APP/$icon.png" ] || [ -f "$APP/$icon@3x.png" ]; then
      echo "  OK   alternate icon $icon found at bundle root"
    else
      echo "  FAIL alternate icon $icon missing from bundle root (ITMS-90032)" >&2
      FAILED=1
    fi
    IFS='
'
  done
  IFS=$OLD_IFS
fi

if [ "$EMBED_WIDGET" = true ]; then
  check_entitlements "$APP/PlugIns/MasjidWidgetExtension.appex" "widget"
  if [ -f "$APP/PlugIns/MasjidWidgetExtension.appex/embedded.mobileprovision" ]; then
    echo "  OK   widget has embedded.mobileprovision"
  else
    echo "  FAIL widget missing embedded.mobileprovision" >&2
    FAILED=1
  fi
fi

if [ "$EMBED_NOTIFICATION_SERVICE" = true ]; then
  NOTIFICATION_SERVICE_APPEX="$APP/PlugIns/MasjidNotificationServiceExtension.appex"
  check_entitlement_value \
    "$NOTIFICATION_SERVICE_APPEX" \
    "notification service" \
    "application-identifier" \
    "$TEAM_ID.$NOTIFICATION_SERVICE_BUNDLE_ID"
  check_entitlement_value \
    "$NOTIFICATION_SERVICE_APPEX" \
    "notification service" \
    "com.apple.developer.team-identifier" \
    "$TEAM_ID"
  if [ -f "$NOTIFICATION_SERVICE_APPEX/embedded.mobileprovision" ]; then
    echo "  OK   notification service has embedded.mobileprovision"
  else
    echo "  FAIL notification service missing embedded.mobileprovision" >&2
    FAILED=1
  fi
  if [ "$(plutil -extract NSExtension.NSExtensionPointIdentifier raw "$NOTIFICATION_SERVICE_APPEX/Info.plist" 2>/dev/null || true)" = "com.apple.usernotifications.service" ]; then
    echo "  OK   notification service has the expected extension point"
  else
    echo "  FAIL notification service has an invalid extension point" >&2
    FAILED=1
  fi
  if codesign --verify --deep --strict "$NOTIFICATION_SERVICE_APPEX" 2>/dev/null; then
    echo "  OK   notification service signature is valid"
  else
    echo "  FAIL notification service signature is invalid" >&2
    FAILED=1
  fi
fi

if [ "$FAILED" -ne 0 ]; then
  echo "Verification failed - not uploading. See docs/PUBLISHING.md." >&2
  exit 1
fi

# ── Step 6: upload ───────────────────────────────────────────────────────────

echo "==> Step 6: upload"
if [ -n "${API_KEY_ID:-}" ] && [ -n "${API_ISSUER_ID:-}" ]; then
  # altool looks for AuthKey_<API_KEY_ID>.p8 in ./private_keys, ~/private_keys,
  # ~/.private_keys, ~/.appstoreconnect/private_keys, or $API_PRIVATE_KEYS_DIR.
  KEY_FOUND=""
  for d in "./private_keys" "$HOME/private_keys" "$HOME/.private_keys" \
           "$HOME/.appstoreconnect/private_keys" "${API_PRIVATE_KEYS_DIR:-}"; do
    [ -n "$d" ] || continue
    if [ -f "$d/AuthKey_$API_KEY_ID.p8" ]; then
      KEY_FOUND="$d/AuthKey_$API_KEY_ID.p8"
      break
    fi
  done
  [ -n "$KEY_FOUND" ] || {
    echo "Could not find AuthKey_$API_KEY_ID.p8. Put it in ~/.appstoreconnect/private_keys/." >&2
    echo "See docs/PUBLISHING.md Step 6 for how to create the App Store Connect API key." >&2
    exit 1
  }
  echo "Using API key $KEY_FOUND"

  # A valid .ipa is a zip with the app under Payload/. Zipping the .app directly
  # produces an archive App Store Connect rejects.
  STAGE=$(mktemp -d)
  mkdir -p "$STAGE/Payload"
  cp -R "$APP" "$STAGE/Payload/"
  IPA="$STAGE/MasjidApp.ipa"
  (cd "$STAGE" && ditto -c -k --sequesterRsrc --keepParent Payload "$IPA")

  # Preserve the exact package before validation/upload. The staging directory is deleted
  # below, and reconstructing an IPA later would not preserve the uploaded zip byte-for-byte.
  if [ -n "${PUBLISH_ARTIFACTS_DIR:-}" ]; then
    cp "$IPA" "$PUBLISH_ARTIFACTS_DIR/MasjidApp.ipa"
  fi

  echo "Validating..."
  xcrun altool --validate-app -f "$IPA" -t ios \
    --api-key "$API_KEY_ID" --api-issuer "$API_ISSUER_ID"

  echo "Uploading..."
  xcrun altool --upload-app -f "$IPA" -t ios \
    --api-key "$API_KEY_ID" --api-issuer "$API_ISSUER_ID"

  rm -rf "$STAGE"
  echo "Uploaded. Continue with docs/PUBLISHING.md Step 7 in App Store Connect."
else
  echo "API_KEY_ID/API_ISSUER_ID not set - upload manually:"
  echo "  Xcode -> Window -> Organizer -> select archive -> Distribute App -> App Store Connect -> Upload"
  echo "  Archive: $ARCHIVE"
fi
