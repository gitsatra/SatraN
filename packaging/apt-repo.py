#!/usr/bin/env python3
"""Build a minimal apt repository tree from satran_*.deb files.

usage: apt-repo.py <deb_or_dist_dir> <out_dir>
Produces: out/dists/stable/main/binary-amd64/Packages(.gz) and pool/main/s/satran/*.deb
Publish the out/ directory to the Satra Hub apt path (signed with the Hub apt key); releases live on gitsatra/SatraN.
"""
import gzip
import hashlib
import os
import shutil
import sys
import time

src, out = sys.argv[1:3]
os.makedirs(out, exist_ok=True)

debs = []
if os.path.isfile(src) and src.endswith(".deb"):
    debs = [src]
else:
    debs = sorted(
        os.path.join(src, f) for f in os.listdir(src) if f.startswith("satran_") and f.endswith(".deb")
    )
if not debs:
    sys.exit("no satran_*.deb found")

pool = os.path.join(out, "pool/main/s/satran")
os.makedirs(pool, exist_ok=True)
binary = os.path.join(out, "dists/stable/main/binary-amd64")
os.makedirs(binary, exist_ok=True)

entries = []
for deb in debs:
    name = os.path.basename(deb)
    dest = os.path.join(pool, name)
    shutil.copy2(deb, dest)
    size = os.path.getsize(dest)
    sha256 = hashlib.sha256(open(dest, "rb").read()).hexdigest()
    # Parse Version/Architecture from control via dpkg-deb when available
    ver = name[len("satran_") : -len("_amd64.deb")]
    arch = "amd64"
    try:
        import subprocess

        ctrl = subprocess.check_output(["dpkg-deb", "-f", dest, "Package", "Version", "Architecture", "Depends", "Description"], text=True)
        fields = {}
        key = None
        for line in ctrl.splitlines():
            if ":" in line and not line.startswith(" "):
                k, v = line.split(":", 1)
                fields[k.strip()] = v.strip()
                key = k.strip()
            elif key and line.startswith(" "):
                fields[key] = fields.get(key, "") + "\n" + line
        pkg = fields.get("Package", "satran")
        ver = fields.get("Version", ver)
        arch = fields.get("Architecture", arch)
        depends = fields.get("Depends", "")
        desc = fields.get("Description", "SatraN desktop client")
    except Exception:
        pkg, depends, desc = "satran", "", "SatraN desktop client"
    rel = f"pool/main/s/satran/{name}"
    entries.append(
        f"Package: {pkg}\nVersion: {ver}\nArchitecture: {arch}\n"
        f"Filename: {rel}\nSize: {size}\nSHA256: {sha256}\n"
        + (f"Depends: {depends}\n" if depends else "")
        + f"Description: {desc}\n"
    )

packages = "\n".join(entries) + "\n"
open(os.path.join(binary, "Packages"), "w").write(packages)
with gzip.open(os.path.join(binary, "Packages.gz"), "wb") as gz:
    gz.write(packages.encode())

def hashes(algo):
    out_lines = []
    for rel in ("main/binary-amd64/Packages", "main/binary-amd64/Packages.gz"):
        data = open(os.path.join(out, "dists/stable", rel), "rb").read()
        out_lines.append(f" {hashlib.new(algo, data).hexdigest()} {len(data)} {rel}")
    return "\n".join(out_lines)


release = f"""Origin: SatraN
Label: SatraN
Suite: stable
Codename: stable
Architectures: amd64
Components: main
Date: {time.strftime('%a, %d %b %Y %H:%M:%S +0000', time.gmtime())}
MD5Sum:
{hashes("md5")}
SHA256:
{hashes("sha256")}
"""
rel_path = os.path.join(out, "dists/stable/Release")
open(rel_path, "w").write(release)
# Sign when a key is available: SATRAN_GPG_KEY=<fingerprint> (GNUPGHOME selects the keyring)
key = os.environ.get("SATRAN_GPG_KEY")
if key:
    import subprocess

    d = os.path.join(out, "dists/stable")
    for f in ("InRelease", "Release.gpg"):
        try:
            os.remove(os.path.join(d, f))
        except FileNotFoundError:
            pass
    g = ["gpg", "--batch", "--yes", "--pinentry-mode", "loopback", "--passphrase", "", "-u", key]
    subprocess.check_call(g + ["--clearsign", "-o", os.path.join(d, "InRelease"), rel_path])
    subprocess.check_call(g + ["-abs", "-o", os.path.join(d, "Release.gpg"), rel_path])
    subprocess.check_call(["gpg", "--batch", "--yes", "--export", "-o", os.path.join(out, "satran.gpg"), key])
print("apt repo ready at", out, "with", len(debs), "package(s)", "(signed)" if key else "(unsigned)")
