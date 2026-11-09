namespace NamakeKaigi.Models;

/// <summary>ハーネス(AIに許す道具・コマンドの範囲)の定義と、設定引数の読み書き。</summary>
public static class HarnessInfo
{
    /// <summary>画面のチェックボックスに並べる、よく使う組み込みツール(値と日本語ラベル)。</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> CommonTools = new[]
    {
        ("PowerShell", "💻 ターミナル操作 (PowerShell)"),
        ("Read", "📄 ファイル読み込み (Read)"),
        ("Edit", "✏️ ファイル編集 (Edit)"),
        ("Write", "📝 ファイル新規作成 (Write)"),
        ("Glob", "🔍 ファイル検索 (Glob)"),
        ("Grep", "🔎 文字列検索 (Grep)"),
        ("WebFetch", "🌐 URL取得 (WebFetch)"),
        ("WebSearch", "🔭 Web検索 (WebSearch)"),
        ("NotebookEdit", "📓 ノートブック編集 (NotebookEdit)"),
        ("TodoWrite", "✅ タスク管理 (TodoWrite)"),
        ("Agent", "🤝 サブエージェント呼び出し (Agent)"),
    };

    /// <summary>引数の並びから、--tools に指定された道具の一覧を取り出す。</summary>
    /// <param name="args">CLI引数の並び</param>
    /// <returns>道具名の並び(なければ空)</returns>
    public static IReadOnlyList<string> ParseTools(IReadOnlyList<string>? args)
    {
        if (args is null) return Array.Empty<string>();
        var result = new List<string>();
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "--tools")
            {
                foreach (var t in args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    // 旧表記の Bash は Windows環境では PowerShell と同じ枠として扱う
                    var normalized = string.Equals(t, "Bash", StringComparison.OrdinalIgnoreCase) ? "PowerShell" : t;
                    if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                        result.Add(normalized);
                }
            }
        }
        return result;
    }

    /// <summary>引数の並びから、--allowedTools に指定された許可コマンド(PowerShell(...)の中身、またはそのまま)を取り出す。</summary>
    /// <param name="args">CLI引数の並び</param>
    /// <returns>許可パターンの並び(例: "dotnet build*", "dotnet test*")</returns>
    public static IReadOnlyList<string> ParseAllowedCommands(IReadOnlyList<string>? args)
    {
        if (args is null) return Array.Empty<string>();
        var result = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is "--allowedTools" or "--allowed-tools")
            {
                for (var j = i + 1; j < args.Count && !args[j].StartsWith("--", StringComparison.Ordinal); j++)
                {
                    result.Add(UnwrapCommand(args[j]));
                    i = j;
                }
            }
        }
        return result;
    }

    /// <summary>道具名から、画面表示用のラベル(例: "📄 ファイル読み込み (Read)")を返す。</summary>
    /// <param name="tool">道具名</param>
    /// <returns>表示ラベル</returns>
    public static string LabelOf(string tool)
    {
        foreach (var (value, label) in CommonTools)
        {
            if (string.Equals(value, tool, StringComparison.OrdinalIgnoreCase))
                return label;
        }
        return tool;
    }

    /// <summary>Claudeのいまの状態(読むだけ / 一部できる / 何でもできる)に応じた、ツールチップ用の説明文を作る。</summary>
    /// <param name="settings">Claudeの設定</param>
    /// <param name="isWriter">いま書く担当(一部できる以上)か</param>
    /// <param name="commandsOn">いまコマンドも使うか</param>
    /// <param name="isFull">いま「全部」か</param>
    /// <returns>ツールチップに出す説明文</returns>
    public static string DescribeClaude(ClaudeSettings settings, bool isWriter, bool commandsOn, bool isFull = false)
    {
        if (isFull && settings.FullExtraArgs is { Count: > 0 })
        {
            return "【いまの段階: 全部 (許可は自動判定)】\n"
                   + "使えるツール: すべて(コマンド・Web・スラッシュコマンドを含む)\n"
                   + DescribeConnectors(settings.EnabledConnectors).Trim() + "\n"
                   + "・頼まれた操作は、確認なしで実行されます(普段のClaudeアプリと同じ)\n"
                   + "※ 道具をすべて読み込むため、1発言あたりのトークンが増えます";
        }

        var activeArgs = isWriter && commandsOn && settings.CommandExtraArgs is { Count: > 0 }
            ? settings.CommandExtraArgs
            : isWriter && settings.WriterExtraArgs is { Count: > 0 }
                ? settings.WriterExtraArgs
                : settings.ExtraArgs;

        var modeName = isWriter && commandsOn ? "読み書き ＋ コマンド実行"
            : isWriter ? "読み書き"
            : "読む";

        var tools = ParseTools(activeArgs);
        var toolLines = tools.Count > 0
            ? string.Join("\n  ・", tools.Select(LabelOf))
            : "(指定なし)";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"【いまの段階: {modeName}】");
        sb.AppendLine($"使えるツール ({tools.Count}件):");
        sb.Append($"  ・{toolLines}");

        var allowed = ParseAllowedCommands(activeArgs);
        if (allowed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"許可コマンド ({allowed.Count}件):");
            sb.Append("  ・" + string.Join("\n  ・", allowed));
        }

        sb.AppendLine();
        sb.Append("※右の歯車(ツール・許可の設定)から変更できます");
        return sb.ToString();
    }

    /// <summary>
    /// Claudeに、いまの段階でできること・できないことを伝える文(発言の先頭に添える)を作る。
    /// 「読み書き」は、設定の道具と許可コマンドから組み立てる(設定画面で変えれば、文も変わる)。
    /// </summary>
    /// <param name="settings">Claudeの設定</param>
    /// <param name="isWriter">いま書く担当(一部できる以上)か</param>
    /// <param name="commandsOn">いまコマンドも使うか</param>
    /// <param name="isFull">いま「全部」か</param>
    /// <returns>添える文(添えないときは空)</returns>
    public static string BuildClaudeStatusNote(ClaudeSettings settings, bool isWriter, bool commandsOn, bool isFull)
    {
        if (isFull)
        {
            var note = settings.FullNote?.Trim() ?? "";
            return note.Length > 0 ? note + DescribeConnectors(settings.EnabledConnectors) : "";
        }
        if (!isWriter) return settings.ReadOnlyNote?.Trim() ?? "";

        var args = commandsOn && settings.CommandExtraArgs is { Count: > 0 } ? settings.CommandExtraArgs : settings.WriterExtraArgs;
        var tools = ParseTools(args);
        var commands = commandsOn ? ParseAllowedCommands(args) : Array.Empty<string>();

        var sb = new System.Text.StringBuilder("[状態] いまは「読み書き」です。");
        sb.Append(tools.Count > 0 ? $"使える道具は {string.Join(", ", tools)} だけです。" : "");
        sb.Append(commands.Count > 0
            ? $"コマンドは {string.Join("、", commands)} なら、確認なしで通ります。それ以外のコマンド(git など)は、実行の前に、画面でユーザーに承認を求めます(拒否されたら実行されません)。"
            : "コマンドは使えません。");
        sb.Append("ファイルの書き換えは、確認なしで通ります。");
        sb.Append("使えない道具やコマンドが必要なときは、できないことを伝え、画面左で「全部」に切り替えてもらうよう、ユーザーに頼んでください。");
        return sb.ToString();
    }

    /// <summary>「全部」のときに使える外部連携を伝える一文を作る(先頭に空白を付ける)。</summary>
    /// <param name="enabled">ONにした連携の名前</param>
    /// <returns>説明の一文</returns>
    public static string DescribeConnectors(IReadOnlyList<string>? enabled) =>
        enabled is { Count: > 0 }
            ? $" 外部連携は {string.Join("、", enabled)} だけが使えます(ほかの連携はOFFです)。"
            : " 外部連携(Gmail・カレンダーなど)は、すべてOFFです。";

    /// <summary>Gemini(agy)のいまの状態に応じた、ツールチップ用の説明文を作る。</summary>
    /// <param name="settings">agyの設定</param>
    /// <param name="isWriter">いま書く担当か</param>
    /// <param name="isFull">いま「全部」か</param>
    /// <returns>ツールチップに出す説明文</returns>
    public static string DescribeAgy(AgySettings settings, bool isWriter, bool isFull = false)
    {
        if (isFull && settings.FullExtraArgs is { Count: > 0 })
        {
            return "【いまの段階: 全部 (許可の確認をすべて飛ばす)】\n"
                   + "・ファイルの書き換えもコマンド(git・ビルドなど)も、確認なしで実行されます(普段のAntigravityのTurboと同じ)\n"
                   + "⚠️ 危ない操作を止める判定はありません。頼み方に気をつけてください\n"
                   + "※右の歯車(ツール・許可の設定)から確認・変更できます";
        }

        // 起動引数は画面に出さない(普通の人には分かりにくいため。段階ごとの引数は README に書く)
        var modeName = isWriter ? "読み書き" : "読む";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"【いまの段階: {modeName}】");
        if (isWriter && !string.IsNullOrWhiteSpace(settings.WriterWarning))
            sb.AppendLine($"⚠️ {settings.WriterWarning}");
        else
            sb.AppendLine("・作業フォルダの中を読むだけ(ファイルの書き換えやコマンド実行はできません)");
        if (isWriter)
            sb.AppendLine("・コマンドは使えません(裏から呼ぶと、確認できないので自動で断られます)");
        sb.Append("※右の歯車(ツール・許可の設定)から確認・変更できます");
        return sb.ToString();
    }

    /// <summary>チャッピー(Codex)のいまの状態に応じた、ツールチップ用の説明文を作る。</summary>
    /// <param name="settings">Codexの設定</param>
    /// <param name="isWriter">いま書く担当(一部できる)か</param>
    /// <param name="isFull">いま「全部」か</param>
    /// <returns>ツールチップに出す説明文</returns>
    public static string DescribeCodex(CodexSettings settings, bool isWriter, bool isFull = false)
    {
        // 起動引数は画面に出さない(普通の人には分かりにくいため。段階ごとの引数は README に書く)
        var sb = new System.Text.StringBuilder();
        if (isFull)
        {
            sb.AppendLine("【いまの段階: 全部 (制限なし)】");
            sb.AppendLine("・ファイルの書き換えもコマンドも、ネット接続も、フォルダの外の操作も、確認なしで実行されます");
            sb.Append("⚠️ 危ない操作を止める仕組みはありません。頼み方に気をつけてください");
        }
        else if (isWriter)
        {
            sb.AppendLine("【いまの段階: 読み書き (作業フォルダの中だけ書き換え)】");
            sb.AppendLine("・作業フォルダの中のファイルを書き換えられます(コマンドも、その中だけ)");
            sb.Append("・ネット接続と、フォルダの外への書き込みはできません");
        }
        else
        {
            sb.AppendLine("【いまの段階: 読む】");
            sb.Append("・作業フォルダの中を読むだけ(ファイルの書き換えやコマンド実行はできません)");
        }
        return sb.ToString();
    }

    /// <summary>画面で選ばれたツールと許可コマンドから、新しい ClaudeSettings を組み立てる。</summary>
    /// <param name="current">元の設定</param>
    /// <param name="readOnlyTools">読み取り専用時のツール一覧</param>
    /// <param name="writerTools">書く担当時のツール一覧</param>
    /// <param name="allowedCommands">コマンドON時の許可コマンド一覧</param>
    /// <returns>更新した ClaudeSettings</returns>
    public static ClaudeSettings BuildClaudeSettings(
        ClaudeSettings current,
        IReadOnlyList<string> readOnlyTools,
        IReadOnlyList<string> writerTools,
        IReadOnlyList<string> allowedCommands)
    {
        var cleanRead = readOnlyTools.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cleanRead.Count == 0) cleanRead.Add("Read");

        var cleanWrite = writerTools.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cleanWrite.Count == 0) cleanWrite.AddRange(new[] { "Read", "Edit", "Write" });

        // 書く担当のみのときは、許可なしコマンドが走らないよう PowerShell を外した一覧にする
        var writerOnlyTools = cleanWrite.Where(t => !string.Equals(t, "PowerShell", StringComparison.OrdinalIgnoreCase)).ToList();
        if (writerOnlyTools.Count == 0) writerOnlyTools.Add("Read");

        // コマンドも使うときは、書く担当のツールに PowerShell を必ず含める
        var cmdTools = cleanWrite.ToList();
        if (!cmdTools.Contains("PowerShell", StringComparer.OrdinalIgnoreCase))
            cmdTools.Add("PowerShell");

        var extraArgs = new List<string>
        {
            "--disable-slash-commands",
            "--strict-mcp-config",
            "--tools",
            string.Join(",", cleanRead),
        };

        var writerExtraArgs = new List<string>
        {
            "--disable-slash-commands",
            "--strict-mcp-config",
            "--tools",
            string.Join(",", writerOnlyTools),
            "--permission-mode",
            "acceptEdits",
        };

        var cleanCmds = allowedCommands
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .Select(WrapCommand)
            .Distinct()
            .ToList();

        var commandExtraArgs = new List<string>
        {
            "--disable-slash-commands",
            "--strict-mcp-config",
            "--tools",
            string.Join(",", cmdTools),
            "--permission-mode",
            "acceptEdits",
        };
        if (cleanCmds.Count > 0)
        {
            commandExtraArgs.Add("--allowedTools");
            commandExtraArgs.AddRange(cleanCmds);
        }

        return current with
        {
            ExtraArgs = extraArgs,
            WriterExtraArgs = writerExtraArgs,
            CommandExtraArgs = cleanCmds.Count > 0 ? commandExtraArgs : current.CommandExtraArgs,
        };
    }

    private static string UnwrapCommand(string raw)
    {
        const string prefix = "PowerShell(";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && raw.EndsWith(')'))
            return raw[prefix.Length..^1];
        return raw;
    }

    private static string WrapCommand(string cmd)
    {
        if (cmd.Contains('(') && cmd.EndsWith(')')) return cmd;
        return $"PowerShell({cmd})";
    }
}
