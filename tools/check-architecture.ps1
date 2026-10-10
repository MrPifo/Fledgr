<#
.SYNOPSIS
    Prüft die Architektur-Regeln von Fledgr (siehe CLAUDE.md, Abschnitt „Architektur-Regeln“).

.DESCRIPTION
    1. Projekt-Verweise: nur erlaubte Richtungen.
    2. Keine Windows-/Plattform-APIs in den gemeinsamen Projekten (Android-Fähigkeit).
    3. Keine festen Farben, Schriftgrößen, Abstände oder Radien in Views (nur Tokens aus Ui/Theme).

    Exit-Code = Anzahl der Verstöße.
#>
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$violations = [System.Collections.Generic.List[string]]::new()

# Erlaubte Verweise je Projekt (ohne Präfix YTSPlayer.)
$allowed = @{
    'Contracts'                   = @()
    'Ui'                          = @('Contracts')
    'Shell'                       = @('Contracts', 'Ui')
    'Modules.MusicPlayer'         = @('Contracts', 'Ui')
    'Modules.YtConnect'           = @('Contracts', 'Ui')
    'Modules.RadioPlayer'         = @('Contracts', 'Ui')
    'Modules.YtConnect.Windows'   = @('Modules.YtConnect', 'Platform.Windows')
    'Platform.Windows'            = @('Contracts')
    'Desktop'                     = @('Contracts', 'Ui', 'Shell', 'Modules.MusicPlayer', 'Modules.YtConnect', 'Modules.YtConnect.Windows', 'Modules.RadioPlayer', 'Platform.Windows')
}
# Gemeinsame Projekte: dürfen keinen Windows-Code enthalten.
$shared = @('Contracts', 'Ui', 'Shell', 'Modules.MusicPlayer', 'Modules.YtConnect', 'Modules.RadioPlayer')

foreach ($name in $allowed.Keys) {
    $dir = Join-Path $src "YTSPlayer.$name"
    $csproj = Join-Path $dir "YTSPlayer.$name.csproj"
    if (-not (Test-Path $csproj)) { $violations.Add("Projekt fehlt: YTSPlayer.$name"); continue }

    # 1. Projekt-Verweise
    $refs = [regex]::Matches((Get-Content $csproj -Raw), 'ProjectReference Include="[^"]*YTSPlayer\.([^\\"]+)\.csproj"') | ForEach-Object { $_.Groups[1].Value }
    foreach ($r in $refs) {
        if ($allowed[$name] -notcontains $r) { $violations.Add("Verweis nicht erlaubt: $name -> $r") }
    }

    if ($shared -contains $name) {
        $files = Get-ChildItem $dir -Recurse -File -Include *.cs, *.axaml | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }

        # 2. Plattform-Code
        $forbidden = 'DllImport', 'LibraryImport', 'Microsoft\.Web\.WebView2', 'System\.Windows\.Forms', 'System\.Windows\.Controls', 'Environment\.GetFolderPath', 'Environment\.SpecialFolder', '[A-Z]:\\\\', '%AppData%', '%LocalAppData%'
        foreach ($f in $files | Where-Object Extension -eq '.cs') {
            $text = Get-Content $f.FullName -Raw
            foreach ($p in $forbidden) {
                if ($text -match $p) { $violations.Add("Plattform-Code in gemeinsamem Projekt: $($f.FullName.Substring($root.Length + 1)) enthält '$p'") }
            }
        }

        # 3. Feste Gestaltung in Views (Palette selbst ausgenommen)
        foreach ($f in $files | Where-Object { $_.Extension -eq '.axaml' -and $_.Name -ne 'Palette.axaml' -and $_.Name -ne 'Metrics.axaml' }) {
            $lines = Get-Content $f.FullName
            for ($i = 0; $i -lt $lines.Count; $i++) {
                $l = $lines[$i]
                if ($f.Directory.Name -eq 'Ui' -or $f.FullName -match '\\YTSPlayer\.Ui\\') { $isUi = $true } else { $isUi = $false }
                $rel = $f.FullName.Substring($root.Length + 1)
                if ($l -match '#[0-9A-Fa-f]{6,8}\b') { $violations.Add("Feste Farbe: ${rel}:$($i + 1)") }
                if ($l -match '\bFontSize="\d') { $violations.Add("Feste Schriftgröße: ${rel}:$($i + 1)") }
                if ($l -match '\b(Margin|Padding|Spacing|RowSpacing|ColumnSpacing|CornerRadius)="\d' -and -not $isUi) { $violations.Add("Fester Abstand/Radius: ${rel}:$($i + 1)") }
            }
        }
    }
}

if ($violations.Count -eq 0) {
    Write-Host 'Architektur-Check: OK' -ForegroundColor Green
} else {
    Write-Host "Architektur-Check: $($violations.Count) Verstoß/Verstöße" -ForegroundColor Red
    $violations | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
}
exit $violations.Count
