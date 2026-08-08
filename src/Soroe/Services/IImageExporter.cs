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
    /// 実行したらどうなるかを、実際には書き出さずに求める。
    /// </summary>
    /// <param name="sourcePaths">元ファイルの絶対パス。リストの順に評価する。</param>
    /// <param name="settings">書き出しの設定。</param>
    /// <returns>出力先の例と件数の見積もり。</returns>
    /// <remarks>
    /// <see cref="Export" /> と同じ解決処理を同じ順で回すので、表示と結果が一致する。
    /// <para>
    /// 上書きが選ばれていない場合、既存ファイルは必ず避けられるため
    /// <see cref="ExportPlan.OverwriteCount" /> も <see cref="ExportPlan.SkippedCount" /> も
    /// 必ず 0 になる。その場合は全件を調べず、1 件目だけを解決する。
    /// </para>
    /// </remarks>
    ExportPlan Plan(IReadOnlyList<string> sourcePaths, ExportSettings settings);
}
