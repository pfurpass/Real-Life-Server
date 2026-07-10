#!/usr/bin/env bash
# Generates placeholder slate videos for local testing/first boot, so the compositor
# (docs/CONCEPT.md, chapter 4) has something to loop before you drop in branded assets.
# Requires a local ffmpeg. Replace these files with real branded loops for production.
set -euo pipefail
cd "$(dirname "$0")"

make_slate () {
  local file=$1 text=$2 color=$3
  # Both -i inputs must come before any output option (-vf here) - an option placed between
  # two -i flags is parsed as an input option for the *following* input, not an output filter.
  ffmpeg -y \
    -f lavfi -i "color=c=${color}:s=1280x720:d=10:r=30" \
    -f lavfi -i "anullsrc=r=44100:cl=stereo" \
    -vf "drawtext=fontcolor=white:fontsize=54:x=(w-text_w)/2:y=(h-text_h)/2:text='${text}'" \
    -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac -t 10 "${file}"
}

make_slate brb.mp4           "Be Right Back"                      "0x1e293b"
make_slate reconnecting.mp4  "Verbindung wird wiederhergestellt…" "0xb45309"
make_slate offline.mp4       "Offline"                            "0x111827"

echo "Placeholder scenes written to $(pwd)"
