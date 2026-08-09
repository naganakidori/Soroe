using System.Text.Json.Serialization;
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
    private string _prefix = string.Empty;
    private string _suffix = string.Empty;
    private string _sequenceBaseName = "photo";

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

    /// <summary>出力ファイル名の決め方。</summary>
    [ObservableProperty]
    public partial FileNaming Naming { get; set; }

    /// <summary>元のファイル名の前に足す文字。</summary>
    public string Prefix
    {
        get => _prefix;
        set => SetProperty(ref _prefix, FileNamePart.Sanitize(value));
    }

    /// <summary>元のファイル名の後ろに足す文字。</summary>
    public string Suffix
    {
        get => _suffix;
        set => SetProperty(ref _suffix, FileNamePart.Sanitize(value));
    }

    /// <summary>
    /// 連番リネームのベース名。
    /// </summary>
    /// <remarks>
    /// 空でもよい。その場合は区切りも付かず <c>001.jpg</c> のようになる。
    /// 番号だけの連番にしたいという要求は普通にあるため、禁止しない。
    /// </remarks>
    public string SequenceBaseName
    {
        get => _sequenceBaseName;
        set => SetProperty(ref _sequenceBaseName, FileNamePart.Sanitize(value));
    }

    /// <summary>
    /// 同名のファイルがあるときに上書きするかどうか。
    /// </summary>
    /// <remarks>
    /// オフのときは別名（<c>名前 (2).jpg</c>）で保存する。エクスプローラーのコピーと同じ挙動。
    /// オンでも<b>同じ実行の中で書いたファイルは決して上書きしない</b>。
    /// 上書きが意図するのは前回の実行結果の置き換えであって、今回の成果物ではないため。
    /// </remarks>
    [ObservableProperty]
    public partial bool Overwrite { get; set; }

    /// <summary>
    /// 実際に OpenCV へ渡す WebP の品質。可逆が選ばれていれば
    /// <see cref="LosslessWebPQuality" /> になる。
    /// </summary>
    /// <remarks>
    /// 他の 2 つから導出される値なので保存しない。書き出すと、設定ファイルを直接
    /// 編集した人が「ここを変えれば効く」と誤解する。
    /// </remarks>
    [JsonIgnore]
    public int EffectiveWebPQuality => WebPLossless ? LosslessWebPQuality : WebPQuality;

    /// <summary>
    /// 現在の値をそのまま写した別インスタンスを返す。
    /// </summary>
    /// <remarks>
    /// 書き出しはバックグラウンドで行うため、その間に画面側で値が書き換わっても
    /// 影響が出ないよう、開始時点の値を固定するために使う。
    /// <b>項目を追加したらここにも追加すること。</b>写し漏れると、画面で選んだ設定が
    /// 黙って既定値に戻るという分かりにくい不具合になる。
    /// </remarks>
    public ExportSettings Clone() => new()
    {
        Folder = Folder,
        Format = Format,
        JpegQuality = JpegQuality,
        WebPQuality = WebPQuality,
        WebPLossless = WebPLossless,
        Naming = Naming,
        Prefix = Prefix,
        Suffix = Suffix,
        SequenceBaseName = SequenceBaseName,
        Overwrite = Overwrite,
    };
}
