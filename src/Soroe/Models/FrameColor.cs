namespace Soroe.Models;

/// <summary>
/// 枠線の色。
/// </summary>
/// <remarks>
/// 固定色にはできない。白い背景に載せるなら黒、暗い背景なら白と、用途で必ず変わる。
/// かといってフルカラーのピッカーは、一括処理でその自由度が要る度合いに対して
/// UI が重すぎる（ピッカーのダイアログ、16 進の入力、プリセットへの保存形式）。
/// 無彩色 3 色ならドロップダウン 1 つで済み、設定は列挙のままプリセットに入る。
/// <para>
/// 任意色が要ると分かったら、この列挙に値を足すか ARGB へ移す。
/// <c>ProcessingSettings.SchemaVersion</c> があるので移行できる。
/// </para>
/// </remarks>
public enum FrameColor
{
    /// <summary>白。透過の合成色と同じで、このアプリが「足す色」の既定。</summary>
    White,

    /// <summary>黒。</summary>
    Black,

    /// <summary>グレー。明るい背景にも暗い背景にも載る。</summary>
    Gray,
}
