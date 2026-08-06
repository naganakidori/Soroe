using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// 明るさの設定。
/// </summary>
public sealed partial class BrightnessOption : ObservableObject
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
    /// 各画素に加算する量。0 で変化なし。
    /// </summary>
    /// <remarks>
    /// 範囲外の値はここで丸める。View 側の丸め（依存関係プロパティの CoerceValue）は
    /// バインディングのソースまで伝わらないため、モデル側で必ず抑える必要がある。
    /// これを怠ると、画面には 100 と出ているのに実際は 1000 で処理される、という
    /// 表示と結果の食い違いが起きる。
    /// </remarks>
    public int Value
    {
        get => _value;
        set => SetProperty(ref _value, Math.Clamp(value, MinValue, MaxValue));
    }
}
