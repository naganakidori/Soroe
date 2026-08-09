using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// 設定ファイルの中身。
/// </summary>
/// <remarks>
/// 終了時に自動保存し、起動時に復元する「最後に使っていた状態」。
/// 名前付きプリセットとは別物なので、<c>presets</c> フォルダには置かない
/// （<c>settings</c> という名前のプリセットと衝突するため）。
/// </remarks>
public sealed class StoredSettings
{
    /// <summary>
    /// ファイル形式の版。
    /// </summary>
    /// <remarks>
    /// <see cref="ProcessingSettings.SchemaVersion" /> とは役割が違う。こちらは
    /// <b>ファイルの構造</b>の版で、あちらは<b>調整設定の内容</b>の版である。
    /// </remarks>
    public int SchemaVersion { get; set; } = SettingsStore.CurrentSchemaVersion;

    /// <summary>調整の設定。</summary>
    public ProcessingSettings Processing { get; set; } = new();

    /// <summary>書き出しの設定。</summary>
    public ExportSettings Export { get; set; } = new();
}
