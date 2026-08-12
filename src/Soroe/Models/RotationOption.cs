using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// 回転の設定（適用順序 1 番目）。
/// </summary>
/// <remarks>
/// 順序が 1 番目であるため、以降の全項目が回転後の画像を前提にする。噛み合わせは
/// 次のとおり（CLAUDE.md「適用順序は固定」）。
/// <list type="bullet">
/// <item>リサイズ … 「長辺を N にする」の長辺は 90 度回転で変わらない。
/// 入れ替わるのはどちらが長辺かだけ</item>
/// <item>大津のしきい値 … 倍率は長辺から決まるので不変。しきい値はヒストグラムから
/// 決まり、90 度回転は画素の並べ替えなのでヒストグラムは変わらない</item>
/// <item>シャープ … ガウシアンは等方で x / y 同一のカーネルなので 90 度回転と可換</item>
/// <item>枠線 … 回転後の寸法の内側に一様な幅で描くだけ</item>
/// </list>
/// この可換性は検証ハーネスで「回転 → 全チェーン」と「全チェーン → 回転」の
/// 全画素一致として常設してある。崩れたらどの項目が噛み合わなくなったかが分かる。
/// </remarks>
public sealed partial class RotationOption : ObservableObject
{
    private RotationAngle _angle = RotationAngle.Clockwise90;

    /// <summary>この項目を適用するかどうか。既定は OFF。</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>
    /// 回す角度。時計回り。
    /// </summary>
    /// <remarks>
    /// 列挙に無い値はここで既定へ丸める。<see cref="System.Text.Json" /> は列挙の
    /// 基底型に収まる数値なら定義の有無にかかわらず通すため、手で書き換えた
    /// 設定ファイルから未定義の値が入りうる。丸めを View 側に置かない理由は
    /// <see cref="BrightnessOption.Value" /> と同じ。
    /// </remarks>
    public RotationAngle Angle
    {
        get => _angle;
        set => SetProperty(
            ref _angle,
            Enum.IsDefined(value) ? value : RotationAngle.Clockwise90);
    }

    /// <summary>
    /// 元画像の寸法に対して、この設定が指す回転後の寸法を返す。
    /// </summary>
    /// <param name="width">回転前の幅。</param>
    /// <param name="height">回転前の高さ。</param>
    /// <returns>回転後の寸法。この項目が無効なら元の寸法をそのまま返す。</returns>
    public (int Width, int Height) ResolveSize(int width, int height)
        => Enabled && Angle != RotationAngle.Half ? (height, width) : (width, height);
}
