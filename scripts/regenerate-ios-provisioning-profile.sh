#!/usr/bin/env bash
# Creates a fresh App Store provisioning profile from the capabilities currently enabled
# for the MasjidApp bundle ID, verifies its production push entitlements, installs it
# locally, and updates the GitHub Actions profile secret without printing secret content.
#
# App Attest Opt-In must already be enabled for the bundle ID in Apple Developer. Apple's
# public API does not expose that opt-in, so this script proves it reached the generated
# profile before changing the GitHub secret.
#
# Usage:
#   scripts/regenerate-ios-provisioning-profile.sh [options]
#
#   --repo <owner/name>       GitHub repository (default: masjidfeed/app).
#   --bundle-id <identifier>  Apple bundle ID (default: com.goodsoftware.masjidapp).
#   --profile-name <name>     Profile name prefix shown in Apple Developer.
#   --certificate-id <id>     Use one App Store Connect certificate instead of all active
#                             iOS distribution certificates.
#   --notification-service    Generate the Notification Service Extension profile instead.
#   --output <path>           Keep the downloaded profile at this path.
#   --no-install              Do not install the profile in Xcode's profile directories.
#   --no-push-secret          Do not replace IOS_PROVISIONING_PROFILE_BASE64 in GitHub.
set -euo pipefail

API_BASE="https://api.appstoreconnect.apple.com/v1"
REPO="masjidfeed/app"
BUNDLE_ID="com.goodsoftware.masjidapp"
PROFILE_NAME_PREFIX="MasjidApp App Store"
PROFILE_SECRET="IOS_PROVISIONING_PROFILE_BASE64"
PROFILE_KIND="app"
CERTIFICATE_ID=""
OUTPUT="${MASJIDAPP_CONFIG_DIR:-$HOME/.config/masjidapp}/MasjidApp-AppStore.mobileprovision"
INSTALL_PROFILE=true
PUSH_SECRET=true

fail() {
  echo "$*" >&2
  exit 1
}

usage() {
  sed -n '2,22p' "$0"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --repo) REPO="${2:-}"; shift 2 ;;
    --bundle-id) BUNDLE_ID="${2:-}"; shift 2 ;;
    --profile-name) PROFILE_NAME_PREFIX="${2:-}"; shift 2 ;;
    --certificate-id) CERTIFICATE_ID="${2:-}"; shift 2 ;;
    --notification-service)
      PROFILE_KIND="notification-service"
      BUNDLE_ID="com.goodsoftware.masjidapp.NotificationServiceExtension"
      PROFILE_NAME_PREFIX="MasjidApp Notification Service App Store"
      PROFILE_SECRET="IOS_NOTIFICATION_SERVICE_PROFILE_BASE64"
      OUTPUT="${MASJIDAPP_CONFIG_DIR:-$HOME/.config/masjidapp}/MasjidApp-NotificationService-AppStore.mobileprovision"
      shift ;;
    --output) OUTPUT="${2:-}"; shift 2 ;;
    --no-install) INSTALL_PROFILE=false; shift ;;
    --no-push-secret) PUSH_SECRET=false; shift ;;
    -h|--help) usage; exit 0 ;;
    *) fail "Unknown option: $1" ;;
  esac
done

for command in curl jq openssl plutil ruby; do
  command -v "$command" >/dev/null 2>&1 || fail "Required command not found: $command"
done

API_KEY_ID="${API_KEY_ID:-}"
API_ISSUER_ID="${API_ISSUER_ID:-}"
API_PRIVATE_KEY="${API_PRIVATE_KEY:-}"

if [ -z "$API_PRIVATE_KEY" ]; then
  key_candidates=""
  for directory in \
    "$HOME/.appstoreconnect/private_keys" \
    "$HOME/private_keys" \
    "$HOME/.private_keys"; do
    [ -d "$directory" ] || continue
    for candidate in "$directory"/AuthKey_*.p8; do
      [ -e "$candidate" ] || continue
      key_candidates="$key_candidates$candidate
