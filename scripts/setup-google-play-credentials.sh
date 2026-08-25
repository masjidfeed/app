#!/usr/bin/env bash
# One-time setup for the Google Play publishing credentials used by
# scripts/publish-google-play.sh and .github/workflows/publish-google-play.yml.
#
# Creates and verifies the upload keystore and its password file, installs the Play
# service-account JSON, and optionally uploads the matching values to Azure Key Vault.
# Secrets are never echoed, and existing files are never overwritten without --force.
#
# Usage:
#   scripts/setup-google-play-credentials.sh [options]
#
#   --service-account <path>  Downloaded Play service-account JSON to install.
#   --maps-key <path>         File containing the production Android Maps API key.
#   --push-key-vault          Upload the configured values to Azure Key Vault.
#   --key-vault <name>        Key Vault for --push-key-vault (default: masjidfeed-publish).
#   --force                   Replace an existing keystore. See the warning below.
set -euo pipefail

CONFIG_DIR="${MASJIDAPP_CONFIG_DIR:-$HOME/.config/masjidapp}"
KEYSTORE="$CONFIG_DIR/masjidapp-upload.keystore"
PASSWORD_FILE="$CONFIG_DIR/android-signing-password"
PLAY_JSON="$CONFIG_DIR/google-play-service-account.json"
MAPS_KEY_FILE="$CONFIG_DIR/google-maps-api-key"
KEY_ALIAS="masjidapp-upload"
DNAME="CN=MasjidApp Upload, O=Bothell Mosque, L=Bothell, ST=Washington, C=US"

SERVICE_ACCOUNT_SOURCE=""
MAPS_KEY_SOURCE=""
PUSH_KEY_VAULT=false
KEY_VAULT="masjidfeed-publish"
FORCE=false

fail() {
  echo "$*" >&2
  exit 1
}

while [ $# -gt 0 ]; do
  case "$1" in
    --service-account) SERVICE_ACCOUNT_SOURCE="${2:-}"; shift 2 ;;
    --maps-key) MAPS_KEY_SOURCE="${2:-}"; shift 2 ;;
    --key-vault) KEY_VAULT="${2:-}"; shift 2 ;;
    --push-key-vault) PUSH_KEY_VAULT=true; shift ;;
    --force) FORCE=true; shift ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) fail "Unknown option: $1" ;;
  esac
done

for command in keytool openssl python3; do
  command -v "$command" >/dev/null 2>&1 || fail "Required command not found: $command"
done

mkdir -p "$CONFIG_DIR"
chmod 700 "$CONFIG_DIR"

echo "==> Upload keystore"
if [ -f "$KEYSTORE" ] && [ "$FORCE" = true ]; then
  # Google Play permanently binds an app to its upload certificate. Replacing a keystore
  # that Play has already accepted requires an upload-key reset through support.
  echo "WARNING: --force replaces $KEYSTORE."
  echo "Do this only if no bundle signed by it has been uploaded to Google Play."
  printf 'Type REPLACE to continue: '
  read -r confirmation
  [ "$confirmation" = "REPLACE" ] || fail "Aborted."
  backup="$KEYSTORE.$(date -u +%Y%m%dT%H%M%SZ).bak"
  mv "$KEYSTORE" "$backup"
  chmod 600 "$backup"
  mv "$PASSWORD_FILE" "$backup.password" 2>/dev/null || true
  echo "Previous keystore kept at $backup"
fi

if [ -f "$KEYSTORE" ]; then
  echo "Keystore already exists; leaving it unchanged."
  [ -f "$PASSWORD_FILE" ] ||
    fail "$PASSWORD_FILE is missing and cannot be recovered from the keystore.
Restore it from your password manager, or re-run with --force to create a new key."
else
  # Write the password to disk first and read it back with keytool's :file modifier.
  # Prompt- or clipboard-based flows silently desynchronize the keystore and this file.
  if [ -f "$PASSWORD_FILE" ]; then
    echo "Reusing the existing password file."
  else
    ( umask 077; openssl rand -base64 32 | tr -d '\n' > "$PASSWORD_FILE" )
    echo "Generated a new 32-byte random password."
  fi
  chmod 600 "$PASSWORD_FILE"

  keytool -genkeypair \
    -keystore "$KEYSTORE" \
    -alias "$KEY_ALIAS" \
    -keyalg RSA -keysize 2048 -validity 10000 \
    -dname "$DNAME" \
    -storepass:file "$PASSWORD_FILE" \
    -keypass:file "$PASSWORD_FILE"
  chmod 600 "$KEYSTORE"
  echo "Created $KEYSTORE"
fi

