namespace NamakeKaigi.Models;

/// <summary>Claude CLIの設定。</summary>
/// <param name="Executable">claude の実行ファイル(名前だけならPATHから探す)</param>
/// <param name="Model">使うモデル(別名でも、正式名でもよい)</param>
/// <param name="ExtraArgs">追加の引数(使う道具の絞り込みなど)</param>
/// <param name="Models">画面で選べるモデルの一覧(省略時は sonnet / opus / haiku)</param>
/// <param name="WriterExtraArgs">「書く担当」にしたときの追加の引数(ExtraArgs の代わりに使う。省略・空なら書く担当にできない)</param>
/// <param name="ReadOnlyNote">書く担当でないとき、本文の先頭に添えて、いまの状態(読み取り専用)を知らせる文(省略・空なら添えない)</param>
/// <param name="CommandExtraArgs">「コマンドも使う」にしたときの追加の引数(WriterExtraArgs の代わりに使う。許すコマンドを名前で絞って渡す。省略・空ならコマンドは使えない)</param>
/// <param name="Effort">使う労力(low / medium / high / xhigh / max。省略時は指定なし)</param>
/// <param name="FullExtraArgs">「全部」にしたときの追加の引数(道具を絞らず、許可は自動判定。省略・空なら「全部」にできない)</param>
/// <param name="FullNote">「全部」のとき、本文の先頭に添えて、いまの状態を知らせる文(省略・空なら添えない)</param>
/// <param name="EnabledConnectors">「全部」のときに使う外部連携の名前(`claude mcp list` の名前。載っていない連携は使わない。省略・空ならすべて使わない)</param>
/// <param name="ConnectorListCacheSec">外部連携の一覧を取り直すまでの秒数(取るのに数秒かかるため、使い回す)</param>
/// <param name="ConnectorListTimeoutSec">外部連携の一覧を取るときの制限時間(秒。過ぎたら連携をすべて使わない)</param>
public sealed record ClaudeSettings(string Executable, string Model, IReadOnlyList<string> ExtraArgs,
    IReadOnlyList<string>? Models = null, IReadOnlyList<string>? WriterExtraArgs = null, string? ReadOnlyNote = null,
    IReadOnlyList<string>? CommandExtraArgs = null, string? Effort = null,
    IReadOnlyList<string>? FullExtraArgs = null, string? FullNote = null,
    IReadOnlyList<string>? EnabledConnectors = null, int ConnectorListCacheSec = 300, int ConnectorListTimeoutSec = 30);

/// <summary>agy(Gemini側)の設定。</summary>
/// <param name="Executable">agy.exe のパス</param>
/// <param name="ExtraArgs">追加の引数(--mode plan など)</param>
/// <param name="NoToolNote">本文の先頭に添える「ツールは使わない」の添え書き</param>
/// <param name="Model">使うモデル(空なら指定せず、agyの既定に任せる)</param>
/// <param name="Models">agy models が取れなかったときに選べるモデルの一覧</param>
/// <param name="WriterExtraArgs">「書く担当」にしたときの追加の引数(ExtraArgs の代わりに使う。省略・空なら書く担当にできない)</param>
/// <param name="WriterNote">書く担当のとき、本文の先頭に添える文(NoToolNote の代わり。範囲を言葉で頼むのに使う)</param>
/// <param name="WriterWarning">書く担当にしたとき、画面に出す注意(例: 作業フォルダの外にも書ける)</param>
/// <param name="Effort">使う労力(low / medium / high / xhigh / max。省略時は指定なし)</param>
/// <param name="FullExtraArgs">「何でもできる(Turbo)」にしたときの追加の引数(許可の確認をすべて飛ばす。省略・空なら「全部」にできない)</param>
/// <param name="FullNote">「全部」のとき、本文の先頭に添える文(NoToolNote / WriterNote の代わり)</param>
/// <param name="FullWarning">「全部」にしたとき、画面に出す注意(判定がなく、止まらないこと)</param>
/// <param name="UpdateCheckArgs">設定画面の「更新を確認」で、agyの更新係を直接起動する引数(省略・空なら標準。直接起動すると、15分以内の確認を飛ばす決まりにかからない)</param>
/// <param name="UpdateStatusFile">更新係が結果を書く記録ファイル(%USERPROFILE% などの環境変数を使える。省略・空なら標準)</param>
/// <param name="UpdateCheckTimeoutSec">更新の確認を待つ上限(秒)</param>
public sealed record AgySettings(string Executable, IReadOnlyList<string> ExtraArgs, string NoToolNote,
    string? Model = null, IReadOnlyList<string>? Models = null, IReadOnlyList<string>? WriterExtraArgs = null,
    string? WriterNote = null, string? WriterWarning = null, string? Effort = null,
    IReadOnlyList<string>? FullExtraArgs = null, string? FullNote = null, string? FullWarning = null,
    IReadOnlyList<string>? UpdateCheckArgs = null, string? UpdateStatusFile = null, int UpdateCheckTimeoutSec = 180);

