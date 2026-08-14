namespace Soroe.Services;

/// <summary>
/// 書き出しに失敗した 1 枚。
/// </summary>
/// <param name="SourcePath">元ファイルの絶対パス。</param>
/// <param name="Message">画面に出す、日本語の短い理由。</param>
/// <param name="Detail">
/// 例外の原文。画面には出さず、失敗一覧をコピーしたときにだけ含める。
/// 不具合の報告を受けたときに原因を絞るための手掛かり。
/// </param>
public sealed record ExportFailure(string SourcePath, string Message, string Detail);

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
        int frameCapped,
        IReadOnlyList<ExportFailure> failures,
        bool canceled,
        string? abortReason)
    {
        Exported = exported;
        SkippedSameAsSource = skippedSameAsSource;
        FrameCapped = frameCapped;
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

    /// <summary>
    /// 枠線の太さが頭打ちに当たった枚数。
    /// </summary>
    /// <remarks>
    /// 画像ごとに短辺が違うので、指定した太さで描けるかどうかは開いてみないと分からない。
    /// 画面に出せるのは選択中の 1 枚だけなので、全件についてはここで数えて結果に載せる。
    /// <b>書き出し前に出す形は採らない</b> — 全件の寸法を先に読む必要があり、
    /// ヘッダだけ読む方法は WebP が Windows のコーデック拡張に依存するため、
    /// 入っていない環境で黙って数え落とす。詳細は CLAUDE.md「枠線」。
    /// </remarks>
    public int FrameCapped { get; }

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
    public static ExportResult Abort(string reason) => new(0, 0, 0, [], false, reason);

    /// <summary>
    /// 処理を行った結果を作る。
    /// </summary>
    public static ExportResult Completed(
        int exported,
        int skippedSameAsSource,
        int frameCapped,
        IReadOnlyList<ExportFailure> failures,
        bool canceled)
        => new(exported, skippedSameAsSource, frameCapped, failures, canceled, null);
}
