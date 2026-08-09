using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Soroe.Common;
using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// 設定の保存と復元。
/// </summary>
/// <remarks>
/// 保存先は exe と同じ場所。zip 解凍で動く可搬性を重視しており、<c>%AppData%</c> は使わない。
/// 開発時は <c>bin\Debug\net10.0-windows\</c> 配下になるため、Debug と Release で
/// 設定は別々になり、<c>dotnet clean</c> で消える。可搬性を優先した結果として受け入れる。
/// </remarks>
public sealed class SettingsStore
{
    /// <summary>いま書き出しているファイル形式の版。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 保存しない項目。
    /// </summary>
    /// <remarks>
    /// <b><see cref="ExportSettings.Overwrite" /> は意図的に除外している。</b>
    /// 前回オンにしたまま忘れ、次回起動時もオンのままだと、気づかないうちに上書きが走る。
    /// 他の設定と違って取り返しがつかないため、毎回意識的にオンにしてもらう（原則 2）。
    /// <para>
    /// このリストは<b>保存時と復元時の両方</b>で使う。片方だけだと、古い設定ファイルが
    /// 残っている環境で値が復活する。
    /// </para>
    /// </remarks>
    private static readonly string[] NotSaved = [nameof(ExportSettings.Overwrite)];

    /// <summary>
    /// 書き込み途中のファイルに付ける拡張子。
    /// </summary>
    /// <remarks>書き出しの一時ファイルと同じ考え方で揃えている。</remarks>
    private const string TempExtension = ".soroe-tmp";

    /// <summary>
    /// 書き込み途中のファイルのパス。
    /// </summary>
    /// <remarks>
    /// <b>プロセス ID を挟んで、多重起動でも取り合いにならないようにする。</b>
    /// zip を解凍して置くだけの配布形態である以上、同じフォルダの exe を 2 つ
    /// 起動されるのは普通にありうる。固定名だと 2 つのプロセスが同じ一時ファイルを
    /// 使うため、片方が書いている最中にもう片方がそれを <c>File.Move</c> しうる。
    /// 一時ファイルを挟んでいる目的（中途半端な内容を本体にしない）が崩れる。
    /// <para>
    /// 実測では、固定名だと 2 プロセスの連続保存で 73% が失敗した。プロセス ID を
    /// 挟むと 7% まで下がる（残りは置き換え先の取り合いで、<see cref="SaveAttempts" />
    /// の再試行で吸収する）。なお中途半端な内容が本体になる事象自体は、
    /// 2400 回の試行では観測できなかった。起こりにくいだけで、防ぐ手が安いほうを採る。
    /// </para>
    /// <para>
    /// 本体（<c>settings.json</c>）を最後に書いた方が勝つことは避けられないが、
    /// それは設定を 1 つのファイルで持つ以上どうにもならない。
    /// </para>
    /// </remarks>
    private readonly string _tempPath;

    /// <summary>
    /// 置き換えを試す回数。
    /// </summary>
    /// <remarks>
    /// 一時ファイルを分けても、置き換え先の <c>settings.json</c> は 1 つしかない。
    /// 多重起動していると、相手が掴んでいる一瞬に当たって <c>File.Move</c> が失敗する。
    /// 実測では 2 プロセスで連続保存した場合の 7% 程度で起きた。すぐ諦めると
    /// 設定が黙って保存されないので、少し待って試し直す。
    /// </remarks>
    private const int SaveAttempts = 5;

    /// <summary>再試行までの待ち時間（ミリ秒）。試すたびに伸ばす。</summary>
    private const int RetryDelayMs = 20;

    /// <summary>
    /// 一時ファイルを置き去りとみなすまでの時間。
    /// </summary>
    /// <remarks>
    /// 保存 1 回は数ミリ秒で終わるので、これだけ開けておけば書いている最中のものに
    /// 当たることはない。長めに取っても、消し損ねたごみが次の起動まで残るだけである。
    /// </remarks>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    /// <summary>自分のプロセス名。一時ファイルの持ち主を判定するのに使う。</summary>
    private static readonly string CurrentProcessName = Process.GetCurrentProcess().ProcessName;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    /// <summary>
    /// <see cref="SettingsStore" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="directory">
    /// 保存先のフォルダ。省略すると exe と同じ場所になる。
    /// </param>
    public SettingsStore(string? directory = null)
    {
        // Assembly.Location は単一ファイル発行で空になり、Environment.ProcessPath は
        // dotnet Soroe.dll 形式の起動で dotnet.exe を指す。exe と同じ場所を確実に取れるのはこれ
        _path = Path.Combine(directory ?? AppContext.BaseDirectory, "settings.json");
        _tempPath = $"{_path}.{Environment.ProcessId}{TempExtension}";
    }

