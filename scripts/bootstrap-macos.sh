#!/usr/bin/env bash
#
# bootstrap-macos.sh - set up the Masjid App developer environment on a clean macOS machine.
#
# Installs and configures everything needed to build, test, and run the .NET MAUI app
# (iOS + Android) and the local Docker backend. Safe to re-run - every step is idempotent
# and skips work that is already done.
#
# Installs:
#   • Homebrew (if missing)
#   • .NET SDK pinned by global.json + the MAUI workload
#   • Microsoft OpenJDK 21 (required by the .NET MAUI Android toolchain)
#   • Android SDK command-line tools, platform-tools, platform/build-tools, emulator,
#     a system image, and the "masjid_test" AVD - all under ~/Library/Android/sdk
#   • Docker Desktop (local WordPress + mock backend)
#   • The VS Code .NET MAUI extension (if the `code` CLI is available)
#
# What it can NOT do automatically (prints guidance instead):
#   • Install full Xcode - get it from the App Store, then re-run this script.
#
# Usage:
#   ./scripts/bootstrap-macos.sh
#
# You may be prompted for your macOS password (sudo) when installing the JDK and Docker.
set -euo pipefail

# ── Configuration ────────────────────────────────────────────────────────────
ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-$HOME/Library/Android/sdk}"
ANDROID_API_LEVEL=36
ANDROID_BUILD_TOOLS="36.0.0"
ANDROID_CMDLINE_TOOLS="19.0"
AVD_NAME="masjid_test"
AVD_DEVICE="pixel_7"
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# CPU architecture → Android system-image ABI.
if [[ "$(uname -m)" == "arm64" ]]; then
  ANDROID_ABI="arm64-v8a"
else
  ANDROID_ABI="x86_64"
fi
ANDROID_SYSTEM_IMAGE="system-images;android-${ANDROID_API_LEVEL};google_apis;${ANDROID_ABI}"

# ── Logging helpers ──────────────────────────────────────────────────────────
if [[ -t 1 ]]; then
  BLUE=$'\033[0;34m'; GREEN=$'\033[0;32m'; YELLOW=$'\033[0;33m'; RED=$'\033[0;31m'; BOLD=$'\033[1m'; RESET=$'\033[0m'
else
  BLUE=""; GREEN=""; YELLOW=""; RED=""; BOLD=""; RESET=""
fi
step() { printf '\n%s==>%s %s%s%s\n' "$BLUE" "$RESET" "$BOLD" "$1" "$RESET"; }
ok()   { printf '%s  ✓%s %s\n' "$GREEN" "$RESET" "$1"; }
warn() { printf '%s  !%s %s\n' "$YELLOW" "$RESET" "$1"; }
err()  { printf '%s  ✗%s %s\n' "$RED" "$RESET" "$1" >&2; }

# ── Preconditions ────────────────────────────────────────────────────────────
if [[ "$(uname -s)" != "Darwin" ]]; then
  err "This script only supports macOS."
  exit 1
fi

# ── 1. Homebrew ──────────────────────────────────────────────────────────────
install_homebrew() {
  step "Homebrew"
  if command -v brew >/dev/null 2>&1; then
    ok "Homebrew already installed ($(brew --version | head -1))"
  else
    warn "Installing Homebrew (you may be prompted for your password)…"
    NONINTERACTIVE=1 /bin/bash -c \
      "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
  fi
  # Make brew available in this shell regardless of how it was installed.
  if [[ -x /opt/homebrew/bin/brew ]]; then
    eval "$(/opt/homebrew/bin/brew shellenv)"
  elif [[ -x /usr/local/bin/brew ]]; then
    eval "$(/usr/local/bin/brew shellenv)"
  fi
}

brew_cask_installed() { brew list --cask "$1" >/dev/null 2>&1; }

