#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
install_dir="$repo_root/.dotnet"
sdk_version="10.0.401"

if [[ -x "$install_dir/dotnet" ]] && [[ "$($install_dir/dotnet --version)" == "$sdk_version" ]]; then
  echo ".NET SDK $sdk_version is already installed in $install_dir"
  exit 0
fi

installer="$(mktemp "${TMPDIR:-/tmp}/midi-usb-doctor-dotnet-install.XXXXXX.sh")"
cleanup() {
  rm -f "$installer"
}
trap cleanup EXIT

curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$installer"
bash "$installer" --version "$sdk_version" --install-dir "$install_dir"

echo
echo "Installed .NET SDK $sdk_version."
echo "Run repo commands with: ./scripts/dotnet <arguments>"
