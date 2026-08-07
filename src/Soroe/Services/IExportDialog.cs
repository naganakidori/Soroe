using Soroe.ViewModels;

namespace Soroe.Services;

/// <summary>
/// 書き出しの設定・実行・結果表示を行うダイアログ。
/// </summary>
/// <remarks>
/// 設定・実行・進捗・結果をこの 1 か所で完結させる。押した場所と結果が出る場所が
/// 違うと操作として不自然になるため。
/// <para>
/// モーダルで開く。書き出し中に本体を触れないことは制限ではなく、
/// 「いま触ってよいのか分からない」という曖昧さを無くす利点として扱う。
/// ダイアログの移動、アプリの最小化、他アプリへの切り替えは妨げられない。
/// </para>
/// </remarks>
public interface IExportDialog
{
    /// <summary>
    /// ダイアログを開き、閉じられるまで待つ。
    /// </summary>
    /// <param name="viewModel">ダイアログに表示する内容。</param>
    /// <remarks>
    /// 閉じても<b>設定は破棄しない</b>。出力先を選び直してから「やはり後で」と
    /// 閉じたときに消えるのは煩わしいため。ボタンの文言も「キャンセル」ではなく
    /// 「閉じる」にしてある。
    /// </remarks>
    void Show(ExportDialogViewModel viewModel);
}
