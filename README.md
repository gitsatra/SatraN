# SatraN

Premium desktop client for Satra on Windows, Linux (Debian/Ubuntu) and macOS. It is a fork of
[v2rayN](https://github.com/2dust/v2rayN) (GPL-3.0) and keeps its engine (ServiceLib, Xray core),
but the whole interface is new and matches the Android app SatraNG.

## What's different from v2rayN
- **Satra UI**: dark theme with an orange brand colour that turns green while connected. It has five pages:
  - **Home**: animated connect orb, live speed graph, today/session totals.
  - **Servers**: exit-country flags, tunnel/CDN badges (Cloudflare, Fastly, Arvan), favourites, ping and speed test per server or for all, Smart connect.
  - **Subscriptions**: quota/expiry bar that goes green → orange → red, announcement, Renew and Support buttons.
  - **Usage**: 7- and 30-day charts and the limits on this computer.
  - **Settings**: simple mode, plus an Advanced switch that opens the full v2rayN engine settings.
- **Satra Hub**: for subscriptions that advertise a hub (`X-Satra-Hub` header), SatraN:
  - registers the computer (`X-HWID` and the other identity headers)
  - switches to the per-device subscription link
  - shows owner screens behind the owner PIN: the device list with usage, and server-side limits per subscription or per device with top-ups
- **Local limits**:
  - an all-traffic cap per day, week or month (disconnects when the cap is used up)
  - site/program blocks once their budget is used up (0 MB blocks them immediately)
  - exact per-site quotas are enforced server-side through Satra Hub
- Languages: English, فارسی (RTL), Русский.
- Routing presets: Iran and Russia.
- Statistics are always on.

## Build
```
dotnet publish v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj -c Release -r linux-x64 -p:SelfContained=true -o pub
python3 packaging/package.py linux-x64 pub <xray-dir> 1.0.0 dist   # tar.gz + .deb
```
RIDs: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64` (unsigned `.app` zip).
CI: `.github/workflows/satran.yml` builds all four and uploads them as private artifacts.

## License
GPL-3.0, same as upstream v2rayN.
