#!/bin/sh
# SatraN installer:  curl -fsSL https://p.iivt.ir:2097/satra/dl/install.sh | sh
set -e
B=https://p.iivt.ir:2097/satra/dl
SUDO=""; [ "$(id -u)" = 0 ] || SUDO=sudo
if command -v apt-get >/dev/null 2>&1 && [ "$(dpkg --print-architecture 2>/dev/null)" = amd64 ]; then
  echo "Adding the SatraN apt repository…"
  curl -fsSL "$B/apt/satran.gpg" | $SUDO tee /usr/share/keyrings/satran-archive.gpg >/dev/null
  echo "deb [arch=amd64 signed-by=/usr/share/keyrings/satran-archive.gpg] $B/apt stable main" | $SUDO tee /etc/apt/sources.list.d/satran.list >/dev/null
  $SUDO apt-get update -o Dir::Etc::sourcelist=sources.list.d/satran.list -o Dir::Etc::sourceparts=- -o APT::Get::List-Cleanup=0
  $SUDO apt-get install -y satran
  echo "SatraN installed. Updates arrive with: sudo apt update && sudo apt upgrade"
else
  echo "No apt: installing the portable build to ~/.local/opt/satran"
  T=$(mktemp -d); V=$(curl -fsSL "$B/latest.json" | sed -n 's/.*"version": *"\([^"]*\)".*/\1/p')
  curl -fL "$B/$V/SatraN-$V-linux-x64.tar.gz" -o "$T/s.tgz"
  mkdir -p ~/.local/opt/satran && tar -xzf "$T/s.tgz" -C ~/.local/opt/satran --strip-components=1 && rm -rf "$T"
  echo "Run: ~/.local/opt/satran/SatraN"
fi
