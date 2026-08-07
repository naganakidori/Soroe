using System.IO;
using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// <see cref="IImageExporter" /> の実装。
/// </summary>
/// <remarks>
/// 並列化はしない。進捗・中止・エラー集約が複雑になるうえ、同名ファイルの連番付けが
/// 既存ファイルの有無に依存しているため、順次処理であることが正しさの前提になっている。
/// UI スレッドを塞がなければ体感上は十分である。
/// </remarks>
public sealed class ImageExporter : IImageExporter
{
    /// <summary>
    /// 書き込み途中のファイルに付ける拡張子。
    /// </summary>
    private const string TempExtension = ".soroe-tmp";

    private readonly IImageRenderer _renderer;
    private readonly OutputPathResolver _resolver = new();

    /// <summary>
    /// <see cref="ImageExporter" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="renderer">画像の読み込みとエンコードに使う実装。</param>
    public ImageExporter(IImageRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <inheritdoc />
    public string ResolveOutputPath(string sourcePath, ExportSettings settings)
        => _resolver.Resolve(sourcePath, settings);

    /// <inheritdoc />
    public ExportResult Export(
        IReadOnlyList<string> sourcePaths,
        ExportSettings settings,
        ProcessingSettings processing,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 書き込めないフォルダを選んでいた場合、1 件ずつ失敗させると
        // 全件分のエラーが並ぶだけになる。始める前に 1 度だけ確かめる
        var abortReason = CheckWritable(settings.Folder);
        if (abortReason is not null)
        {
            return ExportResult.Abort(abortReason);
        }

        var failures = new List<ExportFailure>();
        var exported = 0;
        var skipped = 0;
        var canceled = false;
        var processed = 0;

        for (var i = 0; i < sourcePaths.Count; i++)
        {
            // 1 枚のエンコードは長くても数百 ms なので、その途中で割り込む必要はない
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            var sourcePath = sourcePaths[i];

            try
            {
                // 進捗に出力名も載せるため、先に出力先を確定させる。
                // 同名衝突の連番までここで決まるので、報告するのは最終的な名前になる
                var outputPath = _resolver.Resolve(sourcePath, settings);

                // 安全ガード。出力先が元ファイルそのものなら、無条件で飛ばす。
                // 呼び出し方に依存せず必ず通るよう、書き込みの直前のここで判定する
                var skipping = IsSamePath(sourcePath, outputPath);

                progress?.Report(new ExportProgress(
                    i,
                    sourcePaths.Count,
                    Path.GetFileName(sourcePath),
                    skipping ? string.Empty : Path.GetFileName(outputPath),
                    skipping));

                if (skipping)
                {
                    skipped++;
                    continue;
                }

                ExportOne(sourcePath, outputPath, processing, settings);
                exported++;
            }
            catch (Exception ex)
            {
                // 1 枚の失敗で全体を止めない。集めて最後にまとめて報告する
                failures.Add(new ExportFailure(sourcePath, ex.Message));
            }
            finally
            {
                processed = i + 1;
            }
        }

        progress?.Report(new ExportProgress(processed, sourcePaths.Count, string.Empty, string.Empty, false));
        return ExportResult.Completed(exported, skipped, failures, canceled);
    }

    /// <summary>
    /// 1 枚を書き出す。
    /// </summary>
    private void ExportOne(
        string sourcePath, string outputPath, ProcessingSettings processing, ExportSettings settings)
    {
        byte[] bytes;

        // 書き出しは原寸で行う。maxEdge に 0 を渡すと縮小されず Scale は 1.0 になる
        using (var source = _renderer.Load(sourcePath, 0))
        {
            if (source is null)
            {
                throw new IOException("画像を読み込めませんでした。");
            }

            // 拡張子は OutputPathResolver が形式に合わせて決めている
            var encode = new EncodeSettings
            {
                Extension = Path.GetExtension(outputPath),
                JpegQuality = settings.JpegQuality,
                WebPQuality = settings.EffectiveWebPQuality,
            };
            bytes = _renderer.Encode(source, processing, source.Scale, encode);
        }

        // 一時ファイルに書いてから移す。中止や失敗で中途半端なファイルを残さないため。
        // 第 2 段で上書きを実装したとき、これが無いと書き込み中の中断で原本が壊れる
        var tempPath = outputPath + TempExtension;
        try
        {
            File.WriteAllBytes(tempPath, bytes);

            // 同じフォルダ内なので、移動は実質的に名前の付け替えで済む
            File.Move(tempPath, outputPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// 出力先に書き込めるかどうかを確かめる。
    /// </summary>
    /// <returns>書き込めない理由。問題なければ <see langword="null" />。</returns>
    /// <remarks>
    /// 属性や ACL を調べるのではなく、実際に書いて消してみる。権限・読み取り専用メディア・
    /// 容量など、事前に判別しづらい要因をまとめて確認できるため。
    /// </remarks>
    private static string? CheckWritable(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return "出力先が指定されていません。";
        }

        if (!Directory.Exists(folder))
        {
            return $"出力先のフォルダが見つかりません: {folder}";
        }

        var probePath = Path.Combine(folder, $"soroe-write-test-{Guid.NewGuid():N}{TempExtension}");
        try
        {
            File.WriteAllBytes(probePath, [0]);
            File.Delete(probePath);
            return null;
        }
        catch (Exception ex)
        {
            return $"出力先に書き込めません: {ex.Message}";
        }
    }

    private static bool IsSamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
