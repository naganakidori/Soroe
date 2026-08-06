using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// 調整を適用した画像をファイルへ書き出す。
/// </summary>
public interface IImageExporter
{
    /// <summary>
    /// 一覧のファイルを順に処理して書き出す。
    /// </summary>
    /// <param name="sourcePaths">元ファイルの絶対パス。リストの順に処理する。</param>
    /// <param name="settings">書き出しの設定。</param>
    /// <param name="processing">適用する調整。</param>
    /// <param name="progress">進み具合の通知先。不要なら <see langword="null" />。</param>
    /// <param name="cancellationToken">中止の合図。1 枚を処理する前に確認する。</param>
    /// <returns>成功・スキップ・失敗を集計した結果。</returns>
    /// <remarks>
    /// 時間のかかる同期処理。<b>呼ぶ側が <c>Task.Run</c> などで UI スレッドから外すこと。</b>
    /// <para>
    /// 中止された場合も例外は投げない。そこまでに何枚書けたのかを報告するため、
    /// 結果の <see cref="ExportResult.Canceled" /> で返す。
    /// </para>
    /// </remarks>
    ExportResult Export(
        IReadOnlyList<string> sourcePaths,
        ExportSettings settings,
        ProcessingSettings processing,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// 1 枚の出力先を、実際には書き出さずに求める。
    /// </summary>
    /// <param name="sourcePath">元ファイルの絶対パス。</param>
    /// <param name="settings">書き出しの設定。</param>
    /// <returns>出力先の絶対パス。</returns>
    /// <remarks>実行前に出力先を画面へ 1 行表示するために使う。</remarks>
    string ResolveOutputPath(string sourcePath, ExportSettings settings);
}
