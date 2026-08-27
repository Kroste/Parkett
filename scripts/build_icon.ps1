# Parkett App-Icon-Generator (PowerShell-Port von build_icon.py).
#
# Warum zwei Fassungen: der Arbeitslaptop hat nur den WindowsApps-Store-Stub
# statt eines echten Python, dort laeuft das .py-Skript nicht. Beide Fassungen
# muessen dasselbe Icon erzeugen - Aenderungen am Design also IMMER in beiden
# nachziehen, sonst driften PNG und ICO je nach Rechner auseinander.
#
# Design: drei Kerzen eines Candlestick-Charts in Kroste-Gold auf abgerundetem
# dunklem Grund. Ohne Text, damit es auch als 16x16-Favicon lesbar bleibt -
# drei senkrechte Balken unterschiedlicher Hoehe sind die kleinste Form, die
# noch eindeutig nach Boerse aussieht.
#
# Erzeugt:
#   Parkett/Assets/parkett.png  (256x256, master)
#   Parkett/Assets/parkett.ico  (Windows-Multi-Res)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

# Kroste-Palette (aus App.axaml)
$gold    = [System.Drawing.Color]::FromArgb(255, 224, 177, 76)   # #E0B14C
$goldD   = [System.Drawing.Color]::FromArgb(255, 168, 130, 48)   # abgedunkelt, fallende Kerze
$surface = [System.Drawing.Color]::FromArgb(255, 26, 29, 33)     # #1A1D21
$border  = [System.Drawing.Color]::FromArgb(255, 46, 52, 60)     # #2E343C

$corner = 48
$appName = 'parkett'
$outDir = Join-Path $PSScriptRoot '..' | Join-Path -ChildPath 'Parkett' |
    Join-Path -ChildPath 'Assets'

# x-Mitte, Docht oben, Koerper oben, Koerper unten, Docht unten, steigend
# Werte in 256er-Koordinaten, von links nach rechts gelesen wie ein Chart.
$kerzen = @(
    [pscustomobject]@{ X = 74;  WickTop = 96; BodyTop = 120; BodyBottom = 186; WickBottom = 206; Rising = $true }
    [pscustomobject]@{ X = 128; WickTop = 62; BodyTop = 84;  BodyBottom = 150; WickBottom = 172; Rising = $false }
    [pscustomobject]@{ X = 182; WickTop = 44; BodyTop = 66;  BodyBottom = 128; WickBottom = 152; Rising = $true }
)

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)

    $pfad = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2

    if ($d -le 0) {
        $pfad.AddRectangle((New-Object System.Drawing.RectangleF($X, $Y, $W, $H)))
        return $pfad
    }

    $pfad.AddArc($X, $Y, $d, $d, 180, 90)
    $pfad.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $pfad.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $pfad.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $pfad.CloseFigure()

    return $pfad
}

function New-Icon {
    param([int]$Size)

    $skala = $Size / 256.0
    $bild = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bild)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $randbreite = [Math]::Max(1, [int](2 * $skala))
    $grundPfad = New-RoundedPath 0 0 ($Size - 1) ($Size - 1) ($corner * $skala)

    $pinsel = New-Object System.Drawing.SolidBrush($surface)
    $g.FillPath($pinsel, $grundPfad)
    $pinsel.Dispose()

    $stift = New-Object System.Drawing.Pen($border, $randbreite)
    $g.DrawPath($stift, $grundPfad)
    $stift.Dispose()
    $grundPfad.Dispose()

    $koerperHalb = [Math]::Max(2, [int](17 * $skala))
    $dochtHalb = [Math]::Max(1, [int](3 * $skala))
    $radius = [Math]::Max(1, [int](4 * $skala))

    foreach ($k in $kerzen) {
        $farbe = if ($k.Rising) { $gold } else { $goldD }
        $x = $k.X * $skala
        $pinsel = New-Object System.Drawing.SolidBrush($farbe)

        # Docht: duenner Balken ueber die volle Hoehe
        $g.FillRectangle($pinsel,
            [float]($x - $dochtHalb), [float]($k.WickTop * $skala),
            [float]($dochtHalb * 2), [float](($k.WickBottom - $k.WickTop) * $skala))

        # Koerper: breiter Balken, abgerundet damit es bei 256px nicht hart wirkt
        $koerper = New-RoundedPath ([float]($x - $koerperHalb)) ([float]($k.BodyTop * $skala)) `
            ([float]($koerperHalb * 2)) ([float](($k.BodyBottom - $k.BodyTop) * $skala)) ([float]$radius)
        $g.FillPath($pinsel, $koerper)
        $koerper.Dispose()

        $pinsel.Dispose()
    }

    $g.Dispose()
    return $bild
}

# ICO von Hand schreiben: System.Drawing kann kein Multi-Res-ICO speichern.
function Save-Ico {
    param([System.Drawing.Bitmap[]]$Frames, [string]$Path)

    $pngs = foreach ($frame in $Frames) {
        $puffer = New-Object System.IO.MemoryStream
        $frame.Save($puffer, [System.Drawing.Imaging.ImageFormat]::Png)
        , $puffer.ToArray()
    }

    $datei = [System.IO.File]::Create($Path)
    $schreiber = New-Object System.IO.BinaryWriter($datei)

    $schreiber.Write([uint16]0)                  # reserviert
    $schreiber.Write([uint16]1)                  # Typ 1 = Icon
    $schreiber.Write([uint16]$Frames.Count)

    # Verzeichnis: 6 Byte Kopf + 16 Byte je Eintrag, danach die PNG-Daten.
    $offset = 6 + (16 * $Frames.Count)

    for ($i = 0; $i -lt $Frames.Count; $i++) {
        $kante = $Frames[$i].Width
        # 256 wird im ICO-Format als 0 kodiert.
        $schreiber.Write([byte]$(if ($kante -ge 256) { 0 } else { $kante }))
        $schreiber.Write([byte]$(if ($kante -ge 256) { 0 } else { $kante }))
        $schreiber.Write([byte]0)                # Farbpalette
        $schreiber.Write([byte]0)                # reserviert
        $schreiber.Write([uint16]1)              # Farbebenen
        $schreiber.Write([uint16]32)             # Bit pro Pixel
        $schreiber.Write([uint32]$pngs[$i].Length)
        $schreiber.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }

    foreach ($png in $pngs) { $schreiber.Write($png) }

    $schreiber.Dispose()
    $datei.Dispose()
}

$zielOrdner = [System.IO.Path]::GetFullPath($outDir)
$null = New-Item -ItemType Directory -Path $zielOrdner -Force

$master = New-Icon -Size 256
$pngPfad = Join-Path $zielOrdner "$appName.png"
$master.Save($pngPfad, [System.Drawing.Imaging.ImageFormat]::Png)

# Windows-Multi-Res: jede Groesse einzeln rendern statt herunterzuskalieren,
# sonst verschwimmen die Dochte bei 16x16.
$groessen = @(16, 24, 32, 48, 64, 128, 256)
$frames = foreach ($g in $groessen) { New-Icon -Size $g }

$icoPfad = Join-Path $zielOrdner "$appName.ico"
Save-Ico -Frames $frames -Path $icoPfad

foreach ($frame in $frames) { $frame.Dispose() }
$master.Dispose()

"geschrieben: $pngPfad"
"geschrieben: $icoPfad"
