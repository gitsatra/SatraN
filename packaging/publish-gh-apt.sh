#!/bin/bash
# Publish the signed SatraN apt repo to GitHub Pages (gh-pages branch of gitsatra/SatraN).
# apt source: deb [arch=amd64 signed-by=/usr/share/keyrings/satran-archive.gpg] https://gitsatra.github.io/SatraN/apt stable main
# usage: publish-gh-apt.sh <satran_X_amd64.deb>   env: SATRAN_GPG_KEY, GNUPGHOME
set -euo pipefail
DEB=$1; HERE=$(cd "$(dirname "$0")" && pwd); W=$(mktemp -d)
mkdir -p "$W/site" "$W/debs"; cp "$DEB" "$W/debs/"
python3 "$HERE/apt-repo.py" "$W/debs" "$W/site/apt"
cp "$HERE/satran-archive.gpg" "$W/site/apt/satran-archive.gpg"
for f in install.sh uninstall.sh install.ps1 uninstall.ps1; do [ -f "$HERE/../$f" ] && cp "$HERE/../$f" "$W/site/"; done
touch "$W/site/.nojekyll"; echo '<a href="https://github.com/gitsatra/SatraN">SatraN</a>' > "$W/site/index.html"
cd "$W/site" && git init -q -b gh-pages && git add -A && git -c user.name=gitsatra -c user.email=gitsatra@users.noreply.github.com commit -qm "apt: $(basename "$DEB")"
git push -f "${GH_REMOTE:-https://github.com/gitsatra/SatraN.git}" gh-pages
rm -rf "$W"
