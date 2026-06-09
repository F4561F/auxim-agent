#!/usr/bin/env bash
set -euo pipefail

AUXIM_HOME_DIR="${AUXIM_HOME:-$HOME/.auxim}"
TARGET_DIR="${AUXIM_INSTALL_DIR:-$HOME/.local/bin}"
TARGET="$TARGET_DIR/auxim"

assume_yes=false
remove_path_entry=false

for arg in "$@"; do
  case "$arg" in
    -y|--yes)
      assume_yes=true
      ;;
    --remove-path-entry)
      remove_path_entry=true
      ;;
    -h|--help)
      cat <<EOF
Usage: ./remove-auxim.sh [--yes] [--remove-path-entry]

Removes Auxim local state and the user-level auxim command.

Options:
  -y, --yes            Do not ask for confirmation.
  --remove-path-entry  Also remove the PATH line added by install-auxim.sh.
  -h, --help           Show this help.

Environment:
  AUXIM_HOME         Config/state directory. Defaults to ~/.auxim.
  AUXIM_INSTALL_DIR  Install directory. Defaults to ~/.local/bin.
EOF
      exit 0
      ;;
    *)
      echo "Unknown option: $arg" >&2
      exit 1
      ;;
  esac
done

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

confirm() {
  if [[ "$assume_yes" == true ]]; then
    return 0
  fi

  echo "This will remove Auxim local state:"
  echo "  $AUXIM_HOME_DIR"
  echo
  echo "It will also remove the installed command if present:"
  echo "  $TARGET"
  echo
  read -r -p "Continue? [y/N] " answer
  case "$answer" in
    y|Y|yes|YES) return 0 ;;
    *) echo "Cancelled."; exit 0 ;;
  esac
}

remove_path_line() {
  local profile
  profile="$(shell_profile)"
  if [[ ! -f "$profile" ]]; then
    return
  fi

  local temp
  temp="$(mktemp)"
  grep -Fv "export PATH=\"$TARGET_DIR:\$PATH\"" "$profile" \
    | grep -Fv "fish_add_path \"$TARGET_DIR\"" > "$temp"
  mv "$temp" "$profile"
  echo "Removed Auxim PATH entry from $profile"
}

confirm

if [[ -e "$AUXIM_HOME_DIR" ]]; then
  rm -rf "$AUXIM_HOME_DIR"
  echo "Removed $AUXIM_HOME_DIR"
else
  echo "No Auxim state directory found at $AUXIM_HOME_DIR"
fi

if [[ -L "$TARGET" || -f "$TARGET" ]]; then
  rm -f "$TARGET"
  echo "Removed $TARGET"
else
  echo "No installed auxim command found at $TARGET"
fi

if [[ "$remove_path_entry" == true ]]; then
  remove_path_line
fi

echo "Auxim removal complete."
