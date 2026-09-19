# Build first (Release), then: pwsh sensible_hunting/tools/package.ps1 (wraps the shared packager to ship the locator art)
& (Join-Path $PSScriptRoot '../../tools/package.ps1') (Split-Path $PSScriptRoot -Parent) -Extra 'art/icons/*.png'