    /// <summary>
    /// 保存されている設定を読み込む。
    /// </summary>
    /// <returns>
    /// 読み込めた設定。ファイルが無い・壊れている・読めない場合は既定値。
    /// </returns>
    /// <remarks>
    /// <b>どんな失敗でも既定値で起動する。</b>設定が読めないことでアプリが使えなくなるのは
    /// 割に合わない。原因はデバッグ出力に残す。
    /// </remarks>
    public StoredSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new StoredSettings();
            }

            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(_path), Options);
            if (stored is null)
            {
                return new StoredSettings();
            }

            // 現在より新しい版は読み方が分からないので既定値に落とす。
            // 逆に古い版は将来の移行の対象になる。version 2 のアプリが version 1 の
            // ファイルを読む場合は、ここで内容を読み替えてから返す
            if (stored.SchemaVersion > CurrentSchemaVersion)
            {
                FileErrorMessage.Log(
                    "設定の読み込み", new InvalidOperationException($"未知の版: {stored.SchemaVersion}"));
                return new StoredSettings();
            }

            ResetNotSaved(stored.Export);
            return stored;
        }
        catch (Exception ex)
        {
            FileErrorMessage.Log($"設定の読み込み {_path}", ex);
            return new StoredSettings();
        }
        finally
        {
            CleanStaleTempFiles();
        }
    }

    /// <summary>
    /// 置き去りになった一時ファイルを片付ける。
    /// </summary>
    /// <remarks>
    /// 終了時の保存は上限で打ち切ることがあり、その場合は書きかけの一時ファイルが残る。
    /// zip を解凍して置くだけの配布形態なので、exe の隣にごみが溜まっていくのは避けたい。
    /// <para>
    /// <b>「掴まれていないから消してよい」とは判断できない。</b>一時ファイルは
    /// 書き終えてから <c>File.Move</c> するまでの一瞬、誰にも掴まれていない。
    /// その瞬間に別のインスタンスが起動すると、まだ有効なものを消してしまい、
    /// 相手の保存が 1 回分失われる。<see cref="IsStale" /> で二重に確かめる。
    /// </para>
    /// </remarks>
    private void CleanStaleTempFiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (directory is null)
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(
                directory, $"{Path.GetFileName(_path)}.*{TempExtension}"))
            {
                if (!IsStale(path))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (IsContention(ex))
                {
                    // 他のインスタンスが使っている。触らないのが正しい
                }
            }
        }
        catch (Exception ex)
        {
            // 片付けに失敗しても起動は妨げない
            FileErrorMessage.Log("一時ファイルの片付け", ex);
        }
    }

    /// <summary>
    /// その一時ファイルが置き去りかどうか。
    /// </summary>
    /// <remarks>
    /// 条件を 2 つとも満たすものだけを消す。<b>迷ったら消さない側に倒す。</b>
    /// 消し損ねても数百バイトのごみが次の起動まで残るだけだが、消し間違えると
    /// 動いているインスタンスの保存が失われる。損得が釣り合っていない。
    /// <list type="number">
    /// <item>名前の PID が、動いている同名のプロセスに該当しないこと</item>
    /// <item>十分に古いこと。PID は使い回されるため、1 つ目だけでは足りない</item>
    /// </list>
    /// </remarks>
    private static bool IsStale(string path)
    {
        if (IsOwnerRunning(path))
        {
            return false;
        }

        try
        {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > StaleAfter;
        }
        catch (Exception ex) when (IsContention(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// 一時ファイルを書いたインスタンスがまだ動いているか。
    /// </summary>
    /// <remarks>
    /// 名前を読めない場合や、調べられない場合は <see langword="true" />（消さない）を返す。
    /// </remarks>
    private static bool IsOwnerRunning(string path)
    {
        // settings.json.<pid>.soroe-tmp から PID を取り出す
        var name = Path.GetFileNameWithoutExtension(path);
        var separator = name.LastIndexOf('.');
        if (separator < 0 || !int.TryParse(name[(separator + 1)..], out var processId))
        {
            return true;
        }

        try
        {
            using var process = Process.GetProcessById(processId);

            // PID は使い回されるので、同じ名前のプロセスかどうかまで見る。
            // 無関係のプロセスに当たった場合は、古さの判定のほうで拾う
            return string.Equals(process.ProcessName, CurrentProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // その PID のプロセスは存在しない
            return false;
        }
        catch (Exception)
        {
            // 調べられないなら消さない
            return true;
        }
    }

    /// <summary>
    /// 設定を保存する。
    /// </summary>
    /// <returns>保存できた場合は <see langword="true" />。</returns>
    /// <remarks>
    /// <b>失敗しても例外を外に出さない。</b>終了処理を妨げないため。
    /// <para>
    /// 一時ファイルに書いてから置き換える。直接上書きしている最中に電源が落ちると、
    /// 壊れた JSON が残って設定が全部消える。書き出しの一時ファイルと同じ手法。
    /// </para>
    /// </remarks>
    public bool Save(ProcessingSettings processing, ExportSettings export)
        => Write(Capture(processing, export));

    /// <summary>
    /// いまの設定の写しを取る。
    /// </summary>
    /// <remarks>
    /// <b>必ず設定を編集している側のスレッド（UI スレッド）で呼ぶこと。</b>
    /// 書き出しはバックグラウンドで行うため、その最中に画面側が値を変えても
    /// 影響が出ないよう、ここで値を固定する。レンダリングや書き出しと同じ考え方。
    /// </remarks>
    public StoredSettings Capture(ProcessingSettings processing, ExportSettings export) => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Processing = processing.Clone(),
        Export = export.Clone(),
    };

    /// <summary>
    /// 写しをファイルへ書き出す。
    /// </summary>
    /// <returns>保存できた場合は <see langword="true" />。</returns>
    /// <remarks>
    /// スレッドは問わないが、<b>同じ <see cref="SettingsStore" /> に対して同時に呼ばないこと</b>。
    /// 一時ファイルのパスはインスタンスごとに 1 つなので、重なると取り合いになる。
    /// <para>
    /// 保存しない項目を落とすのはここで行う。書き出す経路が 1 つになるので、
    /// どこから呼んでも除外が効く（原則 2 に関わるため、抜け道を作らない）。
    /// </para>
    /// </remarks>
    public bool Write(StoredSettings stored)
    {
        try
        {
            ResetNotSaved(stored.Export);
            var json = JsonSerializer.Serialize(stored, Options);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.WriteAllText(_tempPath, json);
                    File.Move(_tempPath, _path, overwrite: true);
                    return true;
                }
                catch (Exception ex) when (attempt < SaveAttempts && IsContention(ex))
                {
                    // 相手が置き換え先を掴んでいるだけなら、待てば通る。
                    // 最後の 1 回で駄目なら外の catch で記録して諦める
                    Thread.Sleep(RetryDelayMs * attempt);
                }
            }
        }
        catch (Exception ex)
        {
            FileErrorMessage.Log($"設定の保存 {_path}", ex);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(_tempPath))
                {
                    File.Delete(_tempPath);
                }
            }
            catch (IOException)
            {
                // 後始末に失敗しても保存の成否には関係しない
            }
        }
    }

    /// <summary>
    /// 待てば解消しうる失敗かどうか。
    /// </summary>
    /// <remarks>
    /// <b><see cref="UnauthorizedAccessException" /> を必ず含めること。</b>これは
    /// <see cref="IOException" /> を継承していないため、<c>IOException</c> だけを捕まえる
    /// 書き方だと素通りする。実測でも、多重起動時の <c>File.Move</c> の失敗は
    /// ほとんどがこちらだった（「アクセスが拒否されました」）。
    /// <para>
    /// 書き込めないフォルダを指定している場合も同じ例外になるが、その場合は
    /// 数回試して諦めるだけで、待ち時間は合計 200ms 程度にとどまる。
    /// </para>
    /// </remarks>
    private static bool IsContention(Exception exception)
        => exception is IOException or UnauthorizedAccessException;

    /// <summary>
    /// 保存しない項目を既定値へ戻す。
    /// </summary>
    private static void ResetNotSaved(ExportSettings export)
    {
        var defaults = new ExportSettings();
        foreach (var name in NotSaved)
        {
            var property = typeof(ExportSettings).GetProperty(name);
            property?.SetValue(export, property.GetValue(defaults));
        }
    }
}
