#!/usr/bin/env bash
# Dev stack-in real smoke testi: infrastrukturu qaldırır, healthcheck-ləri gözləyir və
# E00-T02-nin iddialarını yoxlayır (Mongo tranzaksiyası, bucket versioning-i, init-in idempotentliyi).
#
#   deploy/tests/smoke.sh          # qaldır → yoxla → söndür (volume-lar daxil)
#   KEEP=1 deploy/tests/smoke.sh   # yoxlamadan sonra stack-i işlək saxla
set -euo pipefail

cd "$(dirname "$0")/.."
export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-qonaqet-smoke}"
dc() { docker compose -f docker-compose.yml "$@"; }

cleanup() {
  code=$?
  if [ "$code" -ne 0 ]; then
    echo "::group::konteyner logları"
    dc ps -a || true
    dc logs --tail 60 || true
    echo "::endgroup::"
  fi
  # Yalnız bu testin öz müvəqqəti layihəsi (qonaqet-smoke) yığışdırılır.
  [ "${KEEP:-0}" = "1" ] || dc down --volumes --remove-orphans >/dev/null 2>&1 || true
  exit "$code"
}
trap cleanup EXIT

step() { printf '\n== %s\n' "$*"; }
fail() { echo "XƏTA: $*" >&2; exit 1; }

step "stack qalxır (healthcheck-lər gözlənilir)"
dc up -d --wait --wait-timeout 300

step "rustfs-init uğurla bitib"
[ "$(dc ps -a --format '{{.ExitCode}}' rustfs-init)" = "0" ] || fail "rustfs-init sıfırdan fərqli kodla bitdi"

step "Mongo: replica set primary-dir və tranzaksiya commit olunur"
dc exec -T mongo mongosh --quiet --eval '
  // KEEP=1 ilə təkrar işə düşə bilsin deyə hər qaçışın öz run id-si var.
  const run = new ObjectId().toString();
  const s = db.getMongo().startSession();
  const c = s.getDatabase("smoke").getCollection("tx");
  s.startTransaction();
  c.insertOne({ run: run, n: 1, isDeleted: false });
  c.insertOne({ run: run, n: 2, isDeleted: false });
  s.commitTransaction();
  const n = db.getSiblingDB("smoke").tx.countDocuments({ run: run, isDeleted: false });
  if (n !== 2) { print("gözlənilən 2, alınan " + n); quit(1); }
  print("tranzaksiya OK");
'

step "Postgres sorğu qəbul edir"
# Qeyd: `| grep -q` işlədilmir — pipefail ilə SIGPIPE (141) verir; çıxış dəyişənə yığılır.
out="$(dc exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc "select 1"' | tr -d '\r')"
[ "$out" = "1" ] || fail "postgres cavabı: '$out'"

step "Valkey cavab verir"
out="$(dc exec -T valkey valkey-cli ping | tr -d '\r')"
[ "$out" = "PONG" ] || fail "valkey cavabı: '$out'"

step "RabbitMQ işləkdir"
dc exec -T rabbitmq rabbitmq-diagnostics -q check_running

aws_cli() { dc run --rm -T --no-deps --entrypoint aws rustfs-init "$@"; }
bucket="$(dc config --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)["services"]["rustfs-init"]["environment"]["STORAGE_BUCKET"].split(",")[0])')"

step "RustFS: '$bucket' bucket-i var və versioning açıqdır"
status="$(aws_cli s3api get-bucket-versioning --bucket "$bucket" --query Status --output text | tr -d '\r')"
[ "$status" = "Enabled" ] || fail "versioning statusu: '$status'"

step "RustFS: eyni açara iki yazı iki versiya saxlayır"
key="smoke/versioned-$(date +%s)-$$.txt"
for body in bir iki; do
  printf '%s' "$body" | aws_cli s3 cp - "s3://$bucket/$key" >/dev/null
done
versions="$(aws_cli s3api list-object-versions --bucket "$bucket" --prefix "$key" \
  --query 'length(Versions)' --output text | tr -d '\r')"
[ "$versions" = "2" ] || fail "gözlənilən 2 versiya, alınan '$versions'"

step "rustfs-init idempotentdir (ikinci işə düşmə)"
out="$(dc run --rm -T --no-deps rustfs-init)"
printf '%s\n' "$out"
case "$out" in
  *"bucket mövcuddur: $bucket"*) ;;
  *) fail "ikinci işə düşmə mövcud bucket-i tanımadı" ;;
esac

step "Mailpit və Aspire Dashboard HTTP-yə cavab verir"
python3 - <<'PY'
import json, subprocess, sys, time, urllib.request

cfg = json.loads(subprocess.run(
    ["docker", "compose", "-f", "docker-compose.yml", "config", "--format", "json"],
    check=True, capture_output=True, text=True).stdout)["services"]

def host_port(service, target):
    return next(p["published"] for p in cfg[service]["ports"] if p["target"] == target)

for name, url in (
    ("mailpit", f"http://127.0.0.1:{host_port('mailpit', 8025)}/readyz"),
    ("aspire-dashboard", f"http://127.0.0.1:{host_port('aspire-dashboard', 18888)}/"),
):
    for attempt in range(30):
        try:
            with urllib.request.urlopen(url, timeout=5) as r:
                if r.status == 200:
                    print(f"{name} OK")
                    break
        except Exception as e:  # noqa: BLE001 — retry
            last = e
        time.sleep(2)
    else:
        sys.exit(f"{name} cavab vermədi: {url} ({last})")
PY

step "hamısı yaşıldır"
dc ps