"
    done
  done
  key_candidates=$(printf '%s' "$key_candidates" | grep . | sort -u || true)
  key_count=$(printf '%s' "$key_candidates" | grep -c . || true)
  [ "$key_count" -eq 1 ] || fail \
    "Expected exactly one AuthKey_*.p8; found $key_count. Set API_PRIVATE_KEY and API_KEY_ID."
  API_PRIVATE_KEY="$key_candidates"
fi

[ -f "$API_PRIVATE_KEY" ] || fail "App Store Connect private key not found: $API_PRIVATE_KEY"

if [ -z "$API_KEY_ID" ]; then
  key_filename=${API_PRIVATE_KEY##*/AuthKey_}
  API_KEY_ID=${key_filename%.p8}
fi

if [ -z "$API_ISSUER_ID" ] && [ -f "$HOME/.appstoreconnect/issuer_id" ]; then
  API_ISSUER_ID=$(tr -d '[:space:]' < "$HOME/.appstoreconnect/issuer_id")
fi
[ -n "$API_ISSUER_ID" ] || fail \
  "Set API_ISSUER_ID or save it in ~/.appstoreconnect/issuer_id."

# App Store Connect uses ES256 JWT signatures as a raw 32-byte R value followed by a
# raw 32-byte S value. Ruby's OpenSSL returns ASN.1 DER, so convert it explicitly.
TOKEN=$(ruby --disable-gems -ropenssl -rjson -rbase64 -e '
  def encode(value)
    Base64.urlsafe_encode64(value, padding: false)
  end

  key_id, issuer_id, key_path = ARGV
  now = Time.now.to_i
  header = encode(JSON.generate({ alg: "ES256", kid: key_id, typ: "JWT" }))
  payload = encode(JSON.generate({
    iss: issuer_id,
    iat: now - 30,
    exp: now + 1_200,
    aud: "appstoreconnect-v1",
  }))
  signing_input = "#{header}.#{payload}"
  key = OpenSSL::PKey.read(File.binread(key_path))
  der = key.sign(OpenSSL::Digest::SHA256.new, signing_input)
  integers = OpenSSL::ASN1.decode(der).value
  raw_signature = integers.map do |integer|
    hex = integer.value.to_i.to_s(16)
    raise "Invalid ES256 signature component" if hex.length > 64
    [hex.rjust(64, "0")].pack("H*")
  end.join
  puts "#{signing_input}.#{encode(raw_signature)}"
' "$API_KEY_ID" "$API_ISSUER_ID" "$API_PRIVATE_KEY")

WORK_DIR=$(mktemp -d)
trap 'rm -rf "$WORK_DIR"' EXIT

api_get() {
  local path="$1"
  shift
  curl --fail-with-body --silent --show-error --get \
    --header "Authorization: Bearer $TOKEN" \
    --header "Accept: application/json" \
    "$API_BASE/$path" "$@"
}

api_post() {
  local path="$1" body="$2"
  curl --fail-with-body --silent --show-error \
    --request POST \
    --header "Authorization: Bearer $TOKEN" \
    --header "Content-Type: application/json" \
    --data-binary "@$body" \
    "$API_BASE/$path"
}

echo "==> Resolve bundle ID"
api_get bundleIds \
  --data-urlencode "filter[identifier]=$BUNDLE_ID" \
  --data-urlencode "filter[platform]=IOS" \
  --data-urlencode "limit=2" > "$WORK_DIR/bundle-ids.json"
jq --arg identifier "$BUNDLE_ID" \
  '.data = [.data[] | select(.attributes.identifier == $identifier)]' \
  "$WORK_DIR/bundle-ids.json" > "$WORK_DIR/exact-bundle-id.json"
mv "$WORK_DIR/exact-bundle-id.json" "$WORK_DIR/bundle-ids.json"

bundle_count=$(jq '.data | length' "$WORK_DIR/bundle-ids.json")
if [ "$bundle_count" -eq 0 ] && [ "$PROFILE_KIND" = "notification-service" ]; then
  jq -n \
    --arg identifier "$BUNDLE_ID" \
    '{
      data: {
        type: "bundleIds",
        attributes: {
          identifier: $identifier,
          name: "MasjidApp Notification Service Extension",
          platform: "IOS"
        }
      }
    }' > "$WORK_DIR/create-bundle-id.json"
  api_post bundleIds "$WORK_DIR/create-bundle-id.json" > "$WORK_DIR/created-bundle-id.json"
  jq '{data: [.data]}' "$WORK_DIR/created-bundle-id.json" > "$WORK_DIR/bundle-ids.json"
  bundle_count=1
  echo "  Created $BUNDLE_ID"
fi
if [ "$bundle_count" -ne 1 ]; then
  echo "Apple returned these iOS bundle resources:" >&2
  jq -r '.data[] |
    "  id=\(.id) name=\(.attributes.name) platform=\(.attributes.platform) seedId=\(.attributes.seedId)"' \
    "$WORK_DIR/bundle-ids.json" >&2
  fail "Expected one Apple bundle ID for $BUNDLE_ID; found $bundle_count."
fi
BUNDLE_RESOURCE_ID=$(jq -r '.data[0].id' "$WORK_DIR/bundle-ids.json")
echo "  OK   $BUNDLE_ID ($BUNDLE_RESOURCE_ID)"

echo "==> Resolve distribution certificates"
if [ -n "$CERTIFICATE_ID" ]; then
  api_get "certificates/$CERTIFICATE_ID" > "$WORK_DIR/certificates.json"
  CERTIFICATES=$(jq -c '
    [.data
      | select(.attributes.expirationDate > (now | todateiso8601))
      | select(.attributes.certificateType == "DISTRIBUTION" or
               .attributes.certificateType == "IOS_DISTRIBUTION")
      | {type: "certificates", id: .id}]
  ' "$WORK_DIR/certificates.json")
else
  api_get certificates \
    --data-urlencode "filter[certificateType]=DISTRIBUTION,IOS_DISTRIBUTION" \
    --data-urlencode "limit=200" > "$WORK_DIR/certificates.json"
  CERTIFICATES=$(jq -c '
    [.data[]
      | select(.attributes.expirationDate > (now | todateiso8601))
      | {type: "certificates", id: .id}]
  ' "$WORK_DIR/certificates.json")
fi

certificate_count=$(printf '%s' "$CERTIFICATES" | jq 'length')
if [ "$certificate_count" -eq 0 ]; then
  echo "Apple returned these matching certificates:" >&2
  jq -r '
    if .data | type == "array" then .data[] else .data end |
    "  id=\(.id) type=\(.attributes.certificateType) active=\(.attributes.activated) expires=\(.attributes.expirationDate)"
  ' "$WORK_DIR/certificates.json" >&2
  fail "No active, unexpired iOS distribution certificate is available."
fi
echo "  OK   using $certificate_count active distribution certificate(s)"

PROFILE_NAME="$PROFILE_NAME_PREFIX $(date -u +%Y%m%dT%H%M%SZ)"
jq -n \
  --arg name "$PROFILE_NAME" \
  --arg bundleId "$BUNDLE_RESOURCE_ID" \
  --argjson certificates "$CERTIFICATES" \
  '{
    data: {
      type: "profiles",
      attributes: {
        name: $name,
        profileType: "IOS_APP_STORE"
      },
      relationships: {
        bundleId: {data: {type: "bundleIds", id: $bundleId}},
        certificates: {data: $certificates}
      }
    }
  }' > "$WORK_DIR/create-profile.json"

echo "==> Create App Store profile"
api_post profiles "$WORK_DIR/create-profile.json" > "$WORK_DIR/profile-response.json"
jq -er '.data.attributes.profileContent' "$WORK_DIR/profile-response.json" |
  openssl base64 -d -A > "$WORK_DIR/profile.mobileprovision"
[ -s "$WORK_DIR/profile.mobileprovision" ] || fail "Apple returned an empty profile."

security cms -D -i "$WORK_DIR/profile.mobileprovision" > "$WORK_DIR/profile.plist"
plutil -extract Entitlements xml1 -o "$WORK_DIR/entitlements.plist" "$WORK_DIR/profile.plist"
plutil -convert json -o "$WORK_DIR/entitlements.json" "$WORK_DIR/entitlements.plist"

echo "==> Verify generated profile"
PROFILE_UUID=$(plutil -extract UUID raw "$WORK_DIR/profile.plist")
PROFILE_APP_ID=$(jq -er '.["application-identifier"]' "$WORK_DIR/entitlements.json")
APS_ENVIRONMENT=$(jq -r '.["aps-environment"] // empty' "$WORK_DIR/entitlements.json")

[[ "$PROFILE_APP_ID" == *".$BUNDLE_ID" ]] || fail \
  "Generated profile targets $PROFILE_APP_ID instead of $BUNDLE_ID."
if [ "$PROFILE_KIND" = "app" ]; then
  [ "$APS_ENVIRONMENT" = "production" ] || fail \
    "Generated profile has aps-environment=${APS_ENVIRONMENT:-missing}; expected production."
  jq -e '
    .["com.apple.developer.devicecheck.appattest-environment"] as $environment |
    $environment == "production" or
      (($environment | type) == "array" and ($environment | index("production")) != null)
  ' "$WORK_DIR/entitlements.json" >/dev/null || fail \
    "Generated profile does not allow the production App Attest environment."
  jq -e --arg group "group.com.goodsoftware.masjidapp" \
    '.["com.apple.security.application-groups"] | index($group) != null' \
    "$WORK_DIR/entitlements.json" >/dev/null || fail \
    "Generated profile is missing group.com.goodsoftware.masjidapp."
fi
if plutil -extract ProvisionedDevices xml1 -o - "$WORK_DIR/profile.plist" >/dev/null 2>&1; then
  fail "Generated profile unexpectedly contains development/ad-hoc devices."
fi
if [ "$PROFILE_KIND" = "app" ]; then
  echo "  OK   aps-environment=production"
  echo "  OK   App Attest production environment allowed"
  echo "  OK   App Group present"
fi
echo "  OK   application-identifier targets $BUNDLE_ID"

mkdir -p "$(dirname "$OUTPUT")"
install -m 600 "$WORK_DIR/profile.mobileprovision" "$OUTPUT"
echo "  Saved $OUTPUT"

if [ "$INSTALL_PROFILE" = true ]; then
  xcode_profiles="$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles"
  legacy_profiles="$HOME/Library/MobileDevice/Provisioning Profiles"
  mkdir -p "$xcode_profiles" "$legacy_profiles"
  install -m 600 "$OUTPUT" "$xcode_profiles/$PROFILE_UUID.mobileprovision"
  install -m 600 "$OUTPUT" "$legacy_profiles/$PROFILE_UUID.mobileprovision"
  echo "  Installed $PROFILE_UUID for Xcode and .NET iOS"
fi

if [ "$PUSH_SECRET" = true ]; then
  command -v gh >/dev/null 2>&1 || fail "GitHub CLI (gh) is required to update the secret."
  gh auth status >/dev/null 2>&1 || fail "Run 'gh auth login' first."
  openssl base64 -A -in "$OUTPUT" |
    gh secret set "$PROFILE_SECRET" --repo "$REPO"
  echo "  Updated $PROFILE_SECRET in $REPO"
fi

echo "Done. The next App Store publish will use profile '$PROFILE_NAME'."