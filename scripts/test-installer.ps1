param([string]$Version = '0.1.14')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$setup = Join-Path $projectRoot "artifacts\packages\CraftHelm-$Version-win-x64-setup.exe"
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A3AF1289-728F-4FB3-A791-EC7DDD897C14}_is1'
if (Test-Path -LiteralPath $key) { throw 'CraftHarbor is already installed; run this test on a clean user account.' }
$testId = [guid]::NewGuid().ToString('N')
$testDirectory = Join-Path $projectRoot "artifacts\installer-test-$testId"
$group = "CraftHarbor Installer Test $testId"
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$group\CraftHelm.lnk"
$dataDirectory = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'CraftHarbor\data'
New-Item -ItemType Directory -Force -Path $testDirectory, $dataDirectory | Out-Null
$marker = Join-Path $dataDirectory "installer-preserve-$testId.txt"
Set-Content -LiteralPath $marker -Value $testId
$profilePath = Join-Path $dataDirectory 'profiles.json'
$profileHash = if (Test-Path -LiteralPath $profilePath) { (Get-FileHash -LiteralPath $profilePath).Hash } else { $null }
$javaBefore = @(Get-Process -Name java,javaw -ErrorAction SilentlyContinue | ForEach-Object { @{ Id = $_.Id; StartTime = $_.StartTime } })
function InvokeInstaller([string]$File, [string[]]$Options) {
    $process = Start-Process -FilePath $File -ArgumentList (@('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-') + $Options) -WindowStyle Hidden -Wait -PassThru
    return $process.ExitCode
}
function CheckData {
    if ((Get-Content -LiteralPath $marker -Raw).Trim() -ne $testId) { throw 'Server data marker changed' }
    if ($profileHash -and (Get-FileHash -LiteralPath $profilePath).Hash -ne $profileHash) { throw 'Existing profiles changed' }
}
$installOptions = @('/LANG=japanese', ('/DIR="{0}"' -f $testDirectory), ('/GROUP="{0}"' -f $group))
$created = $false
$mutex = [Threading.Mutex]::new($false, 'Local\CraftHarbor.Desktop', [ref]$created)
if (-not $created) { $mutex.Dispose(); throw 'CraftHarbor is running; the installer test cannot continue.' }
try {
    if ((InvokeInstaller $setup $installOptions) -eq 0) { throw 'Installer ignored running-app mutex' }
    if (Test-Path -LiteralPath $key) { throw 'Blocked install registered an application' }
} finally { $mutex.Dispose() }
Write-Output 'PASS running-app install refusal (no process terminated)'
# Real previous-name upgrade, including replacement of old Start menu links.
$oldSetup = Join-Path $testDirectory 'previous-0.1.9.exe'
Invoke-WebRequest -Uri 'https://github.com/ryuya0124/CraftHarbor/releases/download/v0.1.9/CraftHarbor-0.1.9-win-x64-setup.exe' -OutFile $oldSetup
if ((Get-FileHash -LiteralPath $oldSetup -Algorithm SHA256).Hash -ne '99ACCD08CE7C258B9DC2C77F24067D82C3B631700BD5EC8435D3FD100A2E14BA') { throw 'Previous release checksum mismatch' }
if ((InvokeInstaller $oldSetup @('/LANG=japanese', ('/DIR="{0}"' -f $testDirectory), '/GROUP="CraftHarbor"')) -ne 0) { throw 'Previous version installation failed' }
$oldShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'CraftHarbor\CraftHarbor.lnk'
if (-not (Test-Path -LiteralPath $oldShortcut)) { throw 'Previous-name shortcut missing' }
CheckData
for ($pass = 0; $pass -lt 2; $pass++) {
    if ((InvokeInstaller $setup ($installOptions + ('/LOG="{0}"' -f (Join-Path $testDirectory "install-$pass.log")))) -ne 0) { throw 'Install/upgrade failed' }
    $installed = Get-ItemProperty -LiteralPath $key
    if (-not (Test-Path -LiteralPath (Join-Path $testDirectory 'coreclr.dll'))) { throw 'Installer must deploy runtime without launch-time extraction' }
    if ($installed.DisplayName -ne 'CraftHelm') { throw 'Application display name was not renamed' }
    if (Test-Path -LiteralPath $oldShortcut) { throw 'Old application shortcut was not replaced' }
    if ($installed.DisplayVersion -ne $Version) { throw 'Uninstall registration version mismatch' }
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($shortcut).TargetPath -ne (Join-Path $testDirectory 'CraftHarbor.exe')) { throw 'Start menu target mismatch' }
    CheckData
    Write-Output "PASS install/upgrade $pass, Start menu and uninstall registration, data preserved"
}
# Exercise the same detached helper used by automatic updates, with a simulated exiting parent.
$helper = Join-Path $projectRoot "artifacts\helper-$testId.ps1"
Copy-Item -LiteralPath (Join-Path $testDirectory 'UpdateHelper.ps1') -Destination $helper
$manifest = Join-Path $projectRoot "artifacts\helper-$testId.json"
$parent = Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 2') -WindowStyle Hidden -PassThru
$request = @{ Installer=$setup; SHA256=('0' * 64); Size=(Get-Item -LiteralPath $setup).Length; ParentId=$parent.Id; ParentStartedTicks=$parent.StartTime.ToUniversalTime().Ticks; TargetDirectory=$testDirectory; Restart=$false }
$request | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8
$run = Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $helper),'-Manifest',('"{0}"' -f $manifest)) -WindowStyle Hidden -Wait -PassThru
if ($run.ExitCode -eq 0 -or (Get-Content -LiteralPath ($manifest + '.result.json') -Raw | ConvertFrom-Json).Status -ne 'failed') { throw 'Update helper accepted tampered installer' }
$request.SHA256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
$parent = Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 4') -WindowStyle Hidden -PassThru
$request.ParentId = $parent.Id
$request.ParentStartedTicks = $parent.StartTime.ToUniversalTime().Ticks
$elapsed = [Diagnostics.Stopwatch]::StartNew()
$request | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8
$run = Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $helper),'-Manifest',('"{0}"' -f $manifest)) -WindowStyle Hidden -Wait -PassThru
if ($run.ExitCode -ne 0 -or (Get-Content -LiteralPath ($manifest + '.result.json') -Raw | ConvertFrom-Json).Status -ne 'success') {
    Get-Content -LiteralPath ($manifest + '.result.json')
    if (Test-Path -LiteralPath ($manifest + '.install.log')) { Get-Content -LiteralPath ($manifest + '.install.log') -Tail 35 }
    throw "Update helper failed to install verified update (exit $($run.ExitCode))"
}
if ($elapsed.Elapsed.TotalSeconds -lt 4 -or -not $parent.HasExited) { throw 'Update helper did not wait for parent exit' }
CheckData
Write-Output 'PASS detached update helper waits for parent, rejects tampering and applies verified installer with data preserved'
$uninstaller = Join-Path $testDirectory 'unins000.exe'
$mutex = [Threading.Mutex]::new($false, 'Local\CraftHarbor.Desktop')
try {
    if ((InvokeInstaller $uninstaller @()) -eq 0) { throw 'Uninstaller ignored running-app mutex' }
    if (-not (Test-Path -LiteralPath (Join-Path $testDirectory 'CraftHarbor.exe'))) { throw 'Blocked uninstall removed app' }
} finally { $mutex.Dispose() }
Write-Output 'PASS running-app uninstall refusal (no process terminated)'
if ((InvokeInstaller $uninstaller @(('/LOG="{0}"' -f (Join-Path $testDirectory 'uninstall.log')))) -ne 0) { throw 'Uninstall failed' }
if ((Test-Path -LiteralPath $key) -or (Test-Path -LiteralPath $shortcut) -or (Test-Path -LiteralPath (Join-Path $testDirectory 'CraftHarbor.exe'))) { throw 'Uninstall left registered application or shortcut' }
CheckData
foreach ($original in $javaBefore) {
    if ((Get-Process -Id $original.Id).StartTime -ne $original.StartTime) { throw 'Original Java process changed' }
}
Remove-Item -LiteralPath $marker
Write-Output 'PASS uninstall removes app/shortcuts/registration and preserves server data and original Java processes'
