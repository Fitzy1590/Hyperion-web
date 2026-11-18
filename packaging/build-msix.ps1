<#
 Builds a self-contained win-x64 MSIX from src/Hyperion.
 Unsigned unless -PfxPath is given (a signed package is required to install it).
 Example: ./packaging/build-msix.ps1 -Version 1.0.3.0 -Publisher "CN=Hyperion Dev" -PfxPath dev.pfx -PfxPassword pw
#>
param(
    [string]$Version = "1.0.0.0",
    [string]$Publisher = "CN=Hyperion Dev",
    [string]$PfxPath,
    [string]$PfxPassword,
    [string]$Output = "artifacts"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$stage = Join-Path $root "$Output/stage"
$msix = Join-Path $root "$Output/Hyperion_$Version`_x64.msix"

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

dotnet publish "$root/src/Hyperion/Hyperion.csproj" -c Release -r win-x64 --self-contained true -p:Version=$Version -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item "$PSScriptRoot/Assets" "$stage/Assets" -Recurse -Force
(Get-Content "$PSScriptRoot/AppxManifest.xml" -Raw).Replace("__VERSION__", $Version).Replace("__PUBLISHER__", $Publisher) |
    Set-Content "$stage/AppxManifest.xml" -Encoding UTF8

$kits = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$makeappx = Get-ChildItem "$kits\*\x64\makeappx.exe" | Sort-Object FullName | Select-Object -Last 1
if (-not $makeappx) { throw "makeappx.exe not found; install the Windows 10/11 SDK" }
& $makeappx.FullName pack /d $stage /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

if ($PfxPath) {
    $signtool = Get-ChildItem "$kits\*\x64\signtool.exe" | Sort-Object FullName | Select-Object -Last 1
    & $signtool.FullName sign /fd SHA256 /f $PfxPath /p $PfxPassword $msix
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
}
Write-Host "Created $msix"
