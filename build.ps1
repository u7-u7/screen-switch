$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}

$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File |
    Select-Object -ExpandProperty FullName
$output = Join-Path $PSScriptRoot 'dist'
$icon = Join-Path $PSScriptRoot 'ScreenSwitch.ico'
$executable = Join-Path $output 'ScreenSwitch.exe'
New-Item -ItemType Directory -Force -Path $output | Out-Null

& $compiler /nologo /warnaserror+ /target:winexe /optimize+ /codepage:65001 `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Accessibility.dll `
    "/win32icon:$icon" "/out:$executable" $sources
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Output $executable
