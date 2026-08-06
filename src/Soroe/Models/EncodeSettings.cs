namespace Soroe.Models;

/// <summary>
/// 画像を 1 枚エンコードするときの設定。
/// </summary>
/// <remarks>
/// 拡張子だけを引数で渡す形にすると、品質や圧縮率を足すたびにシグネチャを変えることになる。
/// この型にまとめておき、項目が増えても中身を足すだけで済むようにする。
/// <para>
/// 追加予定: JPEG 品質（0〜100、既定 95）、WebP 品質（101 で可逆）、
/// PNG 圧縮率（可逆で画質が変わらないため UI には出さない）。
/// </para>
/// </remarks>
public sealed class EncodeSettings
{
    /// <summary>
    /// 出力する形式の拡張子。先頭のドットを含む（例: <c>.jpg</c>）。
    /// </summary>
    public string Extension { get; set; } = ".png";
}
