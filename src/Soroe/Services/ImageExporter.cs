using System.IO;
using Soroe.Common;
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
    public ExportPlan Plan(IReadOnlyList<string> sourcePaths, ExportSettings settings)
    {
        if (sourcePaths.Count == 0)
        {
            return ExportPlan.Empty;
        }

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 上書きしないなら既存ファイルは必ず避けられる。したがって置き換えも
        // 元ファイルとの一致も起こりえない。全件を調べる必要がない
        if (!settings.Overwrite)
        {
            return new ExportPlan(
                _resolver.Resolve(sourcePaths[0], settings, 0, sourcePaths.Count, reserved),
                sourcePaths.Count,
                0,
                0);
        }

        var sources = BuildSourceSet(sourcePaths);
        var first = string.Empty;
        var overwrite = 0;
        var skipped = 0;

        for (var i = 0; i < sourcePaths.Count; i++)
        {
            var outputPath = _resolver.Resolve(sourcePaths[i], settings, i, sourcePaths.Count, reserved);
            if (i == 0)
            {
                first = outputPath;
            }

            if (sources.Contains(outputPath))
            {
                skipped++;
                continue;
            }

            reserved.Add(outputPath);
            if (File.Exists(outputPath))
            {
                overwrite++;
            }
        }

        return new ExportPlan(first, sourcePaths.Count, overwrite, skipped);
    }

    /// <summary>
    /// 安全ガードの比較対象。<b>リスト内のすべての入力パス</b>を集める。
    /// </summary>
    /// <remarks>
    /// 自分自身とだけ比べるのでは足りない。例えば <c>IMG_0001.jpg</c> と
    /// <c>小_IMG_0001.jpg</c> が同じリストにあり、同フォルダ出力・プレフィックス
    /// <c>小_</c>・上書き ON だと、前者の処理結果が後者（別の原本）を潰してしまう。
    /// </remarks>
    private static HashSet<string> BuildSourceSet(IReadOnlyList<string> sourcePaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in sourcePaths)
        {
            set.Add(Path.GetFullPath(path));
        }

        return set;
    }

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

        // 安全ガードの比較対象。リスト内のすべての入力パスと突き合わせる
        var sources = BuildSourceSet(sourcePaths);

        // この実行で使うと決めたパス。上書きが選ばれていても、ここにあるものは避ける
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sourcePaths.Count; i++)
        {
            // 中止の確認は 1 枚ごと。エンコードの途中では割り込まない。
            //
            // 1 枚にかかる時間は出力形式で大きく変わる。実測 2026-08-10
            // （Release、実写 3000×4000、読み込みから書き込みまで）で
            // JPEG 80ms / BMP 85ms に対し、PNG は 0.8 秒、WebP は 1.8 秒かかる。
            // WebP が遅いのはエンコーダ自体の費用で、設定では下げられない。
            // 再現は tests/Soroe.Verify の --bench（CLAUDE.md「構成とビルド」参照）。
            // したがって「中止を押してから止まるまで」は、最悪でこの 1 枚分待つことになる。
            // 途中で割り込む作りにすると中途半端なファイルの後始末が要るので、
            // 1 枚を区切りにするこの形は変えない
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
                var outputPath = _resolver.Resolve(sourcePath, settings, i, sourcePaths.Count, reserved);

                // 安全ガード。出力先が「リスト内のいずれかの原本」と一致したら無条件で飛ばす。
                // 自分自身とだけ比べるのでは足りない。別の原本を処理結果で潰す経路が残る。
                // 呼び出し方に依存せず必ず通るよう、書き込みの直前のここで判定する
                var skipping = sources.Contains(Path.GetFullPath(outputPath));

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

                // 書き込みの成否に関わらず予約する。同じ実行の中で同じ名前を
                // 二度使わないことが目的なので、失敗した枠も空けない
                reserved.Add(outputPath);

                ExportOne(sourcePath, outputPath, processing, settings);
                exported++;
            }
            catch (Exception ex)
            {
                // 1 枚の失敗で全体を止めない。集めて最後にまとめて報告する。
                // 画面には日本語の要約を出し、原文は控えとデバッグ出力に残す
                FileErrorMessage.Log($"書き出し失敗 {sourcePath}", ex);
                failures.Add(new ExportFailure(
                    sourcePath, FileErrorMessage.Describe(ex, sourcePath), FileErrorMessage.Detail(ex)));
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
                throw new UserMessageException("画像を読み込めませんでした");
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

            // 同じフォルダ内なので、移動は実質的に名前の付け替えで済む。
            // 上書きしない設定なら空きパスが保証されているので overwrite も false でよい。
            // 常に true にすると、想定外の衝突が起きたときに気づけなくなる
            File.Move(tempPath, outputPath, overwrite: settings.Overwrite);
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
            FileErrorMessage.Log($"出力先の書き込み確認 {folder}", ex);
            return $"出力先に書き込めません: {FileErrorMessage.Describe(ex, probePath)}";
        }
    }
}
