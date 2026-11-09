using System.IO;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services.Remote;

/// <summary>
/// スマホのブラウザ向けの小さなWebサーバー。ページ(/)と、状態(/api/state)・発言の送信(/api/send)を配る。
/// すべての入口で合言葉を確かめる。会議の中身は IRemoteHost 越しにだけ触る。
/// </summary>
public sealed class RemoteServer : IAsyncDisposable
{
    private readonly RemoteSettings _settings;
    private readonly string _token;
    private readonly IRemoteHost _host;
    private readonly IPAddress _address;
    private WebApplication? _app;

    /// <summary>サーバーを用意する(起動は StartAsync)。</summary>
    /// <param name="settings">リモートの設定</param>
    /// <param name="token">合言葉(空は不可)</param>
    /// <param name="address">待ち受けるアドレス</param>
    /// <param name="host">会議の窓口</param>
    public RemoteServer(RemoteSettings settings, string token, IPAddress address, IRemoteHost host)
    {
        if (string.IsNullOrEmpty(token)) throw new ArgumentException("合言葉が空です", nameof(token));
        _settings = settings;
        _token = token;
        _address = address;
        _host = host;
    }

    /// <summary>実際に待ち受けているポート番号(起動後)。</summary>
    public int Port { get; private set; }

    /// <summary>スマホで開くURL(合言葉つき)。</summary>
    public string Url => $"http://{_address}:{Port}/?t={_token}";

    /// <summary>サーバーを起動する。</summary>
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(_address, _settings.Port));
        var app = builder.Build();

        app.MapGet("/", async ctx =>
        {
            if (!await AuthorizeAsync(ctx, ctx.Request.Query["t"])) return;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await ctx.Response.WriteAsync(LoadPage(_settings.PollMs));
        });
        app.MapGet("/api/state", async ctx =>
        {
            if (!await AuthorizeAsync(ctx, ctx.Request.Headers["X-Token"])) return;
            var snapshot = await _host.GetSnapshotAsync(Math.Max(1, _settings.MaxMessages));
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await ctx.Response.WriteAsJsonAsync(snapshot, JsonOptions);
        });
        app.MapPost("/api/send", async ctx =>
        {
            if (!await AuthorizeAsync(ctx, ctx.Request.Headers["X-Token"])) return;
            SendRequest? body;
            try { body = await ctx.Request.ReadFromJsonAsync<SendRequest>(JsonOptions); }
            catch (JsonException) { body = null; }
            var text = body?.Text?.Trim() ?? "";
            if (text.Length == 0 || text.Length > _settings.MaxTextChars)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("発言が空か、長すぎます");
                return;
            }
            var accepted = await _host.SendAsync(text);
            if (!accepted) ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsJsonAsync(new { ok = accepted }, JsonOptions);
        });

        await app.StartAsync();
        _app = app;
        Port = new Uri(app.Urls.First()).Port;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>合言葉を確かめる。違えば、少し待ってから401を返す(総当たりを遅くする)。</summary>
    private async Task<bool> AuthorizeAsync(HttpContext ctx, string? given)
    {
        if (RemoteAccess.TokenMatches(_token, given)) return true;
        await Task.Delay(Math.Max(0, _settings.FailDelayMs));
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsync("合言葉が違います");
        return false;
    }

    /// <summary>ページのHTMLを読み込み、設定値(更新の間隔)を埋め込む。</summary>
    private static string LoadPage(int pollMs)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("remote.html")
                           ?? throw new InvalidOperationException("remote.html が組み込まれていません");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("__POLL_MS__", Math.Max(500, pollMs).ToString());
    }

    /// <summary>送信リクエストの本体。</summary>
    private sealed record SendRequest(string? Text);

    /// <summary>サーバーを止める。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }
}
