#!/usr/bin/env bash
# QONAQET — GitHub bootstrap (label-lar, epic issue-ları, task sub-issue-ları).
#
# Yalnız REST işlədir, ona görə həm lokal, həm Actions daxilində işləyir.
# Project (v2) board-u ayrıca qurulur: scripts/bootstrap-project.sh (GraphQL + PROJECT_TOKEN).
#
# İdempotentdir: ikinci dəfə işləyəndə dublikat yaratmır, mövcudları tapıb davam edir.
#
#   REPO=QonaqE/qonaqet ./scripts/bootstrap-github.sh
#   DRY_RUN=1 ./scripts/bootstrap-github.sh      # heç nə yaratmadan nə olacağını göstərir
set -euo pipefail

REPO="${REPO:-QonaqE/qonaqet}"
EPICS_DIR="${EPICS_DIR:-docs/epics}"
DRY_RUN="${DRY_RUN:-0}"
REPO_URL="https://github.com/${REPO}"

command -v gh >/dev/null || { echo "gh CLI tapılmadı"; exit 1; }
command -v python3 >/dev/null || { echo "python3 tapılmadı"; exit 1; }

log()  { printf '%s\n' "$*"; }
step() { printf '\n\033[1m%s\033[0m\n' "$*"; }
run()  { if [ "$DRY_RUN" = "1" ]; then log "  [dry-run] $*"; else "$@"; fi; }

# ---------------------------------------------------------------- label-lar
step "Label-lar"

# ad|rəng|təsvir
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

while IFS='|' read -r name color desc; do
  [ -z "${name:-}" ] && continue
  log "  $name"
  run gh label create "$name" --repo "$REPO" --color "$color" --description "$desc" --force
done <<< "$LABELS"

# ---------------------------------------------------------------- epic-lər
step "Epic sənədləri oxunur"
EPICS_JSON="$(python3 scripts/lib/parse-epics.py "$EPICS_DIR")"
EPIC_COUNT=$(printf '%s' "$EPICS_JSON" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)))')
TASK_COUNT=$(printf '%s' "$EPICS_JSON" | python3 -c 'import json,sys; print(sum(len(e["tasks"]) for e in json.load(sys.stdin)))')
log "  $EPIC_COUNT epic, $TASK_COUNT task"

# Mövcud issue-ları bir dəfə yükləyirik (başlıq → nömrə, başlıq → id).
step "Mövcud issue-lar yüklənir"
EXISTING="$(gh api --paginate "repos/${REPO}/issues?state=all&per_page=100" \
  --jq '.[] | select(.pull_request | not) | "\(.number)\t\(.id)\t\(.title)"' || true)"
log "  $(printf '%s' "$EXISTING" | grep -c . || true) issue tapıldı"

# find_issue "<başlıq prefiksi>" → "number<TAB>id" və ya boş
find_issue() {
  printf '%s\n' "$EXISTING" | awk -F'\t' -v p="$1" 'index($3, p) == 1 { print $1 "\t" $2; exit }'
}

remember_issue() {  # number id title
  EXISTING="$(printf '%s\n%s\t%s\t%s' "$EXISTING" "$1" "$2" "$3")"
}

CREATED_EPICS=0; REUSED_EPICS=0; CREATED_TASKS=0; REUSED_TASKS=0; LINKED=0; CLOSED=0
SUMMARY=""

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
printf '%s' "$EPICS_JSON" > "$TMP/epics.json"

EPIC_IDS=$(python3 -c 'import json,sys; print("\n".join(e["id"] for e in json.load(open(sys.argv[1]))))' "$TMP/epics.json")

for EID in $EPIC_IDS; do
  python3 - "$TMP/epics.json" "$EID" "$TMP" "$REPO_URL" <<'PY'
import json, sys, pathlib
epics, eid, tmp, repo_url = json.load(open(sys.argv[1])), sys.argv[2], pathlib.Path(sys.argv[3]), sys.argv[4]
e = next(x for x in epics if x["id"] == eid)
body = f"""## Goal

{e['goal']}

## Scope

{e['scope']}

## Out of scope

{e['out_of_scope']}

## Acceptance criteria

{e['acceptance']}

---

Task-lar bu epic-in **sub-issue**-larıdır.
Mənbə: [`{e['file']}`]({repo_url}/blob/main/{e['file']}) · Qaydalar: [`CLAUDE.md`]({repo_url}/blob/main/CLAUDE.md)
"""
(tmp / "epic-body.md").write_text(body, encoding="utf-8")
(tmp / "epic-title.txt").write_text(f"[{e['id']}] {e['title']}", encoding="utf-8")
(tmp / "epic-area.txt").write_text(e["area"], encoding="utf-8")
(tmp / "epic-prio.txt").write_text(e["priority"], encoding="utf-8")
(tmp / "task-keys.txt").write_text("\n".join(t["key"] for t in e["tasks"]), encoding="utf-8")
PY

  ETITLE="$(cat "$TMP/epic-title.txt")"
  EAREA="$(cat "$TMP/epic-area.txt")"
  EPRIO="$(cat "$TMP/epic-prio.txt")"

  step "$ETITLE"

  HIT="$(find_issue "[${EID}]")"
  if [ -n "$HIT" ]; then
    ENUM="$(printf '%s' "$HIT" | cut -f1)"
    log "  epic artıq var → #$ENUM"
    REUSED_EPICS=$((REUSED_EPICS+1))
  elif [ "$DRY_RUN" = "1" ]; then
    log "  [dry-run] epic yaradılacaq: $ETITLE"
    ENUM="DRY"
  else
    ENUM="$(gh issue create --repo "$REPO" \
      --title "$ETITLE" \
      --body-file "$TMP/epic-body.md" \
      --label epic --label "area:${EAREA}" --label "$EPRIO" \
      | sed 's#.*/##')"
    log "  epic yaradıldı → #$ENUM"
    CREATED_EPICS=$((CREATED_EPICS+1))
    EID_NUM="$(gh api "repos/${REPO}/issues/${ENUM}" --jq .id)"
    remember_issue "$ENUM" "$EID_NUM" "$ETITLE"
  fi
  SUMMARY="${SUMMARY}
