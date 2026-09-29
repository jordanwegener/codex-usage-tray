$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputDirectory = Join-Path $projectRoot 'dist'
$outputFile = Join-Path $outputDirectory 'CodexUsageTray.exe'
$manifest = Join-Path $projectRoot 'app.manifest'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework C# compiler was not found. Install .NET Framework 4.8.'
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' |
    Sort-Object Name |
    ForEach-Object FullName

& $compiler `
    /nologo `
    /target:winexe `
    /optimize+ `
    /debug:pdbonly `
    /out:$outputFile `
    /win32manifest:$manifest `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    $sourceFiles

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Host "Built $outputFile"

