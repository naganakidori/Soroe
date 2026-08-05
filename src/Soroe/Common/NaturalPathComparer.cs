using System.Runtime.InteropServices;

namespace Soroe.Common;

/// <summary>
/// エクスプローラーと同じ自然順でパスを比較する。
/// </summary>
/// <remarks>
/// 序数比較では "IMG_10.jpg" が "IMG_2.jpg" より前に来てしまい、
/// 連番リネームの結果が画面の見た目とずれる。
/// 並び順をエクスプローラーに一致させるため、エクスプローラー自身が使う
/// shlwapi.dll の StrCmpLogicalW をそのまま呼ぶ。
/// <para>
/// <b>このコンパレータは <see cref="List{T}.Sort(IComparer{T})" /> や
/// <c>Array.Sort</c> に渡さないこと。</b>StrCmpLogicalW は比較の推移性
/// （A &lt; B かつ B &lt; C ならば A &lt; C）を保証せず、これらは内部の整合性
/// チェックで <see cref="InvalidOperationException" /> を投げることがある。
/// 件数が多いときに稀に落ちる、再現性の低い不具合になる。
/// 並べ替えには <see cref="Enumerable.OrderBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey}, IComparer{TKey}?)" />
/// を使う。安定ソートで、この整合性チェックを行わない。
/// </para>
/// </remarks>
public sealed class NaturalPathComparer : IComparer<string>
{
    /// <summary>共有インスタンス。状態を持たないため使い回してよい。</summary>
    public static NaturalPathComparer Instance { get; } = new();

    private NaturalPathComparer()
    {
    }

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        // null は前に寄せる。実際には呼ばれない想定だが、IComparer の契約として定義しておく
        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return StrCmpLogicalW(x, y);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);
}
