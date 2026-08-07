using System.Diagnostics;
using System.IO;

namespace Soroe.Common;

/// <summary>
/// 例外を、原因が伝わる日本語の短い文にする。
/// </summary>
/// <remarks>
/// .NET の例外メッセージは英語なので、そのまま画面に出すと日本語 UI の中で浮くうえ、
/// 一般の利用者には何を直せばよいか伝わらない。
/// <para>
/// <b>原文は捨てない。</b><see cref="Log" /> でデバッグ出力へ流し、失敗一覧は
/// 原文込みでクリップボードへコピーできるようにしてある。不具合の報告を受けたときに
/// 原因を絞れるようにするため。
/// </para>
/// </remarks>
public static class FileErrorMessage
{
    /// <summary>空き容量不足を表す Win32 のエラーコード。</summary>
    private const int ErrorHandleDiskFull = 0x27;

    /// <summary>空き容量不足を表す Win32 のエラーコード。</summary>
    private const int ErrorDiskFull = 0x70;

    /// <summary>他のプロセスが使用中であることを表す Win32 のエラーコード。</summary>
    private const int ErrorSharingViolation = 0x20;

    /// <summary>
    /// 例外を日本語の短い文にする。
    /// </summary>
    /// <param name="exception">対象の例外。</param>
    /// <param name="path">
    /// 関係するパス。分かる場合に渡すと、原因の判定がより正確になる。
    /// </param>
    public static string Describe(Exception exception, string? path = null) => exception switch
    {
        // アプリ自身が投げた、そのまま見せてよいメッセージ
        UserMessageException user => user.Message,

        DirectoryNotFoundException => "フォルダが見つかりません",
        FileNotFoundException => "ファイルが見つかりません",
        UnauthorizedAccessException => "書き込みが許可されていません",
        PathTooLongException => "パスが長すぎます",

        // ArgumentException は発生源が広い。実際に不正な文字がある場合に限って断定する
        ArgumentException when HasInvalidCharacters(path) => "パスに使えない文字が含まれています",

        NotSupportedException => "パスの形式が正しくありません",

        IOException io when Win32Code(io) is ErrorDiskFull or ErrorHandleDiskFull => "空き容量が足りません",
        IOException io when Win32Code(io) == ErrorSharingViolation => "他のプログラムが使用中です",
        IOException => "ファイルの読み書きに失敗しました",

        // 対応表から漏れたもの。型名だけ添えて後から追えるようにする（英文は出さない）
        _ => $"予期しないエラーです（{exception.GetType().Name}）",
    };

    /// <summary>
    /// 例外の原文をデバッグ出力へ流す。
    /// </summary>
    /// <remarks>
    /// 画面には日本語の要約しか出さないため、原因を追う手掛かりをここに残す。
    /// </remarks>
    public static void Log(string context, Exception exception)
        => Trace.WriteLine($"[Soroe] {context}: {exception}");

    /// <summary>
    /// 例外の原文を 1 行にまとめる。失敗一覧のコピー用。
    /// </summary>
    public static string Detail(Exception exception)
        => $"{exception.GetType().FullName}: {exception.Message}";

    private static int Win32Code(IOException exception) => exception.HResult & 0xFFFF;

    private static bool HasInvalidCharacters(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return true;
        }

        try
        {
            var name = Path.GetFileName(path);
            return name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
