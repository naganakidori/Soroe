using System.Collections.ObjectModel;
using System.IO;
using System.Text;
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
    /// <para>
    /// <b>値は <see cref="ImageRenderer.CanonicalEdge" /> をそのまま使う。ここに数字を
    /// 書かないこと。</b>二値化のしきい値は同じ寸法に縮小した画像から決まるので、
    /// 2 か所に別々の数字があると、片方だけ変えたときにプレビューと書き出しで
    /// 違う絵が出る。窓の大きさや DPI に依存させてもいけない。
    /// </para>
    /// </remarks>
    private const int PreviewMaxEdge = ImageRenderer.CanonicalEdge;

    /// <summary>取り扱う拡張子。これ以外のファイルは追加時に無視する。</summary>
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    private readonly IImageRenderer _renderer;
    private readonly IImageExporter _exporter;
    private readonly IFolderPicker _folderPicker;
    private readonly IExportDialog _exportDialog;

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
    /// <param name="settings">全件に適用する調整の設定。復元したものを渡す。</param>
    /// <param name="output">書き出しの設定。復元したものを渡す。</param>
    /// <param name="renderer">画像の読み込みと加工に使う実装。</param>
    /// <param name="exporter">書き出しに使う実装。</param>
    /// <param name="folderPicker">フォルダ選択ダイアログの実装。</param>
    /// <param name="exportDialog">書き出しの設定・確認ダイアログの実装。</param>
    /// <remarks>
    /// 設定を外から受け取るのは、復元した値を後から書き写す経路を作らないため。
    /// 写す実装にすると、項目を足したときに写し漏れる（<c>Clone</c> と同じ落とし穴）。
    /// </remarks>
    public MainViewModel(
        ProcessingSettings settings,
        ExportSettings output,
        IImageRenderer renderer,
        IImageExporter exporter,
        IFolderPicker folderPicker,
        IExportDialog exportDialog)
    {
        Settings = settings;
        Output = output;
        _renderer = renderer;
        _exporter = exporter;
        _folderPicker = folderPicker;
        _exportDialog = exportDialog;

        // 件数が変わるとボタンの有効・無効と出力先の表示が変わる
        Files.CollectionChanged += (_, _) =>
        {
            ClearCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            UpdateOutputPreview();
        };

        // 調整値が変わったらプレビューを作り直す。元画像は読み直さない
        Settings.Changed += (_, _) =>
        {
            UpdatePreviewSizeText();
            _ = UpdatePreviewAsync(reloadSource: false);
        };

        SyncResizePresetFromSettings();

        // ダイアログ側で設定が変わったときも、サマリ行を追随させる
        Output.PropertyChanged += (_, _) => UpdateOutputPreview();

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
    /// 名前付きプリセットの読み込みを実装する際は、このインスタンスを差し替えるのではなく
    /// 各項目に値を書き込むこと。差し替えるとバインドと <see cref="ProcessingSettings.Changed" />
    /// の購読が切れる。
    /// </remarks>
    public ProcessingSettings Settings { get; }

    /// <summary>リストで選択中の 1 件。プレビューの対象になる。</summary>
    [ObservableProperty]
    public partial ImageItem? SelectedFile { get; set; }

    /// <summary>プレビューに表示している画像。未選択の場合は <see langword="null" />。</summary>
    [ObservableProperty]
    public partial BitmapSource? PreviewImage { get; set; }

    /// <summary>画面下部に出す状態表示。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "画像ファイルまたはフォルダをドロップしてください";

    /// <summary>
    /// 選択中の 1 枚の寸法。リサイズで変わる場合は変化後も並べて出す。
    /// </summary>
    /// <remarks>
    /// プレビューは枠に合わせて表示されるため、長辺 1920 でも 800 でも見た目がほとんど
    /// 変わらない。リサイズだけは視覚的な手応えが得られない項目なので、数値で示す。
    /// </remarks>
    [ObservableProperty]
    public partial string PreviewSizeText { get; set; } = string.Empty;

    /// <summary>リサイズのドロップダウンに並べる候補。</summary>
    public IReadOnlyList<ResizePreset> ResizePresets { get; } =
    [
        new(1920, "1920（フルHD・ブログ向け）"),
        new(1280, "1280（メール添付向け）"),
        new(800, "800（SNS向け）"),
        new(null, "自由入力"),
    ];

    /// <summary>ドロップダウンで選ばれている候補。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResizeFreeInput))]
    public partial ResizePreset? SelectedResizePreset { get; set; }

    /// <summary>自由入力が選ばれているかどうか。数値入力欄の表示切り替えに使う。</summary>
    /// <remarks>
    /// 「候補を選んだ状態」か「自由入力でたまたま同じ値を打った状態」かは設定値からは
    /// 区別できないため、ここだけは ViewModel が持つ。ただしこれは入力方法の選択であって
    /// 設定値ではないので、プリセットの保存対象にはしない。
    /// </remarks>
    public bool IsResizeFreeInput => SelectedResizePreset is { LongestEdge: null };

    /// <summary>回転のドロップダウンに並べる候補。</summary>
    /// <remarks>
    /// 「回転しない」は入れない。チェックボックスがその役目を持っているので、
    /// 入れると「回転しない」の言い方が 2 つできる。
    /// <para>
    /// 270 ではなく「左に 90°」と書く。時計回りの度数は内部の表現であって、
    /// 画面で読むものではない。
    /// </para>
    /// </remarks>
    public IReadOnlyList<RotationChoice> RotationChoices { get; } =
    [
        new(RotationAngle.Clockwise90, "右に 90°"),
        new(RotationAngle.Half, "180°"),
        new(RotationAngle.CounterClockwise90, "左に 90°"),
    ];

    /// <summary>枠線の色のドロップダウンに並べる候補。</summary>
    public IReadOnlyList<FrameColorChoice> FrameColorChoices { get; } =
    [
        new(FrameColor.White, "白"),
        new(FrameColor.Black, "黒"),
        new(FrameColor.Gray, "グレー"),
    ];

    /// <summary>
    /// 書き出しの設定。
    /// </summary>
    /// <remarks>
    /// 設定の編集はダイアログが担う。ダイアログはこの実体をそのまま書き換えるので、
    /// 閉じた後も値が残り、サマリ行も追随する。
    /// </remarks>
    public ExportSettings Output { get; }

    /// <summary>書き出しボタンの上に出す、出力先の 1 行表示。</summary>
    [ObservableProperty]
    public partial string OutputPathPreview { get; set; } = string.Empty;

    /// <summary>出力先の全体。長くて省略される場合に備えて別に持つ。</summary>
    [ObservableProperty]
    public partial string OutputPathTooltip { get; set; } = string.Empty;

    /// <summary>書き出しの実行中かどうか。</summary>

    /// <summary>
    /// ドロップされたファイル・フォルダをリストに追加する。
    /// </summary>
    /// <param name="paths">ドロップされたパス。</param>
    [RelayCommand]
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
    [RelayCommand]
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

    private bool CanClear() => Files.Count > 0;

    /// <summary>
    /// 書き出しダイアログを開く。
    /// </summary>
    /// <remarks>
    /// ボタンの文言は「書き出し...」。押しても即座には書き出さず、まずダイアログが開く。
    /// 三点リーダーがその意味を表す。
    /// <para>
    /// 設定・実行・進捗・結果はすべてダイアログの中で完結する。モーダルなので、
    /// 書き出し中にこの画面を触れないことが構造的に保証される。
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        // 設定はダイアログが直接書き換える。閉じても値は残る
        var dialog = new ExportDialogViewModel(
            Output, Settings, Files.Select(f => f.FullPath).ToArray(), _exporter, _folderPicker);

        _exportDialog.Show(dialog);

        // 何が起きたかの記録を 1 行だけ残す。結果そのものはダイアログで見せている
        if (dialog.ResultMessage.Length > 0)
        {
            StatusMessage = dialog.ResultMessage;
        }

        // 書き出した結果、同名ファイルが増えて連番の付き方が変わることがある
        UpdateOutputPreview();
    }

    /// <summary>
    /// 書き出しダイアログを開ける条件。
    /// </summary>
    /// <remarks>
    /// <b>出力先は条件に入れない。</b>出力先を設定する場所がダイアログの中なので、
    /// 出力先を条件にすると、設定するためのダイアログを開けなくなる。
    /// 出力先が必須であるという判定はダイアログ側の実行ボタンが持つ。
    /// </remarks>
    private bool CanExport() => Files.Count > 0;

    partial void OnSelectedResizePresetChanged(ResizePreset? value)
    {
        // 自由入力へ切り替えたときは値を変えない。直前に選んでいた候補の値を
        // そのまま引き継ぎ、そこから微調整できるようにする
        if (value?.LongestEdge is int longestEdge)
        {
            Settings.Resize.LongestEdge = longestEdge;
        }
    }

    /// <summary>
    /// 設定値からドロップダウンの選択を導き直す。
    /// </summary>
    /// <remarks>
    /// 選択モードは設定として保存しないため、復元時はここで導出する。
    /// 復元された値が候補のいずれかと一致すればその候補を選び、
    /// 一致しなければ自由入力として扱う。
    /// <b>プリセットを読み込んだ後は必ず呼ぶこと。</b>呼ばないと
    /// 「値は 1000 なのにドロップダウンは 1920」といった食い違いが起きる。
    /// </remarks>
    public void SyncResizePresetFromSettings()
    {
        SelectedResizePreset =
            ResizePresets.FirstOrDefault(p => p.LongestEdge == Settings.Resize.LongestEdge)
            ?? ResizePresets.First(p => p.LongestEdge is null);
    }

    /// <summary>
    /// 選択中の 1 枚について、元の寸法と出力寸法の表示を作り直す。
    /// </summary>
    /// <remarks>
    /// 寸法は<b>回転を通した後</b>で求める。回転は適用順序 1 番目なので、90 度回すと
    /// 縦横が入れ替わり、回転前の寸法にリサイズを掛けた値は実際の出力と食い違う。
    /// <para>
    /// 枠線が有効な間は太さもここに出す。<b>上限に当たったときだけ出す形にはしない。</b>
    /// 常に出ていれば「16px と指定して 16px と表示される」状態が基準になり、そこから
    /// 変わったときに気づける。上限のときだけ出す形だと、出ていない状態が
    /// 「指定どおり」なのか「表示する条件を満たしていない」のか区別できない。
    /// </para>
    /// </remarks>
    private void UpdatePreviewSizeText()
    {
        var source = _previewSource;
        if (source is null)
        {
            PreviewSizeText = string.Empty;
            return;
        }

        var (width, height) = Settings.ResolveOutputSize(source.OriginalWidth, source.OriginalHeight);
        var original = $"{source.OriginalWidth} × {source.OriginalHeight}";

        var text = width == source.OriginalWidth && height == source.OriginalHeight
            ? original
            : $"{original}  →  {width} × {height}";

        if (Settings.Frame.Enabled)
        {
            var thickness = Settings.Frame.ResolveThickness(width, height);
            text += thickness < Settings.Frame.Thickness
                ? $"　枠線 {thickness}px（上限）"
                : $"　枠線 {thickness}px";
        }

        PreviewSizeText = text;
    }

    /// <summary>
    /// 実行ボタンの上に出す 1 行を作り直す。
    /// </summary>
    private void UpdateOutputPreview()
    {
        // 実行を妨げているものから順に案内する。
        // 画像が無ければボタン自体が無効なので、まずそちらを促す
        var first = Files.FirstOrDefault();
        if (first is null)
        {
            OutputPathPreview = "画像を追加してください";
            OutputPathTooltip = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(Output.Folder))
        {
            // 主画面に出力先を選ぶ手段は無い。ボタンは押せる状態なので詰まらない
            OutputPathPreview = "出力先が未設定です";
            OutputPathTooltip = string.Empty;
            return;
        }

        try
        {
            // 実際に使う判定と同じ経路で求めるので、別名が付く場合はその名前が出る。
            // 主画面では 1 件目だけ分かればよいので、件数の内訳までは求めない
            var plan = _exporter.Plan([first.FullPath], Output);
            OutputPathPreview = $"{plan.FirstOutputPath}（{Files.Count} 件を処理）";
            OutputPathTooltip = plan.FirstOutputPath;
        }
        catch (Exception ex)
        {
            FileErrorMessage.Log("出力先の確認", ex);
            OutputPathPreview = $"出力先を確認してください: {FileErrorMessage.Describe(ex, Output.Folder)}";
            OutputPathTooltip = string.Empty;
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
                PreviewSizeText = string.Empty;
                return;
            }

            UpdatePreviewSizeText();

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
                // 寸法は PreviewSizeText が受け持つので、ここではファイル名だけにする
                StatusMessage = item.FileName;
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
