# Assets の SVG から Soroe.ico を作る。
#
# 使い方（AGENTS.md「構成とビルド」も参照）
#   powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
#
# アイコンは「サイズごとに別の絵」である。256 の絵を縮小すると、16 では帯と
# 間隔が半端な位置に落ちて滲む。そのため src/Soroe/Assets/icon-<辺>.svg を
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
    foreach ($node in $svg.ChildNodes) {
        if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        if ($node.LocalName -ne 'rect') { throw "解釈できない要素 <$($node.LocalName)>: $path" }
        $fill = $node.GetAttribute('fill')
        if ([string]::IsNullOrEmpty($fill)) { throw "fill の無い rect: $path" }
        $rect = New-Object System.Windows.Rect(
            [double]$node.GetAttribute('x'), [double]$node.GetAttribute('y'),
            [double]$node.GetAttribute('width'), [double]$node.GetAttribute('height'))
        $radius = [double]$node.GetAttribute('rx')
        $dc.DrawRoundedRectangle((New-FrozenBrush $fill), $null, $rect, $radius, $radius)
        $drawn++
    }
    $dc.Close()
    if ($drawn -eq 0) { throw "rect が 1 つも無い: $path" }

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

# 16px は「3 枚が離れて見える」ことがこの意匠の要。間隔が角丸に食われて
# 1 枚の塊になる失敗を、実際の画素から数えて捕まえる。
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

# 列ごとにインクの有無を取り、連なりに畳む。
$ink = New-Object bool[] 16
for ($x = 0; $x -lt 16; $x++) {
    for ($y = 0; $y -lt 16; $y++) {
        if ($buf[$y * $stride + $x * 4 + 3] -gt 0) { $ink[$x] = $true; break }
    }
}
$runs = @()
$runStart = 0
for ($x = 1; $x -le 16; $x++) {
    if ($x -eq 16 -or $ink[$x] -ne $ink[$runStart]) {
        $runs += [PSCustomObject]@{ Ink = $ink[$runStart]; Len = ($x - $runStart) }
        $runStart = $x
    }
}
$bars = @($runs | Where-Object { $_.Ink } | ForEach-Object { $_.Len })
# 両端の余白は「間隔」ではないので、帯にはさまれた分だけを見る。
$first = 0
while ($first -lt $runs.Count -and -not $runs[$first].Ink) { $first++ }
$last = $runs.Count - 1
while ($last -ge 0 -and -not $runs[$last].Ink) { $last-- }
$inner = @()
for ($i = $first; $i -le $last; $i++) { if (-not $runs[$i].Ink) { $inner += $runs[$i].Len } }

$why = $null
if ($bars.Count -ne 3) {
    $why = "帯が 3 本に分かれていない（$($bars.Count) 本に見えている）"
} elseif (($bars | Select-Object -Unique).Count -ne 1) {
    $why = "帯の幅が揃っていない（$($bars -join ' / ') px）"
} elseif ($bars[0] -lt 3) {
    $why = "帯が細すぎる（$($bars[0]) px。3px 以上必要）"
} elseif (($inner | Select-Object -Unique).Count -ne 1) {
    $why = "間隔が揃っていない（$($inner -join ' / ') px）"
} elseif ($inner[0] -lt 2) {
    # 間隔 1px は角丸に食われて 1 枚の塊に見える。案 A で実際にそうなった。
    $why = "間隔が狭すぎる（$($inner[0]) px。2px 以上必要）"
}
if ($null -ne $why) { Remove-Item $outPath -Force; throw "16px の意匠が崩れている: $why" }

$len = (Get-Item $outPath).Length
Write-Output ""
Write-Output ("完成: {0}" -f $outPath)
Write-Output ("  {0:N0} バイト / {1} 枚 / 16px は帯 3 本" -f $len, $count)
