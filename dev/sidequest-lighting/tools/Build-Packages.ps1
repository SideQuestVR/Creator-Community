# SideQuest Lighting Tools - MIT
#
# Builds the Creator-Community package folders from the single editable tree in dev/.
#
# Three jobs, in order of how badly each fails silently:
#
# 1. Verify every .meta GUID against GUIDS.md. Each tool package embeds a byte-identical
#    copy of Core at the same path; a .unitypackage import is keyed on GUID AND path, so
#    matching GUIDs make the second import overwrite rather than duplicate. If a GUID
#    drifts, a user importing two tools gets CS0101 duplicate types and no clue why.
#
# 2. Write a frozen src/ snapshot into each version folder. CONTRIBUTING asks reviewers to
#    check exact file contents and to treat submitted code as data; nobody can read a
#    gzipped tar. The snapshot makes each published version independently auditable and
#    lets a maintainer rebuild the artifact to compare without trusting this script.
#
# 3. Validate each listing.json against the fields and patterns the repository schema
#    requires. Windows PowerShell 5.1 has no draft 2020-12 validator, so the ~20 rules are
#    checked explicitly here rather than pretending a Test-Json call covered them.
#
# The .unitypackage export is optional and off by default, because it needs a Unity
# install and a scratch project. Without it, listings simply carry no download block -
# which the schema permits, and which is the correct state for a listing under review.
#
#   .\Build-Packages.ps1                 # verify, snapshot, validate
#   .\Build-Packages.ps1 -Export         # also export .unitypackage files and hash them

[CmdletBinding()]
param(
    [switch] $Export,
    [string] $UnityVersion = "6000.3.21f1",
    [string] $UnityRoot = "C:\Program Files\Unity\Hub\Editor",

    # A URP project to borrow a package manifest from when exporting. The scratch project
    # must have URP or the sources do not compile and Unity will not export anything.
    [string] $ReferenceProject = "R:\UNITY\Creator-Community"
)

$ErrorActionPreference = "Stop"

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$devDir = Split-Path -Parent $toolsDir
$repoRoot = Split-Path -Parent (Split-Path -Parent $devDir)

$manifestPath = Join-Path $devDir "package-manifest.json"
$guidsPath = Join-Path $devDir "GUIDS.md"
$sourceAssets = Join-Path $devDir "Assets"

if (-not (Test-Path $manifestPath)) { throw "package-manifest.json not found at $manifestPath" }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

$packagesDir = Join-Path $repoRoot "packages"
$failures = New-Object System.Collections.Generic.List[string]

# ---- 1. GUID verification ---------------------------------------------------

function Test-Guids {
    if (-not (Test-Path $guidsPath)) { throw "GUIDS.md not found at $guidsPath" }

    $expected = @{}
    foreach ($line in Get-Content $guidsPath) {
        if ($line -match '^\|\s*`(?<path>[^`]+)`\s*\|\s*`(?<guid>[0-9a-f]{32})`\s*\|') {
            $expected[$Matches.path] = $Matches.guid
        }
    }

    if ($expected.Count -eq 0) { throw "GUIDS.md contains no entries - regenerate it" }

    $drifted = 0
    $missing = 0

    foreach ($entry in $expected.GetEnumerator()) {
        $metaPath = Join-Path $devDir ($entry.Key + ".meta")
        if (-not (Test-Path $metaPath)) {
            Write-Host "  MISSING .meta: $($entry.Key)" -ForegroundColor Red
            $missing++
            continue
        }

        $content = Get-Content $metaPath -Raw
        if ($content -notmatch 'guid:\s*(?<guid>[0-9a-f]{32})') {
            Write-Host "  NO GUID in .meta: $($entry.Key)" -ForegroundColor Red
            $missing++
            continue
        }

        if ($Matches.guid -ne $entry.Value) {
            Write-Host "  GUID DRIFT: $($entry.Key)" -ForegroundColor Red
            Write-Host "    expected $($entry.Value), found $($Matches.guid)" -ForegroundColor Red
            $drifted++
        }
    }

    Write-Host "GUIDs: $($expected.Count) checked, $drifted drifted, $missing missing" -ForegroundColor Cyan

    if ($drifted -gt 0 -or $missing -gt 0) {
        $failures.Add("GUID verification failed - the shared-core overwrite depends on these being stable")
    }
}

# ---- 2. src/ snapshot -------------------------------------------------------

