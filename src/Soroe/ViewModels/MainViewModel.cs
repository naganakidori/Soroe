using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soroe.Common;
using Soroe.Models;
using Soroe.Services;

namespace Soroe.ViewModels;

/// <summary>
/// メインウィンドウの ViewModel。
/// </summary>
/// <remarks>
/// この層は OpenCvSharp を参照しない。画像の読み込みと加工は <see cref="IImageRenderer" /> 越しに行う。
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// プレビュー用に読み込むときの長辺の上限。
    /// </summary>
    /// <remarks>
    /// スライダーを動かすたびに全画素を処理するため、原寸のままでは追従できない。
    /// 表示に足りる程度まで縮めてから調整をかけ、書き出し時にフル解像度で計算し直す。
    /// </remarks>
    private const int PreviewMaxEdge = 1600;

    /// <summary>取り扱う拡張子。これ以外のファイルは追加時に無視する。</summary>
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    private readonly IImageRenderer _renderer;
    private readonly IImageExporter _exporter;
    private readonly IFolderPicker _folderPicker;

    private CancellationTokenSource? _exportCancellation;

    /// <summary>
    /// 追加済みの絶対パス。同じフォルダを 2 回追加しても二重処理しないために持つ。
    /// </summary>
    private readonly HashSet<string> _addedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// プレビューの更新を 1 度に 1 つだけに制限する。
    /// </summary>
    /// <remarks>
    /// <see cref="_previewSource" /> の差し替えと破棄もこの中で行うため、
    /// レンダリング中に元画像が破棄される事故が起きない。
    /// </remarks>
    private readonly SemaphoreSlim _previewGate = new(1, 1);

    /// <summary>
    /// プレビュー更新の要求番号。新しい要求が来た時点で古い要求は用済みになる。
    /// </summary>
    private int _previewVersion;

    private RenderSource? _previewSource;

    /// <summary>
    /// <see cref="MainViewModel" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="renderer">画像の読み込みと加工に使う実装。</param>
    /// <param name="exporter">書き出しに使う実装。</param>
    /// <param name="folderPicker">フォルダ選択ダイアログの実装。</param>
    public MainViewModel(IImageRenderer renderer, IImageExporter exporter, IFolderPicker folderPicker)
    {
        _renderer = renderer;
        _exporter = exporter;
        _folderPicker = folderPicker;

        // 件数が変わるとボタンの有効・無効と出力先の表示が変わる
        Files.CollectionChanged += (_, _) =>
        {
            ClearCommand.NotifyCanExecuteChanged();
            RunExportCommand.NotifyCanExecuteChanged();
            UpdateOutputPreview();
        };

        // 調整値が変わったらプレビューを作り直す。元画像は読み直さない
        Settings.Changed += (_, _) => _ = UpdatePreviewAsync(reloadSource: false);

        Output.PropertyChanged += (_, _) =>
        {
            RunExportCommand.NotifyCanExecuteChanged();
            UpdateOutputPreview();
        };

        UpdateOutputPreview();
    }

    /// <summary>
    /// 処理対象のファイル。
    /// </summary>
    /// <remarks>
    /// 連番リネームの順序はこの表示順に固定する。追加したバッチごとに自然順で
    /// 並べ替えて末尾に足すため、以降は順序が勝手に変わらない。
    /// </remarks>
    public ObservableCollection<ImageItem> Files { get; } = [];

    /// <summary>
    /// 全件に適用する調整の設定。
    /// </summary>
    /// <remarks>
    /// プリセットの読み込みを実装する際は、このインスタンスを差し替えるのではなく
    /// 各項目に値を書き込むこと。差し替えるとバインドと <see cref="ProcessingSettings.Changed" />
    /// の購読が切れる。
    /// </remarks>
    public ProcessingSettings Settings { get; } = new();

    /// <summary>リストで選択中の 1 件。プレビューの対象になる。</summary>
    [ObservableProperty]
    public partial ImageItem? SelectedFile { get; set; }

    /// <summary>プレビューに表示している画像。未選択の場合は <see langword="null" />。</summary>
    [ObservableProperty]
    public partial BitmapSource? PreviewImage { get; set; }

    /// <summary>画面下部に出す状態表示。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "画像ファイルまたはフォルダをドロップしてください";

    /// <summary>書き出しの設定。</summary>
    public ExportSettings Output { get; } = new();

    /// <summary>実行ボタンの上に出す、出力先の 1 行表示。</summary>
    [ObservableProperty]
    public partial string OutputPathPreview { get; set; } = string.Empty;

    /// <summary>書き出しの実行中かどうか。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPathsCommand))]
    public partial bool IsExporting { get; set; }

    /// <summary>書き出しが終わった枚数。</summary>
    [ObservableProperty]
    public partial int ExportCompleted { get; set; }

    /// <summary>書き出す総枚数。</summary>
    [ObservableProperty]
    public partial int ExportTotal { get; set; }

    /// <summary>いま書き出しているファイル名。</summary>
    [ObservableProperty]
    public partial string ExportingFileName { get; set; } = string.Empty;

    /// <summary>
    /// 直前の書き出しで失敗したファイルの一覧。
    /// </summary>
    /// <remarks>1 枚の失敗で全体を止めないため、終わってからまとめて見せる。</remarks>
    public ObservableCollection<string> ExportFailures { get; } = [];

    /// <summary>
    /// ドロップされたファイル・フォルダをリストに追加する。
    /// </summary>
    /// <param name="paths">ドロップされたパス。</param>
    [RelayCommand(CanExecute = nameof(CanEditList))]
    private void AddPaths(IReadOnlyList<string>? paths)
    {
        if (paths is null)
        {
            return;
        }

        Add(paths.SelectMany(EnumerateFiles));
    }

    /// <summary>
    /// フォルダを選ばせ、その直下の画像をリストに追加する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditList))]
    private void AddFolder()
    {
        var folder = _folderPicker.Pick();
        if (folder is null)
        {
            return;
        }

        Add(EnumerateFiles(folder));
    }

    /// <summary>
    /// リストを空にする。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Files.Clear();
        _addedPaths.Clear();

        // 選択を外すと、プレビューの更新経路で元画像も破棄される
        SelectedFile = null;
        StatusMessage = "画像ファイルまたはフォルダをドロップしてください";
    }

    private bool CanClear() => Files.Count > 0 && !IsExporting;

    /// <summary>書き出し中はリストを触らせない。処理対象が途中で変わると結果が読めなくなるため。</summary>
    private bool CanEditList() => !IsExporting;

    /// <summary>
    /// 出力先フォルダを選ばせる。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditList))]
    private void SelectOutputFolder()
    {
        var folder = _folderPicker.Pick();
        if (folder is not null)
        {
            Output.Folder = folder;
        }
    }

    /// <summary>
    /// リストの全件を書き出す。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunExport))]
    private async Task RunExportAsync()
    {
        // 処理中に画面側が変わっても影響しないよう、開始時点の値を写して渡す
        var paths = Files.Select(f => f.FullPath).ToArray();
        var exportSettings = new ExportSettings { Folder = Output.Folder };
        var processing = Settings.Clone();

        ExportFailures.Clear();
        ExportCompleted = 0;
        ExportTotal = paths.Length;
        ExportingFileName = string.Empty;
        IsExporting = true;

        // Progress<T> は生成時の SynchronizationContext を捕まえるため、
        // コールバックは UI スレッドで呼ばれる。ここで作ることに意味がある
        var progress = new Progress<ExportProgress>(p =>
        {
            ExportCompleted = p.Completed;
            ExportTotal = p.Total;
            ExportingFileName = p.CurrentFileName;
        });

        using var cancellation = new CancellationTokenSource();
        _exportCancellation = cancellation;

        try
        {
            var result = await Task.Run(
                () => _exporter.Export(paths, exportSettings, processing, progress, cancellation.Token))
                .ConfigureAwait(true);

            foreach (var failure in result.Failures)
            {
                ExportFailures.Add($"{Path.GetFileName(failure.SourcePath)} — {failure.Message}");
            }

            StatusMessage = Describe(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"書き出しに失敗しました（{ex.Message}）";
        }
        finally
        {
            _exportCancellation = null;
            IsExporting = false;
            ExportingFileName = string.Empty;
        }
    }

    private bool CanRunExport()
        => !IsExporting && Files.Count > 0 && !string.IsNullOrWhiteSpace(Output.Folder);

    /// <summary>
    /// 実行中の書き出しを中止する。
    /// </summary>
    [RelayCommand]
    private void CancelExport() => _exportCancellation?.Cancel();

    /// <summary>
    /// 結果を 1 行の文にする。
    /// </summary>
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
    /// 実行ボタンの上に出す 1 行を作り直す。
    /// </summary>
    private void UpdateOutputPreview()
    {
        if (string.IsNullOrWhiteSpace(Output.Folder))
        {
            OutputPathPreview = "出力先を選んでください";
            return;
        }

        var first = Files.FirstOrDefault();
        if (first is null)
        {
            OutputPathPreview = "処理する画像がありません";
            return;
        }

        try
        {
            // 実際に使う判定と同じ経路で求めるので、連番が付く場合はその名前が出る
            var path = _exporter.ResolveOutputPath(first.FullPath, Output);
            OutputPathPreview = $"{path}（{Files.Count} 件を処理）";
        }
        catch (Exception ex)
        {
            OutputPathPreview = $"出力先を確認してください（{ex.Message}）";
        }
    }

    /// <summary>
    /// 対象拡張子のものだけを、重複を除いて追加する。
    /// </summary>
    private void Add(IEnumerable<string> candidates)
    {
        var added = new List<string>();
        var duplicated = 0;

        foreach (var path in candidates)
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            // 相対表記や大文字小文字の違いで同じファイルを二重に持たないよう、絶対パスで判定する
            var fullPath = Path.GetFullPath(path);
            if (!_addedPaths.Add(fullPath))
            {
                duplicated++;
                continue;
            }

            added.Add(fullPath);
        }

        // Directory.EnumerateFiles の順序も、ドロップ時に OS が渡す順序も保証されない。
        // 連番リネームは表示順に従うため、ここで並びを確定させる。
        //
        // List<T>.Sort ではなく OrderBy を使うこと。NaturalPathComparer は推移性を
        // 保証できず、List<T>.Sort / Array.Sort の整合性チェックに引っかかると
        // InvalidOperationException で落ちる。詳細は NaturalPathComparer の注記を参照。
        //
        // 並べ替えるのは追加分だけ。リスト全体を並べ替え直すと、追加したものが
        // 途中に紛れて追加できたのかどうかが分からなくなる
        foreach (var path in added.OrderBy(p => p, NaturalPathComparer.Instance))
        {
            Files.Add(new ImageItem(path));
        }

        // 1 枚目を自動で選ぶ。追加した直後に何も表示されないと、追加できたかが分からないため
        SelectedFile ??= Files.FirstOrDefault();

        StatusMessage = (added.Count, duplicated) switch
        {
            (0, 0) => "対応するファイルがありませんでした（.jpg .jpeg .png .bmp .webp）",
            (0, _) => $"すべて追加済みでした（合計 {Files.Count} 件）",
            (_, 0) => $"{added.Count} 件を追加しました（合計 {Files.Count} 件）",
            _ => $"{added.Count} 件を追加しました。{duplicated} 件は追加済みのため除外（合計 {Files.Count} 件）",
        };
    }

    /// <summary>
    /// パスがフォルダならその直下のファイルを、ファイルならそれ自身を返す。
    /// </summary>
    /// <remarks>
    /// サブフォルダは辿らない。辿ると出力時にフォルダ構造を再現するかどうかの判断が発生するため。
    /// </remarks>
    private static IReadOnlyList<string> EnumerateFiles(string path)
    {
        if (File.Exists(path))
        {
            return [path];
        }

        if (!Directory.Exists(path))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(path).ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    partial void OnSelectedFileChanged(ImageItem? value)
    {
        // プロパティの変更通知は同期的に返し、読み込みは待たせない
        _ = UpdatePreviewAsync(reloadSource: true);
    }

    /// <summary>
    /// プレビューを作り直す。
    /// </summary>
    /// <param name="reloadSource">
    /// 選択が変わった場合は <see langword="true" />。調整値だけが変わった場合は
    /// <see langword="false" /> を渡し、読み込み済みの元画像を使い回す。
    /// </param>
    /// <remarks>
    /// スライダーを動かしている間は要求が連続して届く。処理中に届いた要求は待たせ、
    /// 待っている間にさらに新しい要求が来たら古いほうは何もせず抜ける（最新のものだけが残る）。
    /// </remarks>
    private async Task UpdatePreviewAsync(bool reloadSource)
    {
        var version = Interlocked.Increment(ref _previewVersion);

        await _previewGate.WaitAsync().ConfigureAwait(true);
        try
        {
            // 待っている間に新しい要求が来ていた。この要求は捨てる
            if (Volatile.Read(ref _previewVersion) != version)
            {
                return;
            }

            var item = SelectedFile;

            if (reloadSource)
            {
                _previewSource?.Dispose();
                _previewSource = null;

                if (item is not null)
                {
                    _previewSource = await Task.Run(() => _renderer.Load(item.FullPath, PreviewMaxEdge))
                        .ConfigureAwait(true);

                    if (_previewSource is null)
                    {
                        PreviewImage = null;
                        StatusMessage = $"読み込めませんでした: {item.FileName}";
                        return;
                    }
                }
            }

            var source = _previewSource;
            if (source is null)
            {
                PreviewImage = null;
                return;
            }

            // バックグラウンドで処理している間に UI 側の値が変わっても影響しないよう、
            // 開始時点の値を写してから渡す
            var snapshot = Settings.Clone();
            var bitmap = await Task.Run(() => _renderer.Render(source, snapshot, source.Scale))
                .ConfigureAwait(true);

            // 処理中に選択や調整値が変わっていたら、古い結果で上書きしない
            if (Volatile.Read(ref _previewVersion) != version)
            {
                return;
            }

            PreviewImage = bitmap;

            if (item is not null)
            {
                StatusMessage = $"{item.FileName}（{source.OriginalWidth} × {source.OriginalHeight}）";
            }
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _previewVersion) == version)
            {
                PreviewImage = null;
                StatusMessage = $"プレビューの生成に失敗しました（{ex.Message}）";
            }
        }
        finally
        {
            _previewGate.Release();
        }
    }
}
