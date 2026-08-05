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

    /// <summary>
    /// 各画素に加算する量。0 で変化なし。
    /// </summary>
    [ObservableProperty]
    public partial int Value { get; set; }
}