[ -s "$PASSWORD_FILE" ] || fail "Password file is empty: $PASSWORD_FILE"

certificate=$(keytool -list -v \
  -keystore "$KEYSTORE" \
  -alias "$KEY_ALIAS" \
  -storepass:file "$PASSWORD_FILE") ||
  fail "The password in $PASSWORD_FILE does not open $KEYSTORE."

echo "  OK   password file opens the keystore"
printf '%s\n' "$certificate" | grep -E '^[[:space:]]*(SHA1|SHA256):' | sed 's/^[[:space:]]*/  /'
echo "  Compare these with Play Console > Protected with Play > Play Store distribution > Go to Play app signing > Upload key certificate."

echo "==> Play service account"
if [ -n "$SERVICE_ACCOUNT_SOURCE" ]; then
  [ -f "$SERVICE_ACCOUNT_SOURCE" ] || fail "No such file: $SERVICE_ACCOUNT_SOURCE"
  if [ -f "$PLAY_JSON" ] && [ "$FORCE" != true ]; then
    fail "$PLAY_JSON already exists. Re-run with --force to replace it."
  fi
  install -m 600 "$SERVICE_ACCOUNT_SOURCE" "$PLAY_JSON"
  echo "Installed $PLAY_JSON"
fi

if [ -f "$PLAY_JSON" ]; then
  chmod 600 "$PLAY_JSON"
  python3 -c 'import json,sys; json.load(open(sys.argv[1]))' "$PLAY_JSON" ||
    fail "$PLAY_JSON is not valid JSON."
  account_email=$(python3 -c \
    'import json,sys; print(json.load(open(sys.argv[1])).get("client_email",""))' "$PLAY_JSON")
  [ -n "$account_email" ] || fail "$PLAY_JSON has no client_email; it is not a service-account key."
  echo "  OK   service-account key is valid"
  echo "  Invite this account in Play Console > Users and permissions:"
  echo "    $account_email"
else
  echo "Not configured. Download the JSON key, then re-run with:"
  echo "  --service-account ~/Downloads/<key>.json"
fi

echo "==> Maps API key"
if [ -n "$MAPS_KEY_SOURCE" ]; then
  [ -f "$MAPS_KEY_SOURCE" ] || fail "No such file: $MAPS_KEY_SOURCE"
  install -m 600 "$MAPS_KEY_SOURCE" "$MAPS_KEY_FILE"
  echo "Installed $MAPS_KEY_FILE"
fi

if [ -f "$MAPS_KEY_FILE" ]; then
  chmod 600 "$MAPS_KEY_FILE"
  [ -s "$MAPS_KEY_FILE" ] || fail "Maps key file is empty: $MAPS_KEY_FILE"
  echo "  OK   Maps key present"
  echo "  Export it before publishing:"
  echo "    export GOOGLE_MAPS_API_KEY=\"\$(cat $MAPS_KEY_FILE)\""
else
  echo "Not configured. Publish builds require it; save the restricted key with:"
  echo "  --maps-key <file containing the key>"
fi

echo "==> Azure Key Vault"
if [ "$PUSH_KEY_VAULT" != true ]; then
  echo "Skipped. Re-run with --push-key-vault to upload them to $KEY_VAULT."
  exit 0
fi

command -v az >/dev/null 2>&1 || fail "Azure CLI (az) is required for --push-key-vault."
az account show >/dev/null 2>&1 || fail "Run 'az login' first."

upload_base64() {
  local source="$1" secret_name="$2" encoded
  encoded=$(mktemp)
  trap 'rm -f "$encoded"' RETURN
  openssl base64 -A -in "$source" > "$encoded"
  az keyvault secret set --vault-name "$KEY_VAULT" --name "$secret_name" --file "$encoded" --output none
  rm -f "$encoded"
  trap - RETURN
}

upload_base64 "$KEYSTORE" android-upload-keystore-base64
az keyvault secret set --vault-name "$KEY_VAULT" --name android-upload-keystore-password --file "$PASSWORD_FILE" --output none
echo "  Uploaded Android upload-key credentials"

if [ -f "$PLAY_JSON" ]; then
  upload_base64 "$PLAY_JSON" google-play-service-account-json-base64
  echo "  Uploaded Google Play service-account key"
fi

if [ -f "$MAPS_KEY_FILE" ]; then
  az keyvault secret set --vault-name "$KEY_VAULT" --name google-maps-api-key --file "$MAPS_KEY_FILE" --output none
  echo "  Uploaded Google Maps key"
fi

echo
echo "Back up $KEYSTORE and $PASSWORD_FILE in a password manager."
echo "Key Vault is not a substitute for an offline upload-key backup; a lost upload key requires a Play reset."
