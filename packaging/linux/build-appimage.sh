#!/usr/bin/env bash
# Build the Linux AppImage: dist/Verdite3-<VERSION>-x86_64.AppImage.
#
# The script is Verdite Core's; packaging/package.env names this port to it. Needs
# the RecompOne subtree built (scripts/setup_tools.sh) and the .NET 10 SDK, and
# NOT the disc. See "Building a release" in docs/PACKAGING.md.
exec bash "$(dirname "${BASH_SOURCE[0]}")/../../tools/verdite-core/packaging/linux/build-appimage.sh" "$@"
