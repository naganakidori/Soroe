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

    /// <summary>
    /// 件数の計算を始めるまでの待ち時間。
    /// </summary>
    /// <remarks>
    /// 上書き時は全件に <c>File.Exists</c> を掛けるため、入力のたびに走らせない。
    /// ネットワークドライブや USB メモリでは 1 件あたりの確認が目に見えて遅い。
    /// </remarks>
    private static readonly TimeSpan PlanDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>件数の計算を 1 度に 1 つだけに制限する。</summary>
    private readonly SemaphoreSlim _planGate = new(1, 1);

    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<ExportFailure> _lastFailures = [];

    /// <summary>件数の計算要求の番号。新しい要求が来た時点で古い要求は用済みになる。</summary>
    private int _planVersion;

    /// <summary>画面に出している見積もり。</summary>
    private ExportPlan _plan = ExportPlan.Empty;

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
            OnPropertyChanged(nameof(ShowPrefixSuffix));
            OnPropertyChanged(nameof(ShowSequence));
            OnPropertyChanged(nameof(ShowRenameNotice));
            _ = UpdatePlanAsync();
        };

        _ = UpdatePlanAsync();
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

    /// <summary>出力ファイル名の決め方の選択肢。</summary>
    public IReadOnlyList<FileNamingChoice> NamingChoices { get; } =
    [
        new(FileNaming.KeepOriginal, "そのまま"),
        new(FileNaming.PrefixSuffix, "前後に文字を足す"),
        new(FileNaming.Sequence, "連番で付け直す"),
    ];

    /// <summary>プレフィックス・サフィックスの欄を出すかどうか。</summary>
    /// <remarks>選んだ方式の欄だけを出す。効かない入力欄を並べない。</remarks>
    public bool ShowPrefixSuffix => Output.Naming is FileNaming.PrefixSuffix;

    /// <summary>連番のベース名の欄を出すかどうか。</summary>
    public bool ShowSequence => Output.Naming is FileNaming.Sequence;

    /// <summary>「同名なら別名で保存します」の案内を出すかどうか。</summary>
    /// <remarks>
    /// 上書きが選ばれているときは件数の警告がその役目を果たすので、重ねて出さない。
    /// </remarks>
    public bool ShowRenameNotice => !Output.Overwrite;

    /// <summary>上書きする件数。0 なら表示しない。</summary>
    [ObservableProperty]
    public partial int OverwriteCount { get; set; }

    /// <summary>
    /// 全件がスキップされる場合の警告。該当しなければ空。
    /// </summary>
    [ObservableProperty]
    public partial string BlockingWarning { get; set; } = string.Empty;

    /// <summary>実行を押した後に出す注意。件数が変わって実行を見送ったときに使う。</summary>
    [ObservableProperty]
    public partial string RecheckNotice { get; set; } = string.Empty;

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
    /// <remarks>
    /// 全件がスキップされる設定では実行させない。押しても 1 枚も書き出されず、
    /// 「壊れている」という印象になる。設定を直す場所はこのダイアログの中にあるので、
    /// 無効にしても手詰まりにはならない。<b>一部だけのスキップでは無効にしない</b> —
    /// 残りは正しく書き出される、正当な実行だから。
    /// </remarks>
    public bool CanRun => !IsExporting && !string.IsNullOrWhiteSpace(Output.Folder) && !_plan.IsAllSkipped;

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
        RecheckNotice = string.Empty;

        // 表示している件数が古いまま実行してはいけない。この件数は単なる情報ではなく、
        // 「うち 3 件を上書き」という最後の確認そのものだから。押した時点で確定させる
        var shown = _plan;
        var confirmed = await ConfirmPlanAsync().ConfigureAwait(true);

        // ただし見送るのは「確認が要る内容」を含むときだけ。上書きも全件スキップも
        // 無い見積もりなら、確認すべきものが無いのでそのまま進めてよい。
        //
        // 判定は必ず confirmed（最新）で行う。shown（表示していた古い方）で判定すると、
        // 上書きに切り替えた直後にすぐ実行を押した場合、古い件数が 0 のまま
        // 「確認するものが無い」と誤判定して全件を上書きしてしまう。
        // 上書きへ切り替えた直後が最も危ないタイミングであり、そこが抜ける
        if (confirmed != shown && NeedsConfirmation(confirmed))
        {
            RecheckNotice = "設定が変わったため件数を計算し直しました。内容を確認してもう一度実行してください";
            return;
        }

        if (confirmed.IsAllSkipped)
        {
            return;
        }

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

            // 書き出した結果、既存ファイルが増えて別名の付き方が変わる
            _ = UpdatePlanAsync();
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

    /// <summary>
    /// 見積もりを計算し直して表示へ反映する。
    /// </summary>
    /// <remarks>
    /// 上書き時は全件に <c>File.Exists</c> が走るため、少し待ってからバックグラウンドで行う。
    /// 走っている間に新しい要求が来たら古いほうは捨てる（プレビュー生成と同じ形）。
    /// </remarks>
    private async Task UpdatePlanAsync()
    {
        var version = Interlocked.Increment(ref _planVersion);

        // 上書きしないなら全件を調べないので、待つ意味がない
        if (Output.Overwrite)
        {
            try
            {
                await Task.Delay(PlanDelay).ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (Volatile.Read(ref _planVersion) != version)
            {
                return;
            }
        }

        await _planGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (Volatile.Read(ref _planVersion) != version)
            {
                return;
            }

            var settings = Output.Clone();
            var plan = await Task.Run(() => Calculate(settings)).ConfigureAwait(true);

            if (Volatile.Read(ref _planVersion) != version)
            {
                return;
            }

            Apply(plan);
        }
        finally
        {
            _planGate.Release();
        }
    }

    /// <summary>
    /// その見積もりに、実行前の確認が要る内容が含まれるか。
    /// </summary>
    private static bool NeedsConfirmation(ExportPlan plan) => plan.OverwriteCount > 0 || plan.IsAllSkipped;

    /// <summary>
    /// 実行の直前に見積もりを確定させる。
    /// </summary>
    /// <remarks>遅延や計算の途中でも、待って最新の値を得る。</remarks>
    private async Task<ExportPlan> ConfirmPlanAsync()
    {
        var version = Interlocked.Increment(ref _planVersion);
        await _planGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var settings = Output.Clone();
            var plan = await Task.Run(() => Calculate(settings)).ConfigureAwait(true);
            if (Volatile.Read(ref _planVersion) == version)
            {
                Apply(plan);
            }

            return plan;
        }
        finally
        {
            _planGate.Release();
        }
    }

    /// <summary>
    /// 見積もりを求める。失敗した場合は理由を表示に載せる。
    /// </summary>
    private ExportPlan Calculate(ExportSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Folder) || _sourcePaths.Count == 0)
        {
            return ExportPlan.Empty;
        }

        return _exporter.Plan(_sourcePaths, settings);
    }

    private void Apply(ExportPlan plan)
    {
        _plan = plan;
        RunCommand.NotifyCanExecuteChanged();
        OverwriteCount = plan.OverwriteCount;

        if (string.IsNullOrWhiteSpace(Output.Folder))
        {
            OutputPathPreview = "出力先が未設定です";
            OutputPathTooltip = string.Empty;
            BlockingWarning = string.Empty;
            return;
        }

        if (_sourcePaths.Count == 0)
        {
            OutputPathPreview = "処理する画像がありません";
            OutputPathTooltip = string.Empty;
            BlockingWarning = string.Empty;
            return;
        }

        OutputPathPreview = $"{plan.FirstOutputPath}（{plan.Total} 件を処理）";
        OutputPathTooltip = plan.FirstOutputPath;

        BlockingWarning = plan.IsAllSkipped
            ? $"この設定では {plan.Total} 件すべてがスキップされます。"
                + "出力先が元のファイルと同じになるためです。出力先かファイル名を変えてください"
            : string.Empty;
    }
}
