#!/usr/bin/env bash
# Flutter SDK-nı rəsmi flutter/flutter git tag-ından quraşdırır (E00-T06).
# Üçüncü tərəf action işlədilmir (CLAUDE.md → Dependency policy). Tag və onun commit SHA-sı
# mobile/flutter-version-dadır; klonlanan commit SHA ilə üst-üstə düşməsə, quraşdırma dayanır
# (tag sonradan başqa commit-ə yönəldilsə, səssizcə başqa kod işləməsin).
#
#   scripts/ci/install-flutter.sh [hədəf qovluq]     # default: $HOME/flutter
# GITHUB_PATH varsa, flutter/bin ora əlavə olunur.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
pin_file="$root/mobile/flutter-version"
dest="${1:-$HOME/flutter}"

read -r tag sha < <(grep -v '^#' "$pin_file" | awk 'NF {print $1, $2; exit}')
if [ -z "${tag:-}" ] || ! [[ "${sha:-}" =~ ^[0-9a-f]{40}$ ]]; then
  echo "XƏTA: $pin_file '<tag> <40 simvollu sha>' formatında deyil" >&2
  exit 1
fi

if [ -d "$dest/.git" ]; then
  echo "mövcud SDK: $dest"
else
  git clone --quiet --depth 1 --branch "$tag" https://github.com/flutter/flutter.git "$dest"
fi

actual="$(git -C "$dest" rev-parse HEAD)"
if [ "$actual" != "$sha" ]; then
  echo "XƏTA: flutter $tag gözlənilən $sha deyil, $actual-dır — pin-i yoxla" >&2
  exit 1
fi
echo "flutter $tag @ ${sha:0:12} ✓"

if [ -n "${GITHUB_PATH:-}" ]; then
  echo "$dest/bin" >> "$GITHUB_PATH"
fi
export PATH="$dest/bin:$PATH"
flutter config --no-analytics >/dev/null
dart --disable-analytics >/dev/null 2>&1 || true
flutter --version
