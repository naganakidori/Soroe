using Soroe.Models;

namespace Soroe.ViewModels;

/// <summary>
/// 枠線の色のドロップダウンに並べる項目。
/// </summary>
/// <param name="Color">対応する色。</param>
/// <param name="Label">画面に出す文言。</param>
public sealed record FrameColorChoice(FrameColor Color, string Label);
