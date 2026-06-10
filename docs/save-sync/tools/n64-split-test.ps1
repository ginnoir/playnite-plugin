# Validate the SHIPPED N64SaveConverter.FromCanonical split against a REAL steamdeck .srm pulled
# from the live server (no emulator needed). Downloads the canonical 296960-byte RetroArch
# mupen64plus-next save, runs it through the plugin's converter via reflection, and prints the
# component files + sizes the standalone path would write. Proves the split logic on production data;
# the RMG filename gap (GoodName-CRC vs file basename) is a separate documented limitation.
# Usage: pwsh -File docs/save-sync/tools/n64-split-test.ps1 [-SaveId 14] [-TargetExt eep]
param(
    [int]$SaveId = 14,               # 14 = Chameleon Twist 2 (USA) deck .srm; 15 = 007 TWINE
    [string]$TargetExt = "eep",      # any non-"srm" ext triggers the standalone split
    [string]$BinDir = "$PSScriptRoot\..\..\..\bin\Release\net462"
)
$ErrorActionPreference = 'Stop'

$cfg = Get-Content "$env:LOCALAPPDATA\Playnite\ExtensionsData\9700aa21-447d-41b4-a989-acd38f407d9f\config.json" | ConvertFrom-Json
$H = @{ Authorization = "Bearer $($cfg.RomMApiToken)" }
$work = Join-Path $env:TEMP "romm-n64split"
New-Item -ItemType Directory -Force $work | Out-Null

# Pull the canonical .srm bytes from the server.
$srmPath = Join-Path $work "canonical.srm"
Invoke-WebRequest "$($cfg.RomMHost)/api/saves/$SaveId/content" -Headers $H -OutFile $srmPath
$srmBytes = [System.IO.File]::ReadAllBytes($srmPath)
Write-Output "pulled save id=$SaveId : $($srmBytes.Length) bytes (expect 296960 = 0x48800)"

# Load the shipped converter via reflection and run FromCanonical.
$asm = [System.Reflection.Assembly]::LoadFrom((Resolve-Path "$BinDir\RomM.dll"))
$convType  = $asm.GetType("RomM.SaveSync.Converters.N64SaveConverter")
$saveFileT = $asm.GetType("RomM.SaveSync.Converters.SaveFile")
$ctxType   = $asm.GetType("RomM.SaveSync.Converters.ConversionContext")
$flags = [System.Reflection.BindingFlags]"NonPublic,Public,Instance"

$converter = [System.Activator]::CreateInstance($convType, $true)
$canonical = [System.Activator]::CreateInstance($saveFileT, @("Chameleon Twist 2 (USA).srm", $srmBytes))
$ctx = [System.Activator]::CreateInstance($ctxType, $true)
$ctxType.GetProperty("BaseName").SetValue($ctx, "Chameleon Twist 2 (USA)")
$ctxType.GetProperty("CanonicalExtension").SetValue($ctx, "srm")
$ctxType.GetProperty("TargetNativeExtension").SetValue($ctx, $TargetExt)

$fromCanonical = $convType.GetMethod("FromCanonical", $flags)
$outputs = $fromCanonical.Invoke($converter, @($canonical, $ctx))

Write-Output ""
Write-Output "FromCanonical(target='$TargetExt') produced $($outputs.Count) component file(s):"
$nameProp = $saveFileT.GetProperty("Name")
$bytesProp = $saveFileT.GetProperty("Bytes")
foreach ($o in $outputs) {
    $name = $nameProp.GetValue($o)
    $bytes = $bytesProp.GetValue($o)
    $nonZero = 0; foreach ($b in $bytes) { if ($b -ne 0) { $nonZero++ } }
    Write-Output ("  {0,-32} {1,7} bytes  ({2} non-zero)" -f $name, $bytes.Length, $nonZero)
}

# Round-trip: join the components back and confirm we recover the exact canonical bytes.
$toCanonical = $convType.GetMethod("ToCanonical", $flags)
# Rebuild an IList<SaveFile> of the outputs for ToCanonical.
$genericList = [System.Activator]::CreateInstance(([System.Collections.Generic.List`1].MakeGenericType($saveFileT)))
foreach ($o in $outputs) { $genericList.Add($o) }
$ctx2 = [System.Activator]::CreateInstance($ctxType, $true)
$ctxType.GetProperty("BaseName").SetValue($ctx2, "Chameleon Twist 2 (USA)")
$ctxType.GetProperty("CanonicalExtension").SetValue($ctx2, "srm")
$ctxType.GetProperty("TargetNativeExtension").SetValue($ctx2, $TargetExt)
$rejoined = $toCanonical.Invoke($converter, @($genericList, $ctx2))

if ($null -eq $rejoined) {
    Write-Output ""
    Write-Output "ROUND-TRIP: ToCanonical returned null (round-trip self-check tripped) — INVESTIGATE"
} else {
    $rjBytes = $bytesProp.GetValue($rejoined)
    $md5a = [BitConverter]::ToString(([System.Security.Cryptography.MD5]::Create()).ComputeHash($srmBytes)).Replace('-','').ToLowerInvariant()
    $md5b = [BitConverter]::ToString(([System.Security.Cryptography.MD5]::Create()).ComputeHash($rjBytes)).Replace('-','').ToLowerInvariant()
    Write-Output ""
    Write-Output "ROUND-TRIP: split -> join md5 $md5b vs original $md5a  match=$($md5a -eq $md5b)"
}
