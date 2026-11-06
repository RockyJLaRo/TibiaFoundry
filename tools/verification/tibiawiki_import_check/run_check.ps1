# Repeats the TibiaWiki import that failed in 7.0.0 (see README.md in this folder): Item Catalog, "TibiaWiki item IDs"
# with no catalog yet, the suggested name, the live download, the import confirmed. Needs the internet and an unlocked
# desktop (the program's window opens for about a minute; the mouse is not moved).
param(
    [string]$Work = (Join-Path ([System.IO.Path]::GetTempPath()) ("TibiaFoundry-wikicheck-run-" + (Get-Date -Format "yyyyMMdd-HHmmss"))),
    [string]$App
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = (Resolve-Path (Join-Path $here "..\..\..")).Path
if (-not $App) {
    dotnet build (Join-Path $root "src\TibiaFoundry.App\TibiaFoundry.App.csproj") -c Release -v q -nologo | Out-Host
    $App = Join-Path $root "src\TibiaFoundry.App\bin\Release\net10.0-windows\win-x64"
}
$out = Join-Path ([System.IO.Path]::GetTempPath()) "TibiaFoundry-wikicheck\tool"
dotnet build (Join-Path $here "ImportCheck\ImportCheck.csproj") -c Release -v q -nologo -o $out | Out-Host
& (Join-Path $out "ImportCheck.exe") $Work $App
$code = $LASTEXITCODE
"Report: " + (Join-Path $Work "import_check_report.txt")
exit $code
