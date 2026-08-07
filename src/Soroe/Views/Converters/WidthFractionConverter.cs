using System.Globalization;
using System.Windows.Data;

namespace Soroe.Views.Converters;

/// <summary>
/// 幅に係数を掛けた値を返す。要素の上限幅を親の幅に追随させるために使う。
/// </summary>
/// <remarks>
/// <c>MaxWidth</c> を固定値で書くと、ウィンドウを広げても省略が解けない。
/// かといって <c>*</c> 列にすると、文字が短いときも列が場所を取って間延びする。
/// 親の幅に対する割合で上限を決めることで、左詰めのまま追随させる。
/// </remarks>
public sealed class WidthFractionConverter : IValueConverter
{
    /// <summary>この幅より狭くはしない。狭すぎて何も読めなくなるのを防ぐ。</summary>
    private const double MinimumWidth = 80;

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width) || width <= 0)
        {
            return double.PositiveInfinity;
        }

        var fraction = parameter is string text
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0.5;

        return Math.Max(MinimumWidth, width * fraction);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
