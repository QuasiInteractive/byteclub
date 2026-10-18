#!/usr/bin/env bash
# Copy the five FF7 menu clips into web/public/sfx as small MP3s. They are not committed to git.
# Usage: deploy/copy-sfx.sh "/c/Users/Deadl/Downloads/FFVII Menu Sounds"
set -euo pipefail
src="${1:-/c/Users/Deadl/Downloads/FFVII Menu Sounds}"
out="$(dirname "$0")/../web/public/sfx"
mkdir -p "$out"
for pair in move:deck_ui_navigation confirm:deck_ui_default_activation back:deck_ui_hide_modal \
            error:bumper_end victory:deck_ui_launch_game; do
  ffmpeg -hide_banner -loglevel error -y -i "$src/${pair#*:}.wav" -ac 1 -b:a 96k "$out/${pair%%:*}.mp3"
done
echo "copied 5 clips to $out"
