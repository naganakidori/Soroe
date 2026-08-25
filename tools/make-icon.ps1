# Assets の SVG から Soroe.ico を作る。
#
# 使い方（AGENTS.md「構成とビルド」も参照）
#   powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
#
# アイコンは「サイズごとに別の絵」である。256 の絵を縮小すると、小さいサイズでは
# 境界が半端な位置に落ちて滲む。そのため src/Soroe/Assets/icon-<辺>.svg を
# サイズごとに手で書き、それぞれを実寸で描いて 1 つの .ico にまとめる。
#
# SVG は <rect> しか解釈しない。角丸矩形しか使わないと決めているので、完全な
# SVG 実装は要らず、追加の依存も増やさずに済む。ただし将来 <path> や <circle>
# を足したとき、黙って無視されると「SVG は直したのに .ico が変わらない」という
# 気づけない壊れ方をする。そこで知らない要素・fill の無い矩形が来たら必ず
# 例外で止める。
#
# 変換は WPF（アプリ本体と同じ描画エンジン）と PowerShell だけで行う。
# ImageMagick も Inkscape も要らない。
#
# 検査はできあがった .ico を読み直して行う。作るときに使った一覧ではなく、
# 下の $RequiredSizes と突き合わせる。同じ一覧で作って同じ一覧で検査すると、
# 一覧から項目を落としたときに処理と検査が同時に消えて素通りする
# （tools/pack.ps1 で実際にやった）。

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$ErrorActionPreference = 'Stop'

# .ico に必ず入っていなければならない辺の長さ。これは仕様であって、入力
# ファイルから導いた値ではない。増やすときは SVG も足すこと。
$RequiredSizes = @(16, 20, 24, 32, 48, 64, 256)

$repo    = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assets  = Join-Path $repo 'src\Soroe\Assets'
$outPath = Join-Path $assets 'Soroe.ico'

function New-FrozenBrush([string]$hex) {
    $color = [System.Windows.Media.ColorConverter]::ConvertFromString($hex)
    $brush = New-Object System.Windows.Media.SolidColorBrush($color)
    $brush.Freeze()
    return $brush
}

# SVG を実寸で描く。矩形以外が来たら止める。
function Convert-SvgToBitmap([string]$path) {
    [xml]$doc = Get-Content -Raw -Encoding UTF8 $path
    $svg = $doc.DocumentElement
    if ($svg.LocalName -ne 'svg') { throw "根が <svg> ではない: $path" }

    $width  = [int]$svg.GetAttribute('width')
    $height = [int]$svg.GetAttribute('height')
    if ($width -ne $height) { throw "正方形ではない ${width}x${height}: $path" }

    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $drawn = 0
    $lefts = @()
    $bottoms = @()
    foreach ($node in $svg.ChildNodes) {
        if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        if ($node.LocalName -ne 'rect') { throw "解釈できない要素 <$($node.LocalName)>: $path" }
        $fill = $node.GetAttribute('fill')
        if ([string]::IsNullOrEmpty($fill)) { throw "fill の無い rect: $path" }
        $x = [double]$node.GetAttribute('x')
        $y = [double]$node.GetAttribute('y')
        $w = [double]$node.GetAttribute('width')
        $h = [double]$node.GetAttribute('height')
        $rect = New-Object System.Windows.Rect($x, $y, $w, $h)
        $radius = [double]$node.GetAttribute('rx')
        $dc.DrawRoundedRectangle((New-FrozenBrush $fill), $null, $rect, $radius, $radius)
        $lefts += $x
        $bottoms += ($y + $h)
        $drawn++
    }
    $dc.Close()
    if ($drawn -eq 0) { throw "rect が 1 つも無い: $path" }

    # 揃いは座標で見る。描いた画素から測ると角丸の中間色で 1px ぶれて、
    # 揃っていても落ちたり、理由を取り違えたりする（実際にそうなった）。
    # 座標なら厳密で、しかも 16px だけでなく全サイズを見られる。
    if (@($lefts | Select-Object -Unique).Count -ne 1) {
        throw "左端が揃っていない（$($lefts -join ' / ')）: $path"
    }
    if (@($bottoms | Select-Object -Unique).Count -ne 1) {
        throw "下端が揃っていない（$($bottoms -join ' / ')）: $path"
    }

    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $width, $height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $bmp.Freeze()
    return $bmp
}

