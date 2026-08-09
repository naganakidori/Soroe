using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// 彩度の設定。
/// </summary>
/// <remarks>
/// HSV や Lab へ変換せず、<b>輝度（<c>BGR2GRAY</c>）との線形補間</b>で行う。
/// 色空間を往復すると 8bit の量子化でそれだけで画素が変わってしまい、
/// 「有効にしただけで画が変わる」状態になる。
/// <para>
/// また倍率 0 の結果が <c>BGR2GRAY</c> と完全に一致するため、後から入る
/// グレースケールと地続きになる。「彩度 -100」と「グレースケール ON」で
/// 違う絵が出るという食い違いが起きない。
/// </para>
/// </remarks>
public sealed partial class SaturationOption : ObservableObject
{
    /// <summary>設定できる値の下限。</summary>
    public const int MinValue = -100;

    /// <summary>設定できる値の上限。</summary>
    public const int MaxValue = 100;

    /// <summary>この項目を適用するかどうか。既定は OFF。</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    private int _value;

    /// <summary>
    /// 彩度の強さ。0 で変化なし。
    /// </summary>
    /// <remarks>
    /// 倍率は <c>1 + Value / 100</c> になる。-100 で完全なグレー、+100 で 2 倍。
    /// 範囲外の値はここで丸める（理由は <see cref="BrightnessOption.Value" /> と同じ）。
    /// </remarks>
    public int Value
    {
        get => _value;
        set => SetProperty(ref _value, Math.Clamp(value, MinValue, MaxValue));
    }
}
