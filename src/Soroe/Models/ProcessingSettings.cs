using System.ComponentModel;
using System.Text.Json.Serialization;

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
/// <remarks>
/// <see cref="Resize" /> などの項目は setter を持たない（差し替えると
/// <see cref="Changed" /> の購読が切れるため）。読み込み時に新しいインスタンスへ
/// 差し替えるのではなく、既存のインスタンスへ値を書き込ませる必要があるので
/// <see cref="JsonObjectCreationHandling.Populate" /> を指定している。
/// </remarks>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class ProcessingSettings
{
    private int _schemaVersion = 1;

    /// <summary>
    /// <see cref="ProcessingSettings" /> の新しいインスタンスを生成する。
    /// </summary>
    public ProcessingSettings()
    {
        // 各項目の変更をこのクラスの 1 本のイベントにまとめる。
        // 購読側（ViewModel）が項目ごとに購読し直さずに済む。
        Rotation.PropertyChanged += OnOptionChanged;
        Resize.PropertyChanged += OnOptionChanged;
        Brightness.PropertyChanged += OnOptionChanged;
        Contrast.PropertyChanged += OnOptionChanged;
        Saturation.PropertyChanged += OnOptionChanged;
        Grayscale.PropertyChanged += OnOptionChanged;
        Binarize.PropertyChanged += OnOptionChanged;
        Sharpen.PropertyChanged += OnOptionChanged;
        Frame.PropertyChanged += OnOptionChanged;
    }

    /// <summary>
    /// いずれかの項目が変更されたときに発生する。
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 設定の版。項目を変更したときに、古いプリセットを分岐処理するために使う。
    /// </summary>
    /// <remarks>
    /// 自動保存の対象である以上、変えたら <see cref="Changed" /> が上がらなければならない。
    /// 通知しない保存対象を 1 つでも作ると、「変更しても保存されない」項目が生まれ、
    /// しかも往復のテストは通ったままになる。
    /// </remarks>
    public int SchemaVersion
    {
        get => _schemaVersion;
        set
        {
            if (_schemaVersion == value)
            {
                return;
            }

            _schemaVersion = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>回転（適用順序 1 番目）。</summary>
    public RotationOption Rotation { get; } = new();

    /// <summary>リサイズ（適用順序 2 番目）。</summary>
    public ResizeOption Resize { get; } = new();

    /// <summary>明るさ（適用順序 3 番目）。</summary>
    public BrightnessOption Brightness { get; } = new();

    /// <summary>コントラスト（適用順序 3 番目。明るさと同じ LUT で一括適用）。</summary>
    public ContrastOption Contrast { get; } = new();

    /// <summary>彩度（適用順序 4 番目）。</summary>
    public SaturationOption Saturation { get; } = new();

    /// <summary>グレースケール（適用順序 5 番目）。</summary>
    public GrayscaleOption Grayscale { get; } = new();

    /// <summary>二値化（適用順序 6 番目）。</summary>
    public BinarizeOption Binarize { get; } = new();

    /// <summary>シャープ（適用順序 7 番目）。</summary>
    public SharpenOption Sharpen { get; } = new();

    /// <summary>枠線（適用順序 8 番目）。</summary>
    public FrameOption Frame { get; } = new();

    /// <summary>
    /// 元画像の寸法に対して、この設定が指す出力寸法を返す。
    /// </summary>
    /// <param name="width">元画像の幅。</param>
    /// <param name="height">元画像の高さ。</param>
    /// <returns>回転（順序 1）とリサイズ（順序 2）を通した後の寸法。</returns>
    /// <remarks>
    /// <b>出力寸法の求め方をこの 1 箇所に閉じる。</b>寸法表示・チェーンの適用・
    /// 枠線の頭打ち判定がそれぞれ「回転してからリサイズ」を書き写していると、
    /// 片方だけ直したときに表示と結果が食い違う。二値化のしきい値で同じ形の
    /// 食い違いを起こしたので、こちらは最初から 1 本にする。
    /// <para>
    /// 順序 3 以降は画素値を変えるだけなので寸法に関わらない。順序 8 の枠線は
    /// 内側に描くので寸法を変えない。
    /// </para>
    /// </remarks>
    public (int Width, int Height) ResolveOutputSize(int width, int height)
    {
        var (rotatedWidth, rotatedHeight) = Rotation.ResolveSize(width, height);
        return Resize.ResolveSize(rotatedWidth, rotatedHeight);
    }

    /// <summary>
    /// 元画像の寸法に対して、枠線の太さが頭打ちに当たるかどうかを返す。
    /// </summary>
    /// <param name="width">元画像の幅。</param>
    /// <param name="height">元画像の高さ。</param>
    /// <returns>指定した太さより細い枠線しか描けない場合は <see langword="true" />。</returns>
    public bool IsFrameCapped(int width, int height)
    {
        if (!Frame.Enabled)
        {
            return false;
        }

        var (outputWidth, outputHeight) = ResolveOutputSize(width, height);
        return Frame.ResolveThickness(outputWidth, outputHeight) < Frame.Thickness;
    }

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
        Rotation =
        {
            Enabled = Rotation.Enabled,
            Angle = Rotation.Angle,
        },
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
        Contrast =
        {
            Enabled = Contrast.Enabled,
            Value = Contrast.Value,
        },
        Saturation =
        {
            Enabled = Saturation.Enabled,
            Value = Saturation.Value,
        },
        Grayscale = { Enabled = Grayscale.Enabled },
        Binarize = { Enabled = Binarize.Enabled },
        Sharpen =
        {
            Enabled = Sharpen.Enabled,
            Value = Sharpen.Value,
        },
        Frame =
        {
            Enabled = Frame.Enabled,
            Thickness = Frame.Thickness,
            Color = Frame.Color,
        },
    };

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
        => Changed?.Invoke(this, EventArgs.Empty);
}
