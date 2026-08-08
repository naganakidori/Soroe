using System.IO;

namespace Soroe.Models;

/// <summary>
/// ファイル名の一部として使う文字列の整形。
/// </summary>
/// <remarks>
/// プレフィックス・サフィックス・連番のベース名で共通に使う。片方だけに入れると、
/// 項目が増えたときに漏れる。
/// </remarks>
public static class FileNamePart
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    /// <summary>
    /// ファイル名に使えない文字を取り除く。
    /// </summary>
    /// <remarks>
    /// View 側だけで弾くとバインディングのソースまで伝わらないため、値の側で保証する
    /// （数値を <c>Math.Clamp</c> で丸めるのと同じ考え方）。
    /// <c>Path.GetInvalidFileNameChars</c> は <c>\ / : * ? " &lt; &gt; |</c> に加えて
    /// 制御文字も含む。制御文字はコピー＆ペーストで紛れ込むことがある。
    /// </remarks>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny(Invalid) < 0
            ? value
            : new string(value.Where(c => Array.IndexOf(Invalid, c) < 0).ToArray());
    }
}
