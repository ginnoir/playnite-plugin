# Follow-up probe for the negotiate "upload instead of no_op" anomaly seen in verify-contract.ps1
# on RomM 4.9.0-beta.2, and cleanup of the leaked test save (id from -CleanupSaveId).
# Dumps the FULL negotiate response (all operations + reasons + totals) for an identical-hash save.
# Usage: pwsh -File docs/save-sync/tools/probe-negotiate.ps1 [-RomId 4] [-CleanupSaveId 35]
param(
    [int]$RomId = 4,
    [int]$CleanupSaveId = 0
)
$ErrorActionPreference = 'Stop'

$cfg = Get-Content "$env:LOCALAPPDATA\Playnite\ExtensionsData\9700aa21-447d-41b4-a989-acd38f407d9f\config.json" | ConvertFrom-Json
$H = @{ Authorization = "Bearer $($cfg.RomMApiToken)" }
$base = $cfg.RomMHost
$work = Join-Path $env:TEMP "romm-contract-verify"
New-Item -ItemType Directory -Force $work | Out-Null

function Invoke-Api {
    param([string]$Method, [string]$Url, [hashtable]$Form, [string]$JsonBody)
    $params = @{ Method = $Method; Uri = $Url; Headers = $H; SkipHttpErrorCheck = $true; StatusCodeVariable = 'sc' }
    if ($Form) { $params.Form = $Form }
    if ($JsonBody) { $params.ContentType = 'application/json'; $params.Body = $JsonBody }
    $body = Invoke-RestMethod @params
    return @{ Status = $sc; Body = $body }
}

if ($CleanupSaveId -gt 0) {
    # NB: build the JSON by hand — ConvertTo-Json unwraps single-element arrays.
    $del = Invoke-Api POST "$base/api/saves/delete" -JsonBody "{`"saves`":[$CleanupSaveId]}"
    Write-Output "cleanup save ${CleanupSaveId}: HTTP $($del.Status)"
}

Write-Output "=== re-register probe device ==="
$reg = Invoke-Api POST "$base/api/devices" -JsonBody (@{
    name = "__contract_verify__"; platform = "Windows"; client = "playnite-plugin"
    client_version = "0.7.0"; hostname = "CONTRACT-VERIFY"; mac_address = "02:00:5E:C0:FF:EE"
    sync_mode = "api"; allow_existing = $true; allow_duplicate = $false; reset_syncs = $false
} | ConvertTo-Json)
$deviceId = $reg.Body.device_id
Write-Output "device: HTTP $($reg.Status) id=$deviceId"

Write-Output ""
Write-Output "=== seed fresh save (with device_id) ==="
$rng = [System.Random]::new(424242)
$bytes = [byte[]]::new(8192); $rng.NextBytes($bytes)
$p = Join-Path $work "__negotiate_probe__.srm"
[System.IO.File]::WriteAllBytes($p, $bytes)
$localHash = (Get-FileHash -Algorithm MD5 $p).Hash.ToLowerInvariant()
$up = Invoke-Api POST "$base/api/saves?rom_id=$RomId&device_id=$deviceId" -Form @{ saveFile = (Get-Item $p) }
Write-Output "upload: HTTP $($up.Status) id=$($up.Body.id) server_hash=$($up.Body.content_hash) local_hash=$localHash match=$($up.Body.content_hash -eq $localHash)"
$saveId = $up.Body.id

Write-Output ""
Write-Output "=== GET save back (check slot/emulator/file_name as stored) ==="
$got = Invoke-Api GET "$base/api/saves/$saveId"
Write-Output ($got.Body | Select-Object id, file_name, slot, emulator, content_hash, updated_at, missing_from_fs | ConvertTo-Json -Compress)

Write-Output ""
Write-Output "=== negotiate with IDENTICAL state (expect no_op) — FULL RESPONSE ==="
$neg = Invoke-Api POST "$base/api/sync/negotiate" -JsonBody (@{
    device_id = $deviceId
    saves = @(@{
        rom_id = $RomId; file_name = $got.Body.file_name; slot = $null; emulator = $null
        content_hash = $localHash
        updated_at = ([DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
        file_size_bytes = $bytes.Length
    })
} | ConvertTo-Json -Depth 4)
Write-Output "HTTP $($neg.Status)"
Write-Output ($neg.Body | ConvertTo-Json -Depth 6)

Write-Output ""
Write-Output "=== cleanup ==="
$del2 = Invoke-Api POST "$base/api/saves/delete" -JsonBody "{`"saves`":[$saveId]}"
Write-Output "delete save ${saveId}: HTTP $($del2.Status)"
$delDev = Invoke-Api DELETE "$base/api/devices/$deviceId"
Write-Output "delete device: HTTP $($delDev.Status)"
$remaining = Invoke-Api GET "$base/api/saves?rom_id=$RomId"
Write-Output "remaining saves on rom ${RomId}: $($remaining.Body.Count)"
