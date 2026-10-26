#!/bin/sh
# SatraN installer (Linux):  curl -fsSL https://raw.githubusercontent.com/gitsatra/SatraN/main/install.sh | sh
# Adds the signed SatraN apt repository on GitHub Pages (so `apt upgrade` updates SatraN); otherwise installs the
# latest public GitHub release (gitsatra/SatraN). No dependency on Satra Hub.
set -e
REPO=gitsatra/SatraN
APT=https://gitsatra.github.io/SatraN/apt
SUDO=""; [ "$(id -u)" = 0 ] || SUDO=sudo
T=$(mktemp -d)
if command -v apt-get >/dev/null 2>&1 && [ "$(dpkg --print-architecture 2>/dev/null)" = amd64 ] && curl -fsSL -m 30 "$APT/satran-archive.gpg" -o "$T/k.gpg"; then
  $SUDO install -Dm644 "$T/k.gpg" /usr/share/keyrings/satran-archive.gpg
  printf '# SatraN updates (signed, GitHub Pages)\ndeb [arch=amd64 signed-by=/usr/share/keyrings/satran-archive.gpg] %s stable main\n' "$APT" | $SUDO tee /etc/apt/sources.list.d/satran.list >/dev/null
  if $SUDO apt-get update -o Dir::Etc::sourcelist=sources.list.d/satran.list -o Dir::Etc::sourceparts=- -o APT::Get::List-Cleanup=0 && $SUDO apt-get install -y satran; then
    rm -rf "$T"; echo "SatraN installed. Updates: sudo apt update && sudo apt upgrade (or in-app)."; exit 0
  fi
fi
echo "apt repo unavailable: installing from GitHub releases ($REPO)"
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
