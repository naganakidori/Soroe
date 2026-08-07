using Soroe.ViewModels;

namespace Soroe.Services;

/// <summary>
/// 書き出しの設定と確認を行うダイアログ。
/// </summary>
/// <remarks>
/// 書き出しは不可逆な操作なので、実行の前に必ずここを通す。
/// ダイアログを挟む形にすることで、主画面から出力設定の場所を空けられる。
/// </remarks>
public interface IExportDialog
{
    /// <summary>
    /// 設定を確認させる。
    /// </summary>
    /// <param name="viewModel">ダイアログに表示する内容。</param>
    /// <returns>実行が選ばれた場合は <see langword="true" />。閉じられた場合は <see langword="false" />。</returns>
    /// <remarks>
    /// 閉じられた場合も<b>設定は破棄しない</b>。出力先を選び直してから
    /// 「やはり後で」と閉じたときに消えるのは煩わしいため。
    /// ボタンの文言も「キャンセル」ではなく「閉じる」にしてある。
    /// </remarks>
    bool Confirm(ExportDialogViewModel viewModel);
}
