#!/bin/sh
# SatraN installer (Linux):  curl -fsSL https://raw.githubusercontent.com/gitsatra/SatraN/main/install.sh | sh
# Uses the Satra Hub apt repository when reachable (so `apt upgrade` updates SatraN); otherwise installs the
# latest public GitHub release (gitsatra/SatraN). The app also updates itself from the Hub or GitHub.
set -e
REPO=gitsatra/SatraN
HUB=https://p.iivt.ir:2097/satra/dl
if curl -fsS -m 10 --connect-to p.iivt.ir:2097:107.189.24.195:2097 "$HUB/latest.json" >/dev/null 2>&1; then
  curl -fsSL --connect-to p.iivt.ir:2097:107.189.24.195:2097 "$HUB/install.sh" | sh
  exit $?
fi
echo "Satra Hub unreachable: installing from GitHub releases ($REPO)"
SUDO=""; [ "$(id -u)" = 0 ] || SUDO=sudo
T=$(mktemp -d)
curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" -o "$T/r.json"
V=$(sed -n 's/.*"tag_name": *"v\{0,1\}\([^"]*\)".*/\1/p' "$T/r.json" | head -1)
[ -n "$V" ] || { echo "could not find the latest release"; exit 1; }
get() { curl -fL --retry 3 -o "$T/$1" "https://github.com/$REPO/releases/download/v$V/$1"; }
check() { if get SHA256SUMS 2>/dev/null; then (cd "$T" && grep " $1\$" SHA256SUMS | sha256sum -c -) || { echo "checksum mismatch"; exit 1; }; fi; }
if command -v apt-get >/dev/null 2>&1 && [ "$(dpkg --print-architecture 2>/dev/null)" = amd64 ]; then
  F="satran_${V}_amd64.deb"; get "$F"; check "$F"
  $SUDO apt-get install -y "$T/$F"
  echo "SatraN $V installed. The app checks for updates itself on start."
else
  F="SatraN-$V-linux-x64.tar.gz"; get "$F"; check "$F"
  mkdir -p ~/.local/opt/satran && tar -xzf "$T/$F" -C ~/.local/opt/satran --strip-components=1
  echo "Run: ~/.local/opt/satran/SatraN"
fi
rm -rf "$T"
