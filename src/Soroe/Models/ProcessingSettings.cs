using System.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// 画像に何をどれだけかけるかの設定。
/// </summary>
/// <remarks>
/// 画像そのものは変更せず、この設定値だけを保持する。表示や書き出しのたびに
/// 元画像からこの設定を適用し直す（非破壊）。
/// <para>
/// 項目数も適用順序も固定であるため、多態なリストにはせず全項目を持つ単一のクラスとし、
/// 各項目が <c>Enabled</c> を持つ。JSON のポリモーフィックシリアライズが不要になる。
/// </para>
/// </remarks>
public sealed class ProcessingSettings
{
    /// <summary>
    /// <see cref="ProcessingSettings" /> の新しいインスタンスを生成する。
    /// </summary>
    public ProcessingSettings()
    {
        // 各項目の変更をこのクラスの 1 本のイベントにまとめる。
        // 購読側（ViewModel）が項目ごとに購読し直さずに済む。
        Resize.PropertyChanged += OnOptionChanged;
        Brightness.PropertyChanged += OnOptionChanged;
    }

    /// <summary>
    /// いずれかの項目が変更されたときに発生する。
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 設定の版。項目を変更したときに、古いプリセットを分岐処理するために使う。
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>リサイズ（適用順序 2 番目）。</summary>
    public ResizeOption Resize { get; } = new();

    /// <summary>明るさ（適用順序 3 番目）。</summary>
    public BrightnessOption Brightness { get; } = new();

    /// <summary>
    /// 現在の値をそのまま写した別インスタンスを返す。
    /// </summary>
    /// <remarks>
    /// レンダリングはバックグラウンドスレッドで行うため、その間に UI 側で値が
    /// 書き換わっても影響が出ないよう、開始時点の値を固定するために使う。
    /// <b>項目を追加したらここにも追加すること。</b>
    /// </remarks>
    public ProcessingSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Resize =
        {
            Enabled = Resize.Enabled,
            LongestEdge = Resize.LongestEdge,
        },
        Brightness =
        {
            Enabled = Brightness.Enabled,
            Value = Brightness.Value,
        },
    };

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
        => Changed?.Invoke(this, EventArgs.Empty);
}
