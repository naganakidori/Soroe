using System.IO;
using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// 出力先のファイル名を決める。
/// </summary>
/// <remarks>
/// ファイル名に関する判断をここに集約する。第 2 段で増えるプレフィックス / サフィックス /
/// 連番リネーム / 形式変換による拡張子の変化も、すべてこのクラスの中だけで完結させる。
/// 書き出し本体（ループ・進捗・安全ガード・一時ファイル）を触らずに済ませるための配置。
/// </remarks>
public sealed class OutputPathResolver
{
    /// <summary>
    /// 連番を試す上限。これを超えるほど同名ファイルがある場合は異常とみなす。
    /// </summary>
    private const int MaxAttempts = 10000;

    /// <summary>
    /// 元ファイルに対する出力先の絶対パスを決める。
    /// </summary>
    /// <param name="sourcePath">元ファイルの絶対パス。</param>
    /// <param name="settings">書き出しの設定。</param>
    /// <returns>出力先の絶対パス。</returns>
    /// <remarks>
    /// 同名のファイルが既にある場合は <c>名前 (2).jpg</c> のように連番を付ける。
    /// <para>
    /// 既存ファイルの有無で判断するため、<b>順次処理であることが前提</b>になる。
    /// N 枚目を書き終えてから N+1 枚目の名前を決めるので、同じ実行の中で名前が
    /// ぶつかった場合も正しく連番が付く。
    /// </para>
    /// </remarks>
    public string Resolve(string sourcePath, ExportSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Folder))
        {
            throw new InvalidOperationException("出力先が指定されていません。");
        }

        var folder = Path.GetFullPath(settings.Folder);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = ResolveExtension(sourcePath, settings.Format);

        var candidate = Path.Combine(folder, name + extension);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 2; i < MaxAttempts; i++)
        {
            var numbered = Path.Combine(folder, $"{name} ({i}){extension}");
            if (!File.Exists(numbered))
            {
                return numbered;
            }
        }

        throw new IOException($"同名のファイルが多すぎて連番を付けられません: {name}{extension}");
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
