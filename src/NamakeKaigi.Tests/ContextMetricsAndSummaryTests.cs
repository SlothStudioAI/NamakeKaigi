using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;
using Xunit;

namespace NamakeKaigi.Tests;

public sealed class ContextMetricsAndSummaryTests
{
    [Fact]
    public void transcriptから最新のinput_tokensとcache_read_tokensを合算できる()
    {
        using var dir = new TempDir();
        var convId = "conv-123";
        var logsDir = Path.Combine(dir.Path, convId, ".system_generated", "logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllLines(Path.Combine(logsDir, "transcript.jsonl"), new[]
        {
            """{"step_index":1,"type":"USER_INPUT","content":"hello"}""",
            """{"step_index":2,"type":"PLANNER_RESPONSE","input_tokens":1000,"cache_read_tokens":5000}""",
            """{"step_index":3,"type":"PLANNER_RESPONSE","input_tokens":2500,"cache_read_tokens":12000}""",
        });

        var (inp, cache) = ContextMetricsService.ReadGeminiTokens(convId, dir.Path);
        Assert.Equal(2500, inp);
        Assert.Equal(12000, cache);

        var metrics = ContextMetricsService.Measure(
            new[]
            {
                new ChatEntry(Participants.UserId, "12345", DateTime.Now),
                new ChatEntry(Participants.GeminiId, "abcdef", DateTime.Now),
            },
            convId,
            dir.Path);

        Assert.Equal(2, metrics.MessageCount);
        Assert.Equal(11, metrics.TotalChars);
        Assert.Equal(14500, metrics.GeminiTotalTokens);
        Assert.Equal("2発言 · 11字 · 14,500 tok", ContextMetricsService.FormatLine(metrics));
    }

    [Fact]
    public void 不正な会話IDやファイルなしはnullになりアラート判定も各しきい値で動く()
    {
        using var dir = new TempDir();
        Assert.Equal((null, null), ContextMetricsService.ReadGeminiTokens(null, dir.Path));
        Assert.Equal((null, null), ContextMetricsService.ReadGeminiTokens("../escape", dir.Path));
        Assert.Equal((null, null), ContextMetricsService.ReadGeminiTokens("missing-id", dir.Path));

        var th = new SummarySettings(MaxMessages: 10, MaxChars: 100, MaxTokens: 1000);
        var stats = new Dictionary<string, (int, int)>();
        Assert.False(ContextMetricsService.IsOverThreshold(new ContextMetrics(9, 99, 500, 499, stats), th));
        Assert.True(ContextMetricsService.IsOverThreshold(new ContextMetrics(10, 50, null, null, stats), th));
        Assert.True(ContextMetricsService.IsOverThreshold(new ContextMetrics(2, 100, null, null, stats), th));
        Assert.True(ContextMetricsService.IsOverThreshold(new ContextMetrics(2, 10, 400, 600, stats), th));
    }

