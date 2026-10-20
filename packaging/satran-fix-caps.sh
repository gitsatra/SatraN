#!/bin/sh
# One-shot helper: restore CAP_NET_ADMIN on packaged SatraN cores (Outline-style).
# Invoked via pkexec; a polkit rule allows active local users without a password.
set -e
SETCAP=/usr/sbin/setcap
[ -x "$SETCAP" ] || SETCAP=/sbin/setcap
[ -x "$SETCAP" ] || SETCAP=$(command -v setcap)
[ -n "$SETCAP" ] || { echo "setcap not found" >&2; exit 1; }

apply() {
  f="$1"
  [ -f "$f" ] || return 0
  [ -L "$f" ] && return 0
  "$SETCAP" cap_net_admin,cap_net_bind_service,cap_net_raw+ep "$f"
  touch "$f.caps"
}

apply /opt/satran/bin/sing_box/sing-box
apply /opt/satran/bin/xray/xray
exit 0
