#!/usr/bin/env python3
"""QONAQET project-sync — built-in Project workflow-larının etmədiyi board sinxronu (E00-T10).

    project-sync.py issue <n>        # issue-nu board-a əlavə et (yoxdursa) və sahələrini doldur
    project-sync.py branch <ref>     # `feat|fix|chore/<n>-<slug>` → task-ı In Progress-ə keçir
    project-sync.py backfill         # bütün issue-lar üçün `issue` (idempotent)

Mühit: GH_TOKEN (PROJECT_TOKEN), REPO, PROJECT_TITLE (default QONAQET). Yalnız Python stdlib.

Sahələr:
  Epic      — epic-in öz açarı, task üçün valideyn epic-in açarı (`[E00] ...` başlığından)
  Area      — `area:*` label-ı (prefikssiz)
  Priority  — `p0|p1|p2` label-ı
  Size      — issue body-sindəki `**Size:** M` sətri
Status-a yalnız `branch` toxunur və yalnız irəli: Backlog/Ready/boş → In Progress.
"""
from __future__ import annotations

import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from typing import Any, Callable

API = "https://api.github.com"
KEY = re.compile(r"^\[(E\d{2})(?:-T\d{2})?\]")
SIZE = re.compile(r"^\*\*Size:\*\*\s*(XS|S|M|L)\s*$", re.MULTILINE)
BRANCH = re.compile(r"^(?:refs/heads/)?(?:feat|fix|chore)/(\d+)-[a-z0-9][a-z0-9._-]*$")
ADVANCEABLE = {None, "Backlog", "Ready"}

Rest = Callable[[str], Any]                 # path → JSON | None (404)
Gql = Callable[[str, dict], dict]           # (query, variables) → data


class SyncError(Exception):
    pass


# ------------------------------------------------------------ HTTP
def _request(url: str, token: str, body: dict | None = None) -> Any:
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method="POST" if data else "GET", headers={
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "Content-Type": "application/json",
    })
    for attempt in range(4):
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                return json.load(resp)
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            if error.code in (403, 429, 502, 503) and attempt < 3:  # ikincili limit / keçici xəta
                time.sleep(2 ** attempt * 5)
                continue
            raise SyncError(f"HTTP {error.code} {url}: {error.read()[:300]!r}") from error
    raise SyncError(f"{url}: təkrar cəhdlər bitdi")


def http_clients(repo: str, token: str) -> tuple[Rest, Gql]:
    def rest(path: str) -> Any:
        return _request(f"{API}/repos/{repo}/{path}", token)

    def gql(query: str, variables: dict) -> dict:
        result = _request(f"{API}/graphql", token, {"query": query, "variables": variables})
        if result is None or result.get("errors"):
            raise SyncError(f"GraphQL: {result and result.get('errors')}")
        return result["data"]

    return rest, gql


# ------------------------------------------------------------ board
Q_PROJECTS = """query($login:String!){ organization(login:$login){ projectsV2(first:100){ nodes { id title } } } }"""
Q_FIELDS = """query($p:ID!){ node(id:$p){ ... on ProjectV2 { fields(first:50){ nodes {
  ... on ProjectV2FieldCommon { id name }
  ... on ProjectV2SingleSelectField { id name options { id name } } } } } } }"""
M_ADD = """mutation($p:ID!,$c:ID!){ addProjectV2ItemById(input:{projectId:$p, contentId:$c}){ item { id } } }"""
Q_STATUS = """query($i:ID!){ node(id:$i){ ... on ProjectV2Item {
  fieldValueByName(name:"Status"){ ... on ProjectV2ItemFieldSingleSelectValue { name } } } } }"""
M_TEXT = """mutation($p:ID!,$i:ID!,$f:ID!,$v:String!){ updateProjectV2ItemFieldValue(
  input:{projectId:$p, itemId:$i, fieldId:$f, value:{text:$v}}){ clientMutationId } }"""
M_OPTION = """mutation($p:ID!,$i:ID!,$f:ID!,$v:String!){ updateProjectV2ItemFieldValue(
  input:{projectId:$p, itemId:$i, fieldId:$f, value:{singleSelectOptionId:$v}}){ clientMutationId } }"""


