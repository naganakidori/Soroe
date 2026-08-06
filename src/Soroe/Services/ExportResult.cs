namespace Soroe.Services;

/// <summary>
/// 書き出しに失敗した 1 枚。
/// </summary>
/// <param name="SourcePath">元ファイルの絶対パス。</param>
/// <param name="Message">失敗した理由。</param>
public sealed record ExportFailure(string SourcePath, string Message);

/// <summary>
/// 書き出しの結果。
/// </summary>
/// <remarks>
/// 1 枚の失敗で全体を止めないため、成功・スキップ・失敗をすべて集めて返す。
/// キャンセルされた場合も、そこまでに何枚書けたかが分かるように例外ではなく
/// この結果で返す。
/// </remarks>
public sealed class ExportResult
{
    private ExportResult(
        int exported,
        int skippedSameAsSource,
        IReadOnlyList<ExportFailure> failures,
        bool canceled,
        string? abortReason)
    {
        Exported = exported;
        SkippedSameAsSource = skippedSameAsSource;
        Failures = failures;
        Canceled = canceled;
        AbortReason = abortReason;
    }

    /// <summary>書き出せた枚数。</summary>
    public int Exported { get; }

    /// <summary>
    /// 出力先が元ファイルと同じだったため飛ばした枚数（安全ガード）。
    /// </summary>
    public int SkippedSameAsSource { get; }

    /// <summary>失敗した 1 枚ごとの内訳。</summary>
    public IReadOnlyList<ExportFailure> Failures { get; }

    /// <summary>途中で中止されたかどうか。</summary>
    public bool Canceled { get; }

    /// <summary>
    /// 処理を開始できなかった理由。開始できた場合は <see langword="null" />。
    /// </summary>
    public string? AbortReason { get; }

    /// <summary>
    /// 処理を開始せずに終わった結果を作る。
    /// </summary>
    /// <param name="reason">開始できなかった理由。</param>
    public static ExportResult Abort(string reason) => new(0, 0, [], false, reason);

    /// <summary>
    /// 処理を行った結果を作る。
    /// </summary>
    public static ExportResult Completed(
        int exported, int skippedSameAsSource, IReadOnlyList<ExportFailure> failures, bool canceled)
        => new(exported, skippedSameAsSource, failures, canceled, null);
}
