#!/usr/bin/env bash
# QONAQET — GitHub Project (v2) board-unun qurulması.
#
# Projects v2 YALNIZ GraphQL ilə idarə olunur və GITHUB_TOKEN ona yaza bilmir,
# ona görə bu skript `PROJECT_TOKEN` tələb edir (`repo` + `read:org` + `project` scope-ları).
#
# İdempotentdir: mövcud project, sahə və variantlar yenidən yaradılmır.
#
#   GH_TOKEN=$PROJECT_TOKEN REPO=QonaqE/qonaqet ./scripts/bootstrap-project.sh
set -euo pipefail

REPO="${REPO:-QonaqE/qonaqet}"
OWNER="${REPO%%/*}"
NAME="${REPO##*/}"
PROJECT_TITLE="${PROJECT_TITLE:-QONAQET}"
EPICS_DIR="${EPICS_DIR:-docs/epics}"

command -v gh >/dev/null || { echo "gh CLI tapılmadı"; exit 1; }
command -v jq >/dev/null || { echo "jq tapılmadı"; exit 1; }

if [ -z "${GH_TOKEN:-}" ]; then
  echo "XƏTA: GH_TOKEN boşdur. Projects v2 üçün PROJECT_TOKEN secret-i lazımdır." >&2
  echo "       Repo → Settings → Secrets and variables → Actions → PROJECT_TOKEN" >&2
  exit 1
fi

log()  { printf '%s\n' "$*"; }
step() { printf '\n\033[1m%s\033[0m\n' "$*"; }

gql() { gh api graphql "$@"; }

# ---------------------------------------------------------------- project
step "Project tapılır və ya yaradılır"

OWNER_ID="$(gql -f query='query($login:String!){ organization(login:$login){ id } }' \
  -F login="$OWNER" --jq '.data.organization.id' 2>/dev/null || true)"
IS_ORG=1
if [ -z "$OWNER_ID" ] || [ "$OWNER_ID" = "null" ]; then
  IS_ORG=0
  OWNER_ID="$(gql -f query='query($login:String!){ user(login:$login){ id } }' \
    -F login="$OWNER" --jq '.data.user.id')"
fi

if [ "$IS_ORG" = "1" ]; then
  PROJECTS_JSON="$(gql -f query='query($login:String!){
      organization(login:$login){ projectsV2(first:100){ nodes { id number title } } } }' \
    -F login="$OWNER" --jq '.data.organization.projectsV2.nodes')"
else
  PROJECTS_JSON="$(gql -f query='query($login:String!){
      user(login:$login){ projectsV2(first:100){ nodes { id number title } } } }' \
    -F login="$OWNER" --jq '.data.user.projectsV2.nodes')"
fi

PROJECT_ID="$(printf '%s' "$PROJECTS_JSON" | jq -r --arg t "$PROJECT_TITLE" \
  '.[] | select(.title == $t) | .id' | head -1)"
PROJECT_NUMBER="$(printf '%s' "$PROJECTS_JSON" | jq -r --arg t "$PROJECT_TITLE" \
  '.[] | select(.title == $t) | .number' | head -1)"

if [ -n "$PROJECT_ID" ] && [ "$PROJECT_ID" != "null" ]; then
  log "  mövcud project → #$PROJECT_NUMBER"
else
  CREATED="$(gql -f query='mutation($ownerId:ID!,$title:String!){
      createProjectV2(input:{ownerId:$ownerId, title:$title}){ projectV2 { id number } } }' \
    -F ownerId="$OWNER_ID" -F title="$PROJECT_TITLE" --jq '.data.createProjectV2.projectV2')"
  PROJECT_ID="$(printf '%s' "$CREATED" | jq -r .id)"
  PROJECT_NUMBER="$(printf '%s' "$CREATED" | jq -r .number)"
  log "  project yaradıldı → #$PROJECT_NUMBER"
fi

PROJECT_URL="https://github.com/orgs/${OWNER}/projects/${PROJECT_NUMBER}"
[ "$IS_ORG" = "1" ] || PROJECT_URL="https://github.com/users/${OWNER}/projects/${PROJECT_NUMBER}"

# repo-ya bağla
REPO_NODE_ID="$(gql -f query='query($owner:String!,$name:String!){
    repository(owner:$owner,name:$name){ id } }' \
  -F owner="$OWNER" -F name="$NAME" --jq '.data.repository.id')"
gql -f query='mutation($p:ID!,$r:ID!){ linkProjectV2ToRepository(input:{projectId:$p, repositoryId:$r}){ clientMutationId } }' \
  -F p="$PROJECT_ID" -F r="$REPO_NODE_ID" >/dev/null 2>&1 || true
