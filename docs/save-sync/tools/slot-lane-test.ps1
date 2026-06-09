# End-to-end slot-lane verification against RomM >= 4.9.0-beta.2, mirroring the plugin's flow
# after the SyncSlots.Live ("default") fix. Covers CONTRACT.md §9 items 1/3/5 under the REAL
# named-slot semantics (negotiate ignores null-slot rows entirely):
#   - upload to slot=default with device_id -> negotiate identical -> no_op for our rom
#   - server changes behind our back -> negotiate -> download; blind re-upload -> slot-lane 409
#   - overwrite=true -> 200 and advances this device's sync mark (next negotiate = no_op)
# Uses a throwaway device + clearly-named saves on a saveless ROM; deletes everything after.
# Usage: pwsh -File docs/save-sync/tools/slot-lane-test.ps1 [-RomId 4]
param(
    [int]$RomId = 4
)
$ErrorActionPreference = 'Stop'

$cfg = Get-Content "$env:LOCALAPPDATA\Playnite\ExtensionsData\9700aa21-447d-41b4-a989-acd38f407d9f\config.json" | ConvertFrom-Json
$H = @{ Authorization = "Bearer $($cfg.RomMApiToken)" }
$base = $cfg.RomMHost
$work = Join-Path $env:TEMP "romm-slotlane"
New-Item -ItemType Directory -Force $work | Out-Null
$SLOT = "default"

function Invoke-Api {
    param([string]$Method, [string]$Url, [hashtable]$Form, [string]$JsonBody)
    $params = @{ Method = $Method; Uri = $Url; Headers = $H; SkipHttpErrorCheck = $true; StatusCodeVariable = 'sc' }
    if ($Form) { $params.Form = $Form }
    if ($JsonBody) { $params.ContentType = 'application/json'; $params.Body = $JsonBody }
    $body = Invoke-RestMethod @params
    return @{ Status = $sc; Body = $body }
}

function Invoke-Negotiate([string]$deviceId, [string]$hash, [long]$size, [string]$ts) {
    $neg = Invoke-Api POST "$base/api/sync/negotiate" -JsonBody (@{
        device_id = $deviceId
        saves = @(@{
            rom_id = $RomId; file_name = "__slotlane__.srm"; slot = $SLOT; emulator = $null
            content_hash = $hash; updated_at = $ts; file_size_bytes = $size
        })
    } | ConvertTo-Json -Depth 4)
    $mine = @($neg.Body.operations | Where-Object { $_.rom_id -eq $RomId -and $_.slot -eq $SLOT })
    $others = @($neg.Body.operations | Where-Object { $_.rom_id -ne $RomId -or $_.slot -ne $SLOT })
    return @{ Status = $neg.Status; Mine = $mine; OtherCount = $others.Count; Session = $neg.Body.session_id }
}

$reg = Invoke-Api POST "$base/api/devices" -JsonBody (@{
    name = "__contract_verify__"; platform = "Windows"; client = "playnite-plugin"
    client_version = "0.7.0"; hostname = "CONTRACT-VERIFY"; mac_address = "02:00:5E:C0:FF:EE"
    sync_mode = "api"; allow_existing = $true; allow_duplicate = $false; reset_syncs = $false
} | ConvertTo-Json)
$dev = $reg.Body.device_id
Write-Output "device: HTTP $($reg.Status) id=$dev"

