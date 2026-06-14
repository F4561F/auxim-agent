#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET_DIR="${AUXIM_INSTALL_DIR:-$HOME/.local/bin}"
TARGET="$TARGET_DIR/auxim"

path_contains() {
  case ":$PATH:" in
    *":$1:"*) return 0 ;;
    *) return 1 ;;
  esac
}

shell_profile() {
  local shell_name
  shell_name="$(basename "${SHELL:-}")"

  case "$shell_name" in
    zsh)
      echo "$HOME/.zshrc"
      ;;
    bash)
      if [[ "$(uname -s)" == "Darwin" ]]; then
        echo "$HOME/.bash_profile"
      else
        echo "$HOME/.bashrc"
      fi
      ;;
    fish)
      echo "$HOME/.config/fish/config.fish"
      ;;
    *)
      echo "$HOME/.profile"
      ;;
  esac
}

ensure_path_configured() {
  if path_contains "$TARGET_DIR"; then
    echo "$TARGET_DIR is already on PATH."
    return
  fi

  local profile
  profile="$(shell_profile)"
  mkdir -p "$(dirname "$profile")"
  touch "$profile"

  if grep -Fq "$TARGET_DIR" "$profile"; then
    echo "$TARGET_DIR is referenced in $profile, but it is not active in this shell."
    echo "Reload your shell or run: source $profile"
    return
  fi

  if [[ "$(basename "${SHELL:-}")" == "fish" ]]; then
    printf '\n# Auxim\nfish_add_path "%s"\n' "$TARGET_DIR" >> "$profile"
  else
    printf '\n# Auxim\nexport PATH="%s:$PATH"\n' "$TARGET_DIR" >> "$profile"
  fi

  echo "Added $TARGET_DIR to PATH in $profile"
  echo "Reload your shell or run: source $profile"
}

dotnet publish "$ROOT_DIR/src/Auxim.Cli/Auxim.Cli.csproj" \
  -c Release \
  -o "$ROOT_DIR/dist/auxim" \
  --self-contained false \
  -p:UseAppHost=true

mkdir -p "$TARGET_DIR"
ln -sf "$ROOT_DIR/dist/auxim/auxim" "$TARGET"

echo "Installed auxim -> $TARGET"
ensure_path_configured
