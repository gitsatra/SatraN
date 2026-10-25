#!/bin/bash
# Publish a CI release to Satra Hub downloads (/satra/dl/) + rebuild the signed apt repo.
# usage: publish.sh <dir-with-release-files> <version>
# env: SATRAN_GPG_KEY (fingerprint), GNUPGHOME, HUB_SSH (command prefix that runs ssh to the Hub host)
set -euo pipefail
SRC=$1; V=$2; HERE=$(cd "$(dirname "$0")" && pwd)
M=${HUB_MIRROR:-$HOME/.satra/hubdl}
mkdir -p "$M/$V" "$M/debs"
find "$SRC" -type f \( -name "*$V*" \) -exec cp -f {} "$M/$V/" \;
cp -f "$M/$V"/satran_${V}_amd64.deb "$M/debs/"
# keep the 3 newest versions (rollback)
ls -1d "$M"/[0-9]*/ | sort -V | head -n -3 | xargs -r rm -rf
ls -1 "$M"/debs/*.deb | sort -V | head -n -3 | xargs -r rm -f
rm -rf "$M/apt"; python3 "$HERE/../apt-repo.py" "$M/debs" "$M/apt"
cp -f "$HERE"/install.sh "$HERE"/uninstall.sh "$HERE"/install.ps1 "$HERE"/uninstall.ps1 "$M/"
python3 - "$M" "$V" <<'PY'
import hashlib, json, os, sys, time
m, v = sys.argv[1:3]
files = {}
for f in sorted(os.listdir(os.path.join(m, v))):
    p = os.path.join(m, v, f)
    files[f] = {"url": f"{v}/{f}", "size": os.path.getsize(p), "sha256": hashlib.sha256(open(p, "rb").read()).hexdigest()}
json.dump({"version": v, "date": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "files": files}, open(os.path.join(m, "latest.json"), "w"), indent=1)
PY
tar -C "$M" --warning=no-file-changed -cf - . | $HUB_SSH 'mkdir -p /var/lib/satra-hub/dl.new && tar -C /var/lib/satra-hub/dl.new -xf - && rm -rf /var/lib/satra-hub/dl.old && { [ -d /var/lib/satra-hub/dl ] && mv /var/lib/satra-hub/dl /var/lib/satra-hub/dl.old || true; } && mv /var/lib/satra-hub/dl.new /var/lib/satra-hub/dl && rm -rf /var/lib/satra-hub/dl.old && chmod -R a+rX /var/lib/satra-hub/dl'
echo "published $V"
