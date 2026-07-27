[CmdletBinding()]
param(
    [string]$GameDir = $env:NUCLEAR_OPTION_DIR,
    [string]$Configuration = "Release",
    [string]$DotNet = "dotnet"
)

$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "KellysKNOBS.csproj"
$assemblyName = "KellysKNOBS.dll"
$projectXml = [xml](Get-Content -LiteralPath $projectFile -Raw)
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read the project version from KellysKNOBS.csproj."
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $defaultGameDir = "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option"
    if (Test-Path -LiteralPath $defaultGameDir) {
        $GameDir = $defaultGameDir
    }
}

if ([string]::IsNullOrWhiteSpace($GameDir) -or
    -not (Test-Path -LiteralPath (Join-Path $GameDir "NuclearOption_Data\Managed\Rewired_Core.dll"))) {
    throw "Set -GameDir or NUCLEAR_OPTION_DIR to a valid Nuclear Option installation."
}

& $DotNet build $projectFile -c $Configuration "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$binary = Join-Path $projectRoot "bin\$Configuration\net472\$assemblyName"
$releaseRoot = Join-Path $projectRoot "release"
$staging = Join-Path $releaseRoot "KellysKNOBS-$version"
$archive = Join-Path $releaseRoot "KellysKNOBS-$version.zip"

if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}

New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -LiteralPath $binary -Destination $staging
Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $staging
Copy-Item -LiteralPath (Join-Path $projectRoot "CHANGELOG.md") -Destination $staging
Copy-Item -LiteralPath (Join-Path $projectRoot "LICENSE") -Destination $staging
Copy-Item -LiteralPath (Join-Path $projectRoot "assets") -Destination $staging -Recurse

Compress-Archive -LiteralPath $staging -DestinationPath $archive -CompressionLevel Optimal

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $archive
Write-Host "Created $archive"
Write-Host "SHA256 $($hash.Hash)"