# ── 2. .NET SDK + MAUI workload ──────────────────────────────────────────────
install_dotnet() {
  step ".NET SDK (pinned by global.json)"
  local required_band
  required_band="$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]+"' "$REPO_ROOT/global.json" | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')"

  if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q "^${required_band%.*}\."; then
    ok "A matching .NET SDK is already on PATH ($(dotnet --version))"
  else
    warn "Installing .NET SDK ${required_band} to ${DOTNET_ROOT} via dotnet-install.sh…"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --jsonfile "$REPO_ROOT/global.json" --install-dir "$DOTNET_ROOT"
    export DOTNET_ROOT
    export PATH="$DOTNET_ROOT:$PATH"
    add_to_zshrc 'export DOTNET_ROOT="$HOME/.dotnet"'
    add_to_zshrc 'export PATH="$DOTNET_ROOT:$PATH"'
  fi

  step ".NET MAUI workload"
  local dotnet_bin
  dotnet_bin="$(command -v dotnet)"
  if "$dotnet_bin" workload list 2>/dev/null | grep -qiE '^maui|maui-ios|maui-android'; then
    ok "MAUI workload already installed"
  else
    warn "Installing the MAUI workload (this can take a few minutes)…"
    "$dotnet_bin" workload install maui
  fi
}

# ── 3. Microsoft OpenJDK 21 ──────────────────────────────────────────────────
install_jdk() {
  step "Microsoft OpenJDK 21 (required by the MAUI Android toolchain)"
  if [[ -d /Library/Java/JavaVirtualMachines/microsoft-21.jdk ]]; then
    ok "JDK 21 already installed"
  else
    warn "Installing microsoft-openjdk@21 (sudo password required for the pkg installer)…"
    brew install --cask microsoft-openjdk@21
  fi
}

# ── 4. Android SDK + AVD ─────────────────────────────────────────────────────
install_android() {
  step "Android SDK command-line tools"
  if ! command -v sdkmanager >/dev/null 2>&1 && ! brew_cask_installed android-commandlinetools; then
    warn "Installing android-commandlinetools…"
    brew install --cask android-commandlinetools
  else
    ok "Android command-line tools present"
  fi

  # Locate sdkmanager / avdmanager (brew puts them on PATH; fall back to the cask dir).
  local sdkmanager avdmanager
  sdkmanager="$(command -v sdkmanager || echo /opt/homebrew/share/android-commandlinetools/cmdline-tools/latest/bin/sdkmanager)"
  avdmanager="$(command -v avdmanager || echo /opt/homebrew/share/android-commandlinetools/cmdline-tools/latest/bin/avdmanager)"
  if [[ ! -x "$sdkmanager" ]]; then
    err "sdkmanager not found after installing android-commandlinetools."
    exit 1
  fi

  export ANDROID_HOME="$ANDROID_SDK_ROOT"
  export ANDROID_SDK_ROOT
  export JAVA_HOME="/Library/Java/JavaVirtualMachines/microsoft-21.jdk/Contents/Home"
  mkdir -p "$ANDROID_SDK_ROOT"

  step "Android SDK components (into $ANDROID_SDK_ROOT)"
  warn "Accepting Android SDK licenses…"
  yes | "$sdkmanager" --sdk_root="$ANDROID_SDK_ROOT" --licenses >/dev/null || true

  warn "Installing platform-tools, platform android-${ANDROID_API_LEVEL}, build-tools, emulator, system image…"
  "$sdkmanager" --sdk_root="$ANDROID_SDK_ROOT" \
    "platform-tools" \
    "platforms;android-${ANDROID_API_LEVEL}" \
    "build-tools;${ANDROID_BUILD_TOOLS}" \
    "cmdline-tools;${ANDROID_CMDLINE_TOOLS}" \
    "emulator" \
    "$ANDROID_SYSTEM_IMAGE"
  ok "Android SDK components installed"

  step "Android Virtual Device ($AVD_NAME)"
  if "$avdmanager" list avd 2>/dev/null | grep -qE "Name:[[:space:]]*${AVD_NAME}$"; then
    ok "AVD '$AVD_NAME' already exists"
  else
    warn "Creating AVD '$AVD_NAME' from $ANDROID_SYSTEM_IMAGE…"
    echo "no" | "$avdmanager" create avd \
      --name "$AVD_NAME" \
      --package "$ANDROID_SYSTEM_IMAGE" \
      --device "$AVD_DEVICE" --force
    ok "AVD '$AVD_NAME' created"
  fi

  add_to_zshrc 'export ANDROID_HOME="$HOME/Library/Android/sdk"'
  add_to_zshrc 'export ANDROID_SDK_ROOT="$ANDROID_HOME"'
}

