using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soroe.Models;
using Soroe.Services;

namespace Soroe.ViewModels;

/// <summary>
/// メインウィンドウの ViewModel。
/// </summary>
/// <remarks>
/// この層は OpenCvSharp を参照しない。画像の読み込みは <see cref="IImageLoader" /> 越しに行う。
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>取り扱う拡張子。これ以外のファイルは追加時に無視する。</summary>
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    private readonly IImageLoader _imageLoader;
    private readonly IFolderPicker _folderPicker;

    /// <summary>
    /// 追加済みの絶対パス。同じフォルダを 2 回追加しても二重処理しないために持つ。
    /// </summary>
    private readonly HashSet<string> _addedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <see cref="MainViewModel" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="imageLoader">画像の読み込みに使う実装。</param>
    /// <param name="folderPicker">フォルダ選択ダイアログの実装。</param>
    public MainViewModel(IImageLoader imageLoader, IFolderPicker folderPicker)
    {
        _imageLoader = imageLoader;
        _folderPicker = folderPicker;

        // 件数が変わるとクリアボタンの有効・無効も変わる
        Files.CollectionChanged += (_, _) => ClearCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 処理対象のファイル。
    /// </summary>
    /// <remarks>
    /// 連番リネームの順序はこの表示順に固定するため、追加した順を保つ。
    /// </remarks>
    public ObservableCollection<ImageItem> Files { get; } = [];

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
        SelectedFile = null;
        StatusMessage = "画像ファイルまたはフォルダをドロップしてください";
    }

    private bool CanClear() => Files.Count > 0;

    /// <summary>
    /// 対象拡張子のものだけを、重複を除いて追加する。
    /// </summary>
    private void Add(IEnumerable<string> candidates)
    {
        var added = 0;
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

            Files.Add(new ImageItem(fullPath));
            added++;
        }

        // 1 枚目を自動で選ぶ。追加した直後に何も表示されないと、追加できたかが分からないため
        SelectedFile ??= Files.FirstOrDefault();

        StatusMessage = (added, duplicated) switch
        {
            (0, 0) => "対応するファイルがありませんでした（.jpg .jpeg .png .bmp .webp）",
            (0, _) => $"すべて追加済みでした（合計 {Files.Count} 件）",
            (_, 0) => $"{added} 件を追加しました（合計 {Files.Count} 件）",
            _ => $"{added} 件を追加しました。{duplicated} 件は追加済みのため除外（合計 {Files.Count} 件）",
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
        _ = UpdatePreviewAsync(value);
    }

    /// <summary>
    /// 選択中の 1 枚をプレビューに読み込む。
    /// </summary>
    private async Task UpdatePreviewAsync(ImageItem? item)
    {
        if (item is null)
        {
            PreviewImage = null;
            return;
        }

        try
        {
            // フル解像度の展開は数百ミリ秒かかることがあるので UI スレッドから外す。
            // ImageLoader は Freeze 済みの BitmapSource を返すため、そのまま UI に渡せる。
            var image = await Task.Run(() => _imageLoader.Load(item.FullPath));

            // 読み込み中に選択が変わっていたら、古い画像で上書きしない
            if (!ReferenceEquals(item, SelectedFile))
            {
                return;
            }

            if (image is null)
            {
                PreviewImage = null;
                StatusMessage = $"読み込めませんでした: {item.FileName}";
                return;
            }

            PreviewImage = image;
            StatusMessage = $"{item.FileName}（{image.PixelWidth} × {image.PixelHeight}）";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(item, SelectedFile))
            {
                PreviewImage = null;
                StatusMessage = $"読み込みに失敗しました: {item.FileName}（{ex.Message}）";
            }
        }
    }
}
