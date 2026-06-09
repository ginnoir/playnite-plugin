# Golden hash test (CONTRACT.md §4): verify the server's content_hash matches the SHIPPED
# SaveHashing implementation, for both the raw-bytes path and the zip-composite path.
# Uploads two clearly-named test saves to a ROM with no existing saves, then deletes them.
# Usage: pwsh -File docs/save-sync/tools/golden-hash-test.ps1 [-RomId 4]
param(
    [int]$RomId = 4,
    [string]$BinDir = "$PSScriptRoot\..\..\..\bin\Release\net462"
)
$ErrorActionPreference = 'Stop'

$cfg = Get-Content "$env:LOCALAPPDATA\Playnite\ExtensionsData\9700aa21-447d-41b4-a989-acd38f407d9f\config.json" | ConvertFrom-Json
$H = @{ Authorization = "Bearer $($cfg.RomMApiToken)" }
$base = $cfg.RomMHost
$work = Join-Path $env:TEMP "romm-goldenhash"
New-Item -ItemType Directory -Force $work | Out-Null

# Load the shipped hashing code from the built plugin so we test the real implementation.
[void][System.Reflection.Assembly]::LoadFrom((Resolve-Path "$BinDir\SharpCompress.dll"))
$asm = [System.Reflection.Assembly]::LoadFrom((Resolve-Path "$BinDir\RomM.dll"))
$hashType = $asm.GetType("RomM.SaveSync.SaveHashing")
$computeHash = $hashType.GetMethod("ComputeContentHash", [System.Reflection.BindingFlags]"NonPublic,Public,Static", $null, [type[]]@([byte[]]), $null)
if (-not $computeHash) { throw "could not bind SaveHashing.ComputeContentHash(byte[])" }

# Fixture 1: raw 32 KB SRAM-like save (seeded → reproducible).
$raw = [byte[]]::new(32768)
$rng = [System.Random]::new(20260609)
$rng.NextBytes($raw)
$rawPath = Join-Path $work "__savesync_goldentest__.srm"
[System.IO.File]::WriteAllBytes($rawPath, $raw)
$localRawHash = $computeHash.Invoke($null, @(,$raw))

# Fixture 2: multi-entry zip, entries added out of sorted order to exercise the ordinal sort.
$zipPath = Join-Path $work "__savesync_goldentest_zip__.srm"
if (Test-Path $zipPath) { Remove-Item $zipPath }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$fs = [System.IO.File]::Create($zipPath)
$zip = [System.IO.Compression.ZipArchive]::new($fs, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($name in @("b_entry.sav", "A_entry.sav")) {
    $entry = $zip.CreateEntry($name)
    $es = $entry.Open()
    $buf = [byte[]]::new(2048); $rng.NextBytes($buf)
    $es.Write($buf, 0, $buf.Length); $es.Dispose()
}
$zip.Dispose(); $fs.Dispose()
$zipBytes = [System.IO.File]::ReadAllBytes($zipPath)
$localZipHash = $computeHash.Invoke($null, @(,$zipBytes))

# Upload both. Multipart field name 'saveFile', params in the query string (LIVE item 2).
$up1 = Invoke-RestMethod -Method Post "$base/api/saves?rom_id=$RomId" -Headers $H -Form @{ saveFile = (Get-Item $rawPath) }
$up2 = Invoke-RestMethod -Method Post "$base/api/saves?rom_id=$RomId" -Headers $H -Form @{ saveFile = (Get-Item $zipPath) }

Write-Output "RAW : local=$localRawHash server=$($up1.content_hash) match=$($localRawHash -eq $up1.content_hash)"
Write-Output "ZIP : local=$localZipHash server=$($up2.content_hash) match=$($localZipHash -eq $up2.content_hash)"
Write-Output "raw save: id=$($up1.id) file_name='$($up1.file_name)' size=$($up1.file_size_bytes) slot='$($up1.slot)' emulator='$($up1.emulator)'"
Write-Output "zip save: id=$($up2.id) file_name='$($up2.file_name)' size=$($up2.file_size_bytes)"

# Cleanup: remove both test saves from the server.
$null = Invoke-RestMethod -Method Post "$base/api/saves/delete" -Headers $H -ContentType 'application/json' -Body (@{ saves = @($up1.id, $up2.id) } | ConvertTo-Json)
Write-Output "cleanup: deleted save ids $($up1.id), $($up2.id)"
$remaining = Invoke-RestMethod "$base/api/saves?rom_id=$RomId" -Headers $H
Write-Output "remaining saves on rom ${RomId}: $($remaining.Count)"

if (($localRawHash -ne $up1.content_hash) -or ($localZipHash -ne $up2.content_hash)) {
    Write-Error "GOLDEN HASH TEST FAILED — fix SaveHashing before anything else (every save would read as a conflict)."
}
Write-Output "GOLDEN HASH TEST PASSED"
