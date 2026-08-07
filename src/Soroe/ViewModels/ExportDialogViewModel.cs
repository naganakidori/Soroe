using System.Collections.ObjectModel;
using System.IO;
using System.Text;
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
/// 設定の編集、実行、進捗、結果の表示までをここで受け持つ。
/// <para>
/// 設定は<b>本体と同じ <see cref="ExportSettings" /> の実体を直接編集する</b>。
/// 閉じても値を保持する仕様なので、写して確定するという仕組みが要らない。
/// 主画面のサマリ行もその場で追随する。
/// </para>
/// </remarks>
public sealed partial class ExportDialogViewModel : ObservableObject
{
    private readonly IImageExporter _exporter;
    private readonly IFolderPicker _folderPicker;
    private readonly ProcessingSettings _processing;
    private readonly IReadOnlyList<string> _sourcePaths;

    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<ExportFailure> _lastFailures = [];

    /// <summary>
    /// <see cref="ExportDialogViewModel" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="settings">編集対象の書き出し設定。実体をそのまま受け取る。</param>
    /// <param name="processing">適用する調整。実行時に写して使う。</param>
    /// <param name="sourcePaths">処理する元ファイルのパス。</param>
    /// <param name="exporter">書き出しに使う実装。</param>
    /// <param name="folderPicker">フォルダ選択ダイアログの実装。</param>
    public ExportDialogViewModel(
        ExportSettings settings,
        ProcessingSettings processing,
        IReadOnlyList<string> sourcePaths,
        IImageExporter exporter,
        IFolderPicker folderPicker)
    {
        Output = settings;
        _processing = processing;
        _sourcePaths = sourcePaths;
        _exporter = exporter;
        _folderPicker = folderPicker;

        Output.PropertyChanged += (_, _) =>
        {
            RunCommand.NotifyCanExecuteChanged();
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

    /// <summary>実行中かどうか。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectOutputFolderCommand))]
    public partial bool IsExporting { get; set; }

    /// <summary>実行前に見せる、出力先の 1 行表示。</summary>
    [ObservableProperty]
    public partial string OutputPathPreview { get; set; } = string.Empty;

    /// <summary>出力先の全体。長くて省略される場合に備えて別に持つ。</summary>
    [ObservableProperty]
    public partial string OutputPathTooltip { get; set; } = string.Empty;

    /// <summary>書き出しが終わった枚数。</summary>
    [ObservableProperty]
    public partial int ExportCompleted { get; set; }

    /// <summary>書き出す総枚数。</summary>
    [ObservableProperty]
    public partial int ExportTotal { get; set; }

    /// <summary>いま処理している元ファイルの名前。</summary>
    [ObservableProperty]
    public partial string ExportingSourceName { get; set; } = string.Empty;

    /// <summary>
    /// 書き出し先のファイル名から拡張子を除いた部分。
    /// </summary>
    /// <remarks>
    /// 拡張子を別に分けているのは、長い名前を省略しても拡張子を残すため。
    /// この表示の主目的は形式の指定が効いているかの確認なので、末尾から削ると
    /// 一番見たい部分が最初に失われる。
    /// </remarks>
    [ObservableProperty]
    public partial string ExportingOutputName { get; set; } = string.Empty;

    /// <summary>書き出し先のファイル名の拡張子。省略しない。</summary>
    [ObservableProperty]
    public partial string ExportingOutputExtension { get; set; } = string.Empty;

    /// <summary>完了後に出す結果の 1 行。実行前は空。</summary>
    [ObservableProperty]
    public partial string ResultMessage { get; set; } = string.Empty;

    /// <summary>
    /// 直前の書き出しで失敗したファイルの一覧。
    /// </summary>
    /// <remarks>1 枚の失敗で全体を止めないため、終わってからまとめて見せる。</remarks>
    public ObservableCollection<string> ExportFailures { get; } = [];

    /// <summary>実行できる状態かどうか。</summary>
    /// <remarks>
    /// 出力先が必須であるという判定はここが持つ。主画面のボタンは
    /// 「出力先を指定するためにダイアログを開く」ためのものなので、
    /// 出力先を条件にすると開けなくなる。
    /// </remarks>
    public bool CanRun => !IsExporting && !string.IsNullOrWhiteSpace(Output.Folder);

    /// <summary>
    /// 出力先フォルダを選ばせる。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
    private void SelectOutputFolder()
    {
        var folder = _folderPicker.Pick();
        if (folder is not null)
        {
            Output.Folder = folder;
        }
    }

    private bool CanChangeSettings() => !IsExporting;

    /// <summary>
    /// 書き出しを実行する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        // 処理中に値が変わっても影響しないよう、開始時点の値を写して渡す
        var exportSettings = Output.Clone();
        var processing = _processing.Clone();

        ExportFailures.Clear();
        _lastFailures = [];
        ResultMessage = string.Empty;
        ExportCompleted = 0;
        ExportTotal = _sourcePaths.Count;
        ExportingSourceName = string.Empty;
        ExportingOutputName = string.Empty;
        ExportingOutputExtension = string.Empty;
        IsExporting = true;

        // Progress<T> は生成時の SynchronizationContext を捕まえるため、
        // コールバックは UI スレッドで呼ばれる。ここで作ることに意味がある
        var progress = new Progress<ExportProgress>(p =>
        {
            ExportCompleted = p.Completed;
            ExportTotal = p.Total;
            ExportingSourceName = p.SourceFileName;

            if (p.Skipped)
            {
                // 一瞬しか出ないうえ、件数は完了時にまとめて報告する。
                // ここでは「書き出さなかった」ことだけ分かれば十分
                ExportingOutputName = "スキップ（出力先が元ファイルと同じ）";
                ExportingOutputExtension = string.Empty;
                return;
            }

            ExportingOutputName = Path.GetFileNameWithoutExtension(p.OutputFileName);
            ExportingOutputExtension = Path.GetExtension(p.OutputFileName);
        });

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        try
        {
            var result = await Task.Run(
                () => _exporter.Export(_sourcePaths, exportSettings, processing, progress, cancellation.Token))
                .ConfigureAwait(true);

            // 画面には日本語の要約だけを出す。原文は控えに残し、コピーで取り出せるようにする
            _lastFailures = result.Failures;
            foreach (var failure in result.Failures)
            {
                ExportFailures.Add($"{Path.GetFileName(failure.SourcePath)} — {failure.Message}");
            }

            // 進捗の通知は SynchronizationContext 越しに届くため、最後の 1 通が
            // ここより後になる可能性がある。最後まで進んだことを確実に見せる
            if (!result.Canceled && result.AbortReason is null)
            {
                ExportCompleted = ExportTotal;
            }

            ResultMessage = Describe(result);
        }
        catch (Exception ex)
        {
            FileErrorMessage.Log("書き出し", ex);
            ResultMessage = $"書き出しに失敗しました: {FileErrorMessage.Describe(ex, Output.Folder)}";
        }
        finally
        {
            _cancellation = null;
            IsExporting = false;
            ExportingSourceName = string.Empty;
            ExportingOutputName = string.Empty;
            ExportingOutputExtension = string.Empty;

            // 連番の付き方が変わるので、出力先の表示を作り直す
            UpdateOutputPathPreview();
        }
    }

    /// <summary>
    /// 実行中の書き出しを中止する。
    /// </summary>
    /// <remarks>
    /// 本体が触れない間、止める手段はこれだけになる。1 枚ごとに合図を確認するので、
    /// 押してから長くても 1 枚分で止まる。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsExporting))]
    private void Cancel() => _cancellation?.Cancel();

    /// <summary>
    /// 失敗の一覧を、例外の原文込みで 1 つの文字列にする。
    /// </summary>
    /// <remarks>
    /// 画面には日本語の要約しか出していないため、不具合の報告に使えるようにここで原文を添える。
    /// </remarks>
    public string BuildFailureReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Soroe 書き出しエラー  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"{_lastFailures.Count} 件");

        foreach (var failure in _lastFailures)
        {
            builder.AppendLine();
            builder.AppendLine(failure.SourcePath);
            builder.AppendLine($"  {failure.Message}");
            builder.AppendLine($"  {failure.Detail}");
        }

        return builder.ToString();
    }

    private static string Describe(ExportResult result)
    {
        if (result.AbortReason is not null)
        {
            return result.AbortReason;
        }

        var text = result.Canceled
            ? $"中止しました（{result.Exported} 件を書き出し済み）"
            : $"{result.Exported} 件を書き出しました";

        if (result.SkippedSameAsSource > 0)
        {
            text += $"。{result.SkippedSameAsSource} 件は出力先が元ファイルと同じためスキップ";
        }

        if (result.Failures.Count > 0)
        {
            text += $"。{result.Failures.Count} 件は失敗";
        }

        return text;
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
