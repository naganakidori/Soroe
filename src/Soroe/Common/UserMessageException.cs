namespace Soroe.Common;

/// <summary>
/// 利用者にそのまま見せてよい日本語のメッセージを持つ例外。
/// </summary>
/// <remarks>
/// <see cref="FileErrorMessage" /> はこの例外だけメッセージをそのまま通す。
/// アプリ自身が原因を分かって投げる場合に使い、.NET が投げる例外と区別する。
/// </remarks>
/// <param name="message">利用者に見せる日本語のメッセージ。</param>
public sealed class UserMessageException(string message) : Exception(message);