    [Fact]
    public async Task 新セッションの引き継ぎ要約は各AIの初回発言時だけ裏で添えられる()
    {
        using var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, n) => new AgentReply($"c{n}", $"c-conv-{n}"));
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, n) => new AgentReply($"g{n}", $"g-conv-{n}"));
        var log = new MarkdownLogWriter(dir.Path, DateTime.Now);

        var session = new ChatSession(
            new IChatAgent[] { claude, gemini },
            log,
            maxInjectChars: 4000,
            initialSummary: "## 決定事項\n- A案を採用");

        // 1回目: Claude と Gemini の両方に聞く -> 両方のプロンプトに引き継ぎ要約が入る
        await session.SendAsync("続きを相談", new[] { Participants.ClaudeId, Participants.GeminiId }, _ => { });
        Assert.Contains("【前の会議からの引き継ぎ要約】", claude.Calls[0].Prompt);
        Assert.Contains("A案を採用", claude.Calls[0].Prompt);
        Assert.Contains("【前の会議からの引き継ぎ要約】", gemini.Calls[0].Prompt);

        // 2回目: 会話IDがあるので引き継ぎ要約は添えない
        await session.SendAsync("2回目", new[] { Participants.ClaudeId }, _ => { });
        Assert.DoesNotContain("【前の会議からの引き継ぎ要約】", claude.Calls[1].Prompt);
    }

    [Fact]
    public async Task 要約して新しい会議へを実行すると要約を生成して新セッションへ切り替わる()
    {
        using var dir = new TempDir();
        var claude1 = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("了解です", "c1"));
        var claude2 = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("引き継ぎOK", "c2"));
        var summaryAgent = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("## 決定事項\n- テスト完了", "s1"));

        var count = 0;
        IChatSession CreateSession(string? summary)
        {
            count++;
            var agent = count == 1 ? claude1 : claude2;
            var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("g", "g1"));
            var chappy = new FakeAgent(Participants.ChappyId, "Chappy", (_, _, _) => new AgentReply("p", "p1"));
            var log = new MarkdownLogWriter(Path.Combine(dir.Path, $"m{count}"), DateTime.Now);
            return new ChatSession(new IChatAgent[] { agent, gemini, chappy }, log, 4000, workDir: dir.Path, initialSummary: summary);
        }

        var summaryService = new MeetingSummaryService(_ => summaryAgent);
        var settings = new SummarySettings(Enabled: true, MaxMessages: 2, MaxChars: 10000, MaxTokens: 100000);
        var vm = new MainViewModel(
            () => CreateSession(null),
            summarySessionFactory: CreateSession,
            summaryService: summaryService,
            summarySettings: settings);

        vm.Target = ChatTarget.Claude;
        vm.InputText = "テスト発言";
        await vm.SendAsync();

        // ユーザー発言 + Claude返答 = 2発言 >= MaxMessages(2) なのでアラートON
        Assert.True(vm.IsContextAlert);
        Assert.True(vm.ShowSummarizeButton);
        Assert.True(vm.CanSummarize);

        await vm.SummarizeAndNewMeetingAsync();

        Assert.Single(summaryAgent.Calls);
        Assert.Empty(vm.Messages); // 新セッションに切り替わり画面はクリア
        Assert.False(vm.IsContextAlert);

        // 新セッションで発言すると裏で引き継ぎ要約が渡される
        vm.InputText = "新しい会議です";
        await vm.SendAsync();
        Assert.Contains("テスト完了", claude2.Calls[0].Prompt);
    }

    [Fact]
    public async Task バックフィルは4発言未満と自動実行を除いてsummary_mdを生成する()
    {
        using var dir = new TempDir();
        var t = new DateTime(2026, 11, 10, 12, 0, 0);

        // 1) 対象: 通常会議・4発言
        var normalDir = Path.Combine(dir.Path, "20261110_120000", "通常会議");
        MeetingStore.SaveState(Path.Combine(normalDir, MeetingStore.StateFileName), new MeetingState
        {
            StartedAt = t,
            Entries = new List<ChatEntry>
            {
                new(Participants.UserId, "1", t),
                new(Participants.ClaudeId, "2", t),
                new(Participants.UserId, "3", t),
                new(Participants.GeminiId, "4", t),
            },
        });

        // 2) 対象外: 3発言
        var shortDir = Path.Combine(dir.Path, "20261110_120000", "短い会議");
        MeetingStore.SaveState(Path.Combine(shortDir, MeetingStore.StateFileName), new MeetingState
        {
            StartedAt = t,
            Entries = new List<ChatEntry>
            {
                new(Participants.UserId, "1", t),
                new(Participants.ClaudeId, "2", t),
                new(Participants.UserId, "3", t),
            },
        });

        // 3) 対象外: 自動実行・4発言
        var autoDir = Path.Combine(dir.Path, "20261110_120000", "自動 夜の制作 0100");
        MeetingStore.SaveState(Path.Combine(autoDir, MeetingStore.StateFileName), new MeetingState
        {
            StartedAt = t,
            IsScheduled = true,
            Entries = new List<ChatEntry>
            {
                new(Participants.UserId, "1", t),
                new(Participants.ClaudeId, "2", t),
                new(Participants.UserId, "3", t),
                new(Participants.GeminiId, "4", t),
            },
        });

        var summaryAgent = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("## 要約\n完了", "s"));
        var backfill = new MeetingSummaryBackfillService(new MeetingSummaryService(_ => summaryAgent));

        var count = await backfill.BackfillAllAsync(dir.Path, new SummarySettings(Enabled: true));
        Assert.Equal(1, count);
        Assert.True(File.Exists(Path.Combine(normalDir, MeetingStore.SummaryFileName)));
        Assert.False(File.Exists(Path.Combine(shortDir, MeetingStore.SummaryFileName)));
        Assert.False(File.Exists(Path.Combine(autoDir, MeetingStore.SummaryFileName)));
    }

    [Fact]
    public void 古い会議は30日以上でarchiveに移動され一覧から除外される()
    {
        using var dir = new TempDir();
        var now = new DateTime(2026, 11, 13, 12, 0, 0);
        var oldTime = new DateTime(2026, 10, 1, 10, 0, 0); // 43日前
        var recentTime = new DateTime(2026, 11, 1, 10, 0, 0); // 12日前

        var oldDir = Path.Combine(dir.Path, "20261001_100000", "古い会議");
        var oldStatePath = Path.Combine(oldDir, MeetingStore.StateFileName);
        MeetingStore.SaveState(oldStatePath, new MeetingState
        {
            StartedAt = oldTime,
            Entries = new List<ChatEntry> { new(Participants.UserId, "old", oldTime) },
        });
        // 最終更新日時も古くする
        File.SetLastWriteTime(oldStatePath, oldTime);
        Directory.SetLastWriteTime(oldDir, oldTime);

        var recentDir = Path.Combine(dir.Path, "20261101_100000", "最近の会議");
        MeetingStore.SaveState(Path.Combine(recentDir, MeetingStore.StateFileName), new MeetingState
        {
            StartedAt = recentTime,
            Entries = new List<ChatEntry> { new(Participants.UserId, "recent", recentTime) },
        });

        var moved = MeetingArchiveService.ArchiveOldMeetings(dir.Path, archiveDays: 30, now);
        Assert.Equal(1, moved);
        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(Path.Combine(dir.Path, MeetingStore.ArchiveDirName, "2026-10", "20261001_100000", "古い会議")));
        Assert.True(Directory.Exists(recentDir));

        var listed = MeetingStore.ListMeetings(dir.Path);
        Assert.Single(listed);
        Assert.Equal("最近の会議", listed[0].MeetingName);
    }

    [Fact]
    public async Task 過去会議から要約を引き継いで新しく開ける()
    {
        using var dir = new TempDir();
        var t = new DateTime(2026, 11, 10, 12, 0, 0);
        var pastDir = Path.Combine(dir.Path, "20261110_120000", "過去会議");
        MeetingStore.SaveState(Path.Combine(pastDir, MeetingStore.StateFileName), new MeetingState
        {
            StartedAt = t,
            Entries = new List<ChatEntry> { new(Participants.UserId, "議題A", t) },
        });
        MeetingStore.SaveSummary(pastDir, "## 決定事項\n- 既存要約");

        var claudeNext = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("ok", "c2"));
        IChatSession CreateSession(string? summary)
        {
            var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("g", "g1"));
            var chappy = new FakeAgent(Participants.ChappyId, "Chappy", (_, _, _) => new AgentReply("p", "p1"));
            var log = new MarkdownLogWriter(Path.Combine(dir.Path, "next"), DateTime.Now);
            return new ChatSession(new IChatAgent[] { claudeNext, gemini, chappy }, log, 4000, workDir: dir.Path, initialSummary: summary);
        }

        var vm = new MainViewModel(
            () => CreateSession(null),
            summarySessionFactory: CreateSession,
            summarySettings: new SummarySettings(Enabled: true));

        var past = MeetingStore.ListMeetings(dir.Path).Single();
        Assert.True(past.HasSummary);

        await vm.OpenPastMeetingWithSummaryAsync(past);
        Assert.Equal($"過去会議_{DateTime.Now:yyyyMMdd}", vm.RoomTitle);
        vm.Target = ChatTarget.Claude;
        vm.InputText = "続き";
        await vm.SendAsync();

        Assert.Contains("既存要約", claudeNext.Calls[0].Prompt);
    }

    [Fact]
    public void 簡易表示モード切替でSummaryAgentOptionsの表示名が更新され選択AIが維持される()
    {
        using var dir = new TempDir();
        IChatSession CreateSession()
        {
            var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("ok", "c1"));
            var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("ok", "g1"));
            var chappy = new FakeAgent(Participants.ChappyId, "Chappy", (_, _, _) => new AgentReply("ok", "p1"));
            var log = new MarkdownLogWriter(Path.Combine(dir.Path, "m"), DateTime.Now);
            return new ChatSession(new IChatAgent[] { claude, gemini, chappy }, log, 4000);
        }

        var vm = new MainViewModel(
            CreateSession,
            summarySettings: new SummarySettings(Enabled: true, AgentId: Participants.GeminiId));

        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is not null) raised.Add(e.PropertyName); };

        Assert.Contains(vm.SummaryAgentOptions, o => o.Id == Participants.GeminiId && o.Label == "Gemini");

        vm.IsBusinessMode = true;

        Assert.DoesNotContain(nameof(MainViewModel.SummaryAgentOptions), raised);
        Assert.Contains(nameof(MainViewModel.SelectedSummaryAgentId), raised);
        Assert.Contains(vm.SummaryAgentOptions, o => o.Id == Participants.GeminiId && o.Label == "Gemini");
        Assert.Equal(Participants.GeminiId, vm.SelectedSummaryAgentId);

        // WPF ComboBox が ItemsSource 差し替え時に一時的に null を書き戻しても、選択AIがリセットされない
        vm.SelectedSummaryAgentId = null!;
        Assert.Equal(Participants.GeminiId, vm.SelectedSummaryAgentId);
    }

    [Fact]
    public void 引き継ぎ時の部屋名に日付が付き同日重複があれば連番が付く()
    {
        var today = new DateTime(2026, 11, 14);

        // 基本: 部屋名_yyyyMMdd
        Assert.Equal(
            "なまけ会議室_20261114",
            MainViewModel.BuildHandoverRoomTitle("なまけ会議室", today, Array.Empty<string>(), 30));

        // 同日重複あり: _2, _3
        Assert.Equal(
            "なまけ会議室_20261114_2",
            MainViewModel.BuildHandoverRoomTitle("なまけ会議室", today, new[] { "なまけ会議室_20261114" }, 30));
        Assert.Equal(
            "なまけ会議室_20261114_3",
            MainViewModel.BuildHandoverRoomTitle("なまけ会議室", today, new[] { "なまけ会議室_20261114", "なまけ会議室_20261114_2" }, 30));

        // すでに _yyyyMMdd や _yyyyMMdd_2 が付いていれば、二重にせず置き換える
        Assert.Equal(
            "なまけ会議室_20261114_2",
            MainViewModel.BuildHandoverRoomTitle("なまけ会議室_20261113_2", today, new[] { "なまけ会議室_20261114" }, 30));

        // 最大文字数を超える場合はベース名側を切り詰めて日付サフィックスを残す
        var trimmed = MainViewModel.BuildHandoverRoomTitle("とても長い会議室の名前テスト用文字列", today, Array.Empty<string>(), 15);
        Assert.Equal(15, trimmed.Length);
        Assert.EndsWith("_20261114", trimmed);
    }

    [Fact]
    public async Task 要約AIが自動設定のとき先頭AIがエラーなら次の利用可能なAIへフォールバックする()
    {
        var entries = new[] { new ChatEntry(Participants.UserId, "議題", DateTime.Now) };
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => throw new InvalidOperationException("Claude error"));
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("## 決定事項\n- Geminiで要約成功", "g1"));
        var chappy = new FakeAgent(Participants.ChappyId, "Chappy", (_, _, _) => new AgentReply("unused", "p1"));

        var service = new MeetingSummaryService(s => s.AgentId switch
        {
            Participants.ClaudeId => claude,
            Participants.GeminiId => gemini,
            Participants.ChappyId => chappy,
            _ => null,
        });

        var result = await service.SummarizeAsync(entries, new SummarySettings(Enabled: true, AgentId: ""));

        Assert.Contains("Geminiで要約成功", result);
        Assert.Single(claude.Calls);
        Assert.Single(gemini.Calls);
        Assert.Empty(chappy.Calls);
    }

    [Fact]
    public async Task 要約AIが明示指定のときはエラーでも別AIへフォールバックしない()
    {
        var entries = new[] { new ChatEntry(Participants.UserId, "議題", DateTime.Now) };
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => throw new InvalidOperationException("Claude error"));
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("unused", "g1"));

        var service = new MeetingSummaryService(s => s.AgentId == Participants.ClaudeId ? claude : gemini);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SummarizeAsync(entries, new SummarySettings(Enabled: true, AgentId: Participants.ClaudeId)));
        Assert.Single(claude.Calls);
        Assert.Empty(gemini.Calls);
    }

    [Fact]
    public async Task 要約して新しい会議へを実行すると各AIの権限も新会議へ引き継がれる()
    {
        using var dir = new TempDir();
        var count = 0;
        IChatSession CreateSession(string? summary)
        {
            count++;
            var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("c", "c1"))
            {
                CanWrite = true,
                CanFull = true,
            };
            var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("g", "g1"))
            {
                CanWrite = true,
                CanFull = true,
            };
            var chappy = new FakeAgent(Participants.ChappyId, "Chappy", (_, _, _) => new AgentReply("p", "p1"))
            {
                CanWrite = true,
                CanFull = true,
            };
            var log = new MarkdownLogWriter(Path.Combine(dir.Path, $"m{count}"), DateTime.Now);
            return new ChatSession(new IChatAgent[] { claude, gemini, chappy }, log, 4000, initialSummary: summary);
        }

        var summaryAgent = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("## 決定事項\n- OK", "s1"));
        var vm = new MainViewModel(
            () => CreateSession(null),
            summarySessionFactory: CreateSession,
            summaryService: new MeetingSummaryService(_ => summaryAgent),
            summarySettings: new SummarySettings(Enabled: true));

        vm.ClaudeMember.SelectedLevelId = PermissionLevelOption.PartialId;
        vm.GeminiMember.SelectedLevelId = PermissionLevelOption.FullId;
        vm.Target = ChatTarget.Claude;
        vm.InputText = "テスト";
        await vm.SendAsync();

        await vm.SummarizeAndNewMeetingAsync();

        Assert.Equal(PermissionLevelOption.PartialId, vm.ClaudeMember.SelectedLevelId);
        Assert.Equal(PermissionLevelOption.FullId, vm.GeminiMember.SelectedLevelId);
        Assert.Equal(PermissionLevelOption.ReadId, vm.ChappyMember.SelectedLevelId);
    }

    [Fact]
    public async Task 過去会議から要約を引き継いで新しく開くと保存されていた権限が全部も含めて復元される()
    {
        using var dir = new TempDir();
        var t = new DateTime(2026, 11, 10, 12, 0, 0);
        var pastDir = Path.Combine(dir.Path, "20261110_120000", "権限あり会議");
        MeetingStore.SaveState(Path.Combine(pastDir, MeetingStore.StateFileName), new MeetingState
        {
            StartedAt = t,
            Entries = new List<ChatEntry> { new(Participants.UserId, "議題A", t) },
            Levels = new Dictionary<string, string>
            {
                [Participants.ClaudeId] = PermissionLevelOption.PartialId,
                [Participants.GeminiId] = PermissionLevelOption.FullId,
            },
        });
        MeetingStore.SaveSummary(pastDir, "## 決定事項\n- 既存要約");

        IChatSession CreateSession(string? summary)
        {
            var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("c", "c1"))
            {
                CanWrite = true,
                CanFull = true,
            };
            var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("g", "g1"))
            {
                CanWrite = true,
                CanFull = true,
            };
            var chappy = new FakeAgent(Participants.ChappyId, "Chappy", (_, _, _) => new AgentReply("p", "p1"))
            {
                CanWrite = true,
                CanFull = true,
            };
            var log = new MarkdownLogWriter(Path.Combine(dir.Path, "next"), DateTime.Now);
            return new ChatSession(new IChatAgent[] { claude, gemini, chappy }, log, 4000, initialSummary: summary);
        }

        var vm = new MainViewModel(
            () => CreateSession(null),
            summarySessionFactory: CreateSession,
            summarySettings: new SummarySettings(Enabled: true));

        var past = MeetingStore.ListMeetings(dir.Path).Single();
        await vm.OpenPastMeetingWithSummaryAsync(past);

        Assert.Equal(PermissionLevelOption.PartialId, vm.ClaudeMember.SelectedLevelId);
        Assert.Equal(PermissionLevelOption.FullId, vm.GeminiMember.SelectedLevelId);
        Assert.Equal(PermissionLevelOption.ReadId, vm.ChappyMember.SelectedLevelId);
    }

    [Fact]
    public void コンテキスト目安は20万トークンで1段階目25万トークンで2段階目になる()
    {
        var th = new SummarySettings();
        var stats = new Dictionary<string, (int, int)>();

        Assert.Equal(200000, th.WarnTokens);
        Assert.Equal(250000, th.MaxTokens);

        Assert.Equal(ContextAlertLevel.None, ContextMetricsService.GetAlertLevel(new ContextMetrics(10, 5000, 100000, 99999, stats), th));
        Assert.Equal(ContextAlertLevel.Warning, ContextMetricsService.GetAlertLevel(new ContextMetrics(10, 5000, 100000, 100000, stats), th));
        Assert.Equal(ContextAlertLevel.Danger, ContextMetricsService.GetAlertLevel(new ContextMetrics(10, 5000, 100000, 150000, stats), th));
    }

    [Fact]
    public void ClaudeとCodexのセッションjsonlからトークン数を取得しツールチップに表示できる()
    {
        using var claudeDir = new TempDir();
        using var codexDir = new TempDir();

        var claudeSub = Directory.CreateDirectory(Path.Combine(claudeDir.Path, "C--Work"));
        File.WriteAllLines(Path.Combine(claudeSub.FullName, "claude-123.jsonl"), new[]
        {
            """{"type":"assistant","message":{"usage":{"input_tokens":0,"cache_read_input_tokens":0}}}""",
            """{"type":"assistant","message":{"usage":{"input_tokens":1200,"cache_creation_input_tokens":300,"cache_read_input_tokens":4500}}}"""
        });

        var codexSub = Directory.CreateDirectory(Path.Combine(codexDir.Path, "2026", "11", "18"));
        File.WriteAllLines(Path.Combine(codexSub.FullName, "rollout-2026-11-18-codex-456.jsonl"), new[]
        {
            """{"type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":12000}}}}""",
            """{"type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":25662}}}}"""
        });

        var entries = new List<ChatEntry>
        {
            new(Participants.ClaudeId, "hello", DateTime.Now),
            new(Participants.ChappyId, "world", DateTime.Now),
        };
        var ids = new Dictionary<string, string>
        {
            [Participants.ClaudeId] = "claude-123",
            [Participants.ChappyId] = "codex-456",
        };

        var metrics = ContextMetricsService.Measure(entries, ids, claudeProjectsDir: claudeDir.Path, codexSessionsDir: codexDir.Path);
        Assert.Equal(6000, metrics.AgentTokens![Participants.ClaudeId]);
        Assert.Equal(25662, metrics.AgentTokens![Participants.ChappyId]);
        Assert.Equal("2発言 · 10字 · 25,662 tok", ContextMetricsService.FormatLine(metrics));

        var tip = ContextMetricsService.FormatTooltip(metrics, new SummarySettings());
        Assert.Contains("約6,000 tok", tip);
        Assert.Contains("約25,662 tok", tip);
    }

    [Fact]
    public void 過去の会議をそのまま開くと保存されていた権限が全部も含めて復元される()
    {
        using var dir = new TempDir();
        var t = new DateTime(2026, 11, 10, 12, 0, 0);
        var pastDir = Path.Combine(dir.Path, "20261110_120000", "権限あり会議");
        MeetingStore.SaveState(Path.Combine(pastDir, MeetingStore.StateFileName), new MeetingState
        {
            StartedAt = t,
            Entries = new List<ChatEntry>
            {
                new(Participants.UserId, "議題A", t, TargetIds: new[] { Participants.ClaudeId, Participants.GeminiId }),
                new(Participants.ClaudeId, "c-ok", t),
                new(Participants.GeminiId, "g-ok", t),
            },
            Levels = new Dictionary<string, string>
            {
                [Participants.ClaudeId] = PermissionLevelOption.PartialId,
                [Participants.GeminiId] = PermissionLevelOption.FullId,
            },
        });

        IChatSession CreateSession(MeetingState? restored = null)
        {
            var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("c", "c1"))
            {
                CanWrite = true,
                CanFull = true,
            };
            var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("g", "g1"))
            {
                CanWrite = true,
                CanFull = true,
            };
            var chappy = new FakeAgent(Participants.ChappyId, "Chappy", (_, _, _) => new AgentReply("p", "p1"))
            {
                CanWrite = true,
                CanFull = true,
            };
            var log = new MarkdownLogWriter(Path.Combine(dir.Path, "current"), DateTime.Now);
            return new ChatSession(
                new IChatAgent[] { claude, gemini, chappy },
                log,
                4000,
                restoredState: restored,
                openPastMeeting: path => CreateSession(MeetingStore.LoadState(path)));
        }

        var vm = new MainViewModel(() => CreateSession());
        vm.OpenPastMeeting(pastDir);

        Assert.Equal(PermissionLevelOption.PartialId, vm.ClaudeMember.SelectedLevelId);
        Assert.Equal(PermissionLevelOption.FullId, vm.GeminiMember.SelectedLevelId);
        Assert.Equal(PermissionLevelOption.ReadId, vm.ChappyMember.SelectedLevelId);
    }
}
