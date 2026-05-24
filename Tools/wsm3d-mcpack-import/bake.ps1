param(
    [Parameter(Mandatory = $true)]
    [string]$AtlasPath,

    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [string]$OutputBundle = "",

    [string]$UnityEditorPath = "",

    [string]$BuildTarget = "StandaloneWindows64",

    [string]$BundleAssetName = "atlas",

    [switch]$NoCleanup
)

$scriptDir = Split-Path -Parent $PSCommandPath
$unityProject = Join-Path $scriptDir "wsm3d-mcpack-editor"
$editorScript = Join-Path $unityProject "Assets/Editor/Wsm3dMcPackBundleBaker.cs"
$editorScriptSrc = Join-Path $scriptDir "Wsm3dMcPackBundleBaker.cs"

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$atlasPath = [System.IO.Path]::GetFullPath($AtlasPath)
$manifestPath = [System.IO.Path]::GetFullPath($ManifestPath)

if (-not (Test-Path $atlasPath)) {
    throw "Atlas file does not exist: $atlasPath"
}

if (-not (Test-Path $manifestPath)) {
    throw "Manifest file does not exist: $manifestPath"
}

if ([string]::IsNullOrWhiteSpace($OutputBundle)) {
    $OutputBundle = Join-Path ([System.IO.Path]::GetDirectoryName($manifestPath)) "atlas.bundle"
}
$OutputBundle = [System.IO.Path]::GetFullPath($OutputBundle)

$unityEditor = $UnityEditorPath
if ([string]::IsNullOrWhiteSpace($unityEditor)) {
    if (-not [string]::IsNullOrWhiteSpace($env:UNITY_EDITOR)) {
        $unityEditor = $env:UNITY_EDITOR
    } elseif (Test-Path "${env:ProgramFiles}\Unity\Hub\Editor") {
        $installed = Get-ChildItem "${env:ProgramFiles}\Unity\Hub\Editor" -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($null -ne $installed) {
            $unityEditor = Join-Path $installed.FullName "Editor\Unity.exe"
        }
    } elseif (Test-Path "${env:ProgramFiles(x86)}\Unity\Hub\Editor") {
        $installed = Get-ChildItem "${env:ProgramFiles(x86)}\Unity\Hub\Editor" -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($null -ne $installed) {
            $unityEditor = Join-Path $installed.FullName "Editor\Unity.exe"
        }
    }
}
if (-not (Test-Path $unityEditor)) {
    throw "Unity editor not found. Pass -UnityEditorPath or set $env:UNITY_EDITOR"
}

try {
    New-Item -ItemType Directory -Path $unityProject -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $unityProject "Assets/Editor") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $unityProject "Library") -Force | Out-Null
    Copy-Item $editorScriptSrc $editorScript -Force

    $assetBundleDir = [System.IO.Path]::GetDirectoryName($OutputBundle)
    New-Item -ItemType Directory -Path $assetBundleDir -Force | Out-Null

    $bundleArg = $OutputBundle
    $args = @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", $unityProject,
        "-executeMethod", "Wsm3dMcPackBundleBaker.BakeFromCommandLine",
        "--atlas", $atlasPath,
        "--manifest", $manifestPath,
        "--output-bundle", $bundleArg,
        "--build-target", $BuildTarget,
        "--bundle-name", $BundleAssetName
    )

    $process = Start-Process -FilePath $unityEditor -ArgumentList $args -PassThru -NoNewWindow -Wait
    if ($process.ExitCode -ne 0) {
        throw "Unity bake failed with exit code $($process.ExitCode)."
    }

    if (-not (Test-Path $OutputBundle)) {
        throw "Expected AssetBundle at $OutputBundle was not produced."
    }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $manifest | Add-Member -NotePropertyName atlas_bundle -NotePropertyValue (Split-Path $OutputBundle -Leaf) -Force
    $manifest | Add-Member -NotePropertyName atlas_rgb -NotePropertyValue (Split-Path $atlasPath -Leaf) -Force
    $manifest | ConvertTo-Json -Depth 64 | Set-Content -NoNewline $manifestPath
    Write-Output "Wrote AssetBundle: $OutputBundle"
    Write-Output "Updated manifest: $manifestPath"
}
finally {
    if (-not $NoCleanup) {
        if (Test-Path $unityProject) {
            Remove-Item -Recurse -Force $unityProject -ErrorAction SilentlyContinue
        }
    }
}
