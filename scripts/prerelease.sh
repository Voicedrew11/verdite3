#!/usr/bin/env bash
# Publish the current branch's HEAD as the release candidate: the rolling `rc`
# prerelease on GitHub, replacing the last one, each package zipped with the
# RC_PASSWORD secret. Release.yml's rc job does the work; this starts it on the
# branch as GitHub has it, so HEAD must be pushed. See "Release candidates" in
# docs/PACKAGING.md.
#
#   bash scripts/prerelease.sh            # start it and wait for it
#   bash scripts/prerelease.sh --no-wait
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

WAIT=1
[ "${1:-}" = "--no-wait" ] && WAIT=0

branch="$(git rev-parse --abbrev-ref HEAD)"
[ "$branch" != HEAD ] || { echo "detached HEAD; check out the branch to publish"; exit 1; }
[ -z "$(git status --porcelain)" ] || { echo "working tree is dirty"; exit 1; }
git fetch -q origin "$branch"
[ "$(git rev-parse HEAD)" = "$(git rev-parse "origin/$branch")" ] \
    || { echo "HEAD is not origin/$branch; push it first (the workflow builds what GitHub has)"; exit 1; }
gh secret list | grep -q '^RC_PASSWORD\b' \
    || { echo "the RC_PASSWORD secret is not set: gh secret set RC_PASSWORD"; exit 1; }

sha="$(git rev-parse HEAD)"
start="$(date -u -d "-30 sec" +%Y-%m-%dT%H:%M:%SZ)"  # a little early, for clock skew
gh workflow run release.yml --ref "$branch" -f rc=true
echo "==> release candidate ${sha::9} started"
# The run appears a moment after it is asked for; find it by its commit, and
# started no earlier than this, since a commit can have been a candidate before.
for _ in $(seq 20); do
    run="$(gh run list --workflow release.yml --event workflow_dispatch --commit "$sha" --limit 5 \
        --json databaseId,createdAt --jq "[.[] | select(.createdAt >= \"$start\")][0].databaseId // empty")"
    [ -n "$run" ] && break
    sleep 3
done
[ -n "${run:-}" ] || { echo "could not find the run; see: gh run list --workflow release.yml"; exit 1; }
echo "    $(gh run view "$run" --json url --jq .url)"
[ "$WAIT" = 1 ] || exit 0
gh run watch "$run" --exit-status --interval 30 >/dev/null
echo "==> published: $(gh release view rc --json url --jq .url)"
