using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// 書き出しの設定。
/// </summary>
/// <remarks>
/// ファイル名のオプション（プレフィックス / サフィックス / 連番）と、同名ファイルが
/// あったときの扱いは今後ここに足す。
/// </remarks>
public sealed partial class ExportSettings : ObservableObject
{
    /// <summary>品質の下限。</summary>
    public const int MinQuality = 0;

    /// <summary>品質の上限。</summary>
    public const int MaxQuality = 100;

    /// <summary>WebP 品質の下限。0 は受け付けない。</summary>
    public const int MinWebPQuality = 1;

    /// <summary>
    /// WebP を可逆にするときに渡す値。
    /// </summary>
    /// <remarks>
    /// OpenCV は品質が 100 を超えると可逆圧縮に切り替える。この 101 という数字は
    /// 知らなければ意味が分からないため、UI ではチェックボックスとして見せ、
    /// この定数への変換はここで閉じる。
    /// </remarks>
    public const int LosslessWebPQuality = 101;

    private int _jpegQuality = 95;
    private int _webPQuality = 95;

    /// <summary>出力先フォルダの絶対パス。未選択なら <see langword="null" />。</summary>
    [ObservableProperty]
    public partial string? Folder { get; set; }

    /// <summary>書き出す形式。既定は元の形式を維持する。</summary>
    [ObservableProperty]
    public partial ExportFormat Format { get; set; }

    /// <summary>JPEG の品質（0〜100）。</summary>
    public int JpegQuality
    {
        get => _jpegQuality;
        set => SetProperty(ref _jpegQuality, Math.Clamp(value, MinQuality, MaxQuality));
    }

    /// <summary>WebP の品質（1〜100）。可逆にする場合は <see cref="WebPLossless" /> を使う。</summary>
    public int WebPQuality
    {
        get => _webPQuality;
        set => SetProperty(ref _webPQuality, Math.Clamp(value, MinWebPQuality, MaxQuality));
    }

    /// <summary>WebP を可逆で書き出すかどうか。</summary>
    [ObservableProperty]
    public partial bool WebPLossless { get; set; }

    /// <summary>
    /// 実際に OpenCV へ渡す WebP の品質。可逆が選ばれていれば
    /// <see cref="LosslessWebPQuality" /> になる。
    /// </summary>
    public int EffectiveWebPQuality => WebPLossless ? LosslessWebPQuality : WebPQuality;
}
