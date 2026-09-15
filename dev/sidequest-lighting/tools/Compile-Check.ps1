# SideQuest Lighting Tools - MIT
#
# Compiles the suite against real Unity and URP assemblies without opening Unity.
#
# Unity only reports compile errors once the Editor has imported the scripts, which is a
# slow loop and needs a project open. This runs Unity's own Roslyn over the same sources
# with the same references in a couple of seconds, so a typo is caught before it costs an
# Editor restart.
#
# It is a syntax and binding check only - it proves the code compiles, never that it
# behaves. Editor testing against a real scene is still required.
#
#   .\Compile-Check.ps1
#   .\Compile-Check.ps1 -UnityVersion 6000.4.3f1 -UrpProject "R:\UNITY\Banter\HolloweenHospital"

[CmdletBinding()]
param(
    [string] $UnityVersion = "6000.4.3f1",

    # Any URP project whose Library\ScriptAssemblies holds the compiled URP assemblies.
    # URP ships as a package, so its DLLs exist per-project rather than in the Editor.
    [string] $UrpProject = "",

    [string] $UnityRoot = "C:\Program Files\Unity\Hub\Editor"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path (Split-Path -Parent $scriptDir) "Assets"

$editorData = Join-Path $UnityRoot "$UnityVersion\Editor\Data"
if (-not (Test-Path $editorData)) {
    throw "Unity $UnityVersion not found at $editorData. Pass -UnityVersion or -UnityRoot."
}

$csc = Join-Path $editorData "DotNetSdkRoslyn\csc.dll"
if (-not (Test-Path $csc)) { throw "Roslyn compiler not found at $csc" }

# ---- references -------------------------------------------------------------

$references = New-Object System.Collections.Generic.List[string]

# -noconfig means nothing is referenced implicitly, so the base class library has to be
# supplied explicitly. Unity compiles against netstandard 2.1, and its reference assembly
# forwards System.Object, System.String and the rest - without it every file fails with
# "predefined type System.Object is not defined".
$netstandard = Join-Path $editorData "NetStandard\ref\2.1.0\netstandard.dll"
if (-not (Test-Path $netstandard)) { throw "netstandard reference assembly not found at $netstandard" }
$references.Add($netstandard)

$netstandardExtensions = Join-Path $editorData "NetStandard\Extensions\2.0.0"
if (Test-Path $netstandardExtensions) {
    Get-ChildItem $netstandardExtensions -Filter "*.dll" | ForEach-Object { $references.Add($_.FullName) }
}

Get-ChildItem (Join-Path $editorData "Managed\UnityEngine") -Filter "UnityEngine*.dll" |
    ForEach-Object { $references.Add($_.FullName) }

Get-ChildItem (Join-Path $editorData "Managed") -Filter "UnityEditor*.dll" |
    ForEach-Object { $references.Add($_.FullName) }

if ([string]::IsNullOrEmpty($UrpProject)) {
    # Any project with URP compiled will do; the assemblies are identical per URP version.
    $candidates = @(
        "R:\UNITY\Banter\HolloweenHospital"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate "Library\ScriptAssemblies\Unity.RenderPipelines.Universal.Runtime.dll")) {
            $UrpProject = $candidate
            break
        }
    }
}

if ([string]::IsNullOrEmpty($UrpProject)) {
    throw "No URP project found for reference assemblies. Pass -UrpProject with a path to a URP project that has been opened at least once."
}

$scriptAssemblies = Join-Path $UrpProject "Library\ScriptAssemblies"
$urpAssemblies = @(
    "Unity.RenderPipelines.Universal.Runtime.dll",
    "Unity.RenderPipelines.Universal.Editor.dll",
    "Unity.RenderPipelines.Core.Runtime.dll",
    "Unity.RenderPipelines.Core.Editor.dll"
)

foreach ($assembly in $urpAssemblies) {
    $path = Join-Path $scriptAssemblies $assembly
    if (-not (Test-Path $path)) { throw "Missing URP assembly: $path" }
    $references.Add($path)
}

# ---- response files ---------------------------------------------------------
#
# Paths go through response files because "C:\Program Files" would otherwise be split at
# the space, and each entry is quoted for the same reason.

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("sqlt-compile-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    $refsFile = Join-Path $temp "refs.rsp"
    $srcFile = Join-Path $temp "sources.rsp"
    $outFile = Join-Path $temp "SideQuestLightingTools.dll"

    ($references | Sort-Object -Unique | ForEach-Object { '-r:"' + $_ + '"' }) |
        Out-File -FilePath $refsFile -Encoding utf8

    $sources = Get-ChildItem $sourceRoot -Recurse -Filter "*.cs"
    if ($sources.Count -eq 0) { throw "No .cs files found under $sourceRoot" }

    ($sources | ForEach-Object { '"' + $_.FullName + '"' }) |
        Out-File -FilePath $srcFile -Encoding utf8

    Write-Host "Compiling $($sources.Count) file(s) against Unity $UnityVersion ..." -ForegroundColor Cyan

    $output = & dotnet $csc -nologo -target:library -langversion:9.0 -noconfig `
        -define:UNITY_EDITOR -out:$outFile "@$refsFile" "@$srcFile" 2>&1

    $errors = @($output | Select-String -Pattern "error CS" -SimpleMatch)
    $warnings = @($output | Select-String -Pattern "warning CS" -SimpleMatch)

    foreach ($warning in $warnings) { Write-Host $warning -ForegroundColor DarkYellow }
    foreach ($compileError in $errors) { Write-Host $compileError -ForegroundColor Red }

    Write-Host ""
    if ($errors.Count -gt 0) {
        Write-Host "FAILED - $($errors.Count) error(s), $($warnings.Count) warning(s)" -ForegroundColor Red
        exit 1
    }

    Write-Host "OK - 0 errors, $($warnings.Count) warning(s)" -ForegroundColor Green
    exit 0
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
