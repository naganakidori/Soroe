using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Soroe.Views.Controls;

/// <summary>
/// 数値を直接入力でき、▲▼ とマウスホイールで 1 ずつ増減できる入力欄。
/// </summary>
/// <remarks>
/// スライダーだけでは 1 目盛りを狙えないため、詰めの操作をこちらが受け持つ。
/// 調整項目は今後増えるので、項目ごとに作らず 1 つを使い回す。
/// </remarks>
public partial class NumericSpinner : UserControl
{
    /// <summary>現在値。</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(int),
        typeof(NumericSpinner),
        new FrameworkPropertyMetadata(
            0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged,
            CoerceValue));

    /// <summary>入力できる下限。</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(NumericSpinner), new PropertyMetadata(int.MinValue, OnRangeChanged));

    /// <summary>入力できる上限。</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(NumericSpinner), new PropertyMetadata(int.MaxValue, OnRangeChanged));

    /// <summary>▲▼ とホイール 1 回あたりの増減量。</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(int), typeof(NumericSpinner), new PropertyMetadata(1));

    /// <summary>
    /// <see cref="NumericSpinner" /> の新しいインスタンスを生成する。
    /// </summary>
    public NumericSpinner()
    {
        InitializeComponent();
        UpdateText();
    }

    /// <summary>現在値。</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>入力できる下限。</summary>
    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>入力できる上限。</summary>
    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>▲▼ とホイール 1 回あたりの増減量。</summary>
    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var spinner = (NumericSpinner)d;

        // 範囲外は丸める。入力・ボタン・ホイールのどの経路でもここを通る
        return Math.Clamp((int)baseValue, spinner.Minimum, spinner.Maximum);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((NumericSpinner)d).UpdateText();

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => d.CoerceValue(ValueProperty);

    /// <summary>
    /// 入力欄の内容が、確定している値と食い違っているかどうか。
    /// </summary>
    /// <remarks>打ちかけの入力があるかの判定に使う。</remarks>
    private bool HasPendingEdit => ValueText.Text != Value.ToString(CultureInfo.InvariantCulture);

    private void UpdateText()
    {
        var text = Value.ToString(CultureInfo.InvariantCulture);
        if (ValueText.Text != text)
        {
            ValueText.Text = text;
        }
    }

    /// <summary>
    /// 入力欄の内容を値として確定する。
    /// </summary>
    private void Commit()
    {
        // 数値として読めなければ元の値に戻す。中途半端な入力を残さない
        if (int.TryParse(ValueText.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            Value = parsed;
        }

        // 丸めや差し戻しの結果を表示にも反映する
        UpdateText();
    }

    private void Nudge(int direction) => Value += direction * Step;

    private void OnStepUp(object sender, RoutedEventArgs e) => Nudge(1);

    private void OnStepDown(object sender, RoutedEventArgs e) => Nudge(-1);

    /// <summary>
    /// ホイールで値を増減する。ただしフォーカスがあるときだけ。
    /// </summary>
    /// <remarks>
    /// 調整パネルはスクロールするため、通り過ぎただけのホイールで値が変わると、
    /// スクロールしたつもりで設定が書き換わったことに気づけない。一括処理の設定が
    /// 黙って変わるのは実害があるため、意図が明確な場合（一度クリックして
    /// フォーカスがある場合）に限る。フォーカスが無いときはスクロールに流す。
    /// </remarks>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsKeyboardFocusWithin)
        {
            return;
        }

        Nudge(Math.Sign(e.Delta));
        e.Handled = true;
    }

    private void OnTextGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 打ち直しをすぐ始められるように全選択しておく
        ValueText.SelectAll();
    }

    private void OnTextLostFocus(object sender, RoutedEventArgs e) => Commit();

    private void OnTextKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // 打ちかけの入力があるときだけ確定させ、キーを消費する。
                // 確定済みなら親へ通す。そうしないとダイアログの既定ボタン（実行）に
                // 永久に届かず、品質を打った直後に Enter で実行できなくなる
                if (!HasPendingEdit)
                {
                    break;
                }

                Commit();
                ValueText.SelectAll();
                e.Handled = true;
                break;

            case Key.Escape:
                // 取り消すものが無ければ親へ通す。ダイアログの「閉じる」に届かせるため
                if (!HasPendingEdit)
                {
                    break;
                }

                // 入力を捨てて現在値の表示に戻す
                UpdateText();
                ValueText.SelectAll();
                e.Handled = true;
                break;

            case Key.Up:
                Nudge(1);
                e.Handled = true;
                break;

            case Key.Down:
                Nudge(-1);
                e.Handled = true;
                break;
        }
    }
}
