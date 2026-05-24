$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$manifestRoot = Join-Path $repoRoot "docs/journeys/manifests"
$journeyRepo = "C:/Users/koosh/Dino/tools/phenotype-journeys"
$journeyCache = Join-Path $repoRoot "tools/.cache/phenotype-journeys"

function Resolve-PhenotypeJourneyBinary {
    $journeyCmd = Get-Command phenotype-journey -ErrorAction SilentlyContinue
    if ($journeyCmd) {
        return $journeyCmd.Source
    }

    $candidatePaths = @(
        (Join-Path $journeyRepo "target/release/phenotype-journey.exe"),
        (Join-Path $journeyRepo "target/release/phenotype-journey"),
        (Join-Path $journeyCache "target/release/phenotype-journey.exe"),
        (Join-Path $journeyCache "target/release/phenotype-journey")
    )

    foreach ($candidate in $candidatePaths) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $buildRoots = @()
    if (Test-Path -LiteralPath $journeyRepo -PathType Container) {
        $buildRoots += $journeyRepo
    }
    if (Test-Path -LiteralPath $journeyCache -PathType Container) {
        $buildRoots += $journeyCache
    }

    foreach ($root in $buildRoots) {
        Write-Host ("Building phenotype-journey from " + $root + "...")
        Push-Location $root
        try {
            & cargo build --release --bin phenotype-journey
            if ($LASTEXITCODE -ne 0) {
                throw "cargo build failed in $root"
            }
        } finally {
            Pop-Location
        }

        foreach ($candidate in $candidatePaths) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }

    throw "phenotype-journey not found on PATH and no local source or cache build could produce a binary."
}

$pjBin = Resolve-PhenotypeJourneyBinary

$manifests = Get-ChildItem -Path $manifestRoot -Recurse -Filter "manifest.json" | Sort-Object FullName
if (-not $manifests -or $manifests.Count -eq 0) {
    throw "No journey manifests found under $manifestRoot"
}

foreach ($manifest in $manifests) {
    & $pjBin verify $manifest.FullName --mock
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-Host ("OK " + $manifest.FullName)
}

Write-Host ("Verified " + $manifests.Count + " journey manifest(s).")
