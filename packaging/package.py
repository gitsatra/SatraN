#!/usr/bin/env python3
"""SatraN packager: turns `dotnet publish` output into release artifacts.

usage: package.py <rid> <publish_dir> <xray_dir> <version> <out_dir>
  rid: win-x64 | linux-x64 | osx-x64 | osx-arm64
Produces: SatraN-<ver>-win-x64.zip, SatraN-<ver>-linux-x64.tar.gz + satran_<ver>_amd64.deb,
          SatraN-<ver>-osx-<arch>.app.zip (unsigned)
"""
import os, sys, shutil, stat, tarfile, zipfile, subprocess, tempfile

rid, pub, xray, ver, out = sys.argv[1:6]
os.makedirs(out, exist_ok=True)
exe = "SatraN.exe" if rid.startswith("win") else "SatraN"


def inject_core(dst):
    b = os.path.join(dst, "bin")
    os.makedirs(os.path.join(b, "xray"), exist_ok=True)
    core = "xray.exe" if rid.startswith("win") else "xray"
    shutil.copy2(os.path.join(xray, core), os.path.join(b, "xray", core))
    os.chmod(os.path.join(b, "xray", core), 0o755)
    sb = "sing-box.exe" if rid.startswith("win") else "sing-box"
    if os.path.exists(os.path.join(xray, sb)):  # sing-box powers VPN (TUN) mode
        os.makedirs(os.path.join(b, "sing_box"), exist_ok=True)
        shutil.copy2(os.path.join(xray, sb), os.path.join(b, "sing_box", sb))
        os.chmod(os.path.join(b, "sing_box", sb), 0o755)
        if rid.startswith("win") and os.path.exists(os.path.join(xray, "wintun.dll")):
            shutil.copy2(os.path.join(xray, "wintun.dll"), os.path.join(b, "sing_box", "wintun.dll"))
    for g in ("geoip.dat", "geosite.dat"):
        shutil.copy2(os.path.join(xray, g), os.path.join(b, g))


def zipdir(src, zpath, prefix):
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for root, dirs, files in os.walk(src):
            for d in dirs:  # keep directory entries (empty dirs inside .app)
                p = os.path.join(root, d)
                zi = zipfile.ZipInfo(os.path.join(prefix, os.path.relpath(p, src)) + "/")
                zi.external_attr = (0o40755 << 16) | 0x10
                z.writestr(zi, "")
            for f in files:
                p = os.path.join(root, f)
                arc = os.path.join(prefix, os.path.relpath(p, src))
                zi = zipfile.ZipInfo.from_file(p, arc)
                mode = os.stat(p).st_mode
                zi.external_attr = (mode & 0xFFFF) << 16
                zi.compress_type = zipfile.ZIP_DEFLATED
                with open(p, "rb") as fh:
                    z.writestr(zi, fh.read())


work = tempfile.mkdtemp()
app = os.path.join(work, "SatraN")
shutil.copytree(pub, app)
inject_core(app)
os.chmod(os.path.join(app, exe), 0o755)

if rid.startswith("win"):
    zipdir(app, os.path.join(out, f"SatraN-{ver}-{rid}.zip"), "SatraN")

