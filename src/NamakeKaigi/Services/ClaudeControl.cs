using System.IO;
using System.Text;
using System.Text.Json;

namespace NamakeKaigi.Services;

/// <summary>Claude CLI から届いた、許可の問い合わせ(can_use_tool)1件。</summary>
/// <param name="RequestId">問い合わせのID(答えるときに返す)</param>
/// <param name="ToolName">道具の名前</param>
/// <param name="Detail">何をするか(コマンドの文、ファイルのパスなど)</param>
/// <param name="InputJson">道具の引数(許可するとき、そのまま返す)</param>
public sealed record ClaudeToolRequest(string RequestId, string ToolName, string Detail, string InputJson);

/// <summary>
/// Claude CLI との、標準入力・標準出力の制御のやりとり(--input-format stream-json と --permission-prompt-tool stdio)。
/// 発言を送る行・許可の問い合わせの読み取り・答えの行を作る。
/// </summary>
public static class ClaudeControl
{
    /// <summary>ユーザーの発言を、標準入力へ送る1行(stream-json)にする。</summary>
    /// <param name="text">発言の本文</param>
    /// <returns>送る1行</returns>
    public static string BuildUserMessage(string text) => Write(w =>
    {
        w.WriteString("type", "user");
        w.WriteStartObject("message");
        w.WriteString("role", "user");
        w.WriteString("content", text);
        w.WriteEndObject();
    });

    /// <summary>制御の問い合わせの行か(許可の問い合わせ以外も含む)。</summary>
    /// <param name="line">標準出力の1行</param>
    /// <param name="requestId">問い合わせのID</param>
    /// <param name="subtype">問い合わせの種類(can_use_tool など)</param>
    /// <returns>制御の問い合わせならtrue</returns>
    public static bool TryParseRequest(string line, out string requestId, out string subtype)
    {
        requestId = "";
        subtype = "";
        if (!line.StartsWith('{') || !line.Contains("\"control_request\"")) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (Str(root, "type") != "control_request" || !root.TryGetProperty("request", out var req)) return false;
            requestId = Str(root, "request_id");
            subtype = Str(req, "subtype");
            return requestId.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>許可の問い合わせ(can_use_tool)を読み取る。</summary>
    /// <param name="line">標準出力の1行</param>
    /// <returns>問い合わせ(許可の問い合わせでなければnull)</returns>
    public static ClaudeToolRequest? TryParseToolRequest(string line)
    {
        if (!TryParseRequest(line, out var requestId, out var subtype) || subtype != "can_use_tool") return null;
        using var doc = JsonDocument.Parse(line);
        var req = doc.RootElement.GetProperty("request");
        var tool = Str(req, "tool_name");
        var hasInput = req.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object;
        var detail = hasInput ? DetailOf(input) : null;
        if (string.IsNullOrWhiteSpace(detail)) detail = Str(req, "description");
        return new ClaudeToolRequest(requestId, tool, detail ?? "", hasInput ? input.GetRawText() : "{}");
    }

    /// <summary>許可の問い合わせへの答えの行を作る。</summary>
    /// <param name="request">問い合わせ</param>
    /// <param name="allow">許可ならtrue</param>
    /// <returns>標準入力へ送る1行</returns>
    public static string BuildToolResponse(ClaudeToolRequest request, bool allow) => Write(w =>
    {
        w.WriteString("type", "control_response");
        w.WriteStartObject("response");
        w.WriteString("subtype", "success");
        w.WriteString("request_id", request.RequestId);
        w.WriteStartObject("response");
        if (allow)
        {
            w.WriteString("behavior", "allow");
            w.WritePropertyName("updatedInput");
            using var input = JsonDocument.Parse(request.InputJson);
            input.RootElement.WriteTo(w);
        }
        else
        {
            w.WriteString("behavior", "deny");
            w.WriteString("message", "ユーザーが承認しませんでした。");
        }
        w.WriteEndObject();
        w.WriteEndObject();
    });

    /// <summary>対応していない種類の問い合わせへの、断りの行を作る(返さないとCLIが待ち続けるため)。</summary>
    /// <param name="requestId">問い合わせのID</param>
    /// <param name="subtype">問い合わせの種類</param>
    /// <returns>標準入力へ送る1行</returns>
    public static string BuildUnsupportedResponse(string requestId, string subtype) => Write(w =>
    {
        w.WriteString("type", "control_response");
        w.WriteStartObject("response");
        w.WriteString("subtype", "error");
        w.WriteString("request_id", requestId);
        w.WriteString("error", $"ナマケ会議は {subtype} に対応していません。");
        w.WriteEndObject();
    });

    /// <summary>道具の引数から、何をするかの文(コマンドの文、ファイルのパスなど)を取り出す。</summary>
    private static string? DetailOf(JsonElement input) =>
        input.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : ActivityText.FindTarget(input);

    /// <summary>JSONのオブジェクト1つを、1行の文字列にする。</summary>
    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions
               { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
