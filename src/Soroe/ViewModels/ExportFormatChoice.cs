using Soroe.Models;

namespace Soroe.ViewModels;

/// <summary>
/// 出力形式のドロップダウンに並べる項目。
/// </summary>
/// <param name="Format">対応する形式。</param>
/// <param name="Label">画面に出す文言。</param>
public sealed record ExportFormatChoice(ExportFormat Format, string Label);