class Board:
    def __init__(self, owner: str, title: str, rest: Rest, gql: Gql, log: Callable[[str], None] = print) -> None:
        self.rest, self.gql, self.log = rest, gql, log
        projects = gql(Q_PROJECTS, {"login": owner})["organization"]["projectsV2"]["nodes"]
        match = [p for p in projects if p["title"] == title]
        if not match:
            raise SyncError(f"'{title}' project-i {owner} təşkilatında tapılmadı")
        self.project = match[0]["id"]
        self.fields = {f["name"]: f for f in gql(Q_FIELDS, {"p": self.project})["node"]["fields"]["nodes"] if f}

    def _option(self, field: str, name: str) -> str | None:
        for option in self.fields.get(field, {}).get("options", []):
            if option["name"] == name:
                return option["id"]
        return None

    def _item(self, issue: dict) -> str:
        # addProjectV2ItemById mövcud elementi qaytarır — həm "əlavə et", həm "tap" kimi idempotentdir.
        return self.gql(M_ADD, {"p": self.project, "c": issue["node_id"]})["addProjectV2ItemById"]["item"]["id"]

    def _set_option(self, item: str, field: str, value: str | None) -> bool:
        if not value or field not in self.fields:
            return False
        option = self._option(field, value)
        if option is None:
            self.log(f"  ⚠ {field}: '{value}' variantı board-da yoxdur")
            return False
        self.gql(M_OPTION, {"p": self.project, "i": item, "f": self.fields[field]["id"], "v": option})
        return True

    # ---------------------------------------------------------- əməliyyatlar
    def desired_fields(self, issue: dict) -> dict[str, str | None]:
        labels = [label["name"] for label in issue.get("labels", [])]
        epic = None
        own = KEY.match(issue.get("title", ""))
        if "epic" in labels and own:
            epic = own.group(1)
        else:
            parent = self.rest(f"issues/{issue['number']}/parent")
            if parent:
                found = KEY.match(parent.get("title", ""))
                epic = found.group(1) if found else None
        size = SIZE.search(issue.get("body") or "")
        return {
            "Epic": epic,
            "Area": next((l.removeprefix("area:") for l in labels if l.startswith("area:")), None),
            "Priority": next((l for l in labels if re.fullmatch(r"p[0-2]", l)), None),
            "Size": size.group(1) if size else None,
        }

    def sync_issue(self, number: int) -> dict[str, str | None]:
        issue = self.rest(f"issues/{number}")
        if issue is None:
            raise SyncError(f"#{number} tapılmadı")
        if "pull_request" in issue:
            self.log(f"#{number} PR-dır — sahələri yoxdur, buraxılır")
            return {}
        item = self._item(issue)
        wanted = self.desired_fields(issue)
        if wanted["Epic"] and "Epic" in self.fields:
            self.gql(M_TEXT, {"p": self.project, "i": item, "f": self.fields["Epic"]["id"], "v": wanted["Epic"]})
        for field in ("Area", "Priority", "Size"):
            self._set_option(item, field, wanted[field])
        self.log(f"#{number}: " + ", ".join(f"{k}={v or '—'}" for k, v in wanted.items()))
        return wanted

    def start_branch(self, ref: str) -> str:
        match = BRANCH.match(ref)
        if not match:
            self.log(f"'{ref}' issue branch-ı deyil (feat|fix|chore/<n>-<slug>) — heç nə edilmir")
            return "ignored"
        number = int(match.group(1))
        issue = self.rest(f"issues/{number}")
        if issue is None or "pull_request" in issue:
            self.log(f"#{number} issue deyil — heç nə edilmir")
            return "ignored"
        if issue.get("state") != "open":
            self.log(f"#{number} bağlıdır — status dəyişmir")
            return "closed"
        item = self._item(issue)
        node = self.gql(Q_STATUS, {"i": item})["node"]
        current = (node.get("fieldValueByName") or {}).get("name")
        if current not in ADVANCEABLE:
            self.log(f"#{number} artıq '{current}' statusundadır — geri çəkilmir")
            return current
        if not self._set_option(item, "Status", "In Progress"):
            raise SyncError("Status sahəsində 'In Progress' variantı yoxdur")
        self.log(f"#{number}: {current or '—'} → In Progress")
        return "In Progress"

    def backfill(self) -> int:
        count, page = 0, 1
        while True:
            # --paginate/Link başlığı proxy-də qırılır — açıq page=N ilə.
            chunk = self.rest(f"issues?state=all&per_page=100&page={page}") or []
            if not chunk:
                return count
            for issue in chunk:
                if "pull_request" not in issue:
                    self.sync_issue(issue["number"])
                    count += 1
            page += 1


def main(argv: list[str]) -> int:
    token = os.environ.get("GH_TOKEN", "")
    if not token:
        print("::error title=project-sync::PROJECT_TOKEN secret-i təyin edilməyib. "
              "Settings → Secrets and variables → Actions → PROJECT_TOKEN (`repo` + `read:org` + `project`).")
        return 1
    if len(argv) < 2 or argv[1] not in ("issue", "branch", "backfill") or (argv[1] != "backfill" and len(argv) < 3):
        print(__doc__, file=sys.stderr)
        return 2
    repo = os.environ.get("REPO", "QonaqEt/qonaqet")
    rest, gql = http_clients(repo, token)
    try:
        board = Board(repo.split("/")[0], os.environ.get("PROJECT_TITLE", "QONAQET"), rest, gql)
        if argv[1] == "issue":
            board.sync_issue(int(argv[2]))
        elif argv[1] == "branch":
            board.start_branch(argv[2])
        else:
            print(f"backfill: {board.backfill()} issue")
    except SyncError as error:
        print(f"::error title=project-sync::{error}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
