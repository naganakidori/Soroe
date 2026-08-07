using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soroe.Common;
using Soroe.Models;
using Soroe.Services;

namespace Soroe.ViewModels;

/// <summary>
/// 書き出しダイアログの ViewModel。
/// </summary>
/// <remarks>
/// 設定は<b>本体と同じ <see cref="ExportSettings" /> の実体を直接編集する</b>。
/// 閉じても値を保持する仕様なので、写して確定するという仕組みが要らない。
/// 主画面のサマリ行もその場で追随する。
/// </remarks>
public sealed partial class ExportDialogViewModel : ObservableObject
{
    private readonly IImageExporter _exporter;
    private readonly IFolderPicker _folderPicker;
    private readonly IReadOnlyList<string> _sourcePaths;

    /// <summary>
    /// <see cref="ExportDialogViewModel" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="settings">編集対象の設定。実体をそのまま受け取る。</param>
    /// <param name="sourcePaths">処理する元ファイルのパス。</param>
    /// <param name="exporter">出力先の算出に使う実装。</param>
    /// <param name="folderPicker">フォルダ選択ダイアログの実装。</param>
    public ExportDialogViewModel(
        ExportSettings settings,
        IReadOnlyList<string> sourcePaths,
        IImageExporter exporter,
        IFolderPicker folderPicker)
    {
        Output = settings;
        _sourcePaths = sourcePaths;
        _exporter = exporter;
        _folderPicker = folderPicker;

        Output.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(ShowJpegQuality));
            OnPropertyChanged(nameof(ShowWebPQuality));
            UpdateOutputPathPreview();
        };

        UpdateOutputPathPreview();
    }

    /// <summary>書き出しの設定。</summary>
    public ExportSettings Output { get; }

    /// <summary>処理する枚数。</summary>
    public int FileCount => _sourcePaths.Count;

    /// <summary>出力形式のドロップダウンに並べる項目。</summary>
    public IReadOnlyList<ExportFormatChoice> ExportFormats { get; } =
    [
        new(ExportFormat.KeepOriginal, "元の形式を維持"),
        new(ExportFormat.Jpeg, "JPEG (.jpg)"),
        new(ExportFormat.Png, "PNG (.png)"),
        new(ExportFormat.Bmp, "BMP (.bmp)"),
        new(ExportFormat.WebP, "WebP (.webp)"),
    ];

    /// <summary>JPEG 品質の欄を出すかどうか。</summary>
    /// <remarks>
    /// 「元の形式を維持」のときは入力次第で JPEG にも WebP にもなるため、両方出す。
    /// 隠すと、実際に効いている設定が見えないことになる。
    /// </remarks>
    public bool ShowJpegQuality => Output.Format is ExportFormat.KeepOriginal or ExportFormat.Jpeg;

    /// <summary>WebP 品質の欄を出すかどうか。</summary>
    public bool ShowWebPQuality => Output.Format is ExportFormat.KeepOriginal or ExportFormat.WebP;

    /// <summary>実行できる状態かどうか。</summary>
    /// <remarks>
    /// 出力先が必須であるという判定はここが持つ。主画面のボタンは
    /// 「出力先を指定するためにダイアログを開く」ためのものなので、
    /// 出力先を条件にすると開けなくなる。
    /// </remarks>
    public bool CanRun => !string.IsNullOrWhiteSpace(Output.Folder);

    /// <summary>実行前に見せる、出力先の 1 行表示。</summary>
    [ObservableProperty]
    public partial string OutputPathPreview { get; set; } = string.Empty;

    /// <summary>出力先の全体。長くて省略される場合に備えて別に持つ。</summary>
    [ObservableProperty]
    public partial string OutputPathTooltip { get; set; } = string.Empty;

    /// <summary>
    /// 出力先フォルダを選ばせる。
    /// </summary>
    [RelayCommand]
    private void SelectOutputFolder()
    {
        var folder = _folderPicker.Pick();
        if (folder is not null)
        {
            Output.Folder = folder;
        }
    }

    private void UpdateOutputPathPreview()
    {
        if (string.IsNullOrWhiteSpace(Output.Folder))
        {
            OutputPathPreview = "出力先が未設定です";
            OutputPathTooltip = string.Empty;
            return;
        }

        var first = _sourcePaths.FirstOrDefault();
        if (first is null)
        {
            OutputPathPreview = "処理する画像がありません";
            OutputPathTooltip = string.Empty;
            return;
        }

        try
        {
            // 実際に使う判定と同じ経路で求めるので、連番が付く場合はその名前が出る
            var path = _exporter.ResolveOutputPath(first, Output);
            OutputPathPreview = $"{path}（{FileCount} 件を処理）";
            OutputPathTooltip = path;
        }
        catch (Exception ex)
        {
            FileErrorMessage.Log("出力先の確認", ex);
            OutputPathPreview = $"出力先を確認してください: {FileErrorMessage.Describe(ex, Output.Folder)}";
            OutputPathTooltip = string.Empty;
        }
    }
}
