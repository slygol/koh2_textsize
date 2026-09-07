$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$patcherProjectFile = Join-Path $projectRoot 'KoH2.LargerText.Patcher.csproj'
$monoCecilDll = Join-Path $projectRoot 'tools\.store\ilspycmd\11.0.0.9375\ilspycmd\11.0.0.9375\tools\net10.0\any\Mono.Cecil.dll'
$readme = Join-Path $projectRoot 'README_RU.txt'
$packageReadme = Join-Path $projectRoot 'package\INSTALL_RU.txt'

if (-not (Test-Path -LiteralPath $monoCecilDll)) {
    throw "Mono.Cecil was not found: $monoCecilDll"
}

$referenceDirectory = Join-Path $projectRoot 'vendor\BepInEx\BepInEx\core'
New-Item -ItemType Directory -Path $referenceDirectory -Force | Out-Null
Copy-Item -LiteralPath $monoCecilDll -Destination (Join-Path $referenceDirectory 'Mono.Cecil.dll') -Force

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
