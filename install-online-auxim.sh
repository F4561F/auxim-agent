#!/usr/bin/env bash
set -euo pipefail

# =========================
# Config
# =========================
REPO="${AUXIM_REPO:-F4561F/auxim}"
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
  echo "❌ Failed to fetch latest release version"
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
      *) echo "❌ Unsupported architecture: $ARCH"; exit 1 ;;
    esac
    ;;
  darwin)
    case "$ARCH" in
      x86_64) RID="osx-x64" ;;
      arm64) RID="osx-arm64" ;;
      *) echo "❌ Unsupported architecture: $ARCH"; exit 1 ;;
    esac
    ;;
  msys*|mingw*|cygwin*)
    RID="win-x64"
    ;;
  *)
    echo "❌ Unsupported OS: $OS"
    exit 1
    ;;
esac

# ⚠️ 这里匹配你的 repo release 名
ASSET="auxim-${RID}.tar.gz"
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
  echo "❌ Downloaded file is not a valid archive"
  exit 1
}

echo "==> Extracting..."
tar -xzf "$TMP_FILE" -C "$TMP_DIR"

# =========================
# Validate structure
# =========================
if [[ ! -f "$TMP_DIR/auxim/auxim" ]]; then
  echo "❌ Invalid package structure"
  echo "Expected: auxim/auxim inside archive"
  exit 1
fi

chmod +x "$TMP_DIR/auxim/auxim"

mv "$TMP_DIR/auxim/auxim" "$BIN"

echo "==> Installed: auxim → $BIN"

# =========================
# PATH check
# =========================
case ":$PATH:" in
  *":$INSTALL_DIR:"*)
    echo "✔ PATH already configured"
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
echo "🚀 Done. Run: auxim --version"
