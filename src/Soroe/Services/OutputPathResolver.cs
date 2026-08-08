using System.Globalization;
using System.IO;
using Soroe.Common;
using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// 出力先のファイル名を決める。
/// </summary>
/// <remarks>
/// ファイル名に関する判断をここに集約する。書き出し本体（ループ・進捗・安全ガード・
/// 一時ファイル）を触らずに済ませるための配置。
/// <para>
/// このクラス自身は状態を持たない。同じ実行の中で使ったパスは <c>reserved</c> として
/// 呼び出し側から渡す。出力パスのプレビューにも同じ処理を使うため、内部に状態を持たせると
/// 表示のたびに汚染されてしまう。
/// </para>
/// </remarks>
public sealed class OutputPathResolver
{
    /// <summary>連番の最小桁数。</summary>
    private const int MinimumSequenceDigits = 3;

    /// <summary>
    /// 別名を試す上限。
    /// </summary>
    /// <remarks>
    /// 同じ出力先へ繰り返し書き出すと <c>(2) (3) ...</c> が溜まる。上限が無いと、
    /// 病的な状況で無言のまま遅くなるため、明確な失敗に変える。
    /// </remarks>
    private const int MaxAlternatives = 999;

    /// <summary>
    /// 元ファイルに対する出力先の絶対パスを決める。
    /// </summary>
    /// <param name="sourcePath">元ファイルの絶対パス。</param>
    /// <param name="settings">書き出しの設定。</param>
    /// <param name="index">リスト内の位置（0 始まり）。連番に使う。</param>
    /// <param name="total">処理する総件数。連番の桁数に使う。</param>
    /// <param name="reserved">
    /// この実行で既に使うと決めたパス。<b>上書きが選ばれていても、ここにあるパスは避ける。</b>
    /// </param>
    /// <returns>出力先の絶対パス。</returns>
    public string Resolve(
        string sourcePath,
        ExportSettings settings,
        int index,
        int total,
        IReadOnlySet<string> reserved)
    {
        if (string.IsNullOrWhiteSpace(settings.Folder))
        {
            throw new InvalidOperationException("出力先が指定されていません。");
        }

        var folder = Path.GetFullPath(settings.Folder);
        var extension = ResolveExtension(sourcePath, settings.Format);
        var name = BuildName(sourcePath, settings, index, total);

        var candidate = Path.Combine(folder, name + extension);
        if (IsFree(candidate, settings, reserved))
        {
            return candidate;
        }

        for (var i = 2; i <= MaxAlternatives; i++)
        {
            var numbered = Path.Combine(folder, $"{name} ({i}){extension}");
            if (IsFree(numbered, settings, reserved))
            {
                return numbered;
            }
        }

        throw new UserMessageException(
            $"同名のファイルが {MaxAlternatives} 個以上あるため別名を付けられません: {name}{extension}");
    }

    /// <summary>
    /// そのパスを使ってよいか。
    /// </summary>
    /// <remarks>
    /// <b>予約済みのパスは上書き設定に関わらず避ける。</b>
    /// <c>File.Exists</c> では「前回の実行結果」と「今回この実行で書いたファイル」を
    /// 区別できないため、後者を潰さないよう別に記録している。
    /// </remarks>
    private static bool IsFree(string path, ExportSettings settings, IReadOnlySet<string> reserved)
        => !reserved.Contains(path) && (settings.Overwrite || !File.Exists(path));

    /// <summary>
    /// 拡張子を除いた出力ファイル名を組み立てる。
    /// </summary>
    private static string BuildName(string sourcePath, ExportSettings settings, int index, int total)
    {
        var original = Path.GetFileNameWithoutExtension(sourcePath);

        var name = settings.Naming switch
        {
            FileNaming.PrefixSuffix => settings.Prefix + original + settings.Suffix,
            FileNaming.Sequence => BuildSequenceName(settings.SequenceBaseName, index, total),
            _ => original,
        };

        // Windows はファイル名の末尾に空白とドットを置けない。
        // 3 つのモードすべてをここ 1 か所で始末する
        name = name.TrimEnd(' ', '.');

        return name.Length > 0 ? name : original;
    }

    /// <summary>
    /// 連番の名前を作る。
    /// </summary>
    /// <remarks>
    /// 桁数は総件数に合わせる（最小 3 桁）。ゼロ詰めしないと、辞書順で並べる道具で
    /// 10 が 2 より前に来る。開始番号は 1 で固定し、選ばせない。
    /// <para>
    /// ベース名が空のときは区切りも付けない。<c>_001.jpg</c> のような不自然な名前を
    /// 避けつつ、「番号だけの連番」という要求にも応えられる。
    /// </para>
    /// </remarks>
    private static string BuildSequenceName(string baseName, int index, int total)
    {
        var digits = Math.Max(MinimumSequenceDigits, Math.Max(total, 1).ToString(CultureInfo.InvariantCulture).Length);
        var number = (index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');

        return baseName.Length > 0 ? $"{baseName}_{number}" : number;
    }

    /// <summary>
    /// 出力する形式から拡張子を決める。
    /// </summary>
    /// <remarks>
    /// 「元の形式を維持」では元ファイルの拡張子をそのまま引き継ぐ。<c>.JPG</c> のような
    /// 大文字も、また <c>.jpg</c> と <c>.jpeg</c> の違いも変えない。ユーザーが付けた名前を
    /// 勝手に書き換えないため。
    /// </remarks>
    private static string ResolveExtension(string sourcePath, ExportFormat format) => format switch
    {
        ExportFormat.Jpeg => ".jpg",
        ExportFormat.Png => ".png",
        ExportFormat.Bmp => ".bmp",
        ExportFormat.WebP => ".webp",
        _ => Path.GetExtension(sourcePath),
    };
}
