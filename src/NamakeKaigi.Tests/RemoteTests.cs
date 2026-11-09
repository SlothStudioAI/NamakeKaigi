using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Windows.Threading;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;
using NamakeKaigi.Services.Remote;

namespace NamakeKaigi.Tests;

/// <summary>リモート(スマホ用Webページ)の試験: 合言葉・待ち受けアドレス・サーバーの入口。</summary>
public class RemoteTests
{
    /// <summary>会議の代わりの偽物。送られた発言を覚える。</summary>
    private sealed class FakeHost : IRemoteHost
    {
        public List<string> Sent { get; } = new();
        public bool Accept { get; set; } = true;

        public Task<RemoteSnapshot> GetSnapshotAsync(int maxMessages) =>
            Task.FromResult(new RemoteSnapshot("会議室1", false, new[]
            {
                new RemoteMessage(0, "user", "あなた", "10:00", "こんにちは", true, false, false, ""),
            }));

        public Task<bool> SendAsync(string text)
        {
            if (Accept) Sent.Add(text);
            return Task.FromResult(Accept);
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task<(RemoteServer Server, HttpClient Client, FakeHost Host)> StartAsync(string token = "secret-token")
    {
        var host = new FakeHost();
        var settings = new RemoteSettings(Enabled: true, Port: FreePort(), FailDelayMs: 0, MaxTextChars: 20);
        var server = new RemoteServer(settings, token, IPAddress.Loopback, host);
        await server.StartAsync();
        return (server, new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") }, host);
    }

    [Fact]
    public void 合言葉は一致したときだけ通る()
    {
        Assert.True(RemoteAccess.TokenMatches("abc", "abc"));
        Assert.False(RemoteAccess.TokenMatches("abc", "abd"));
        Assert.False(RemoteAccess.TokenMatches("abc", null));
        Assert.False(RemoteAccess.TokenMatches("", ""));
    }

    [Fact]
    public void 新しい合言葉は毎回違い32文字()
    {
        var a = RemoteAccess.NewToken();
        Assert.Equal(32, a.Length);
        Assert.NotEqual(a, RemoteAccess.NewToken());
    }

    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("192.168.0.5", false)]
    public void Tailscaleのアドレスを見分ける(string ip, bool expected) =>
        Assert.Equal(expected, RemoteAccess.IsTailscale(IPAddress.Parse(ip)));

    [Fact]
    public void アドレスは設定優先_次にTailscale_なければ自分のPC内だけ()
    {
        var local = new[] { IPAddress.Parse("192.168.0.5"), IPAddress.Parse("100.90.1.2") };
        Assert.Equal(IPAddress.Parse("100.90.1.2"), RemoteAccess.PickBindAddress(null, local).Address);
        Assert.Equal(IPAddress.Parse("10.0.0.9"), RemoteAccess.PickBindAddress("10.0.0.9", local).Address);
        var none = RemoteAccess.PickBindAddress("", new[] { IPAddress.Parse("192.168.0.5") });
        Assert.Equal(IPAddress.Loopback, none.Address);
        Assert.True(none.LoopbackOnly);
    }

    [Fact]
    public async Task 合言葉がないとページも状態も送信も401()
    {
        var (server, client, host) = await StartAsync();
        await using var _ = server;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/?t=wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/state")).StatusCode);
        var post = await client.PostAsJsonAsync("/api/send", new { text = "こんにちは" });
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Empty(host.Sent);
    }

    [Fact]
    public async Task 合言葉があればページと状態が返る()
    {
        var (server, client, _) = await StartAsync();
        await using var _ = server;
        var page = await client.GetAsync("/?t=secret-token");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("ナマケ会議", html);
        Assert.DoesNotContain("__POLL_MS__", html);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/state");
        req.Headers.Add("X-Token", "secret-token");
        var state = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        using var doc = System.Text.Json.JsonDocument.Parse(await state.Content.ReadAsStringAsync());
        Assert.Equal("会議室1", doc.RootElement.GetProperty("room").GetString());
        Assert.Equal("こんにちは", doc.RootElement.GetProperty("messages")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task 発言は届き_空や長すぎる発言は断る()
    {
        var (server, client, host) = await StartAsync();
        await using var _ = server;

        async Task<HttpStatusCode> Post(string text)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/send") { Content = JsonContent.Create(new { text }) };
            req.Headers.Add("X-Token", "secret-token");
            return (await client.SendAsync(req)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await Post("  よろしく  "));
        Assert.Equal(new[] { "よろしく" }, host.Sent);
        Assert.Equal(HttpStatusCode.BadRequest, await Post("   "));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new string('あ', 21)));
        host.Accept = false;
        Assert.Equal(HttpStatusCode.Conflict, await Post("送れない"));
    }

    [Fact]
    public void 合言葉が空ならサーバーを作れない() =>
        Assert.Throws<ArgumentException>(() =>
            new RemoteServer(new RemoteSettings(), "", IPAddress.Loopback, new FakeHost()));

    [Fact]
    public async Task スマホからの発言は会議に載り_PCの入力欄の下書きは残る()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() => { dispatcher = Dispatcher.CurrentDispatcher; ready.Set(); Dispatcher.Run(); }) { IsBackground = true };
        thread.Start();
        ready.Wait();
        try
        {
            var rooms = await dispatcher!.InvokeAsync(() => new MeetingRooms(() => new MainViewModel(() => new FakeSession()), 3)).Task;
            await dispatcher.InvokeAsync(() => rooms.Current.InputText = "書きかけ").Task;
            var host = new RemoteHost(rooms, dispatcher);

            Assert.True(await host.SendAsync("スマホから"));
            var snapshot = await host.GetSnapshotAsync(10);

            Assert.Equal("会議室1", snapshot.Room);
            Assert.Contains(snapshot.Messages, m => m.IsUser && m.Text == "スマホから");
            Assert.Equal("書きかけ", await dispatcher.InvokeAsync(() => rooms.Current.InputText).Task);
        }
        finally
        {
            dispatcher!.InvokeShutdown();
        }
    }

    [Fact]
    public void URLを_QRコードのPNG画像にできる()
    {
        var png = RemoteQr.ToPng("http://100.64.0.1:8765/?t=abc");
        Assert.True(png.Length > 100);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]); // PNGの印
    }
}
