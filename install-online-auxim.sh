#!/usr/bin/env bash
set -euo pipefail

# =========================
# Config
# =========================
REPO="${AUXIM_REPO:-F4561F/auxim-agent}"
INSTALL_DIR="${AUXIM_INSTALL_DIR:-$HOME/.local/bin}"
BIN="$INSTALL_DIR/auxim"

mkdir -p "$INSTALL_DIR"

echo "==> Fetching latest release..."

# =========================
# Get latest version
# =========================
API_URL="https://api.github.com/repos/$REPO/releases/latest"

LATEST=$(curl -s "$API_URL" \
  | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p')

if [[ -z "$LATEST" ]]; then
  echo "ERROR: Failed to fetch latest release version"
  exit 1
fi

echo "==> Latest version: $LATEST"

# =========================
# Detect platform
# =========================
OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH="$(uname -m)"

case "$OS" in
  linux)
    case "$ARCH" in
      x86_64) RID="linux-x64" ;;
      aarch64|arm64) RID="linux-arm64" ;;
      *) echo "ERROR: Unsupported architecture: $ARCH"; exit 1 ;;
    esac
    ;;
  darwin)
    case "$ARCH" in
      x86_64) RID="osx-x64" ;;
      arm64) RID="osx-arm64" ;;
      *) echo "ERROR: Unsupported architecture: $ARCH"; exit 1 ;;
    esac
    ;;
  msys*|mingw*|cygwin*)
    RID="win-x64"
    BIN="$INSTALL_DIR/auxim.exe"
    ;;
  *)
    echo "ERROR: Unsupported OS: $OS"
    exit 1
    ;;
esac

ASSET="auxim-agent-${LATEST}-${RID}.tar.gz"
URL="https://github.com/$REPO/releases/download/$LATEST/$ASSET"

echo "==> Downloading: $ASSET"

TMP_FILE=$(mktemp)
TMP_DIR=$(mktemp -d)

trap 'rm -rf "$TMP_FILE" "$TMP_DIR"' EXIT

curl -L "$URL" -o "$TMP_FILE"

# =========================
# Validate download
# =========================
file "$TMP_FILE" | grep -q "gzip" || {
  echo "ERROR: Downloaded file is not a valid archive"
  exit 1
}

echo "==> Extracting..."
tar -xzf "$TMP_FILE" -C "$TMP_DIR"

# =========================
# Validate structure
# =========================
EXTRACTED_BIN="$TMP_DIR/auxim/auxim"
if [[ "$RID" == win-* ]]; then
  EXTRACTED_BIN="$TMP_DIR/auxim/auxim.exe"
fi

if [[ ! -f "$EXTRACTED_BIN" ]]; then
  echo "ERROR: Invalid package structure"
  echo "Expected: auxim/auxim or auxim/auxim.exe inside archive"
  exit 1
fi

chmod +x "$EXTRACTED_BIN"

mv "$EXTRACTED_BIN" "$BIN"

echo "==> Installed: auxim -> $BIN"

# =========================
# PATH check
# =========================
case ":$PATH:" in
  *":$INSTALL_DIR:"*)
    echo "OK: PATH already configured"
    ;;
  *)
    echo ""
    echo "⚠️  $INSTALL_DIR is not in PATH"
    echo "Add this to your shell profile:"
    echo ""
    echo "export PATH=\"$INSTALL_DIR:\$PATH\""
    echo ""
    echo "Then run:"
    echo "source ~/.zshrc (or your shell config)"
    ;;
esac

echo ""
echo "Done. Run: auxim --version"
