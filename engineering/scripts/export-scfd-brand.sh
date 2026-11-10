#!/usr/bin/env bash
set -euo pipefail

output_dir="${1:?Pass an output directory for the SCFD artwork}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
master="$repo/product/brand/scfd-master.png"
navy='lutrgb=r=35:g=50:b=79'
white='lutrgb=r=255:g=255:b=255'
mark='format=rgba,crop=760:408:60:177,pad=800:800:20:196:color=black@0'
mkdir -p "$output_dir"

ffmpeg -loglevel error -y -i "$master" \
  -vf "format=rgba,crop=2052:408:60:177,scale=820:-1:flags=lanczos,$navy" \
  -frames:v 1 -update 1 "$output_dir/scfd-logo.png"

ffmpeg -loglevel error -y -i "$master" \
  -vf "$mark,$navy" -frames:v 1 -update 1 "$output_dir/short-colored-light.png"
ffmpeg -loglevel error -y -i "$master" \
  -vf "$mark,$white" -frames:v 1 -update 1 "$output_dir/short-colored-dark.png"

ffmpeg -loglevel error -y -i "$output_dir/short-colored-light.png" \
  -vf 'scale=256:256:flags=lanczos' -frames:v 1 -update 1 "$output_dir/favicon.png"
ffmpeg -loglevel error -y -i "$output_dir/short-colored-dark.png" \
  -vf 'scale=256:256:flags=lanczos' -frames:v 1 -update 1 "$output_dir/favicon-dark.png"
ffmpeg -loglevel error -y -i "$output_dir/short-colored-dark.png" \
  -filter_complex 'scale=144:144:flags=lanczos[mark];color=0x23324F:s=180x180[carrier];[carrier][mark]overlay=18:18:format=rgb,format=rgb24' \
  -frames:v 1 -update 1 "$output_dir/apple-touch-icon.png"

# Review the same artwork at header and favicon sizes on both demo surfaces.
ffmpeg -loglevel error -y -i "$output_dir/scfd-logo.png" -i "$output_dir/favicon.png" \
  -filter_complex "
    [0:v]scale=-1:28:flags=lanczos,split[navy][header];
    [header]$white[white];
    [1:v]split=4[a][b][c][d];
    [a]scale=16:16:flags=lanczos[m16];
    [b]scale=20:20:flags=lanczos[m20];
    [c]scale=24:24:flags=lanczos[m24];
    [d]scale=32:32:flags=lanczos[m32];
    color=black@0:s=240x60,format=rgba[strip];
    [strip][m16]overlay=20:22:format=rgb[s1];
    [s1][m20]overlay=75:20:format=rgb[s2];
    [s2][m24]overlay=130:18:format=rgb[s3];
    [s3][m32]overlay=187:14:format=rgb,split[marks][ink];
    [ink]$white[marksDark];
    color=white:s=460x60,format=rgba[light];
    color=0x171d28:s=460x60,format=rgba[dark];
    [light][navy]overlay=20:16:format=rgb[l1];
    [l1][marks]overlay=200:0:format=rgb[l2];
    [dark][white]overlay=20:16:format=rgb[d1];
    [d1][marksDark]overlay=200:0:format=rgb[d2];
    [l2][d2]vstack,format=rgb24
  " -frames:v 1 -update 1 "$output_dir/scfd-review.png"
