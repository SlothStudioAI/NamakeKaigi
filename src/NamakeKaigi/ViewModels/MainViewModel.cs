using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>宛先の選択肢。</summary>
public enum ChatTarget
{
    Claude,
    Gemini,
    Chappy,
    Everyone,
}

/// <summary>メイン画面の状態とコマンド。会議の進行はIChatSessionに任せる。</summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly Func<IChatSession> _sessionFactory;

    private readonly Func<DateTime> _clock;

    private readonly MessageLists _lists = new();
    private readonly Dictionary<string, MessageViewModel> _streaming = new();

    // 作業の様子の欄に出す、つぶやきの一言の最大文字数(長い前置きで吹き出しが膨らまないように)
    private const int StatusNoteMaxChars = 120;

    private readonly Dictionary<string, string> _rawStreamingText = new();

    private IChatSession _session;

    // 順番のときの2人目が、1人目の答えを待っている間に左の欄へ出す一言
    private const string WaitingTurnText = "順番待ち(1人目の答えのあとに答えます)";

    // 道具を使う前に、ユーザーの承認を待っている間の一言
    private const string ApprovalWaitingText = "承認を待っています";

    /// <summary>画面の状態を作り、最初の会議を始める。</summary>
    /// <param name="sessionFactory">新しい会議を作る関数</param>
    /// <param name="clock">現在時刻の取得(試験で差し替える。省略時は現在時刻)</param>
    /// <param name="speech">返答を声で読み上げる部品(省略すると、読み上げは使えない)</param>
    /// <param name="voice">読み上げの設定(省略時は標準)</param>
    /// <param name="defaultParticipants">新しい会議の最初に参加しているAIの識別名(省略・空・知らない名前だけなら全員)</param>
    /// <param name="roomTitleMaxChars">会議室の名前の長さの上限(文字。1未満は1にそろえる)</param>
    /// <param name="installedAis">CLIが見つかったAIの識別名(省略時は全員。入っていないAIは、カードを出さず参加させない)</param>
    public MainViewModel(Func<IChatSession> sessionFactory, Func<DateTime>? clock = null,
        ISpeechService? speech = null, VoiceSettings? voice = null,
        IReadOnlyList<string>? defaultParticipants = null, int roomTitleMaxChars = 30,
        IReadOnlyCollection<string>? installedAis = null)
    {
        _roomTitleMaxChars = Math.Max(1, roomTitleMaxChars);
        _sessionFactory = sessionFactory;
        _defaultParticipants = NormalizeParticipants(defaultParticipants);
        _clock = clock ?? (() => DateTime.Now);
        _readAloud = new ReadAloudController(speech, voice ?? new VoiceSettings(), name => Raise(name));
        ToggleReadAloudCommand = new RelayCommand(() => ReadAloudOn = !ReadAloudOn, () => ReadAloudAvailable);
        StopSpeakingCommand = new RelayCommand(_readAloud.Stop);
        _session = sessionFactory();
        _session.SetMeetingTitle(_roomTitle); // 最初の会議のフォルダ名は、タブの名前(会議室1)
        _selectedThemeId = ThemeCatalog.GetOrDefault(_session.ThemeId).Id;
        _selectedTimeoutSec = _session.TimeoutSec;
        _selectedAllOrder = _session.AllOrder;
        Members = Participants.All.Select(p => new MemberViewModel(p, OnModelChosen, OnWriterChosen, OnCommandsChosen, OnEffortChosen, OnFullChosen, OnPresenceChosen)
        {
            IsInstalled = p.Id == Participants.UserId || installedAis is null || installedAis.Contains(p.Id),
        }).ToList();
        ApplyPresence(_defaultParticipants);
        ApplyWritePermissions();
        foreach (var m in Members)
        {
            // 段階が変わったら、順番の説明(作ってから確認／考えてから作る など)を出し直す
            m.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MemberViewModel.SelectedLevelId) or nameof(MemberViewModel.IsWriter)
                    or nameof(MemberViewModel.IsFull))
                {
                    Raise(nameof(AllOrderHint));
                }
            };
        }
        SendCommand =new RelayCommand(() => _ = SendAsync(), () => !string.IsNullOrWhiteSpace(InputText) || Attachments.Count > 0);
        CancelCommand = new RelayCommand(CancelPending, () => IsBusy);
        NewMeetingCommand = new RelayCommand(StartNewMeeting);
        SelectRoomCommand = new RelayCommand(() => Rooms?.Select(this));
        CloseRoomCommand = new RelayCommand(() => Rooms?.Close(this), () => Rooms?.CanCloseRoom ?? false);
        Attachments.CollectionChanged += (_, _) =>
        {
            Raise(nameof(HasAttachments));
            ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
        };
        ApplyModelLabels();
        ApplyAppearanceToAll();
        _ = RefreshModelOptionsAsync();
    }

    /// <summary>全員の発言を、時系列に並べた一覧(まとめて見る画面)。</summary>
    public ObservableCollection<MessageViewModel> Messages => _lists.All;

    /// <summary>クローディとのやりとりだけの一覧(別々に見る画面の左の列)。</summary>
    public ObservableCollection<MessageViewModel> ClaudeMessages => _lists.Claude;

    /// <summary>ジェミリーとのやりとりだけの一覧(別々に見る画面の右の列)。</summary>
    public ObservableCollection<MessageViewModel> GeminiMessages => _lists.Gemini;

    /// <summary>チャッピーとのやりとりだけの一覧(別々に見る画面の3つ目の列)。</summary>
    public ObservableCollection<MessageViewModel> ChappyMessages => _lists.Chappy;

    public IReadOnlyList<MemberViewModel> Members { get; }

    /// <summary>ユーザー本人(入力欄の横の小さなアバターに使う)。</summary>
    public MemberViewModel UserMember => Member(Participants.UserId);

    public MemberViewModel ClaudeMember => Member(Participants.ClaudeId);

    public MemberViewModel GeminiMember => Member(Participants.GeminiId);

    public MemberViewModel ChappyMember => Member(Participants.ChappyId);

    public ICommand SendCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand NewMeetingCommand { get; }

    /// <summary>いずれかのAIが返答中(考え中・返答作成中)ならtrue。停止ボタンの表示に使う。</summary>
    public bool IsBusy => Members.Any(m => m.IsThinking);

    /// <summary>実行中のAIの返答待ちをすべてキャンセル(停止)する。</summary>
    public void CancelPending()
    {
        _readAloud.Stop();
        _session.CancelPending();
    }

    private MemberViewModel Member(string id) => Members.First(m => m.Participant.Id == id);
}
