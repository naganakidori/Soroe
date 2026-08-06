namespace Soroe.ViewModels;

/// <summary>
/// リサイズのドロップダウンに並べる候補。
/// </summary>
/// <param name="LongestEdge">
/// 長辺の上限。<see langword="null" /> のときは「自由入力」を表す。
/// </param>
/// <param name="Label">画面に出す文言。</param>
/// <remarks>
/// 数値の自由入力だけにすると「何を入れればいいか分からない」ため、よく使う値を
/// 用途つきで並べる。
/// </remarks>
public sealed record ResizePreset(int? LongestEdge, string Label);
