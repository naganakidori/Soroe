namespace Soroe.Models;

/// <summary>
/// 回転の角度。時計回り。
/// </summary>
/// <remarks>
/// <b>90 度単位のみで、任意角度は入れない。</b>理由は補間や余白の色ではなく、
/// 任意角度が一括処理と噛み合わないこと。傾き補正の角度は 1 枚ずつ見ないと
/// 決められないもので、同じ 2.3 度を 200 枚にかければ 199 枚が傾く。
/// 全件に同じ値をかけるという前提の下で意味を持つのは 90 度単位だけである。
/// <para>
/// 副次的に、90 度単位なら <c>Cv2.Rotate</c> が転置と反転だけで済むため補間による
/// 画素の変化がなく、余白も出ず、寸法は縦横が入れ替わるだけになる。
/// </para>
/// <para>
/// 「回転しない」はこの列挙に持たせず <c>RotationOption.Enabled</c> で表す。
/// 0 度を足すと「回転しない」の言い方が 2 つできてしまう。
/// </para>
/// </remarks>
public enum RotationAngle
{
    /// <summary>右に 90 度。</summary>
    Clockwise90 = 90,

    /// <summary>180 度。</summary>
    Half = 180,

    /// <summary>左に 90 度。</summary>
    CounterClockwise90 = 270,
}
