#!/usr/bin/env python3
"""SatraN packager: turns `dotnet publish` output into release artifacts.

usage: package.py <rid> <publish_dir> <xray_dir> <version> <out_dir>
  rid: win-x64 | linux-x64 | osx-x64 | osx-arm64
Produces: SatraN-<ver>-win-x64.zip, SatraN-<ver>-linux-x64.tar.gz + satran_<ver>_amd64.deb,
          SatraN-<ver>-osx-<arch>.app.zip (unsigned)
"""
import os, re, sys, shutil, stat, tarfile, zipfile, subprocess, tempfile

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
    if os.path.isdir(os.path.join(xray, "srss")):  # bundled sing-box rule sets (offline-safe TUN)
        shutil.copytree(os.path.join(xray, "srss"), os.path.join(b, "srss"), dirs_exist_ok=True)


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
# size: drop debugger/crash-dump helpers that end users never need
for junk in ("createdump", "libmscordaccore.so", "libmscordbi.so", "mscordaccore.dll", "mscordbi.dll", "createdump.exe"):
    jp = os.path.join(app, junk)
    if os.path.exists(jp):
        os.remove(jp)
inject_core(app)
os.chmod(os.path.join(app, exe), 0o755)

if rid.startswith("win"):
    # Admin install/uninstall helpers (TUN needs elevation on Windows)
    win_dir = os.path.join(os.path.dirname(__file__), "windows")
    for name in ("Install-SatraN.ps1", "Uninstall-SatraN.ps1"):
        src = os.path.join(win_dir, name)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(app, name))
            if name.endswith(".ps1"):
                dst = os.path.join(app, name)
                txt = open(dst, encoding="utf-8-sig").read().replace("__VERSION__", ver)  # read BEFORE truncating
                with open(dst, "w", newline="\r\n", encoding="utf-8-sig") as fh:
                    fh.write(txt)
    zip_path = os.path.join(out, f"SatraN-{ver}-{rid}.zip")
    zipdir(app, zip_path, "SatraN")
    # SatraN-Setup: self-extracting archive when 7z is available; else a fat zip named like Setup.
    setup_zip = os.path.join(out, f"SatraN-Setup-{ver}.zip")
    shutil.copy2(zip_path, setup_zip)

    # Standard wizard installer (NSIS): Next/Next/Install/Finish, Program Files, Helper service, full uninstaller.
    nsi = os.path.join(os.path.dirname(__file__), "windows", "SatraN.nsi")
    setup_exe = os.path.abspath(os.path.join(out, f"SatraN-Setup-{ver}.exe"))
    subprocess.check_call(["makensis", "-V2", f"-DVERSION={ver}", f"-DSRC={os.path.abspath(app)}",
                           f"-DOUTFILE={setup_exe}", nsi])
    print("wrote", setup_exe)


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
        # Force XDG data dir — /opt/satran is not writable for configs; without this
        # a user-owned install tree stores empty guiConfigs next to the binary.
        f.write("#!/bin/sh\nexport V2RAYN_LOCAL_APPLICATION_DATA_V2=1\nexec /opt/satran/SatraN \"$@\"\n")
    os.chmod(os.path.join(root, "usr/bin/satran"), 0o755)
    os.makedirs(os.path.join(root, "usr/share/applications"))
    with open(os.path.join(root, "usr/share/applications/satran.desktop"), "w") as f:
        f.write("[Desktop Entry]\nName=SatraN\nComment=Satra VPN client\nExec=env V2RAYN_LOCAL_APPLICATION_DATA_V2=1 /opt/satran/SatraN\nIcon=satran\nStartupWMClass=SatraN\nTerminal=false\nType=Application\nCategories=Network;\n")
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
    # Outline/OpenVPN-style: helper + polkit so TUN never needs a typed password after install
    helper_src = os.path.join(os.path.dirname(__file__), "satran-fix-caps.sh")
    policy_src = os.path.join(os.path.dirname(__file__), "com.satran.policy")
    resolved_rules = os.path.join(os.path.dirname(__file__), "com.satran.resolved.rules")
    shutil.copy2(helper_src, os.path.join(root, "opt/satran/bin/satran-fix-caps"))
    os.chmod(os.path.join(root, "opt/satran/bin/satran-fix-caps"), 0o755)
    os.makedirs(os.path.join(root, "usr/share/polkit-1/actions"), exist_ok=True)
    shutil.copy2(policy_src, os.path.join(root, "usr/share/polkit-1/actions/com.satran.policy"))
    # sing-box TUN configures DNS via systemd-resolved (3 polkit actions). Allow without password.
    os.makedirs(os.path.join(root, "usr/share/polkit-1/rules.d"), exist_ok=True)
    shutil.copy2(resolved_rules, os.path.join(root, "usr/share/polkit-1/rules.d/com.satran.resolved.rules"))
    # Signed apt repo on Satra Hub so `sudo apt update && sudo apt upgrade` updates SatraN
    os.makedirs(os.path.join(root, "usr/share/keyrings"), exist_ok=True)
    shutil.copy2(os.path.join(os.path.dirname(__file__), "satran-archive.gpg"), os.path.join(root, "usr/share/keyrings/satran-archive.gpg"))
    os.makedirs(os.path.join(root, "etc/apt/sources.list.d"), exist_ok=True)
    with open(os.path.join(root, "etc/apt/sources.list.d/satran.list"), "w") as f:
        f.write(
            "# SatraN updates (signed). Remove this file to stop apt updates for SatraN.\n"
            "# Satra Hub origin IP: the hostname's CDN front does not carry port 2097. Chain is verified; packages are GPG-signed.\n"
            "deb [arch=amd64 signed-by=/usr/share/keyrings/satran-archive.gpg] https://107.189.24.195:2097/satra/dl/apt stable main\n"
        )
    os.makedirs(os.path.join(root, "etc/apt/apt.conf.d"), exist_ok=True)
    with open(os.path.join(root, "etc/apt/apt.conf.d/99satran"), "w") as f:
        f.write('// SatraN apt repo is reached by IP; its certificate is for the Hub domain\nAcquire::https::107.189.24.195::Verify-Host "false";\n')
    # Privileged TUN helper (systemd, root): connect never asks for a password
    os.makedirs(os.path.join(root, "lib/systemd/system"), exist_ok=True)
    shutil.copy2(os.path.join(os.path.dirname(__file__), "satran-helper.service"), os.path.join(root, "lib/systemd/system/satran-helper.service"))
    os.makedirs(os.path.join(root, "DEBIAN"))
    with open(os.path.join(root, "DEBIAN/prerm"), "w") as f:
        f.write("#!/bin/sh\nif [ \"$1\" = remove ] && command -v systemctl >/dev/null 2>&1; then systemctl disable --now satran-helper.service >/dev/null 2>&1 || true; fi\nexit 0\n")
    os.chmod(os.path.join(root, "DEBIAN/prerm"), 0o755)
    with open(os.path.join(root, "DEBIAN/postinst"), "w") as f:
        f.write("""#!/bin/sh
set -e
# VPN like OpenVPN/Outline: capabilities on packaged cores; polkit helper for repair — no per-connect password.
SETCAP=/usr/sbin/setcap
[ -x "$SETCAP" ] || SETCAP=/sbin/setcap
[ -x "$SETCAP" ] || SETCAP=$(command -v setcap || true)
apply_caps() {
  c="$1"
  [ -L "$c" ] && return 0
  [ -f "$c" ] && [ -x "$c" ] || return 0
  if [ -n "$SETCAP" ] && "$SETCAP" cap_net_admin,cap_net_bind_service,cap_net_raw+ep "$c"; then
    touch "$c.caps"
  else
    rm -f "$c.caps"
  fi
}
for c in /opt/satran/bin/sing_box/sing-box /opt/satran/bin/xray/xray; do apply_caps "$c"; done
# Xray resolves geosite/geoip next to the real binary (/opt/satran/bin/xray/), not bin/.
for g in geoip.dat geosite.dat; do
  [ -f "/opt/satran/bin/$g" ] || continue
  ln -sfn "/opt/satran/bin/$g" "/opt/satran/bin/xray/$g"
  ln -sfn "/opt/satran/bin/$g" "/opt/satran/bin/sing_box/$g" 2>/dev/null || true
done
# Helper ACL: the installing (desktop) user may use the helper; group "satran" members too.
mkdir -p /etc/satran
touch /etc/satran/helper.allow; chmod 644 /etc/satran/helper.allow
getent group satran >/dev/null 2>&1 || groupadd -r satran 2>/dev/null || true
U="${SUDO_USER:-}"
[ -z "$U" ] && [ -n "${PKEXEC_UID:-}" ] && U=$(getent passwd "$PKEXEC_UID" | cut -d: -f1)
[ -z "$U" ] && U=$(loginctl list-sessions --no-legend 2>/dev/null | awk '$3!="root"&&$3!=""{print $3; exit}')
if [ -n "$U" ] && [ "$U" != root ] && ! grep -qx "$U" /etc/satran/helper.allow; then echo "$U" >> /etc/satran/helper.allow; fi
if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
  systemctl daemon-reload || true
  systemctl enable satran-helper.service >/dev/null 2>&1 || true
  systemctl restart satran-helper.service || true
fi
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t /usr/share/icons/hicolor || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
exit 0
""")
    os.chmod(os.path.join(root, "DEBIAN/postinst"), 0o755)
    size = sum(os.path.getsize(os.path.join(r, f)) for r, _, fs in os.walk(root) for f in fs) // 1024
    with open(os.path.join(root, "DEBIAN/control"), "w") as f:
        f.write(f"Package: satran\nVersion: {ver}\nArchitecture: amd64\nMaintainer: Satra <satra@localhost>\nInstalled-Size: {size}\n"
                "Depends: libc6, libcap2-bin, libfontconfig1, libice6, libsm6, libx11-6, libxcursor1, libxext6, libxi6, libxrandr2\n"
                "Section: net\nPriority: optional\nHomepage: https://github.com/gitsatra/SatraN\n"
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
