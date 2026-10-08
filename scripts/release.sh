#!/usr/bin/env bash
# Cut a release: tag HEAD v<version>; the tag is the version. Does not push.
#
#   bash scripts/release.sh 0.2.0
#
# The script is Verdite Core's; see "Cutting one" in docs/PACKAGING.md.
exec bash "$(dirname "${BASH_SOURCE[0]}")/../tools/verdite-core/scripts/release.sh" "$@"
