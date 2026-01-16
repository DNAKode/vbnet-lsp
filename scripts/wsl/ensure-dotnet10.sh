#!/usr/bin/env bash
set -euo pipefail

DEFAULT_USER="${SUDO_USER:-$USER}"
DOTNET_ROOT_DEFAULT="/home/$DEFAULT_USER/.dotnet"
PROFILED_PATH="/etc/profile.d/dotnet-user.sh"
MARKER_START="# vbnet-lsp: dotnet user install"
MARKER_END="# /vbnet-lsp: dotnet user install"

if [ ! -x "$DOTNET_ROOT_DEFAULT/dotnet" ]; then
  echo "dotnet not found at $DOTNET_ROOT_DEFAULT/dotnet"
  echo "Install .NET 10 for this user first, or adjust DOTNET_ROOT_DEFAULT."
  exit 1
fi

write_profiled() {
  cat > "$PROFILED_PATH" <<'EOF'
# vbnet-lsp: dotnet user install
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
# /vbnet-lsp: dotnet user install
EOF
  chmod 644 "$PROFILED_PATH"
  echo "Wrote $PROFILED_PATH"
}

append_user_profile() {
  local target="$1"
  if [ -f "$target" ] && grep -q "$MARKER_START" "$target"; then
    echo "Marker already present in $target"
    return
  fi
  {
    echo ""
    echo "$MARKER_START"
    echo 'export DOTNET_ROOT="$HOME/.dotnet"'
    echo 'export PATH="$DOTNET_ROOT:$PATH"'
    echo "$MARKER_END"
  } >> "$target"
  echo "Appended DOTNET_ROOT/PATH to $target"
}

if [ "$(id -u)" -eq 0 ]; then
  write_profiled
else
  echo "Not running as root. Will update user profiles instead."
  append_user_profile "$HOME/.profile"
  append_user_profile "$HOME/.bashrc"
fi

echo "Done. Start a new shell or run:"
echo "  source /etc/profile || source ~/.profile"