# 乗算済みアルファ（Pbgra32）のままでは DIB に書けないので、素の BGRA に直す。
function Get-BgraPixels($bmp) {
    $conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap
    $conv.BeginInit()
    $conv.Source = $bmp
    $conv.DestinationFormat = [System.Windows.Media.PixelFormats]::Bgra32
    $conv.EndInit()
    $stride = $bmp.PixelWidth * 4
    $buf = New-Object byte[] ($stride * $bmp.PixelHeight)
    $conv.CopyPixels($buf, $stride, 0)
    return $buf
}

# 64 以下は DIB で入れる。Windows が最も素直に扱う形。
function ConvertTo-IcoDib($bmp) {
    $w = $bmp.PixelWidth
    $h = $bmp.PixelHeight
    $pixels = Get-BgraPixels $bmp
    $stride = $w * 4
    $maskStride = [int][math]::Floor(($w + 31) / 32) * 4

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER。高さは XOR とマスクを合わせた 2 倍を書く決まり。
    $bw.Write([uint32]40)
    $bw.Write([int32]$w)
    $bw.Write([int32]($h * 2))
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]0)
    $bw.Write([uint32]($stride * $h))
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    $bw.Write([uint32]0)
    $bw.Write([uint32]0)
    # XOR 部。DIB は下から上へ並べる。
    for ($y = $h - 1; $y -ge 0; $y--) {
        $bw.Write($pixels, ($y * $stride), $stride)
    }
    # AND マスク。32bpp では実際にはアルファが使われるが、古い経路のために
    # 完全に透明な画素だけ 1 にしておく。
    for ($y = $h - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $w; $x++) {
            if ($pixels[$y * $stride + $x * 4 + 3] -eq 0) {
                $byteIndex = [int][math]::Floor($x / 8)
                $row[$byteIndex] = [byte]($row[$byteIndex] -bor (128 -shr ($x % 8)))
            }
        }
        $bw.Write($row, 0, $maskStride)
    }
    $bw.Flush()
    return $ms.ToArray()
}

# 256 は PNG で入れる。DIB のままだと 1 枚で 256KB を超える。
function ConvertTo-IcoPng($bmp) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp)) | Out-Null
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return $ms.ToArray()
}

# ---- 作る ----
$svgs = Get-ChildItem (Join-Path $assets 'icon-*.svg') | Sort-Object { [int]($_.BaseName -replace '\D', '') }
if ($svgs.Count -eq 0) { throw "SVG が 1 つも無い: $assets" }

$entries = @()
foreach ($svg in $svgs) {
    $bmp = Convert-SvgToBitmap $svg.FullName
    $size = $bmp.PixelWidth
    if ($size -ge 256) {
        $payload = ConvertTo-IcoPng $bmp
        $kind = 'PNG'
    } else {
        $payload = ConvertTo-IcoDib $bmp
        $kind = 'DIB'
    }
    $entries += [PSCustomObject]@{ Size = $size; Payload = $payload }
    Write-Output ("  {0,3}px  {1,8:N0} バイト  {2}" -f $size, $payload.Length, $kind)
}

$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)              # 予約
$bw.Write([uint16]1)              # 種別 1 = アイコン
$bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    # 256 は 1 バイトに収まらないので 0 と書く決まり。
    $dim = 0
    if ($e.Size -lt 256) { $dim = $e.Size }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Payload.Length)
    $bw.Write([uint32]$offset)
    $offset += $e.Payload.Length
}
foreach ($e in $entries) { $bw.Write($e.Payload, 0, $e.Payload.Length) }
$bw.Flush()
$fs.Close()

