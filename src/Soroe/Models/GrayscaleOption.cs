using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// グレースケールの設定。
/// </summary>
/// <remarks>
/// 値を持たない。掛けるか掛けないかしかないため。
/// <para>
/// 実装は <c>BGR2GRAY</c>。これは「彩度 -100」と同じ結果になる（全画素一致を検証済み）。
/// 彩度を輝度との線形補間で実装した根拠がここにある。
/// </para>
/// <para>
/// 二値化が有効なときは、この項目の ON / OFF は結果に影響しない。二値化も内部で
/// 輝度へ落とすためで、<c>BGR2GRAY</c> は既にグレーの画像に対して冪等である
/// （こちらも検証済み）。<b>だからといって設定を自動で切り替えたりはしない。</b>
/// 触っていない設定が勝手に変わるほうが分かりにくい。
/// </para>
/// </remarks>
public sealed partial class GrayscaleOption : ObservableObject
{
    /// <summary>この項目を適用するかどうか。既定は OFF。</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }
}
