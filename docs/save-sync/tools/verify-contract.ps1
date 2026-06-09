# Contract verification round 2 (CONTRACT.md §9 remaining items 1,3,4,5) against a live RomM 4.9.x.
# Items covered, in dependency order:
#   4. devices.write scope — register a throwaway device (distinct fingerprint; never touches real devices)
#   1. updated_at format tolerance in POST /api/sync/negotiate (Z-suffix, fractional seconds, +00:00 offset)
#   5. live 409 body shape on POST /api/saves (device-tracked save changed behind our back)
#   3. overwrite=true vs PUT /api/saves/{id} for "keep local" (which advances updated_at / sync mark)
# Uploads clearly-named test saves to a saveless ROM, then deletes them. Test device is left
# registered (named __contract_verify__) unless DELETE /api/devices/{id} exists, in which case it is removed.
# Usage: pwsh -File docs/save-sync/tools/verify-contract.ps1 [-RomId 4]
param(
    [int]$RomId = 4
)
$ErrorActionPreference = 'Stop'

$cfg = Get-Content "$env:LOCALAPPDATA\Playnite\ExtensionsData\9700aa21-447d-41b4-a989-acd38f407d9f\config.json" | ConvertFrom-Json
$H = @{ Authorization = "Bearer $($cfg.RomMApiToken)" }
$base = $cfg.RomMHost
$work = Join-Path $env:TEMP "romm-contract-verify"
New-Item -ItemType Directory -Force $work | Out-Null

function Invoke-Api {
    # Returns @{ Status; Body } and never throws on HTTP errors, so we can inspect 409/403 bodies.
    param([string]$Method, [string]$Url, [hashtable]$Form, [string]$JsonBody)
    try {
        $params = @{ Method = $Method; Uri = $Url; Headers = $H; SkipHttpErrorCheck = $true; StatusCodeVariable = 'sc' }
        if ($Form) { $params.Form = $Form }
        if ($JsonBody) { $params.ContentType = 'application/json'; $params.Body = $JsonBody }
        $body = Invoke-RestMethod @params
        return @{ Status = $sc; Body = $body }
    } catch {
        return @{ Status = -1; Body = $_.Exception.Message }
    }
}

Write-Output "=== Item 4: devices.write — register throwaway device ==="
$devPayload = @{
    name            = "__contract_verify__"
    platform        = "Windows"
    client          = "playnite-plugin"
    client_version  = "0.7.0"
    hostname        = "CONTRACT-VERIFY"
    mac_address     = "02:00:5E:C0:FF:EE"   # locally-administered, never a real NIC
    sync_mode       = "api"
    allow_existing  = $true
    allow_duplicate = $false
    reset_syncs     = $false
} | ConvertTo-Json
$reg = Invoke-Api POST "$base/api/devices" -JsonBody $devPayload
Write-Output "register: HTTP $($reg.Status) body=$($reg.Body | ConvertTo-Json -Compress -Depth 4)"
if ($reg.Status -notin 200, 201) { throw "device registration failed — devices.write scope problem? stop here." }
$deviceId = $reg.Body.device_id
Write-Output "device_id=$deviceId"

# Idempotency probe: same fingerprint again should return the SAME device (200, not 201).
$reg2 = Invoke-Api POST "$base/api/devices" -JsonBody $devPayload
Write-Output "re-register: HTTP $($reg2.Status) same_id=$($reg2.Body.device_id -eq $deviceId)"

Write-Output ""
Write-Output "=== Setup: seed save on rom $RomId (device-tracked) ==="
$rng = [System.Random]::new(20260609)
function New-SaveFile([string]$name, [int]$size) {
    $b = [byte[]]::new($size); $rng.NextBytes($b)
    $p = Join-Path $work $name
    [System.IO.File]::WriteAllBytes($p, $b)
    return $p
}
$v1 = New-SaveFile "__contract_verify__.srm" 8192
$up1 = Invoke-Api POST "$base/api/saves?rom_id=$RomId&device_id=$deviceId" -Form @{ saveFile = (Get-Item $v1) }
Write-Output "v1 upload (with device_id): HTTP $($up1.Status) id=$($up1.Body.id) hash=$($up1.Body.content_hash) updated_at=$($up1.Body.updated_at)"
if ($up1.Status -notin 200, 201) { throw "seed upload failed" }
$saveId = $up1.Body.id
$saveName = $up1.Body.file_name

