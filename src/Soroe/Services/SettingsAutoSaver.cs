using System.ComponentModel;
using System.Windows.Threading;
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
/// </remarks>
public sealed class SettingsAutoSaver : IDisposable
{
    /// <summary>最後の変更から保存までの待ち時間。</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private readonly SettingsStore _store;
    private readonly ProcessingSettings _processing;
    private readonly ExportSettings _export;
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// <see cref="SettingsAutoSaver" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="store">保存先。</param>
    /// <param name="processing">監視する調整の設定。</param>
    /// <param name="export">監視する書き出しの設定。</param>
    /// <param name="saveDelay">
    /// 最後の変更から保存までの待ち時間。省略すると既定値になる。
    /// </param>
    public SettingsAutoSaver(
        SettingsStore store,
        ProcessingSettings processing,
        ExportSettings export,
        TimeSpan? saveDelay = null)
    {
        _store = store;
        _processing = processing;
        _export = export;

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
    /// 待たずにいま保存する。
    /// </summary>
    /// <remarks>
    /// 終了時に呼ぶ。待機中の変更を取りこぼさないための締めであって、
    /// これだけで足りるという意味ではない。
    /// </remarks>
    public void SaveNow()
    {
        _timer.Stop();
        _store.Save(_processing, _export);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _processing.Changed -= OnChanged;
        _export.PropertyChanged -= OnChanged;
    }

    private void OnTick(object? sender, EventArgs e) => SaveNow();

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
