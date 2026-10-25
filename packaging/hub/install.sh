#!/bin/sh
# SatraN installer:  curl -fsSL --connect-to p.iivt.ir:2097:107.189.24.195:2097 https://p.iivt.ir:2097/satra/dl/install.sh | sh
# (p.iivt.ir sits behind a CDN that does not carry port 2097 — the Hub origin IP is used directly; TLS is still verified.)
set -e
B=https://p.iivt.ir:2097/satra/dl
IP=107.189.24.195
C="curl -fsSL --connect-to p.iivt.ir:2097:$IP:2097"
SUDO=""; [ "$(id -u)" = 0 ] || SUDO=sudo
if command -v apt-get >/dev/null 2>&1 && [ "$(dpkg --print-architecture 2>/dev/null)" = amd64 ]; then
  echo "Adding the SatraN apt repository…"
  $C "$B/apt/satran.gpg" | $SUDO tee /usr/share/keyrings/satran-archive.gpg >/dev/null
  # apt has no --connect-to: use the origin IP; the certificate chain is still checked and every package is GPG-signed
  echo "Acquire::https::$IP::Verify-Host \"false\";" | $SUDO tee /etc/apt/apt.conf.d/99satran >/dev/null
  echo "deb [arch=amd64 signed-by=/usr/share/keyrings/satran-archive.gpg] https://$IP:2097/satra/dl/apt stable main" | $SUDO tee /etc/apt/sources.list.d/satran.list >/dev/null
  $SUDO apt-get update -o Dir::Etc::sourcelist=sources.list.d/satran.list -o Dir::Etc::sourceparts=- -o APT::Get::List-Cleanup=0
  $SUDO apt-get install -y satran
  echo "SatraN installed. Updates arrive with: sudo apt update && sudo apt upgrade"
else
  echo "No apt: installing the portable build to ~/.local/opt/satran"
  T=$(mktemp -d); $C "$B/latest.json" -o "$T/l.json"
  V=$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' "$T/l.json" | head -1)
  F="SatraN-$V-linux-x64.tar.gz"
  $C "$B/$V/$F" -o "$T/s.tgz"
  SHA=$(tr -d '\n ' < "$T/l.json" | sed -n "s/.*\"$F\":{[^}]*\"sha256\":\"\([0-9a-f]*\)\".*/\1/p")
  [ -z "$SHA" ] || echo "$SHA  $T/s.tgz" | sha256sum -c - >/dev/null || { echo "checksum mismatch"; exit 1; }
  mkdir -p ~/.local/opt/satran && tar -xzf "$T/s.tgz" -C ~/.local/opt/satran --strip-components=1 && rm -rf "$T"
  echo "Run: ~/.local/opt/satran/SatraN"
fi