/// <summary>
/// AIに意見を頼むときの頼み書き。「全員」宛てで1人目のあとに答える人の本文の最後に添える文と、吹き出しの「🔍 〇〇に聞く」で送る文。
/// 「{first}」は1人目の名前、「{speaker}」は意見を聞く対象の発言者の名前に置き換える。
/// </summary>
/// <param name="ReviewNote">1人目のあとに答える人に、意見を頼む文(読む段階のとき、またはあとに答える人が2人以上のとき)</param>
/// <param name="WorkNote">1人目のあとに答える人が1人で、書き換えできるときの頼み書き(続きの作業を頼む。同じファイルは触らず、コミットは1人だけ)</param>
/// <param name="OpinionNote">吹き出しの「🔍 〇〇に聞く」で送る文(この後ろに、対象の発言を添える)</param>
public sealed record RelaySettings(
    string ReviewNote =
        "[順番] いまは「全員」宛てで、{first}のあとに答える番です。上の{first}の答えを読んで、" +
        "あなたの視点でどう思うかを、簡潔に伝えてください(賛成・反対・気になる点・別の考え方など。粗探しをする必要はなく、同じ内容の繰り返しも不要です)。",
    string WorkNote =
        "[順番] いまは「全員」宛てで、{first}のあとに答える番です。上の{first}の答えを読んでから、その続きの作業をしてください。" +
        "{first}が書き換えたファイルは触らないでください。コミットは{first}に任せてください(コミットするのは1人だけ)。",
    string OpinionNote =
        "直前の{speaker}の発言について、あなたの視点でどう思うかを、簡潔に教えてください。" +
        "賛成・反対・気になる点・別の考え方など、思ったことをそのままで構いません(粗探しをする必要はありません)。");

/// <summary>アプリ全体の設定(settings.json の中身)。</summary>
/// <param name="Claude">Claude CLIの設定</param>
/// <param name="Agy">agyの設定</param>
/// <param name="WorkDir">CLIを動かす作業フォルダ</param>
/// <param name="MeetingsDir">会議ログの保存先</param>
/// <param name="MaxInjectChars">未読発言を添える長さの上限(文字)</param>
/// <param name="TimeoutSec">CLI1回のタイムアウト(秒)</param>
/// <param name="Theme">画面の配色テーマID(forest / mint / matcha / dark-forest / dark-slate / office-light / office-dark。省略時は forest)</param>
/// <param name="Appearance">簡易表示モードやカスタム表示名の設定(省略時は標準)</param>
/// <param name="AllOrder">「全員」宛ての答える順番(parallel / claude-first / gemini-first / chappy-first。省略時は parallel=同時)</param>
/// <param name="Relay">意見を頼むときの頼み書き(順番のときに1人目のあとに答える人へ添える文、「🔍 〇〇に聞く」で送る文。省略時は標準の文)</param>
/// <param name="KeepAlive">CLIを起動したままにして、2回目からの発言を速くするか(省略時はtrue。falseなら発言ごとに起動する)</param>
/// <param name="Codex">チャッピー(Codex CLI)の設定(省略時は既定値)</param>
/// <param name="CallNames">発言の先頭の呼びかけ(「ジェミ、〜」)で宛先を切り替える呼び名の一覧(省略した宛先は標準の呼び名)</param>
/// <param name="Voice">返答の読み上げの設定(省略時は標準)</param>
/// <param name="DefaultParticipants">新しい会議の最初に参加しているAIの識別名(claude / gemini / chappy)。省略・空なら全員。1on1で始めたいときは1人だけ書く</param>
/// <param name="MaxRooms">同時に開ける会議室(タブ)の数の上限。起動時は常に1部屋。省略時は3</param>
/// <param name="Roster">メンバー名簿(定義ファイルを探す場所と上限)の設定(省略時は作業フォルダから探す)</param>
/// <param name="RoomTitleMaxChars">会議室の名前の長さの上限(文字)。省略時は30</param>
/// <param name="ScheduleFile">予約(定期実行)の保存ファイル schedule.json のパス。省略・空なら、自動実行の機能は使わない(ボタンも出さない)。settings.json はビルドで上書きされるので、予約の中身はこのファイルに置く</param>
/// <param name="Remote">リモート(スマホのブラウザから参加)の設定。省略時はオフ</param>
/// <param name="LinkProbe">返答の中のパスが実在するかを調べるときの待ち時間などの設定(省略時は標準)</param>
/// <param name="StreamRenderIntervalMs">返答が流れてくる間、吹き出しの中身を作り直す最短の間隔(ミリ秒)。省略時は200。0なら届くたびに作り直す</param>
public sealed record AppSettings(
    ClaudeSettings Claude,
    AgySettings Agy,
    string WorkDir,
    string MeetingsDir,
    int MaxInjectChars,
    int TimeoutSec,
    string? Theme = null,
    AppearanceSettings? Appearance = null,
    string? AllOrder = null,
    RelaySettings? Relay = null,
    bool KeepAlive = true,
    CodexSettings? Codex = null,
    CallNameSettings? CallNames = null,
    VoiceSettings? Voice = null,
    IReadOnlyList<string>? DefaultParticipants = null,
    int MaxRooms = 3,
    RosterSettings? Roster = null,
    int RoomTitleMaxChars = 30,
    int StreamRenderIntervalMs = 200,
    string? ScheduleFile = null,
    RemoteSettings? Remote = null,
    LinkProbeSettings? LinkProbe = null);

