param(
    [string[]]$Projects = @(
        "tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj",
        "tests/WorldSphereMod.Tests.Integration/WorldSphereMod.Tests.Integration.csproj",
        "tests/WorldSphereMod.Tests.E2E/WorldSphereMod.Tests.E2E.csproj"
    ),
    [string]$OutputDirectory = "docs/coverage"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$outputRoot = Join-Path $repoRoot $OutputDirectory
$coverageRoot = Join-Path $outputRoot "coverage"

New-Item -ItemType Directory -Force -Path $coverageRoot | Out-Null

if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
    dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.5.1 | Out-Null
}

$toolRoot = $env:USERPROFILE
if (-not $toolRoot) {
    $toolRoot = $env:HOME
}

$env:PATH = "$toolRoot/.dotnet/tools;$env:PATH"

$results = @()
$failedProjects = @()
foreach ($project in $Projects) {
    $projectPath = Join-Path $repoRoot $project
    $projectName = [IO.Path]::GetFileNameWithoutExtension($projectPath)
    $projectOutput = Join-Path $coverageRoot $projectName
    New-Item -ItemType Directory -Force -Path $projectOutput | Out-Null

    & dotnet test $projectPath --collect:"XPlat Code Coverage" --results-directory $projectOutput
    if ($LASTEXITCODE -ne 0) {
        $failedProjects += $projectName
    }
    $results += Get-ChildItem -Path $projectOutput -Recurse -Filter coverage.cobertura.xml | ForEach-Object { $_.FullName }
}

if ($results.Count -eq 0) {
    throw "No coverage reports were produced."
}

reportgenerator `
    "-reports:$($results -join ';')" `
    "-targetdir:$outputRoot" `
    "-reporttypes:Html;HtmlSummary;Cobertura;TextSummary" | Out-Null

Get-Content (Join-Path $outputRoot "Summary.txt")
if ($failedProjects.Count -gt 0) {
    Write-Warning ("Test failures were present in: " + ($failedProjects -join ", "))
}
Write-Host "Coverage report written to $outputRoot"
