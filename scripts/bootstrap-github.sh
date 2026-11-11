#!/usr/bin/env bash
# QONAQET — GitHub bootstrap (label-lar, epic issue-ları, task sub-issue-ları).
#
# Yalnız REST işlədir, ona görə həm lokal, həm Actions daxilində işləyir.
# Board ayrıca qurulur: scripts/bootstrap-project.sh (GraphQL + PROJECT_TOKEN).
#
# İdempotentdir: mövcud issue-lar başlıq prefiksinə görə tapılır, yenidən yaradılmır.
# GitHub-ın ikincili limitinə görə yaradılış çağırışları arasında fasilə verilir.
#
#   REPO=QonaqE/qonaqet ./scripts/bootstrap-github.sh
#   DRY_RUN=1 ./scripts/bootstrap-github.sh
#   RATE_SLEEP=2 ./scripts/bootstrap-github.sh     # limitə dəyirsə fasiləni artır
set -euo pipefail

REPO="${REPO:-QonaqE/qonaqet}"
EPICS_DIR="${EPICS_DIR:-docs/epics}"
DRY_RUN="${DRY_RUN:-0}"
RATE_SLEEP="${RATE_SLEEP:-1}"
REPO_URL="https://github.com/${REPO}"

command -v gh >/dev/null || { echo "gh CLI tapılmadı"; exit 1; }
command -v python3 >/dev/null || { echo "python3 tapılmadı"; exit 1; }

