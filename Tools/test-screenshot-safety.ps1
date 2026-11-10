[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$testRoot = $env:WSM_SCREENSHOT_TEST_ROOT
if ([string]::IsNullOrWhiteSpace($testRoot) -or -not [System.IO.Path]::IsPathFullyQualified($testRoot)) {
    throw 'Set WSM_SCREENSHOT_TEST_ROOT to an explicit approved absolute scratch directory. There is no fallback.'
}
$testRoot = [System.IO.Path]::GetFullPath($testRoot)
if ($testRoot.TrimEnd('\', '/') -eq [System.IO.Path]::GetPathRoot($testRoot).TrimEnd('\', '/')) {
    throw 'WSM_SCREENSHOT_TEST_ROOT must not be a filesystem root.'
}
$env:WSM_SCREENSHOT_TEST_ROOT = $testRoot
$projectPath = Join-Path $PSScriptRoot '../tests/WorldSphereMod.Tests.ScreenshotSafety/WorldSphereMod.Tests.ScreenshotSafety.csproj'
$artifactPath = Join-Path $testRoot 'artifacts'
$resultPath = Join-Path $testRoot 'results'
& dotnet test $projectPath --artifacts-path $artifactPath --results-directory $resultPath --logger trx
exit $LASTEXITCODE
