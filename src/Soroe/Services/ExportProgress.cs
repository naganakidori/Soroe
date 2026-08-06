namespace Soroe.Services;

/// <summary>
/// 書き出しの進み具合。
/// </summary>
/// <param name="Completed">処理を終えた枚数。</param>
/// <param name="Total">処理する総枚数。</param>
/// <param name="CurrentFileName">いま処理しているファイル名。終了時は空。</param>
public readonly record struct ExportProgress(int Completed, int Total, string CurrentFileName);
