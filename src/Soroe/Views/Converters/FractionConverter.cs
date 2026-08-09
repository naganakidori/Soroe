using System.Globalization;
using System.Windows.Data;

namespace Soroe.Views.Converters;

/// <summary>
/// 長さに係数を掛けた値を返す。要素の上限を親の大きさに追随させるために使う。
/// </summary>
/// <remarks>
/// 上限を固定値で書くと、窓を広げても効き方が変わらない。かといって <c>*</c> に
/// すると、中身が小さいときも場所を取って間延びする。<b>親に対する割合で上限を
/// 決めると、中身が少ないうちは中身なりの大きさで、増えたら頭打ちになる。</b>
/// <para>
/// 幅にも高さにも使う。下限は用途で違うので <see cref="Minimum" /> で与える。
/// </para>
/// </remarks>
public sealed class FractionConverter : IValueConverter
{
    /// <summary>これより小さくはしない。小さすぎて用を成さなくなるのを防ぐ。</summary>
    public double Minimum { get; set; } = 80;

    /// <summary>親に対する割合の既定値。<c>ConverterParameter</c> で上書きできる。</summary>
    public double Fraction { get; set; } = 0.5;

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double length || double.IsNaN(length) || length <= 0)
        {
            return double.PositiveInfinity;
        }

        var fraction = parameter is string text
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : Fraction;

        return Math.Max(Minimum, length * fraction);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