Write-Output ""
Write-Output "=== Item 1: updated_at format tolerance in /api/sync/negotiate ==="
$formats = @(
    @{ label = "Z-suffix seconds";        ts = "2026-06-09T12:34:56Z" },
    @{ label = "Z-suffix fractional";     ts = "2026-06-09T12:34:56.123456Z" },
    @{ label = "+00:00 offset";           ts = "2026-06-09T12:34:56+00:00" },
    @{ label = "naive (no tz)";           ts = "2026-06-09T12:34:56" }
)
foreach ($f in $formats) {
    $neg = Invoke-Api POST "$base/api/sync/negotiate" -JsonBody (@{
        device_id = $deviceId
        saves = @(@{
            rom_id = $RomId; file_name = $saveName; slot = $null; emulator = $null
            content_hash = $up1.Body.content_hash
            updated_at = $f.ts
            file_size_bytes = 8192
        })
    } | ConvertTo-Json -Depth 4)
    $op = if ($neg.Body.operations) { $neg.Body.operations[0].action } else { "" }
    Write-Output "$($f.label.PadRight(22)) -> HTTP $($neg.Status) action=$op session=$($neg.Body.session_id) err=$(if ($neg.Status -ge 400) { $neg.Body | ConvertTo-Json -Compress -Depth 4 })"
}

Write-Output ""
Write-Output "=== Item 5: live 409 trigger + body shape ==="
# Change the save behind the device's back (upload same name WITHOUT device_id), then re-upload
# with device_id and no overwrite -> expect 409.
$v2 = New-SaveFile "__contract_verify__.srm" 8200
$up2 = Invoke-Api POST "$base/api/saves?rom_id=$RomId" -Form @{ saveFile = (Get-Item $v2) }
Write-Output "v2 upload (no device_id): HTTP $($up2.Status) id=$($up2.Body.id) (same id as v1? $($up2.Body.id -eq $saveId))"
$v3 = New-SaveFile "__contract_verify__.srm" 8208
$up3 = Invoke-Api POST "$base/api/saves?rom_id=$RomId&device_id=$deviceId" -Form @{ saveFile = (Get-Item $v3) }
Write-Output "v3 upload (device_id, no overwrite): HTTP $($up3.Status)"
Write-Output "409 BODY: $($up3.Body | ConvertTo-Json -Compress -Depth 4)"

Write-Output ""
Write-Output "=== Item 3: overwrite=true vs PUT for keep-local ==="
$before = Invoke-Api GET "$base/api/saves/$saveId"
Write-Output "pre  : updated_at=$($before.Body.updated_at) hash=$($before.Body.content_hash)"
$up4 = Invoke-Api POST "$base/api/saves?rom_id=$RomId&device_id=$deviceId&overwrite=true" -Form @{ saveFile = (Get-Item $v3) }
Write-Output "overwrite=true: HTTP $($up4.Status) id=$($up4.Body.id) updated_at=$($up4.Body.updated_at) hash=$($up4.Body.content_hash)"
# Does overwrite advance THIS device's sync mark? Negotiate again with v3's local state: expect no_op if marked current.
$v3hash = (Get-FileHash -Algorithm MD5 $v3).Hash.ToLowerInvariant()
$neg2 = Invoke-Api POST "$base/api/sync/negotiate" -JsonBody (@{
    device_id = $deviceId
    saves = @(@{ rom_id = $RomId; file_name = $saveName; slot = $null; emulator = $null
                 content_hash = $v3hash; updated_at = ([DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                 file_size_bytes = 8208 })
} | ConvertTo-Json -Depth 4)
$op2 = if ($neg2.Body.operations) { $neg2.Body.operations[0].action } else { "" }
Write-Output "negotiate after overwrite: HTTP $($neg2.Status) action=$op2 (no_op => overwrite advanced sync mark)"
# PUT path: replace content via PUT /api/saves/{id}
$v4 = New-SaveFile "__contract_verify__.srm" 8216
$put = Invoke-Api PUT "$base/api/saves/$saveId" -Form @{ saveFile = (Get-Item $v4) }
Write-Output "PUT /api/saves/{id}: HTTP $($put.Status) updated_at=$($put.Body.updated_at) hash=$($put.Body.content_hash)"

Write-Output ""
Write-Output "=== Cleanup ==="
$allIds = @($saveId, $up2.Body.id, $up3.Body.id, $up4.Body.id) | Where-Object { $_ } | Select-Object -Unique
$del = Invoke-Api POST "$base/api/saves/delete" -JsonBody (@{ saves = $allIds } | ConvertTo-Json)
Write-Output "delete saves $($allIds -join ','): HTTP $($del.Status)"
$remaining = Invoke-Api GET "$base/api/saves?rom_id=$RomId"
Write-Output "remaining saves on rom ${RomId}: $($remaining.Body.Count)"
$delDev = Invoke-Api DELETE "$base/api/devices/$deviceId"
Write-Output "delete test device: HTTP $($delDev.Status) (404/405 = endpoint absent, device left behind named __contract_verify__)"
Write-Output ""
Write-Output "VERIFY-CONTRACT COMPLETE — paste results into CONTRACT.md §9"
