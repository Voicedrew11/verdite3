# Build the Windows x64 package: dist/Verdite3-<version>-win-x64.zip, and the
# installer when iscc is on PATH.
#
# The script is Verdite Core's; packaging/package.env names this port to it. Needs
# the RecompOne subtree built (scripts/setup_tools.sh) and the .NET 10 SDK, and
# NOT the disc. See "Building a release" in docs/PACKAGING.md.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '..\..\tools\verdite-core\packaging\windows\build-windows.ps1') @args
if ($LASTEXITCODE) { exit $LASTEXITCODE }
