# 配布用の zip を作る。発行 → 不要ファイルの削除 → 検査 → 圧縮 まで通す。
#
# 使い方（AGENTS.md「構成とビルド」も参照）
#   powershell -ExecutionPolicy Bypass -File tools/pack.ps1 -Out <出力先フォルダの絶対パス>
#
# 出力先は絶対パスで渡すこと。相対パスは実行時の作業フォルダ（多くはリポジトリ
# 直下）に解決され、100MB 近い成果物がリポジトリに紛れ込む。リポジトリ配下も拒否する。
#
# 自己完結（self-contained）+ 単一ファイルで発行する。
#   - 自己完結: 「zip 解凍で動く」という約束を、.NET ランタイムの有無に依存させないため
#   - 単一ファイル: 発行物が 407 ファイルから 10 ファイルになる。ネイティブ DLL は
#     exe の隣に残るので「1 ファイル」にはならないが、解凍後の中身が把握できる形になる
#   - ReadyToRun は使わない。起動が 570ms から 467ms になる代わりに zip が 6.1MB 増え、
#     一括処理のソフトで 0.1 秒の短縮に見合わない（実測 2026-08-19）
#   - PublishTrimmed は WPF では使えない（NETSDK1168 でビルドが止まる）
param(
  [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not [System.IO.Path]::IsPathRooted($Out)) {
  throw "出力先は絶対パスで指定すること: '$Out'"
}

# リポジトリ配下への出力を拒む。成果物を誤ってコミットするのを防ぐ
$outFull = [System.IO.Path]::GetFullPath($Out)
if ($outFull.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) {
  throw "出力先をリポジトリ配下にしないこと: '$outFull'"
}

# 配布物に入れてはいけないもの。
#   opencv_videoio_ffmpeg4130_64.dll … FFmpeg（LGPL v2.1）。このアプリは動画を扱わない。
#                                      同梱すると使わない機能のために LGPL の義務を負う
#   Soroe.xml                        … XML ドキュメント。コメント漏れ検知のための生成物
#   Soroe.pdb                        … デバッグ情報
$excluded = @(
  'opencv_videoio_ffmpeg4130_64.dll',
  'Soroe.xml',
  'Soroe.pdb'
)

# 配布物に必ず入れるもの。ライセンス表記は義務なので、欠けたら止める
$required = @(
  'LICENSE',
  'THIRD-PARTY-NOTICES.txt'
)

# 版はここに書かない。csproj を唯一の出所にする
$csproj = Join-Path $repo 'src/Soroe/Soroe.csproj'
[xml]$project = Get-Content $csproj -Encoding UTF8
$version = ($project.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) {
  throw "csproj から Version を読めなかった: $csproj"
}

$name = "Soroe-$version-win-x64"
$stage = Join-Path $outFull $name
$zip = Join-Path $outFull "$name.zip"

Write-Host "版 $version / 出力先 $outFull"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory $stage -Force | Out-Null

Write-Host '発行中...'
& dotnet publish (Join-Path $repo 'src/Soroe') `
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
  -o $stage --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
  throw "発行に失敗した（終了コード $LASTEXITCODE）"
}

foreach ($item in $excluded) {
  $path = Join-Path $stage $item
  if (Test-Path $path) {
    Remove-Item $path -Force
    Write-Host "  除外 $item"
  }
}

foreach ($item in $required) {
  Copy-Item (Join-Path $repo $item) $stage -Force
  Write-Host "  同梱 $item"
}

Write-Host '圧縮中...'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

# --- 検査。ここを通らないものは配らない ---
#
# 検査は「できあがった zip の中身」に対して行う。$excluded / $required の配列を
# 見て確かめると、配列から項目を落としたときに処理と検査が同時に消えてしまい、
# 検査が素通りする。実際にそう書いていて、LICENSE を同梱し忘れる壊し方を
# 1 件も捕まえられなかった。検査は動作から独立していなければ意味がない。
#
# 検査に落ちたら zip を消す。壊れた成果物を置いたままにすると、
# 失敗に気づかず配ってしまう
try {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
  try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })

    # 1. ライセンス表記が入っていること。BSD 系は「バイナリ配布物に条項を再現すること」を
    #    求めており、Apache-2.0 も写しの添付を求める。入れ忘れは義務違反になる
    foreach ($item in @('LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
      $entry = $archive.Entries | Where-Object { $_.FullName -eq $item } | Select-Object -First 1
      if (-not $entry) {
        throw "zip に $item が入っていない。ライセンス表記は同梱が義務"
      }

      if ($entry.Length -eq 0) {
        throw "zip の $item が空になっている"
      }
    }

    # 2. FFmpeg の DLL が残っていないこと。名前は版で変わるので、綴りではなく
    #    「ffmpeg を含むファイル名」で探す
    $ffmpeg = @($entries | Where-Object { $_ -like '*ffmpeg*' })
    if ($ffmpeg.Count -gt 0) {
      throw "zip に FFmpeg の DLL が残っている: $($ffmpeg -join ', ')"
    }

    # 3. 本体があること
    if (-not ($entries -contains 'Soroe.exe')) {
      throw 'zip に Soroe.exe が入っていない'
    }
  }
  finally {
    $archive.Dispose()
  }
}
catch {
  if (Test-Path $zip) { Remove-Item $zip -Force }
  throw
}

$files = @(Get-ChildItem $stage -Recurse -File)
Write-Host ''
Write-Host "できあがり: $zip"
Write-Host ("  {0} ファイル / 展開 {1:N1} MB / zip {2:N1} MB" -f `
  $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB), ((Get-Item $zip).Length / 1MB))
Write-Host ''
$files | Sort-Object Length -Descending | ForEach-Object {
  Write-Host ("  {0,10:N0} KB  {1}" -f ($_.Length / 1KB), $_.Name)
}
