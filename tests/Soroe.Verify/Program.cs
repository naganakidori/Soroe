using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using Soroe.Common;
using Soroe.Models;
using Soroe.Services;
using Soroe.ViewModels;

internal static class Program
{
    private static int _failed;

    [STAThread]
    private static int Main()
    {
        TestFileList();
        TestOrdering();
        TestRendering();
        TestSpinner();
        TestCloneCompleteness();
        TestResolveSize();
        TestResize();
        TestToneCurve();
        TestMonochrome();
        TestCanonicalImage();
        TestRenderEncodeAgreement();
        TestExport();
        TestFormats();
        TestTransparency();
        TestBitDepth();
        TestNonAsciiOutput();
        TestNaming();
        TestCollision();
        TestStalePlanGuard().GetAwaiter().GetResult();
        TestStaleAllSkippedGuard().GetAwaiter().GetResult();
        TestEstimateAndResultAreExclusive().GetAwaiter().GetResult();
        TestExportDialog().GetAwaiter().GetResult();
        TestExportDialogViewModel().GetAwaiter().GetResult();
        TestErrorMessages();
        TestSettingsStore();
        TestAutoSaveTriggers();

        Console.WriteLine(_failed == 0 ? "\nすべて成功" : $"\n{_failed} 件失敗");
        return _failed == 0 ? 0 : 1;
    }

    private static void TestFileList()
    {
        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        // 対応 3 件 + 非対応 1 件 + サブフォルダに 1 件
        WriteImage(Path.Combine(root, "a.jpg"), 120, 80);
        WriteImage(Path.Combine(root, "b.PNG"), 60, 40);
        WriteImage(Path.Combine(root, "c.webp"), 30, 20);
        File.WriteAllText(Path.Combine(root, "memo.txt"), "not an image");
        WriteImage(Path.Combine(root, "sub", "d.jpg"), 10, 10);

        var vm = NewViewModel(new StubFolderPicker(root));

        vm.AddFolderCommand.Execute(null);
        Check("フォルダ追加で対応 3 件のみ", vm.Files.Count == 3);
        Check("サブフォルダは含めない", vm.Files.All(f => !f.FullPath.Contains(Path.Combine("sub", ""))));
        Check("非対応拡張子を除外", vm.Files.All(f => !f.FileName.EndsWith(".txt")));
        Check("1 枚目が自動選択される", vm.SelectedFile is not null && vm.SelectedFile == vm.Files[0]);

        vm.AddFolderCommand.Execute(null);
        Check("同じフォルダの再追加で増えない", vm.Files.Count == 3);
        Check("重複時の状態表示", vm.StatusMessage.Contains("追加済み"), vm.StatusMessage);

        // 大文字小文字だけが違うパスも同一とみなす
        vm.AddPathsCommand.Execute(new[] { Path.Combine(root, "A.JPG") });
        Check("大文字小文字違いを重複扱い", vm.Files.Count == 3);

        vm.ClearCommand.Execute(null);
        Check("クリアで空になる", vm.Files.Count == 0);
        Check("クリアで選択が外れる", vm.SelectedFile is null);
        Check("クリアでプレビューが消える", vm.PreviewImage is null);
        Check("空のときクリアは無効", !vm.ClearCommand.CanExecute(null));

        // フォルダのドロップはフォルダ追加と同じ結果になる
        vm.AddPathsCommand.Execute(new[] { root });
        Check("フォルダのドロップも同じ 3 件", vm.Files.Count == 3);
        Check("件数ありでクリアが有効", vm.ClearCommand.CanExecute(null));

        // プレビューは非同期に読み込まれる
        vm.SelectedFile = vm.Files.First(f => f.FileName == "a.jpg");
        Check("プレビューが読み込まれる", SpinUntil(() => vm.PreviewImage is not null, TimeSpan.FromSeconds(5)));
        Check("プレビューの寸法が一致", vm.PreviewImage is { PixelWidth: 120, PixelHeight: 80 },
            $"{vm.PreviewImage?.PixelWidth}x{vm.PreviewImage?.PixelHeight}");

        // 壊れたファイルでも落ちない
        var broken = Path.Combine(root, "broken.jpg");
        File.WriteAllBytes(broken, new byte[] { 0xFF, 0xD8, 0x00, 0x01 });
        vm.AddPathsCommand.Execute(new[] { broken });
        vm.SelectedFile = vm.Files.First(f => f.FileName == "broken.jpg");
        SpinUntil(() => vm.StatusMessage.Contains("読み込めません"), TimeSpan.FromSeconds(5));
        Check("壊れたファイルは状態表示で知らせる", vm.StatusMessage.Contains("読み込めません"), vm.StatusMessage);

        vm.ClearCommand.Execute(null);
        SpinUntil(() => vm.PreviewImage is null, TimeSpan.FromSeconds(5));
        DeleteWithRetry(root);
    }

    private static void TestOrdering()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_order");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        // 作成順をわざと崩す
        foreach (var name in new[] { "IMG_10.jpg", "IMG_2.jpg", "IMG_1.jpg" })
        {
            WriteImage(Path.Combine(first, name), 8, 8);
        }

        // 2 バッチ目。名前としては 1 バッチ目より前に来るもの
        WriteImage(Path.Combine(second, "AAA.jpg"), 8, 8);
        WriteImage(Path.Combine(second, "BBB.jpg"), 8, 8);

        var vm = NewViewModel(new StubFolderPicker(first));

        vm.AddFolderCommand.Execute(null);
        Check("フォルダ追加が自然順（IMG_1 → IMG_2 → IMG_10）",
            Names(vm) == "IMG_1.jpg,IMG_2.jpg,IMG_10.jpg", Names(vm));

        vm.AddPathsCommand.Execute(new[] { second });
        Check("2 バッチ目は名前が前でも末尾に付く",
            Names(vm) == "IMG_1.jpg,IMG_2.jpg,IMG_10.jpg,AAA.jpg,BBB.jpg", Names(vm));

        // ドロップは OS が渡す順序が不定なので、逆順で渡しても並びが揃うこと
        var dropped = NewViewModel(new StubFolderPicker(first));
        dropped.AddPathsCommand.Execute(new[]
        {
            Path.Combine(first, "IMG_2.jpg"),
            Path.Combine(first, "IMG_10.jpg"),
            Path.Combine(first, "IMG_1.jpg"),
        });
        Check("ファイルのドロップも自然順に整列",
            Names(dropped) == "IMG_1.jpg,IMG_2.jpg,IMG_10.jpg", Names(dropped));

