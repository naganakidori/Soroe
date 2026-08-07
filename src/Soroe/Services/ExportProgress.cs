namespace Soroe.Services;

/// <summary>
/// 書き出しの進み具合。
/// </summary>
/// <param name="Completed">処理を終えた枚数。</param>
/// <param name="Total">処理する総枚数。</param>
/// <param name="SourceFileName">いま処理している元ファイルの名前。終了時は空。</param>
/// <param name="OutputFileName">
/// 書き出し先のファイル名。同名衝突の連番まで反映した<b>最終的な名前</b>で、
/// 書き込み中の一時ファイル名ではない。スキップした場合と終了時は空。
/// </param>
/// <param name="Skipped">安全ガードにより書き出さなかった場合は <see langword="true" />。</param>
/// <remarks>
/// 元の名前と出力名を並べて見せることで、形式やファイル名の指定が効いているかを
/// 実行中に確認できる。
/// </remarks>
public readonly record struct ExportProgress(
    int Completed,
    int Total,
    string SourceFileName,
    string OutputFileName,
    bool Skipped);
