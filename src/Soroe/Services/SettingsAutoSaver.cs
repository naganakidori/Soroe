using System.ComponentModel;
using System.Windows.Threading;
using Soroe.Common;
using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// 設定が変わったら、少し置いて自動的に保存する。
/// </summary>
/// <remarks>
/// <b>終了時の保存だけに頼らない。</b><c>Application.OnExit</c> は、クラッシュ・
/// タスクマネージャからの強制終了・Windows のシャットダウンでは呼ばれない。
/// それに任せきりだと、その回の変更が丸ごと失われる。
/// <para>
/// かといって変更のたびに書くと、スライダーを動かしている間ずっとディスクに
/// 書き続けることになる。最後の変更から少し待ってまとめて書く。
/// </para>
/// <para>
/// <b>書き込みは UI スレッドで行わない。</b>合図は <see cref="DispatcherTimer" /> で
/// 受けるが、そこで直接書くと、多重起動時の置き換えの取り合いで入る再試行の待ちが
/// そのまま画面の固まりになる。実測では通常 0.5ms 未満だが、競合したときは 200ms を
/// 超えることがあった。値の写しだけ UI スレッドで取り、書き込みは投げる。
/// </para>
/// </remarks>
public sealed class SettingsAutoSaver : IDisposable
{
    /// <summary>最後の変更から保存までの待ち時間。</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 終了時に保存を待つ上限。
    /// </summary>
    /// <remarks>
    /// 書き込み先は exe と同じ場所なので普通は一瞬で終わるが、ネットワーク共有や
    /// 遅い USB メモリに置いて起動されることはありうる。上限が無いと、そこで
    /// アプリが閉じられなくなる。<b>設定が保存されることより、確実に終了することを
    /// 優先する</b>。走っている保存の完了待ちと自分の書き込みを合わせてこの時間まで。
    /// </remarks>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromMilliseconds(500);

    private readonly SettingsStore _store;
    private readonly ProcessingSettings _processing;
    private readonly ExportSettings _export;
    private readonly DispatcherTimer _timer;
    private readonly TimeSpan _shutdownTimeout;

    /// <summary>
    /// 書き込みを 1 度に 1 つだけに制限する。
    /// </summary>
    /// <remarks>
    /// 一時ファイルのパスは <see cref="SettingsStore" /> ごとに 1 つなので、
    /// 同じプロセスの中で重ねて書くとそこで取り合いになる。
    /// <para>
    /// あえて <c>Dispose</c> しない。終了時の保存が待っている裏で片付けると、
    /// 解放済みのものを触ることになる。プロセスが終わるだけなので実害はない。
    /// </para>
    /// </remarks>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>保存要求の番号。新しい要求が来た時点で古い写しは用済みになる。</summary>
    private int _saveVersion;

    private int _discardedSaves;

    /// <summary>
    /// <see cref="SettingsAutoSaver" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="store">保存先。</param>
    /// <param name="processing">監視する調整の設定。</param>
    /// <param name="export">監視する書き出しの設定。</param>
    /// <param name="saveDelay">
    /// 最後の変更から保存までの待ち時間。省略すると既定値になる。
    /// </param>
    /// <param name="shutdownTimeout">
    /// 終了時に保存を待つ上限。省略すると既定値になる。
    /// </param>
    public SettingsAutoSaver(
        SettingsStore store,
        ProcessingSettings processing,
        ExportSettings export,
        TimeSpan? saveDelay = null,
        TimeSpan? shutdownTimeout = null)
    {
        _store = store;
        _processing = processing;
        _export = export;
        _shutdownTimeout = shutdownTimeout ?? ShutdownTimeout;

        _timer = new DispatcherTimer { Interval = saveDelay ?? SaveDelay };
        _timer.Tick += OnTick;

        _processing.Changed += OnChanged;
        _export.PropertyChanged += OnChanged;
    }

    /// <summary>
    /// 保存が必要になったことを検知したときに発生する。
    /// </summary>
    /// <remarks>
    /// 実際に書き出すのはこの後の待ち時間が過ぎてからで、ここでは保存を予約しただけである。
    /// <para>
    /// これを外へ出しているのは、<b>どの設定項目が保存の引き金になるかを検証できるようにする</b>ため。
    /// 保存されるかどうかは購読しているイベントの網羅性に依存しており、入れ子の項目が増えたときに
    /// 通知が上がらなくなっても、値の往復を見るテストは通ったままになる。
    /// </para>
    /// </remarks>
    public event EventHandler? SaveRequested;

