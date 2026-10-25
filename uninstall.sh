#!/bin/sh
# SatraN uninstaller:  curl -fsSL https://raw.githubusercontent.com/gitsatra/SatraN/main/uninstall.sh | sh   (add "-s -- --purge" to also delete settings)
SUDO=""; [ "$(id -u)" = 0 ] || SUDO=sudo
pkill -x SatraN 2>/dev/null || true
if dpkg -s satran >/dev/null 2>&1; then $SUDO apt-get remove -y satran; fi
$SUDO rm -f /etc/apt/apt.conf.d/99satran /etc/apt/sources.list.d/satran.list /usr/share/keyrings/satran-archive.gpg
rm -rf ~/.local/opt/satran
case " $* " in *" --purge "*) rm -rf ~/.local/share/SatraN; echo "Settings removed." ;; esac
echo "SatraN removed."
