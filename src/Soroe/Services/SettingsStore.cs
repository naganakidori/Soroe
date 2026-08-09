using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Soroe.Common;
using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// 設定の保存と復元。
/// </summary>
/// <remarks>
/// 保存先は exe と同じ場所。zip 解凍で動く可搬性を重視しており、<c>%AppData%</c> は使わない。
/// 開発時は <c>bin\Debug\net10.0-windows\</c> 配下になるため、Debug と Release で
/// 設定は別々になり、<c>dotnet clean</c> で消える。可搬性を優先した結果として受け入れる。
/// </remarks>
public sealed class SettingsStore
{
    /// <summary>いま書き出しているファイル形式の版。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 保存しない項目。
    /// </summary>
    /// <remarks>
    /// <b><see cref="ExportSettings.Overwrite" /> は意図的に除外している。</b>
    /// 前回オンにしたまま忘れ、次回起動時もオンのままだと、気づかないうちに上書きが走る。
    /// 他の設定と違って取り返しがつかないため、毎回意識的にオンにしてもらう（原則 2）。
    /// <para>
    /// このリストは<b>保存時と復元時の両方</b>で使う。片方だけだと、古い設定ファイルが
    /// 残っている環境で値が復活する。
    /// </para>
    /// </remarks>
    private static readonly string[] NotSaved = [nameof(ExportSettings.Overwrite)];

    /// <summary>
    /// 書き込み途中のファイルに付ける拡張子。
    /// </summary>
    /// <remarks>書き出しの一時ファイルと同じ考え方で揃えている。</remarks>
    private const string TempExtension = ".soroe-tmp";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    /// <summary>
    /// <see cref="SettingsStore" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="directory">
    /// 保存先のフォルダ。省略すると exe と同じ場所になる。
    /// </param>
    public SettingsStore(string? directory = null)
    {
        // Assembly.Location は単一ファイル発行で空になり、Environment.ProcessPath は
        // dotnet Soroe.dll 形式の起動で dotnet.exe を指す。exe と同じ場所を確実に取れるのはこれ
        _path = Path.Combine(directory ?? AppContext.BaseDirectory, "settings.json");
    }

    /// <summary>
    /// 保存されている設定を読み込む。
    /// </summary>
    /// <returns>
    /// 読み込めた設定。ファイルが無い・壊れている・読めない場合は既定値。
    /// </returns>
    /// <remarks>
    /// <b>どんな失敗でも既定値で起動する。</b>設定が読めないことでアプリが使えなくなるのは
    /// 割に合わない。原因はデバッグ出力に残す。
    /// </remarks>
    public StoredSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new StoredSettings();
            }

            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(_path), Options);
            if (stored is null)
            {
                return new StoredSettings();
            }

            // 現在より新しい版は読み方が分からないので既定値に落とす。
            // 逆に古い版は将来の移行の対象になる。version 2 のアプリが version 1 の
            // ファイルを読む場合は、ここで内容を読み替えてから返す
            if (stored.SchemaVersion > CurrentSchemaVersion)
            {
                FileErrorMessage.Log(
                    "設定の読み込み", new InvalidOperationException($"未知の版: {stored.SchemaVersion}"));
                return new StoredSettings();
            }

            ResetNotSaved(stored.Export);
            return stored;
        }
        catch (Exception ex)
        {
            FileErrorMessage.Log($"設定の読み込み {_path}", ex);
            return new StoredSettings();
        }
    }

    /// <summary>
    /// 設定を保存する。
    /// </summary>
    /// <returns>保存できた場合は <see langword="true" />。</returns>
    /// <remarks>
    /// <b>失敗しても例外を外に出さない。</b>終了処理を妨げないため。
    /// <para>
    /// 一時ファイルに書いてから置き換える。直接上書きしている最中に電源が落ちると、
    /// 壊れた JSON が残って設定が全部消える。書き出しの一時ファイルと同じ手法。
    /// </para>
    /// </remarks>
    public bool Save(ProcessingSettings processing, ExportSettings export)
    {
        var tempPath = _path + TempExtension;
        try
        {
            // 除外項目を既定に戻した写しを書き出す。元の設定は触らない
            var stored = new StoredSettings
            {
                SchemaVersion = CurrentSchemaVersion,
                Processing = processing.Clone(),
                Export = export.Clone(),
            };

            ResetNotSaved(stored.Export);

            File.WriteAllText(tempPath, JsonSerializer.Serialize(stored, Options));
            File.Move(tempPath, _path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            FileErrorMessage.Log($"設定の保存 {_path}", ex);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
                // 後始末に失敗しても保存の成否には関係しない
            }
        }
    }

    /// <summary>
    /// 保存しない項目を既定値へ戻す。
    /// </summary>
    private static void ResetNotSaved(ExportSettings export)
    {
        var defaults = new ExportSettings();
        foreach (var name in NotSaved)
        {
            var property = typeof(ExportSettings).GetProperty(name);
            property?.SetValue(export, property.GetValue(defaults));
        }
    }
}
