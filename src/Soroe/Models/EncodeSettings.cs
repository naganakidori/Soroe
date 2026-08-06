namespace Soroe.Models;

/// <summary>
/// 画像を 1 枚エンコードするときの設定。
/// </summary>
/// <remarks>
/// 拡張子だけを引数で渡す形にすると、品質や圧縮率を足すたびにシグネチャを変えることになる。
/// この型にまとめておき、項目が増えても中身を足すだけで済むようにする。
/// </remarks>
public sealed class EncodeSettings
{
    /// <summary>
    /// 出力する形式の拡張子。先頭のドットを含む（例: <c>.jpg</c>）。
    /// </summary>
    public string Extension { get; set; } = ".png";

    /// <summary>JPEG の品質（0〜100）。JPEG 以外では使わない。</summary>
    public int JpegQuality { get; set; } = 95;

    /// <summary>
    /// WebP の品質。101 で可逆になる。WebP 以外では使わない。
    /// </summary>
    public int WebPQuality { get; set; } = 95;
}
