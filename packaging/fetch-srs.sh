#!/bin/bash
# Bundle sing-box rule sets so VPN (TUN) mode never depends on a first-run download.
# usage: fetch-srs.sh <outdir>
out=${1:?}; mkdir -p "$out"
for t in geoip-private geosite-private geosite-cn geoip-cn geosite-category-ru geoip-ru geosite-category-ir geoip-ir \
         geosite-category-ads-all geosite-google geosite-geolocation-!cn geosite-geolocation-cn; do
  k=${t%%-*}
  for u in "https://raw.githubusercontent.com/chocolate4u/Iran-sing-box-rules/rule-set/$t.srs" \
           "https://raw.githubusercontent.com/2dust/sing-box-rules/rule-set-$k/$t.srs"; do
    [[ $t == *-ir || $u == *2dust* ]] || continue
    curl -fsSL --retry 2 -o "$out/$t.srs" "$u" && break
  done
  [ -s "$out/$t.srs" ] || { rm -f "$out/$t.srs"; echo "skip $t"; }
done
ls "$out"