- ${ETITLE} → ${REPO_URL}/issues/${ENUM}"

  # mövcud sub-issue-lar (təkrar bağlamamaq üçün)
  SUBS=""
  if [ "$ENUM" != "DRY" ]; then
    SUBS="$(gh api "repos/${REPO}/issues/${ENUM}/sub_issues?per_page=100" --jq '.[].number' 2>/dev/null || true)"
  fi

  while IFS= read -r TKEY; do
    [ -z "$TKEY" ] && continue
    python3 - "$TMP/epics.json" "$EID" "$TKEY" "$TMP" "$REPO_URL" "$ENUM" <<'PY'
import json, sys, pathlib
epics, eid, tkey, tmp, repo_url, enum = (json.load(open(sys.argv[1])), sys.argv[2], sys.argv[3],
                                         pathlib.Path(sys.argv[4]), sys.argv[5], sys.argv[6])
e = next(x for x in epics if x["id"] == eid)
t = next(x for x in e["tasks"] if x["key"] == tkey)
body = f"""{t['body']}

---

**Valideyn epic:** #{enum} — {e['title']}
**Size:** {t['size']}

Mənbə: [`{e['file']}`]({repo_url}/blob/main/{e['file']}) · Qaydalar: [`CLAUDE.md`]({repo_url}/blob/main/CLAUDE.md)
"""
(tmp / "task-body.md").write_text(body, encoding="utf-8")
(tmp / "task-title.txt").write_text(f"[{t['key']}] {t['title']}", encoding="utf-8")
labels = ["task", f"area:{t['area']}", t["priority"]] + t["extra_labels"]
(tmp / "task-labels.txt").write_text("\n".join(labels), encoding="utf-8")
(tmp / "task-done.txt").write_text("1" if t["done"] else "0", encoding="utf-8")
PY

    TTITLE="$(cat "$TMP/task-title.txt")"
    THIT="$(find_issue "[${TKEY}]")"

    if [ -n "$THIT" ]; then
      TNUM="$(printf '%s' "$THIT" | cut -f1)"
      TID="$(printf '%s' "$THIT" | cut -f2)"
      REUSED_TASKS=$((REUSED_TASKS+1))
    elif [ "$DRY_RUN" = "1" ]; then
      log "  [dry-run] task: $TTITLE"
      continue
    else
      LABEL_ARGS=()
      while IFS= read -r L; do [ -n "$L" ] && LABEL_ARGS+=(--label "$L"); done < "$TMP/task-labels.txt"
      TNUM="$(gh issue create --repo "$REPO" \
        --title "$TTITLE" --body-file "$TMP/task-body.md" "${LABEL_ARGS[@]}" | sed 's#.*/##')"
      TID="$(gh api "repos/${REPO}/issues/${TNUM}" --jq .id)"
      CREATED_TASKS=$((CREATED_TASKS+1))
      remember_issue "$TNUM" "$TID" "$TTITLE"
    fi

    # sub-issue əlaqəsi
    if ! printf '%s\n' "$SUBS" | grep -qx "$TNUM"; then
      if gh api -X POST "repos/${REPO}/issues/${ENUM}/sub_issues" \
           -F sub_issue_id="$TID" >/dev/null 2>&1; then
        LINKED=$((LINKED+1))
      fi
    fi

    # artıq görülmüş iş
    if [ "$(cat "$TMP/task-done.txt")" = "1" ]; then
      STATE="$(gh api "repos/${REPO}/issues/${TNUM}" --jq .state)"
      if [ "$STATE" = "open" ]; then
        gh issue close "$TNUM" --repo "$REPO" \
          --comment "Bu task artıq yerinə yetirilib (epic sənədində \`done: true\`)." >/dev/null
        CLOSED=$((CLOSED+1))
      fi
    fi

    log "  #${TNUM}  ${TTITLE}"
  done < "$TMP/task-keys.txt"
done

step "Nəticə"
log "  epic:  $CREATED_EPICS yaradıldı, $REUSED_EPICS mövcud idi"
log "  task:  $CREATED_TASKS yaradıldı, $REUSED_TASKS mövcud idi"
log "  sub-issue əlaqəsi: $LINKED"
log "  artıq görülmüş olduğu üçün bağlanan: $CLOSED"
log ""
log "Epic-lər:${SUMMARY}"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "## QONAQET bootstrap"
    echo ""
    echo "- epic: **$CREATED_EPICS** yaradıldı, $REUSED_EPICS mövcud idi"
    echo "- task: **$CREATED_TASKS** yaradıldı, $REUSED_TASKS mövcud idi"
    echo "- sub-issue əlaqəsi: $LINKED"
    echo "- bağlanan (artıq görülmüş): $CLOSED"
    echo ""
    echo "### Epic-lər"
    echo "$SUMMARY"
  } >> "$GITHUB_STEP_SUMMARY"
fi
