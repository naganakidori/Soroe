namespace Soroe.Models;

/// <summary>
/// 出力ファイル名の決め方。
/// </summary>
public enum FileNaming
{
    /// <summary>元のファイル名をそのまま使う。</summary>
    KeepOriginal = 0,

    /// <summary>元のファイル名の前後に文字を足す。</summary>
    PrefixSuffix,

    /// <summary>ベース名と連番で付け直す。</summary>
    Sequence,
}