log()  { printf '%s\n' "$*"; }
step() { printf '\n\033[1m%s\033[0m\n' "$*"; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# GitHub ikincili limitə dəyəndə geri çəkilib yenidən cəhd edir.
api() {
  local attempt=1 out rc
  while :; do
    if out="$(gh api "$@" 2>&1)"; then printf '%s' "$out"; return 0; fi
    rc=$?
    if printf '%s' "$out" | grep -qiE 'secondary rate limit|rate limit exceeded|abuse detection'; then
      local wait=$(( attempt * 30 ))
      log "  ⏳ limit — ${wait}s gözlənilir (cəhd $attempt)" >&2
      sleep "$wait"
      attempt=$((attempt+1))
      [ "$attempt" -le 5 ] && continue
    fi
    printf '%s' "$out" >&2
    return "$rc"
  done
}

# ---------------------------------------------------------------- label-lar
step "Label-lar"
LABELS=$(cat <<'EOF'
epic|6F42C1|Böyük iş bloku; task-lar onun sub-issue-larıdır
task|0E8A16|Bir PR ilə bağlanan konkret iş
bug|D73A4A|Səhv davranış
area:backend|1D76DB|.NET backend
area:integration|1D76DB|Partnyor API və webhook-lar
area:mobile|1D76DB|Flutter tətbiqi
area:web-landing|1D76DB|Public sayt
area:web-business|1D76DB|Biznes portalı
area:web-admin|1D76DB|Admin və korporativ portal
area:infra|1D76DB|Repo, CI/CD, deploy
area:design|1D76DB|Design token-lər və brend
p0|B60205|Bloklayıcı — MVP üçün vacib
p1|FBCA04|Vacib, amma bloklamır
p2|0E8A16|Arzuolunan
parallel-ok|C5DEF5|Epic sırasını gözləmədən götürülə bilər
blocked-ui-guidelines|E99695|Angular UI guidelines gələnə qədər başlanmır
dependency-approved|5319E7|Yeni asılılıq repo sahibi tərəfindən təsdiqlənib
EOF
)
LABEL_N=0
while IFS='|' read -r name color desc; do
  [ -z "${name:-}" ] && continue
  if [ "$DRY_RUN" = "1" ]; then
    log "  [dry-run] $name"
  else
    gh label create "$name" --repo "$REPO" --color "$color" --description "$desc" --force >/dev/null
    log "  $name"
  fi
  LABEL_N=$((LABEL_N+1))
done <<< "$LABELS"

# ---------------------------------------------------------------- payload-lar
step "Epic sənədləri oxunur"
python3 scripts/lib/build-payloads.py "$EPICS_DIR" "$TMP" "$REPO_URL"

# ---------------------------------------------------------------- mövcudlar
step "Mövcud issue-lar yüklənir"
api --paginate "repos/${REPO}/issues?state=all&per_page=100" \
  --jq '.[] | select(.pull_request | not) | "\(.title)\t\(.number)\t\(.id)\t\(.state)"' \
  > "$TMP/existing.tsv" || : > "$TMP/existing.tsv"
log "  $(grep -c . < "$TMP/existing.tsv" || true) issue"

# find_issue "<KEY>" → "number<TAB>id<TAB>state" və ya boş
find_issue() {
  awk -F'\t' -v p="[$1]" 'index($1, p) == 1 { print $2 "\t" $3 "\t" $4; exit }' "$TMP/existing.tsv"
}

CREATED_E=0; REUSED_E=0; CREATED_T=0; REUSED_T=0; LINKED=0; CLOSED=0
: > "$TMP/numbers.tsv"   # KEY <TAB> number <TAB> id
: > "$TMP/summary.md"

# ---------------------------------------------------------------- issue-lar
step "Epic-lər və task-lar"
while IFS=$'\t' read -r KIND KEY EPIC_ID TITLE DONE; do
  [ -z "${KIND:-}" ] && continue
  HIT="$(find_issue "$KEY")"

  if [ -n "$HIT" ]; then
    NUM="$(printf '%s' "$HIT" | cut -f1)"
    ID="$(printf '%s' "$HIT" | cut -f2)"
    STATE="$(printf '%s' "$HIT" | cut -f3)"
    [ "$KIND" = "epic" ] && REUSED_E=$((REUSED_E+1)) || REUSED_T=$((REUSED_T+1))
    log "  = #${NUM}  ${TITLE}"
  elif [ "$DRY_RUN" = "1" ]; then
    log "  + [dry-run] ${TITLE}"
    continue
  else
    RESP="$(api -X POST "repos/${REPO}/issues" --input "$TMP/payload/${KEY}.json")"
    NUM="$(printf '%s' "$RESP" | python3 -c 'import json,sys; print(json.load(sys.stdin)["number"])')"
    ID="$(printf '%s' "$RESP" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')"
    STATE="open"
    [ "$KIND" = "epic" ] && CREATED_E=$((CREATED_E+1)) || CREATED_T=$((CREATED_T+1))
    log "  + #${NUM}  ${TITLE}"
    sleep "$RATE_SLEEP"
  fi

  printf '%s\t%s\t%s\t%s\n' "$KEY" "$NUM" "$ID" "$STATE" >> "$TMP/numbers.tsv"
  [ "$KIND" = "epic" ] && printf -- '- %s → %s/issues/%s\n' "$TITLE" "$REPO_URL" "$NUM" >> "$TMP/summary.md"
done < "$TMP/index.tsv"

[ "$DRY_RUN" = "1" ] && { step "Dry-run bitdi"; exit 0; }

# ---------------------------------------------------------------- sub-issue-lar
step "Sub-issue əlaqələri"
num_of() { awk -F'\t' -v k="$1" '$1 == k { print $2; exit }' "$TMP/numbers.tsv"; }
id_of()  { awk -F'\t' -v k="$1" '$1 == k { print $3; exit }' "$TMP/numbers.tsv"; }

CUR_EPIC=""
while IFS=$'\t' read -r KIND KEY EPIC_ID TITLE DONE; do
  [ "$KIND" != "task" ] && continue
  ENUM="$(num_of "$EPIC_ID")"
  [ -z "$ENUM" ] && continue

  if [ "$CUR_EPIC" != "$EPIC_ID" ]; then
    CUR_EPIC="$EPIC_ID"
    api "repos/${REPO}/issues/${ENUM}/sub_issues?per_page=100" --jq '.[].number' \
      > "$TMP/subs.txt" 2>/dev/null || : > "$TMP/subs.txt"
    log "  ${EPIC_ID} (#${ENUM}) — mövcud sub-issue: $(grep -c . < "$TMP/subs.txt" || true)"
  fi

  TNUM="$(num_of "$KEY")"; TID="$(id_of "$KEY")"
  if ! grep -qx "$TNUM" "$TMP/subs.txt" 2>/dev/null; then
    if api -X POST "repos/${REPO}/issues/${ENUM}/sub_issues" -F sub_issue_id="$TID" >/dev/null 2>&1; then
      LINKED=$((LINKED+1))
      sleep "$RATE_SLEEP"
    else
      log "    ⚠ #${TNUM} bağlanmadı"
    fi
  fi
done < "$TMP/index.tsv"

# ---------------------------------------------------------------- bağlananlar
step "Artıq görülmüş task-lar"
while IFS=$'\t' read -r KIND KEY EPIC_ID TITLE DONE; do
  [ "$DONE" != "1" ] && continue
  TNUM="$(num_of "$KEY")"
  STATE="$(awk -F'\t' -v k="$KEY" '$1 == k { print $4; exit }' "$TMP/numbers.tsv")"
  [ "$STATE" != "open" ] && continue
  gh issue close "$TNUM" --repo "$REPO" \
    --comment "Bu task artıq yerinə yetirilib (epic sənədində \`done: true\`)." >/dev/null
  CLOSED=$((CLOSED+1))
  log "  ✓ #${TNUM} bağlandı — ${TITLE}"
  sleep "$RATE_SLEEP"
done < "$TMP/index.tsv"

# ---------------------------------------------------------------- nəticə
step "Nəticə"
log "  label: $LABEL_N"
log "  epic:  $CREATED_E yaradıldı, $REUSED_E mövcud idi"
log "  task:  $CREATED_T yaradıldı, $REUSED_T mövcud idi"
log "  sub-issue əlaqəsi: $LINKED"
log "  bağlanan: $CLOSED"
log ""
cat "$TMP/summary.md"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "## QONAQET bootstrap"
    echo ""
    echo "| | |"
    echo "|---|---|"
    echo "| label | $LABEL_N |"
    echo "| epic | **$CREATED_E** yaradıldı, $REUSED_E mövcud |"
    echo "| task | **$CREATED_T** yaradıldı, $REUSED_T mövcud |"
    echo "| sub-issue əlaqəsi | $LINKED |"
    echo "| bağlanan | $CLOSED |"
    echo ""
    echo "### Epic-lər"
    cat "$TMP/summary.md"
  } >> "$GITHUB_STEP_SUMMARY"
fi
