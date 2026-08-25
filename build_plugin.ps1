$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$patcherProjectFile = Join-Path $projectRoot 'KoH2.LargerText.Patcher.csproj'
$bepInExDll = Join-Path $projectRoot 'vendor\BepInEx\BepInEx\core\BepInEx.dll'
$readme = Join-Path $projectRoot 'README_RU.txt'
$packageReadme = Join-Path $projectRoot 'package\INSTALL_RU.txt'

if (-not (Test-Path -LiteralPath $bepInExDll)) {
    throw "Local BepInEx was not found: $bepInExDll"
}

dotnet build $patcherProjectFile -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Patcher build failed with exit code $LASTEXITCODE"
}

$obsoleteRuntimePlugin = Join-Path $projectRoot 'package\BepInEx\plugins\KoH2.LargerText.Plugin.dll'
if (Test-Path -LiteralPath $obsoleteRuntimePlugin) {
    Remove-Item -LiteralPath $obsoleteRuntimePlugin -Force
}

Copy-Item -LiteralPath $readme -Destination $packageReadme -Force

Write-Host ''
Write-Host 'Done. Preloader patcher installation package:'
Write-Host (Join-Path $projectRoot 'package')
