<#
.SYNOPSIS
    Baut die App als Single-File-EXE nach publish\Fledgr.exe.

.DESCRIPTION
    - Self-contained, win-x64, unkomprimiert (komprimiert braucht ca. 25 MB mehr RAM im Host).
    - LibVLC wird mit in die EXE gepackt. Das Desktop-Projekt nimmt nur 64-Bit und nur die Audio-Teile mit
      (siehe YTSPlayer.Desktop.csproj). Mit -VlcFull kommt alles mit.
    - Läuft die EXE noch, ist der Ordner gesperrt. Dann zuerst beenden.
#>
param(
    [bool]$Compress = $false,
    [switch]$VlcFull
)
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'publish'
$extra = if ($VlcFull) { '-p:VlcFull=true' } else { '' }
dotnet publish "$PSScriptRoot\src\YTSPlayer.Desktop\YTSPlayer.Desktop.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=$Compress `
    -p:DebugType=none $extra -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fehlgeschlagen' }
dotnet build-server shutdown *> $null
Get-ChildItem $out -File | Where-Object { $_.Extension -in '.pdb', '.xml' } | Remove-Item -Force

$total = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Get-ChildItem $out | Select-Object Name, @{n = 'MB'; e = { if ($_.PSIsContainer) { [math]::Round((Get-ChildItem $_.FullName -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1) } else { [math]::Round($_.Length / 1MB, 1) } } }
"Gesamt: {0:N1} MB" -f $total