# Every folder .meta a package needs, from the Assets root down to each of its trees.
# These are what keep folder GUIDs identical across packages; without them two packages
# describe the same path with different GUIDs.
function Get-FolderMetaPaths($package) {
    $paths = New-Object System.Collections.Generic.List[string]
    $paths.Add("SideQuest.meta")
    $paths.Add("SideQuest/LightingTools.meta")
    foreach ($tree in $package.trees) { $paths.Add("SideQuest/LightingTools/$tree.meta") }
    return $paths
}

function Copy-Snapshot($package, $versionDir) {
    $srcDir = Join-Path $versionDir "src"
    if (Test-Path $srcDir) { Remove-Item $srcDir -Recurse -Force }

    $fileCount = 0

    foreach ($tree in $package.trees) {
        $from = Join-Path $sourceAssets "SideQuest/LightingTools/$tree"
        if (-not (Test-Path $from)) { throw "source tree not found: $from" }

        $to = Join-Path $srcDir "Assets/SideQuest/LightingTools/$tree"
        New-Item -ItemType Directory -Path $to -Force | Out-Null

        # .meta files are copied deliberately. GUID stability is load-bearing, and the
        # asmdefs are exactly what a reviewer needs to see.
        Copy-Item (Join-Path $from "*") $to -Recurse -Force
        $fileCount += (Get-ChildItem $to -Recurse -File).Count
    }

    # Folder .meta files are SIBLINGS of the folders they describe, so copying a tree's
    # contents leaves its own .meta behind and Unity invents a fresh GUID for the folder
    # on every export. That produced a different GUID for Assets/SideQuest/LightingTools/Core
    # in each package - the exact drift the frozen GUIDs exist to prevent.
    foreach ($meta in (Get-FolderMetaPaths $package)) {
        $from = Join-Path $sourceAssets $meta
        if (-not (Test-Path $from)) { throw "folder meta missing: $from" }

        $to = Join-Path $srcDir "Assets/$meta"
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        Copy-Item $from $to -Force
        $fileCount++
    }

    return $fileCount
}

# ---- MANIFEST.txt -----------------------------------------------------------

function Write-Manifest($package, $versionDir) {
    # A .unitypackage is a gzipped tar whose entries carry modification times, so it is
    # NOT bit-reproducible between machines - two correct builds of identical sources
    # produce different bytes and different hashes.
    #
    # download.sha256 pins the exact published bytes, which is all the schema asks. This
    # file is the other half: per-file content hashes of the sources inside, so a reviewer
    # can verify what the archive contains without unpacking it, and a rebuild can be
    # compared against the original by content rather than by archive hash.
    $srcDir = Join-Path $versionDir "src"
    if (-not (Test-Path $srcDir)) { return 0 }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# $($package.id) $($manifest.suiteVersion) - source file hashes")
    $lines.Add("#")
    $lines.Add("# SHA-256 of each file in src/, which is the source inside the .unitypackage.")
    $lines.Add("# The archive itself is not bit-reproducible (gzip tar stores mtimes), so verify")
    $lines.Add("# contents against this and the published archive against download.sha256.")
    $lines.Add("")

    $files = Get-ChildItem $srcDir -Recurse -File | Sort-Object FullName
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($srcDir.Length + 1).Replace("\", "/")
        $hash = (Get-FileHash -Algorithm SHA256 $file.FullName).Hash.ToLowerInvariant()
        $lines.Add("$hash  $relative")
    }

    $manifestFile = Join-Path $versionDir "MANIFEST.txt"
    ($lines -join "`n") + "`n" | Out-File -FilePath $manifestFile -Encoding utf8 -NoNewline

    return $files.Count
}

# ---- 3. listing validation --------------------------------------------------

