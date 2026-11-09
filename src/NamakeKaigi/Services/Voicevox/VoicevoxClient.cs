using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services.Voicevox;

/// <summary>VOICEVOXのエンジンに、文を送って音声を作ってもらう。</summary>
public interface IVoicevoxClient
{
    /// <summary>文を音声(WAV)にする。</summary>
    /// <param name="text">読む文</param>
    /// <param name="speaker">声</param>
    /// <param name="cancellationToken">取り消し</param>
    /// <returns>WAVのバイト列</returns>
    Task<byte[]> SynthesizeAsync(string text, VoicevoxSpeaker speaker, CancellationToken cancellationToken);
}

/// <summary>VOICEVOXエンジンのAPI(audio_query → synthesis)を呼ぶ。つながらないときは例外を投げる(呼ぶ側が、代わりの声に切り替える)。</summary>
public sealed class VoicevoxClient : IVoicevoxClient, IDisposable
{
    private readonly HttpClient _http;

    /// <summary>クライアントを作る。</summary>
    /// <param name="settings">VOICEVOXの設定(アドレス・制限時間)</param>
    /// <param name="handler">通信の部分(試験で差し替える。省略時は標準)</param>
    public VoicevoxClient(VoicevoxSettings settings, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSec));
    }

    /// <inheritdoc />
    public async Task<byte[]> SynthesizeAsync(string text, VoicevoxSpeaker speaker, CancellationToken cancellationToken)
    {
        var query = $"audio_query?text={Uri.EscapeDataString(text)}&speaker={speaker.StyleId}";
        using var queryResponse = await _http.PostAsync(query, null, cancellationToken).ConfigureAwait(false);
        queryResponse.EnsureSuccessStatusCode();
        var parameters = JsonNode.Parse(await queryResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))!.AsObject();
        parameters["intonationScale"] = speaker.IntonationScale;
        parameters["speedScale"] = speaker.SpeedScale;

        using var body = new StringContent(parameters.ToJsonString(), Encoding.UTF8, "application/json");
        using var synthesisResponse = await _http.PostAsync($"synthesis?speaker={speaker.StyleId}", body, cancellationToken).ConfigureAwait(false);
        synthesisResponse.EnsureSuccessStatusCode();
        return await synthesisResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>通信の部品を片づける。</summary>
    public void Dispose() => _http.Dispose();
}
