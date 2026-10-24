# Builds dist\DelayedTaskRunner.exe (single file, needs .NET 8 Desktop Runtime).
# Use -Install to also replace E:\appServices\DelayedTaskRunnerHome\DelayedTaskRunner.exe
# (the old exe is renamed to DelayedTaskRunner.exe.bak-<date> first).
param([switch]$Install)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$stop = Join-Path $HOME '.claude\skills\desktop-app-rules\stop-app.ps1'
$home2 = 'E:\appServices\DelayedTaskRunnerHome'

if (Test-Path $stop) { & $stop -Path (Join-Path $dist 'DelayedTaskRunner.exe') }

dotnet publish (Join-Path $root 'DelayedTaskRunner.csproj') -c Release -o $dist --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Get-ChildItem $dist -Exclude 'DelayedTaskRunner.exe' | Remove-Item -Recurse -Force

if ($Install) {
    $target = Join-Path $home2 'DelayedTaskRunner.exe'
    if (Test-Path $stop) { & $stop -Path $target }
    if (Test-Path $target) {
        $bak = "$target.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
        Rename-Item $target (Split-Path $bak -Leaf)
        Write-Host "Old exe renamed to $bak"
    }
    Copy-Item (Join-Path $dist 'DelayedTaskRunner.exe') $target
    Write-Host "Installed $target"
}