        vm.ClearCommand.Execute(null);
        dropped.ClearCommand.Execute(null);
        SpinUntil(() => vm.PreviewImage is null && dropped.PreviewImage is null, TimeSpan.FromSeconds(5));
        DeleteWithRetry(root);
    }

    private static void TestRendering()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_render");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        // 中間調のべた塗り。明るさの増減が画素値でそのまま確かめられる
        var flat = Path.Combine(root, "flat.png");
        WriteFlat(flat, 40, 30, 100);

        // プレビューの上限（長辺 1600）を超える画像
        var large = Path.Combine(root, "large.png");
        WriteFlat(large, 4000, 2000, 100);

        var vm = NewViewModel(new StubFolderPicker(root));
        vm.AddPathsCommand.Execute(new[] { flat });
        SpinUntil(() => vm.PreviewImage is not null, TimeSpan.FromSeconds(5));

        Check("既定は全項目 OFF", !vm.Settings.Brightness.Enabled && vm.Settings.Brightness.Value == 0);
        Check("調整なしなら元の画素のまま", Center(vm) == 100, $"{Center(vm)}");

        // 値だけ動かしても Enabled が OFF なら効かない
        vm.Settings.Brightness.Value = 60;
        SpinUntil(() => false, TimeSpan.FromMilliseconds(500));
        Check("OFF のままなら値を変えても効かない", Center(vm) == 100, $"{Center(vm)}");

        vm.Settings.Brightness.Enabled = true;
        SpinUntil(() => Center(vm) == 160, TimeSpan.FromSeconds(5));
        Check("ON にすると明るくなる（100 → 160）", Center(vm) == 160, $"{Center(vm)}");

        vm.Settings.Brightness.Value = -40;
        SpinUntil(() => Center(vm) == 60, TimeSpan.FromSeconds(5));
        Check("負の値で暗くなる（100 → 60）", Center(vm) == 60, $"{Center(vm)}");

        // 非破壊: 値を戻せば元の画素に戻る（劣化が蓄積しない）
        vm.Settings.Brightness.Value = 0;
        SpinUntil(() => Center(vm) == 100, TimeSpan.FromSeconds(5));
        Check("値を 0 に戻すと元の画素に戻る", Center(vm) == 100, $"{Center(vm)}");

        vm.Settings.Brightness.Value = 100;
        SpinUntil(() => Center(vm) == 200, TimeSpan.FromSeconds(5));
        Check("加算されて 200 になる", Center(vm) == 200, $"{Center(vm)}");

        // View 側の丸めはバインディングのソースまで伝わらないため、モデルで抑える。
        // ここが効いていないと、画面には 100 と出ているのに 1000 で処理される
        vm.Settings.Brightness.Value = 1000;
        Check("範囲外の値はモデルが丸める", vm.Settings.Brightness.Value == 100,
            $"{vm.Settings.Brightness.Value}");
        SpinUntil(() => Center(vm) == 200, TimeSpan.FromSeconds(5));
        Check("丸めた値でプレビューされる（真っ白にならない）", Center(vm) == 200, $"{Center(vm)}");

        vm.Settings.Brightness.Value = -1000;
        Check("下限側も同じく丸める", vm.Settings.Brightness.Value == -100,
            $"{vm.Settings.Brightness.Value}");

        // 連続変更しても最新の値に落ち着く（スライダーを動かしたときの挙動）
        for (var v = 1; v <= 50; v++) vm.Settings.Brightness.Value = v;
        SpinUntil(() => Center(vm) == 150, TimeSpan.FromSeconds(10));
        Check("連続変更でも最後の値に落ち着く（+50）", Center(vm) == 150, $"{Center(vm)}");

        // プレビューは縮小される
        vm.AddPathsCommand.Execute(new[] { large });
        vm.SelectedFile = vm.Files.First(f => f.FileName == "large.png");
        SpinUntil(() => vm.PreviewImage is { PixelWidth: 1600 }, TimeSpan.FromSeconds(15));
        Check("大きい画像はプレビュー用に縮小される（4000 → 1600）",
            vm.PreviewImage is { PixelWidth: 1600, PixelHeight: 800 },
            $"{vm.PreviewImage?.PixelWidth}x{vm.PreviewImage?.PixelHeight}");
        Check("寸法表示は縮小前の寸法を出す",
            vm.PreviewSizeText.Contains("4000") && vm.PreviewSizeText.Contains("2000"), vm.PreviewSizeText);

        // リサイズを入れると「元 → 出力」の形になる
        vm.Settings.Resize.Enabled = true;
        vm.Settings.Resize.LongestEdge = 1920;
        Check("リサイズ有効時は出力寸法も並べて出す",
            vm.PreviewSizeText.Contains("→") && vm.PreviewSizeText.Contains("1920"), vm.PreviewSizeText);
        vm.Settings.Resize.Enabled = false;
        Check("リサイズ無効に戻すと元の寸法のみ",
            !vm.PreviewSizeText.Contains("→"), vm.PreviewSizeText);

        // 縮小されていても明るさは同じだけ効く（画素値は倍率の影響を受けない）
        Check("縮小後でも明るさは同じだけ効く", Center(vm) == 150, $"{Center(vm)}");

        // 書き出し相当（原寸で読み、倍率 1.0 で適用）でも同じ結果になること
        var renderer = new ImageRenderer();
        using (var full = renderer.Load(large, 0))
        {
            Check("原寸で読むと縮小されない", full is { OriginalWidth: 4000, Scale: 1.0 },
                $"{full?.OriginalWidth} scale={full?.Scale}");

            var rendered = renderer.Render(full!, vm.Settings.Clone(), full!.Scale);
            Check("原寸レンダリングも同じ画素値", CenterOf(rendered) == 150, $"{CenterOf(rendered)}");
            Check("原寸レンダリングは原寸のまま", rendered.PixelWidth == 4000, $"{rendered.PixelWidth}");
        }

        vm.ClearCommand.Execute(null);
        SpinUntil(() => vm.PreviewImage is null, TimeSpan.FromSeconds(5));
        DeleteWithRetry(root);
    }

    private static void TestSpinner()
    {
        Console.WriteLine();

        var spinner = new Soroe.Views.Controls.NumericSpinner
        {
            Minimum = BrightnessOption.MinValue,
            Maximum = BrightnessOption.MaxValue,
        };

        Check("スピナーの既定値は 0", spinner.Value == 0, $"{spinner.Value}");
        Check("既定の増減量は 1", spinner.Step == 1, $"{spinner.Step}");

        spinner.Value = 42;
        Check("範囲内はそのまま入る", spinner.Value == 42, $"{spinner.Value}");

        spinner.Value = 9999;
        Check("上限を超えたら丸める", spinner.Value == BrightnessOption.MaxValue, $"{spinner.Value}");

        spinner.Value = -9999;
        Check("下限を下回ったら丸める", spinner.Value == BrightnessOption.MinValue, $"{spinner.Value}");

        // 範囲を後から狭めても現在値が外に残らないこと
        spinner.Value = 0;
        spinner.Minimum = 10;
        Check("範囲を変えたら現在値も追従する", spinner.Value == 10, $"{spinner.Value}");
    }

    /// <summary>
    /// プレビューと書き出しの内容差の上限（平均絶対差、0〜255 の尺度）。
    /// </summary>
    /// <remarks>
    /// プレビューは「元画像 → 1600px → 出力寸法×倍率」と 2 回縮小されるのに対し、
    /// 書き出しは「元画像 → 出力寸法」の 1 回なので、原理的に完全一致はしない。
    /// 2.0 は 256 階調に対して 1% 未満で、目視では違いが分からない水準。
    /// 一方、適用の取り違え（明るさが二重にかかる、リサイズが片方だけ効く等）が
    /// 起きれば平均差は数十以上になるため、異常はこの閾値で確実に捕まえられる。
    /// </remarks>
    private const double MaxMeanDifference = 2.0;

    /// <summary>
    /// Clone() が全プロパティを写しているかをリフレクションで検証する。
    /// </summary>
    /// <remarks>
    /// 「項目を追加したらここにも追加すること」というコメントは人の注意力に頼る対策で、
    /// 実際に破られた。写し漏れたプロパティは既定値のまま残るので、全プロパティを
    /// 既定と違う値にしてから Clone すれば必ず検出できる。
    /// プロパティを増やした時点で自動的に検証対象になるため、書き足す必要がない。
    /// </remarks>
    private static void TestErrorMessages()
    {
        Console.WriteLine();

        // 英文がそのまま出ないこと、原因ごとに文言が分かれること
        Check("フォルダなし", FileErrorMessage.Describe(new DirectoryNotFoundException("Could not find a part of the path")) == "フォルダが見つかりません");
        Check("権限なし", FileErrorMessage.Describe(new UnauthorizedAccessException("Access to the path is denied")) == "書き込みが許可されていません");
        Check("パスが長い", FileErrorMessage.Describe(new PathTooLongException()) == "パスが長すぎます");

        var diskFull = new IOException("There is not enough space on the disk") { HResult = unchecked((int)0x80070070) };
        Check("空き容量不足", FileErrorMessage.Describe(diskFull) == "空き容量が足りません",
            FileErrorMessage.Describe(diskFull));

        var sharing = new IOException("The process cannot access the file") { HResult = unchecked((int)0x80070020) };
        Check("使用中", FileErrorMessage.Describe(sharing) == "他のプログラムが使用中です", FileErrorMessage.Describe(sharing));

        Check("その他の IO", FileErrorMessage.Describe(new IOException("boom")) == "ファイルの読み書きに失敗しました");

        // ArgumentException は実際に不正文字があるときだけ断定する
        var argument = new ArgumentException("Illegal characters in path");
        Check("不正文字ありなら断定する",
            FileErrorMessage.Describe(argument, "C:\\out\\bad|name.jpg") == "パスに使えない文字が含まれています",
            FileErrorMessage.Describe(argument, "C:\\out\\bad|name.jpg"));
        Check("不正文字が無ければ断定しない",
            FileErrorMessage.Describe(argument, "C:\\out\\ok.jpg").StartsWith("予期しないエラーです"),
            FileErrorMessage.Describe(argument, "C:\\out\\ok.jpg"));
        Check("パス不明でも断定しない",
            FileErrorMessage.Describe(argument).StartsWith("予期しないエラーです"),
            FileErrorMessage.Describe(argument));

        // 対応表から漏れたものは型名を添える
        Check("未対応は型名を添える",
            FileErrorMessage.Describe(new InvalidTimeZoneException()) == "予期しないエラーです（InvalidTimeZoneException）",
            FileErrorMessage.Describe(new InvalidTimeZoneException()));

        // アプリ自身のメッセージはそのまま通す
        Check("自前のメッセージはそのまま",
            FileErrorMessage.Describe(new UserMessageException("画像を読み込めませんでした")) == "画像を読み込めませんでした");

        // 原文は失われない
        var detail = FileErrorMessage.Detail(new IOException("boom"));
        Check("原文が残る", detail.Contains("System.IO.IOException") && detail.Contains("boom"), detail);
    }

    private static void TestSettingsStore()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_settings");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, "settings.json");
        var store = new SettingsStore(root);

        var initial = store.Load();
        Check("ファイルが無ければ既定値",
            !initial.Export.Overwrite && initial.Export.Folder is null && !initial.Processing.Brightness.Enabled);

        // 全プロパティを既定と違う値にしてから往復させる。
        // 項目を足したときに保存し忘れても、ここで落ちる
        var processing = new ProcessingSettings();
        var export = new Soroe.Models.ExportSettings();
        var changed = new List<string>();
        var unchangeable = new List<string>();
        Mutate(processing, "ProcessingSettings", changed, unchangeable);
        Mutate(export, "ExportSettings", changed, unchangeable);

        Console.WriteLine($"    往復の対象: {string.Join(", ", changed)}");

        Check($"保存前に全プロパティを既定と変えられた（{changed.Count} 項目）",
            unchangeable.Count == 0, string.Join(", ", unchangeable));
        Check("上書きが ON になっている（除外の検証に必要）", export.Overwrite);

        Check("保存できる", store.Save(processing, export));
        Check("設定ファイルができる", File.Exists(path));
        Check("一時ファイルが残らない", !File.Exists(path + ".soroe-tmp"));
        Check("保存しても元の設定は変わらない（写しに対して除外する）", export.Overwrite);

        var loaded = store.Load();

        var mismatches = new List<string>();
        Compare(processing, loaded.Processing, "ProcessingSettings", mismatches);
        Compare(export, loaded.Export, "ExportSettings", mismatches);

        // Overwrite だけは意図的に落とすので、往復で一致しないのが正しい
        Check("除外した項目以外はすべて往復する",
            mismatches.Count == 1 && mismatches[0].StartsWith("ExportSettings.Overwrite"),
            string.Join(" / ", mismatches));
        Check("Overwrite は保存されず既定へ戻る", !loaded.Export.Overwrite);
        Check("ファイルにも Overwrite の値が残らない",
            !File.ReadAllText(path).Replace(" ", "").Contains("\"overwrite\":true"));

        // 読み込んだ設定は、そのまま画面に繋いで使えなければ意味がない
        var fired = false;
        loaded.Processing.Changed += (_, _) => fired = true;
        loaded.Processing.Brightness.Value = loaded.Processing.Brightness.Value == 12 ? 13 : 12;
        Check("復元後も Changed が繋がっている", fired);

        // 上書きだけをオンにした古い設定ファイルを直接置いても復活しない
        File.WriteAllText(path, "{\"schemaVersion\":1,\"export\":{\"overwrite\":true,\"jpegQuality\":40}}");
        var handEdited = store.Load();
        Check("ファイルに書かれていても Overwrite は復元しない", !handEdited.Export.Overwrite);
        Check("同じ版の他の項目は読み込む", handEdited.Export.JpegQuality == 40, $"{handEdited.Export.JpegQuality}");

        // 現在より古い版は将来の移行対象。読み込めること自体は変えない
        File.WriteAllText(path, "{\"schemaVersion\":0,\"export\":{\"jpegQuality\":41}}");
        Check("古い版も読み込む", store.Load().Export.JpegQuality == 41);

        // 現在より新しい版は読み方が分からない
        File.WriteAllText(path, "{\"schemaVersion\":999,\"export\":{\"jpegQuality\":42}}");
        Check("現在より新しい版は既定値に落とす", store.Load().Export.JpegQuality == 95);

        File.WriteAllText(path, "{ これは JSON ではない");
        var broken = store.Load();
        Check("壊れた JSON でも落ちず既定値", broken.Export.Folder is null && broken.Export.JpegQuality == 95);

        File.WriteAllText(path, "{\"schemaVersion\":1,\"export\":{\"format\":\"Tiff\"}}");
        Check("知らない形式名でも落ちず既定値", store.Load().Export.Format == ExportFormat.KeepOriginal);

        File.WriteAllText(path, "null");
        Check("中身が null でも既定値", store.Load().Export.JpegQuality == 95);

        // 保存できない場所でも例外を出さない（終了処理を止めないため）。
        // 同名のフォルダを置いて File.Move を失敗させる
        var blockedRoot = Path.Combine(root, "blocked");
        Directory.CreateDirectory(Path.Combine(blockedRoot, "settings.json"));
        var blocked = new SettingsStore(blockedRoot);
        Check("保存に失敗しても例外を出さない", !blocked.Save(processing, export));
        Check("失敗しても一時ファイルを残さない",
            !File.Exists(Path.Combine(blockedRoot, "settings.json.soroe-tmp")));
        Check("読めない場所でも既定値を返す", blocked.Load().Export.JpegQuality == 95);

        // 復元した値からリサイズの入力モードが導出されること
        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);

        var freeSettings = new ProcessingSettings();
        freeSettings.Resize.LongestEdge = 1000;
        var freeVm = new MainViewModel(
            freeSettings, new Soroe.Models.ExportSettings(), renderer, exporter,
            new StubFolderPicker(root), new StubExportDialog());
        Check("候補に無い値で復元したら自由入力になる", freeVm.IsResizeFreeInput);
        Check("自由入力でも値は復元した値のまま", freeVm.Settings.Resize.LongestEdge == 1000,
            $"{freeVm.Settings.Resize.LongestEdge}");

        var presetSettings = new ProcessingSettings();
        presetSettings.Resize.LongestEdge = 1280;
        var presetVm = new MainViewModel(
            presetSettings, new Soroe.Models.ExportSettings(), renderer, exporter,
            new StubFolderPicker(root), new StubExportDialog());
        Check("候補と同じ値で復元したらその候補が選ばれる",
            !presetVm.IsResizeFreeInput && presetVm.SelectedResizePreset?.LongestEdge == 1280);

        // 復元した実体をそのまま画面が使う（写し取らない）ことの確認
        var restoredExport = new Soroe.Models.ExportSettings { Folder = root, JpegQuality = 33 };
        var restoredVm = new MainViewModel(
            new ProcessingSettings(), restoredExport, renderer, exporter,
            new StubFolderPicker(root), new StubExportDialog());
        Check("書き出し設定は復元した実体をそのまま使う", ReferenceEquals(restoredVm.Output, restoredExport));
        Check("復元した出力先と品質がそのまま残る",
            restoredVm.Output.Folder == root && restoredVm.Output.JpegQuality == 33);

        DeleteWithRetry(root);
    }

    /// <summary>
    /// 「値が正しく往復するか」ではなく「変えたら保存が走るか」を確かめる。
    /// </summary>
    /// <remarks>
    /// 保存されるかどうかは、購読しているイベントが全項目の変更を拾えているかに依存する。
    /// 入れ子の項目が増えて通知が上がらなくなっても、往復のテストは通ったままになる。
    /// Clone のときと同じ構図なので、同じくリフレクションで全項目を突く。
    /// </remarks>
    private static void TestAutoSaveTriggers()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_autosave");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, "settings.json");
        var store = new SettingsStore(root);
        var processing = new ProcessingSettings();
        var export = new Soroe.Models.ExportSettings();

        // 多重起動で一時ファイルを取り合わないこと
        var tempPath = (string)typeof(SettingsStore)
            .GetField("_tempPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(store)!;
        Check("一時ファイル名にプロセス ID が入る", tempPath.Contains($".{Environment.ProcessId}."), tempPath);

        var requested = 0;
        var saver = new SettingsAutoSaver(store, processing, export, TimeSpan.FromMilliseconds(60));
        saver.SaveRequested += (_, _) => requested++;

        var leaves = LeafProperties(processing, "ProcessingSettings")
            .Concat(LeafProperties(export, "ExportSettings"))
            .ToList();

        Console.WriteLine($"    引き金の対象: {string.Join(", ", leaves.Select(l => l.Path))}");

        Check($"保存対象のプロパティを列挙できた（{leaves.Count} 項目）", leaves.Count >= 21, $"{leaves.Count}");

        var silent = new List<string>();
        var unchangeable = new List<string>();

        foreach (var (owner, property, name) in leaves)
        {
            var before = property.GetValue(owner);
            var applied = false;
            requested = 0;

            foreach (var candidate in Candidates(property.PropertyType, before))
            {
                property.SetValue(owner, candidate);
                if (!Equals(property.GetValue(owner), before))
                {
                    applied = true;
                    break;
                }
            }

            if (!applied)
            {
                unchangeable.Add(name);
                continue;
            }

            if (requested == 0) silent.Add(name);
        }

        Check("すべての保存対象を既定と違う値にできた", unchangeable.Count == 0, string.Join(", ", unchangeable));
        Check("すべての保存対象が保存の引き金になる", silent.Count == 0, string.Join(", ", silent));

        // 引き金が引かれるだけでなく、待ち時間の後に実際に書かれること
        if (File.Exists(path)) File.Delete(path);
        export.JpegQuality = 44;
        Check("待っている間はまだ書かれない", !File.Exists(path));

        Pump(TimeSpan.FromMilliseconds(400));

        // 書き込みはバックグラウンドなので、合図の後に少しだけ猶予を見る
        Check("待ち時間が過ぎたら実際に保存される",
            SpinUntil(() => File.Exists(path), TimeSpan.FromSeconds(5)));
        Check("保存された内容が最新", store.Load().Export.JpegQuality == 44, $"{store.Load().Export.JpegQuality}");
        Check("保存後に一時ファイルが残らない", !File.Exists(tempPath));

        // 終了時の保存は待たずに書く
        File.Delete(path);
        export.JpegQuality = 46;
        saver.SaveNow();
        Check("SaveNow は待たずに保存する", File.Exists(path) && store.Load().Export.JpegQuality == 46);

        saver.Dispose();
        File.Delete(path);
        export.JpegQuality = 47;
        Pump(TimeSpan.FromMilliseconds(400));
        Check("Dispose 後は保存しない", !File.Exists(path));

        // 保存の待ちが UI スレッドに乗っていないこと。
        // settings.json と同名のフォルダを置くと File.Move が必ず失敗するので、
        // 再試行が最後まで走る（合計 200ms 待つ）状況を作れる
        var slowRoot = Path.Combine(root, "slow");
        Directory.CreateDirectory(Path.Combine(slowRoot, "settings.json"));
        var slowExport = new Soroe.Models.ExportSettings();
        var slowSaver = new SettingsAutoSaver(
            new SettingsStore(slowRoot), new ProcessingSettings(), slowExport, TimeSpan.FromMilliseconds(20));

        // 一定間隔の拍を打ち、途切れた最大の幅を見る。UI スレッドで待たれると
        // その分だけまとめて拍が抜けるので、件数より幅のほうがはっきり出る
        var maxGap = TimeSpan.Zero;
        var last = DateTime.UtcNow;
        var beat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        beat.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            if (now - last > maxGap) maxGap = now - last;
            last = now;
        };

        last = DateTime.UtcNow;
        beat.Start();
        slowExport.JpegQuality = 50;
        Pump(TimeSpan.FromMilliseconds(500));
        beat.Stop();
        slowSaver.Dispose();

        Check("再試行の間も UI スレッドが止まらない", maxGap < TimeSpan.FromMilliseconds(100),
            $"最大 {maxGap.TotalMilliseconds:F0}ms 途切れた");

        // 写しを取る順序と、実際に書かれる順序が入れ替わらないこと。
        // 入れ替わると直前の変更が巻き戻り、その後は変更が起きないので
        // 巻き戻ったまま確定する
        var orderRoot = Path.Combine(root, "order");
        Directory.CreateDirectory(orderRoot);
        var orderStore = new SettingsStore(orderRoot);
        var orderExport = new Soroe.Models.ExportSettings();
        var orderSaver = new SettingsAutoSaver(
            orderStore, new ProcessingSettings(), orderExport, TimeSpan.FromMilliseconds(1));

        var rolledBack = new List<string>();
        for (var round = 1; round <= 5; round++)
        {
            // 待ち時間を 1ms にしてあるので、書き込みが終わらないうちに
            // 次の写しが取られる状況になる
            var final = round * 10;
            for (var value = final - 9; value <= final; value++)
            {
                orderExport.JpegQuality = value;
                Pump(TimeSpan.FromMilliseconds(3));
            }

            // すべての保存が終わるだけの猶予を置いてから確定値を見る
            Pump(TimeSpan.FromMilliseconds(300));
            var settled = orderStore.Load().Export.JpegQuality;
            if (settled != final) rolledBack.Add($"{round} 回目: {settled} ≠ {final}");
        }

        Check("連続で変更しても最後の値で確定する", rolledBack.Count == 0, string.Join(" / ", rolledBack));
        orderSaver.Dispose();

        // 上の「最後の値」だけでは、最新優先を外しても通ってしまう（実測で確認済み）。
        // 仕組みが実際に働いていることを直接見る。
        // settings.json と同名のフォルダを置くと 1 回の書き込みが 200ms 掛かるので、
        // その間に要求を重ねれば、古い写しが順番待ちに溜まる状況を作れる
        var busyRoot = Path.Combine(root, "busy");
        Directory.CreateDirectory(Path.Combine(busyRoot, "settings.json"));
        var busyExport = new Soroe.Models.ExportSettings();
        var busySaver = new SettingsAutoSaver(
            new SettingsStore(busyRoot), new ProcessingSettings(), busyExport, TimeSpan.FromMilliseconds(1));

        busyExport.JpegQuality = 1;
        Pump(TimeSpan.FromMilliseconds(20));

        for (var value = 2; value <= 5; value++)
        {
            busyExport.JpegQuality = value;
            Pump(TimeSpan.FromMilliseconds(5));
        }

        Pump(TimeSpan.FromMilliseconds(500));
        Check("書き込み中に重なった古い写しは捨てる", busySaver.DiscardedSaves > 0,
            $"{busySaver.DiscardedSaves} 件");
        busySaver.Dispose();

        // 終了時の保存に上限があること。settings.json と同名のフォルダを置くと
        // 必ず再試行に入り、1 回の書き込みに 200ms かかる
        var slowExitRoot = Path.Combine(root, "slowexit");
        Directory.CreateDirectory(Path.Combine(slowExitRoot, "settings.json"));
        var exitExport = new Soroe.Models.ExportSettings();
        var exitSaver = new SettingsAutoSaver(
            new SettingsStore(slowExitRoot),
            new ProcessingSettings(),
            exitExport,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(30));

        exitExport.JpegQuality = 50;
        Pump(TimeSpan.FromMilliseconds(40));

        var exitWatch = System.Diagnostics.Stopwatch.StartNew();
        var saved = exitSaver.SaveNow();
        exitWatch.Stop();

        Check("終了時の保存は上限で打ち切る", exitWatch.ElapsedMilliseconds < 150,
            $"{exitWatch.ElapsedMilliseconds}ms 待った");
        Check("打ち切ったことが分かる", !saved);
        exitSaver.Dispose();

        // 打ち切りで置き去りになった一時ファイルは次の起動時に片付く。
        // ただし一時ファイルは「書き終えてから File.Move するまで」の一瞬だけ
        // 誰にも掴まれていないので、掴まれていないことを根拠に消してはいけない
        var strayRoot = Path.Combine(root, "stray");
        Directory.CreateDirectory(strayRoot);

        var oldStray = Path.Combine(strayRoot, "settings.json.99999.soroe-tmp");
        File.WriteAllText(oldStray, "{}");
        File.SetLastWriteTimeUtc(oldStray, DateTime.UtcNow.AddHours(-2));

        // 別のインスタンスがいま書いたばかりかもしれないもの
        var freshStray = Path.Combine(strayRoot, "settings.json.99998.soroe-tmp");
        File.WriteAllText(freshStray, "{}");

        // 動いているプロセスのもの（自分自身の PID を使う）
        var ownStray = Path.Combine(strayRoot, $"settings.json.{Environment.ProcessId}.soroe-tmp");
        File.WriteAllText(ownStray, "{}");
        File.SetLastWriteTimeUtc(ownStray, DateTime.UtcNow.AddHours(-2));

        new SettingsStore(strayRoot).Load();

        Check("持ち主がおらず古い一時ファイルは片付ける", !File.Exists(oldStray));
        Check("新しい一時ファイルは残す（書き込み中かもしれない）", File.Exists(freshStray));
        Check("動いているインスタンスの一時ファイルは残す", File.Exists(ownStray));

        DeleteWithRetry(root);
    }

    /// 設定オブジェクトを辿って、値を持つ書き込み可能なプロパティだけを集める
    private static IEnumerable<(object Owner, PropertyInfo Property, string Path)> LeafProperties(
        object target, string path)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;

            var name = $"{path}.{property.Name}";

            if (IsSettingsObject(property.PropertyType))
            {
                var child = property.GetValue(target);
                if (child is not null)
                {
                    foreach (var leaf in LeafProperties(child, name)) yield return leaf;
                }

                continue;
            }

            if (!property.CanWrite || property.SetMethod?.IsPublic != true) continue;

            yield return (target, property, name);
        }
    }

    /// DispatcherTimer は回している間しか動かないので、指定時間だけメッセージを処理する
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer { Interval = duration };
        stop.Tick += (_, _) =>
        {
            stop.Stop();
            frame.Continue = false;
        };
        stop.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void TestCloneCompleteness()
    {
        Console.WriteLine();

        VerifyClone("ProcessingSettings", new ProcessingSettings(), s => s.Clone());
        VerifyClone("ExportSettings", new Soroe.Models.ExportSettings(), s => s.Clone());
    }

    private static void VerifyClone<T>(string label, T original, Func<T, T> clone)
        where T : notnull
    {
        var changed = new List<string>();
        var unchangeable = new List<string>();
        Mutate(original, label, changed, unchangeable);

        // 項目を足したときに実際に対象へ入っているかを目で確かめられるよう、名前を出す
        Console.WriteLine($"    {label} の対象: {string.Join(", ", changed)}");

        Check($"{label}: 全プロパティに既定と違う値を入れられた（{changed.Count} 項目）",
            unchangeable.Count == 0, string.Join(", ", unchangeable));

        var mismatches = new List<string>();
        Compare(original, clone(original), label, mismatches);

        Check($"{label}: Clone が全プロパティを写す", mismatches.Count == 0, string.Join(" / ", mismatches));
    }

    /// 設定クラスかどうか。入れ子になった Option 類は再帰的に辿る
    private static bool IsSettingsObject(Type type)
        => type.IsClass && type != typeof(string) && type.Assembly == typeof(ProcessingSettings).Assembly;

    private static void Mutate(object target, string path, List<string> changed, List<string> unchangeable)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;

            var name = $"{path}.{property.Name}";
            var before = property.GetValue(target);

            if (IsSettingsObject(property.PropertyType))
            {
                if (before is not null) Mutate(before, name, changed, unchangeable);
                continue;
            }

            if (!property.CanWrite || property.SetMethod?.IsPublic != true) continue;

            var applied = false;
            foreach (var candidate in Candidates(property.PropertyType, before))
            {
                property.SetValue(target, candidate);

                // Clamp する setter があるので、実際に変わったかを読み直して確かめる
                if (!Equals(property.GetValue(target), before))
                {
                    changed.Add(name);
                    applied = true;
                    break;
                }
            }

            if (!applied) unchangeable.Add(name);
        }
    }

    private static IEnumerable<object?> Candidates(Type type, object? current)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
        {
            yield return !(bool)(current ?? false);
        }
        else if (underlying == typeof(int))
        {
            var value = (int)(current ?? 0);
            yield return value + 7;
            yield return value - 7;
            yield return value + 1;
            yield return value - 1;
        }
        else if (underlying == typeof(double))
        {
            yield return (double)(current ?? 0d) + 1.5;
        }
        else if (underlying == typeof(string))
        {
            yield return $"clone-test-{Guid.NewGuid():N}";
        }
        else if (underlying.IsEnum)
        {
            foreach (var value in Enum.GetValues(underlying))
            {
                if (!Equals(value, current)) yield return value;
            }
        }
    }

    private static void Compare(object left, object right, string path, List<string> mismatches)
    {
        foreach (var property in left.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;

            var name = $"{path}.{property.Name}";
            var a = property.GetValue(left);
            var b = property.GetValue(right);

            if (IsSettingsObject(property.PropertyType))
            {
                if (a is not null && b is not null) Compare(a, b, name, mismatches);
                continue;
            }

            if (!Equals(a, b)) mismatches.Add($"{name}: {a} ≠ {b}");
        }
    }

    private static void TestResolveSize()
    {
        Console.WriteLine();

        var option = new ResizeOption { Enabled = true, LongestEdge = 1920 };

        Check("無効なら元の寸法のまま",
            new ResizeOption { LongestEdge = 800 }.ResolveSize(4000, 3000) == (4000, 3000));
        Check("横長は長辺が指定値ちょうど", option.ResolveSize(4000, 3000) == (1920, 1440),
            $"{option.ResolveSize(4000, 3000)}");
        Check("縦長でも長辺基準", option.ResolveSize(3000, 4000) == (1440, 1920),
            $"{option.ResolveSize(3000, 4000)}");
        Check("正方形", option.ResolveSize(5000, 5000) == (1920, 1920), $"{option.ResolveSize(5000, 5000)}");
        Check("ちょうど上限なら変えない", option.ResolveSize(1920, 1080) == (1920, 1080),
            $"{option.ResolveSize(1920, 1080)}");
        Check("上限より小さければ拡大しない", option.ResolveSize(1200, 900) == (1200, 900),
            $"{option.ResolveSize(1200, 900)}");

        // 縦横比が保たれること（丸めで 1px 以内）
        var (w, h) = option.ResolveSize(4032, 3024);
        var ratio = Math.Abs(((double)w / h) - (4032.0 / 3024.0));
        Check("縦横比が保たれる", ratio < 0.002 && w == 1920, $"{w}x{h} 比の差 {ratio:F5}");

        // 極端な縦横比。短辺が 0 になると Cv2.Resize が例外を投げるため、必ず 1 以上にする
        var tiny = new ResizeOption { Enabled = true, LongestEdge = ResizeOption.MinValue };
        foreach (var (srcWidth, srcHeight, expected) in new[]
                 {
                     (10000, 50, (16, 1)),
                     (20000, 3, (16, 1)),
                     (3, 20000, (1, 16)),
                     (10000, 1, (16, 1)),
                 })
        {
            var actual = tiny.ResolveSize(srcWidth, srcHeight);
            Check($"{srcWidth}x{srcHeight} を長辺 16 にしても 0 にならない", actual == expected, $"{actual}");
        }

        Check("極端に細長くても 1px を下回らない", option.ResolveSize(20000, 3) == (1920, 1), $"{option.ResolveSize(20000, 3)}");

        var clamped = new ResizeOption { LongestEdge = 999999 };
        Check("長辺の値はモデルが丸める", clamped.LongestEdge == ResizeOption.MaxValue, $"{clamped.LongestEdge}");
    }

    private static void TestResize()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_resize");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        // 階調のある画像。単色だと縮小の差が出ず、検証にならない
        var path = Path.Combine(root, "gradient.png");
        WriteGradient(path, 4000, 3000);

        var renderer = new ImageRenderer();
        var settings = new ProcessingSettings();
        settings.Resize.Enabled = true;
        settings.Resize.LongestEdge = 1920;

        // 書き出し（原寸・倍率 1.0）
        using (var full = renderer.Load(path, 0))
        {
            var bytes = renderer.Encode(full!, settings, full!.Scale, new EncodeSettings { Extension = ".png" });
            using var written = Cv2.ImDecode(bytes, ImreadModes.Color);
            Check("書き出し寸法が指定どおり（4000x3000 → 1920x1440）",
                written.Width == 1920 && written.Height == 1440, $"{written.Width}x{written.Height}");
        }

        // プレビュー（縮小済みの元画像・倍率 < 1）
        using (var preview = renderer.Load(path, 1600))
        {
            var scale = preview!.Scale;
            var rendered = renderer.Render(preview, settings, scale);

            // 期待値は「出力寸法 × 倍率」。倍率から元寸法を逆算していないことの確認でもある
            var expectedWidth = (int)Math.Round(1920 * scale);
            var expectedHeight = (int)Math.Round(1440 * scale);
            Check($"プレビュー寸法 == round(書き出し寸法 × {scale:F4})",
                rendered.PixelWidth == expectedWidth && rendered.PixelHeight == expectedHeight,
                $"{rendered.PixelWidth}x{rendered.PixelHeight} 期待 {expectedWidth}x{expectedHeight}");
        }

        // 上限より小さい画像は拡大されない
        var small = Path.Combine(root, "small.png");
        WriteGradient(small, 1200, 900);
        using (var source = renderer.Load(small, 0))
        {
            var bytes = renderer.Encode(source!, settings, source!.Scale, new EncodeSettings { Extension = ".png" });
            using var written = Cv2.ImDecode(bytes, ImreadModes.Color);
            Check("上限より小さい画像は拡大されない", written.Width == 1200 && written.Height == 900,
                $"{written.Width}x{written.Height}");
        }

        // 極端な縦横比を実際に通す。プレビュー側は「出力寸法 × 倍率」で
        // さらに小さくなるため、そこでも 0 にならないことを確かめる
        var thin = Path.Combine(root, "thin.png");
        WriteGradient(thin, 10000, 50);
        var tiny = new ProcessingSettings();
        tiny.Resize.Enabled = true;
        tiny.Resize.LongestEdge = ResizeOption.MinValue;
        try
        {
            using var full = renderer.Load(thin, 0);
            var bytes = renderer.Encode(full!, tiny, full!.Scale, new EncodeSettings { Extension = ".png" });
            using var written = Cv2.ImDecode(bytes, ImreadModes.Color);
            Check("10000x50 を長辺 16 で書き出せる（例外なし）",
                written.Width == 16 && written.Height == 1, $"{written.Width}x{written.Height}");

            using var preview = renderer.Load(thin, 1600);
            var rendered = renderer.Render(preview!, tiny, preview!.Scale);
            Check("同じ画像のプレビューでも 0px にならない",
                rendered.PixelWidth >= 1 && rendered.PixelHeight >= 1,
                $"{rendered.PixelWidth}x{rendered.PixelHeight}");
        }
        catch (Exception ex)
        {
            Check("極端な縦横比で例外が出ない", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        // リサイズ + 明るさの併用（適用順序 2 → 3）
        settings.Brightness.Enabled = true;
        settings.Brightness.Value = 40;
        using (var full = renderer.Load(path, 0))
        {
            var bytes = renderer.Encode(full!, settings, full!.Scale, new EncodeSettings { Extension = ".png" });
            using var written = Cv2.ImDecode(bytes, ImreadModes.Color);
            Check("リサイズと明るさを併用できる", written.Width == 1920 && written.Height == 1440,
                $"{written.Width}x{written.Height}");
        }

        settings.Brightness.Enabled = false;

        // プレビューと書き出しの内容が一致すること（2 段階縮小のため許容差あり）
        using (var full = renderer.Load(path, 0))
        using (var preview = renderer.Load(path, 1600))
        {
            var exported = renderer.Encode(full!, settings, full!.Scale, new EncodeSettings { Extension = ".png" });
            using var exportedMat = Cv2.ImDecode(exported, ImreadModes.Color);

            var rendered = renderer.Render(preview!, settings, preview!.Scale);

            // 書き出し結果をプレビューと同じ寸法まで落として比べる
            using var shrunk = new Mat();
            Cv2.Resize(exportedMat, shrunk, new OpenCvSharp.Size(rendered.PixelWidth, rendered.PixelHeight),
                interpolation: InterpolationFlags.Area);

            var (mean, max) = Difference(rendered, shrunk);
            Check($"プレビューと書き出しの内容が一致（平均差 {mean:F3} / 最大差 {max}）",
                mean <= MaxMeanDifference, $"閾値 {MaxMeanDifference}");
        }

        DeleteWithRetry(root);
    }

    /// 平均絶対差と最大差を返す。平均だけでは局所的な破綻を見逃すため最大も出す
    private static (double Mean, int Max) Difference(BitmapSource rendered, Mat other)
    {
        var bytesPerPixel = (rendered.Format.BitsPerPixel + 7) / 8;
        var stride = rendered.PixelWidth * bytesPerPixel;
        var buffer = new byte[stride * rendered.PixelHeight];
        rendered.CopyPixels(buffer, stride, 0);

        long total = 0;
        var count = 0;
        var max = 0;
        for (var y = 0; y < rendered.PixelHeight; y++)
        {
            for (var x = 0; x < rendered.PixelWidth; x++)
            {
                var o = (y * stride) + (x * bytesPerPixel);
                var expected = other.At<Vec3b>(y, x);
                foreach (var d in new[]
                         {
                             Math.Abs(buffer[o] - expected.Item0),
                             Math.Abs(buffer[o + 1] - expected.Item1),
                             Math.Abs(buffer[o + 2] - expected.Item2),
                         })
                {
                    total += d;
                    count++;
                    if (d > max) max = d;
                }
            }
        }

        return (count == 0 ? 0 : (double)total / count, max);
    }

    private static void WriteGradient(string path, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3);
        int rows = mat.Rows, cols = mat.Cols;
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                mat.Set(y, x, new Vec3b(
                    (byte)(x * 255 / Math.Max(1, cols - 1)),
                    (byte)(y * 255 / Math.Max(1, rows - 1)),
                    (byte)((x + y) % 256)));
            }
        }

        Cv2.ImEncode(Path.GetExtension(path).ToLowerInvariant(), mat, out var bytes);
        File.WriteAllBytes(path, bytes);
    }

    private static MainViewModel NewViewModel(IFolderPicker picker)
    {
        var renderer = new ImageRenderer();
        return new MainViewModel(
            new ProcessingSettings(),
            new Soroe.Models.ExportSettings(),
            renderer,
            new ImageExporter(renderer),
            picker,
            new StubExportDialog());
    }

    /// <summary>
    /// ダイアログの代わり。開かれたときに何をするかを差し替えられる。
    /// </summary>
    /// <remarks>
    /// 実際のダイアログは中で書き出しまで行うので、既定でも実行するようにしておく。
    /// </remarks>
    private sealed class StubExportDialog : IExportDialog
    {
        public bool RunExport { get; set; } = true;

        public int ShownCount { get; private set; }

        public Action<ExportDialogViewModel>? OnShown { get; set; }

        public void Show(ExportDialogViewModel viewModel)
        {
            ShownCount++;
            OnShown?.Invoke(viewModel);

            if (RunExport)
            {
                viewModel.RunCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            }
        }
    }

    /// プレビューと書き出しで結果が一致すること。ここが崩れると Soroe の売りが壊れる
    /// <summary>
    /// コントラストの効き方を、両端の画素値で固定する。
    /// </summary>
    /// <remarks>
    /// 倍率は 2^(c/100)。1 + c/100 に戻すと -100 で 0 倍になり、一面が中間の明るさに
    /// 潰れて画像が消える。設計上の判断なので、値そのもので押さえておく。
    /// </remarks>
    private static void TestToneCurve()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_tone");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        // 0 と 255 を含む横方向のグレー階調
        var path = Path.Combine(root, "ramp.png");
        using (var mat = new Mat(8, 256, MatType.CV_8UC3))
        {
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 256; x++)
                {
                    mat.Set(y, x, new Vec3b((byte)x, (byte)x, (byte)x));
                }
            }

            Cv2.ImEncode(".png", mat, out var png);
            File.WriteAllBytes(path, png);
        }

        var renderer = new ImageRenderer();

        // 入力 x に対する出力を返す
        int[] Apply(int contrast)
        {
            var settings = new ProcessingSettings();
            settings.Contrast.Enabled = true;
            settings.Contrast.Value = contrast;

            using var source = renderer.Load(path, 0);
            var encoded = renderer.Encode(source!, settings, source!.Scale, new EncodeSettings { Extension = ".png" });
            using var decoded = Cv2.ImDecode(encoded, ImreadModes.Color);

            var result = new int[256];
            for (var x = 0; x < 256; x++) result[x] = decoded.At<Vec3b>(4, x).Item0;
            return result;
        }

        var identity = Apply(0);
        Check("コントラスト 0 は素通り", Enumerable.Range(0, 256).All(x => identity[x] == x),
            $"0->{identity[0]} 255->{identity[255]}");

        // 2^(-1) = 0.5 倍。0 と 255 が中心 127.5 に向かって半分だけ寄る
        var down = Apply(-100);
        Check("コントラスト -100 は 0.5 倍（64〜191 に収まる）",
            down[0] == 64 && down[255] == 191, $"0->{down[0]} 255->{down[255]}");
        Check("コントラスト -100 でも階調が消えない",
            down.Distinct().Count() > 100, $"{down.Distinct().Count()} 段階");

        // 2^(1) = 2 倍。中心から離れた側は飽和する
        var up = Apply(100);
        Check("コントラスト 100 は 2 倍（両端が飽和）",
            up[0] == 0 && up[255] == 255 && up[64] == 0 && up[192] == 255,
            $"0->{up[0]} 64->{up[64]} 192->{up[192]} 255->{up[255]}");

        // 倍率が対称であること。中心からの距離が -100 で半分、+100 で 2 倍
        var half = Math.Abs(down[255] - 127.5);
        var whole = Math.Abs(identity[255] - 127.5);
        Check("負方向の倍率がちょうど半分", Math.Abs(half * 2 - whole) <= 1.0, $"{half} と {whole}");

        DeleteWithRetry(root);
    }

    /// <summary>
    /// グレースケールと二値化。相互作用と、彩度との地続きを確かめる。
    /// </summary>
    private static void TestMonochrome()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_mono");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        // 色の偏りが出るよう、3 チャンネルが別々に動く画像にする
        var path = Path.Combine(root, "color.png");
        using (var mat = new Mat(120, 160, MatType.CV_8UC3))
        {
            int rows = mat.Rows, cols = mat.Cols;
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    mat.Set(y, x, new Vec3b((byte)(x * 255 / cols), (byte)(y * 255 / rows), (byte)((x * y) % 256)));
                }
            }

            Cv2.ImEncode(".png", mat, out var png);
            File.WriteAllBytes(path, png);
        }

        var renderer = new ImageRenderer();

        byte[] Run(Action<ProcessingSettings> configure)
        {
            var settings = new ProcessingSettings();
            configure(settings);
            using var source = renderer.Load(path, 0);
            return renderer.Encode(source!, settings, source!.Scale, new EncodeSettings { Extension = ".png" });
        }

        // 束 1 で彩度を輝度補間にした根拠。ここが崩れると設計判断の前提が崩れる
        var saturationMinus100 = Run(s => { s.Saturation.Enabled = true; s.Saturation.Value = -100; });
        var grayscaleOn = Run(s => s.Grayscale.Enabled = true);
        Check("「彩度 -100」と「グレースケール ON」が全画素一致",
            saturationMinus100.SequenceEqual(grayscaleOn));

        // 吸収。二値化が ON なら、グレースケールの ON / OFF は結果を変えない
        var binarizeOnly = Run(s => s.Binarize.Enabled = true);
        var binarizeWithGray = Run(s => { s.Grayscale.Enabled = true; s.Binarize.Enabled = true; });
        Check("二値化 ON ならグレースケールの有無で結果が変わらない",
            binarizeOnly.SequenceEqual(binarizeWithGray));

        // 彩度は吸収されない。グレースケールと同じ形に見えるが別物である。
        //
        // 彩度は out = 元 × k + グレー × (1-k)。k <= 1 では出力が元とグレーの間に
        // 収まるので飽和せず、輝度は理屈どおり変わらない（残る差は 8bit の丸めだけ）。
        // k > 1 では 0 / 255 で頭打ちになり、飽和した画素の輝度が実際に変わる。
        // 順序が固定である以上これは仕様であって不具合ではない。
        double SaturationEffect(int value)
        {
            var withSaturation = Run(s =>
            {
                s.Saturation.Enabled = true;
                s.Saturation.Value = value;
                s.Binarize.Enabled = true;
            });

            using var a = Cv2.ImDecode(binarizeOnly, ImreadModes.Color);
            using var b = Cv2.ImDecode(withSaturation, ImreadModes.Color);
            using var diff = new Mat();
            Cv2.Absdiff(a, b, diff);
            return Cv2.CountNonZero(diff.Reshape(1)) / 3.0 / (a.Rows * a.Cols);
        }

        var lowered = SaturationEffect(-40);
        Check($"彩度を下げる方向は二値化にほぼ影響しない（差 {lowered * 100:F3}%、丸めのみ）",
            lowered < 0.005, $"{lowered * 100:F3}%");

        var raised = SaturationEffect(80);
        Check($"彩度を上げる方向は二値化に影響する（差 {raised * 100:F3}%、飽和で輝度が変わる）",
            raised > 0.005, $"{raised * 100:F3}%");

        // 出力が白と黒だけであること
        using (var binarized = Cv2.ImDecode(binarizeOnly, ImreadModes.Color))
        {
            var levels = new HashSet<int>();
            int rows = binarized.Rows, cols = binarized.Cols;
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    var p = binarized.At<Vec3b>(y, x);
                    levels.Add(p.Item0);
                    levels.Add(p.Item1);
                    levels.Add(p.Item2);
                }
            }

            Check("二値化の出力は 0 と 255 だけ",
                levels.All(v => v is 0 or 255) && levels.Count == 2, string.Join(",", levels.Order()));
        }

        // グレースケールは 3ch のまま（チェーンは常に 8bit 3ch）
        using (var gray = Cv2.ImDecode(grayscaleOn, ImreadModes.Unchanged))
        {
            Check("グレースケールでもチャンネル数は 3", gray.Channels() == 3, $"{gray.Channels()}ch");
        }

        CheckThresholdAgreement(renderer, root);
        DeleteWithRetry(root);
    }

    /// <summary>
    /// 二値化のしきい値が、プレビューと書き出しで同じ値になるかを確かめる。
    /// </summary>
    /// <remarks>
    /// 出力どうしを直接比べる方法は使えない。二値化した細かい模様を縮小すると中間色に
    /// なるため、しきい値が同じでも平均差が大きく出てしまう。
    /// <para>
    /// 代わりに<b>なだらかな階調の帯を仕込み、白黒が切り替わる位置から
    /// しきい値そのものを読み取る</b>。階調は縮小しても形が保たれるので、
    /// 読み取った値を両経路で直接比べられる。
    /// </para>
    /// <para>
    /// 帯の下には細かい雑音を置く。縮小で平均化されてヒストグラムが大きく変わるため、
    /// 経路ごとに大津を求める素朴な作りだと、ここで値が食い違う。
    /// </para>
    /// </remarks>
    private static void CheckThresholdAgreement(ImageRenderer renderer, string root)
    {
        // 長辺 4000。canonical へ 0.4 倍に縮むので、細い模様が十分に平均化される
        const int width = 4000;
        const int height = 3000;

        // 上の帯はしきい値を読み取るための階調。面積を抑えてヒストグラムへの影響を小さくする
        const int rampHeight = 240;

        var path = Path.Combine(root, "ramp_and_lines.png");
        using (var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(245)))
        {
            for (var y = 0; y < rampHeight; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var v = (byte)(x * 255 / (width - 1));
                    mat.Set(y, x, new Vec3b(v, v, v));
                }
            }

            // 下は白地に細い黒線（文書のスキャンに近い形）。
            // 原寸では 245 の山と 30 の小さな山、縮小すると線がぼけて中間色が増え、
            // ヒストグラムの形が変わる。経路ごとに大津を求めるとここで値が食い違う
            for (var y = rampHeight + 40; y < height - 40; y += 60)
            {
                for (var x = 40; x < width - 40; x += 5)
                {
                    if ((x / 5) % 7 == 0)
                    {
                        continue;
                    }

                    for (var t = 0; t < 3; t++)
                    {
                        mat.Set(y + t, x, new Vec3b(30, 30, 30));
                        mat.Set(y + t, x + 1, new Vec3b(30, 30, 30));
                    }
                }
            }

            Cv2.ImEncode(".png", mat, out var png);
            File.WriteAllBytes(path, png);
        }

        var settings = new ProcessingSettings();
        settings.Binarize.Enabled = true;

        using var full = renderer.Load(path, 0);
        using var preview = renderer.Load(path, ImageRenderer.CanonicalEdge);

        var exported = renderer.Encode(full!, settings, full!.Scale, new EncodeSettings { Extension = ".png" });
        using var exportedMat = Cv2.ImDecode(exported, ImreadModes.Color);
        var rendered = renderer.Render(preview!, settings, preview!.Scale);

        var fromExport = ReadThreshold(exportedMat);
        var fromPreview = ReadThreshold(rendered);

        Check($"二値化のしきい値がプレビューと書き出しで一致（{fromPreview} と {fromExport}）",
            Math.Abs(fromPreview - fromExport) <= 1, $"差 {Math.Abs(fromPreview - fromExport)} 階調");
    }

    /// 階調の帯で白へ切り替わる位置から、使われたしきい値を逆算する
    private static int ReadThreshold(Mat binarized)
    {
        // 階調の帯は上端から 8% ぶん。その内側を読む
        var y = binarized.Rows / 25;
        var cols = binarized.Cols;
        for (var x = 0; x < cols; x++)
        {
            if (binarized.At<Vec3b>(y, x).Item0 == 255)
            {
                return x * 255 / (cols - 1);
            }
        }

        return -1;
    }

    /// 同上。プレビュー側は BitmapSource で返る
    private static int ReadThreshold(BitmapSource binarized)
    {
        var bytesPerPixel = (binarized.Format.BitsPerPixel + 7) / 8;
        var stride = binarized.PixelWidth * bytesPerPixel;
        var buffer = new byte[stride * binarized.PixelHeight];
        binarized.CopyPixels(buffer, stride, 0);

        var y = binarized.PixelHeight / 25;
        for (var x = 0; x < binarized.PixelWidth; x++)
        {
            if (buffer[(y * stride) + (x * bytesPerPixel)] == 255)
            {
                return x * 255 / (binarized.PixelWidth - 1);
            }
        }

        return -1;
    }

    /// <summary>
    /// しきい値の元になる canonical 画像が、プレビュー経路と書き出し経路で同じ画素になるか。
    /// </summary>
    /// <remarks>
    /// 「同じ入力に同じ処理を掛けるから一致する」という説明は、canonical 画像そのものが
    /// 両経路で同一である場合にしか成り立たない。<c>Cv2.Resize</c> は倍率指定と寸法指定で
    /// 内部の係数の求め方が違い、割り切れない比では別の画素になる。
    /// <para>
    /// 壊れても実写では差が出ず、谷の狭いヒストグラムの画像でだけ症状が出るため、
    /// 通ったことが動いている証拠にならない種類の防御である。
    /// </para>
    /// </remarks>
    private static void TestCanonicalImage()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_canonical");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        var renderer = new ImageRenderer();

        // 1600 / 長辺 が割り切れない寸法をわざと混ぜる。割り切れる比だけだと
        // 縮小の書き方が違っても偶然一致してしまい、検出できない
        foreach (var (width, height) in new[]
                 {
                     (3000, 4000), (3000, 2001), (2999, 1777), (2551, 1699), (1601, 1201), (800, 600),
                 })
        {
            var path = Path.Combine(root, $"s_{width}x{height}.png");
            using (var mat = new Mat(height, width, MatType.CV_8UC3))
            {
                // 縮小の係数の違いが出るよう、細かい模様を入れる
                Cv2.Randu(mat, Scalar.All(0), Scalar.All(255));
                Cv2.ImEncode(".png", mat, out var png);
                File.WriteAllBytes(path, png);
            }

            using var preview = renderer.Load(path, ImageRenderer.CanonicalEdge);
            using var full = renderer.Load(path, 0);

            var fromPreview = renderer.EncodeCanonical(preview!);
            var fromFull = renderer.EncodeCanonical(full!);

            Check($"canonical 画像が両経路でバイト一致（{width}x{height}）",
                fromPreview.SequenceEqual(fromFull),
                $"{fromPreview.Length} バイトと {fromFull.Length} バイト");
        }

        DeleteWithRetry(root);
    }

    private static void TestRenderEncodeAgreement()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_agree");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        // 一様な色ではなく階調のある画像にして、全画素を突き合わせる
        var path = Path.Combine(root, "gradient.png");
        using (var mat = new Mat(120, 160, MatType.CV_8UC3))
        {
            int rows = mat.Rows, cols = mat.Cols;
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    mat.Set(y, x, new Vec3b((byte)(x % 256), (byte)(y % 256), (byte)((x + y) % 256)));
                }
            }

            Cv2.ImEncode(".png", mat, out var png);
            File.WriteAllBytes(path, png);
        }

        var renderer = new ImageRenderer();
        var settings = new ProcessingSettings();
        settings.Brightness.Enabled = true;
        settings.Contrast.Enabled = true;
        settings.Saturation.Enabled = true;

        // 明るさだけでは倍率の影響を受けないため、リサイズ有効の組み合わせも通す。
        // これを入れて初めて previewScale がこのテストに効いてくる
        foreach (var (value, contrast, saturation, maxEdge, resizeTo) in new[]
                 {
                     (0, 0, 0, 0, 0), (37, 0, 0, 0, 0), (-63, 0, 0, 0, 0), (37, 0, 0, 80, 0),
                     (0, 0, 0, 0, 100), (37, 0, 0, 0, 100), (37, 0, 0, 80, 100), (-20, 0, 0, 120, 64),

                     // コントラストと彩度。単独と、明るさ・リサイズとの組み合わせ
                     (0, 55, 0, 0, 0), (0, -100, 0, 0, 0), (0, 100, 0, 0, 0),
                     (0, 0, 73, 0, 0), (0, 0, -100, 0, 0), (0, 0, 100, 0, 0),
                     (37, 55, 73, 0, 0), (-63, -40, -55, 0, 0),
                     (37, 55, 73, 80, 100), (-20, -40, -55, 120, 64),
                 })
        {
            settings.Brightness.Value = value;
            settings.Contrast.Value = contrast;
            settings.Saturation.Value = saturation;
            settings.Resize.Enabled = resizeTo > 0;
            if (resizeTo > 0) settings.Resize.LongestEdge = resizeTo;

            using var source = renderer.Load(path, maxEdge);
            var rendered = renderer.Render(source!, settings, source!.Scale);

            var encoded = renderer.Encode(source, settings, source.Scale, new EncodeSettings { Extension = ".png" });
            using var decoded = Cv2.ImDecode(encoded, ImreadModes.Color);

            var label = $"明 {value,4} コン {contrast,4} 彩 {saturation,4}、倍率 {source.Scale:F2}、"
                + $"リサイズ {(resizeTo > 0 ? resizeTo.ToString() : "なし"),4}";
            if (rendered.PixelWidth != decoded.Width || rendered.PixelHeight != decoded.Height)
            {
                Check($"{label}: 寸法が一致", false,
                    $"{rendered.PixelWidth}x{rendered.PixelHeight} と {decoded.Width}x{decoded.Height}");
                continue;
            }

            Check($"{label}: 全画素が一致", SamePixels(rendered, decoded, out var detail), detail);
        }

        // グレースケールと二値化。値を持たないので組み合わせで回す
        settings.Brightness.Value = 0;
        settings.Contrast.Value = 0;
        settings.Saturation.Value = 0;

        foreach (var (gray, binarize, maxEdge, resizeTo) in new[]
                 {
                     (true, false, 0, 0), (false, true, 0, 0), (true, true, 0, 0),
                     (true, false, 80, 0), (false, true, 80, 0),
                     (true, false, 0, 100), (false, true, 0, 100), (true, true, 120, 64),
                 })
        {
            settings.Grayscale.Enabled = gray;
            settings.Binarize.Enabled = binarize;
            settings.Resize.Enabled = resizeTo > 0;
            if (resizeTo > 0) settings.Resize.LongestEdge = resizeTo;

            using var source = renderer.Load(path, maxEdge);
            var rendered = renderer.Render(source!, settings, source!.Scale);

            // 比較のため可逆な形式でエンコードする（JPEG では非可逆なので一致しない）
            var encoded = renderer.Encode(source, settings, source.Scale, new EncodeSettings { Extension = ".png" });
            using var decoded = Cv2.ImDecode(encoded, ImreadModes.Color);

            var label = $"グレー {(gray ? "ON " : "OFF")} 二値化 {(binarize ? "ON " : "OFF")}、"
                + $"倍率 {source.Scale:F2}、リサイズ {(resizeTo > 0 ? resizeTo.ToString() : "なし"),4}";
            if (rendered.PixelWidth != decoded.Width || rendered.PixelHeight != decoded.Height)
            {
                Check($"{label}: 寸法が一致", false, $"{rendered.PixelWidth}x{rendered.PixelHeight} と {decoded.Width}x{decoded.Height}");
                continue;
            }

            Check($"{label}: 全画素が一致", SamePixels(rendered, decoded, out var detail), detail);
        }

        Directory.Delete(root, true);
    }

    /// Render の結果（BGRA/BGR）と Encode→デコードの結果（BGR）を全画素比較する
    private static bool SamePixels(BitmapSource rendered, Mat decoded, out string detail)
    {
        var bytesPerPixel = (rendered.Format.BitsPerPixel + 7) / 8;
        var stride = rendered.PixelWidth * bytesPerPixel;
        var buffer = new byte[stride * rendered.PixelHeight];
        rendered.CopyPixels(buffer, stride, 0);

        for (var y = 0; y < rendered.PixelHeight; y++)
        {
            for (var x = 0; x < rendered.PixelWidth; x++)
            {
                var o = (y * stride) + (x * bytesPerPixel);
                var expected = decoded.At<Vec3b>(y, x);
                if (buffer[o] != expected.Item0 || buffer[o + 1] != expected.Item1 || buffer[o + 2] != expected.Item2)
                {
                    detail = $"({x},{y}) で {buffer[o]},{buffer[o + 1]},{buffer[o + 2]} と "
                        + $"{expected.Item0},{expected.Item1},{expected.Item2}";
                    return false;
                }
            }
        }

        detail = string.Empty;
        return true;
    }

    private static void TestExport()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_export");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);

        foreach (var name in new[] { "a.png", "b.png", "c.png" })
        {
            WriteFlat(Path.Combine(input, name), 20, 16, 100);
        }

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var processing = new ProcessingSettings();
        processing.Brightness.Enabled = true;
        processing.Brightness.Value = 50;

        var settings = new Soroe.Models.ExportSettings { Folder = output };
        var paths = Directory.GetFiles(input).OrderBy(p => p).ToArray();

        var reports = new List<ExportProgress>();
        var progress = new SyncProgress(reports.Add);
        var result = exporter.Export(paths, settings, processing, progress, CancellationToken.None);

        Check("3 件を書き出した", result.Exported == 3, $"{result.Exported}");
        Check("失敗なし", result.Failures.Count == 0, string.Join(" / ", result.Failures.Select(f => f.Message)));
        Check("中止されていない", !result.Canceled);
        Check("開始できている", result.AbortReason is null, result.AbortReason);
        Check("出力先に 3 件できた", Directory.GetFiles(output).Length == 3);
        Check("一時ファイルが残っていない", Directory.GetFiles(output, "*.soroe-tmp").Length == 0);
        Check("進捗が通知された", reports.Count >= 4, $"{reports.Count}");
        Check("最後の進捗が完了を示す", reports[^1].Completed == 3 && reports[^1].Total == 3,
            $"{reports[^1].Completed}/{reports[^1].Total}");

        // 進捗には元の名前と出力名の両方が載る
        var named = reports.Where(r => r.SourceFileName.Length > 0).ToArray();
        Check("進捗に元ファイル名が載る", named.Length == 3 && named.All(r => r.SourceFileName.EndsWith(".png")),
            string.Join(",", named.Select(r => r.SourceFileName)));
        Check("進捗に出力名が載る", named.All(r => r.OutputFileName.Length > 0),
            string.Join(",", named.Select(r => r.OutputFileName)));
        Check("進捗の出力名が一時ファイル名でない",
            named.All(r => !r.OutputFileName.Contains(".soroe-tmp")),
            string.Join(",", named.Select(r => r.OutputFileName)));
        Check("進捗の出力名は実際に作られたファイルと一致",
            named.All(r => File.Exists(Path.Combine(output, r.OutputFileName))),
            string.Join(",", named.Select(r => r.OutputFileName)));

        // 書き出した内容が、原寸で適用した結果と一致すること
        using (var written = Cv2.ImDecode(File.ReadAllBytes(Path.Combine(output, "a.png")), ImreadModes.Color))
        {
            Check("書き出した画素が調整後の値", written.Get<Vec3b>(8, 10).Item0 == 150,
                $"{written.Get<Vec3b>(8, 10).Item0}");
            Check("書き出しは原寸", written.Width == 20 && written.Height == 16, $"{written.Width}x{written.Height}");
        }

        // 同名衝突 → 連番
        var again = exporter.Export(paths, settings, processing, null, CancellationToken.None);
        Check("再実行しても上書きしない", again.Exported == 3 && Directory.GetFiles(output).Length == 6,
            $"{Directory.GetFiles(output).Length}");
        Check("連番が付く", File.Exists(Path.Combine(output, "a (2).png")));

        // 出力先に入力と同じフォルダを選んでも原本が壊れないこと。
        // 第 1 段は衝突時に必ず連番が付くため、安全ガード自体はここでは発動しない
        var guardDir = Path.Combine(root, "guard");
        Directory.CreateDirectory(guardDir);
        var guardFile = Path.Combine(guardDir, "same.png");
        WriteFlat(guardFile, 8, 8, 100);
        var before = File.ReadAllBytes(guardFile);
        var guardReports = new List<ExportProgress>();
        exporter.Export(
            [guardFile], new Soroe.Models.ExportSettings { Folder = guardDir }, processing,
            new SyncProgress(guardReports.Add), CancellationToken.None);
        Check("同じフォルダへ出しても原本が変わらない", File.ReadAllBytes(guardFile).SequenceEqual(before));
        Check("連番を付けた別ファイルとして書き出す", File.Exists(Path.Combine(guardDir, "same (2).png")));
        Check("スキップしていないので Skipped は立たない",
            guardReports.All(r => !r.Skipped), $"{guardReports.Count(r => r.Skipped)} 件");

        // 書き込めない出力先 → 開始せずに理由を返す
        var missing = Path.Combine(root, "not-exist");
        var aborted = exporter.Export(paths, new Soroe.Models.ExportSettings { Folder = missing }, processing, null, CancellationToken.None);
        Check("存在しない出力先は開始しない", aborted.AbortReason is not null, aborted.AbortReason);
        Check("開始しないので失敗リストも空", aborted.Failures.Count == 0 && aborted.Exported == 0);

        // 壊れたファイルがあっても全体は止まらない
        var broken = Path.Combine(input, "broken.png");
        File.WriteAllBytes(broken, new byte[] { 0x89, 0x50, 0x00, 0x01 });
        var mixedOut = Path.Combine(root, "mixed");
        Directory.CreateDirectory(mixedOut);
        var mixed = exporter.Export(
            Directory.GetFiles(input).OrderBy(p => p).ToArray(),
            new Soroe.Models.ExportSettings { Folder = mixedOut }, processing, null, CancellationToken.None);
        Check("壊れた 1 件で止まらない", mixed.Exported == 3 && mixed.Failures.Count == 1,
            $"書き出し {mixed.Exported} / 失敗 {mixed.Failures.Count}");
        Check("失敗したファイル名が分かる", mixed.Failures.Count == 1 && mixed.Failures[0].SourcePath.EndsWith("broken.png"));

        // キャンセル
        var cancelOut = Path.Combine(root, "cancel");
        Directory.CreateDirectory(cancelOut);
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            var canceled = exporter.Export(
                paths, new Soroe.Models.ExportSettings { Folder = cancelOut }, processing, null, cts.Token);
            Check("開始直後の中止で 0 件", canceled.Canceled && canceled.Exported == 0,
                $"canceled={canceled.Canceled} exported={canceled.Exported}");
            Check("中止時にファイルを残さない", Directory.GetFiles(cancelOut).Length == 0);
        }

        DeleteWithRetry(root);
    }

    private static void TestFormats()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_format");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        Directory.CreateDirectory(input);

        // ノイズ画像。単色だと品質を変えてもサイズが変わらず検証にならない
        var jpg = Path.Combine(input, "photo.jpg");
        var upper = Path.Combine(input, "PHOTO.PNG");
        WriteNoise(jpg, 400, 400);
        WriteNoise(upper, 400, 400);

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var processing = new ProcessingSettings();
        var resolver = new OutputPathResolver();

        // 拡張子の決定（OutputPathResolver の担当）
        var folder = Path.Combine(root, "resolve");
        Directory.CreateDirectory(folder);
        foreach (var (format, expected) in new[]
                 {
                     (ExportFormat.Jpeg, ".jpg"), (ExportFormat.Png, ".png"),
                     (ExportFormat.Bmp, ".bmp"), (ExportFormat.WebP, ".webp"),
                 })
        {
            var resolved = Resolve(resolver, jpg, new ExportSettings { Folder = folder, Format = format });
            Check($"形式 {format} の拡張子は {expected}", Path.GetExtension(resolved) == expected,
                Path.GetExtension(resolved));
        }

        Check("元の形式を維持は元の拡張子を引き継ぐ",
            Path.GetExtension(Resolve(resolver, jpg, new ExportSettings { Folder = folder })) == ".jpg");
        Check("元の形式を維持は大文字もそのまま",
            Path.GetExtension(Resolve(resolver, upper, new ExportSettings { Folder = folder })) == ".PNG",
            Path.GetExtension(Resolve(resolver, upper, new ExportSettings { Folder = folder })));

        // 実際に各形式で書き出せること
        foreach (var (format, expected) in new[]
                 {
                     (ExportFormat.Jpeg, ".jpg"), (ExportFormat.Png, ".png"),
                     (ExportFormat.Bmp, ".bmp"), (ExportFormat.WebP, ".webp"),
                 })
        {
            var output = Path.Combine(root, $"out_{format}");
            Directory.CreateDirectory(output);
            var result = exporter.Export(
                [jpg], new ExportSettings { Folder = output, Format = format }, processing, null, CancellationToken.None);
            var files = Directory.GetFiles(output);
            Check($"{format} で書き出せる",
                result.Exported == 1 && files.Length == 1 && Path.GetExtension(files[0]) == expected,
                result.Failures.Count > 0 ? result.Failures[0].Message : $"{files.Length} 件");
        }

        // 大文字の拡張子でもエンコードできる（.PNG をそのまま渡している）
        var keepOut = Path.Combine(root, "out_keep");
        Directory.CreateDirectory(keepOut);
        var keep = exporter.Export(
            [upper], new ExportSettings { Folder = keepOut }, processing, null, CancellationToken.None);
        Check("大文字の拡張子でも書き出せる", keep.Exported == 1,
            keep.Failures.Count > 0 ? keep.Failures[0].Message : "-");

        // 品質でサイズが変わること
        long Size(ExportFormat format, Action<ExportSettings> configure)
        {
            var dir = Path.Combine(root, $"q_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            var settings = new ExportSettings { Folder = dir, Format = format };
            configure(settings);
            exporter.Export([jpg], settings, processing, null, CancellationToken.None);
            return new FileInfo(Directory.GetFiles(dir)[0]).Length;
        }

        var jpegLow = Size(ExportFormat.Jpeg, s => s.JpegQuality = 20);
        var jpegHigh = Size(ExportFormat.Jpeg, s => s.JpegQuality = 95);
        Check($"JPEG 品質でサイズが変わる（20: {jpegLow:N0} / 95: {jpegHigh:N0} bytes）", jpegLow < jpegHigh);

        var webpLow = Size(ExportFormat.WebP, s => s.WebPQuality = 20);
        var webpHigh = Size(ExportFormat.WebP, s => s.WebPQuality = 95);
        var webpLossless = Size(ExportFormat.WebP, s => s.WebPLossless = true);
        Check($"WebP 品質でサイズが変わる（20: {webpLow:N0} / 95: {webpHigh:N0} bytes）", webpLow < webpHigh);
        Check($"可逆のほうが大きい（可逆: {webpLossless:N0} bytes）", webpLossless > webpHigh);

        // 可逆なら元の画素と完全一致すること
        var losslessDir = Path.Combine(root, "lossless");
        Directory.CreateDirectory(losslessDir);
        exporter.Export(
            [upper],
            new ExportSettings { Folder = losslessDir, Format = ExportFormat.WebP, WebPLossless = true },
            processing, null, CancellationToken.None);
        using (var original = Cv2.ImDecode(File.ReadAllBytes(upper), ImreadModes.Color))
        using (var written = Cv2.ImDecode(File.ReadAllBytes(Directory.GetFiles(losslessDir)[0]), ImreadModes.Color))
        using (var diff = new Mat())
        {
            Cv2.Absdiff(original, written, diff);
            using var gray = new Mat();
            Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
            Check("WebP 可逆は元の画素と完全一致", Cv2.CountNonZero(gray) == 0, $"{Cv2.CountNonZero(gray)} 画素が相違");
        }

        // 可逆にチェックが入っていれば品質の数値は無視される
        var ignored = new ExportSettings { WebPQuality = 30, WebPLossless = true };
        Check("可逆時は 101 が渡る", ignored.EffectiveWebPQuality == ExportSettings.LosslessWebPQuality,
            $"{ignored.EffectiveWebPQuality}");
        ignored.WebPLossless = false;
        Check("可逆を外すと元の品質に戻る", ignored.EffectiveWebPQuality == 30, $"{ignored.EffectiveWebPQuality}");

        DeleteWithRetry(root);
    }

    private static void TestTransparency()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_alpha");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        Directory.CreateDirectory(input);

        // 3 種類の領域を作る:
        //   x < 30       不透明の赤              -> そのまま赤
        //   30 <= x < 60 完全透明・下は黒        -> 白になるはず
        //   60 <= x      半透明(50%)・下は黒     -> 黒と白の中間になるはず
        var source = Path.Combine(input, "alpha.png");
        using (var mat = new Mat(20, 90, MatType.CV_8UC4))
        {
            for (var y = 0; y < 20; y++)
            {
                for (var x = 0; x < 90; x++)
                {
                    var v = x < 30 ? new Vec4b(0, 0, 255, 255)
                        : x < 60 ? new Vec4b(0, 0, 0, 0)
                        : new Vec4b(0, 0, 0, 128);
                    mat.Set(y, x, v);
                }
            }

            Cv2.ImEncode(".png", mat, out var bytes);
            File.WriteAllBytes(source, bytes);
        }

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var processing = new ProcessingSettings();

        foreach (var format in new[] { ExportFormat.Png, ExportFormat.Bmp, ExportFormat.WebP, ExportFormat.Jpeg })
        {
            var output = Path.Combine(root, $"out_{format}");
            Directory.CreateDirectory(output);
            exporter.Export(
                [source],
                new ExportSettings { Folder = output, Format = format, JpegQuality = 100, WebPLossless = true },
                processing, null, CancellationToken.None);

            var files = Directory.GetFiles(output);
            if (files.Length != 1)
            {
                Check($"{format}: 書き出せる", false, $"{files.Length} 件");
                continue;
            }

            using var written = Cv2.ImDecode(File.ReadAllBytes(files[0]), ImreadModes.Unchanged);
            var opaque = written.At<Vec3b>(10, 10);
            var transparent = written.At<Vec3b>(10, 45);
            var half = written.At<Vec3b>(10, 75);

            // JPEG は非可逆なので少しずれる。許容幅を持たせる
            var tolerance = format == ExportFormat.Jpeg ? 6 : 0;
            Check($"{format}: 透明部分が白になる",
                Near(transparent, 255, 255, 255, tolerance), $"{transparent}");
            Check($"{format}: 半透明は白と混ざる（黒 50% → 約 127）",
                Near(half, 127, 127, 127, tolerance + 2), $"{half}");
            Check($"{format}: 不透明部分はそのまま",
                Near(opaque, 0, 0, 255, tolerance), $"{opaque}");
            Check($"{format}: 出力にアルファは含まれない", written.Channels() == 3, $"{written.Channels()}ch");
        }

        DeleteWithRetry(root);
    }

    private static bool Near(Vec3b actual, int b, int g, int r, int tolerance)
        => Math.Abs(actual.Item0 - b) <= tolerance
            && Math.Abs(actual.Item1 - g) <= tolerance
            && Math.Abs(actual.Item2 - r) <= tolerance;

    private static void TestBitDepth()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_depth");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);

        // 16bit PNG。値 30000/65535 は 8bit で約 117 になる
        var source = Path.Combine(input, "深い.png");
        using (var mat = new Mat(16, 20, MatType.CV_16UC3, new Scalar(30000, 30000, 30000)))
        {
            Cv2.ImEncode(".png", mat, out var bytes);
            File.WriteAllBytes(source, bytes);
        }

        using (var check = Cv2.ImDecode(File.ReadAllBytes(source), ImreadModes.Unchanged))
        {
            Check("検証用ファイルが 16bit である", check.Depth() == (int)MatType.CV_16U, $"depth={check.Depth()}");
        }

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);

        // 明るさ（LUT）とリサイズを併用。LUT は 8bit 前提なので、ここで落ちなければ正規化できている
        var processing = new ProcessingSettings();
        processing.Brightness.Enabled = true;
        processing.Brightness.Value = 20;
        processing.Resize.Enabled = true;
        processing.Resize.LongestEdge = 16;

        var result = exporter.Export(
            [source], new ExportSettings { Folder = output, Format = ExportFormat.Png },
            processing, null, CancellationToken.None);

        Check("16bit PNG を調整して書き出せる", result.Exported == 1,
            result.Failures.Count > 0 ? result.Failures[0].Message : "-");

        if (result.Exported == 1)
        {
            using var written = Cv2.ImDecode(File.ReadAllBytes(Directory.GetFiles(output)[0]), ImreadModes.Unchanged);
            Check("出力は 8bit に落ちている", written.Depth() == (int)MatType.CV_8U, $"depth={written.Depth()}");
            Check("リサイズも効いている", written.Width == 16 && written.Height <= 16,
                $"{written.Width}x{written.Height}");

            // 30000/65535*255 = 116.7 -> 117、明るさ +20 で 137 前後
            var value = written.At<Vec3b>(5, 5).Item0;
            Check("画素値が 8bit として妥当（約 137）", Math.Abs(value - 137) <= 2, $"{value}");
        }

        // プレビュー経路も通ること
        using (var previewSource = renderer.Load(source, 1600))
        {
            Check("プレビューでも 16bit を読める", previewSource is not null);
            if (previewSource is not null)
            {
                var bitmap = renderer.Render(previewSource, processing, previewSource.Scale);
                Check("プレビューを生成できる", bitmap.PixelWidth > 0);
            }
        }

        DeleteWithRetry(root);
    }

    private static void WriteNoise(string path, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3);
        Cv2.Randu(mat, Scalar.All(0), Scalar.All(255));
        Cv2.ImEncode(Path.GetExtension(path).ToLowerInvariant(), mat, out var bytes);
        File.WriteAllBytes(path, bytes);
    }

    /// ImWrite を避けた理由そのものの確認。CP932 外の文字を含むパスへ書けること
    private static void TestNonAsciiOutput()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_nonascii");
        if (Directory.Exists(root)) Directory.Delete(root, true);

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var processing = new ProcessingSettings();
        processing.Brightness.Enabled = true;
        processing.Brightness.Value = 50;

        foreach (var folderName in new[] { "출력폴더", "出力🌸フォルダ", "Ausgabeordner-Ä" })
        {
            var input = Path.Combine(root, $"in_{folderName}");
            var output = Path.Combine(root, folderName);
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(output);

            // 元ファイル名にも CP932 外の文字を入れる
            var sourcePath = Path.Combine(input, $"사진_{folderName}.png");
            WriteFlat(sourcePath, 24, 18, 100);

            var result = exporter.Export(
                [sourcePath],
                new Soroe.Models.ExportSettings { Folder = output },
                processing,
                null,
                CancellationToken.None);

            var written = Directory.GetFiles(output);
            var ok = result.Exported == 1 && result.Failures.Count == 0 && written.Length == 1;
            Check($"「{folderName}」へ書き出せる", ok,
                result.Failures.Count > 0 ? result.Failures[0].Message : $"{written.Length} 件");

            if (ok)
            {
                using var decoded = Cv2.ImDecode(File.ReadAllBytes(written[0]), ImreadModes.Color);
                Check($"「{folderName}」の内容が正しい", decoded.At<Vec3b>(9, 12).Item0 == 150,
                    $"{decoded.At<Vec3b>(9, 12).Item0}");
            }
        }

        // ViewModel 経由（画面から実行したときと同じ道筋）でも通ること
        var vmInput = Path.Combine(root, "화면_입력🌸");
        var vmOutput = Path.Combine(root, "화면_출력🌸");
        Directory.CreateDirectory(vmInput);
        Directory.CreateDirectory(vmOutput);
        for (var i = 1; i <= 3; i++)
        {
            WriteFlat(Path.Combine(vmInput, $"사진_{i}🌸.png"), 24, 18, 100);
        }

        var vm = NewViewModel(new StubFolderPicker(vmInput));
        vm.AddFolderCommand.Execute(null);
        vm.Output.Folder = vmOutput;
        vm.Settings.Brightness.Enabled = true;
        vm.Settings.Brightness.Value = 50;
        vm.ExportCommand.Execute(null);
        Check("ViewModel 経由でも非 ASCII パスへ書き出せる",
            Directory.GetFiles(vmOutput).Length == 3 && !vm.StatusMessage.Contains("失敗"),
            $"{Directory.GetFiles(vmOutput).Length} 件 / {vm.StatusMessage}");

        DeleteWithRetry(root);
    }

    /// 1 件だけ解決する簡略版
    private static string Resolve(OutputPathResolver resolver, string source, Soroe.Models.ExportSettings settings)
        => resolver.Resolve(source, settings, 0, 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// 出力ファイル名の決め方
    private static void TestNaming()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_naming");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);

        var resolver = new OutputPathResolver();
        var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var source = Path.Combine(input, "IMG_0001.jpg");

        string Name(Soroe.Models.ExportSettings s, int index = 0, int total = 1)
            => Path.GetFileName(resolver.Resolve(source, s, index, total, empty));

        Check("そのまま", Name(new() { Folder = output }) == "IMG_0001.jpg",
            Name(new() { Folder = output }));

        Check("プレフィックスのみ",
            Name(new() { Folder = output, Naming = FileNaming.PrefixSuffix, Prefix = "小_" }) == "小_IMG_0001.jpg",
            Name(new() { Folder = output, Naming = FileNaming.PrefixSuffix, Prefix = "小_" }));

        Check("サフィックスのみ",
            Name(new() { Folder = output, Naming = FileNaming.PrefixSuffix, Suffix = "_small" }) == "IMG_0001_small.jpg",
            Name(new() { Folder = output, Naming = FileNaming.PrefixSuffix, Suffix = "_small" }));

        Check("前後の両方",
            Name(new() { Folder = output, Naming = FileNaming.PrefixSuffix, Prefix = "a", Suffix = "b" }) == "aIMG_0001b.jpg",
            Name(new() { Folder = output, Naming = FileNaming.PrefixSuffix, Prefix = "a", Suffix = "b" }));

        // 連番。桁数は総件数に合わせる（最小 3 桁）
        var sequence = new Soroe.Models.ExportSettings { Folder = output, Naming = FileNaming.Sequence };
        Check("連番 9 件は 3 桁", Name(sequence, 0, 9) == "photo_001.jpg", Name(sequence, 0, 9));
        Check("連番 211 件も 3 桁", Name(sequence, 210, 211) == "photo_211.jpg", Name(sequence, 210, 211));
        Check("連番 1500 件は 4 桁", Name(sequence, 0, 1500) == "photo_0001.jpg", Name(sequence, 0, 1500));
        Check("連番は 1 から始まる", Name(sequence, 0, 10) == "photo_001.jpg", Name(sequence, 0, 10));

        var noBase = new Soroe.Models.ExportSettings
        {
            Folder = output, Naming = FileNaming.Sequence, SequenceBaseName = "",
        };
        Check("ベース名が空なら区切りも付かない", Name(noBase, 0, 10) == "001.jpg", Name(noBase, 0, 10));

        // 形式を変えると拡張子も変わる
        Check("連番でも形式に従う",
            Name(new() { Folder = output, Naming = FileNaming.Sequence, Format = ExportFormat.WebP }, 0, 10) == "photo_001.webp",
            Name(new() { Folder = output, Naming = FileNaming.Sequence, Format = ExportFormat.WebP }, 0, 10));

        // 不正文字は setter が落とす
        var dirty = new Soroe.Models.ExportSettings
        {
            Prefix = "a\\b/c:d*e?f\"g<h>i|j",
            Suffix = "x/y",
            SequenceBaseName = "p*q",
        };
        Check("プレフィックスの不正文字を除去", dirty.Prefix == "abcdefghij", dirty.Prefix);
        Check("サフィックスの不正文字を除去", dirty.Suffix == "xy", dirty.Suffix);
        Check("ベース名の不正文字を除去", dirty.SequenceBaseName == "pq", dirty.SequenceBaseName);

        // 末尾の空白とドットは Windows が許さないので落とす
        var trailing = new Soroe.Models.ExportSettings
        {
            Folder = output, Naming = FileNaming.PrefixSuffix, Suffix = " .",
        };
        Check("末尾の空白とドットを落とす", Name(trailing) == "IMG_0001.jpg", Name(trailing));

        Directory.Delete(root, true);
    }

    /// 同名衝突の扱い
    private static void TestCollision()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_collision");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var processing = new ProcessingSettings();

        var a = Path.Combine(input, "IMG_0001.png");
        WriteFlat(a, 16, 12, 100);

        // 上書きオフ: 既存があれば (2)、さらにあれば (3)
        var keep = new Soroe.Models.ExportSettings { Folder = output };
        exporter.Export([a], keep, processing, null, CancellationToken.None);
        exporter.Export([a], keep, processing, null, CancellationToken.None);
        exporter.Export([a], keep, processing, null, CancellationToken.None);
        Check("上書きオフは別名で増える",
            File.Exists(Path.Combine(output, "IMG_0001.png"))
                && File.Exists(Path.Combine(output, "IMG_0001 (2).png"))
                && File.Exists(Path.Combine(output, "IMG_0001 (3).png")),
            string.Join(",", Directory.GetFiles(output).Select(Path.GetFileName)));

        // 上書きオン: 既存を置き換えるので増えない
        var overwriteDir = Path.Combine(root, "ow");
        Directory.CreateDirectory(overwriteDir);
        var overwrite = new Soroe.Models.ExportSettings { Folder = overwriteDir, Overwrite = true };
        exporter.Export([a], overwrite, processing, null, CancellationToken.None);
        exporter.Export([a], overwrite, processing, null, CancellationToken.None);
        Check("上書きオンは増えない", Directory.GetFiles(overwriteDir).Length == 1,
            string.Join(",", Directory.GetFiles(overwriteDir).Select(Path.GetFileName)));
        Check("一時ファイルが残らない", Directory.GetFiles(overwriteDir, "*.soroe-tmp").Length == 0);

        // 同一実行内の衝突は、上書きオンでも別名にする
        var multiIn1 = Path.Combine(root, "m1");
        var multiIn2 = Path.Combine(root, "m2");
        var multiOut = Path.Combine(root, "mout");
        Directory.CreateDirectory(multiIn1);
        Directory.CreateDirectory(multiIn2);
        Directory.CreateDirectory(multiOut);
        var s1 = Path.Combine(multiIn1, "同じ名前.png");
        var s2 = Path.Combine(multiIn2, "同じ名前.png");
        WriteFlat(s1, 16, 12, 60);
        WriteFlat(s2, 16, 12, 200);

        var multi = new Soroe.Models.ExportSettings
        {
            Folder = multiOut, Overwrite = true, Naming = FileNaming.PrefixSuffix, Prefix = "小_",
        };
        var multiResult = exporter.Export([s1, s2], multi, processing, null, CancellationToken.None);
        Check("同一実行内の衝突は上書きせず別名にする",
            multiResult.Exported == 2 && Directory.GetFiles(multiOut).Length == 2,
            $"書き出し {multiResult.Exported} / ファイル {Directory.GetFiles(multiOut).Length}");

        // 安全ガード: 出力先が「リスト内の別の原本」と一致する場合
        var guardDir = Path.Combine(root, "guard");
        Directory.CreateDirectory(guardDir);
        var plain = Path.Combine(guardDir, "IMG_0001.png");
        var prefixed = Path.Combine(guardDir, "小_IMG_0001.png");
        WriteFlat(plain, 16, 12, 100);
        WriteFlat(prefixed, 16, 12, 200);
        var before = File.ReadAllBytes(prefixed);

        var guard = new Soroe.Models.ExportSettings
        {
            Folder = guardDir, Overwrite = true, Naming = FileNaming.PrefixSuffix, Prefix = "小_",
        };
        var guardResult = exporter.Export([plain, prefixed], guard, processing, null, CancellationToken.None);
        Check("別の原本を潰さない（内容が変わらない）",
            File.ReadAllBytes(prefixed).SequenceEqual(before),
            $"書き出し {guardResult.Exported} / スキップ {guardResult.SkippedSameAsSource}");
        Check("その 1 枚はスキップとして数える", guardResult.SkippedSameAsSource >= 1,
            $"{guardResult.SkippedSameAsSource}");

        // 全件スキップ: 同フォルダ・そのまま・上書き
        var allDir = Path.Combine(root, "all");
        Directory.CreateDirectory(allDir);
        var p1 = Path.Combine(allDir, "a.png");
        var p2 = Path.Combine(allDir, "b.png");
        WriteFlat(p1, 8, 8, 100);
        WriteFlat(p2, 8, 8, 100);
        var allSettings = new Soroe.Models.ExportSettings { Folder = allDir, Overwrite = true };
        var plan = exporter.Plan([p1, p2], allSettings);
        Check("全件スキップを見積もりが検出する", plan.IsAllSkipped && plan.SkippedCount == 2,
            $"skipped={plan.SkippedCount}/{plan.Total}");

        var allResult = exporter.Export([p1, p2], allSettings, processing, null, CancellationToken.None);
        Check("見積もりと実際が一致する", allResult.SkippedSameAsSource == plan.SkippedCount,
            $"{allResult.SkippedSameAsSource} と {plan.SkippedCount}");

        // 上書きオフなら全件スキャンをせず、件数は 0
        var offPlan = exporter.Plan([p1, p2], new Soroe.Models.ExportSettings { Folder = allDir });
        Check("上書きオフなら件数は 0", offPlan.OverwriteCount == 0 && offPlan.SkippedCount == 0
            && !offPlan.IsAllSkipped, $"{offPlan}");

        // 上書き件数の見積もりが実際と合う
        var countIn = Path.Combine(root, "cin");
        var countOut = Path.Combine(root, "cout");
        Directory.CreateDirectory(countIn);
        Directory.CreateDirectory(countOut);
        var files = new List<string>();
        for (var i = 1; i <= 5; i++)
        {
            var path = Path.Combine(countIn, $"c{i}.png");
            WriteFlat(path, 8, 8, 100);
            files.Add(path);
        }

        var countSettings = new Soroe.Models.ExportSettings { Folder = countOut, Overwrite = true };
        exporter.Export(files.Take(3).ToArray(), countSettings, processing, null, CancellationToken.None);
        var countPlan = exporter.Plan(files, countSettings);
        Check("上書き件数の見積もり（3 件）", countPlan.OverwriteCount == 3, $"{countPlan.OverwriteCount}");

        DeleteWithRetry(root);
    }

    /// <summary>
    /// 上書きへ切り替えた直後、見積もりが届く前に実行を押した場合。
    /// </summary>
    /// <remarks>
    /// 最も危ない瞬間。古い見積もり（上書きオフ時のもの）は上書き件数 0 なので、
    /// そちらで「確認するものが無い」と判断すると、実際には全件が上書きされる。
    /// 判定は必ず押した時点で確定させた最新の見積もりで行う必要がある。
    /// </remarks>
    private static async Task TestStalePlanGuard()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_stale");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);

        // 出力先に同名のファイルを置いておく。中身で区別できるようにする
        var paths = new List<string>();
        for (var i = 1; i <= 5; i++)
        {
            var name = $"IMG_{i}.png";
            WriteFlat(Path.Combine(input, name), 16, 12, 100);
            WriteFlat(Path.Combine(output, name), 16, 12, 50);
            paths.Add(Path.Combine(input, name));
        }

        var existing = Directory.GetFiles(output).ToDictionary(f => f, File.ReadAllBytes);

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var vm = new ExportDialogViewModel(
            new Soroe.Models.ExportSettings { Folder = output },
            new ProcessingSettings(),
            paths,
            exporter,
            new StubFolderPicker(output));

        // 上書きオフの見積もりが届くのを待つ（この時点の上書き件数は 0）
        SpinUntil(() => vm.OutputPathPreview.Contains("IMG_1"), TimeSpan.FromSeconds(5));
        Check("切り替え前の上書き件数は 0", vm.OverwriteCount == 0, $"{vm.OverwriteCount}");

        // 上書きに切り替えた直後、見積もり（250ms 待ち）が届く前に実行を押す
        vm.Output.Overwrite = true;
        await vm.RunCommand.ExecuteAsync(null);

        Check("古い見積もりのまま実行しない", vm.RecheckNotice.Length > 0, vm.RecheckNotice);
        Check("既存ファイルが 1 バイトも変わっていない",
            existing.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)),
            "上書きされてしまった");
        Check("上書き件数が最新に更新される", vm.OverwriteCount == 5, $"{vm.OverwriteCount}");

        // 内容を確認したうえで押し直せば実行される
        await vm.RunCommand.ExecuteAsync(null);
        Check("押し直せば実行される", vm.ResultMessage.Contains("5 件を書き出しました"), vm.ResultMessage);
        Check("今度は実際に上書きされている",
            existing.Any(pair => !File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)));

        DeleteWithRetry(root);
    }

    /// <summary>
    /// 見積もりと結果の排他、および結果表示中に実行を押した場合。
    /// </summary>
    /// <remarks>
    /// 見積もりは「今の設定でもう一度押したらこうなる」という予告なので、
    /// 結果と並べると直前の実行の報告に見える。排他にしたうえで、
    /// <b>予告を見せずに上書きが実行される経路が無いこと</b>を確かめる。
    /// </remarks>
    private static async Task TestEstimateAndResultAreExclusive()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_exclusive");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);

        var paths = new List<string>();
        for (var i = 1; i <= 3; i++)
        {
            var name = $"IMG_{i}.png";
            WriteFlat(Path.Combine(input, name), 16, 12, 100);
            WriteFlat(Path.Combine(output, name), 16, 12, 50);   // 上書き対象を置いておく
            paths.Add(Path.Combine(input, name));
        }

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var vm = new ExportDialogViewModel(
            new Soroe.Models.ExportSettings { Folder = output, Overwrite = true },
            new ProcessingSettings(),
            paths,
            exporter,
            new StubFolderPicker(output));

        // 見積もりが届くのを待つ。予告を読んだ状態にする
        SpinUntil(() => vm.OverwriteCount == 3, TimeSpan.FromSeconds(5));
        Check("実行前は見積もりだけ出る", vm.ShowEstimate && !vm.ShowResult,
            $"estimate={vm.ShowEstimate} result={vm.ShowResult}");
        Check("予告の件数が出ている", vm.OverwriteCount == 3, $"{vm.OverwriteCount}");

        // 予告を読んだうえでの実行は通る
        await vm.RunCommand.ExecuteAsync(null);
        Check("予告を読んでいれば実行できる", vm.ResultMessage.Contains("3 件を書き出しました"), vm.ResultMessage);
        Check("実行後は結果だけ出る", vm.ShowResult && !vm.ShowEstimate,
            $"estimate={vm.ShowEstimate} result={vm.ShowResult}");

        // 結果表示中にもう一度押す。予告を見せていないので実行してはいけない。
        // 同じ内容を書き直すとバイト列は変わらないので、書き込み時刻で判定する
        Thread.Sleep(50);
        var before = Directory.GetFiles(output).ToDictionary(f => f, f => File.GetLastWriteTimeUtc(f));
        await vm.RunCommand.ExecuteAsync(null);

        Check("結果表示中の実行は一旦止まる", vm.RecheckNotice.Length > 0, vm.RecheckNotice);
        Check("そのとき書き出しは起きない",
            before.All(pair => File.GetLastWriteTimeUtc(pair.Key) == pair.Value),
            "ファイルが書き換えられた");
        Check("結果も更新されない", vm.ResultMessage.Length == 0, vm.ResultMessage);
        Check("見積もりの表示に戻る", vm.ShowEstimate && !vm.ShowResult,
            $"estimate={vm.ShowEstimate} result={vm.ShowResult}");
        Check("予告の件数が読める", vm.OverwriteCount == 3, $"{vm.OverwriteCount}");

        // 予告を読んだうえで押し直せば実行される
        await vm.RunCommand.ExecuteAsync(null);
        Check("読み直してから押せば実行される", vm.ResultMessage.Contains("3 件を書き出しました"), vm.ResultMessage);

        // 設定を変えたら結果は消えて見積もりに戻る
        vm.Output.Naming = FileNaming.PrefixSuffix;
        Check("設定を変えると結果が消える", vm.ShowEstimate && vm.ResultMessage.Length == 0, vm.ResultMessage);

        DeleteWithRetry(root);
    }

    /// <summary>
    /// 全件スキップになる設定へ変えた直後、見積もりが届く前に実行を押した場合。
    /// </summary>
    /// <remarks>
    /// 実行ボタンの無効化は表示中の見積もりに基づくので、古い値のままボタンが
    /// 有効な瞬間がある。破壊は起きないが、0 件で終わって理由が分からない状態に
    /// なるため、押した時点の確定で止める必要がある。
    /// </remarks>
    private static async Task TestStaleAllSkippedGuard()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_staleskip");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);

        var paths = new List<string>();
        for (var i = 1; i <= 4; i++)
        {
            var path = Path.Combine(input, $"IMG_{i}.png");
            WriteFlat(path, 16, 12, 100);
            paths.Add(path);
        }

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var vm = new ExportDialogViewModel(
            new Soroe.Models.ExportSettings { Folder = output },
            new ProcessingSettings(),
            paths,
            exporter,
            new StubFolderPicker(output));

        SpinUntil(() => vm.OutputPathPreview.Contains("IMG_1"), TimeSpan.FromSeconds(5));
        Check("切り替え前は実行できる", vm.RunCommand.CanExecute(null));

        // 全件スキップになる設定（同フォルダ・そのまま・上書き）へ変え、
        // 見積もりが届く前に実行を押す
        vm.Output.Overwrite = true;
        vm.Output.Folder = input;
        await vm.RunCommand.ExecuteAsync(null);

        Check("全件スキップになるなら実行しない", vm.ResultMessage.Length == 0, vm.ResultMessage);
        Check("出力先には何も作られない", Directory.GetFiles(output).Length == 0,
            $"{Directory.GetFiles(output).Length} 件");
        Check("理由が画面に出る", vm.BlockingWarning.Contains("スキップ"), vm.BlockingWarning);
        Check("以降は実行ボタンが無効になる", !vm.RunCommand.CanExecute(null));

        DeleteWithRetry(root);
    }

    private static async Task TestExportDialog()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_dialog");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        for (var i = 1; i <= 3; i++) WriteFlat(Path.Combine(input, $"IMG_{i}.png"), 40, 30, 100);

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var picker = new StubFolderPicker(input);
        var dialog = new StubExportDialog();
        var vm = new MainViewModel(
            new ProcessingSettings(), new Soroe.Models.ExportSettings(), renderer, exporter, picker, dialog);

        Check("画像が 0 件なら押せない", !vm.ExportCommand.CanExecute(null));
        Check("0 件のときの案内", vm.OutputPathPreview == "画像を追加してください", vm.OutputPathPreview);

        vm.AddFolderCommand.Execute(null);
        Check("画像があれば出力先が未設定でも押せる", vm.ExportCommand.CanExecute(null));

        // 実行せずに閉じた場合。設定は残る
        dialog.RunExport = false;
        dialog.OnShown = d =>
        {
            d.Output.Folder = output;
            d.Output.Format = ExportFormat.WebP;
            d.Output.JpegQuality = 40;
        };
        vm.ExportCommand.Execute(null);

        Check("閉じたら書き出さない", Directory.GetFiles(output).Length == 0, $"{Directory.GetFiles(output).Length} 件");
        Check("閉じても出力先が残る", vm.Output.Folder == output, vm.Output.Folder);
        Check("閉じても形式が残る", vm.Output.Format == ExportFormat.WebP, $"{vm.Output.Format}");
        Check("閉じても品質が残る", vm.Output.JpegQuality == 40, $"{vm.Output.JpegQuality}");
        SpinUntil(() => vm.OutputPathPreview.Contains(".webp"), TimeSpan.FromSeconds(5));
        Check("閉じた後にサマリ行が新しい形式になる",
            vm.OutputPathPreview.Contains(".webp") && vm.OutputPathPreview.Contains("3 件"), vm.OutputPathPreview);

        // 実行した場合
        dialog.RunExport = true;
        dialog.OnShown = null;
        vm.ExportCommand.Execute(null);
        var written = Directory.GetFiles(output);
        Check("実行すると書き出す", written.Length == 3, $"{written.Length} 件");
        Check("ダイアログで選んだ形式が使われる", written.All(f => Path.GetExtension(f) == ".webp"),
            string.Join(",", written.Select(Path.GetExtension).Distinct()));
        Check("結果が主画面の状態表示にも残る", vm.StatusMessage.Contains("3 件を書き出しました"), vm.StatusMessage);

        DeleteWithRetry(root);
        await Task.CompletedTask;
    }

    /// ダイアログ側（設定・実行・進捗・結果）の検証
    private static async Task TestExportDialogViewModel()
    {
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "soroe_vmtest_dialogvm");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        var paths = new List<string>();
        for (var i = 1; i <= 6; i++)
        {
            var path = Path.Combine(input, $"IMG_{i}.png");
            WriteFlat(path, 200, 150, 100);
            paths.Add(path);
        }

        var renderer = new ImageRenderer();
        var exporter = new ImageExporter(renderer);
        var settings = new Soroe.Models.ExportSettings();
        var processing = new ProcessingSettings();

        var vm = new ExportDialogViewModel(settings, processing, paths, exporter, new StubFolderPicker(output));

        Check("出力先が未設定なら実行は無効", !vm.RunCommand.CanExecute(null));
        Check("出力先が未設定のときの案内", vm.OutputPathPreview == "出力先が未設定です", vm.OutputPathPreview);
        Check("実行前は結果が空", vm.ResultMessage.Length == 0, vm.ResultMessage);
        Check("実行前はキャンセルできない", !vm.CancelCommand.CanExecute(null));

        vm.SelectOutputFolderCommand.Execute(null);
        Check("参照で出力先が入る", vm.Output.Folder == output, vm.Output.Folder);
        Check("出力先が入ると実行できる", vm.RunCommand.CanExecute(null));

        // 見積もりは非同期に届く
        SpinUntil(() => vm.OutputPathPreview.Contains("IMG_1"), TimeSpan.FromSeconds(5));
        Check("出力パスの表示", vm.OutputPathPreview.Contains("IMG_1"), vm.OutputPathPreview);

        await vm.RunCommand.ExecuteAsync(null);

        Check("6 件を書き出した", Directory.GetFiles(output).Length == 6, $"{Directory.GetFiles(output).Length}");
        Check("結果がダイアログに出る", vm.ResultMessage.Contains("6 件を書き出しました"), vm.ResultMessage);
        Check("進捗が N / N に到達する", vm.ExportCompleted == vm.ExportTotal && vm.ExportTotal == 6,
            $"{vm.ExportCompleted}/{vm.ExportTotal}");
        Check("終わると実行中ではない", !vm.IsExporting);
        Check("失敗リストは空", vm.ExportFailures.Count == 0);
        Check("終わると実行できる状態に戻る", vm.RunCommand.CanExecute(null));

        // 続けてもう一度実行できる（連番が付く）
        await vm.RunCommand.ExecuteAsync(null);
        Check("続けてもう一度実行できる", Directory.GetFiles(output).Length == 12,
            $"{Directory.GetFiles(output).Length}");

        // キャンセル
        var cancelOut = Path.Combine(root, "cancel");
        Directory.CreateDirectory(cancelOut);
        var cancelVm = new ExportDialogViewModel(
            new Soroe.Models.ExportSettings { Folder = cancelOut }, processing, paths, exporter,
            new StubFolderPicker(cancelOut));
        var task = cancelVm.RunCommand.ExecuteAsync(null);
        cancelVm.CancelCommand.Execute(null);
        await task;
        Check("キャンセル後は実行中ではない", !cancelVm.IsExporting);
        Check("キャンセル後に一時ファイルが残らない",
            Directory.GetFiles(cancelOut, "*.soroe-tmp").Length == 0);
        var afterCancel = Directory.GetFiles(cancelOut).Length;
        await cancelVm.RunCommand.ExecuteAsync(null);
        Check("キャンセル後にもう一度実行できる", Directory.GetFiles(cancelOut).Length > afterCancel,
            $"{afterCancel} → {Directory.GetFiles(cancelOut).Length}");

        // 失敗の詳細に原文が含まれる
        var brokenDir = Path.Combine(root, "broken");
        var brokenOut = Path.Combine(root, "broken_out");
        Directory.CreateDirectory(brokenDir);
        Directory.CreateDirectory(brokenOut);
        var broken = Path.Combine(brokenDir, "broken.png");
        File.WriteAllBytes(broken, new byte[] { 0x89, 0x50, 0x00, 0x01 });
        var brokenVm = new ExportDialogViewModel(
            new Soroe.Models.ExportSettings { Folder = brokenOut }, processing, [broken], exporter,
            new StubFolderPicker(brokenOut));
        await brokenVm.RunCommand.ExecuteAsync(null);
        Check("失敗が一覧に出る", brokenVm.ExportFailures.Count == 1, $"{brokenVm.ExportFailures.Count}");
        Check("失敗の表示は日本語", brokenVm.ExportFailures[0].Contains("画像を読み込めませんでした"),
            brokenVm.ExportFailures.Count > 0 ? brokenVm.ExportFailures[0] : "-");
        var report = brokenVm.BuildFailureReport();
        Check("詳細に原文が含まれる", report.Contains("UserMessageException") && report.Contains(broken), report);

        DeleteWithRetry(root);
    }

    private static string Names(MainViewModel vm) => string.Join(",", vm.Files.Select(f => f.FileName));

    /// プレビュー中央 1 画素の値（べた塗りなのでどこを見ても同じ）
    private static int Center(MainViewModel vm) => vm.PreviewImage is null ? -1 : CenterOf(vm.PreviewImage);

    private static int CenterOf(BitmapSource bitmap)
    {
        var bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
        var buffer = new byte[bytesPerPixel];
        bitmap.CopyPixels(
            new Int32Rect(bitmap.PixelWidth / 2, bitmap.PixelHeight / 2, 1, 1), buffer, bytesPerPixel, 0);
        return buffer[0];
    }

    private static void WriteImage(string path, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(30, 90, 200));
        Cv2.ImEncode(Path.GetExtension(path).ToLowerInvariant(), mat, out var bytes);
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteFlat(string path, int width, int height, byte value)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(value, value, value));
        Cv2.ImEncode(Path.GetExtension(path).ToLowerInvariant(), mat, out var bytes);
        File.WriteAllBytes(path, bytes);
    }

    /// プレビューの非同期読み込みがファイルを開いたままのことがあるので、少し待って消す
    private static void DeleteWithRetry(string path)
    {
        for (var i = 0; i < 25; i++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }

        Console.WriteLine($"（後始末に失敗: {path}）");
    }

    private static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }

        return false;
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        Console.WriteLine($"{(ok ? "OK  " : "NG  ")}{label}{(ok || detail is null ? "" : $"  -> {detail}")}");
        if (!ok) _failed++;
    }

    private sealed class StubFolderPicker(string folder) : IFolderPicker
    {
        public string? Pick() => folder;
    }

    /// Progress&lt;T&gt; は SynchronizationContext 経由で非同期に呼ばれるため、
    /// 検証では同期的に受け取れるものを使う
    private sealed class SyncProgress(Action<ExportProgress> onReport) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => onReport(value);
    }
}