    /// <summary>
    /// 古くなった写しを、書かずに捨てた回数。
    /// </summary>
    /// <remarks>
    /// 写しを取る順序と書き込む順序が入れ替わると、直前の変更が巻き戻り、その後は
    /// 変更が起きないので巻き戻ったまま確定する。それを防ぐのが版番号による最新優先で、
    /// これはその仕組みが実際に働いたかを外から確かめるために持つ。
    /// <para>
    /// <b>結果（最後の値）を見るだけのテストでは足りない。</b>順序の入れ替わり自体が
    /// めったに起きないため、仕組みを外しても最後の値は正しいままになる（実測で確認済み）。
    /// </para>
    /// </remarks>
    public int DiscardedSaves => Volatile.Read(ref _discardedSaves);

    /// <summary>
    /// 待たずにいま保存する。書き終わるか、上限に達するまで戻らない。
    /// </summary>
    /// <returns>書き終わった場合は <see langword="true" />。上限で諦めた場合は <see langword="false" />。</returns>
    /// <remarks>
    /// 終了時に呼ぶ。待機中の変更を取りこぼさないための締めであって、
    /// これだけで足りるという意味ではない。
    /// <para>
    /// <b>投げっぱなしにはしない。</b>終了処理の途中でバックグラウンドへ投げると、
    /// 書き終わる前にプロセスが消えることがある。かといって無制限に待つと、
    /// 遅い場所に置かれた exe ではアプリが閉じられなくなる。
    /// <see cref="ShutdownTimeout" /> まで待って、超えたら諦める。
    /// </para>
    /// </remarks>
    public bool SaveNow()
    {
        _timer.Stop();

        // 番号を進めて、順番待ちしているバックグラウンドの保存を無効にする。
        // 待たせたまま古い写しで上書きされてはいけない
        Interlocked.Increment(ref _saveVersion);
        var snapshot = _store.Capture(_processing, _export);

        // 順番待ちも書き込みもバックグラウンドへ置き、こちらは待つだけにする。
        // こうしないと上限を掛けられない（掛けられるのは待ち時間だけになり、
        // 書き込み自体が長引いた場合に効かない）
        var write = Task.Run(() =>
        {
            _writeGate.Wait();
            try
            {
                _store.Write(snapshot);
            }
            finally
            {
                _writeGate.Release();
            }
        });

        if (write.Wait(_shutdownTimeout))
        {
            return true;
        }

        // 諦めても書き込み自体は続くが、プロセスが終われば道連れになる。
        // 中途半端な一時ファイルが残りうるので、次の起動時に片付ける
        FileErrorMessage.Log(
            "設定の保存",
            new TimeoutException($"{_shutdownTimeout.TotalMilliseconds:F0}ms で終わらなかったため諦めた"));
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _processing.Changed -= OnChanged;
        _export.PropertyChanged -= OnChanged;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _ = SaveInBackgroundAsync();
    }

    /// <summary>
    /// 写しを取ってから、書き込みだけをバックグラウンドで行う。
    /// </summary>
    /// <remarks>
    /// 写しを取るのは <c>Task.Run</c> の前、つまり UI スレッド。設定を編集しているのと
    /// 同じスレッドで固定しないと、書いている最中に画面側が値を変える競合になる。
    /// <para>
    /// <b>順番待ちごと <see cref="Task.Run(Action)" /> の中へ入れること。</b>
    /// <c>await _writeGate.WaitAsync().ConfigureAwait(false)</c> と書くと、待たずに
    /// 通れた場合に <c>await</c> がそのまま同期で続き、書き込みが UI スレッドに残る。
    /// <c>ConfigureAwait(false)</c> が効くのは実際に中断したときだけである。
    /// </para>
    /// </remarks>
    private async Task SaveInBackgroundAsync()
    {
        var version = Interlocked.Increment(ref _saveVersion);
        var snapshot = _store.Capture(_processing, _export);

        await Task.Run(() =>
        {
            _writeGate.Wait();
            try
            {
                // 待っている間に新しい要求が来ていたら、この写しはもう古い。
                // ここで書くと、直前の変更が巻き戻ったまま確定してしまう
                if (Volatile.Read(ref _saveVersion) != version)
                {
                    Interlocked.Increment(ref _discardedSaves);
                    return;
                }

                _store.Write(snapshot);
            }
            finally
            {
                _writeGate.Release();
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 変更を受けて、保存までの待ち時間を計り直す。
    /// </summary>
    /// <remarks>
    /// <see cref="PropertyChangedEventArgs" /> は <see cref="EventArgs" /> を継承するため、
    /// このメソッド 1 つで両方のイベントを購読できる。
    /// </remarks>
    private void OnChanged(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Start();
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }
}
