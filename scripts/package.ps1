param([string]$Version = '0.1.16', [switch]$WithInstaller)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $artifactRoot = Join-Path $projectRoot 'artifacts'
    $packageRoot = Join-Path $artifactRoot 'packages'
    New-Item -ItemType Directory -Force $packageRoot | Out-Null
    foreach ($kind in @('portable', 'standalone')) {
        $target = Join-Path $artifactRoot $kind
        if ($kind -eq 'portable') {
            dotnet publish src/CraftHarbor.Desktop -c Release -r win-x64 --self-contained false -o $target
        } else {
            dotnet publish src/CraftHarbor.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $target
        }
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $kind" }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'LICENSE') -Destination $target -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $target -Recurse -Force
        $zipPath = Join-Path $packageRoot "CraftHelm-$Version-win-x64-$kind.zip"
        Compress-Archive -Path (Join-Path $target '*') -DestinationPath $zipPath -Force
    }
    if ($WithInstaller) {
        $installedTarget = Join-Path $artifactRoot 'installed'
        dotnet publish src/CraftHarbor.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -o $installedTarget
        if ($LASTEXITCODE -ne 0) { throw 'Installed application publish failed' }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'LICENSE') -Destination $installedTarget -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $installedTarget -Recurse -Force
        $compiler = & (Join-Path $PSScriptRoot 'setup-installer-compiler.ps1')
        & $compiler "/DAppVersion=$Version" (Join-Path $projectRoot 'installer\CraftHarbor.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
        # Old installations discover this byte-identical compatibility asset.
        Copy-Item -LiteralPath (Join-Path $packageRoot "CraftHelm-$Version-win-x64-setup.exe") -Destination (Join-Path $packageRoot "CraftHarbor-$Version-win-x64-setup.exe") -Force
    }
    $hashLines = Get-ChildItem -LiteralPath $packageRoot | Where-Object { ($_.Name -like "CraftHelm-$Version-*" -or $_.Name -like "CraftHarbor-$Version-*") -and $_.Extension -in '.zip', '.exe' } | ForEach-Object {
        $digest = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        '{0}  {1}' -f $digest.Hash.ToLowerInvariant(), $_.Name
    }
    $hashLines | Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Encoding utf8
    Get-ChildItem -LiteralPath $packageRoot | Select-Object Name, Length
} finally { Pop-Location }
