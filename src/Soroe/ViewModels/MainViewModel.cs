using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    /// <summary>取り扱う拡張子。これ以外のファイルがドロップされても無視する。</summary>
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    private readonly IImageLoader _imageLoader;

    /// <summary>
    /// <see cref="MainViewModel" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="imageLoader">画像の読み込みに使う実装。</param>
    public MainViewModel(IImageLoader imageLoader)
    {
        _imageLoader = imageLoader;
    }

    /// <summary>プレビューに表示している画像。未読み込みの場合は <see langword="null" />。</summary>
    [ObservableProperty]
    public partial BitmapSource? PreviewImage { get; set; }

    /// <summary>画面下部に出す状態表示。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "画像ファイルをドロップしてください";

    /// <summary>
    /// ドロップされたファイルを受け取り、最初の 1 枚をプレビューに表示する。
    /// </summary>
    /// <param name="paths">ドロップされたファイルまたはフォルダのパス。</param>
    [RelayCommand]
    private void AddFiles(IReadOnlyList<string>? paths)
    {
        if (paths is null)
        {
            return;
        }

        // 骨組みの段階なので複数ドロップされても先頭 1 枚だけを扱う。
        // ファイルリスト化は次の段階で ObservableCollection<ImageItem> として実装する。
        var target = paths.FirstOrDefault(IsSupportedImage);
        if (target is null)
        {
            StatusMessage = "対応していないファイルです（.jpg .jpeg .png .bmp .webp）";
            return;
        }

        var image = _imageLoader.Load(target);
        if (image is null)
        {
            StatusMessage = $"読み込めませんでした: {Path.GetFileName(target)}";
            return;
        }

        PreviewImage = image;
        StatusMessage = $"{Path.GetFileName(target)}（{image.PixelWidth} × {image.PixelHeight}）";
    }

    private static bool IsSupportedImage(string path)
        => File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path));
}
