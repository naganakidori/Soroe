using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// シャープの設定。
/// </summary>
/// <remarks>
/// 方式はアンシャープマスク（<c>元 + 量 ×（元 − ぼかし）</c>）。
/// ラプラシアンの 3×3 加算は採らない。半径が 1px に固定されるため、
/// プレビューの倍率に合わせて縮めることができず、プレビューと書き出しで
/// 違う絵になる（詳細は <c>ImageRenderer.SharpenSigma</c>）。
/// <para>
/// 半径（σ）は UI に出さず固定する。2 本目のスライダーを出すほどの価値がない。
/// 必要と分かってから足せる。
/// </para>
/// <para>
/// <b>二値化が有効なときは、この項目は何も変えない。</b>0 と 255 しかない画像では、
/// 明るい画素は 255 を超えて、暗い画素は 0 を下回って、いずれも飽和で元の値に戻る。
/// 実測でも 3600 万要素すべてが変化しなかった。設定を自動で切り替えたりはせず、
/// 文字で断る。
/// </para>
/// </remarks>
public sealed partial class SharpenOption : ObservableObject
{
    /// <summary>設定できる値の下限。0 で変化なし。</summary>
    public const int MinValue = 0;

    /// <summary>設定できる値の上限。</summary>
    public const int MaxValue = 100;

    /// <summary>この項目を適用するかどうか。既定は OFF。</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    private int _value = 50;

    /// <summary>
    /// シャープの強さ。0 で変化なし。
    /// </summary>
    /// <remarks>
    /// アンシャープマスクの「量」は <c>Value / 50</c>（0〜2.0）になる。
    /// <para>
    /// 既定を 0 ではなく 50 にしているのは、0 だと有効にしても何も起きないため。
    /// 上下に振る明るさなどと違い、この項目は下限が「効果なし」になる。
    /// <see cref="ResizeOption.LongestEdge" /> が既定 1920 を持つのと同じ考え方。
    /// </para>
    /// <para>
    /// 範囲外の値はここで丸める（理由は <see cref="BrightnessOption.Value" /> と同じ）。
    /// </para>
    /// </remarks>
    public int Value
    {
        get => _value;
        set => SetProperty(ref _value, Math.Clamp(value, MinValue, MaxValue));
    }
}
