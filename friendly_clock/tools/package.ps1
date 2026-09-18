# Build first (Release), then: pwsh friendly_clock/tools/package.ps1 (wraps the shared packager to ship the dial art)
& (Join-Path $PSScriptRoot '../../tools/package.ps1') (Split-Path $PSScriptRoot -Parent) -Extra art/clock_face.png, art/clock_hand.png, art/clock_cap.png