elif rid.startswith("linux"):
    with tarfile.open(os.path.join(out, f"SatraN-{ver}-{rid}.tar.gz"), "w:gz") as t:
        t.add(app, arcname="SatraN")
    # .deb: /opt/satran + launcher + desktop entry
    root = os.path.join(work, "deb")
    shutil.copytree(app, os.path.join(root, "opt/satran"))
    # configs must live in the user's home, not in /opt
    open(os.path.join(root, "opt/satran/NotStoreConfigHere.txt"), "w").write("configs are stored in ~/.local/share/SatraN\n")
    os.makedirs(os.path.join(root, "usr/bin"))
    with open(os.path.join(root, "usr/bin/satran"), "w") as f:
        f.write("#!/bin/sh\nexec /opt/satran/SatraN \"$@\"\n")
    os.chmod(os.path.join(root, "usr/bin/satran"), 0o755)
    os.makedirs(os.path.join(root, "usr/share/applications"))
    with open(os.path.join(root, "usr/share/applications/satran.desktop"), "w") as f:
        f.write("[Desktop Entry]\nName=SatraN\nComment=Satra VPN client\nExec=/opt/satran/SatraN\nIcon=satran\nStartupWMClass=SatraN\nTerminal=false\nType=Application\nCategories=Network;\n")
    icon = os.path.join(os.path.dirname(__file__), "satran.png")
    if os.path.exists(icon):
        shutil.copy2(icon, os.path.join(root, "opt/satran/satran.png"))
    # icons: hicolor theme (all sizes) + pixmaps, so menus, dock and taskbar show the shield
    icons = os.path.join(os.path.dirname(__file__), "icons")
    for px in (16, 24, 32, 48, 64, 128, 256, 512):
        d = os.path.join(root, f"usr/share/icons/hicolor/{px}x{px}/apps")
        os.makedirs(d, exist_ok=True)
        shutil.copy2(os.path.join(icons, f"satran-{px}.png"), os.path.join(d, "satran.png"))
    os.makedirs(os.path.join(root, "usr/share/pixmaps"), exist_ok=True)
    shutil.copy2(os.path.join(icons, "satran-256.png"), os.path.join(root, "usr/share/pixmaps/satran.png"))
    os.makedirs(os.path.join(root, "DEBIAN"))
    with open(os.path.join(root, "DEBIAN/postinst"), "w") as f:
        f.write("""#!/bin/sh
set -e
# VPN mode without password prompts: let the bundled cores create the TUN device and routes.
# The app also copies cores into ~/.local/share/SatraN/bin — those copies drop capabilities,
# so refresh caps on any existing user copies too.
apply_caps() {
  c="$1"
  [ -x "$c" ] || return 0
  if command -v setcap >/dev/null 2>&1 && setcap cap_net_admin,cap_net_bind_service,cap_net_raw+ep "$c"; then
    touch "$c.caps"
  else
    rm -f "$c.caps"
  fi
}
for c in /opt/satran/bin/sing_box/sing-box /opt/satran/bin/xray/xray; do apply_caps "$c"; done
for home in /home/*; do
  [ -d "$home" ] || continue
  apply_caps "$home/.local/share/SatraN/bin/sing_box/sing-box"
  apply_caps "$home/.local/share/SatraN/bin/xray/xray"
done
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t /usr/share/icons/hicolor || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
exit 0
""")
    os.chmod(os.path.join(root, "DEBIAN/postinst"), 0o755)
    size = sum(os.path.getsize(os.path.join(r, f)) for r, _, fs in os.walk(root) for f in fs) // 1024
    with open(os.path.join(root, "DEBIAN/control"), "w") as f:
        f.write(f"Package: satran\nVersion: {ver}\nArchitecture: amd64\nMaintainer: Satra <satra@localhost>\nInstalled-Size: {size}\n"
                "Depends: libc6, libcap2-bin, libfontconfig1, libice6, libsm6, libx11-6, libxcursor1, libxext6, libxi6, libxrandr2\n"
                "Section: net\nPriority: optional\nHomepage: https://github.com/liamlope/satran\n"
                "Description: SatraN - premium desktop client for Satra (v2rayN fork)\n Xray based client with Satra Hub support.\n")
    for r, ds, fs in os.walk(root):
        for d in ds: os.chmod(os.path.join(r, d), 0o755)
    subprocess.check_call(["dpkg-deb", "--root-owner-group", "--build", root, os.path.join(out, f"satran_{ver}_amd64.deb")])

else:  # macOS .app (unsigned)
    bundle = os.path.join(work, "pkg", "SatraN.app")
    os.makedirs(os.path.join(bundle, "Contents/Resources"))
    shutil.copytree(app, os.path.join(bundle, "Contents/MacOS"))
    icns = os.path.join(bundle, "Contents/MacOS/v2rayN.icns")
    if os.path.exists(icns):
        shutil.copy2(icns, os.path.join(bundle, "Contents/Resources/AppIcon.icns"))
    open(os.path.join(bundle, "Contents/MacOS/NotStoreConfigHere.txt"), "w").write("configs are stored in ~/Library/Application Support\n")
    plist = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleLocalizations</key><array><string>en</string><string>fa</string><string>ru</string></array>
  <key>CFBundleDisplayName</key><string>SatraN</string>
  <key>CFBundleExecutable</key><string>SatraN</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleIdentifier</key><string>com.satra.satran</string>
  <key>CFBundleName</key><string>SatraN</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>{ver}</string>
  <key>CFBundleVersion</key><string>{ver}</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSMinimumSystemVersion</key><string>13.6</string>
</dict></plist>
"""
    open(os.path.join(bundle, "Contents/Info.plist"), "w").write(plist)
    zipdir(os.path.join(work, "pkg"), os.path.join(out, f"SatraN-{ver}-{rid}.app.zip"), "")

shutil.rmtree(work)
print("ok", rid)
