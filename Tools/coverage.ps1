param(
    [string[]]$Projects = @(
        "tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj",
        "tests/WorldSphereMod.Tests.Integration/WorldSphereMod.Tests.Integration.csproj",
        "tests/WorldSphereMod.Tests.E2E/WorldSphereMod.Tests.E2E.csproj"
    ),
    [string]$OutputDirectory = "docs/coverage",
    # Tier filter (Wave 4). `all` runs every project; the named tiers use
    # `dotnet test --filter Category=<Tier>` to select test classes by the
    # [Trait("Category", "Unit"|"Integration"|"E2E")] attribute.
    [ValidateSet("all", "Unit", "Integration", "E2E")]
    [string]$Tier = "all",
    # Coverage floor enforced after reportgenerator writes Summary.txt.
    # Baseline at Wave 4 kick-off: 80% on WorldSphereAPI + 6 compile-linked
    # mod files. Bump to 85/90/95/100 per the staged rollout in
    # wsm3d-coverage-plan-2026-06-05.md.
    [double]$MinLineCoverage = 80.0,
    # CI workflows assert the floor themselves; pass this to skip the throw.
    [switch]$SkipGate
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

    # Map project name to the Trait category ("Unit" / "Integration" / "E2E").
    $category = switch -Wildcard ($projectName) {
        "*.Tests.Unit*"        { "Unit" }
        "*.Tests.Integration*" { "Integration" }
        "*.Tests.E2E*"         { "E2E" }
        default                { $null }
    }

    $testArgs = @($projectPath, "--collect:XPlat Code Coverage", "--results-directory", $projectOutput)
    if ($Tier -ne "all" -and $category -and $category -eq $Tier) {
        # Single-tier selection drives --filter Category=<Tier>.
        $testArgs += @("--filter", "Category=$category")
    } elseif ($Tier -ne "all" -and $category -and $category -ne $Tier) {
        # Skip projects outside the selected tier (test count = 0; coverage file absent).
        Write-Host "==> Skipping $projectName (tier=$Tier)"
        continue
    }

    Write-Host "==> Running tests: $projectName (tier=$Tier)"
    & dotnet test @testArgs
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

# Enforce the line-coverage floor parsed from Summary.txt. Throws on regression
# so the workflow gate (and any local `pwsh Tools/coverage.ps1` invocation) fails
# fast on coverage loss. CI workflows that want to assert their own floor can
# pass -SkipGate.
$summaryPath = Join-Path $outputRoot "Summary.txt"
$lineLine = Get-Content $summaryPath | Where-Object { $_ -match '^Line coverage:\s*(\d+(?:\.\d+)?)%' } | Select-Object -First 1
if ($lineLine) {
    $linePct = [double]($lineLine -replace '^Line coverage:\s*(\d+(?:\.\d+)?)%.*', '$1')
    Write-Host ("Line coverage: {0}% (gate: {1}%)" -f $linePct, $MinLineCoverage)
    if (-not $SkipGate -and $linePct -lt $MinLineCoverage) {
        throw "Coverage gate failed: ${linePct}% < ${MinLineCoverage}%"
    }
} else {
    Write-Warning "Could not parse line coverage from $summaryPath"
}

# TODO(xdd-coverage-100): uncomment strict gate when 85% line / 90% branch
# per the staged rollout (85/90/95/100) in wsm3d-coverage-plan-2026-06-05.md.
# When uncommented, augment the gate above with branch-coverage parsing:
#   $branchLine = Get-Content $summaryPath | Where-Object { $_ -match '^Branch coverage:' } | Select-Object -First 1
#   $branchPct = [double]($branchLine -replace '.*?(\d+(?:\.\d+)?)%.*', '$1')
#   if ($linePct -lt 85.0 -or $branchPct -lt 90.0) { throw "..." }

if ($failedProjects.Count -gt 0) {
    Write-Warning ("Test failures were present in: " + ($failedProjects -join ", "))
    if (-not $SkipGate) {
        throw "Tests failed in: $($failedProjects -join ', '). Re-run with -SkipGate to bypass."
    }
}
Write-Host "Coverage report written to $outputRoot"