# ---- 検査（できあがった .ico を読み直す）----
$bytes = [System.IO.File]::ReadAllBytes($outPath)
$count = [BitConverter]::ToUInt16($bytes, 4)
$found = @()
for ($i = 0; $i -lt $count; $i++) {
    $dim = $bytes[6 + 16 * $i]
    if ($dim -eq 0) { $dim = 256 }
    $found += [int]$dim
}
$missing = @($RequiredSizes | Where-Object { $found -notcontains $_ })
$extra   = @($found | Where-Object { $RequiredSizes -notcontains $_ })
if ($missing.Count -gt 0) { Remove-Item $outPath -Force; throw "必要なサイズが入っていない: $($missing -join ', ')" }
if ($extra.Count -gt 0)   { Remove-Item $outPath -Force; throw "仕様に無いサイズが入っている: $($extra -join ', ')" }

# 16px で「大きさの違う 3 枚が見分けられる」ことがこの意匠の要。上の層が
# 下の層を覆い隠して 2 枚に見える失敗を、実際の画素から捕まえる。
# 揃っているかどうかは Convert-SvgToBitmap が座標で見ている。
$decoder = New-Object System.Windows.Media.Imaging.IconBitmapDecoder(
    (New-Object Uri($outPath)),
    [System.Windows.Media.Imaging.BitmapCreateOptions]::None,
    [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$frame = $decoder.Frames | Where-Object { $_.PixelWidth -eq 16 } | Select-Object -First 1
if ($null -eq $frame) { Remove-Item $outPath -Force; throw '16px の枠が読み出せない' }

$conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap
$conv.BeginInit()
$conv.Source = $frame
$conv.DestinationFormat = [System.Windows.Media.PixelFormats]::Bgra32
$conv.EndInit()
$stride = 16 * 4
$buf = New-Object byte[] ($stride * 16)
$conv.CopyPixels($buf, $stride, 0)

# 色ごとに、見えている画素の数を数える。角の中間色を層と数えないよう、
# 完全に不透明な画素だけを見る。塗りの色そのものは書かない。書くと
# 「意匠と検査が同じ表を見る」ことになり、色を変えたときに両方が同時に
# 変わって素通りする。
$counts = @{}
for ($y = 0; $y -lt 16; $y++) {
    for ($x = 0; $x -lt 16; $x++) {
        $o = $y * $stride + $x * 4
        if ($buf[$o + 3] -ne 255) { continue }
        $key = '{0:X2}{1:X2}{2:X2}' -f $buf[$o + 2], $buf[$o + 1], $buf[$o]
        if ($counts.ContainsKey($key)) { $counts[$key]++ } else { $counts[$key] = 1 }
    }
}
# 12 画素に満たないものは、角丸の重なりでできた混色とみなして層に数えない。
$layers = @($counts.GetEnumerator() | Where-Object { $_.Value -ge 12 })

if ($layers.Count -ne 3) {
    $seen = ($counts.GetEnumerator() | Sort-Object { -$_.Value } |
        Select-Object -First 5 | ForEach-Object { "$($_.Key):$($_.Value)" }) -join ' '
    Remove-Item $outPath -Force
    throw "16px で 3 枚が見分けられない（12 画素以上の色が $($layers.Count) 色。上位: $seen）"
}

$areas = ($layers | Sort-Object { -$_.Value } | ForEach-Object { $_.Value }) -join ' / '
$len = (Get-Item $outPath).Length
Write-Output ""
Write-Output ("完成: {0}" -f $outPath)
Write-Output ("  {0:N0} バイト / {1} 枚" -f $len, $count)
Write-Output ("  16px で 3 枚が見分けられる（面積 {0} 画素）。揃いは SVG の座標で確認済み" -f $areas)
