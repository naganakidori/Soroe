using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// 二値化（大津の方法）の設定。
/// </summary>
/// <remarks>
/// <b>しきい値は自動で決まる。UI には出さない。</b>一括処理では多様な画像が混ざるため、
/// 手で決めた 1 つの値は多くの画像で外れる。大津は画像ごとにヒストグラムから決めるので、
/// 「バラバラな画像を一度に揃える」という目的に合う。加えてプレビューは選択中の 1 枚
/// だけなので、手で合わせるとその 1 枚に最適で他は黙って外れた値になる。
/// <para>
/// 明るさやコントラストで実質的にしきい値を調整することはできない。明るさを +N すると
/// 大津の求める値もほぼ +N ずれるため、結果はほとんど変わらない。ずらす手段が要ると
/// 分かった時点で、大津の値に対するオフセットを 1 つ足せばよい（加算式に足せる）。
/// </para>
/// <para>
/// しきい値の計算そのものは <c>ImageRenderer</c> にあり、
/// <c>ImageRenderer.CanonicalEdge</c> に縮小した画像から求める。
/// プレビューと書き出しで同じ値にするため。
/// </para>
/// </remarks>
public sealed partial class BinarizeOption : ObservableObject
{
    /// <summary>この項目を適用するかどうか。既定は OFF。</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }
}
