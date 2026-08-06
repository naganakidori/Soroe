using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// リサイズの設定。
/// </summary>
/// <remarks>
/// 指定方法は「長辺を何 px 以内に収めるか」のみ。パーセント指定は入れない。
/// 一括処理ではスマホ写真・カメラ・スクリーンショットが混在するのが普通で、
/// 一律に 50% をかけると出力寸法がばらばらになり、「揃える」という目的と逆になる。
/// 長辺の px 指定なら、何を入れても上限が保証される。
/// </remarks>
public sealed partial class ResizeOption : ObservableObject
{
    /// <summary>指定できる長辺の下限。</summary>
    public const int MinValue = 16;

    /// <summary>指定できる長辺の上限。</summary>
    public const int MaxValue = 20000;

    private int _longestEdge = 1920;

    /// <summary>この項目を適用するかどうか。既定は OFF。</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>
    /// 長辺の上限（ピクセル）。
    /// </summary>
    /// <remarks>
    /// 範囲外の値はここで丸める。View 側の丸めはバインディングのソースまで伝わらないため。
    /// </remarks>
    public int LongestEdge
    {
        get => _longestEdge;
        set => SetProperty(ref _longestEdge, Math.Clamp(value, MinValue, MaxValue));
    }

    /// <summary>
    /// 元画像の寸法に対して、この設定が指す出力寸法を返す。
    /// </summary>
    /// <param name="width">元画像の幅。</param>
    /// <param name="height">元画像の高さ。</param>
    /// <returns>出力する寸法。この項目が無効なら元の寸法をそのまま返す。</returns>
    /// <remarks>
    /// <b>拡大はしない。</b>長辺が上限以下の画像はそのままの寸法を返す。一括処理で
    /// 小さい画像を引き伸ばす場面はまず無く、やれば画質が落ちるだけであるため、
    /// 選択式にせず固定の挙動とする。
    /// <para>
    /// 係数は <c>上限 ÷ 長辺</c> なので、長い側は必ず指定値ちょうどになる。
    /// </para>
    /// </remarks>
    public (int Width, int Height) ResolveSize(int width, int height)
    {
        if (!Enabled)
        {
            return (width, height);
        }

        var longest = Math.Max(width, height);
        if (longest <= LongestEdge)
        {
            return (width, height);
        }

        var factor = (double)LongestEdge / longest;
        return (
            Math.Max(1, (int)Math.Round(width * factor)),
            Math.Max(1, (int)Math.Round(height * factor)));
    }
}
