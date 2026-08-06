namespace Soroe.Services;

/// <summary>
/// 設定を適用するときの文脈。
/// </summary>
/// <param name="OriginalWidth">元画像の幅（縮小前）。</param>
/// <param name="OriginalHeight">元画像の高さ（縮小前）。</param>
/// <param name="PreviewScale">
/// これから処理する画像が、元画像に対して何倍かを表す値。書き出し時は 1.0。
/// </param>
/// <remarks>
/// 寸法に関わる調整（リサイズ、枠線の太さ）は、<b>まず元画像に対する出力寸法を決め、
/// そこに <paramref name="PreviewScale" /> を掛ける</b>という順序で計算する。
/// <para>
/// <b><paramref name="PreviewScale" /> から元画像の寸法を逆算してはいけない。</b>
/// 丸め誤差で 1px ずれ、プレビューと書き出しが一致しなくなる。そのため元画像の寸法を
/// 明示的に持ち回る。
/// </para>
/// </remarks>
internal readonly record struct RenderContext(int OriginalWidth, int OriginalHeight, double PreviewScale);
