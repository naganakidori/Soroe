# アプリを起動して、指定した大きさのウィンドウを 1 枚撮る。
#
# 使い方（AGENTS.md「構成とビルド」も参照）
#   pwsh -File tools/screenshot.ps1 -Exe <exe> -Out <出力先.png> [-Width 1000] [-Height 720]
#                                   [-ExeArgs a,b] [-ScrollEnd]
#
#   -ScrollEnd を付けると、縦にスクロールできる領域（調整パネル）を末尾まで
#   送ってから撮る。項目が増えて画面に収まらなくなったときの確認に使う。
#
# 撮影は PrintWindow に PW_RENDERFULLCONTENT を渡して行う。
# Graphics.CopyFromScreen は「いま画面に映っているもの」を取るため、画面が
# 描画されていない状態（ロック中・リモート切断・省電力での消灯・他ウィンドウでの
# 遮蔽）では白紙になる。実際にそれが起き、変更による回帰かどうかの切り分けに
# 時間を使った。PrintWindow は DWM にウィンドウの中身を描き直させるので、
# 画面の状態に依存しない。
#
# 出力先は絶対パスで渡すこと。相対パスは実行時の作業フォルダ（多くはリポジトリ
# 直下）に解決され、成果物がリポジトリに紛れ込む。
#
#   -Width / -Height を省略するとアプリの既定サイズのまま撮る。既定サイズは
#   作業領域に合わせて実行時に縮まることがあるので、数値で指定すると
#   「既定サイズの確認」にならない。
param(
  [Parameter(Mandatory = $true)][string]$Exe,
  [Parameter(Mandatory = $true)][string]$Out,
  [int]$Width = 0,
  [int]$Height = 0,
  [string[]]$ExeArgs = @(),
  [switch]$ScrollEnd,
  [switch]$Maximize
)

$ErrorActionPreference = 'Stop'

if (-not [System.IO.Path]::IsPathRooted($Out)) {
  throw "出力先は絶対パスで指定すること: '$Out'"
}

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;

public static class WindowShot
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);

    public const int SW_MAXIMIZE = 3;
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(
        IntPtr hwnd, int attr, out RECT value, int size);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public static Bitmap Capture(IntPtr hwnd)
    {
        RECT r;

        // 影を含まない実際の枠を取る。GetWindowRect は影のぶん大きい
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0)
        {
            GetWindowRect(hwnd, out r);
        }

        var bitmap = new Bitmap(r.R - r.L, r.B - r.T);
        using (var g = Graphics.FromImage(bitmap))
        {
            var hdc = g.GetHdc();
            try
            {
                PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        return bitmap;
    }
}
"@ -ReferencedAssemblies System.Drawing

[void][WindowShot]::SetProcessDPIAware()
$A = [System.Windows.Automation.AutomationElement]
$Desc = [System.Windows.Automation.TreeScope]::Descendants

$proc = if ($ExeArgs.Count -gt 0) {
  Start-Process $Exe -ArgumentList $ExeArgs -PassThru
} else {
  Start-Process $Exe -PassThru
}

try {
  $win = $null
  foreach ($i in 1..40) {
    Start-Sleep -Milliseconds 400
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
    $win = $A::RootElement.FindFirst($Desc, $cond)
    if ($win -and $win.Current.NativeWindowHandle -ne 0) { break }
  }

  if (-not $win) { throw "ウィンドウが見つからない" }

  $h = [IntPtr]$win.Current.NativeWindowHandle

  # -Width / -Height は XAML と同じ論理ピクセルで受け取る。MoveWindow は物理ピクセル
  # なので DPI 倍率を掛ける。掛けないと高 DPI の環境で要求より小さい窓になり、
  # MinWidth / MinHeight に丸められて「既定サイズの確認」にならない
  $dpi = [WindowShot]::GetDpiForWindow($h)
  if ($dpi -eq 0) { $dpi = 96 }
  $scale = $dpi / 96.0

  if ($Maximize) {
    [void][WindowShot]::ShowWindow($h, [WindowShot]::SW_MAXIMIZE)
  } elseif ($Width -gt 0 -and $Height -gt 0) {
    [void][WindowShot]::MoveWindow($h, 80, 80, [int]($Width * $scale), [int]($Height * $scale), $true)
  }

  Start-Sleep -Milliseconds 1200

  if ($ScrollEnd) {
    # ホイールではなく ScrollPattern で送る。ホイールは要素の下にカーソルを
    # 置く必要があり、フォーカスの有無で挙動が変わる
    $panes = @($win.FindAll($Desc, (New-Object System.Windows.Automation.PropertyCondition(
      $A::IsScrollPatternAvailableProperty, $true))))
    $scroller = $panes | Where-Object {
      $_.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current.VerticallyScrollable
    } | Select-Object -First 1

    if ($scroller) {
      $scroller.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100)
      Start-Sleep -Milliseconds 800
    }
  }

  $bmp = [WindowShot]::Capture($h)
  $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
  "$([System.IO.Path]::GetFileName($Out))  $($bmp.Width)x$($bmp.Height)"
  $bmp.Dispose()
}
finally {
  Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}