/// <summary>チャッピー(Codex CLI)の設定。段階は、読むだけ(既定) / 一部できる(書き換え) / 何でもできる。</summary>
/// <param name="Executable">実行ファイル(PATHにあれば名前だけでよい)</param>
/// <param name="Model">使うモデル(空ならCodexの既定)</param>
/// <param name="ExtraArgs">読むだけのときの起動の追加の引数(既定: -c sandbox_mode=read-only。Gitの外でも動くよう --skip-git-repo-check)</param>
/// <param name="Note">読むだけのとき、本文の先頭に添える、いまの状態の知らせ</param>
/// <param name="WriterExtraArgs">一部できる(書き換え)のときの起動の追加の引数(既定: -c sandbox_mode=workspace-write)</param>
/// <param name="FullExtraArgs">何でもできるときの起動の追加の引数(既定: -c sandbox_mode=danger-full-access)</param>
/// <param name="WriterNote">一部できるとき、本文の先頭に添える知らせ</param>
/// <param name="FullNote">何でもできるとき、本文の先頭に添える知らせ</param>
/// <param name="ModelListArgs">モデル一覧を取る引数(既定: debug models)</param>
/// <param name="Effort">思考の深さ(low / medium / high / xhigh / max。空ならCodexの設定の既定)</param>
public sealed record CodexSettings(
    string Executable = "codex",
    string? Model = null,
    IReadOnlyList<string>? ExtraArgs = null,
    string? Note = null,
    IReadOnlyList<string>? WriterExtraArgs = null,
    IReadOnlyList<string>? FullExtraArgs = null,
    string? WriterNote = null,
    string? FullNote = null,
    IReadOnlyList<string>? ModelListArgs = null,
    string? Effort = null)
{
    /// <summary>既定の、モデル一覧を取る引数(codex debug models。アカウントで選べるモデルの一覧を、JSONで出す)。</summary>
    public static readonly IReadOnlyList<string> DefaultModelListArgs = new[] { "debug", "models" };

    // 権限は「-c sandbox_mode=...」で渡す。`codex exec resume`(2回目以降の発言)は --sandbox を受け付けず、-c なら初回も2回目も通るため。
    // 値に引用符を付けない(Windowsの .cmd 経由で引用符が崩れるのを避ける。TOMLとして読めない値は、そのまま文字列として使われる)

    /// <summary>既定の起動の追加の引数(読むだけ)。</summary>
    public static readonly IReadOnlyList<string> DefaultExtraArgs = new[] { "-c", "sandbox_mode=read-only", "--skip-git-repo-check" };

    /// <summary>既定の起動の追加の引数(一部できる=作業フォルダの中の書き換え。コマンドも、その中だけ・ネット接続なし)。</summary>
    public static readonly IReadOnlyList<string> DefaultWriterExtraArgs = new[] { "-c", "sandbox_mode=workspace-write", "--skip-git-repo-check" };

    /// <summary>既定の起動の追加の引数(何でもできる=制限なし。コマンド・ネット接続・フォルダの外も)。</summary>
    public static readonly IReadOnlyList<string> DefaultFullExtraArgs = new[] { "-c", "sandbox_mode=danger-full-access", "--skip-git-repo-check" };

    /// <summary>既定の状態の知らせ(読むだけ)。</summary>
    public const string DefaultNote =
        "[状態] あなたは、この会議の参加者(チャッピー)です。いまは「読む」です。ファイルを読むことはできますが、書き換えはできません。" +
        "書き換えが必要なときは、クローディかジェミリーに頼むよう、ユーザーに伝えてください。\n\n";

    /// <summary>既定の状態の知らせ(一部できる)。</summary>
    public const string DefaultWriterNote =
        "[状態] あなたは、この会議の参加者(チャッピー)です。いまは「読み書き」です。作業フォルダの中のファイルを書き換えられます。" +
        "コマンドも使えますが、作業フォルダの中だけで、ネット接続とフォルダの外への書き込みはできません。" +
        "消す・上書きするなど、戻しにくい操作は、実行する前に、何をするかを書いてユーザーの返事を待ってください。\n\n";

    /// <summary>既定の状態の知らせ(何でもできる)。</summary>
    public const string DefaultFullNote =
        "[状態] あなたは、この会議の参加者(チャッピー)です。いまは「全部」です。コマンド・ネット接続・フォルダの外の操作も、" +
        "確認なしでできます(制限はありません)。消す・送る・公開する・pushするなど、戻せない操作や外に影響する操作は、" +
        "実行する前に、何をするかを書いてユーザーの返事を待ってください。\n\n";
}
