using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>agy の更新を確認した結果。</summary>
/// <param name="Success">更新係が「成功」と記録したか</param>
/// <param name="Message">更新係の記録の文(例: Already on the latest version.)。記録が読めなければ、その理由</param>
/// <param name="Updated">更新があったか(記録の文が「最新」でない、または agy.exe が書き換わった)</param>
public sealed record AgyUpdateResult(bool Success, string Message, bool Updated);

/// <summary>
/// agy の更新を、ユーザーが意図したときだけ確認する。
/// ふだんは ProcessRunner が自動更新を止めている(黒い窓が出るため)ので、ここだけは止めずに更新係を直接起動する。
/// </summary>
public sealed class AgyUpdater
{
    /// <summary>更新係を直接起動する標準の引数(agy が自分で更新係を起動するときと同じ)。</summary>
    public static readonly IReadOnlyList<string> DefaultUpdateCheckArgs =
        new[] { "--bg-updater", "--app_data_dir=antigravity-cli", "--gemini_dir=.gemini" };

    /// <summary>更新係が結果を書く、標準の記録ファイル。</summary>
    public const string DefaultUpdateStatusFile = "%USERPROFILE%/.gemini/antigravity-cli/updater/update_status.json";

    /// <summary>待ち時間の設定がおかしい(1秒未満)ときに使う上限(秒)。</summary>
    public const int DefaultUpdateCheckTimeoutSec = 180;

    /// <summary>更新係の記録で「最新の版」を表す文の始まり。</summary>
    private const string AlreadyLatestPrefix = "Already on the latest";

    // 確認は、アプリ全体で1つだけ(2つ同時に動くと、agy.exe の入れ替えと記録の読み取りが食い違う)
    private static readonly SemaphoreSlim Running = new(1, 1);

    private readonly AgySettings _settings;
    private readonly Func<ProcessStartInfo, CancellationToken, Task> _run;

    /// <summary>確認役を作る。</summary>
    /// <param name="settings">agy の設定</param>
    /// <param name="run">起動して終わるまで待つ処理(試験で偽物に差し替える。省略時は実際に起動する。キャンセルされたら止めて例外にすること)</param>
    public AgyUpdater(AgySettings settings, Func<ProcessStartInfo, CancellationToken, Task>? run = null)
    {
        _settings = settings;
        _run = run ?? RunProcessAsync;
    }

    /// <summary>
    /// 更新係を起動して、終わるまで待ち、記録を読む。版を確かめるときに黒い窓が一瞬出る(意図した操作なので許す)。
    /// </summary>
    /// <param name="ct">キャンセル(画面を閉じたとき)</param>
    /// <returns>確認の結果</returns>
    public async Task<AgyUpdateResult> CheckAsync(CancellationToken ct = default)
    {
        if (!await Running.WaitAsync(0, ct))
            throw new InvalidOperationException("ほかの画面で確認しています。終わってから押してください。");
        try
        {
            var exe = ProcessRunner.ResolveExecutable(_settings.Executable);
            var statusFile = StatusFilePath(_settings);
            var exeBefore = LastWrite(exe);
            var statusBefore = LastWrite(statusFile);

            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // OS側に同じ変数があっても、このときだけは更新を止めない
            info.Environment.Remove(ProcessRunner.AgyDisableAutoUpdateVariable);
            var args = _settings.UpdateCheckArgs is { Count: > 0 } a ? a : DefaultUpdateCheckArgs;
            foreach (var arg in args) info.ArgumentList.Add(arg);

            var timeoutSec = _settings.UpdateCheckTimeoutSec >= 1 ? _settings.UpdateCheckTimeoutSec : DefaultUpdateCheckTimeoutSec;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            try
            {
                await _run(info, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"更新の確認が {timeoutSec}秒で終わりませんでした。");
            }

            if (LastWrite(statusFile) == statusBefore)
                return new AgyUpdateResult(false, $"更新係の記録が書き換わりませんでした({statusFile})。", false);
            string json;
            try
            {
                json = File.ReadAllText(statusFile);
            }
            catch (IOException ex)
            {
                return new AgyUpdateResult(false, $"更新係の記録を読めませんでした({ex.Message})。", false);
            }
            var (success, message) = ReadStatus(json);
            var updated = success && (LastWrite(exe) != exeBefore
                || !message.StartsWith(AlreadyLatestPrefix, StringComparison.OrdinalIgnoreCase));
            return new AgyUpdateResult(success, message, updated);
        }
        finally
        {
            Running.Release();
        }
    }

    /// <summary>記録ファイルのパスを決める(環境変数を展開する)。</summary>
    /// <param name="settings">agy の設定</param>
    /// <returns>記録ファイルのフルパス</returns>
    public static string StatusFilePath(AgySettings settings) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            string.IsNullOrWhiteSpace(settings.UpdateStatusFile) ? DefaultUpdateStatusFile : settings.UpdateStatusFile));

    /// <summary>更新係の記録(例: {"success":true,"message":"Already on the latest version."})を読む。</summary>
    /// <param name="json">記録の中身</param>
    /// <returns>成功したか と 記録の文。読めなければ (false, 理由)</returns>
    public static (bool Success, string Message) ReadStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? "" : "";
            return (success, message);
        }
        catch (JsonException)
        {
            return (false, "更新係の記録を読めませんでした。");
        }
    }

    /// <summary>結果を、画面に出す文にする(記録の文は、そのまま添える)。</summary>
    /// <param name="result">確認の結果</param>
    /// <returns>画面に出す文</returns>
    public static string Describe(AgyUpdateResult result) =>
        !result.Success ? $"確認できませんでした: {result.Message}"
        : result.Updated ? $"更新がありました: {result.Message}"
        : $"最新の版です: {result.Message}";

    /// <summary>実際に起動して、終わるまで待つ。キャンセルされたら、プロセスを止めてから例外にする。</summary>
    /// <param name="info">起動の設定</param>
    /// <param name="ct">キャンセル</param>
    private static async Task RunProcessAsync(ProcessStartInfo info, CancellationToken ct)
    {
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("agy を起動できませんでした。");
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    /// <summary>ファイルの最終更新日時を返す。</summary>
    /// <param name="path">ファイルのパス</param>
    /// <returns>最終更新日時(UTC)。ファイルがなければ最小値</returns>
    private static DateTime LastWrite(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
}