function Test-Listing($listingPath) {
    $issues = New-Object System.Collections.Generic.List[string]
    $listing = Get-Content $listingPath -Raw | ConvertFrom-Json

    $required = @("schemaVersion", "id", "version", "name", "category", "description",
                  "author", "license", "licensePath", "instructionsPath", "compatibility",
                  "dependencies", "includesCode", "scope", "testNotes")

    foreach ($field in $required) {
        if ($null -eq $listing.$field) { $issues.Add("missing required field: $field") }
    }

    if ($listing.schemaVersion -ne 1) { $issues.Add("schemaVersion must be 1") }

    if ($listing.id -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)+$') { $issues.Add("id does not match the schema pattern: $($listing.id)") }
    if ($listing.version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') { $issues.Add("version is not semver: $($listing.version)") }

    $categories = @("prefab", "graph", "recipe", "plugin", "community-tool")
    if ($categories -notcontains $listing.category) { $issues.Add("category not in enum: $($listing.category)") }

    $scopes = @("editor-only", "runtime", "both", "instructions-only")
    if ($scopes -notcontains $listing.scope) { $issues.Add("scope not in enum: $($listing.scope)") }

    # repositoryPath: forward slashes, at least two segments, and the FIRST segment may
    # not contain a dot - which is why these live under packages/ rather than at the root.
    foreach ($pathField in @("licensePath", "instructionsPath")) {
        $value = $listing.$pathField
        if ($null -eq $value) { continue }
        if ($value -notmatch '^[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-][A-Za-z0-9._-]*)+$') {
            $issues.Add("$pathField does not match repositoryPath pattern: $value")
        }

        $onDisk = Join-Path $repoRoot $value
        if (-not (Test-Path $onDisk)) { $issues.Add("$pathField points at a file that does not exist: $value") }
    }

    foreach ($versionField in @("unity", "creatorSdk", "banterSdk")) {
        if ($null -eq $listing.compatibility.$versionField) { $issues.Add("compatibility.$versionField is missing") }
    }

    if ($listing.description.Length -gt 2000) { $issues.Add("description exceeds 2000 characters") }
    if ($listing.testNotes.Length -gt 4000) { $issues.Add("testNotes exceeds 4000 characters") }

    if ($null -ne $listing.download) {
        if ($null -eq $listing.download.sha256) { $issues.Add("download block has no sha256") }
        elseif ($listing.download.sha256 -cnotmatch '^[a-f0-9]{64}$') { $issues.Add("download.sha256 must be 64 lowercase hex characters") }

        if ($null -eq $listing.download.byteLength) { $issues.Add("download block has no byteLength") }
        if ($null -ne $listing.download.path -and $null -ne $listing.download.url) { $issues.Add("download has both path and url; the schema allows exactly one") }
    }

    return $issues
}

# ---- optional .unitypackage export -----------------------------------------

function Export-Package($package, $versionDir) {
    $unity = Join-Path $UnityRoot "$UnityVersion\Editor\Unity.exe"
    if (-not (Test-Path $unity)) { throw "Unity not found at $unity" }

    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("sqlt-pkg-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
    $scratchAssets = Join-Path $scratch "Assets/SideQuest/LightingTools"
    New-Item -ItemType Directory -Path $scratchAssets -Force | Out-Null

    # The scratch project needs URP, or nothing compiles and Unity refuses to export at
    # all. A bare project gets Unity's default manifest, which has no render pipeline -
    # so the package manifest is copied from a real URP project rather than written by
    # hand, which would mean guessing package versions that have to match this Editor.
    $referenceManifest = Join-Path $ReferenceProject "Packages/manifest.json"
    if (-not (Test-Path $referenceManifest)) {
        throw "No URP project to take a package manifest from at $ReferenceProject. Pass -ReferenceProject with a URP project path."
    }

    New-Item -ItemType Directory -Path (Join-Path $scratch "Packages") -Force | Out-Null
    Copy-Item $referenceManifest (Join-Path $scratch "Packages/manifest.json") -Force

    $referenceLock = Join-Path $ReferenceProject "Packages/packages-lock.json"
    if (Test-Path $referenceLock) {
        Copy-Item $referenceLock (Join-Path $scratch "Packages/packages-lock.json") -Force
    }

    try {
        foreach ($tree in $package.trees) {
            Copy-Item (Join-Path $sourceAssets "SideQuest/LightingTools/$tree") $scratchAssets -Recurse -Force
        }

        # Folder .meta files, including each tree's own. Without them Unity assigns fresh
        # folder GUIDs per export, so the same path ends up with different GUIDs in
        # different packages.
        foreach ($meta in (Get-FolderMetaPaths $package)) {
            $from = Join-Path $sourceAssets $meta
            if (-not (Test-Path $from)) { throw "folder meta missing: $from" }
            Copy-Item $from (Join-Path $scratch "Assets/$meta") -Force
        }

        $outFile = Join-Path $versionDir "$($package.artifact)-$($manifest.suiteVersion).unitypackage"

        # Keep the Unity log. Discarding it turns every batchmode failure - a licence in
        # use, a compile error, a bad path - into the same unhelpful "did not produce".
        $logFile = Join-Path $scratch "unity-export.log"

        Write-Host "  exporting via Unity batchmode (this takes a minute)..." -ForegroundColor DarkGray
        & $unity -batchmode -quit -nographics -projectPath $scratch `
            -exportPackage "Assets/SideQuest" $outFile -logFile $logFile | Out-Null

        if (-not (Test-Path $outFile)) {
            $detail = "no Unity log was written"
            if (Test-Path $logFile) {
                $lines = Get-Content $logFile | Where-Object { $_ -match 'error|Error|licen|Licen|fail|Fail' } | Select-Object -Last 8
                if ($lines) { $detail = ($lines -join "`n    ") }
                Copy-Item $logFile (Join-Path $env:TEMP "sqlt-unity-export.log") -Force
            }
            throw "Unity did not produce $outFile`n    $detail`n    full log copied to $env:TEMP\sqlt-unity-export.log"
        }

        return $outFile
    }
    finally {
        Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Update-Download($listingPath, $artifactPath, $package) {
    $hash = (Get-FileHash -Algorithm SHA256 $artifactPath).Hash.ToLowerInvariant()
    $bytes = (Get-Item $artifactPath).Length
    $relative = "packages/$($package.id)/$($manifest.suiteVersion)/" + (Split-Path -Leaf $artifactPath)

    $listing = Get-Content $listingPath -Raw | ConvertFrom-Json
    $listing | Add-Member -NotePropertyName download -NotePropertyValue ([pscustomobject]@{
        path       = $relative
        byteLength = $bytes
        sha256     = $hash
    }) -Force

    ($listing | ConvertTo-Json -Depth 10) | Out-File -FilePath $listingPath -Encoding utf8
    Write-Host "  download: $bytes bytes, sha256 $($hash.Substring(0,16))..." -ForegroundColor DarkGray
}

# ---- run --------------------------------------------------------------------

Write-Host "SideQuest Lighting Tools - package build" -ForegroundColor Cyan
Write-Host "suite version $($manifest.suiteVersion)`n"

Test-Guids
Write-Host ""

foreach ($package in $manifest.packages) {
    Write-Host "$($package.id)" -ForegroundColor Cyan

    $versionDir = Join-Path $packagesDir "$($package.id)/$($manifest.suiteVersion)"
    if (-not (Test-Path $versionDir)) {
        $failures.Add("$($package.id): version folder missing at $versionDir")
        Write-Host "  version folder missing" -ForegroundColor Red
        continue
    }

    $fileCount = Copy-Snapshot $package $versionDir
    Write-Host "  src snapshot: $fileCount file(s)" -ForegroundColor DarkGray

    $hashed = Write-Manifest $package $versionDir
    Write-Host "  MANIFEST.txt: $hashed file hash(es)" -ForegroundColor DarkGray

    if ($Export) {
        $artifact = Export-Package $package $versionDir
        Update-Download (Join-Path $versionDir "listing.json") $artifact $package
    }

    $listingPath = Join-Path $versionDir "listing.json"
    if (-not (Test-Path $listingPath)) {
        $failures.Add("$($package.id): listing.json missing")
        Write-Host "  listing.json missing" -ForegroundColor Red
        continue
    }

    $issues = Test-Listing $listingPath
    if ($issues.Count -eq 0) {
        Write-Host "  listing: ok" -ForegroundColor Green
    }
    else {
        foreach ($issue in $issues) {
            Write-Host "  listing: $issue" -ForegroundColor Red
            $failures.Add("$($package.id): $issue")
        }
    }
}

Write-Host ""

# index.json is a maintainer step, so this prints the block to paste rather than
# editing the file. CONTRIBUTING is explicit that a contributor does not touch it.
Write-Host "index.json entries for the maintainer to add after review:" -ForegroundColor Cyan
foreach ($package in $manifest.packages) {
    Write-Host "  `"packages/$($package.id)/$($manifest.suiteVersion)/listing.json`","
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "FAILED - $($failures.Count) problem(s)" -ForegroundColor Red
    exit 1
}

Write-Host "OK" -ForegroundColor Green
if (-not $Export) {
    Write-Host "No .unitypackage built. Listings carry no download block, which the schema permits for a listing under review. Re-run with -Export to build and hash the artifacts." -ForegroundColor DarkGray
}
exit 0
