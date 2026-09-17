# Build first (Release), then: pwsh tools/package.ps1
& (Join-Path $PSScriptRoot '../../friendly_clock/tools/package.ps1') -Root (Split-Path $PSScriptRoot -Parent) -Name 'Calm_Seas' -Dll 'CalmSeas' -Extra @()
