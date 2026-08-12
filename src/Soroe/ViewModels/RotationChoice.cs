using Soroe.Models;

namespace Soroe.ViewModels;

/// <summary>
/// 回転のドロップダウンに並べる項目。
/// </summary>
/// <param name="Angle">対応する角度。</param>
/// <param name="Label">画面に出す文言。</param>
public sealed record RotationChoice(RotationAngle Angle, string Label);
