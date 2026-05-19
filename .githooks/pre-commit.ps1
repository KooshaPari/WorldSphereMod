<#
.SYNOPSIS
  Pre-commit hook for WorldSphereMod3D repository.
  Runs sanity checks on staged changes before allowing commit.

.DESCRIPTION
  Checks:
  - .cs files: dotnet build
  - test/*.cs files: dotnet test
  - Tools/wsm3d.ps1: PowerShell parse validation
  - .github/workflows/*.yml: YAML validation (best-effort)
  - .coderabbit.yaml: YAML validation (best-effort)

  Bypass with: git commit --no-verify

.EXIT_CODES
  0 = All checks passed
  1 = One or more checks failed
#>

$ErrorActionPreference = "Stop"
$startTime = [DateTime]::UtcNow

# === Colors ===
$ok = "`e[32m"
$err = "`e[31m"
$warn = "`e[33m"
$reset = "`e[0m"

# === Utilities ===
function Test-YamlValid {
    param([string]$FilePath)

    # Try Python first (accurate)
    $pythonExists = $null -ne (Get-Command python -ErrorAction SilentlyContinue)
    if ($pythonExists) {
        try {
            $output = python -c "import yaml; yaml.safe_load(open('$FilePath'))" 2>&1
            return $true
        } catch {
            return $false
        }
    }

    # Fallback: basic regex check for YAML structure
    try {
        $content = Get-Content $FilePath -Raw
        # Very basic: check for common issues
        if ($content -match ':\s+$' -and -not ($content -match 'on:\s+')) {
            return $false
        }
        return $true
    } catch {
        return $false
    }
}

function Measure-ElapsedMs {
    param([DateTime]$Start)
    return [Math]::Round(([DateTime]::UtcNow - $Start).TotalSeconds, 2)
}

# === Main ===
$repoRoot = Split-Path -Parent $PSScriptRoot
$stagedFiles = @(git diff --cached --name-only --diff-filter=ACM 2>$null | Where-Object { $_ })

# If nothing staged, pass silently
if ($stagedFiles.Count -eq 0) {
    Write-Host "${ok}[pre-commit] OK (0s)${reset}"
    exit 0
}

$checks = @()

# Detect .cs files (build)
$csFiles = $stagedFiles | Where-Object { $_ -match '\.cs$' }
if ($csFiles.Count -gt 0) {
    $checks += @{ name = "dotnet build"; check = $true }
}

# Detect test .cs files (test)
$testFiles = $stagedFiles | Where-Object { $_ -match 'tests?[/\\].*\.cs$' }
if ($testFiles.Count -gt 0) {
    $checks += @{ name = "dotnet test"; check = $true }
}

# Detect Tools/wsm3d.ps1
$wsmFiles = $stagedFiles | Where-Object { $_ -match 'Tools[/\\]wsm3d\.ps1' }
if ($wsmFiles.Count -gt 0) {
    $checks += @{ name = "PowerShell parse"; check = $true }
}

# Detect GitHub workflows YAML
$workflowFiles = $stagedFiles | Where-Object { $_ -match '\.github[/\\]workflows[/\\].*\.ya?ml' }
if ($workflowFiles.Count -gt 0) {
    $checks += @{ name = "YAML (workflows)"; check = $true }
}

# Detect .coderabbit.yaml
$coderabbitFiles = $stagedFiles | Where-Object { $_ -match '\.coderabbit\.ya?ml' }
if ($coderabbitFiles.Count -gt 0) {
    $checks += @{ name = "YAML (.coderabbit)"; check = $true }
}

# Run checks
$failedChecks = @()

foreach ($chk in $checks) {
    switch ($chk.name) {
        "dotnet build" {
            Write-Host -NoNewline "${warn}[pre-commit] build${reset}... "
            try {
                Push-Location $repoRoot
                $out = & dotnet build WorldSphereMod.csproj -c Release --nologo -v quiet 2>&1
                Write-Host "${ok}OK${reset}"
            } catch {
                Write-Host "${err}FAIL${reset}"
                $failedChecks += "dotnet build: $_"
            } finally {
                Pop-Location
            }
        }

        "dotnet test" {
            Write-Host -NoNewline "${warn}[pre-commit] test${reset}... "
            try {
                Push-Location $repoRoot
                $out = & dotnet test tests/WorldSphereMod.Tests.Unit -c Release --nologo -v quiet 2>&1
                Write-Host "${ok}OK${reset}"
            } catch {
                Write-Host "${err}FAIL${reset}"
                $failedChecks += "dotnet test: $_"
            } finally {
                Pop-Location
            }
        }

        "PowerShell parse" {
            Write-Host -NoNewline "${warn}[pre-commit] ps1 parse${reset}... "
            try {
                $scriptPath = Join-Path $repoRoot "Tools/wsm3d.ps1"
                $tokens = $null
                $errors = $null
                [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors) | Out-Null
                if ($errors.Count -gt 0) {
                    throw "Parse error: $($errors[0].Message)"
                }
                Write-Host "${ok}OK${reset}"
            } catch {
                Write-Host "${err}FAIL${reset}"
                $failedChecks += "PowerShell parse: $_"
            }
        }

        "YAML (workflows)" {
            Write-Host -NoNewline "${warn}[pre-commit] yaml (workflows)${reset}... "
            try {
                $allValid = $true
                foreach ($wf in $workflowFiles) {
                    $fullPath = Join-Path $repoRoot $wf
                    if (-not (Test-YamlValid $fullPath)) {
                        $allValid = $false
                        throw "Invalid YAML: $wf"
                    }
                }
                Write-Host "${ok}OK${reset}"
            } catch {
                Write-Host "${err}FAIL${reset}"
                $failedChecks += "YAML (workflows): $_"
            }
        }

        "YAML (.coderabbit)" {
            Write-Host -NoNewline "${warn}[pre-commit] yaml (.coderabbit)${reset}... "
            try {
                foreach ($cr in $coderabbitFiles) {
                    $fullPath = Join-Path $repoRoot $cr
                    if (-not (Test-YamlValid $fullPath)) {
                        throw "Invalid YAML: $cr"
                    }
                }
                Write-Host "${ok}OK${reset}"
            } catch {
                Write-Host "${err}FAIL${reset}"
                $failedChecks += "YAML (.coderabbit): $_"
            }
        }
    }
}

$elapsed = Measure-ElapsedMs $startTime

# === Result ===
if ($failedChecks.Count -eq 0) {
    Write-Host "${ok}[pre-commit] OK (${elapsed}s)${reset}"
    exit 0
} else {
    Write-Host ""
    Write-Host "${err}[pre-commit] FAIL${reset} - ${failedChecks.Count} check(s) failed:"
    foreach ($fail in $failedChecks) {
        Write-Host "  ${err}x${reset} $fail"
    }
    Write-Host ""
    Write-Host "${warn}Bypass with:${reset} git commit --no-verify"
    exit 1
}