function New-Bytes([int]$seed, [int]$size) {
    $b = [byte[]]::new($size); ([System.Random]::new($seed)).NextBytes($b); return $b
}
function Save-Fixture([byte[]]$bytes) {
    $p = Join-Path $work "__slotlane__.srm"
    [System.IO.File]::WriteAllBytes($p, $bytes)
    return $p
}
function Md5([byte[]]$bytes) {
    $md5 = [System.Security.Cryptography.MD5]::Create()
    return ([BitConverter]::ToString($md5.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
}
$nowTs = { [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }

Write-Output ""
Write-Output "--- 1. upload v1 (slot=$SLOT, device_id) ---"
$v1 = New-Bytes 1001 8192
$up1 = Invoke-Api POST "$base/api/saves?rom_id=$RomId&device_id=$dev&slot=$SLOT" -Form @{ saveFile = (Get-Item (Save-Fixture $v1)) }
Write-Output "HTTP $($up1.Status) id=$($up1.Body.id) stored_name='$($up1.Body.file_name)' slot=$($up1.Body.slot) hash=$($up1.Body.content_hash)"
$ids = [System.Collections.Generic.List[int]]::new(); $ids.Add([int]$up1.Body.id)

Write-Output ""
Write-Output "--- 2. negotiate identical -> expect no_op for our rom ---"
$n1 = Invoke-Negotiate $dev (Md5 $v1) $v1.Length (& $nowTs)
Write-Output "HTTP $($n1.Status) ourOps=$($n1.Mine | ConvertTo-Json -Compress -Depth 4) otherOps(count)=$($n1.OtherCount)"

Write-Output ""
Write-Output "--- 3. server changes behind our back (upload v2, NO device_id) ---"
$v2 = New-Bytes 1002 8192
$up2 = Invoke-Api POST "$base/api/saves?rom_id=$RomId&slot=$SLOT" -Form @{ saveFile = (Get-Item (Save-Fixture $v2)) }
Write-Output "HTTP $($up2.Status) id=$($up2.Body.id) (new row in slot? $($up2.Body.id -ne $up1.Body.id))"
if ($up2.Body.id -and -not $ids.Contains([int]$up2.Body.id)) { $ids.Add([int]$up2.Body.id) }

Write-Output ""
Write-Output "--- 4. negotiate with OLD local v1 -> expect download ---"
$n2 = Invoke-Negotiate $dev (Md5 $v1) $v1.Length ([DateTime]::UtcNow.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ssZ"))
Write-Output "HTTP $($n2.Status) ourOps=$($n2.Mine | ConvertTo-Json -Compress -Depth 4)"

Write-Output ""
Write-Output "--- 5. blind upload v3 (device_id, no overwrite) -> expect slot-lane 409 ---"
$v3 = New-Bytes 1003 8192
$up3 = Invoke-Api POST "$base/api/saves?rom_id=$RomId&device_id=$dev&slot=$SLOT" -Form @{ saveFile = (Get-Item (Save-Fixture $v3)) }
Write-Output "HTTP $($up3.Status) body=$($up3.Body | ConvertTo-Json -Compress -Depth 4)"

Write-Output ""
Write-Output "--- 6. upload v3 with overwrite=true -> expect 200 ---"
$up4 = Invoke-Api POST "$base/api/saves?rom_id=$RomId&device_id=$dev&slot=$SLOT&overwrite=true" -Form @{ saveFile = (Get-Item (Save-Fixture $v3)) }
Write-Output "HTTP $($up4.Status) id=$($up4.Body.id) stored_name='$($up4.Body.file_name)' hash=$($up4.Body.content_hash)"
if ($up4.Body.id -and -not $ids.Contains([int]$up4.Body.id)) { $ids.Add([int]$up4.Body.id) }

Write-Output ""
Write-Output "--- 7. negotiate with v3 -> expect no_op (overwrite advanced sync mark) ---"
$n3 = Invoke-Negotiate $dev (Md5 $v3) $v3.Length (& $nowTs)
Write-Output "HTTP $($n3.Status) ourOps=$($n3.Mine | ConvertTo-Json -Compress -Depth 4)"

Write-Output ""
Write-Output "--- 8. complete the last session (playtime ingest smoke test) ---"
$end = [DateTime]::UtcNow; $start = $end.AddMinutes(-5)
$cp = Invoke-Api POST "$base/api/sync/sessions/$($n3.Session)/complete" -JsonBody (@{
    operations_completed = 1; operations_failed = 0
    play_sessions = @(@{ rom_id = $RomId; save_slot = $SLOT
        start_time = $start.ToString("yyyy-MM-ddTHH:mm:ssZ"); end_time = $end.ToString("yyyy-MM-ddTHH:mm:ssZ")
        duration_ms = [long]($end - $start).TotalMilliseconds })
} | ConvertTo-Json -Depth 4)
Write-Output "HTTP $($cp.Status) session_status=$($cp.Body.session.status) play_ingest=$($cp.Body.play_session_ingest | ConvertTo-Json -Compress -Depth 5)"

Write-Output ""
Write-Output "--- cleanup ---"
# Catch any slot rows the above created that we didn't track (datetime-tagged rows accrue).
$all = Invoke-Api GET "$base/api/saves?rom_id=$RomId"
foreach ($s in $all.Body) { if ($s.file_name -like "__slotlane__*" -and -not $ids.Contains([int]$s.id)) { $ids.Add([int]$s.id) } }
$idsJson = "[" + (($ids | ForEach-Object { $_ }) -join ",") + "]"
$del = Invoke-Api POST "$base/api/saves/delete" -JsonBody "{`"saves`":$idsJson}"
Write-Output "delete saves $idsJson : HTTP $($del.Status)"
$delDev = Invoke-Api DELETE "$base/api/devices/$dev"
Write-Output "delete device: HTTP $($delDev.Status)"
$remaining = Invoke-Api GET "$base/api/saves?rom_id=$RomId"
Write-Output "remaining saves on rom ${RomId}: $($remaining.Body.Count)"
