namespace Soroe.Services;

/// <summary>
/// 実行したらどうなるかの見積もり。
/// </summary>
/// <param name="FirstOutputPath">1 件目の出力先。画面に出す例。</param>
/// <param name="Total">処理する総件数。</param>
/// <param name="OverwriteCount">既存のファイルを置き換える件数。</param>
/// <param name="SkippedCount">安全ガードで飛ばされる件数。</param>
/// <remarks>
/// 書き出しと<b>同じ経路</b>で計算する。別の計算で件数を出すと、表示と結果がずれる。
/// <para>
/// レコードなので値で比較できる。実行の直前に計算し直し、表示していた内容と
/// 食い違っていないかを確かめるのに使う。
/// </para>
/// </remarks>
public sealed record ExportPlan(
    string FirstOutputPath,
    int Total,
    int OverwriteCount,
    int SkippedCount)
{
    /// <summary>対象が無いときの見積もり。</summary>
    public static ExportPlan Empty { get; } = new(string.Empty, 0, 0, 0);

    /// <summary>すべてが安全ガードで飛ばされるかどうか。</summary>
    /// <remarks>この状態で実行しても 1 枚も書き出されないため、実行させない。</remarks>
    public bool IsAllSkipped => Total > 0 && SkippedCount == Total;
}
