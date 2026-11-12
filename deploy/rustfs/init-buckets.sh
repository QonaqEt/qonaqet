#!/bin/sh
# RustFS bucket-ini yaradır və versioning-i açır (CLAUDE.md: "immutable keys, versioning on, never removed").
# İdempotentdir: mövcud bucket-ə toxunmur, heç vaxt heç nə silmir.
# Giriş: AWS_ENDPOINT_URL, AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, STORAGE_BUCKET (vergüllə bir neçə ad ola bilər).
set -eu

: "${STORAGE_BUCKET:?STORAGE_BUCKET təyin edilməyib}"

for bucket in $(printf '%s' "$STORAGE_BUCKET" | tr ',' ' '); do
  if aws s3api head-bucket --bucket "$bucket" >/dev/null 2>&1; then
    echo "bucket mövcuddur: $bucket"
  else
    aws s3api create-bucket --bucket "$bucket" >/dev/null
    echo "bucket yaradıldı: $bucket"
  fi

  aws s3api put-bucket-versioning --bucket "$bucket" \
    --versioning-configuration Status=Enabled

  status="$(aws s3api get-bucket-versioning --bucket "$bucket" --query Status --output text)"
  if [ "$status" != "Enabled" ]; then
    echo "XƏTA: $bucket üçün versioning açılmadı (status: $status)" >&2
    exit 1
  fi
  echo "versioning açıqdır: $bucket"
done
