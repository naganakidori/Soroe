using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// コントラストの設定。
/// </summary>
/// <remarks>
/// 明るさと同じ 1 本の LUT にまとめて適用する（<c>ImageRenderer.BuildToneLut</c>）。
/// 2 回に分けて通すと中間の丸めが 2 度入り、階調がわずかに崩れる。
/// </remarks>
public sealed partial class ContrastOption : ObservableObject
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
    /// コントラストの強さ。0 で変化なし。
    /// </summary>
    /// <remarks>
    /// 倍率は <c>1 + Value / 100</c> になる。-100 で 0 倍（全面が中間の明るさに潰れる）、
    /// +100 で 2 倍。明るさと範囲・既定を揃えてあるので、UI も同じ形で並べられる。
    /// <para>
    /// 範囲外の値はここで丸める。View 側の丸め（依存関係プロパティの CoerceValue）は
    /// バインディングのソースまで伝わらないため、モデル側で必ず抑える必要がある。
    /// </para>
    /// </remarks>
    public int Value
    {
        get => _value;
        set => SetProperty(ref _value, Math.Clamp(value, MinValue, MaxValue));
    }
}
