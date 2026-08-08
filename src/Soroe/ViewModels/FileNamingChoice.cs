using Soroe.Models;

namespace Soroe.ViewModels;

/// <summary>
/// 出力ファイル名の決め方のドロップダウンに並べる項目。
/// </summary>
/// <param name="Naming">対応する方式。</param>
/// <param name="Label">画面に出す文言。</param>
public sealed record FileNamingChoice(FileNaming Naming, string Label);