log "  repo-ya bağlandı"

# ---------------------------------------------------------------- sahələr
step "Sahələr və variantlar"

fields_json() {
  gql -f query='query($p:ID!){ node(id:$p){ ... on ProjectV2 {
      fields(first:50){ nodes {
        ... on ProjectV2FieldCommon { id name dataType }
        ... on ProjectV2SingleSelectField { id name options { id name } }
      } } } } }' -F p="$PROJECT_ID" --jq '.data.node.fields.nodes'
}

FIELDS="$(fields_json)"
field_id()  { printf '%s' "$FIELDS" | jq -r --arg n "$1" '.[] | select(.name == $n) | .id' | head -1; }

# Status: default variantları əvəz edirik
STATUS_ID="$(field_id Status)"
if [ -n "$STATUS_ID" ] && [ "$STATUS_ID" != "null" ]; then
  gql -f query='mutation($f:ID!){ updateProjectV2Field(input:{fieldId:$f, singleSelectOptions:[
      {name:"Backlog",     color:GRAY,   description:"Hələ hazır deyil"},
      {name:"Ready",       color:BLUE,   description:"Götürülməyə hazırdır"},
      {name:"In Progress", color:YELLOW, description:"Üzərində işlənir"},
      {name:"In Review",   color:ORANGE, description:"PR açılıb, baxış gözləyir"},
      {name:"Done",        color:GREEN,  description:"Tamamlandı"}
    ]}){ clientMutationId } }' -F f="$STATUS_ID" >/dev/null
  log "  Status: Backlog → Ready → In Progress → In Review → Done"
fi

create_select() {  # name, options JSON massivi
  local name="$1" opts="$2"
  if [ -n "$(field_id "$name")" ] && [ "$(field_id "$name")" != "null" ]; then
    log "  $name — mövcuddur"
    return
  fi
  gh api graphql -f query="mutation(\$p:ID!){ createProjectV2Field(input:{
      projectId:\$p, dataType:SINGLE_SELECT, name:\"${name}\", singleSelectOptions:${opts}
    }){ clientMutationId } }" -F p="$PROJECT_ID" >/dev/null
  log "  $name yaradıldı"
}

if [ -z "$(field_id Epic)" ] || [ "$(field_id Epic)" = "null" ]; then
  gql -f query='mutation($p:ID!){ createProjectV2Field(input:{
      projectId:$p, dataType:TEXT, name:"Epic"}){ clientMutationId } }' -F p="$PROJECT_ID" >/dev/null
  log "  Epic yaradıldı"
else
  log "  Epic — mövcuddur"
fi

create_select "Area" '[
  {name:"backend",      color:BLUE,   description:".NET backend"},
  {name:"integration",  color:BLUE,   description:"Partnyor API"},
  {name:"mobile",       color:PURPLE, description:"Flutter"},
  {name:"web-landing",  color:PINK,   description:"Public sayt"},
  {name:"web-business", color:PINK,   description:"Biznes portalı"},
  {name:"web-admin",    color:PINK,   description:"Admin portalı"},
  {name:"infra",        color:GRAY,   description:"Repo, CI/CD, deploy"},
  {name:"design",       color:ORANGE, description:"Token-lər və brend"}
]'

create_select "Priority" '[
  {name:"p0", color:RED,    description:"Bloklayıcı"},
  {name:"p1", color:YELLOW, description:"Vacib"},
  {name:"p2", color:GREEN,  description:"Arzuolunan"}
]'

create_select "Size" '[
  {name:"XS", color:GRAY,   description:"Bir neçə saat"},
  {name:"S",  color:GREEN,  description:"Yarım gün"},
  {name:"M",  color:YELLOW, description:"1–2 gün"},
  {name:"L",  color:ORANGE, description:"3+ gün"}
]'

FIELDS="$(fields_json)"
opt_id() {  # field adı, variant adı
  printf '%s' "$FIELDS" | jq -r --arg f "$1" --arg o "$2" \
    '.[] | select(.name == $f) | .options[]? | select(.name == $o) | .id' | head -1
}

EPIC_FIELD="$(field_id Epic)"
AREA_FIELD="$(field_id Area)"
PRIO_FIELD="$(field_id Priority)"
SIZE_FIELD="$(field_id Size)"
STATUS_FIELD="$(field_id Status)"
BACKLOG_OPT="$(opt_id Status Backlog)"

