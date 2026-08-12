using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// 枠線の設定（適用順序 8 番目）。
/// </summary>
/// <remarks>
/// <b>枠線は画像の内側に描く。外側には足さない。</b>外側に足すと出力寸法が変わり、
/// 長辺 1920 を指定したのに 1960 になる。ユーザーが 1920 を選ぶのはたいてい上限が
/// ある先へ渡すためで、それが黙って破れるのは、写真の外周が数画素隠れるのとは
/// 質の違う失敗である。構造の面でも、枠線は順序 8 でリサイズ（順序 2）より後に
/// あるため、外側に足すと<b>最後の工程が先の工程の約束を取り消す</b>ことになる。
/// <para>
/// <b>太さは出力画素の px で固定する。短辺に対する比率は採らない。</b>
/// リサイズ後にアスペクト比の違う画像が混ざると、比率では太さが揃わない
/// （長辺 1920 に揃えても 1920x1440 と 1920x1080 では短辺が 1440 と 1080 なので
/// 1.33 倍違う）。長辺に対する比率ならこれは起きないが、px 固定と差が出るのは
/// 「リサイズ無効かつ寸法がバラバラ」の場合だけで、<b>そもそもリサイズしていない
/// バッチは出力寸法自体が揃っていない</b>。枠線だけを相対的に揃えても並べたときに
/// 揃って見えるわけではないので、数値が結果そのものになる px を採る。
/// </para>
/// </remarks>
public sealed partial class FrameOption : ObservableObject
{
    /// <summary>設定できる太さの下限（px）。</summary>
    public const int MinThickness = 1;

    /// <summary>設定できる太さの上限（px）。</summary>
    public const int MaxThickness = 200;

    /// <summary>
    /// 実際に描ける太さの上限を、出力の短辺の何分の 1 とするか。
    /// </summary>
    /// <remarks>
    /// 内側に描く以上、太さが画像を食い尽くしうる。4 なら左右（上下）で短辺の
    /// 半分までを使う計算で、これ以上は絵が残らない。
    /// <para>
    /// <b>この頭打ちは黙って効かせない。</b>設定と結果が食い違うのはこのアプリが
    /// 一貫して避けてきた形なので、選択中の 1 枚について寸法表示の行に出す
    /// （<c>MainViewModel.UpdatePreviewSizeText</c>）。
    /// </para>
    /// </remarks>
    public const int ShortEdgeDivisor = 4;

    private int _thickness = 16;

    /// <summary>この項目を適用するかどうか。既定は OFF。</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>枠線の色。</summary>
    [ObservableProperty]
    public partial FrameColor Color { get; set; }

    /// <summary>
    /// 枠線の太さ（出力画素）。
    /// </summary>
    /// <remarks>
    /// 範囲外の値はここで丸める（理由は <see cref="BrightnessOption.Value" /> と同じ）。
    /// 出力の短辺による頭打ちは画像ごとに違うのでここでは行わず、
    /// <see cref="ResolveThickness" /> が受け持つ。
    /// </remarks>
    public int Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, Math.Clamp(value, MinThickness, MaxThickness));
    }

    /// <summary>
    /// 出力寸法に対して、実際に描く太さを返す。
    /// </summary>
    /// <param name="width">出力の幅（リサイズ後）。</param>
    /// <param name="height">出力の高さ（リサイズ後）。</param>
    /// <returns>描く太さ。この項目が無効なら 0。</returns>
    /// <remarks>
    /// <b>プレビューの倍率を掛ける前の、出力画素での値を返す。</b>倍率は呼び出し側で
    /// 掛ける。ここで掛けてしまうと、プレビューの寸法から頭打ちを計算することになり、
    /// プレビューと書き出しで太さが変わる。
    /// </remarks>
    public int ResolveThickness(int width, int height)
    {
        if (!Enabled)
        {
            return 0;
        }

        var cap = Math.Min(width, height) / ShortEdgeDivisor;

        // 短辺が極端に小さい画像では cap が 0 になる。枠線を有効にした以上、
        // 何も描かないより 1px でも描くほうが「有効にした」という設定と合う
        return Math.Max(1, Math.Min(Thickness, cap));
    }
}