# ── 5. Docker Desktop ────────────────────────────────────────────────────────
install_docker() {
  step "Docker Desktop (local backend)"
  if command -v docker >/dev/null 2>&1; then
    ok "Docker already installed ($(docker --version))"
  elif brew_cask_installed docker-desktop; then
    ok "Docker Desktop already installed (launch it once to start the engine)"
  else
    warn "Installing Docker Desktop (sudo password may be required)…"
    brew install --cask docker-desktop
    warn "Launch Docker Desktop once so the engine starts before 'docker compose up'."
  fi
}

# ── 6. Xcode (checks only) ───────────────────────────────────────────────────
check_xcode() {
  step "Xcode (iOS builds)"
  if ! xcode-select -p >/dev/null 2>&1; then
    warn "Xcode command-line tools not found - launching the installer…"
    xcode-select --install || true
  fi
  if [[ -d /Applications/Xcode.app ]]; then
    ok "Xcode found at /Applications/Xcode.app"
    sudo xcodebuild -license accept >/dev/null 2>&1 || true
    if ! xcrun simctl list runtimes 2>/dev/null | grep -qi 'iOS'; then
      warn "No iOS simulator runtime detected. Run: xcodebuild -downloadPlatform iOS"
    fi
  else
    warn "Full Xcode is not installed. Install it from the App Store, then run:"
    warn "  sudo xcodebuild -license accept && xcodebuild -downloadPlatform iOS"
    warn "iOS builds/simulator will be unavailable until Xcode is installed."
  fi
}

# ── 7. VS Code MAUI extension ────────────────────────────────────────────────
install_vscode_extension() {
  step "VS Code .NET MAUI extension"
  if command -v code >/dev/null 2>&1; then
    if code --list-extensions 2>/dev/null | grep -qi '^ms-dotnettools.dotnet-maui$'; then
      ok "MAUI extension already installed"
    else
      warn "Installing ms-dotnettools.dotnet-maui…"
      code --install-extension ms-dotnettools.dotnet-maui || warn "Could not install the extension automatically."
    fi
  else
    warn "VS Code 'code' CLI not on PATH - skipping. Install the '.NET MAUI' extension manually."
  fi
}

# ── Shell-profile helper ─────────────────────────────────────────────────────
add_to_zshrc() {
  local line="$1"
  local profile="$HOME/.zshrc"
  touch "$profile"
  if ! grep -qF "$line" "$profile"; then
    printf '\n%s\n' "$line" >>"$profile"
    ok "Added to ~/.zshrc: $line"
  fi
}

# ── Summary ──────────────────────────────────────────────────────────────────
print_summary() {
  step "Done"
  cat <<EOF
Environment bootstrap complete.

Next steps:
  1. Open a NEW terminal (or 'source ~/.zshrc') so PATH/JAVA_HOME/ANDROID_HOME are loaded.
  2. Start the local backend:        cd devbox && docker compose up -d
  3. Run the Core tests:             dotnet test tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj -c Release
  4. Build/run on Android:           dotnet build -t:Run -f net10.0-android src/MasjidApp/MasjidApp.csproj
  5. Build/run on iOS (Xcode req'd): dotnet build -t:Run -f net10.0-ios src/MasjidApp/MasjidApp.csproj

Notes:
  • The VS Code .NET MAUI extension reads SDK/JDK paths from .vscode/settings.json
    (androidSdkPreferredPath + javaSdkPreferredPath → JDK 21). Reload the window after this run.
  • The iOS simulator UDID in .vscode/tasks.json is machine-specific; update it to one from
    'xcrun simctl list devices' if the boot task fails.
EOF
}

main() {
  install_homebrew
  install_dotnet
  install_jdk
  install_android
  install_docker
  check_xcode
  install_vscode_extension
  print_summary
}

main "$@"