# ---------------------------------------------------------------- elementlər
step "Issue-lar board-a əlavə olunur"

META="$(python3 scripts/lib/parse-epics.py "$EPICS_DIR" | jq -r '
  [ .[] | . as $e
    | [ { key: $e.id, epic: $e.id, area: $e.area, priority: $e.priority, size: "" } ]
      + [ $e.tasks[] | { key: .key, epic: $e.id, area: .area, priority: .priority, size: .size } ]
  ] | flatten | map({(.key): .}) | add')"

EXISTING_ITEMS="$(gql -f query='query($p:ID!){ node(id:$p){ ... on ProjectV2 {
    items(first:100){ nodes { id content { ... on Issue { number } } } } } } }' \
  -F p="$PROJECT_ID" --jq '.data.node.items.nodes' || echo '[]')"

ADDED=0; UPDATED=0
while IFS=$'\t' read -r NUM NODE_ID TITLE; do
  [ -z "${NUM:-}" ] && continue
  KEY="$(printf '%s' "$TITLE" | sed -n 's/^\[\([A-Z0-9-]*\)\].*/\1/p')"
  [ -z "$KEY" ] && continue
  ROW="$(printf '%s' "$META" | jq -r --arg k "$KEY" '.[$k] // empty')"
  [ -z "$ROW" ] && continue

  ITEM_ID="$(printf '%s' "$EXISTING_ITEMS" | jq -r --argjson n "$NUM" \
    '.[] | select(.content.number == $n) | .id' | head -1)"
  if [ -z "$ITEM_ID" ] || [ "$ITEM_ID" = "null" ]; then
    ITEM_ID="$(gql -f query='mutation($p:ID!,$c:ID!){ addProjectV2ItemById(input:{projectId:$p, contentId:$c}){ item { id } } }' \
      -F p="$PROJECT_ID" -F c="$NODE_ID" --jq '.data.addProjectV2ItemById.item.id')"
    ADDED=$((ADDED+1))
  fi

  set_text() {
    gql -f query='mutation($p:ID!,$i:ID!,$f:ID!,$v:String!){ updateProjectV2ItemFieldValue(
        input:{projectId:$p, itemId:$i, fieldId:$f, value:{text:$v}}){ clientMutationId } }' \
      -F p="$PROJECT_ID" -F i="$ITEM_ID" -F f="$1" -F v="$2" >/dev/null
  }
  set_opt() {
    [ -z "${2:-}" ] && return 0
    gql -f query='mutation($p:ID!,$i:ID!,$f:ID!,$v:String!){ updateProjectV2ItemFieldValue(
        input:{projectId:$p, itemId:$i, fieldId:$f, value:{singleSelectOptionId:$v}}){ clientMutationId } }' \
      -F p="$PROJECT_ID" -F i="$ITEM_ID" -F f="$1" -F v="$2" >/dev/null
  }

  set_text "$EPIC_FIELD" "$(printf '%s' "$ROW" | jq -r .epic)"
  set_opt  "$AREA_FIELD" "$(opt_id Area     "$(printf '%s' "$ROW" | jq -r .area)")"
  set_opt  "$PRIO_FIELD" "$(opt_id Priority "$(printf '%s' "$ROW" | jq -r .priority)")"
  SZ="$(printf '%s' "$ROW" | jq -r .size)"
  [ -n "$SZ" ] && set_opt "$SIZE_FIELD" "$(opt_id Size "$SZ")"
  set_opt "$STATUS_FIELD" "$BACKLOG_OPT"

  UPDATED=$((UPDATED+1))
  log "  #${NUM}  ${TITLE}"
done < <(
  page=1
  while :; do
    chunk="$(gh api "repos/${REPO}/issues?state=all&per_page=100&page=${page}")" || break
    n="$(printf '%s' "$chunk" | jq 'length')"
    [ "$n" -eq 0 ] && break
    printf '%s' "$chunk" | jq -r '.[] | select(.pull_request | not)
      | [.number, .node_id, .title] | @tsv'
    page=$((page+1))
    [ "$page" -gt 50 ] && break
  done
)

step "Nəticə"
log "  board: $PROJECT_URL"
log "  əlavə olunan: $ADDED, sahələri yenilənən: $UPDATED"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "## QONAQET board"
    echo ""
    echo "- Board: [$PROJECT_TITLE]($PROJECT_URL)"
    echo "- Əlavə olunan element: **$ADDED**"
    echo "- Sahələri yenilənən: **$UPDATED**"
  } >> "$GITHUB_STEP_SUMMARY"
fi
