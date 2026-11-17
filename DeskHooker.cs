using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DeskHooker
{
    public sealed class Theme
    {
        public string id, name, summary, safety, file, author, icon;
        public bool community;
        public bool user; // a card the user added on this PC (id starts with "x-"); deletable, never synced
        public string[] instructions;
        public CommunityVideo[] videos;
    }

    internal static class Program
    {
        // 배포 버전(364~370: Knox 메일 초대 합치기 추가): 설정 창과 팝업의 Head Title에 표시된다.
        // Teum-Setup.iss의 TeumVersion, package.ps1의 기본 $Version과 함께 세 곳을 항상 같은 값으로 맞춘다.
        internal const string AppVersion = "1.4.0";

        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        // Session-wide silence since the last mouse/keyboard input. Unsigned subtraction keeps the
        // value correct across the Environment.TickCount wraparound. Zero when the API is unavailable.
        internal static TimeSpan IdleFor()
        {
            var info=new LASTINPUTINFO(); info.cbSize=(uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds((uint)Environment.TickCount-info.dwTime);
        }

        [STAThread] private static void Main(string[] args)
        {
            bool testing = args.Contains("--ui-test");
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\DeskHooker.Teum" + (testing ? ".Test" : ""), out created))
            {
                if (!created) { MessageBox.Show("이미 실행 중입니다. 작업 표시줄의 쉼 아이콘에서 설정을 여세요.", "쉼"); return; }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { Application.Run(new ReminderHost(testing, args.Contains("--background"))); }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.txt"), error.ToString());
                    Environment.ExitCode = 1;
                    if (!testing) MessageBox.Show(error.Message, "쉼 시작 오류");
                }
            }
        }
    }

    internal sealed class ReminderHost : ApplicationContext
    {
        public readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        public readonly bool Testing;
        public readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        // 창 제목표시줄·작업표시줄·트레이가 함께 쓰는 앱 아이콘. build.ps1이 /win32icon으로
        // exe에 심은 app.ico(themes\아이콘.png 기반, 331)를 꺼내 쓴다. WinForms는 Form.Icon을
        // SystemIcons 등으로 덮어쓰면 exe 리소스 대신 그것을 보여 주므로, 여기서 한 번만 로드해
        // 트레이·설정 창·팝업 모두 같은 아이콘을 쓰게 한다. 실패 시(아이콘 없는 시험 빌드 등) 기존처럼
        // 시스템 정보 아이콘으로 돌아간다.
        private static Icon appIcon;
        internal static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information; }
                    catch { appIcon = SystemIcons.Information; }
                }
                return appIcon;
            }
        }
        public Theme[] Themes;
        private Theme[] baseThemes;
        // ---- User's own card edits: built-ins can be hidden, own cards added. Stored in the data folder,
        // never synced. The dance card is never removable.
        private readonly HashSet<string> removedThemeIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Theme> userThemes = new List<Theme>();
        private List<Theme> communityThemes = new List<Theme>();
        private sealed class UserThemeStore { public string[] removed; public Theme[] custom; }
        private const string CommunityNotice = "다른 사람이 공유한 내용으로 검증된 전문 안내가 아닙니다. 몸이 보내는 신호를 먼저 따르세요.";
        private volatile SyncResult pendingSync;
        private bool syncing;
        private string syncEtag, syncError;
        private DateTime? lastSync;
        private int communityCount, communityRejected;
        private List<SyncEntry> syncEntries = new List<SyncEntry>(); // what the last sync downloaded, keyed by file name + git sha
        private System.Windows.Forms.Timer syncTimer;
        // ---- Knox calendar meeting reminders: a separate popup path, fed by a file the knox-ics-export skill writes ----
        private System.Windows.Forms.Timer knoxSyncTimer;
        private volatile string pendingKnoxResult; // null = not finished yet; "" = success; otherwise an error message. Applied on the UI thread by Tick().
        private bool knoxSyncing;
        private string knoxSyncError;
        private DateTime? lastKnoxSync;
        private DateTime? knoxIcsLoadedAt; // UTC last-write-time of knox-schedule.ics, as last read
        private List<CalendarEntry> calendarEntries = new List<CalendarEntry>();
        private HashSet<string> firedKnoxUids = new HashSet<string>(StringComparer.Ordinal);
        private List<KnoxSyncLogEntry> knoxSyncLog = new List<KnoxSyncLogEntry>(); // newest last, capped by KnoxSync.LogLimit
        private readonly object knoxSyncLogLock = new object();
        // 350: 한 번의 일정 알림 화면에 함께 띄우는 일정들(시작 시각 오름차순). 같은 시각에 겹친 일정은
        // 한 화면에 모두 나열하고, 알림이 떠 있는 동안 새로 due가 된 일정은 같은 화면에 합쳐진다.
        private List<CalendarEntry> knoxPopupEvents = new List<CalendarEntry>();
        // 364: Knox 수신 메일 회의 초대 — POP3(pop3.samsung.net:995)로 오늘 받은 "[지원WP] [Invitation]"
        // 메일의 회의를 일정 알림에 병합한다. 비밀번호는 Windows 자격 증명 관리자에만 있고
        // settings.json·동작 기록·오류 메시지에는 절대 나오지 않는다.
        private sealed class KnoxMailResult { public string Error; public List<CalendarEntry> Meetings; public int MailCount, InvitationCount; }
        private volatile KnoxMailResult pendingKnoxMail; // null = not finished yet. Applied on the UI thread by Tick().
        private bool knoxMailSyncing;
        private string knoxMailError;
        private DateTime? lastKnoxMailSync;
        private List<CalendarEntry> knoxMailMeetings = new List<CalendarEntry>(); // today's invitation mails, uid-stable across syncs
        // 373: 회의 시작 전 알림의 SLSI Avatar 메신저 송신(보내기 전용). 연결 토큰(aa_…)은 자격 증명
        // 관리자에만 있고 settings.json·동작 기록·오류 메시지에는 절대 나오지 않는다.
        private sealed class MessengerResult { public bool Login, Ok, Sent; public string Detail; }
        private volatile MessengerResult pendingMessenger; // null = nothing finished. Applied on the UI thread by Tick().
        private volatile bool messengerLoggingIn;
        private string messengerError;
        private DateTime? lastMessengerSend;
        private HashSet<string> messengerSentKeys = new HashSet<string>(StringComparer.Ordinal); // FiredKey of meetings already sent today
        public ReminderSettings Settings;
        public DateTime NextDue;
        public bool Exiting;
        public WebWindow SettingsWindow, Popup;
        private readonly string dataFolder;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer;
        private bool locked, smokeStarted;
        private int cursor;
        private readonly Random rng = new Random();
        private readonly Queue<string> pendingRound = new Queue<string>(); // themes still to show in this reminder's round
        private string lastShown;
        // "몇 시에 실행" 카드가 오늘 이미 떴는지: theme id → 마지막으로 띄운 날짜의 시각.
        private readonly Dictionary<string, DateTime> firedSchedules = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private DateTime lastTick = DateTime.Now;
        private string notice = "간격과 주제를 고른 뒤 알림 시작을 누르세요.";

        public ReminderHost(bool testing, bool background)
        {
            Testing = testing;
            dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), testing ? "DeskHooker-Test" : "DeskHooker");
            Directory.CreateDirectory(dataFolder);
            var themes = Json.Deserialize<Theme[]>(File.ReadAllText(Path.Combine(Root, "themes.json")));
            var list = new List<Theme>(themes);
            list.Add(new Theme { id="dance", name="댄스 안무", summary="공식 안무 영상으로 원본 동작 따라 보기", file="", safety="서서 하는 안무입니다. 충분한 공간을 확보하고 어려운 점프·비틀기는 건너뛰세요. 허리 통증이 있으면 이 주제 대신 저강도 자세 전환을 고르세요.", instructions=new string[] { "공식 채널의 연습 영상을 선택하세요.", "처음에는 느리게 보고, 편한 범위에서만 따라 하세요." } });
            list.Add(new Theme { id="relax", name="릴렉스 음향", summary="편안한 소리를 들으며 잠깐 쉬기", file="", safety="소리는 작게 시작하고, 불편하면 바로 끄세요. 운전·보행 중에는 듣지 마세요. 치료가 아닌 짧은 휴식용 안내입니다.", instructions=new string[0] });
            list.Add(new Theme { id="recipe", name="요리 레시피", summary="짧은 요리 영상으로 잠깐 쉬어 가기", file="", safety="영상은 구경하며 쉬는 용도입니다. 실제로 따라 할 때는 불과 칼을 조심하고, 알레르기나 식이 제한이 있으면 재료를 직접 확인하세요.", instructions=new string[0] });
            baseThemes = list.ToArray();
            LoadUserThemes();
            int cachedRejected;
            syncEntries = LoadCommunityCache();
            var cached = ToThemes(syncEntries.Select(e => e.Text), out cachedRejected);
            communityCount = cached.Count; communityRejected = cachedRejected;
            communityThemes = cached;
            RebuildThemes();
            foreach (var theme in Themes.Where(t => !String.IsNullOrEmpty(t.file)))
            {
                string full = Path.GetFullPath(Path.Combine(Root, theme.file));
                if (!full.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new FileNotFoundException("주제 이미지가 없습니다: " + theme.file);
            }
            // First run gets the 기본 프리셋 (303~308); UI smoke testing and corrupted-file recovery
            // keep the neutral constructor baseline below.
            Settings = testing ? new ReminderSettings() : ReminderSettings.DefaultPreset();
            string config = Path.Combine(dataFolder, "settings.json");
            if (!testing && File.Exists(config))
            {
                try { Settings = Json.Deserialize<ReminderSettings>(File.ReadAllText(config)); PruneThemeIds(); ReminderSchedule.Validate(Settings, Themes.Select(t => t.id)); }
                catch { Settings = new ReminderSettings(); notice = "이전 설정을 읽지 못해 안전한 기본값으로 시작했습니다. 설정을 다시 저장하세요."; }
            }
            else if (!testing)
            {
                // 첫 실행(신규 설치 — 인스톨러가 settings.json을 지운다): 프리셋의 내가 만든 카드까지
                // 깔아 둔다(337). UI 시험(testing)은 항상 깨끗한 상태로 시작한다.
                EnsurePresetCard(); SaveUserThemes();
            }
            // The "all at once" run modes were removed from the menu; settings saved with them keep working as the one-per-reminder modes.
            if (Settings.ThemeMode == ThemeRotation.SequentialAll) Settings.ThemeMode = ThemeRotation.OrderedOne;
            else if (Settings.ThemeMode == ThemeRotation.RandomAll) Settings.ThemeMode = ThemeRotation.RandomOne;
            NextDue = ReminderSchedule.Next(DateTime.Now, Settings);
            tray = new NotifyIcon { Icon=AppIcon, Text="쉼 · 스트레칭과 작은 휴식", Visible=true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("간격·주제 설정", null, delegate { ShowSettings(); });
            menu.Items.Add("지금 한 번 보기", null, delegate { StartRound(); });
            menu.Items.Add("공유된 쉼 지금 동기화", null, delegate { StartSync(true); });
            menu.Items.Add("알림 일시정지", null, delegate { Settings.Enabled=false; Persist(); SendState(SettingsWindow); UpdateStatus(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("알림기 완전히 종료", null, delegate { Quit(); });
            tray.ContextMenuStrip=menu;
            tray.DoubleClick += delegate { ShowSettings(); };
            timer = new System.Windows.Forms.Timer { Interval=1000 };
            timer.Tick += delegate { Tick(); };
            timer.Start();
            SystemEvents.SessionSwitch += SessionChanged;
            SystemEvents.PowerModeChanged += PowerChanged;
            SettingsWindow = new WebWindow(this, false, null);
            if (background && Settings.Enabled) SettingsWindow.Shown += delegate { SettingsWindow.Hide(); };
            SettingsWindow.Show();
            syncTimer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
            syncTimer.Tick += delegate { StartSync(false); };
            syncTimer.Start();
            StartSync(false);
            firedKnoxUids = LoadKnoxState();
            messengerSentKeys = LoadDayKeys(MessengerStatePath, "sent");
            knoxSyncLog = LoadKnoxSyncLog();
            ReloadKnoxIcsIfChanged();
            knoxSyncTimer = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };
            knoxSyncTimer.Tick += delegate { StartKnoxSync(false); };
            knoxSyncTimer.Start();
            if (Settings.KnoxCalendarEnabled) StartKnoxSync(false);
            if (Settings.KnoxMailEnabled) StartKnoxMailSync(false);
        }

        // ---- Community sharing: items live in the repository's community/ folder ----
        private string CachePath { get { return Path.Combine(dataFolder, "community-cache.json"); } }

        private List<SyncEntry> LoadCommunityCache()
        {
            var raws = new List<SyncEntry>();
            try
            {
                if (Testing || !File.Exists(CachePath)) return raws;
                var map = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(CachePath));
                syncEtag = map.ContainsKey("etag") ? map["etag"] as string : null;
                var items = map.ContainsKey("items") ? map["items"] as System.Collections.IEnumerable : null;
                if (items != null) foreach (object item in items)
                {
                    var entry = item as Dictionary<string, object>; // entries from older cache formats are ignored and re-downloaded
                    if (entry == null || !entry.ContainsKey("name") || !entry.ContainsKey("sha") || !entry.ContainsKey("text")) continue;
                    raws.Add(new SyncEntry { Name = Convert.ToString(entry["name"]), Sha = Convert.ToString(entry["sha"]), Text = Convert.ToString(entry["text"]) });
                }
            }
            catch { syncEtag = null; raws.Clear(); }
            return raws;
        }

        private void SaveCommunityCache(List<SyncEntry> entries)
        {
            if (Testing) return;
            try
            {
                string tmp = CachePath + ".new";
                File.WriteAllText(tmp, Json.Serialize(new Dictionary<string, object> { { "etag", syncEtag }, { "items", entries.Select(e => new Dictionary<string, object> { { "name", e.Name }, { "sha", e.Sha }, { "text", e.Text } }).ToList() } }));
                if (File.Exists(CachePath)) File.Replace(tmp, CachePath, null); else File.Move(tmp, CachePath);
            }
            catch { /* The cache only speeds up the next start; syncing again rebuilds it. */ }
        }

        // Every item is re-validated here, even from our own cache; invalid or duplicate ones are skipped.
        private List<Theme> ToThemes(IEnumerable<string> raws, out int rejected)
        {
            var themes = new List<Theme>(); var seen = new HashSet<string>(StringComparer.Ordinal); rejected = 0;
            foreach (string raw in raws)
            {
                try
                {
                    CommunityItem item = CommunityContent.Parse(raw);
                    if (!seen.Add(item.Id)) { rejected++; continue; }
                    themes.Add(new Theme { id = "c-" + item.Id, name = item.Name, summary = item.Summary, instructions = item.Instructions,
                        safety = item.Safety + " " + CommunityNotice, file = "", author = item.Author, community = true, videos = item.Videos, icon = item.Icon });
                }
                catch (ArgumentException) { rejected++; }
            }
            return themes;
        }

        private void StartSync(bool manual)
        {
            if (Testing || syncing || Exiting) return;
            syncing = true; syncError = null; SendThemes();
            string etag = syncEtag;
            var known = syncEntries.ToList();
            Task.Factory.StartNew(delegate
            {
                SyncResult result;
                try { result = CommunitySync.Fetch(etag, known); }
                catch (Exception error) { result = new SyncResult { Error = error.Message }; }
                pendingSync = result; // applied on the UI thread by the 1-second Tick
            });
        }

        private void ApplySync(SyncResult result)
        {
            syncing = false;
            if (result.Error != null) { syncError = result.Error; SendThemes(); return; }
            lastSync = DateTime.Now;
            if (!result.NotModified)
            {
                int rejected;
                var list = ToThemes(result.Entries.Select(e => e.Text), out rejected);
                communityCount = list.Count; communityRejected = rejected + result.Rejected;
                syncEtag = result.Etag;
                communityThemes = list;
                RebuildThemes();
                syncEntries = result.Entries;
                SaveCommunityCache(result.Entries);
                PruneRemovedSelections();
            }
            SendThemes();
        }

        // ---- User's own card edits (user-themes.json in the data folder) ----
        private string UserThemesPath { get { return Path.Combine(dataFolder, "user-themes.json"); } }

        private void LoadUserThemes()
        {
            try
            {
                if (!File.Exists(UserThemesPath)) return;
                var store = Json.Deserialize<UserThemeStore>(File.ReadAllText(UserThemesPath));
                if (store == null) return;
                if (store.removed != null)
                    foreach (string id in store.removed)
                        if (!String.IsNullOrEmpty(id) && id != "dance") removedThemeIds.Add(id);
                if (store.custom != null)
                    foreach (Theme theme in store.custom)
                        if (theme != null && !String.IsNullOrEmpty(theme.id) && theme.id.StartsWith("x-", StringComparison.Ordinal) && !String.IsNullOrEmpty(theme.name))
                        {
                            theme.user = true; theme.community = false; theme.file = null;
                            userThemes.Add(theme);
                        }
            }
            catch { removedThemeIds.Clear(); userThemes.Clear(); } // unreadable edits are safer discarded
        }

        private void SaveUserThemes()
        {
            File.WriteAllText(UserThemesPath, Json.Serialize(new UserThemeStore { removed = removedThemeIds.ToArray(), custom = userThemes.ToArray() }));
        }

        // ---- 프리셋 기본 쉼 카드(337): shim-cards-20261113-2228.json 내보내기에 담긴, 프리셋의 일부가 된
        // 내가 만든 카드. 첫 실행(신규 설치 — 인스톨러가 settings.json을 지운 뒤)과 '기본 프리셋으로
        // 되돌리기' 때 없으면 다시 깐다. 일반 사용자 카드와 같은 성격(◈ · 삭제 가능)이라 기존 카드와
        // 충돌하지 않는다. instructions가 비어 있어 목록에 글 안내('text') 없이 영상만 골라진다 —
        // 내보낸 파일의 최종 모습(listEdits.removed.text)과 같다.
        private void EnsurePresetCard()
        {
            if (userThemes.Any(t => t.id == "x-0a04b57c")) return;
            userThemes.Add(new Theme { id="x-0a04b57c", name="아침에 하는 5분 긍정확언 (아침 확언 명상)", summary="아침에 하는 5분 긍정확언 (아침 확언 명상)", safety="너무 길게 하지 마", icon="◈", user=true,
                videos=new CommunityVideo[] { new CommunityVideo { id="zGifCNokUy8", title="아침에 하는 5분 긍정확언 (아침 확언 명상)" } } });
        }

        // Themes = built-ins (minus removed) + the user's own cards + shared community cards (minus removed).
        private void RebuildThemes()
        {
            var list = new List<Theme>();
            foreach (Theme theme in baseThemes) if (!removedThemeIds.Contains(theme.id)) list.Add(theme);
            list.AddRange(userThemes);
            foreach (Theme theme in communityThemes) if (!removedThemeIds.Contains(theme.id)) list.Add(theme);
            Themes = list.ToArray();
        }

        // Settings must never reference a card that no longer exists; the first live card is the safe fallback.
        private void PruneThemeIds()
        {
            var known = new HashSet<string>(Themes.Select(t => t.id));
            PruneSchedules(known);
            string[] kept = Settings.ThemeIds.Where(id => known.Contains(id)).ToArray();
            if (kept.Length == Settings.ThemeIds.Length) return;
            Settings.ThemeIds = kept;
            if (kept.Length == 0 && (Settings.Schedules == null || Settings.Schedules.Count == 0))
                Settings.ThemeIds = new string[] { Themes.Length > 0 ? Themes[0].id : "stretch" };
            cursor = 0; pendingRound.Clear();
        }

        // A card's "몇 시에 실행" entry dies with the card.
        private void PruneSchedules(HashSet<string> known)
        {
            if (Settings.Schedules == null) return;
            var kept = Settings.Schedules.Where(kv => known.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (kept.Count != Settings.Schedules.Count) Settings.Schedules = kept;
        }

        // A shared item can be deleted upstream while it is still ticked; settings must never keep an unknown id.
        private void PruneRemovedSelections()
        {
            var known = new HashSet<string>(Themes.Select(t => t.id));
            PruneSchedules(known);
            string[] kept = Settings.ThemeIds.Where(id => known.Contains(id)).ToArray();
            if (kept.Length == Settings.ThemeIds.Length) return;
            Settings.ThemeIds = kept.Length > 0 ? kept : new string[] { Themes.Length > 0 ? Themes[0].id : "stretch" };
            cursor = 0; pendingRound.Clear(); Persist();
            notice = "공유가 삭제된 주제를 선택 목록에서 뺐습니다.";
        }

        private object SyncInfo()
        {
            return new { syncing = syncing, last = lastSync.HasValue ? lastSync.Value.ToString("o") : null, count = communityCount, rejected = communityRejected, error = syncError, offline = Testing };
        }

        // ---- Knox calendar meeting reminders ----
        private string KnoxIcsPath { get { return Path.Combine(dataFolder, "knox-schedule.ics"); } }
        private string KnoxStatePath { get { return Path.Combine(dataFolder, "knox-schedule-state.json"); } }
        private string KnoxSyncLogPath { get { return Path.Combine(dataFolder, "knox-sync-log.json"); } }

        private object KnoxSyncInfo()
        {
            KnoxSyncLogEntry[] recent;
            lock (knoxSyncLogLock)
            {
                int count = Math.Min(12, knoxSyncLog.Count); // the newest few lines are plenty for the settings window
                recent = new KnoxSyncLogEntry[count];
                for (int i = 0; i < count; i++) recent[i] = knoxSyncLog[knoxSyncLog.Count - count + i];
            }
            Array.Reverse(recent); // newest first, matching how the list is shown
            // The upcoming meetings the "일정 알리기 시험" picker offers, soonest first. Only meetings that
            // have not started yet make sense to rehearse a lead-time popup for. 364: invitation-mail
            // meetings are part of the same list, flagged so the picker can label them.
            ReloadKnoxIcsIfChanged();
            string account = MessengerLinkedAccountSafe();
            var upcoming = CombinedCalendarEntries().Where(e => e.StartLocal > DateTime.Now).OrderBy(e => e.StartLocal).Take(30)
                .Select(e => new { uid = e.Uid, title = e.Title, start = e.StartLocal.ToString("o"), mail = e.Uid.StartsWith("knoxmail-", StringComparison.Ordinal) }).ToArray();
            return new { enabled = Settings.KnoxCalendarEnabled, syncing = knoxSyncing, last = lastKnoxSync.HasValue ? lastKnoxSync.Value.ToString("o") : null, error = knoxSyncError, offline = Testing, log = recent, calendar = upcoming,
                mail = new { enabled = Settings.KnoxMailEnabled, syncing = knoxMailSyncing, hasPassword = KnoxMailHasPasswordSafe(), last = lastKnoxMailSync.HasValue ? lastKnoxMailSync.Value.ToString("o") : null, error = knoxMailError, meetings = knoxMailMeetings.Count },
                messenger = new { enabled = Settings.MessengerNotifyEnabled, linked = account != null, account = account, loggingIn = messengerLoggingIn, error = messengerError, last = lastMessengerSend.HasValue ? lastMessengerSend.Value.ToString("o") : null } };
        }

        // 364: 일정 알림이 보는 일정 목록 — ics 캘린더에 오늘 받은 초대 메일의 회의를 병합한 것.
        // 같은 시작 시각에 같은(또는 포함되는) 제목의 회의는 캘린더가 이미 알려 주므로 메일 쪽에서 빠진다.
        private List<CalendarEntry> CombinedCalendarEntries()
        {
            return Settings.KnoxMailEnabled ? KnoxMail.MergeMeetingEntries(calendarEntries, knoxMailMeetings) : calendarEntries;
        }

        private List<KnoxSyncLogEntry> LoadKnoxSyncLog()
        {
            var log = new List<KnoxSyncLogEntry>();
            try
            {
                if (Testing || !File.Exists(KnoxSyncLogPath)) return log;
                var loaded = Json.Deserialize<KnoxSyncLogEntry[]>(File.ReadAllText(KnoxSyncLogPath));
                if (loaded != null) log.AddRange(loaded);
                if (log.Count > KnoxSync.LogLimit) log.RemoveRange(0, log.Count - KnoxSync.LogLimit);
            }
            catch { log = new List<KnoxSyncLogEntry>(); }
            return log;
        }

        private void SaveKnoxSyncLog()
        {
            if (Testing) return;
            try
            {
                KnoxSyncLogEntry[] snapshot;
                lock (knoxSyncLogLock) { snapshot = knoxSyncLog.ToArray(); }
                string tmp = KnoxSyncLogPath + ".new";
                File.WriteAllText(tmp, Json.Serialize(snapshot));
                if (File.Exists(KnoxSyncLogPath)) File.Replace(tmp, KnoxSyncLogPath, null); else File.Move(tmp, KnoxSyncLogPath);
            }
            catch { /* best-effort; a missed save only loses history, never reminders */ }
        }

        // Safe to call from background threads: the log list and its file are only touched under the lock.
        private void AddKnoxSyncLog(KnoxSyncLogEntry entry)
        {
            lock (knoxSyncLogLock)
            {
                KnoxSync.AppendLog(knoxSyncLog, entry);
                SaveKnoxSyncLog();
            }
        }

        // Which meeting reminders already popped up today; a new calendar day starts with a clean slate.
        private HashSet<string> LoadKnoxState() { return LoadDayKeys(KnoxStatePath, "fired"); }
        private void SaveKnoxState() { SaveDayKeys(KnoxStatePath, "fired", firedKnoxUids); }
        // 373: 메신저로 이미 보낸 회의(FiredKey) — 팝업 기록과 따로 둔다(팝업은 자리비움 동안 미뤄지지만 메신저는 그때도 간다).
        private string MessengerStatePath { get { return Path.Combine(dataFolder, "knox-messenger-state.json"); } }

        // A per-day key set persisted as {"day": "yyyy-MM-dd", "<listName>": [...]}; another day reads as empty.
        private HashSet<string> LoadDayKeys(string path, string listName)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (Testing || !File.Exists(path)) return keys;
                var map = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                string day = map.ContainsKey("day") ? map["day"] as string : null;
                if (day != DateTime.Now.ToString("yyyy-MM-dd")) return keys;
                var items = map.ContainsKey(listName) ? map[listName] as System.Collections.IEnumerable : null;
                if (items != null) foreach (object item in items) keys.Add(Convert.ToString(item));
            }
            catch { keys.Clear(); }
            return keys;
        }

        private void SaveDayKeys(string path, string listName, HashSet<string> keys)
        {
            if (Testing) return;
            try
            {
                string tmp = path + ".new";
                File.WriteAllText(tmp, Json.Serialize(new Dictionary<string, object> { { "day", DateTime.Now.ToString("yyyy-MM-dd") }, { listName, keys.ToList() } }));
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            catch { /* best-effort; a missed save only risks one repeat alert within the lead window */ }
        }

        // Re-reads knox-schedule.ics only when its contents changed since the last read, so the knox-ics-export
        // skill (or anything else) can refresh it on its own schedule without DeskHooker having to know how.
        private void ReloadKnoxIcsIfChanged()
        {
            try
            {
                if (Testing || !File.Exists(KnoxIcsPath)) return;
                DateTime mtime = File.GetLastWriteTimeUtc(KnoxIcsPath);
                if (knoxIcsLoadedAt.HasValue && mtime <= knoxIcsLoadedAt.Value) return;
                calendarEntries = CalendarSchedule.ParseIcs(File.ReadAllText(KnoxIcsPath));
                knoxIcsLoadedAt = mtime;
            }
            catch { /* keep the previously parsed entries; the next refresh may fix a transient read error */ }
        }

        // A manual fetch (테스트용 바로 가져오기) runs even when the feature toggle is off, so the
        // pipeline can be tested before committing to reminders; the timer only fetches when enabled.
        // 364: it also refreshes today's invitation mails alongside the calendar (each part keeps its
        // own enabled/toggle rules — see StartKnoxMailSync).
        private void StartKnoxSync(bool manual)
        {
            if (Testing || Exiting) return;
            StartKnoxMailSync(manual);
            if (knoxSyncing) return;
            if (!manual && !Settings.KnoxCalendarEnabled) return;
            knoxSyncing = true; knoxSyncError = null; SendThemes();
            Task.Factory.StartNew(delegate { pendingKnoxResult = RunKnoxIcsExport() ?? ""; });
        }

        private const int KnoxHttpTimeoutMs = 15000;

        // Fetches the next 48 hours of the user's own Knox calendar straight from the chatbot2 bot server's
        // plain HTTP API and rewrites knox-schedule.ics. Deterministic I/O only: no opencode, no skill, no
        // LLM in the loop, so this cannot hang on a model round trip or an unanswerable headless permission
        // prompt the way the earlier opencode-driven design did. Returns null on success, or a short status
        // message on failure; every attempt is logged for the settings window.
        // The server requires a Bearer token on the schedules endpoint. The token is baked into the
        // binary so a fresh install on anyone's PC works immediately with no environment variable or
        // token file setup. knox-token.txt in the data folder (first non-empty line) still wins when
        // present, so an operator can swap in a replacement token without rebuilding.
        private const string KnoxTokenDefault = "teum_app_token";

        private string KnoxApiToken()
        {
            try
            {
                string path = Path.Combine(dataFolder, "knox-token.txt");
                if (File.Exists(path))
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length > 0) return trimmed;
                    }
            }
            catch { }
            return KnoxTokenDefault;
        }

        private string RunKnoxIcsExport()
        {
            string knoxId = Environment.UserName;
            string url = KnoxSync.ApiUrl + Uri.EscapeDataString(knoxId);
            string token = KnoxApiToken();
            string body;
            try { body = HttpGetText(url, KnoxHttpTimeoutMs, token); }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    string hint = "토큰이 서버에서 거부됐어요. 관리자에게 토큰 값을 다시 확인하세요.";
                    AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "인증 실패(401)" });
                    return "Knox 일정 서버 인증에 실패했습니다. " + hint;
                }
                if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                {
                    AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "Knox ID가 서버에 등록되어 있지 않아요(" + knoxId + ")" });
                    return "Knox ID '" + knoxId + "'가 일정 서버에 등록돼 있지 않습니다. 관리자에게 등록을 요청하세요.";
                }
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "서버 호출 실패: " + error.Message });
                return "Knox 일정 서버에 연결하지 못했습니다: " + error.Message;
            }
            catch (Exception error)
            {
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "서버 호출 실패: " + error.Message });
                return "Knox 일정 서버에 연결하지 못했습니다: " + error.Message;
            }
            List<KnoxSchedule> schedules;
            try { schedules = KnoxSync.ParseSchedules(body); }
            catch (ArgumentException error)
            {
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "응답 형식 오류: " + KnoxSync.FirstLine(error.Message, 120) });
                return "Knox 일정 서버의 응답을 이해하지 못했습니다: " + error.Message;
            }
            string ics = KnoxSync.BuildIcs(schedules);
            try
            {
                string tmp = KnoxIcsPath + ".new";
                File.WriteAllText(tmp, ics);
                if (File.Exists(KnoxIcsPath)) File.Replace(tmp, KnoxIcsPath, null); else File.Move(tmp, KnoxIcsPath);
            }
            catch (Exception error)
            {
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "일정 파일 쓰기 실패: " + error.Message });
                return "일정 파일을 쓰지 못했습니다: " + error.Message;
            }
            // The success line carries what was actually fetched, so the settings log answers
            // "무엇이 왔나" without opening the ics file.
            string summary = KnoxSync.SummarizeSchedules(schedules, 200);
            AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = true, Detail = "성공(" + schedules.Count + "건)" + (summary.Length > 0 ? ": " + summary : "") });
            return null;
        }

        // Plain HTTP GET with a bounded timeout; the chatbot2 server is plaintext HTTP on the trusted
        // intranet (no TLS to negotiate). Throws WebException on a non-2xx status, same as HttpWebRequest
        // always does, so the caller can tell a 401 (bad/missing token) and a 404 (unregistered Knox ID)
        // apart from any other failure.
        private static string HttpGetText(string url, int timeoutMs, string bearerToken)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "DeskHooker-Teum"; request.Accept = "application/json";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearerToken;
            request.Timeout = timeoutMs; request.ReadWriteTimeout = timeoutMs;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                return reader.ReadToEnd();
        }

        // ---- 364: Knox 수신 메일 — POP3 클라이언트·자격 증명 관리자·동기화 본체 ----

        // POP3 실패의 두 갈래: 로그인 거부(비밀번호 확인 대상)와 접속·프로토콜 계열(사내망/VPN 확인 대상).
        private sealed class Pop3AuthException : Exception { public Pop3AuthException() : base("POP3 login rejected") { } }
        private sealed class Pop3UpstreamException : Exception { public Pop3UpstreamException(string message) : base(message) { } }

        // A minimal blocking POP3 client for the one job this feature needs: reverse-scan today's
        // inbox (chatbot2 app/mail_pop3_client.py 계약 이식). DELE is never sent, so the mailbox and
        // its read flags stay untouched. Lines travel through a Latin-1 char view (1 byte = 1 char)
        // and are cast back losslessly, so 8-bit mail bodies survive until KnoxMail decodes them.
        private sealed class Pop3Client : IDisposable
        {
            private readonly TcpClient client;
            private readonly SslStream stream;

            private Pop3Client(TcpClient client, SslStream stream) { this.client = client; this.stream = stream; }

            public static Pop3Client Connect(string host, int port, int timeoutMs)
            {
                var client = new TcpClient();
                try
                {
                    client.NoDelay = true;
                    client.ReceiveTimeout = timeoutMs;
                    client.SendTimeout = timeoutMs;
                    client.Connect(host, port);
                    var stream = new SslStream(client.GetStream(), false);
                    stream.AuthenticateAsClient(host);
                    var pop3 = new Pop3Client(client, stream);
                    pop3.ReadLine(); // 서버 인사말 "+OK …" — 줄이 -ERR이면 첫 명령에서 실패로 드러난다
                    return pop3;
                }
                catch (Exception)
                {
                    try { client.Close(); } catch { }
                    throw;
                }
            }

            private string ReadLine()
            {
                var line = new StringBuilder();
                while (true)
                {
                    int b = stream.ReadByte();
                    if (b < 0) throw new IOException("POP3 연결이 끊겼습니다");
                    if (b == '\n') break;
                    line.Append((char)(b & 0xFF));
                }
                string text = line.ToString();
                return text.EndsWith("\r", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text;
            }

            private string Command(string command)
            {
                byte[] payload = Encoding.ASCII.GetBytes(command + "\r\n");
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
                string response = ReadLine();
                if (response.StartsWith("-ERR", StringComparison.Ordinal)) throw new Pop3UpstreamException(response);
                return response;
            }

            public void Login(string user, string password)
            {
                try
                {
                    Command("USER " + user);
                    Command("PASS " + password);
                }
                catch (Pop3UpstreamException)
                {
                    // -ERR 배너에 자격증명이 되돌아오지는 않지만, 방어적으로 응답 내용을 담지 않는다.
                    throw new Pop3AuthException();
                }
            }

            public int MessageCount()
            {
                Command("LIST");
                int count = 0;
                while (ReadLine() != ".") count++;
                return count;
            }

            public byte[] Top(int messageNumber) { Command("TOP " + messageNumber + " 0"); return ReadMultiline(); }
            public byte[] Retr(int messageNumber) { Command("RETR " + messageNumber); return ReadMultiline(); }

            // Multi-line response up to the lone "." terminator, with byte-stuffing (".." → ".") undone.
            private byte[] ReadMultiline()
            {
                var bytes = new List<byte>();
                while (true)
                {
                    string line = ReadLine();
                    if (line == ".") break;
                    if (line.StartsWith(".", StringComparison.Ordinal)) line = line.Substring(1);
                    foreach (char c in line) bytes.Add((byte)(c & 0xFF));
                    bytes.Add((byte)'\n');
                }
                return bytes.ToArray();
            }

            public void Dispose()
            {
                try { Command("QUIT"); } catch { } // 세션 정리 실패는 조회 결과와 무관 — 조용히 폐기
                try { stream.Close(); } catch { }
                try { client.Close(); } catch { }
            }
        }

        // ---- Windows 자격 증명 관리자: POP3 비밀번호의 유일한 저장소. 비밀번호는 POP3 소켓과 이 사이를
        // 오가는 일 없이 settings.json·동작 기록·오류 메시지에 나오는 일이 없다.
        private const string KnoxMailCredentialTarget = "DeskHooker.KnoxMail";
        private const int CredentialTypeGeneric = 1, CredentialPersistLocalMachine = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public int Flags;
            public int Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            // wincred.h 순서: CredentialBlobSize(DWORD)가 CredentialBlob(LPBYTE)보다 먼저다. 순서를
            // 바꾸면 CredWrite가 87(잘못된 매개변수)로 실패한다.
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credentialPtr);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref NativeCredential credential, int flags);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);
        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credential);

        private static void KnoxMailSavePassword(string password)
        {
            if (String.IsNullOrEmpty(password)) { KnoxMailDeletePassword(); return; }
            CredSaveSecret(KnoxMailCredentialTarget, Environment.UserName, password, "자격 증명 관리자에 비밀번호를 저장하지 못했습니다");
        }

        private static string KnoxMailReadPassword()
        {
            string user;
            return CredReadSecret(KnoxMailCredentialTarget, out user);
        }

        private static void KnoxMailDeletePassword()
        {
            CredDelete(KnoxMailCredentialTarget, CredentialTypeGeneric, 0); // 없으면 실패만 돌아온다 — 그대로 둔다
        }

        // Generic credential-manager slot: the secret is UTF-16 bytes in CredentialBlob, userName is
        // a display label only (never a secret).
        private static void CredSaveSecret(string target, string userName, string secret, string failure)
        {
            byte[] blob = Encoding.Unicode.GetBytes(secret);
            IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
            Marshal.Copy(blob, 0, blobPtr, blob.Length); // AllocHGlobal은 초기화 안 된 힙을 준다 — 반드시 직접 복사한다
            IntPtr targetPtr = Marshal.StringToHGlobalUni(target);
            IntPtr userPtr = Marshal.StringToHGlobalUni(userName ?? "");
            try
            {
                var credential = new NativeCredential();
                credential.Type = CredentialTypeGeneric;
                credential.TargetName = targetPtr;
                credential.UserName = userPtr;
                credential.CredentialBlobSize = blob.Length;
                credential.CredentialBlob = blobPtr;
                credential.Persist = CredentialPersistLocalMachine;
                if (!CredWrite(ref credential, 0)) throw new Exception(failure + "(" + Marshal.GetLastWin32Error() + ")");
            }
            finally
            {
                Marshal.FreeHGlobal(blobPtr);
                Marshal.FreeHGlobal(targetPtr);
                Marshal.FreeHGlobal(userPtr);
            }
        }

        private static string CredReadSecret(string target, out string userName)
        {
            userName = null;
            IntPtr credentialPtr;
            if (!CredRead(target, CredentialTypeGeneric, 0, out credentialPtr)) return null;
            try
            {
                var credential = (NativeCredential)Marshal.PtrToStructure(credentialPtr, typeof(NativeCredential));
                if (credential.UserName != IntPtr.Zero) userName = Marshal.PtrToStringUni(credential.UserName);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0) return null;
                byte[] blob = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
                string secret = Encoding.Unicode.GetString(blob);
                return secret.Length == 0 ? null : secret;
            }
            finally { CredFree(credentialPtr); }
        }

        // Never throws: the settings window only wants to know whether a password is stored. Test
        // runs never touch the real credential store.
        private bool KnoxMailHasPasswordSafe()
        {
            if (Testing) return false;
            try { return KnoxMailReadPassword() != null; }
            catch { return false; }
        }

        // ---- 373: SLSI Avatar 메신저 회의 알림(보내기 전용) ----
        // 연결: 브라우저로 alpha 서버 SSO → 이 PC loopback 으로 원타임 code → exchange(SSO access 토큰) →
        // /api/me/desktop-token(만료 없는 aa_ 토큰, slsi-agent-desktop 과 같은 발급 경로·PC마다 따로) →
        // 자격 증명 관리자. 보내기: 아바타 봇 서버 /api/desktop/messenger/send — 받는 사람은 토큰 주인 본인뿐이다.
        private const string MessengerCredentialTarget = "DeskHooker.AvatarMessenger";
        private const int MessengerLoginTimeoutMs = 5 * 60 * 1000, MessengerHttpTimeoutMs = 15000;
        private sealed class MessengerUnauthorizedException : Exception { public MessengerUnauthorizedException() : base("desktop token rejected") { } }

        // The linked account label (knox id), or null when this PC is not linked. Never throws; test runs
        // never touch the real credential store.
        private string MessengerLinkedAccountSafe()
        {
            if (Testing) return null;
            try
            {
                string user;
                string token = CredReadSecret(MessengerCredentialTarget, out user);
                return token == null ? null : (String.IsNullOrEmpty(user) ? Environment.UserName : user);
            }
            catch { return null; }
        }

        // 378: 단추를 누른 순간부터 동작 기록에 남긴다(기록이 없으면 눌림 자체가 안 온 것). loopback 대기와
        // 브라우저 열기는 UI(STA) 스레드에서 한다 — ShellExecute 는 STA 에서 불러야 안전하다. 기다림·교환만 뒤에서.
        private void StartMessengerLogin()
        {
            if (Testing || Exiting) return;
            if (messengerLoggingIn) { AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = true, Detail = "메신저: 이미 로그인을 기다리는 중(브라우저 창을 확인하세요)" }); SendThemes(); return; }
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = true, Detail = "메신저: SSO 로그인 시작 — 브라우저를 엽니다(127.0.0.1:" + port + " 에서 결과 대기, 5분)" });
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AvatarMessenger.LoginUrl(port)) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                if (listener != null) try { listener.Stop(); } catch { }
                messengerError = "로그인 창을 열지 못했어요: " + KnoxSync.FirstLine(error.Message, 100);
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "메신저: 로그인 창 열기 실패: " + KnoxSync.FirstLine(error.Message, 100) });
                SendThemes();
                return;
            }
            messengerLoggingIn = true; messengerError = null; SendThemes();
            TcpListener waiting = listener;
            Task.Factory.StartNew(delegate { pendingMessenger = RunMessengerLogin(waiting); });
        }

        private MessengerResult RunMessengerLogin(TcpListener listener)
        {
            var result = new MessengerResult { Login = true };
            try
            {
                string code = WaitForLoginCode(listener, MessengerLoginTimeoutMs);
                if (code == null) { result.Detail = "로그인 시간이 지났어요(5분). 다시 눌러 주세요."; return LogMessenger(result, "연결: 로그인 시간 초과"); }
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = true, Detail = "메신저: SSO 로그인 결과 받음 — 서버와 교환 중" });
                string knoxId;
                string access = AvatarMessenger.AccessTokenFromExchange(
                    HttpPostJson(AvatarMessenger.AlphaBase + AvatarMessenger.ExchangePath, AvatarMessenger.JsonBody("code", code), null), out knoxId);
                // 377: 이 PC 사용자 본인의 SSO 계정일 때만 연결한다 — 다른 계정으로 로그인하면 토큰을 받지도 않는다
                // (받는 사람은 서버가 토큰 주인으로만 정하므로, 여기서 막으면 알림은 이 PC 사용자에게만 간다).
                if (!AvatarMessenger.AccountMatchesPcUser(knoxId, Environment.UserName))
                {
                    result.Detail = "이 PC 사용자(" + Environment.UserName + ") 본인의 계정으로 로그인해 주세요" + (knoxId.Length > 0 ? " — 로그인한 계정: " + knoxId : "") + ".";
                    return LogMessenger(result, "연결 거절: SSO 계정(" + (knoxId.Length > 0 ? knoxId : "알 수 없음") + ")이 이 PC 사용자(" + Environment.UserName + ")와 다름");
                }
                string token = AvatarMessenger.DesktopTokenFromResponse(
                    HttpPostJson(AvatarMessenger.AlphaBase + AvatarMessenger.DesktopTokenPath, "{}", access));
                string account = knoxId;
                CredSaveSecret(MessengerCredentialTarget, account, token, "자격 증명 관리자에 메신저 연결을 저장하지 못했습니다");
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = true, Detail = "메신저: " + account + " 계정과 연결됨(자격 증명 관리자)" });
                // 연결 확인을 겸한 첫 메시지 — 여기까지 되면 회의 알림도 같은 길로 간다. 실패해도 연결은 남긴다
                // (메신저 서버만 잠깐 안 되는 경우) — 단, 서버가 토큰을 거절(401)하면 쓸 수 없는 연결이라 지운다.
                try
                {
                    SendAvatarMessage(token, "✅ [쉼] 이 방으로 회의 시작 전 알림을 보내 드릴게요. (보내기 전용이라 여기에 답장하셔도 쉼은 받지 않아요)");
                    result.Ok = true; result.Sent = true;
                    return LogMessenger(result, "연결 확인 메시지 보냄");
                }
                catch (MessengerUnauthorizedException)
                {
                    try { CredDelete(MessengerCredentialTarget, CredentialTypeGeneric, 0); } catch { }
                    result.Detail = "메신저 서버가 연결 토큰을 받지 않았어요. 잠시 뒤 다시 로그인해 주세요.";
                    return LogMessenger(result, "연결 확인 메시지: 서버 인증 거절(401) — 연결을 지움");
                }
                catch (WebException error)
                {
                    result.Detail = "연결은 저장됐지만 확인 메시지를 보내지 못했어요: " + DescribeWebError(error);
                    return LogMessenger(result, "연결 확인 메시지 못 보냄: " + DescribeWebError(error));
                }
            }
            catch (WebException error)
            {
                result.Detail = "로그인 서버와 연결하지 못했어요: " + DescribeWebError(error);
                return LogMessenger(result, "연결 실패: " + DescribeWebError(error));
            }
            catch (Exception error)
            {
                result.Detail = "메신저 연결에 실패했어요: " + KnoxSync.FirstLine(error.Message, 100);
                return LogMessenger(result, "연결 실패: " + KnoxSync.FirstLine(error.Message, 100));
            }
            finally
            {
                if (listener != null) try { listener.Stop(); } catch { }
            }
        }

        // Accepts loopback connections until one carries our callback with a code (the browser may also
        // ask for /favicon.ico). Answers the browser with a short page either way. Null on timeout.
        private static string WaitForLoginCode(TcpListener listener, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) return null;
                IAsyncResult pending = listener.BeginAcceptTcpClient(null, null);
                if (!pending.AsyncWaitHandle.WaitOne(left)) return null;
                using (TcpClient client = listener.EndAcceptTcpClient(pending))
                {
                    client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
                    NetworkStream stream = client.GetStream();
                    string code = null;
                    try
                    {
                        var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                        code = AvatarMessenger.CodeFromRequestLine(reader.ReadLine());
                        string header;
                        while (!String.IsNullOrEmpty(header = reader.ReadLine())) { } // drain headers
                    }
                    catch (IOException) { continue; }
                    string html = code != null
                        ? "<!doctype html><meta charset=utf-8><title>쉼</title><body style=\"font-family:sans-serif;padding:40px\"><h2>쉼 · 메신저 연결 로그인 완료</h2><p>이 창은 닫아도 돼요. 쉼 설정 창에서 연결 상태를 확인하세요.</p></body>"
                        : "<!doctype html><meta charset=utf-8><title>쉼</title><body>not found</body>";
                    byte[] body = Encoding.UTF8.GetBytes(html);
                    byte[] head = Encoding.ASCII.GetBytes((code != null ? "HTTP/1.1 200 OK" : "HTTP/1.1 404 Not Found") +
                        "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
                    try { stream.Write(head, 0, head.Length); stream.Write(body, 0, body.Length); stream.Flush(); } catch (IOException) { }
                    if (code != null) return code;
                }
            }
        }

        // Fire-and-forget send on a background thread; the outcome lands in the action log and the
        // settings window. A 401 means the desktop token was revoked: the stored link is dropped.
        private void StartMessengerSend(string text, string what)
        {
            if (Testing || Exiting) return;
            string user;
            string token = null;
            try { token = CredReadSecret(MessengerCredentialTarget, out user); } catch { }
            if (token == null)
            {
                messengerError = "메신저가 연결돼 있지 않아요. [SSO 로그인으로 연결]을 눌러 주세요.";
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "메신저: " + what + " 못 보냄(연결 안 됨)" });
                SendThemes();
                return;
            }
            Task.Factory.StartNew(delegate
            {
                var result = new MessengerResult();
                try { SendAvatarMessage(token, text); result.Ok = true; result.Sent = true; pendingMessenger = LogMessenger(result, what + " 보냄"); }
                catch (MessengerUnauthorizedException)
                {
                    try { CredDelete(MessengerCredentialTarget, CredentialTypeGeneric, 0); } catch { }
                    result.Detail = "메신저 연결이 끊겼어요(토큰 폐기됨). 다시 로그인해 주세요.";
                    pendingMessenger = LogMessenger(result, what + " 못 보냄: 인증 거절(401) — 연결을 지움");
                }
                catch (WebException error) { result.Detail = "메신저 서버에 보내지 못했어요: " + DescribeWebError(error); pendingMessenger = LogMessenger(result, what + " 못 보냄: " + DescribeWebError(error)); }
                catch (Exception error) { result.Detail = "메신저로 보내지 못했어요: " + KnoxSync.FirstLine(error.Message, 100); pendingMessenger = LogMessenger(result, what + " 못 보냄: " + KnoxSync.FirstLine(error.Message, 100)); }
            });
        }

        private static void SendAvatarMessage(string token, string text)
        {
            try { HttpPostJson(AvatarMessenger.BotBase + AvatarMessenger.SendPath, AvatarMessenger.JsonBody("text", text), token); }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.Unauthorized) throw new MessengerUnauthorizedException();
                throw;
            }
        }

        private MessengerResult LogMessenger(MessengerResult result, string detail)
        {
            AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = result.Ok, Detail = "메신저: " + detail });
            return result;
        }

        // Status code plus the server's short reason; never the request body (which may carry a code or token).
        private static string DescribeWebError(WebException error)
        {
            var response = error.Response as HttpWebResponse;
            if (response == null) return KnoxSync.FirstLine(error.Message, 100);
            string detail = "";
            try
            {
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    object d;
                    if (map != null && map.TryGetValue("detail", out d) && d is string) detail = (string)d;
                }
            }
            catch { }
            return "HTTP " + (int)response.StatusCode + (detail.Length > 0 ? " " + KnoxSync.FirstLine(detail, 80) : "");
        }

        // JSON POST with a bounded timeout. Direct connection (no PC proxy), like slsi-agent-desktop's
        // trust_env=False: both servers are on the intranet. Throws WebException on a non-2xx status.
        private static string HttpPostJson(string url, string json, string bearerToken)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 on .NET Framework 4.x
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST"; request.Proxy = null;
            request.UserAgent = "DeskHooker-Teum"; request.Accept = "application/json"; request.ContentType = "application/json; charset=utf-8";
            if (!String.IsNullOrEmpty(bearerToken)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearerToken;
            request.Timeout = MessengerHttpTimeoutMs; request.ReadWriteTimeout = MessengerHttpTimeoutMs;
            byte[] body = Encoding.UTF8.GetBytes(json ?? "{}");
            request.ContentLength = body.Length;
            using (Stream upload = request.GetRequestStream()) upload.Write(body, 0, body.Length);
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        // 364: fetches today's mails over POP3 and turns invitation mails into meeting entries.
        // The timer only runs while enabled; a manual run works with the toggle off too — but never
        // having stored a password means the user never set the feature up, so it skips silently
        // instead of piling up failure log lines.
        private void StartKnoxMailSync(bool manual)
        {
            if (Testing || knoxMailSyncing || Exiting) return;
            bool hasPassword = KnoxMailHasPasswordSafe();
            if (!Settings.KnoxMailEnabled && !hasPassword) return;
            if (!hasPassword)
            {
                knoxMailError = "Knox 메일 비밀번호가 저장되지 않았어요. 설정의 비밀번호 칸에 입력하고 저장하세요.";
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "메일: 비밀번호 없음" });
                SendThemes();
                return;
            }
            knoxMailSyncing = true; knoxMailError = null; SendThemes();
            string password = KnoxMailReadPassword();
            Task.Factory.StartNew(delegate { pendingKnoxMail = RunKnoxMailFetch(password); });
        }

        // The fetch itself (background thread): newest message number first, TOP n 0 reads the Date
        // header, the scan stops at the first mail older than today, and each of today's mails is
        // RETR'd and parsed. Capped at KnoxMail.MaxScan. Returns meetings plus counts, or a fixed
        // error text — never the password, never raw text from the login path.
        private KnoxMailResult RunKnoxMailFetch(string password)
        {
            var meetings = new List<CalendarEntry>();
            DateTime today = DateTime.Now.Date;
            int mails = 0, invitations = 0;
            bool truncated = false;
            try
            {
                using (var pop3 = Pop3Client.Connect(KnoxMail.Host, KnoxMail.Port, KnoxMail.TimeoutMs))
                {
                    pop3.Login(Environment.UserName, password);
                    int count = pop3.MessageCount();
                    for (int n = count, scanned = 0; n >= 1; n--)
                    {
                        if (++scanned > KnoxMail.MaxScan) { truncated = true; break; }
                        KnoxMailScanStep step = KnoxMail.DecideScanStep(today, KnoxMail.DateFromHeaderBlock(pop3.Top(n)));
                        if (step == KnoxMailScanStep.Stop) break;
                        if (step != KnoxMailScanStep.Fetch) continue;
                        KnoxMailMessage mail = KnoxMail.ParseMailMessage(pop3.Retr(n), KnoxMail.BodyDefaultChars);
                        mails++;
                        CalendarEntry entry = KnoxMail.InvitationToCalendarEntry(mail);
                        if (entry != null) { meetings.Add(entry); invitations++; }
                    }
                }
            }
            catch (Pop3AuthException)
            {
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "메일: 인증 실패" });
                return new KnoxMailResult { Error = "POP3 로그인이 거부됐어요. Knox 포털 비밀번호를 확인하세요(만료·SSO 재인증일 수 있어요)." };
            }
            catch (Exception error)
            {
                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "메일: 서버 연결 실패: " + KnoxSync.FirstLine(error.Message, 100) });
                return new KnoxMailResult { Error = "POP3 서버(pop3.samsung.net)에 연결하지 못했습니다: " + KnoxSync.FirstLine(error.Message, 100) };
            }
            AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = true, Detail = "메일: 성공(오늘 " + mails + "통 · 초대 " + invitations + "건)" + (truncated ? " · 스캔 상한" : "") });
            return new KnoxMailResult { Meetings = meetings, MailCount = mails, InvitationCount = invitations };
        }

        // Shows a lightweight meeting-reminder popup, distinct from the full-screen stretch/breath overlay.
        // Called only from Tick(), which already holds the Popup==null/locked/session guards. When a
        // prewarm window for one of these meetings is alive, that already-buffered window is revealed
        // instead of creating a cold one, so the bell video starts right away. 350: every meeting that
        // is due right now joins the same screen — the prewarm window's text is refreshed in place
        // (app.js keeps the buffered iframe and only redraws the veil).
        private void ShowCalendarPopup(List<CalendarEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;
            foreach (CalendarEntry e in entries) firedKnoxUids.Add(CalendarSchedule.FiredKey(e));
            SaveKnoxState();
            knoxPopupEvents = SortedKnoxEvents(entries);
            if (knoxPrewarm != null && !knoxPrewarm.IsDisposed && knoxPopupEvents.Any(e => e.Uid == knoxPrewarmUid))
            {
                var ready = knoxPrewarm; knoxPrewarm = null; knoxPrewarmUid = null;
                ready.Prewarm = false;
                ready.Bounds = Screen.PrimaryScreen.Bounds;
                ready.ShowInTaskbar = true;
                Popup = ready;
                SendState(ready); // 겹친 일정 전체 목록으로 글자만 갱신(종소리 iframe은 app.js가 그대로 둔다)
                ready.Show(); ready.Activate();
                try
                {
                    // Un-mute and restart the bell so the reminder rings from the top exactly at the reveal moment.
                    if (ready.Browser.CoreWebView2 != null) ready.Browser.CoreWebView2.ExecuteScriptAsync("window.teumRevealBell&&window.teumRevealBell()");
                }
                catch { /* the page may still be loading; the muted video keeps playing either way */ }
                return;
            }
            Popup = new WebWindow(this, true, "knox:" + knoxPopupEvents[0].Uid);
            Popup.Show(); Popup.Activate();
        }

        // 350: 같은 화면에 나열할 일정들의 순서 — 시작 시각 오름차순, 같은 시각이면 제목 순.
        private static List<CalendarEntry> SortedKnoxEvents(IEnumerable<CalendarEntry> entries)
        {
            var list = new List<CalendarEntry>(entries);
            list.Sort(delegate(CalendarEntry a, CalendarEntry b)
            {
                int byStart = a.StartLocal.CompareTo(b.StartLocal);
                return byStart != 0 ? byStart : String.CompareOrdinal(a.Title ?? "", b.Title ?? "");
            });
            return list;
        }

        // ---- Bell-video prewarming: buffer the video before the popup moment ----
        // A hidden window is created KnoxPrewarmSeconds before the lead-time moment. It loads the same
        // calendar-alert page muted, so the YouTube stream is already flowing when the reveal happens.
        private WebWindow knoxPrewarm;
        private string knoxPrewarmUid;
        private const int KnoxPrewarmSeconds = 30;

        private void CloseKnoxPrewarm()
        {
            if (knoxPrewarm == null) return;
            var window = knoxPrewarm; knoxPrewarm = null; knoxPrewarmUid = null;
            window.Close();
        }

        private void StartKnoxPrewarm(CalendarEntry entry)
        {
            CloseKnoxPrewarm();
            knoxPopupEvents = SortedKnoxEvents(new CalendarEntry[] { entry }); // 예열 창의 첫 그리기용; 실제 알림 순간에 due 전체로 다시 그려진다
            knoxPrewarmUid = entry.Uid;
            knoxPrewarm = new WebWindow(this, true, "knox:" + entry.Uid);
            knoxPrewarm.Prewarm = true;
            // Off-screen and taskbar-less, so nothing is visible or focus-stealing while it buffers.
            knoxPrewarm.ShowInTaskbar = false;
            knoxPrewarm.Bounds = new Rectangle(-32000, -32000, 1280, 720);
            knoxPrewarm.Show();
        }

        // Drops a prewarm window that can no longer lead anywhere: the feature was switched off, the
        // meeting vanished from the calendar, it already started, or its reminder already fired.
        private void CleanupKnoxPrewarm(DateTime now)
        {
            if (knoxPrewarm == null) return;
            if (knoxPrewarm.IsDisposed || !Settings.KnoxCalendarEnabled || firedKnoxUids.Contains(knoxPrewarmUid)) { CloseKnoxPrewarm(); return; }
            var entry = CombinedCalendarEntries().FirstOrDefault(e => e.Uid == knoxPrewarmUid && !firedKnoxUids.Contains(CalendarSchedule.FiredKey(e)));
            if (entry == null || entry.StartLocal <= now) CloseKnoxPrewarm();
        }

        private void SendThemes()
        {
            if (SettingsWindow == null || SettingsWindow.IsDisposed) return;
            SettingsWindow.Send(new { type = "themes", themes = Themes, settings = Settings, sync = SyncInfo(), knoxSync = KnoxSyncInfo(), notice = notice, version = Program.AppVersion });
        }

        private void Persist()
        {
            if (Testing) return;
            string file=Path.Combine(dataFolder,"settings.json"), tmp=file+".new";
            File.WriteAllText(tmp,Json.Serialize(Settings));
            if (File.Exists(file)) File.Replace(tmp,file,null); else File.Move(tmp,file);
        }

        private void Startup(bool enable)
        {
            if (Testing) return;
            using (RegistryKey key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enable) key.SetValue("DeskHooker.Teum", "\""+Application.ExecutablePath+"\" --background", RegistryValueKind.String);
                else key.DeleteValue("DeskHooker.Teum", false);
            }
            // 작업 관리자의 "시작 프로그램 사용 안 함" 기록은 Run 값과 별개로
            // StartupApproved\Run에 남는다. 이 기록이 끔 상태면 Run 값을 다시 써도
            // Windows가 로그인 때 실행을 건너뛴다. 켤 때 이 기록을 함께 지워야
            // 앱의 체크박스만으로 실제 자동 실행이 되살아난다.
            if (enable)
            {
                using (RegistryKey approved=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                {
                    if (approved!=null) approved.DeleteValue("DeskHooker.Teum", false);
                }
            }
        }

        public void ShowSettings()
        {
            if (SettingsWindow.IsDisposed) SettingsWindow=new WebWindow(this,false,null);
            SettingsWindow.Show(); SettingsWindow.WindowState=FormWindowState.Normal; SettingsWindow.Activate(); SendState(SettingsWindow);
        }

        public void ShowPopup(string id)
        {
            if (Popup!=null) { Popup.Activate(); return; }
            // "knox-bell" is the bell-video manager page, not one of the rotating themes.
            if (!Themes.Any(t => t.id==id) && id!="knox-bell") throw new ArgumentException("알 수 없는 주제입니다.");
            Popup=new WebWindow(this,true,id);
            Popup.Show(); Popup.Activate();
        }

        // Plans one reminder from the saved selection and mode, then shows its first theme.
        // In the "all" modes the remaining themes follow as each popup is closed.
        // Cards with an absolute "몇 시에 실행" time never join this rotation — they run on their own clock.
        private void StartRound()
        {
            if (Popup!=null) { Popup.Activate(); return; }
            pendingRound.Clear();
            var scheduled=Settings.Schedules ?? new Dictionary<string,string>();
            string[] periodic=Settings.ThemeIds.Where(id => !scheduled.ContainsKey(id)).ToArray();
            if (periodic.Length==0) return; // nothing rotates; only scheduled cards run
            foreach (var id in ThemeRotation.PlanRound(periodic,Settings.ThemeMode,ref cursor,rng,lastShown)) pendingRound.Enqueue(id);
            ShowNextInRound();
        }

        private void ShowNextInRound()
        {
            while (pendingRound.Count>0)
            {
                string id=pendingRound.Dequeue();
                if (!Themes.Any(t => t.id==id)) continue;
                lastShown=id; ShowPopup(id); return;
            }
        }

        public void PopupClosed(WebWindow window)
        {
            if (Popup==window) Popup=null;
            if (!Exiting)
            {
                NextDue=ReminderSchedule.Next(DateTime.Now,Settings);
                ShowNextInRound();
                UpdateStatus();
            }
        }

        // While a popup's list editor is open, the form switches from borderless-fullscreen to a
        // normal windowed one so text can be copied to/from other apps; closing the editor restores
        // the immersive fullscreen look.
        public void PopupEditMode(WebWindow window, bool on)
        {
            if (window==null || window!=Popup) return;
            if (on)
            {
                window.TopMost=false;
                window.FormBorderStyle=FormBorderStyle.Sizable;
                var screen=Screen.FromControl(window).Bounds;
                window.Bounds=new Rectangle(screen.X+80,screen.Y+80,Math.Min(1180,screen.Width-160),Math.Min(860,screen.Height-160));
            }
            else
            {
                window.FormBorderStyle=FormBorderStyle.None;
                window.Bounds=Screen.FromControl(window).Bounds;
                window.TopMost=true;
            }
        }

        private void SessionChanged(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason==SessionSwitchReason.SessionLock) locked=true;
            if (e.Reason==SessionSwitchReason.SessionUnlock)
            {
                locked=false;
                NextDue=ReminderSchedule.Next(DateTime.Now,Settings);
            }
        }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode==PowerModes.Resume) NextDue=ReminderSchedule.Next(DateTime.Now,Settings);
        }
        private void Tick()
        {
            DateTime now=DateTime.Now;
            if ((now-lastTick).TotalSeconds>15 || now<lastTick) NextDue=ReminderSchedule.Next(now,Settings);
            lastTick=now;
            var finished=pendingSync; if (finished!=null) { pendingSync=null; ApplySync(finished); }
            var finishedKnox=pendingKnoxResult;
            if (finishedKnox!=null)
            {
                pendingKnoxResult=null; knoxSyncing=false;
                if (finishedKnox.Length==0) { lastKnoxSync=DateTime.Now; knoxSyncError=null; } else knoxSyncError=finishedKnox;
                SendThemes();
            }
            // 364: POP3 초대 메일 조회 결과 반영 — 성공이면 오늘 받은 초대 회의 목록을 갈아끼운다.
            var finishedMail=pendingKnoxMail;
            if (finishedMail!=null)
            {
                pendingKnoxMail=null; knoxMailSyncing=false;
                if (finishedMail.Error==null)
                {
                    knoxMailMeetings=finishedMail.Meetings ?? new List<CalendarEntry>();
                    lastKnoxMailSync=DateTime.Now; knoxMailError=null;
                }
                else knoxMailError=finishedMail.Error;
                SendThemes();
            }
            // 373: 메신저 연결·발송 결과 반영.
            var finishedMessenger=pendingMessenger;
            if (finishedMessenger!=null)
            {
                pendingMessenger=null;
                if (finishedMessenger.Login) messengerLoggingIn=false;
                messengerError=finishedMessenger.Ok?null:finishedMessenger.Detail;
                if (finishedMessenger.Sent) lastMessengerSend=DateTime.Now;
                SendThemes();
            }
            // 373: 회의 시작 전 메신저 알림 — 팝업과 같은 시각(시작 N분 전)이지만 자리비움·화면 잠금과
            // 무관하게 보낸다(자리에 없을 때 받으라고 있는 알림이다). 회의마다 하루 한 번만.
            if (Settings.KnoxCalendarEnabled && Settings.MessengerNotifyEnabled && !Testing)
            {
                ReloadKnoxIcsIfChanged();
                var dueMessenger=CalendarSchedule.DueEntries(now,CombinedCalendarEntries(),Settings.KnoxLeadMinutes,messengerSentKeys);
                if (dueMessenger.Count>0)
                {
                    foreach (CalendarEntry e in dueMessenger) messengerSentKeys.Add(CalendarSchedule.FiredKey(e));
                    SaveDayKeys(MessengerStatePath,"sent",messengerSentKeys);
                    StartMessengerSend(AvatarMessenger.MeetingText(dueMessenger,now),"회의 알림 "+dueMessenger.Count+"건");
                }
            }
            // "Away from the desk" (PauseWhenIdle): no input for IdleMinutes means the user is out,
            // so both stretch breaks and meeting alerts are held back until they return.
            bool away=ReminderSchedule.IsAway(Program.IdleFor(),Settings);
            bool sessionActive=!locked && Environment.UserInteractive && !away;
            // "입력 없어서 잠김": once the idle threshold trips, a running stretch/breath popup is
            // closed automatically (the rest of the round is dropped, like an explicit close).
            // Meeting alerts are exempt — they follow only their own auto-close setting, because
            // the meeting still starts whether or not the user is typing.
            if (away && Popup!=null && (Popup.ThemeId==null || !Popup.ThemeId.StartsWith("knox:",StringComparison.Ordinal)))
            { var idleOld=Popup; Popup=null; pendingRound.Clear(); idleOld.Close(); }
            // Meeting alerts are the absolute priority: not subject to work-hours/weekday/lunch-break
            // rules. A newly-due meeting always wins and replaces whatever stretch/breath popup is on
            // screen. A stretch whose moment arrives while an alert is alive is skipped, not deferred:
            // PopupClosed re-arms NextDue on the absolute timetable, so the next stretch comes at the
            // next fixed slot. 350: when a meeting alert is already ringing, a meeting that becomes due
            // joins that same screen instead of replacing it — overlapping meetings are listed together,
            // with the bell video and the already-buffered stream left untouched.
            if (Settings.KnoxCalendarEnabled && sessionActive)
            {
                ReloadKnoxIcsIfChanged();
                var due=CalendarSchedule.DueEntries(now,CombinedCalendarEntries(),Settings.KnoxLeadMinutes,firedKnoxUids);
                if (due.Count>0)
                {
                    if (Popup!=null && Popup.ThemeId!=null && Popup.ThemeId.StartsWith("knox:",StringComparison.Ordinal))
                    {
                        // 350: 떠 있는 일정 알림에 합치기 — 이미 시작해 버린 옛 일정은 안내에서 빼고,
                        // 새로 due가 된 일정을 목록에 더해 글자만 다시 그린다(종소리 영상은 그대로).
                        knoxPopupEvents.RemoveAll(e => e.StartLocal<=now);
                        knoxPopupEvents.AddRange(due);
                        knoxPopupEvents=SortedKnoxEvents(knoxPopupEvents);
                        foreach (CalendarEntry e in due) firedKnoxUids.Add(CalendarSchedule.FiredKey(e));
                        SaveKnoxState();
                        SendState(Popup);
                    }
                    else
                    {
                        if (Popup!=null) { var old=Popup; Popup=null; pendingRound.Clear(); old.Close(); } // 내리고: replace the running popup with the alert
                        ShowCalendarPopup(due);
                    }
                }
                else if (Popup==null && knoxPrewarm==null && !ReminderSchedule.IsDue(now,NextDue,Settings,Popup!=null,false,sessionActive))
                {
                    // Not due yet: prewarm the soonest meeting whose lead-time moment is less than
                    // KnoxPrewarmSeconds away, so the bell video is buffered before the popup appears.
                    CalendarEntry next=null;
                    foreach (CalendarEntry e in CombinedCalendarEntries())
                    {
                        if (String.IsNullOrEmpty(e.Uid) || firedKnoxUids.Contains(CalendarSchedule.FiredKey(e)) || e.StartLocal<=now) continue;
                        DateTime popupAt=e.StartLocal.AddMinutes(-Settings.KnoxLeadMinutes);
                        if (now>=popupAt.AddSeconds(-KnoxPrewarmSeconds) && now<popupAt && (next==null || e.StartLocal<next.StartLocal)) next=e;
                    }
                    if (next!=null) StartKnoxPrewarm(next);
                }
            }
            CheckSchedules(now,sessionActive);
            if (ReminderSchedule.IsDue(now,NextDue,Settings,Popup!=null,false,sessionActive))
            {
                StartRound();
            }
            CleanupKnoxPrewarm(now);
            CloseKnoxPopupIfDue(now);
            UpdateStatus();
        }

        // "몇 시에 실행" 카드: 매일 그 시각(시:분:초)에 한 번만 띄운다. 재생 안 하는 구간(주말·근무 창
        // 시작 전·마감 후·쉬는 시간)에 걸린 시각은 띄우지 않는다(사이드바 목록도 같은 시각을 뺀다).
        // 자리비움·잠금 중이거나 다른 팝업이 열려 있으면 그 상태가 풀릴 때까지 기다렸다가 띄우되,
        // 시각에서 15분이 지나면 놓친 것으로 보고 건너뛴다.
        private void CheckSchedules(DateTime now, bool sessionActive)
        {
            if (!Settings.Enabled || Settings.Schedules==null || Settings.Schedules.Count==0) return;
            if (!sessionActive || Popup!=null) return;
            if (Settings.WeekdaysOnly && (now.DayOfWeek==DayOfWeek.Saturday || now.DayOfWeek==DayOfWeek.Sunday)) return;
            string dueId=null; int dueAt=int.MaxValue;
            foreach (KeyValuePair<string,string> kv in Settings.Schedules)
            {
                int at;
                if (!ReminderSettings.TryParseClock(kv.Value,out at)) continue;
                // 295번: 시각 실행 카드도 "재생 안 하는 구간"(주말·근무 창 밖·쉬는 시간)에서는 띄우지 않는다.
                // 사이드바 "다음 작은 쉼" 목록과 같은 규칙이라, 목록에 보이는 시각만 실제로 뜬다.
                if (!ReminderSchedule.IsAllowed(now.Date.AddSeconds(at),Settings)) continue;
                DateTime fired;
                if (firedSchedules.TryGetValue(kv.Key,out fired) && fired.Date==now.Date) continue; // 오늘은 이미 띄움
                int nowSec=now.Hour*3600+now.Minute*60+now.Second;
                if (nowSec<at || nowSec>=at+15*60) continue; // 아직 시각이 아니거나, 너무 늦어 놓침
                if (at<dueAt) { dueAt=at; dueId=kv.Key; } // 여러 카드가 같은 시각에 걸려 있으면 이른 순서대로 하나씩
            }
            if (dueId==null || !Themes.Any(t => t.id==dueId)) return;
            firedSchedules[dueId]=now;
            pendingRound.Clear();
            ShowPopup(dueId);
        }

        // Auto-close of the meeting-alert popup, per the "일정 알림 자동 닫힘" setting. "none" never
        // closes on its own; "start"/"after" close at/after the LAST listed meeting's start (350: with
        // overlapping meetings on one screen the alert must outlive the earliest start); "end" closes
        // at the latest end time and never fires when any listed meeting carried no end.
        private void CloseKnoxPopupIfDue(DateTime now)
        {
            if (Popup==null || Popup.ThemeId==null || !Popup.ThemeId.StartsWith("knox:",StringComparison.Ordinal)) return;
            if (knoxPopupEvents.Count==0) return;
            DateTime lastStart=DateTime.MinValue;
            foreach (CalendarEntry e in knoxPopupEvents) if (e.StartLocal>lastStart) lastStart=e.StartLocal;
            DateTime? closeAt=null;
            if (Settings.KnoxAutoClose==ReminderSettings.KnoxAutoCloseStart) closeAt=lastStart;
            else if (Settings.KnoxAutoClose==ReminderSettings.KnoxAutoCloseAfter) closeAt=lastStart.AddMinutes(Settings.KnoxAutoCloseMinutes);
            else if (Settings.KnoxAutoClose==ReminderSettings.KnoxAutoCloseEnd)
            {
                DateTime lastEnd=DateTime.MinValue; bool allHaveEnd=true;
                foreach (CalendarEntry e in knoxPopupEvents)
                {
                    if (!e.EndLocal.HasValue) { allHaveEnd=false; break; }
                    if (e.EndLocal.Value>lastEnd) lastEnd=e.EndLocal.Value;
                }
                if (allHaveEnd) closeAt=lastEnd; // 하나라도 종료 시각이 없으면 자동으로 닫지 않는다
            }
            if (closeAt.HasValue && now>=closeAt.Value) Popup.Close(); // PopupClosed re-arms the stretch interval
        }
        private void UpdateStatus()
        {
            if (SettingsWindow!=null && !SettingsWindow.IsDisposed && SettingsWindow.IsHandleCreated)
                SettingsWindow.Send(new { type="tick", enabled=Settings.Enabled, next=NextDue.ToString("o"), now=DateTime.Now.ToString("o"), away=ReminderSchedule.IsAway(Program.IdleFor(),Settings) });
        }
        public void SendState(WebWindow target)
        {
            if (target==null || target.IsDisposed) return;
            bool isKnoxPopup = target.IsPopup && target.ThemeId != null && target.ThemeId.StartsWith("knox:", StringComparison.Ordinal);
            target.Send(new { type="state", mode=target.IsPopup?"popup":"settings", themeId=target.ThemeId, version=Program.AppVersion,
                settings=Settings, themes=Themes, removedThemeIds=removedThemeIds.ToArray(), sync=SyncInfo(), knoxSync=KnoxSyncInfo(), next=NextDue.ToString("o"), now=DateTime.Now.ToString("o"), notice=notice, testing=Testing, remaining=pendingRound.Count,
                away=ReminderSchedule.IsAway(Program.IdleFor(),Settings),
                knoxEvents = isKnoxPopup ? knoxPopupEvents.Select(e => new { title = e.Title, start = e.StartLocal.ToString("o"), end = e.EndLocal.HasValue ? e.EndLocal.Value.ToString("o") : null }).ToArray() : null, knoxPrewarm = isKnoxPopup && target.Prewarm });
        }

        public void Handle(WebWindow source, string message)
        {
            try
            {
                var msg=Json.Deserialize<Dictionary<string,object>>(message);
                string action=Convert.ToString(msg["action"]);
                if (action=="ready") { SendState(source); if (Testing && !source.IsPopup && !smokeStarted) { smokeStarted=true; RunSmoke(); } return; }
                if (action=="close") { if (source.IsPopup) { pendingRound.Clear(); source.Close(); } else source.Hide(); return; }
                if (action=="popup-edit-mode") { PopupEditMode(source, Convert.ToString(msg["on"])=="true"); return; }
                if (action=="next") { if (source.IsPopup) source.Close(); return; }
                if (action=="save")
                {
                    if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 변경할 수 있습니다.");
                    var settings=Json.Deserialize<ReminderSettings>(Json.Serialize(msg["settings"]));
                    // The settings window no longer edits the breathing timings; keep the stored ones (the file format is unchanged).
                    settings.VariantModes=Settings.VariantModes; settings.LoopModes=Settings.LoopModes; settings.InhaleSeconds=Settings.InhaleSeconds; settings.HoldSeconds=Settings.HoldSeconds; settings.ExhaleSeconds=Settings.ExhaleSeconds;
                    ReminderSchedule.Validate(settings,Themes.Select(t => t.id));
                    bool previousStartup=Settings.StartAtLogin, previousKnoxEnabled=Settings.KnoxCalendarEnabled, previousKnoxMail=Settings.KnoxMailEnabled;
                    Settings=settings;
                    firedSchedules.Clear(); // 시각을 새로 정했으니 오늘 발사 기록은 다시 센다
                    // 364: POP3 비밀번호는 설정 객체 옆에 따로 오고 자격 증명 관리자에만 저장된다 —
                    // settings.json·동작 기록·오류 메시지에는 절대 실리지 않는다. 시험 실행은 진짜
                    // 자격 증명 저장소를 건드리지 않는다. 동기화 트리거보다 먼저 처리한다: 토글을
                    // 켠 첫 저장에서 "비밀번호 없음" 실패 기록이 남지 않게 하기 위해서다.
                    bool mailPasswordSaved = false;
                    object mailPassword;
                    if (!Testing && msg.TryGetValue("knoxMailPassword", out mailPassword))
                    {
                        string typed = mailPassword as string;
                        if (!String.IsNullOrEmpty(typed))
                        {
                            try
                            {
                                KnoxMailSavePassword(typed);
                                mailPasswordSaved = true;
                                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = true, Detail = "메일: 비밀번호 저장됨(자격 증명 관리자)" });
                            }
                            catch (Exception error)
                            {
                                AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = false, Detail = "메일: 비밀번호 저장 실패: " + KnoxSync.FirstLine(error.Message, 100) });
                                throw; // 저장 실패는 저장 오류 안내(toast)로도 보여야 한다
                            }
                        }
                    }
                    if (Settings.StartAtLogin!=previousStartup) Startup(Settings.StartAtLogin);
                    if (Settings.KnoxCalendarEnabled && !previousKnoxEnabled) StartKnoxSync(false);
                    if (Settings.KnoxMailEnabled && (!previousKnoxMail || mailPasswordSaved)) StartKnoxMailSync(false);
                    Persist(); cursor=0; pendingRound.Clear(); NextDue=ReminderSchedule.Next(DateTime.Now,Settings);
                    notice=Settings.Enabled?"알림이 시작됐습니다. 창을 닫아도 트레이에서 계속 동작합니다.":"설정이 저장됐습니다. 알림은 일시정지 상태입니다.";
                    SendState(source); return;
                }
                 if (action=="save-variant-mode")
                 {
                     // A card's sub-item order ("보여 주는 순서") is chosen in that card's popup and remembered in settings.json.
                     // "knox-bell" manages the bell-video list outside Themes — allowed, like the loop selector does.
                     if (!source.IsPopup) throw new InvalidOperationException("보여 주는 순서는 카드 팝업에서 바꿉니다.");
                     string cardId=Convert.ToString(msg["themeId"]), mode=Convert.ToString(msg["mode"]);
                     if ((!Themes.Any(t => t.id==cardId) && cardId!="knox-bell") || !ReminderSettings.IsValidVariantMode(mode)) throw new ArgumentException("알 수 없는 카드이거나 순서 값이 올바르지 않습니다.");
                    var modes=new Dictionary<string,string>(Settings.VariantModes ?? new Dictionary<string,string>());
                    modes[cardId]=mode;
                    Settings.VariantModes=modes; Persist(); return;
                }
                if (action=="save-loop-mode")
                {
                    // A card's "반복" toggle (play the video again when it ends) is chosen in that
                    // card's popup and remembered in settings.json, next to the order choice.
                    // "knox-bell" manages the bell-video list outside Themes — allowed, like ShowPopup does.
                    if (!source.IsPopup) throw new InvalidOperationException("반복 설정은 카드 팝업에서 바꿉니다.");
                    string cardId=Convert.ToString(msg["themeId"]), loop=Convert.ToString(msg["on"]);
                    if ((!Themes.Any(t => t.id==cardId) && cardId!="knox-bell") || !ReminderSettings.IsValidLoopMode(loop)) throw new ArgumentException("알 수 없는 카드이거나 반복 값이 올바르지 않습니다.");
                    var loops=new Dictionary<string,string>(Settings.LoopModes ?? new Dictionary<string,string>());
                    loops[cardId]=loop;
                    Settings.LoopModes=loops; Persist(); return;
                }
                if (action=="sync") { if (!source.IsPopup) StartSync(true); return; }
                // ---- Card-list editing in the settings window ----
                if (action=="remove-theme")
                {
                    if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 변경할 수 있습니다.");
                    string id=Convert.ToString(msg["themeId"]);
                    if (id=="dance") throw new InvalidOperationException("댄스 안무 카드는 목록에서 뺄 수 없어요.");
                    if (id=="knox-bell") throw new InvalidOperationException("일정 알림 카드는 목록에서 뺄 수 없어요.");
                    if (id.StartsWith("x-",StringComparison.Ordinal))
                    {
                        if (!userThemes.Any(t => t.id==id)) throw new ArgumentException("알 수 없는 카드입니다.");
                        userThemes.RemoveAll(t => t.id==id);
                    }
                    else
                    {
                        if (!baseThemes.Any(t => t.id==id) && !communityThemes.Any(t => t.id==id)) throw new ArgumentException("알 수 없는 카드입니다.");
                        removedThemeIds.Add(id);
                    }
                    SaveUserThemes(); RebuildThemes(); PruneThemeIds();
                    cursor=0; pendingRound.Clear(); Persist();
                    notice="카드를 목록에서 뺐어요. '기본 프리셋으로 되돌리기'로 언제든 다시 가져올 수 있어요.";
                    SendState(source); return;
                }
                if (action=="add-theme")
                {
                    if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 변경할 수 있습니다.");
                    string name=Convert.ToString(msg["name"]).Trim(), summary=Convert.ToString(msg["summary"]).Trim(), safety=Convert.ToString(msg["safety"]).Trim();
                    if (name.Length<1 || name.Length>30) throw new ArgumentException("이름은 1~30자로 적어 주세요.");
                    if (summary.Length<1 || summary.Length>60) throw new ArgumentException("한 줄 설명은 1~60자로 적어 주세요.");
                    if (safety.Length>150) throw new ArgumentException("주의사항은 150자 이하로 적어 주세요."); // 338: 비워 둬도 저장된다
                    var steps=new List<string>();
                    foreach (string line in Convert.ToString(msg["steps"]).Split('\n'))
                    {
                        string s=line.Trim();
                        if (s.Length==0) continue;
                        if (s.Length>60) s=s.Substring(0,60);
                        steps.Add(s);
                        if (steps.Count==6) break;
                    }
                    var videos=new List<CommunityVideo>();
                    foreach (string line in Convert.ToString(msg["videos"]).Split('\n'))
                    {
                        string[] parts=line.Trim().Split('|');
                        if (parts.Length==0 || String.IsNullOrEmpty(parts[0].Trim())) continue;
                        string vid=parts[0].Trim();
                        if (!System.Text.RegularExpressions.Regex.IsMatch(vid,"^[A-Za-z0-9_-]{11}$")) throw new ArgumentException("영상 ID가 올바르지 않습니다: "+vid);
                        string title=parts.Length>1 ? parts[1].Trim() : "";
                        if (String.IsNullOrEmpty(title) || title.Length>60) title="영상 "+(videos.Count+1);
                        videos.Add(new CommunityVideo { id=vid, title=title });
                        if (videos.Count==5) break;
                    }
                    if (steps.Count==0 && videos.Count==0) throw new ArgumentException("동작 안내를 한 줄 이상 적거나 영상을 하나 넣어 주세요.");
                    string newId;
                    do { newId="x-"+rng.Next(0x10000000).ToString("x8"); } while (userThemes.Any(t => t.id==newId));
                    userThemes.Add(new Theme { id=newId, name=name, summary=summary, safety=safety, icon="◈", instructions=steps.ToArray(), videos=videos.ToArray(), user=true });
                    SaveUserThemes(); RebuildThemes();
                    notice="새 쉼 카드를 추가했어요. 체크하면 알림에 포함돼요.";
                    SendState(source); return;
                }
                 if (action=="reset-themes")
                 {
                      if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 변경할 수 있습니다.");
                      removedThemeIds.Clear(); userThemes.Clear();
                      // 프리셋 되돌리기도 프리셋의 내가 만든 카드를 다시 깐다(337). 시험 모드는 깨끗한 상태 유지.
                      if (!Testing) EnsurePresetCard();
                      SaveUserThemes(); RebuildThemes(); PruneThemeIds();
                      cursor=0; pendingRound.Clear(); Persist();
                      notice="카드 목록을 기본 프리셋으로 되돌렸어요.";
                      SendState(source); return;
                  }
                 // ---- 쉼 카드 내보내기/가져오기: 카드 목록과 각 카드의 하위 영상 목록을 JSON 파일 하나로 ----
                 if (action=="export-cards")
                 {
                     if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 사용할 수 있습니다.");
                     string json=Convert.ToString(msg["json"]);
                     var check=Json.Deserialize<Dictionary<string,object>>(json); // 반드시 JSON이어야 한다
                      if (Convert.ToString(check["format"])!="shim-cards" && Convert.ToString(check["format"])!="teum-cards") throw new ArgumentException("내보낼 내용이 올바르지 않습니다.");
                      using (var dialog=new SaveFileDialog { Title="쉼 카드와 영상 목록 내보내기", Filter="JSON 파일 (*.json)|*.json", FileName="shim-cards-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".json" })
                     {
                         if (dialog.ShowDialog()!=DialogResult.OK) return; // 취소는 조용히 끝낸다
                         File.WriteAllText(dialog.FileName, json);
                         notice="쉼 카드와 영상 목록을 내보냈어요: "+dialog.FileName;
                         SendState(source);
                     }
                     return;
                 }
                 if (action=="import-cards-request")
                 {
                     if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 사용할 수 있습니다.");
                     using (var dialog=new OpenFileDialog { Title="쉼 카드와 영상 목록 가져오기", Filter="JSON 파일 (*.json)|*.json" })
                     {
                         if (dialog.ShowDialog()!=DialogResult.OK) return; // 취소는 조용히 끝낸다
                         string json=File.ReadAllText(dialog.FileName);
                         var check=Json.Deserialize<Dictionary<string,object>>(json);
                          if (Convert.ToString(check["format"])!="shim-cards" && Convert.ToString(check["format"])!="teum-cards") throw new ArgumentException("쉼의 '영상 목록 내보내기'로 저장한 파일이 아닌 것 같아요: "+Path.GetFileName(dialog.FileName));
                         source.Send(new { type="cards-import", json=json }); // 페이지가 목록 편집을 두고 apply-import를 되돌려 보낸다
                     }
                     return;
                 }
                 if (action=="apply-import")
                 {
                     if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 사용할 수 있습니다.");
                     // 되살리는 건 내가 만든 카드("x-")뿐: 기본·공유 카드는 themes.json·동기화가 이미 채운다.
                     var cards=Json.Deserialize<Theme[]>(Json.Serialize(msg["cards"] ?? new object[] { }));
                     int restored=0;
                     foreach (Theme card in cards)
                     {
                         if (card==null || String.IsNullOrEmpty(card.id) || !System.Text.RegularExpressions.Regex.IsMatch(card.id,@"^x-[0-9a-f]{8}$")) throw new ArgumentException("가져온 카드의 아이디가 올바르지 않습니다.");
                          if (String.IsNullOrEmpty(card.name) || card.name.Trim().Length<1 || card.name.Trim().Length>30 || String.IsNullOrEmpty(card.summary) || card.summary.Trim().Length<1 || card.summary.Trim().Length>60 || (card.safety??"").Trim().Length>150) throw new ArgumentException("가져온 카드의 내용이 올바르지 않습니다: "+card.id); // 338: 주의사항은 비어 있어도 된다
                         var steps=(card.instructions ?? new string[] { }).Where(s => !String.IsNullOrWhiteSpace(s)).Select(s => s.Trim().Length>60 ? s.Trim().Substring(0,60) : s.Trim()).Take(6).ToArray();
                         var videos=new List<CommunityVideo>();
                         foreach (CommunityVideo v in card.videos ?? new CommunityVideo[] { })
                         {
                             if (videos.Count>=5) break;
                             if (v==null || String.IsNullOrEmpty(v.id) || !System.Text.RegularExpressions.Regex.IsMatch(v.id,@"^[A-Za-z0-9_-]{11}$")) continue; // 못 쓴 영상 하나는 건너뛴다(전체를 막지 않는다)
                             string title=String.IsNullOrEmpty(v.title) ? "영상 "+(videos.Count+1) : (v.title.Length>60 ? v.title.Substring(0,60) : v.title);
                             videos.Add(new CommunityVideo { id=v.id, title=title });
                         }
                         if (steps.Length==0 && videos.Count==0) continue;
                         Theme existing=userThemes.FirstOrDefault(t => t.id==card.id);
                         string cardSafety=(card.safety??"").Trim();
                         if (existing==null) userThemes.Add(new Theme { id=card.id, name=card.name.Trim(), summary=card.summary.Trim(), safety=cardSafety, icon="◈", instructions=steps, videos=videos.ToArray(), user=true });
                         else { existing.name=card.name.Trim(); existing.summary=card.summary.Trim(); existing.safety=cardSafety; existing.instructions=steps; existing.videos=videos.ToArray(); }
                         restored++;
                     }
                     // 목록에서 뺀 카드: 내보낸 PC와 같은 목록 모양이 되도록 그대로 따른다(모르는 아이디는 뺀다).
                     var removals=Json.Deserialize<string[]>(Json.Serialize(msg["removals"] ?? new object[] { }));
                     removedThemeIds.Clear();
                     foreach (string id in removals) if (baseThemes.Any(t => t.id==id) || communityThemes.Any(t => t.id==id)) removedThemeIds.Add(id);
                     // 선택(체크)한 카드: 이 PC에 있는 것만 따르고, 하나도 없으면 지금 선택을 유지한다.
                     var picked=Json.Deserialize<string[]>(Json.Serialize(msg["themeIds"] ?? new object[] { }));
                     SaveUserThemes(); RebuildThemes(); PruneThemeIds();
                     var known=new HashSet<string>(Themes.Select(t => t.id));
                     string[] kept=picked.Where(id => known.Contains(id)).Distinct().ToArray();
                     if (kept.Length>0) Settings.ThemeIds=kept;
                     cursor=0; pendingRound.Clear(); Persist(); NextDue=ReminderSchedule.Next(DateTime.Now,Settings);
                     notice=String.Format("쉼 카드와 영상 목록을 가져왔어요. 카드 {0}개 · 목록 편집 저장됨.",restored);
                     SendState(source); return;
                 }
                 if (action=="sync-knox") { if (!source.IsPopup) StartKnoxSync(true); return; }
                 // 373: SLSI Avatar 메신저 — 연결(SSO 로그인)·시험 발송·연결 끊기(설정 창에서만).
                 if (action=="messenger-login" || action=="messenger-test" || action=="messenger-logout")
                 {
                     if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 변경할 수 있습니다.");
                     if (action=="messenger-login") StartMessengerLogin();
                     else if (action=="messenger-test") StartMessengerSend("🔔 [쉼] 메신저 알림 시험이에요. 회의 시작 전에 이렇게 알려 드려요.","시험 메시지");
                     else if (!Testing)
                     {
                         CredDelete(MessengerCredentialTarget, CredentialTypeGeneric, 0);
                         messengerError=null;
                         AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = true, Detail = "메신저: 연결 지움" });
                     }
                     SendState(source); return;
                 }
                 // 364: 저장된 POP3 비밀번호를 자격 증명 관리자에서 지운다(설정 창에서만).
                 if (action=="knox-mail-pass-clear")
                 {
                     if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 변경할 수 있습니다.");
                     if (!Testing)
                     {
                         KnoxMailDeletePassword();
                         AddKnoxSyncLog(new KnoxSyncLogEntry { When = DateTime.Now.ToString("o"), Ok = true, Detail = "메일: 비밀번호 삭제됨" });
                     }
                     SendState(source); return;
                 }
                if (action=="test-knox")
                {
                    // Rehearses one upcoming meeting's lead-time popup. Deliberately does NOT mark the UID
                    // fired: a rehearsal must never consume the real reminder.
                    // Rehearses one upcoming meeting's lead-time popup on the same dedicated calendar-alert
                    // page the real reminder uses (the "knox:" prefix is what routes the page there; a
                    // distinct prefix like "knox-test:" would fall through to the first stretch theme).
                    // Deliberately does NOT mark the UID fired: a rehearsal must never consume the real reminder.
                    if (source.IsPopup) return;
                    ReloadKnoxIcsIfChanged();
                    string uid=Convert.ToString(msg["uid"]);
                    var entry=CombinedCalendarEntries().FirstOrDefault(e => e.Uid==uid);
                    if (entry==null) throw new InvalidOperationException("선택한 일정을 찾을 수 없습니다. 먼저 '지금 일정 가져오기'로 일정을 받아 오세요.");
                    if (Popup!=null) { var old=Popup; Popup=null; pendingRound.Clear(); old.Close(); }
                    CloseKnoxPrewarm(); // the rehearsal window replaces any hidden prewarm for the real reminder
                    knoxPopupEvents=SortedKnoxEvents(new CalendarEntry[]{ entry }); // 시험은 한 일정만 띄운다(발사 기록은 남기지 않는다)
                    Popup=new WebWindow(this,true,"knox:"+uid);
                    Popup.Show(); Popup.Activate();
                    // 378: 메신저 알림을 켜 두었으면 시험도 메신저로 같은 글을 한 통 보낸다(실제 발송 기록은 남기지 않는다).
                    if (Settings.MessengerNotifyEnabled) StartMessengerSend("[시험] "+AvatarMessenger.MeetingText(new CalendarEntry[]{ entry },DateTime.Now),"시험 회의 알림");
                    return;
                }
                if (action=="run-now") { if (source.IsPopup) return; if (Settings.ThemeIds==null || Settings.ThemeIds.Length==0) throw new InvalidOperationException("저장된 주제가 없습니다."); StartRound(); return; }
                if (action=="preview") { if (Popup==null) pendingRound.Clear(); ShowPopup(Convert.ToString(msg["themeId"])); return; }
                if (action=="exit") { if (!source.IsPopup) Quit(); return; }
                if (action=="open-link")
                {
                    Uri uri;
                    if (!Uri.TryCreate(Convert.ToString(msg["url"]),UriKind.Absolute,out uri) || !AllowedLink(uri)) throw new ArgumentException("허용된 공식 영상·원문·공유 링크만 열 수 있습니다.");
                    if (source.IsPopup) source.TopMost=false;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute=true }); return;
                }
            }
            catch (Exception error) { source.Send(new { type="error", message=error.Message }); }
        }

        private static bool AllowedLink(Uri uri)
        {
            if (uri.Scheme!="https" || !String.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return false;
            // Exact-host match only: the videos shown in the cards.
            switch (uri.Host)
            {
                case "www.youtube.com": case "youtube.com": case "youtu.be":
                    return true;
            }
            // GitHub: only this project's "new file" page for the community folder and its documents on main.
            return uri.Host=="github.com" && (uri.AbsolutePath.StartsWith("/panicdna/kinds-of-thing/new/main/community", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/panicdna/kinds-of-thing/blob/main/", StringComparison.Ordinal) ||
                // The "delete this file" page of exactly one shared item's file; GitHub asks for login and review before anything changes.
                (System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/panicdna/kinds-of-thing/delete/main/community/[a-z0-9][a-z0-9-]{2,39}\\.json$") && !uri.AbsolutePath.EndsWith("/community/index.json", StringComparison.Ordinal)));
        }

        public void Quit()
        {
            Exiting=true; timer.Stop(); timer.Dispose(); if (syncTimer!=null) { syncTimer.Stop(); syncTimer.Dispose(); }
            if (knoxSyncTimer!=null) { knoxSyncTimer.Stop(); knoxSyncTimer.Dispose(); }
            SystemEvents.SessionSwitch-=SessionChanged; SystemEvents.PowerModeChanged-=PowerChanged;
            tray.Visible=false; tray.Dispose();
            CloseKnoxPrewarm();
            if (Popup!=null) Popup.Close();
            if (SettingsWindow!=null) SettingsWindow.Close();
            ExitThread();
        }

        private async void RunSmoke()
        {
            var result=new Dictionary<string,object>();
            string folder=Path.Combine(Root,"tests","ui"); Directory.CreateDirectory(folder);
            try
            {
                foreach (string blocked in new string[] { "http://www.youtube.com/watch?v=Test", "https://www.youtube.com.evil.invalid/watch?v=Test", "https://user@www.youtube.com/watch?v=Test", "https://www.youtube.com:8443/watch?v=Test", "https://ko.wikisource.org/wiki/Test", "https://github.com/evil/kinds-of-thing/blob/main/README.md", "https://github.com/evil/kinds-of-thing/delete/main/community/u-abc.json", "https://github.com/panicdna/kinds-of-thing/delete/main/README.md", "https://github.com/panicdna/kinds-of-thing/delete/main/community/index.json", "https://github.com/panicdna/kinds-of-thing/delete/main/community/sub/u-abc.json", "file:///C:/Windows/notepad.exe" })
                    if (AllowedLink(new Uri(blocked))) throw new Exception("Unsafe external URL was accepted: " + blocked);
                if (!AllowedLink(new Uri("https://github.com/panicdna/kinds-of-thing/delete/main/community/u-test-video.json"))) throw new Exception("The delete page of a shared item must be openable.");
                if (!AllowedLink(new Uri("https://github.com/panicdna/kinds-of-thing/blob/main/%EC%89%BC-%EC%B6%94%EA%B0%80-%EA%B0%80%EC%9D%B4%EB%93%9C.md"))) throw new Exception("The guide link must be openable.");
                await Task.Delay(700);
                // 목록 편집 상태는 localStorage에, 카드 편집은 user-themes.json에 남으므로, 시험마다 깨끗한 상태에서 시작한다.
                await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("Object.keys(localStorage).filter(k=>k.indexOf('teum.listEdits.')===0).forEach(k=>localStorage.removeItem(k))");
                await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("window.send&&send('reset-themes')");
                await Task.Delay(300);
                string count=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#theme-grid .theme-card').length");
                if (Int32.Parse(count)!=Themes.Length) throw new Exception("Theme cards are missing.");
                result["themeCards"]=Themes.Length;
                await Capture(SettingsWindow,Path.Combine(folder,"settings.png"));
                int beforeStartH=Settings.StartHour, beforeStartM=Settings.StartMinute, beforeEndH=Settings.EndHour, beforeEndM=Settings.EndMinute;
                await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#start-time').value='10:30'; document.querySelector('#end-time').value='10:15'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.StartHour!=beforeStartH || Settings.StartMinute!=beforeStartM || Settings.EndHour!=beforeEndH || Settings.EndMinute!=beforeEndM || Settings.Enabled) throw new Exception("End time earlier than start time was accepted.");
                await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#interval').value='30'; document.querySelector('#start-time').value='08:15'; document.querySelector('#end-time').value='17:45'; document.querySelectorAll('input[name=theme]').forEach(x=>x.checked=['meditation','voice'].includes(x.value)); document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.IntervalMinutes!=30 || !Settings.Enabled || !Settings.ThemeIds.SequenceEqual(new string[]{"meditation","voice"})) throw new Exception("Saving interval/theme settings failed.");
                if (Settings.StartHour!=8 || Settings.StartMinute!=15 || Settings.EndHour!=17 || Settings.EndMinute!=45) throw new Exception("Hour:minute window was not saved.");
                string shown=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#start-time').value+'|'+document.querySelector('#end-time').value");
                if (shown!="\"08:15|17:45\"") throw new Exception("Saved hour:minute window is not shown in the inputs: " + shown);
                result["timeOfDayInputs"]=true;
                result["settingsRoundTrip"]=true;
                // 24-hour inputs: plain text "HH:MM" (no 오전/오후); 9:05 is normalised, 24:00 means midnight, 25:00 is rejected.
                var set24=SettingsWindow.Browser.CoreWebView2;
                if (await set24.ExecuteScriptAsync("document.querySelector('#end-time').type")!="\"text\"") throw new Exception("End time should be a plain 24-hour text input.");
                await set24.ExecuteScriptAsync("var a=document.querySelector('#start-time'); a.value='9:05'; a.dispatchEvent(new Event('blur')); document.querySelector('#end-time').value='24:00'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.StartHour!=9 || Settings.StartMinute!=5 || Settings.EndHour!=24 || Settings.EndMinute!=0) throw new Exception("24-hour times (9:05 and 24:00) were not saved.");
                if (await set24.ExecuteScriptAsync("document.querySelector('#start-time').value+'|'+document.querySelector('#end-time').value")!="\"09:05|24:00\"") throw new Exception("24-hour times are not shown as HH:MM.");
                await set24.ExecuteScriptAsync("document.querySelector('#end-time').value='25:00'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.EndHour!=24) throw new Exception("25:00 was accepted as an end time.");
                await set24.ExecuteScriptAsync("document.querySelector('#start-time').value='08:15'; document.querySelector('#end-time').value='17:45'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.StartHour!=8 || Settings.EndHour!=17) throw new Exception("Restoring the test window failed.");
                result["time24Inputs"]=true;
                // Quiet periods (lunch etc.): off by default; when on, one or more rows save with the
                // other settings — ＋ adds a row, － removes it — and bad ranges are refused.
                if (await set24.ExecuteScriptAsync("document.querySelector('#break-on').checked")!="false" || await set24.ExecuteScriptAsync("document.querySelector('#break-start').disabled")!="true") throw new Exception("The break should start switched off with its times disabled.");
                if (await set24.ExecuteScriptAsync("document.querySelectorAll('#break-list .break-row').length")!="1") throw new Exception("There should be one break row before any editing.");
                await set24.ExecuteScriptAsync("document.querySelector('#break-on').click(); document.querySelector('#break-start').value='12:00'; document.querySelector('#break-end').value='13:30'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (!Settings.BreakEnabled || Settings.Breaks==null || Settings.Breaks.Count!=1 || Settings.Breaks[0].StartHour!=12 || Settings.Breaks[0].StartMinute!=0 || Settings.Breaks[0].EndHour!=13 || Settings.Breaks[0].EndMinute!=30) throw new Exception("The break was not saved.");
                if (await set24.ExecuteScriptAsync("document.querySelector('#break-on').checked && !document.querySelector('#break-start').disabled && document.querySelector('#break-end').value")!="\"13:30\"") throw new Exception("The saved break is not shown again after saving.");
                // A second quiet period: ＋ adds a row that saves as part of the list, － removes it again.
                await set24.ExecuteScriptAsync("document.querySelector('#break-add').click()");
                await Task.Delay(200);
                if (await set24.ExecuteScriptAsync("document.querySelectorAll('#break-list .break-row').length")!="2") throw new Exception("The add button did not add a second break row.");
                await set24.ExecuteScriptAsync("var r=document.querySelectorAll('#break-list .break-row')[1]; r.querySelector('.break-start').value='15:00'; r.querySelector('.break-end').value='15:30'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.Breaks.Count!=2 || Settings.Breaks[1].StartHour!=15 || Settings.Breaks[1].StartMinute!=0 || Settings.Breaks[1].EndHour!=15 || Settings.Breaks[1].EndMinute!=30) throw new Exception("The second break was not saved.");
                if (await set24.ExecuteScriptAsync("document.querySelectorAll('#break-list .break-row').length")!="2" || await set24.ExecuteScriptAsync("document.querySelectorAll('#break-list .break-row')[1].querySelector('.break-end').value")!="\"15:30\"") throw new Exception("The saved second break is not shown again.");
                await set24.ExecuteScriptAsync("document.querySelectorAll('#break-list .break-remove')[1].click(); document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.Breaks.Count!=1 || Settings.Breaks[0].StartHour!=12 || Settings.Breaks[0].EndHour!=13 || Settings.Breaks[0].EndMinute!=30) throw new Exception("Removing the second break was not saved.");
                await set24.ExecuteScriptAsync("document.querySelector('#break-end').value='11:00'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.Breaks[0].EndHour!=13) throw new Exception("A break that ends before it starts was accepted.");
                await set24.ExecuteScriptAsync("document.querySelector('#break-start').value='08:00'; document.querySelector('#break-end').value='18:00'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.Breaks[0].StartHour!=12) throw new Exception("A break covering the whole reminder window was accepted.");
                // Two rows that together swallow the window are refused as well (they merge).
                await set24.ExecuteScriptAsync("document.querySelector('#break-add').click(); var r=document.querySelectorAll('#break-list .break-row')[1]; r.querySelector('.break-start').value='12:00'; r.querySelector('.break-end').value='18:00'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.Breaks.Count!=1 || Settings.Breaks[0].EndHour!=13) throw new Exception("Two breaks covering the whole window were accepted.");
                await set24.ExecuteScriptAsync("document.querySelectorAll('#break-list .break-remove')[1].click(); document.querySelector('#break-on').click(); document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.BreakEnabled) throw new Exception("Switching the break off was not saved.");
                result["breakTimes"]=true;
                // Immediate-run button: opens the next theme in saved order right away, regardless of schedule.
                if (Popup!=null) throw new Exception("A popup was open before the immediate-run check.");
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('#run-now'))")!="true") throw new Exception("Immediate-run button is missing.");
                // Saving pins ThemeMode to random-one, so the order is pinned here to keep the check deterministic.
                string savedRunMode=Settings.ThemeMode; string[] savedRunThemes=Settings.ThemeIds;
                Settings.ThemeMode=ThemeRotation.OrderedOne; Settings.ThemeIds=new string[]{"meditation","voice"}; cursor=0; pendingRound.Clear();
                try
                {
                    var runOrder=new List<string>();
                    for (int run=0; run<2; run++)
                    {
                        await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#run-now').click()");
                        await Task.Delay(600);
                        if (Popup==null) throw new Exception("Immediate-run button did not open a popup.");
                        runOrder.Add(Popup.ThemeId);
                        await Popup.Ready.Task;
                        Popup.Close();
                        await Task.Delay(200);
                    }
                    if (!runOrder.SequenceEqual(new string[]{"meditation","voice"})) throw new Exception("Immediate run did not follow saved theme order: " + String.Join(",",runOrder));
                }
                finally { Settings.ThemeMode=savedRunMode; Settings.ThemeIds=savedRunThemes; pendingRound.Clear(); }
                result["immediateRunButton"]=true;
                // Run mode: the old sidebar radios are gone; a small dropdown ("순차적으로/무작위로")
                // sits next to the "N개 선택" counter of section 03, and saving honors the choice.
                string modeMenu=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('input[name=theme-mode]').length+'|'+document.querySelectorAll('#theme-mode-select option').length+'|'+document.querySelectorAll('input[name=appearance]').length");
                if (modeMenu!="\"0|2|0\"") throw new Exception("Run mode should be the 03-head dropdown (no radios, no appearance radios): "+modeMenu);
                result["modeDropdown"]=true;
                // ---- 카드별 실행 방식: "주기 실행" 체크 ↔ "몇 시에 실행" 시각 입력, 한쪽 선택 시 다른 쪽 회색 ----
                var gridCdp=SettingsWindow.Browser.CoreWebView2;
                string cardOf="function c(){return document.querySelector('input[value=meditation]').closest('.theme-card');}";
                // 주기 실행 중인 카드: 시각 칸은 회색(비활성), "몇 시에 실행"도 회색.
                string cardModes=await gridCdp.ExecuteScriptAsync("(function(){var c=document.querySelector('input[value=meditation]').closest('.theme-card');var p=c.querySelector('input[name=theme]');var w=c.querySelector('input[name=theme-schedule]');var t=c.querySelector('.card-time');return p.checked+'/'+p.disabled+'/'+w.checked+'/'+w.disabled+'/'+t.disabled+'/'+p.closest('label').textContent;})()");
                if (!cardModes.StartsWith("\"true/false/false/true/true/") || !cardModes.Contains("주기 실행")) throw new Exception("주기 실행 카드의 초기 상태가 올바르지 않습니다: "+cardModes);
                // 주기 실행을 끄면 시각 쪽이 살아나고, "몇 시에 실행"을 켜면 주기 실행이 회색으로 바뀐다.
                await gridCdp.ExecuteScriptAsync(cardOf+"c().querySelector('input[name=theme]').click()");
                await Task.Delay(150);
                await gridCdp.ExecuteScriptAsync(cardOf+"c().querySelector('input[name=theme-schedule]').click()");
                await Task.Delay(150);
                string flipped=await gridCdp.ExecuteScriptAsync("(function(){var c=document.querySelector('input[value=meditation]').closest('.theme-card');var p=c.querySelector('input[name=theme]');var w=c.querySelector('input[name=theme-schedule]');var t=c.querySelector('.card-time');return p.checked+'/'+p.disabled+'/'+w.checked+'/'+w.disabled+'/'+t.disabled;})()");
                if (flipped!="\"false/true/true/false/false\"") throw new Exception("몇 시에 실행 선택 시 주기 실행이 회색이 되지 않습니다: "+flipped);
                // 저장: 시각(시:분:초)이 Schedules에 남고, 그 카드는 주기 목록에서 빠진다. 분만 쓰면 :00초로 저장된다.
                await gridCdp.ExecuteScriptAsync(cardOf+"c().querySelector('.card-time').value='13:30:45';document.querySelector('#save-settings').click()");
                await Task.Delay(400);
                if (!Settings.Schedules.ContainsKey("meditation") || Settings.Schedules["meditation"]!="13:30:45") throw new Exception("몇 시에 실행 시각(초 포함)이 설정에 저장되지 않았습니다.");
                if (Settings.ThemeIds.Contains("meditation")) throw new Exception("시각 실행 카드가 주기 목록에 남아 있습니다.");
                // 옛 형식 "13:30"을 입력해도 13:30:00으로 저장된다(초 생략 허용).
                await gridCdp.ExecuteScriptAsync(cardOf+"c().querySelector('.card-time').value='13:30';document.querySelector('#save-settings').click()");
                await Task.Delay(400);
                if (Settings.Schedules["meditation"]!="13:30:00") throw new Exception("분만 쓴 시각이 초 없이 저장되었습니다: "+Settings.Schedules["meditation"]);
                // 잘못된 시각은 저장이 막힌다.
                await gridCdp.ExecuteScriptAsync(cardOf+"c().querySelector('.card-time').value='99:99';document.querySelector('#save-settings').click()");
                await Task.Delay(300);
                if (Settings.Schedules["meditation"]!="13:30:00") throw new Exception("잘못된 시각이 저장을 통과했습니다.");
                // 상태 되돌리기: 시각 실행을 끄고 주기 실행으로 복귀 저장.
                await gridCdp.ExecuteScriptAsync(cardOf+"c().querySelector('input[name=theme-schedule]').click()");
                await Task.Delay(150);
                await gridCdp.ExecuteScriptAsync(cardOf+"c().querySelector('input[name=theme]').click()");
                await Task.Delay(150);
                await gridCdp.ExecuteScriptAsync("document.querySelector('#save-settings').click()");
                await Task.Delay(400);
                if (Settings.Schedules.ContainsKey("meditation")) throw new Exception("시각 실행 해제가 저장되지 않았습니다.");
                if (!Settings.ThemeIds.Contains("meditation")) throw new Exception("주기 실행 복귀가 저장되지 않았습니다.");
                result["cardScheduleToggle"]=true;
                // 실제 발사: 방금 지난 시각(초까지)을 주면 다음 틱(1초) 안에 그 카드만 뜬다. 하루 한 번 제약도 함께 본다.
                // 295번부터 시각 실행도 근무 창·요일 규칙을 따르므로, 이 검사는 벽시계(시각·요일)와 무관하게 돌도록
                // 근무 창과 "월~금만"을 잠시 끈다. 요일까지 끄지 않으면 주말에 이 검사가 실패한다(345).
                if (!Settings.Enabled) throw new Exception("알림이 꺼진 상태에서 시각 발사를 검사할 수 없습니다.");
                await gridCdp.ExecuteScriptAsync("document.querySelector('#limit-hours').click(); document.querySelector('#weekdays').checked=false; document.querySelector('#save-settings').click()");
                await Task.Delay(400);
                if (Settings.LimitHours) throw new Exception("근무 창 끄기가 저장되지 않았습니다.");
                if (Settings.WeekdaysOnly) throw new Exception("요일 제한 끄기가 저장되지 않았습니다.");
                int at=DateTime.Now.Hour*3600+DateTime.Now.Minute*60+DateTime.Now.Second-1; if (at<0) at=0;
                Settings.Schedules["meditation"]=string.Format("{0:00}:{1:00}:{2:00}",at/3600,(at/60)%60,at%60);
                try
                {
                    WebWindow fired=null;
                    for (int wait=0; wait<20 && fired==null; wait++) { await Task.Delay(400); if (Popup!=null) fired=Popup; } // 8s: 틱이 늦어져도(부하) 놓치지 않게
                    if (fired==null || fired.ThemeId!="meditation") throw new Exception("몇 시에 실행 카드가 시각에 뜨지 않았습니다.");
                    await fired.Ready.Task;
                    fired.Close(); await Task.Delay(300);
                    if (Popup!=null) throw new Exception("시각 실행 팝업이 닫히지 않았습니다.");
                    // 하루 한 번: 같은 시각이 남아 있어도 곧바로 다시 뜨지 않는다.
                    await Task.Delay(1500);
                    if (Popup!=null) throw new Exception("시각 실행 카드가 하루에 두 번 떴습니다.");
                }
                finally { Settings.Schedules.Remove("meditation"); }
                await gridCdp.ExecuteScriptAsync("document.querySelector('#limit-hours').click(); document.querySelector('#save-settings').click()");
                await Task.Delay(400);
                if (!Settings.LimitHours) throw new Exception("근무 창 되돌리기가 저장되지 않았습니다.");
                result["scheduleFires"]=true;
                // ---- 295: 사이드바 "다음 작은 쉼" 목록 — 시각 실행 카드의 시각도 뜨고, 지난 시각·재생 안 하는
                // 구간(마감 후·시작 전·쉬는 시간)은 빠진다. 창은 08:15~17:45, 쉬는 시간은 꺼져 있다.
                string schedHelper="window.__sched=function(v){var c=document.querySelector('input[value=meditation]').closest('.theme-card');var s=c.querySelector('input[name=theme-schedule]');if(!s.checked){c.querySelector('input[name=theme]').click();s.click();}c.querySelector('.card-time').value=v;document.querySelector('#save-settings').click();};window.__unsched=function(){var c=document.querySelector('input[value=meditation]').closest('.theme-card');var s=c.querySelector('input[name=theme-schedule]');if(s.checked){s.click();c.querySelector('input[name=theme]').click();}document.querySelector('#save-settings').click();};";
                await gridCdp.ExecuteScriptAsync(schedHelper);
                try
                {
                    string listTxt="(document.querySelector('#next-list')||{textContent:''}).textContent";
                    await gridCdp.ExecuteScriptAsync("window.__sched('23:59:00')"); await Task.Delay(500);
                    string afterClose=await gridCdp.ExecuteScriptAsync(listTxt);
                    if (afterClose.Contains("23:59:00")) throw new Exception("마감 후 시각이 '다음 작은 쉼' 목록에 나타났습니다: "+afterClose);
                    await gridCdp.ExecuteScriptAsync("window.__sched('07:00:00')"); await Task.Delay(500);
                    string beforeOpen=await gridCdp.ExecuteScriptAsync(listTxt);
                    if (beforeOpen.Contains("07:00:00")) throw new Exception("시작 전 시각이 '다음 작은 쉼' 목록에 나타났습니다: "+beforeOpen);
                    await gridCdp.ExecuteScriptAsync("document.querySelector('#break-on').click(); document.querySelector('#break-start').value='12:00'; document.querySelector('#break-end').value='13:30'; window.__sched('12:30:00')"); await Task.Delay(500);
                    string inBreak=await gridCdp.ExecuteScriptAsync(listTxt);
                    if (inBreak.Contains("12:30:00")) throw new Exception("쉬는 시간 내 시각이 '다음 작은 쉼' 목록에 나타났습니다: "+inBreak);
                    // 창을 끄고 미래 시각(지금+2분)을 주면 시각과 카드 이름이 뜬다 — 벽시계와 무관하게.
                    DateTime later=DateTime.Now.AddMinutes(2);
                    string laterTxt=string.Format("{0:00}:{1:00}:{2:00}",later.Hour,later.Minute,later.Second);
                    await gridCdp.ExecuteScriptAsync("document.querySelector('#break-on').click(); document.querySelector('#limit-hours').click(); window.__sched('"+laterTxt+"')"); await Task.Delay(500);
                    string upcoming=await gridCdp.ExecuteScriptAsync(listTxt);
                    if (!upcoming.Contains(laterTxt) || !upcoming.Contains("명상")) throw new Exception("미래의 시각 실행이 '다음 작은 쉼' 목록에 나타나지 않습니다: "+upcoming+" (기대 "+laterTxt+")");
                    // 지난 시각(지금-20분, 15분 유예 밖이라 실제로도 뜨지 않는다)은 생략된다. 자정 직후엔 시각이 감기므로 건너뛴다.
                    if (DateTime.Now.TimeOfDay>=TimeSpan.FromMinutes(25))
                    {
                        DateTime past=DateTime.Now.AddMinutes(-20);
                        string pastTxt=string.Format("{0:00}:{1:00}:{2:00}",past.Hour,past.Minute,past.Second);
                        await gridCdp.ExecuteScriptAsync("window.__sched('"+pastTxt+"')"); await Task.Delay(500);
                        string pastList=await gridCdp.ExecuteScriptAsync(listTxt);
                        if (pastList.Contains(pastTxt)) throw new Exception("지난 시각이 '다음 작은 쉼' 목록에 나타났습니다: "+pastList);
                    }
                    result["nextSchedulesList"]=true;
                }
                finally
                {
                    Settings.Schedules.Remove("meditation"); // 어떤 경로로든 시각 실행이 남지 않게 (await는 finally에 못 쓰므로 UI 복원은 아래에서)
                }
                await gridCdp.ExecuteScriptAsync("document.querySelector('#limit-hours').checked=true; document.querySelector('#break-on').checked=false; document.querySelector('#weekdays').checked=true; window.__unsched()");
                await Task.Delay(400);
                if (Settings.Schedules.ContainsKey("meditation")) throw new Exception("295 검사 뒤 시각 실행이 남아 있습니다.");
                if (!Settings.LimitHours) throw new Exception("295 검사 뒤 근무 창이 켜져 있지 않습니다.");
                if (!Settings.WeekdaysOnly) throw new Exception("295 검사 뒤 요일 제한이 켜져 있지 않습니다.");
                // "Next rest" button: shown only while rests remain in a run-all round; Next continues, Close ends the round.
                string savedMode=Settings.ThemeMode; string[] savedThemes=Settings.ThemeIds;
                Settings.ThemeMode=ThemeRotation.SequentialAll; Settings.ThemeIds=new string[]{"meditation","voice"};
                try
                {
                    StartRound(); await Task.Delay(600);
                    if (Popup==null || Popup.ThemeId!="meditation") throw new Exception("Run-all round did not start with the first theme.");
                    await Popup.Ready.Task; await Task.Delay(500); // the state message renders just after "ready"
                    if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.popup-next'))")!="true") throw new Exception("Next-rest button is missing while rests remain.");
                    if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.popup-next').nextElementSibling===document.querySelector('.popup-close')")!="true") throw new Exception("Next-rest button is not directly left of Close.");
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.popup-next').click()");
                    await Task.Delay(700);
                    if (Popup==null || Popup.ThemeId!="voice") throw new Exception("Next-rest button did not open the next theme.");
                    await Popup.Ready.Task; await Task.Delay(500);
                    if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.popup-next'))")!="false") throw new Exception("Next-rest button should be hidden on the last rest.");
                    Popup.Close(); await Task.Delay(300);
                    if (Popup!=null) throw new Exception("Round did not end after the last rest.");
                    StartRound(); await Task.Delay(600);
                    await Popup.Ready.Task;
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.popup-close').click()");
                    await Task.Delay(700);
                    if (Popup!=null) throw new Exception("Close should end the whole round, but another rest opened.");
                    result["nextRestButton"]=true;
                }
                finally { Settings.ThemeMode=savedMode; Settings.ThemeIds=savedThemes; pendingRound.Clear(); }
                // Voice theme: only videos, no picture, no timer, no steps. 337 프리셋: 3-7 호흡법까지 6개.
                ShowPopup("voice"); await Popup.Ready.Task; await Task.Delay(400);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="6") throw new Exception("Voice sub-item list should have 6 items.");
                string[] voiceIds={"88TBCVp-Tlc","x6y6SPzCQXQ","YdBUnWJBYAo","YLfFw6IuVqc","43MKF1bL_mM","2cy0bowQb5w"};
                for (int v=0; v<6; v++)
                {
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='"+v+"'; s.dispatchEvent(new Event('change'))");
                    await Task.Delay(300);
                    if (!(await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''")).Contains("youtube-nocookie.com/embed/"+voiceIds[v])) throw new Exception("Voice item #"+(v+1)+" did not embed its video.");
                    if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image, .instruction-panel, .timer-box'))")!="false") throw new Exception("Voice should show only the video.");
                }
                Popup.Close(); await Task.Delay(300);
                result["voiceVideoOnly"]=true;
                ShowPopup("stretch"); await Popup.Ready.Task; await Task.Delay(700);
                // Sub-item list: 15 items behind a one-line dropdown; direct picking switches the embedded videos.
                var stretchCdp=Popup.Browser.CoreWebView2;
                if (await stretchCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="15") throw new Exception("Stretch sub-item list should have 15 items.");
                if (await stretchCdp.ExecuteScriptAsync("Boolean(document.querySelector('#variant-mode'))")!="true") throw new Exception("Stretch order selector is missing.");
                string[] expectIds={"Md24PbVP72U","-yOiTWEgU_Y","-_Oqek7ARlk","2DRvc74GgYM","5fnEEzi_ev0","QteVMsNU3dk","ngbWg9WUMoE","VF20iXwq29U","dNZ2aJp2rwI","02pSlIL7lig","A50ClACVyFE","Q7ogtZAplLM","u5GPq0CC7U8","HCjvbjbCSsk"};
                for (int v=1; v<=14; v++)
                {
                    await stretchCdp.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='"+v+"'; s.dispatchEvent(new Event('change'))");
                    await Task.Delay(300);
                    string frameSrc=await stretchCdp.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!frameSrc.Contains("youtube-nocookie.com/embed/"+expectIds[v-1])) throw new Exception("Stretch item #"+(v+1)+" did not embed its video: "+frameSrc);
                }
                await stretchCdp.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='0'; s.dispatchEvent(new Event('change'))");
                await Task.Delay(500);
                result["stretchSubItems"]=true;
                Popup.Close(); await Task.Delay(300);
                // "일어나 움직이기" also has a sub-item list: six standing videos (the basic GIF variant was removed).
                ShowPopup("back-standing"); await Popup.Ready.Task; await Task.Delay(500);
                var standCdp=Popup.Browser.CoreWebView2;
                if (await standCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="6") throw new Exception("Standing sub-item list should have 6 items.");
                string[] standIds={"N-V53dmXJ9M","qB7oUZeiV98","exQ8fzkiZjg","uw1aUHnMfo8","EF_jq5ZaZMI","EN6IpNi45_E"};
                for (int v=0; v<6; v++)
                {
                    await standCdp.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='"+v+"'; s.dispatchEvent(new Event('change'))");
                    await Task.Delay(300);
                    string standSrc=await standCdp.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!standSrc.Contains("youtube-nocookie.com/embed/"+standIds[v])) throw new Exception("Standing item #"+(v+1)+" did not embed its video: "+standSrc);
                }
                // The basic GIF variant was removed, so the list ends with the sixth video — no original-picture item.
                Popup.Close(); await Task.Delay(300);
                result["standingSubItems"]=true;
                // Meditation shows only videos (two): a picker, but no original picture and no step list.
                ShowPopup("meditation"); await Popup.Ready.Task; await Task.Delay(500);
                var medCdp=Popup.Browser.CoreWebView2;
                 if (await medCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="3") throw new Exception("Meditation sub-item list should have 3 items.");
                 string[] medIds={"6Qu5E3MrVH8","LaM3-_ntnjU","dZewQEbQQM0"};
                for (int v=0; v<3; v++)
                {
                    await medCdp.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='"+v+"'; s.dispatchEvent(new Event('change'))");
                    await Task.Delay(300);
                    string medSrc=await medCdp.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!medSrc.Contains("youtube-nocookie.com/embed/"+medIds[v])) throw new Exception("Meditation item #"+(v+1)+" did not embed its video: "+medSrc);
                    if (await medCdp.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image, .instruction-panel'))")!="false") throw new Exception("Meditation should show only the video.");
                }
                Popup.Close(); await Task.Delay(300);
                result["meditationVideoOnly"]=true;
                ShowPopup("stretch"); await Popup.Ready.Task; await Task.Delay(500);
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='0'; s.dispatchEvent(new Event('change'))");
                await Task.Delay(300);
                if (Popup.Bounds!=Screen.PrimaryScreen.Bounds) throw new Exception("Popup is not fullscreen.");
                var active=Popup;
                string loaded=await active.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image') && document.querySelector('.motion-image').naturalWidth>0)");
                 if (loaded!="true") throw new Exception("Animated GIF failed to load.");
                 await Capture(active,Path.Combine(folder,"popup-stretch.png"));
                 // 소리 없는 화면에서도 볼륨 배지는 우하단에 뜨되 '소리 없음'을 알린다.
                 if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#volume-badge')||{textContent:'없음'}).textContent")!="\"🔊 이 화면엔 소리가 없어요\"") throw new Exception("영상 없는 화면의 볼륨 배지가 올바르지 않습니다.");
                 await active.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.popup-close').click()");
                await Task.Delay(350);
                if (Popup!=null || !Settings.Enabled || NextDue<=DateTime.Now) throw new Exception("Closing popup stopped background reminders or failed to rearm.");
                result["fullscreenGifAndDismiss"]=true;
                // 릴렉스 음향: only videos (two), started by itself; no picture and no step list.
                ShowPopup("relax"); await Popup.Ready.Task; await Task.Delay(500);
                // "보여 주는 순서": random by default, remembered in the settings when changed, kept when the settings window saves.
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#variant-mode').value")!="\"random\"") throw new Exception("Default order should be random.");
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var m=document.querySelector('#variant-mode'); m.value='sequential'; m.dispatchEvent(new Event('change'));");
                await Task.Delay(300);
                if (!Settings.VariantModes.ContainsKey("relax") || Settings.VariantModes["relax"]!="sequential") throw new Exception("The chosen order was not remembered in the settings.");
                await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#save-settings').click()");
                await Task.Delay(400);
                if (Settings.VariantModes["relax"]!="sequential") throw new Exception("Saving the settings window dropped the remembered order.");
                Popup.Close(); await Task.Delay(300);
                ShowPopup("relax"); await Popup.Ready.Task; await Task.Delay(500);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#variant-mode').value")!="\"sequential\"") throw new Exception("The remembered order was not applied on the next open.");
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var m=document.querySelector('#variant-mode'); m.value='random'; m.dispatchEvent(new Event('change'));");
                await Task.Delay(300);
                result["variantOrderRemembered"]=true;
                   if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="4") throw new Exception("Relax sub-item list should have 4 items.");
                   string[] relaxIds={"5yApImDt_Fk","Og_Qli3M_nE","p0YfyupeyO4","yq-lSVEu7U0"};
                   for (int v=0; v<4; v++)
                {
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='"+v+"'; s.dispatchEvent(new Event('change'))");
                    await Task.Delay(300);
                    string relaxSrc=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!relaxSrc.Contains("youtube-nocookie.com/embed/"+relaxIds[v]) || !relaxSrc.Contains("autoplay=1")) throw new Exception("Relax item #"+(v+1)+" did not embed its autoplaying video: "+relaxSrc);
                    if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image, .instruction-panel'))")!="false") throw new Exception("Relax should show only the video.");
                 }
                 // 반복 토글: 릴렉스 음향은 기본이 반복이고(loop=1&playlist=), 끄고 켜면 그 선택이 settings.json에 저장된다.
                 if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('#variant-loop'))")!="true") throw new Exception("The loop selector is missing.");
                 if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#variant-loop').value")!="\"on\"") throw new Exception("Relax should default to loop-on.");
                 if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#variant-frame').src.indexOf('loop=1')<0")!="false") throw new Exception("Relax default embed should carry loop parameters.");
                 await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var l=document.querySelector('#variant-loop'); l.value='off'; l.dispatchEvent(new Event('change'))");
                 await Task.Delay(400);
                 string loopOffSrc=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#variant-frame').src");
                 if (loopOffSrc.Contains("loop=1")) throw new Exception("Loop-off did not remove the loop parameters: "+loopOffSrc);
                 if (!Settings.LoopModes.ContainsKey("relax") || Settings.LoopModes["relax"]!="off") throw new Exception("The loop-off choice was not remembered.");
                 await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var l=document.querySelector('#variant-loop'); l.value='on'; l.dispatchEvent(new Event('change'))");
                 await Task.Delay(400);
                 string loopSrc=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#variant-frame').src");
                 if (!loopSrc.Contains("loop=1") || !loopSrc.Contains("playlist=")) throw new Exception("Loop-on did not rebuild the embed with loop parameters: "+loopSrc);
                 if (!Settings.LoopModes.ContainsKey("relax") || Settings.LoopModes["relax"]!="on") throw new Exception("The loop-on choice was not remembered.");
                 // ---- 팝업 볼륨: 우하단 배지 + ↑/↓ 키 입력(5씩). YouTube 재생기에 setVolume 명령을 보낸다. ----
                 await Popup.Browser.CoreWebView2.ExecuteScriptAsync("localStorage.setItem('teum.volume','70')");
                 await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowUp'}))");
                 await Task.Delay(200);
                 if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#volume-badge')||{textContent:'없음'}).textContent")!="\"🔊 볼륨 75 · Up/Down (키보드키)\"") throw new Exception("▲ 키가 볼륨 배지를 올리지 않았습니다.");
                 await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowDown'}));document.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowDown'}))");
                 await Task.Delay(200);
                 if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#volume-badge').textContent")!="\"🔊 볼륨 65 · Up/Down (키보드키)\"") throw new Exception("▼ 키가 볼륨 배지를 내리지 않았습니다.");
                 if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('teum.volume')")!="\"65\"") throw new Exception("볼륨 선택이 저장되지 않았습니다.");
                 // 배지는 닫기 버튼 바로 아래, 오른쪽 끝이 닫기 버튼과 맞는다(우상단). 2px는 소수점 반올림 오차.
                 // 볼륨 키를 누를 때마다 배지의 bump 애니메이션(0.25초, scale)이 재시작되고 getBoundingClientRect는
                 // transform까지 반영하므로, 애니메이션이 끝난 뒤에 재야 한다(중간에 재면 스케일이 위치에 섞인다).
                 await Task.Delay(600);
                 string badgeSpot=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var b=document.querySelector('#volume-badge').getBoundingClientRect();var c=document.querySelector('.popup-close').getBoundingClientRect();return Math.abs(b.right-c.right)<2 && Math.abs(b.top-c.bottom-8)<2;})()");
                 if (badgeSpot!="true") throw new Exception("볼륨 배지가 닫기 버튼 바로 아래에 있지 않습니다.");
                 // 입력칸에 커서가 있으면 화살표는 입력을 방해하지 않는다.
                 await Popup.Browser.CoreWebView2.ExecuteScriptAsync("localStorage.setItem('teum.volume','65');var e=document.createElement('input');document.body.appendChild(e);e.focus();e.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowUp',bubbles:true}));e.remove()");
                 await Task.Delay(200);
                 if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('teum.volume')")!="\"65\"") throw new Exception("입력칸에 커서가 있을 때 화살표가 볼륨을 바꿨습니다.");
                 await Popup.Browser.CoreWebView2.ExecuteScriptAsync("localStorage.setItem('teum.volume','70')"); // 뒤의 시험을 위해 되돌려 둔다
                 Popup.Close(); await Task.Delay(300);
                 result["popupVolumeKeys"]=true;
                 result["relaxVideoOnly"]=true;
                 result["variantLoopToggle"]=true;
                // 요리 레시피: videos only (two), started by itself; no picture and no step list.
                ShowPopup("recipe"); await Popup.Ready.Task; await Task.Delay(500);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="3") throw new Exception("Recipe sub-item list should have 3 items.");
                string[] recipeIds={"s10KwYn064E","Rjfzpzj3bug","kLT591PuXB4"};
                for (int v=0; v<3; v++)
                {
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='"+v+"'; s.dispatchEvent(new Event('change'))");
                    await Task.Delay(300);
                    string recipeSrc=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!recipeSrc.Contains("youtube-nocookie.com/embed/"+recipeIds[v]) || !recipeSrc.Contains("autoplay=1")) throw new Exception("Recipe item #"+(v+1)+" did not embed its autoplaying video: "+recipeSrc);
                    if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image, .instruction-panel'))")!="false") throw new Exception("Recipe should show only the video.");
                }
                Popup.Close(); await Task.Delay(300);
                result["recipeVideoOnly"]=true;
                // 퇴근버스: videos only (one wide bus-ride video), started by itself; no picture and no step list.
                ShowPopup("bus-home"); await Popup.Ready.Task; await Task.Delay(500);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="1") throw new Exception("Bus-home sub-item list should have 1 item.");
                string busSrc=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                if (!busSrc.Contains("youtube-nocookie.com/embed/TiA5TiaGScY") || !busSrc.Contains("autoplay=1")) throw new Exception("Bus-home did not embed its autoplaying video: "+busSrc);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image, .instruction-panel'))")!="false") throw new Exception("Bus-home should show only the video.");
                Popup.Close(); await Task.Delay(300);
                result["busHomeVideoOnly"]=true;
                // 목록 편집: 드롭다운 옆 '목록 편집'으로 항목을 추가·삭제·되돌리기한다. 이 팝업에서 시험하고 반드시 원상복구한다.
                ShowPopup("relax"); await Popup.Ready.Task; await Task.Delay(500);
                await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("send('popup-edit-mode',{on:'false'})"); // ensure a clean fullscreen start even if a previous run left windowed mode
                await Task.Delay(200);
                var editCdp=Popup.Browser.CoreWebView2;
                await editCdp.ExecuteScriptAsync("localStorage.setItem('teum.listEdits.relax','{}')");
                if (await editCdp.ExecuteScriptAsync("Boolean(document.querySelector('#variant-edit-box'))")!="false") throw new Exception("The edit panel should not exist until it is opened.");
                if (Popup.FormBorderStyle!=FormBorderStyle.None || !Popup.TopMost) throw new Exception("The popup should start borderless-fullscreen.");
                await editCdp.ExecuteScriptAsync("document.querySelector('#variant-edit').click()");
                if (await editCdp.ExecuteScriptAsync("Boolean(document.querySelector('#variant-edit-box'))")!="true") throw new Exception("The edit button did not open the panel.");
                await Task.Delay(200);
                if (Popup.FormBorderStyle==FormBorderStyle.None || Popup.TopMost) throw new Exception("Opening the editor did not switch the popup to windowed mode.");
                await editCdp.ExecuteScriptAsync("document.querySelector('#edit-close').click()");
                await Task.Delay(200);
                if (Popup.FormBorderStyle!=FormBorderStyle.None || !Popup.TopMost || Popup.Bounds!=Screen.PrimaryScreen.Bounds) throw new Exception("Closing the editor did not restore fullscreen.");
                // Re-open for the add/remove checks below (windowed mode again, like a real copy&paste session).
                await editCdp.ExecuteScriptAsync("document.querySelector('#variant-edit').click()");
                await Task.Delay(200);
                // 추가: 11자 ID 가진 링크를 넣으면 항목이 끝에 붙고, 패널은 계속 열려 있다.
                await editCdp.ExecuteScriptAsync("document.querySelector('#edit-url').value='https://youtu.be/abcdefghijk'; document.querySelector('#edit-title').value='시험용 영상'; document.querySelector('#edit-add-btn').click()");
                await Task.Delay(400);
                if (await editCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="5") throw new Exception("Adding a video did not grow the list.");
                if (await editCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option')[4].text.indexOf('시험용 영상')>=0")!="true") throw new Exception("The added video is not the last option.");
                if (await editCdp.ExecuteScriptAsync("Boolean(document.querySelector('#variant-edit-box'))")!="true") throw new Exception("The edit panel should stay open after adding.");
                // 추가한 항목 수정: '수정'으로 링크와 제목을 바꾸면 목록 표시와 저장된 항목이 바뀐다(아이디는 그대로).
                await editCdp.ExecuteScriptAsync("document.querySelector('.edit-modify[data-modify-id=\"custom-abcdefghijk\"]').click()");
                await Task.Delay(200);
                if (await editCdp.ExecuteScriptAsync("Boolean(document.querySelector('.edit-row-editing'))")!="true") throw new Exception("The modify button did not open the inline editor.");
                await editCdp.ExecuteScriptAsync("document.querySelector('.edit-row-url').value='https://youtu.be/zzzzzzzzzzz'; document.querySelector('.edit-row-title').value='바꾼 추가 영상'; document.querySelector('.edit-row-save').click()");
                await Task.Delay(400);
                if (await editCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option')[4].text.indexOf('바꾼 추가 영상')>=0")!="true") throw new Exception("Modifying the added video did not update the list.");
                if (await editCdp.ExecuteScriptAsync("JSON.parse(localStorage.getItem('teum.listEdits.relax')).added[0].youtubeId")!="\"zzzzzzzzzzz\"") throw new Exception("Modifying the added video did not update the stored entry.");
                // 사용자 추가 항목 빼기: 목록이 다시 기본 4개로 줄고 추가분은 목록에서 사라진다.
                await editCdp.ExecuteScriptAsync("document.querySelector('.edit-remove[data-remove-id=\"custom-abcdefghijk\"]').click()");
                await Task.Delay(400);
                if (await editCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="4") throw new Exception("Removing the added video did not shrink the list.");
                // 기본 항목 빼기·되돌리기: 뺀 항목은 '뺀 항목' 줄에 나타나고 되돌리면 돌아온다.
                await editCdp.ExecuteScriptAsync("document.querySelector('.edit-remove[data-remove-id=\"yt-5yApImDt_Fk\"]').click()");
                await Task.Delay(400);
                if (await editCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="3") throw new Exception("Removing a built-in did not shrink the list.");
                if (await editCdp.ExecuteScriptAsync("Boolean(document.querySelector('.edit-restore[data-restore-id=\"yt-5yApImDt_Fk\"]'))")!="true") throw new Exception("The removed built-in has no restore button.");
                await editCdp.ExecuteScriptAsync("document.querySelector('.edit-restore[data-restore-id=\"yt-5yApImDt_Fk\"]').click()");
                await Task.Delay(400);
                if (await editCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="4") throw new Exception("Restoring did not bring the built-in back.");
                // 기본 항목 수정: '수정'을 누르면 지금 링크와 제목이 칸에 채워져 확인할 수 있고, 저장하면 목록·재생기가 따른다.
                await editCdp.ExecuteScriptAsync("document.querySelector('.edit-modify[data-modify-id=\"yt-5yApImDt_Fk\"]').click()");
                await Task.Delay(200);
                if (await editCdp.ExecuteScriptAsync("document.querySelector('.edit-row-url').value.indexOf('5yApImDt_Fk')>=0 && document.querySelector('.edit-row-title').value.length>0")!="true") throw new Exception("The inline editor did not show the current link and title.");
                // 이미 목록에 있는 영상 링크는 거절된다(중복 방지). 저장되지 않고 칸은 그대로 열려 있다.
                await editCdp.ExecuteScriptAsync("document.querySelector('.edit-row-url').value='https://youtu.be/Og_Qli3M_nE'; document.querySelector('.edit-row-save').click()");
                await Task.Delay(200);
                if (await editCdp.ExecuteScriptAsync("Object.keys((JSON.parse(localStorage.getItem('teum.listEdits.relax')||'{}').edited)||{}).length")!="0") throw new Exception("A duplicate link should not be saved.");
                if (await editCdp.ExecuteScriptAsync("Boolean(document.querySelector('.edit-row-editing'))")!="true") throw new Exception("The rejected save should keep the inline editor open.");
                await editCdp.ExecuteScriptAsync("document.querySelector('.edit-row-url').value='https://youtu.be/0000000000A'; document.querySelector('.edit-row-title').value='바꾼 기본 영상'; document.querySelector('.edit-row-save').click()");
                await Task.Delay(400);
                if (await editCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option')[0].text.indexOf('바꾼 기본 영상')>=0")!="true") throw new Exception("The modified built-in title did not reach the list.");
                string modSrc=await editCdp.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                if (!modSrc.Contains("youtube-nocookie.com/embed/0000000000A")) throw new Exception("The modified built-in link did not reach the player: "+modSrc);
                if (await editCdp.ExecuteScriptAsync("JSON.parse(localStorage.getItem('teum.listEdits.relax')).edited['yt-5yApImDt_Fk'].youtubeId")!="\"0000000000A\"") throw new Exception("The built-in modification was not stored as an override.");
                // 되돌리기(초기화): 편집 내용 전체(수정 포함)를 지우고 기본 목록으로.
                await editCdp.ExecuteScriptAsync("document.querySelector('.edit-remove[data-remove-id=\"yt-5yApImDt_Fk\"]').click()");
                await Task.Delay(400);
                await editCdp.ExecuteScriptAsync("document.querySelector('#edit-reset').click()");
                await Task.Delay(400);
                if (await editCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="4") throw new Exception("Reset did not restore the default list.");
                string editLeftover=await editCdp.ExecuteScriptAsync("localStorage.getItem('teum.listEdits.relax')||''");
                if (editLeftover!="\"{}\"") throw new Exception("Reset left edits behind: "+editLeftover);
                Popup.Close(); await Task.Delay(300);
                result["variantListEditor"]=true;
                // Community sync: apply a fetched result without the network. Tiles must update and the user's unsaved tick must survive.
                var script = SettingsWindow.Browser.CoreWebView2;
                await script.ExecuteScriptAsync("document.querySelector('input[value=stretch]').checked=true");
                var sample = new SyncResult(); sample.Etag = "test";
                sample.Entries.Add(new SyncEntry { Name = "u-eye-20-20-20.json", Sha = "test", Text = File.ReadAllText(Path.Combine(Root, "community", "u-eye-20-20-20.json")) });
                ApplySync(sample); await Task.Delay(400);
                if (Themes.Length != baseThemes.Length + 1) throw new Exception("Community theme was not added.");
                if (await script.ExecuteScriptAsync("document.querySelectorAll('#theme-grid .theme-card').length") != Themes.Length.ToString()) throw new Exception("Community tile did not appear.");
                if (await script.ExecuteScriptAsync("Boolean(document.querySelector('input[value=\"c-u-eye-20-20-20\"]')) && document.querySelector('input[value=stretch]').checked") != "true") throw new Exception("Sync lost the user's unsaved selection.");
                await script.ExecuteScriptAsync("var f=function(i,v){var e=document.querySelector(i);e.value=v;e.dispatchEvent(new Event('input'));};f('#share-name','공유 확인');f('#share-summary','요약');f('#share-steps','하나\\n둘');f('#share-safety','아프면 중단');");
                if (await script.ExecuteScriptAsync("document.querySelector('#share-preview h3').textContent") != "\"공유 확인\"") throw new Exception("Share preview did not render the tile.");
                if (await script.ExecuteScriptAsync("document.querySelector('#share-panel').open") != "false") throw new Exception("Share dialog should start closed.");
                // The share launcher and the sync-now button are gone from the page.
                if (await script.ExecuteScriptAsync("Boolean(document.querySelector('#share-open') || document.querySelector('#sync-now') || document.querySelector('.share-launch') || document.querySelector('.sync-bar'))")!="false") throw new Exception("The share launcher / sync-now button should not exist.");
                await script.ExecuteScriptAsync("document.querySelector('#share-panel').showModal()");
                await Task.Delay(300);
                if (await script.ExecuteScriptAsync("document.querySelector('#share-panel').open") != "true") throw new Exception("The share dialog did not open.");
                await Capture(SettingsWindow, Path.Combine(folder, "settings-community.png"));
                // In-app guide inside the share dialog: visible, and the example button fills the form.
                if (await script.ExecuteScriptAsync("Boolean(document.querySelector('.share-guide')) && Boolean(document.querySelector('#share-guide-open')) && Boolean(document.querySelector('#share-videos'))") != "true") throw new Exception("The share guide is not shown in the dialog.");
                await script.ExecuteScriptAsync("document.querySelector('#share-example').click()");
                await Task.Delay(200);
                if (await script.ExecuteScriptAsync("document.querySelector('#share-name').value") != "\"목 풀기 한 번\"") throw new Exception("The example button did not fill the form.");
                await script.ExecuteScriptAsync("document.querySelector('#share-name').value=''; document.querySelector('#share-name').dispatchEvent(new Event('input')); document.querySelector('#share-copy').click();");
                await Task.Delay(200);
                if (await script.ExecuteScriptAsync("document.querySelector('#share-note').textContent.length>0") != "true") throw new Exception("A form error is not shown inside the dialog.");
                if (await script.ExecuteScriptAsync("document.querySelectorAll('input[name=share-icon]').length") != "16") throw new Exception("The icon picker should offer 16 symbols.");
                await script.ExecuteScriptAsync("document.querySelector('input[name=share-icon][value=\"♫\"]').click()");
                await Task.Delay(200);
                if (await script.ExecuteScriptAsync("document.querySelector('#share-preview .theme-symbol').textContent") != "\"♫\"") throw new Exception("The chosen icon is not shown in the preview tile.");
                result["shareIcon"]=true;
                result["shareGuide"]=true;
                await script.ExecuteScriptAsync("document.querySelector('#share-close').click()");
                ShowPopup("c-u-eye-20-20-20"); await Popup.Ready.Task; await Task.Delay(350);
                string[] eyeIds={"356wdTdVaTs","2oojWWfappA"};
                for (int v=0; v<2; v++)
                {
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='"+v+"'; s.dispatchEvent(new Event('change'))");
                    await Task.Delay(300);
                    string eyeSrc=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!eyeSrc.Contains("youtube-nocookie.com/embed/"+eyeIds[v]) || await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.text-stage, .instruction-panel, .motion-image'))")!="false") throw new Exception("Community popup should show only its videos: "+eyeSrc);
                }
                Popup.Close();
                result["communitySyncAndShare"] = true;
                // A shared item with videos: its own steps are item #1, each video one more item (reachability is checked by the sync itself).
                var withVideo = new SyncResult(); withVideo.Etag = "test2";
                withVideo.Entries.Add(sample.Entries[0]);
                withVideo.Entries.Add(new SyncEntry { Name = "u-test-video.json", Sha = "test2", Text = "{\"schema\":1,\"id\":\"u-test-video\",\"name\":\"영상 쉼\",\"summary\":\"영상이 있는 쉼\",\"safety\":\"아프면 멈추세요.\",\"icon\":\"♪\",\"instructions\":[\"편히 앉아요.\"],\"videos\":[{\"id\":\"x6y6SPzCQXQ\",\"title\":\"테스트 영상\"}]}" });
                ApplySync(withVideo); await Task.Delay(400);
                // Shared tiles (and only those) offer "삭제하기", which opens GitHub's delete page; built-in tiles and the share preview do not.
                // The GitHub "삭제하기" button is gone with the sync feature: shared tiles are pulled
                // out locally like any other card, and nothing opens GitHub any more.
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('[data-delete]').length")!="0") throw new Exception("The GitHub delete button should not exist.");
                // The chosen icon is the tile's symbol (shared items default to ✿).
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[value=\"c-u-test-video\"]').closest('.theme-card').querySelector('.theme-symbol').textContent")!="\"♪\"") throw new Exception("The shared item's icon is not shown on its tile.");
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[value=\"c-u-eye-20-20-20\"]').closest('.theme-card').querySelector('.theme-symbol').textContent")!="\"✿\"") throw new Exception("A shared item without an icon should keep the default symbol.");
                ShowPopup("c-u-test-video"); await Popup.Ready.Task; await Task.Delay(500);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="2") throw new Exception("A shared item with one video should list its steps plus the video.");
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='1'; s.dispatchEvent(new Event('change'))");
                await Task.Delay(300);
                if (!(await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''")).Contains("youtube-nocookie.com/embed/x6y6SPzCQXQ")) throw new Exception("The shared video did not embed.");
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='0'; s.dispatchEvent(new Event('change'))");
                await Task.Delay(300);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.instruction-panel'))")!="true") throw new Exception("The shared steps did not show as item #1.");
                Popup.Close(); await Task.Delay(300);
                ApplySync(sample); await Task.Delay(300);
                result["communityVideos"]=true;
                ShowPopup("dance"); await Popup.Ready.Task; await Task.Delay(350);
                string danceReport=await Popup.Browser.CoreWebView2.ExecuteScriptAsync(File.ReadAllText(Path.Combine(Root,"tests","dance-ui.js")));
                var danceCheck=Json.Deserialize<Dictionary<string,object>>(danceReport);
                if (danceCheck==null || !Convert.ToBoolean(danceCheck["passed"])) throw new Exception("Dance UI failed: " + (danceCheck==null ? danceReport : Convert.ToString(danceCheck["error"])));
                result["danceControls"]=danceCheck;
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("var s=document.querySelector('#variant-pick'); s.value='2'; s.dispatchEvent(new Event('change'))");
                await Task.Delay(300);
                await Capture(Popup,Path.Combine(folder,"dance-player.png"));
                Popup.Close();
                // Knox 일정 알림 설정: 토글·분 입력이 저장/복원되는지만 확인한다(Testing 모드에서는 CLI 호출 자체를 건너뛴다).
                await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#knox-on').checked=true; document.querySelector('#knox-lead').value='10'; document.querySelector('#save-settings').click();");
                await Task.Delay(400);
                if (!Settings.KnoxCalendarEnabled || Settings.KnoxLeadMinutes!=10) throw new Exception("Knox 일정 알림 설정이 저장되지 않았습니다.");
                string knoxShown=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#knox-on').checked+'|'+document.querySelector('#knox-lead').value");
                if (knoxShown!="\"true|10\"") throw new Exception("Knox 일정 알림 설정이 화면에 그대로 보이지 않습니다: "+knoxShown);
                result["knoxCalendarSettings"]=true;
                // 동기화 기록 란: 항상 자리해야 하고, 받아 온 적이 없으면 빈 안내문을 보여준다(시험 모드에서는 실제 호출을 건너뛴다).
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('#knox-log'))")!="true") throw new Exception("동기화 기록 란이 없습니다.");
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#knox-log').textContent")!="\"기록이 아직 없어요.\"") throw new Exception("빈 기록 란의 안내 문구가 올바르지 않습니다.");
                result["knoxSyncLogUi"]=true;
                // 테스트용 바로 가져오기 단추: 자리하고, 동기화 중에는 꺼진다(시험 모드에서는 실제 호출을 건너뛴다).
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('#knox-fetch'))")!="true") throw new Exception("지금 일정 가져오기 단추가 없습니다.");
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#knox-fetch').disabled")!="false") throw new Exception("가져오기 단추는 평시에 누를 수 있어야 합니다.");
                result["knoxFetchButton"]=true;
                // 일정 알리기 시험 단추: 자리하고, 골라야 할 일정이 없으면 꺼진다(시험 모드에서는 일정 목록이 비어 있다).
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('#knox-test-pick') && document.querySelector('#knox-test'))")!="true") throw new Exception("지금 알리기 시험 목록·단추가 없습니다.");
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#knox-test').disabled")!="true") throw new Exception("가져온 일정이 없으면 시험 단추는 꺼져 있어야 합니다.");
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#knox-test-pick').value")!="\"\"") throw new Exception("빈 목록의 안내 항목 값은 비어 있어야 합니다.");
                result["knoxTestButton"]=true;
                // 일정 알림 자동 닫힘 라디오: 네 가지 값이 자리하고 기본값은 '없음'이다.
                string autoCloseValues=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("Array.from(document.querySelectorAll('input[name=knox-auto-close]')).map(r=>r.value).join('|')+' checked:'+(document.querySelector('input[name=knox-auto-close]:checked')||{}).value");
                if (autoCloseValues!="\"none|start|after|end checked:none\"") throw new Exception("자동 닫힘 라디오 값/기본값이 올바르지 않습니다: "+autoCloseValues);
                result["knoxAutoCloseRadios"]=true;
                // 일정 알림 화면의 자동 닫힘 안내: 실제 knox 팝업을 띄워 설정을 한 줄로 말해 주는지 확인한다.
                knoxPopupEvents=SortedKnoxEvents(new CalendarEntry[]{ new CalendarEntry{ Uid="smoke-test", Title="스모크 시험 일정", StartLocal=DateTime.Now.AddMinutes(3), EndLocal=DateTime.Now.AddMinutes(33) } });
                Popup=new WebWindow(this,true,"knox:smoke-test"); Popup.Show(); Popup.Activate();
                await Popup.Ready.Task; await Task.Delay(500);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.knox-autoclose'))")!="true") throw new Exception("일정 알림 화면에 자동 닫힘 안내 줄이 없습니다.");
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.knox-autoclose').textContent.indexOf('자동으로 닫히지 않아요')<0")!="false") throw new Exception("기본(없음) 안내 문구가 올바르지 않습니다.");
                Settings.KnoxAutoClose=ReminderSettings.KnoxAutoCloseAfter; Settings.KnoxAutoCloseMinutes=7;
                SendState(Popup); await Task.Delay(300);
                string afterLine=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.knox-autoclose').textContent");
                if (!afterLine.Contains("7분 뒤")) throw new Exception("'시작 N분 뒤' 안내가 갱신되지 않았습니다: "+afterLine);
                Settings.KnoxAutoClose=ReminderSettings.KnoxAutoCloseEnd;
                SendState(Popup); await Task.Delay(300);
                string endLine=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.knox-autoclose').textContent");
                if (!endLine.Contains("종료 시각에 자동으로 닫혀요")) throw new Exception("'종료 시각' 안내가 갱신되지 않았습니다: "+endLine);
                Settings.KnoxAutoClose=ReminderSettings.KnoxAutoCloseNone; Settings.KnoxAutoCloseMinutes=5; Persist();
                // ---- 350: 같은 시각에 겹친 일정은 한 화면에 모두 표시 ----
                // 상태를 다시 보내면 종소리 iframe은 그대로 두고 안내 글자(veil)만 갱신돼야 한다.
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("window.__bellSmoke=document.querySelector('#knox-bell')");
                knoxPopupEvents=SortedKnoxEvents(new CalendarEntry[]{
                    new CalendarEntry{ Uid="smoke-a", Title="겹친 일정 첫 번째", StartLocal=DateTime.Now.AddMinutes(3), EndLocal=DateTime.Now.AddMinutes(33) },
                    new CalendarEntry{ Uid="smoke-b", Title="겹친 일정 두 번째", StartLocal=DateTime.Now.AddMinutes(4), EndLocal=null } });
                SendState(Popup); await Task.Delay(300);
                string overlapped=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.knox-item').length+'/'+(document.querySelector('.knox-eyebrow').textContent.indexOf('2개')>=0)+'/'+(document.querySelector('.knox-autoclose').textContent.indexOf('자동으로 닫히지 않아요')>=0)+'/'+(document.querySelector('#knox-bell')===window.__bellSmoke)");
                if (overlapped!="\"2/true/true/true\"") throw new Exception("겹친 일정이 한 화면에 모두 표시되지 않거나 종소리 영상이 다시 로드됩니다: "+overlapped);
                Popup.Close(); await Task.Delay(300);
                result["knoxAutoCloseBanner"]=true;
                result["knoxOverlappedEvents"]=true;
                // 자리비움 판단: 기본값은 꺼짐, 분 입력칸은 비활성.
                string pauseIdle=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var c=document.getElementById('pause-idle');var m=document.getElementById('idle-min');return c.checked+'/'+m.disabled+'/'+m.value;})()");
                if (pauseIdle!="\"false/true/10\"") throw new Exception("자리비움 체크박스 기본값이 올바르지 않습니다: "+pauseIdle);
                result["pauseIdleDefaults"]=true;
                // 토글 → 회색/편집 전환: 체크를 끄고 켜면 종속 입력칸의 disabled 상태가 따라간다(초기 상태 무관).
                string toggleIdle=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var c=document.getElementById('pause-idle');var m=document.getElementById('idle-min');var before=!m.disabled;c.click();var flipped=m.disabled===before;c.click();return flipped&&m.disabled===!before;})()");
                if (toggleIdle!="true") throw new Exception("자리비움 토글이 입력칸 상태를 바꾸지 않습니다.");
                string toggleKnox=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var k=document.getElementById('knox-on');var lead=document.getElementById('knox-lead');var radios=document.querySelectorAll('input[name=knox-auto-close]');var before=!lead.disabled&&!radios[0].disabled;k.click();var flipped=lead.disabled===before&&radios[0].disabled===before;k.click();return flipped&&(!lead.disabled===before);})()");
                if (toggleKnox!="true") throw new Exception("Knox 토글이 종속 입력칸 상태를 바꾸지 않습니다.");
                // ---- 364: Knox 메일 초대 설정 — 기본 꺼짐, 비밀번호 칸은 토글을 따라 활성화된다 ----
                string mailDefaults=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var c=document.getElementById('knox-mail-on');var p=document.getElementById('knox-mail-pass');var b=document.getElementById('knox-mail-pass-clear');return c.checked+'/'+p.disabled+'/'+p.type+'/'+b.disabled;})()");
                if (mailDefaults!="\"false/true/password/true\"") throw new Exception("Knox 메일 설정 기본값이 올바르지 않습니다: "+mailDefaults);
                string toggleMail=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var c=document.getElementById('knox-mail-on');var p=document.getElementById('knox-mail-pass');c.click();var on=!p.disabled;c.click();return on&&p.disabled;})()");
                if (toggleMail!="true") throw new Exception("Knox 메일 토글이 비밀번호 칸 상태를 바꾸지 않습니다.");
                result["knoxMailSettings"]=true;
                // ---- 373: 메신저 회의 알림 — 기본 꺼짐, 시험 실행은 연결 없음(시험·끊기 단추 잠김), 토글은 Knox 일정 토글을 따른다 ----
                string messengerDefaults=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var m=document.getElementById('messenger-on');var t=document.getElementById('messenger-test');var o=document.getElementById('messenger-logout');var l=document.getElementById('messenger-login');return m.checked+'/'+t.disabled+'/'+o.disabled+'/'+l.disabled;})()");
                if (messengerDefaults!="\"false/true/true/false\"") throw new Exception("메신저 알림 설정 기본값이 올바르지 않습니다: "+messengerDefaults);
                string toggleMessenger=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var k=document.getElementById('knox-on');var m=document.getElementById('messenger-on');var before=m.disabled;k.click();var flipped=m.disabled!==before;k.click();return flipped&&m.disabled===before;})()");
                if (toggleMessenger!="true") throw new Exception("Knox 일정 토글이 메신저 알림 토글 상태를 바꾸지 않습니다.");
                // 383: 메신저 줄·동작 기록·안내 문단은 화면에서만 숨긴다(요소는 남아 저장값이 유지된다).
                string uxHidden=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var q=['.knox-messenger-line','.knox-messenger-link-line','#knox-log-box','section:has(#knox-on) p.small-help.ux-hidden'];return q.map(function(x){var e=document.querySelector(x);return e?getComputedStyle(e).display:'missing';}).join('/');})()");
                if (uxHidden!="\"none/none/none/none\"") throw new Exception("숨겨야 할 설정 줄이 화면에 보입니다: "+uxHidden);
                result["messengerSettings"]=true;
                string toggleHours=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("(function(){var l=document.getElementById('limit-hours');var s=document.getElementById('start-time');var e=document.getElementById('end-time');var before=!s.disabled&&!e.disabled;l.click();var flipped=s.disabled===before&&e.disabled===before;l.click();return flipped&&(!s.disabled===before);})()");
                if (toggleHours!="true") throw new Exception("시간대 토글이 시작·종료 시각 상태를 바꾸지 않습니다.");
                result["toggleGreyStates"]=true;
                // 쉼 화면 테마: 선택 도구는 사라졌고, 색 모드는 시스템을 그대로 따라간다(밝게/어둡게 자동).
                string appearanceFollow=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('input[name=appearance]').length+'/'+document.documentElement.dataset.appearance");
                if (appearanceFollow!="\"0/light\"" && appearanceFollow!="\"0/dark\"") throw new Exception("화면 테마가 시스템을 따르지 않습니다: "+appearanceFollow);
                result["appearanceFollowsSystem"]=true;
                // ---- 카드 목록 편집: 빼기·추가·프리셋 복원, 그리고 일정 알림 영상 카드 ----
                var cardCdp=SettingsWindow.Browser.CoreWebView2;
                int baseThemeCount=Themes.Length;
                // 일정 알림 카드: 그리드 밖에 자리하고, 체크박스가 없어 주기 알림에 선택되지 않는다.
                if (await cardCdp.ExecuteScriptAsync("Boolean(document.querySelector('.knox-bell-card [data-preview=\"knox-bell\"]'))")!="true") throw new Exception("일정 알림 영상 카드의 관리 단추가 없습니다.");
                if (await cardCdp.ExecuteScriptAsync("document.querySelector('.knox-bell-card input[name=theme]')===null")!="true") throw new Exception("일정 알림 카드에는 주기 알림용 체크박스가 없어야 합니다.");
                // 댄스 카드는 빼기 단추가 없고, 다른 기본 카드에는 있다.
                if (await cardCdp.ExecuteScriptAsync("document.querySelector('input[value=dance]').closest('.theme-card').querySelector('[data-remove-theme]')===null")!="true") throw new Exception("댄스 카드에는 빼기 단추가 없어야 합니다.");
                if (await cardCdp.ExecuteScriptAsync("Boolean(document.querySelector('input[value=recipe]').closest('.theme-card').querySelector('[data-remove-theme]'))")!="true") throw new Exception("다른 기본 카드에는 빼기 단추가 있어야 합니다.");
                // 빼기: 목록에서 사라지고, 나중에 프리셋 복원으로 되돌아온다.
                await cardCdp.ExecuteScriptAsync("document.querySelector('input[value=recipe]').closest('.theme-card').querySelector('[data-remove-theme]').click()");
                await Task.Delay(400);
                if (Themes.Length!=baseThemeCount-1 || Themes.Any(t => t.id=="recipe")) throw new Exception("빼기 단추가 카드를 목록에서 지우지 않았습니다.");
                if (Settings.ThemeIds.Contains("recipe")) throw new Exception("뺀 카드가 선택 목록에 남아 있습니다.");
                // 댄스 카드 빼기 시도: 거부되고 목록은 그대로다.
                await cardCdp.ExecuteScriptAsync("send('remove-theme',{themeId:'dance'})");
                await Task.Delay(300);
                if (!Themes.Any(t => t.id=="dance")) throw new Exception("댄스 카드가 목록에서 사라졌습니다.");
                // 추가: 대화상자를 채워 보내면 새 카드가 목록 끝에 나타난다.
                await cardCdp.ExecuteScriptAsync("document.querySelector('#add-theme-open').click()");
                await Task.Delay(200);
                if (await cardCdp.ExecuteScriptAsync("document.querySelector('#theme-add-panel').open")!="true") throw new Exception("추가 대화상자가 열리지 않았습니다.");
                // 338: 대화상자(뒷배경) 바깥을 클릭해도 닫히지 않는다 — 입력하던 내용이 사라지지 않게.
                await cardCdp.ExecuteScriptAsync("document.querySelector('#theme-add-panel').click()");
                await Task.Delay(200);
                if (await cardCdp.ExecuteScriptAsync("document.querySelector('#theme-add-panel').open")!="true") throw new Exception("바깥 클릭으로 추가 대화상자가 닫혔습니다.");
                // 338: 주의사항을 비워 둔 채로도 카드가 저장된다.
                await cardCdp.ExecuteScriptAsync("var f=function(i,v){var e=document.querySelector(i);e.value=v;};f('#add-name','시험 쉼');f('#add-summary','시험용 카드');f('#add-steps','한 줄\\n두 줄');f('#add-videos','https://youtu.be/abcdefghijk | 시험 영상');document.querySelector('#theme-add-form').dispatchEvent(new Event('submit'))");
                await Task.Delay(400);
                var added=Themes.FirstOrDefault(t => t.name=="시험 쉼");
                if (added==null || !added.user || added.id==null || !added.id.StartsWith("x-") || added.videos==null || added.videos.Length!=1 || added.videos[0].id!="abcdefghijk") throw new Exception("새 카드가 올바르게 추가되지 않았습니다.");
                if (!String.IsNullOrEmpty(added.safety)) throw new Exception("주의사항을 비워 둔 카드가 저장되지 않아야 합니다(빈 값이어야 함).");
                if (await cardCdp.ExecuteScriptAsync("Boolean(document.querySelector('input[value=\""+added.id+"\"]'))")!="true") throw new Exception("새 카드 타일이 목록에 나타나지 않았습니다.");
                // 339: 내가 만든 카드의 하위 목록에는 '기본 · 동작 안내' 항목이 끼워지지 않는다 — 영상만 나온다.
                ShowPopup(added.id); await Popup.Ready.Task; await Task.Delay(400);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="1") throw new Exception("A user-made card should list only its video, not the '기본 · 동작 안내' item.");
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#variant-pick option').text.indexOf('기본 · 동작 안내')>=0")=="true") throw new Exception("The user-made card must not offer the '기본 · 동작 안내' item.");
                Popup.Close(); await Task.Delay(300);
                // 프리셋 복원: 뺀 카드가 돌아오고 내가 만든 카드는 사라진다.
                await cardCdp.ExecuteScriptAsync("document.querySelector('#reset-themes').click()");
                await Task.Delay(400);
                if (!Themes.Any(t => t.id=="recipe") || Themes.Any(t => t.user)) throw new Exception("프리셋 복원이 카드 목록을 되돌리지 않았습니다.");
                if (Themes.Length!=baseThemeCount) throw new Exception("프리셋 복원 후 카드 수가 다릅니다: "+Themes.Length+" != "+baseThemeCount);
                result["cardListEditing"]=true;
                // 일정 알림 영상 관리 팝업: 4개 프리셋, 목록 편집으로 추가·빼기, 순서(무작위/순서대로)·반복 선택이 있다.
                await cardCdp.ExecuteScriptAsync("localStorage.removeItem('teum.listEdits.knox-bell')");
                ShowPopup("knox-bell"); await Popup.Ready.Task; await Task.Delay(500);
                var bellCdp=Popup.Browser.CoreWebView2;
                if (await bellCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="5") throw new Exception("종소리 영상 프리셋이 5개가 아닙니다.");
                // 순서 선택: 이제 다른 카드와 같은 선택기가 있다. 기본 무작위, '순서대로'는 설정에 저장된다.
                if (await bellCdp.ExecuteScriptAsync("Boolean(document.querySelector('#variant-mode'))")!="true") throw new Exception("종소리 목록에 순서 선택이 없습니다.");
                if (await bellCdp.ExecuteScriptAsync("document.querySelector('#variant-mode').value")!="\"random\"") throw new Exception("종소리 순서 기본값은 무작위여야 합니다.");
                await bellCdp.ExecuteScriptAsync("var m=document.querySelector('#variant-mode'); m.value='sequential'; m.dispatchEvent(new Event('change'));");
                await Task.Delay(400);
                if (!Settings.VariantModes.ContainsKey("knox-bell") || Settings.VariantModes["knox-bell"]!="sequential") throw new Exception("종소리 '순서대로' 선택이 설정에 저장되지 않았습니다.");
                await bellCdp.ExecuteScriptAsync("var m=document.querySelector('#variant-mode'); m.value='random'; m.dispatchEvent(new Event('change'));");
                await Task.Delay(400);
                // 반복 토글: 일정 알림 영상도 끌 수 있다("반복 끄기"). 기본은 켜짐(종소리가 알림 역할).
                if (await bellCdp.ExecuteScriptAsync("Boolean(document.querySelector('#variant-loop'))")!="true") throw new Exception("종소리 목록에 반복 선택이 없습니다.");
                if (await bellCdp.ExecuteScriptAsync("document.querySelector('#variant-loop').value")!="\"on\"") throw new Exception("종소리 반복 기본값은 켜짐이어야 합니다.");
                if (await bellCdp.ExecuteScriptAsync("document.querySelector('#variant-frame').src.indexOf('loop=1')<0")!="false") throw new Exception("종소리 미리보기 기본 임베드에 반복 파라미터가 없습니다.");
                await bellCdp.ExecuteScriptAsync("var l=document.querySelector('#variant-loop'); l.value='off'; l.dispatchEvent(new Event('change'))");
                await Task.Delay(400);
                string bellOff=await bellCdp.ExecuteScriptAsync("document.querySelector('#variant-frame').src");
                if (bellOff.Contains("loop=1")) throw new Exception("종소리 반복 끄기가 미리보기에 반영되지 않았습니다: "+bellOff);
                if (!Settings.LoopModes.ContainsKey("knox-bell") || Settings.LoopModes["knox-bell"]!="off") throw new Exception("종소리 반복 끄기가 설정에 저장되지 않았습니다.");
                await bellCdp.ExecuteScriptAsync("var l=document.querySelector('#variant-loop'); l.value='on'; l.dispatchEvent(new Event('change'))");
                await Task.Delay(400);
                string bellOn=await bellCdp.ExecuteScriptAsync("document.querySelector('#variant-frame').src");
                if (!bellOn.Contains("loop=1")) throw new Exception("종소리 반복 켜기가 미리보기에 반영되지 않았습니다: "+bellOn);
                Settings.LoopModes.Remove("knox-bell"); Persist(); // 다음 실행에는 기본(켜짐)으로
                await bellCdp.ExecuteScriptAsync("document.querySelector('#variant-edit').click()");
                await bellCdp.ExecuteScriptAsync("document.querySelector('#edit-url').value='https://youtu.be/abcdefghijk'; document.querySelector('#edit-title').value='새 종소리'; document.querySelector('#edit-add-btn').click()");
                await Task.Delay(400);
                if (await bellCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="6") throw new Exception("종소리 영상 추가가 목록에 반영되지 않았습니다.");
                await bellCdp.ExecuteScriptAsync("document.querySelector('.edit-remove[data-remove-id=\"yt-ADRjuOE-yJA\"]').click()");
                await Task.Delay(400);
                if (await bellCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="5") throw new Exception("종소리 영상 빼기가 목록에 반영되지 않았습니다.");
                if (await bellCdp.ExecuteScriptAsync("Boolean(document.querySelector('#variant-frame') && document.querySelector('#variant-frame').src.indexOf('youtube-nocookie.com/embed/')>=0)")!="true") throw new Exception("종소리 영상 미리보기가 재생되지 않습니다.");
                await bellCdp.ExecuteScriptAsync("document.querySelector('#edit-reset').click()");
                await Task.Delay(400);
                if (await bellCdp.ExecuteScriptAsync("document.querySelectorAll('#variant-pick option').length")!="5") throw new Exception("종소리 프리셋 복원이 목록을 되돌리지 않았습니다.");
                Popup.Close(); await Task.Delay(300);
                result["knoxBellManager"]=true;
                // ---- 쉼 카드 내보내기/가져오기: 버튼이 있고, 내보내기 JSON이 카드 목록+선택+목록 편집을
                // 담으며, 가져오기가 내 카드·선택·목록 편집을 되살린다(파일 대화상자는 테스트에서 열지 않는다).
                var setCdp=SettingsWindow.Browser.CoreWebView2;
                if (await setCdp.ExecuteScriptAsync("Boolean(document.querySelector('#export-cards') && document.querySelector('#import-cards'))")!="true") throw new Exception("내보내기/가져오기 버튼이 없습니다.");
                await setCdp.ExecuteScriptAsync("localStorage.setItem('teum.listEdits.stretch', JSON.stringify({removed:{'gif-1':true},added:[{id:'yt-test01',youtubeId:'dQw4w9WgXcQ',title:'시험 영상',artist:''}]}))");
                string exportProbe=await setCdp.ExecuteScriptAsync("(function(){var d=JSON.parse(window.__teumCards.build());return d.format+'/'+d.cards.length+'/'+(d.listEdits.stretch?d.listEdits.stretch.added.length:-1)+'/'+d.themeIds.length;})()");
                string exportExpect=String.Format("\"shim-cards/{0}/1/{1}\"",Themes.Length+1,Settings.ThemeIds.Length); // 카드 수 = 그리드 카드 + 종소리 항목
                if (exportProbe!=exportExpect) throw new Exception("내보내기 JSON이 카드 목록/목록 편집/선택을 담지 않았습니다: "+exportProbe+" (기대 "+exportExpect+")");
                await setCdp.ExecuteScriptAsync("window.__teumCards.apply(JSON.stringify({format:'shim-cards',version:1,themeIds:['stretch'],removedThemeIds:[],listEdits:{stretch:{removed:{},added:[{id:'yt-in01',youtubeId:'dQw4w9WgXcQ',title:'가져온 영상',artist:''}]}},cards:[{id:'x-0badc0de',name:'가져온 쉼 카드',summary:'가져오기 시험용',safety:'통증이 생기면 중단하세요.',user:true,instructions:['한 줄 안내'],baseVideos:[{id:'dQw4w9WgXcQ',title:'가져온 영상'}],videos:[]}]}))");
                await Task.Delay(700);
                if (!Themes.Any(t => t.id=="x-0badc0de" && t.name=="가져온 쉼 카드")) throw new Exception("가져오기가 내 카드를 되살리지 않았습니다.");
                if (!Settings.ThemeIds.SequenceEqual(new string[]{"stretch"})) throw new Exception("가져오기가 선택한 카드를 반영하지 않았습니다.");
                if (await setCdp.ExecuteScriptAsync("(function(){var e=JSON.parse(localStorage.getItem('teum.listEdits.stretch')||'{}');return (e.added||[]).some(function(a){return a.id==='yt-in01';});})()")!="true") throw new Exception("가져오기가 목록 편집을 저장하지 않았습니다.");
                if (await setCdp.ExecuteScriptAsync("Boolean(document.querySelector('input[value=\"x-0badc0de\"]'))")!="true") throw new Exception("가져온 카드가 목록에 나타나지 않습니다.");
                // ---- 296: 일정 알림 종소리 영상도 세 기능에 포함된다 ----
                // 내보내기: 종소리 목록 편집이 있으면 그대로 담긴다.
                await setCdp.ExecuteScriptAsync("localStorage.setItem('teum.listEdits.knox-bell', JSON.stringify({removed:{},added:[{id:'yt-bell99',youtubeId:'dQw4w9WgXcQ',title:'종소리 시험',artist:''}]}))");
                string bellProbe=await setCdp.ExecuteScriptAsync("(function(){var d=JSON.parse(window.__teumCards.build());return ('knox-bell' in d.listEdits)+'/'+d.listEdits['knox-bell'].added.length;})()");
                if (bellProbe!="\"true/1\"") throw new Exception("내보내기가 종소리 목록 편집을 담지 않았습니다: "+bellProbe);
                // 기본 프리셋 되돌리기: 종소리 목록 편집도 지워진다. (가져온 카드 정리도 이 클릭이 같이 한다.)
                await setCdp.ExecuteScriptAsync("document.querySelector('#reset-themes').click()");
                await Task.Delay(500);
                if (Themes.Any(t => t.id=="x-0badc0de") || userThemes.Count>0) throw new Exception("가져오기 검사 뒤 사용자 카드가 남아 있습니다.");
                string bellAfterReset=await setCdp.ExecuteScriptAsync("localStorage.getItem('teum.listEdits.knox-bell')||''");
                if (bellAfterReset!="\"\"") throw new Exception("기본 프리셋 되돌리기가 종소리 목록 편집을 지우지 않았습니다: "+bellAfterReset);
                // 가져오기: 목록 편집은 덮어쓰기 — 파일에 빈 종소리 편집이면 이 PC의 종소리 편집도 지워진다.
                // format 'teum-cards'는 '틈' 시절 예전 백업 파일의 호환 경로 검증을 겸한다(336).
                await setCdp.ExecuteScriptAsync("localStorage.setItem('teum.listEdits.knox-bell', JSON.stringify({removed:{},added:[{id:'yt-bell99',youtubeId:'dQw4w9WgXcQ',title:'종소리 시험',artist:''}]}))");
                await setCdp.ExecuteScriptAsync("window.__teumCards.apply(JSON.stringify({format:'teum-cards',version:1,themeIds:['stretch'],removedThemeIds:[],listEdits:{'knox-bell':{removed:{},added:[]}},cards:[]}))");
                await Task.Delay(700);
                string bellAfterImport=await setCdp.ExecuteScriptAsync("localStorage.getItem('teum.listEdits.knox-bell')||'{}'");
                if (bellAfterImport.Contains("yt-bell99")) throw new Exception("가져오기가 종소리 목록 편집을 덮어쓰지 않았습니다: "+bellAfterImport);
                await setCdp.ExecuteScriptAsync("localStorage.removeItem('teum.listEdits.knox-bell'); localStorage.removeItem('teum.listEdits.stretch')");
                result["cardsExportImport"]=true;
                result["passed"]=true;
            }
            catch (Exception error) { result["passed"]=false; result["error"]=error.ToString(); Environment.ExitCode=1; }
            File.WriteAllText(Path.Combine(folder,"results.json"),Json.Serialize(result));
            Quit();
        }
        private async Task Capture(WebWindow window, string path)
        {
            using (var file=File.Create(path)) await window.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        }
        public string ProfilePath { get { return Path.Combine(dataFolder,"WebView2"); } }
    }

    internal sealed class SyncResult
    {
        public bool NotModified;
        public string Error, Etag;
        public int Rejected;
        public List<SyncEntry> Entries = new List<SyncEntry>();
    }

    internal sealed class SyncEntry
    {
        public string Name, Sha, Text;
    }

    // Read-only GitHub client. Fetches only this project's community/ folder; the web page itself never touches the network.
    internal static class CommunitySync
    {
        private const string ListUrl = "https://api.github.com/repos/panicdna/kinds-of-thing/contents/community?ref=main";
        private const string RawPrefix = "https://raw.githubusercontent.com/panicdna/kinds-of-thing/main/community/";
        private const int MaxItems = 60, MaxListingBytes = 400000;
        private static readonly System.Text.RegularExpressions.Regex ShaPattern = new System.Text.RegularExpressions.Regex("^[0-9a-f]{40}$");
        private static readonly System.Text.RegularExpressions.Regex FileName = new System.Text.RegularExpressions.Regex("^[a-z0-9][a-z0-9-]{2,39}\\.json$");

        // known: files from the previous sync. A file is downloaded again only when its git sha changed.
        // Source order: community/index.json on raw.githubusercontent.com (no API quota; kept current by a GitHub Action),
        // then the GitHub contents API (60 requests/hour per IP, so only a fallback for a repo without the index).
        public static SyncResult Fetch(string etag, List<SyncEntry> known)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2 on .NET Framework 4.x
            var result = new SyncResult();
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal); // name -> git blob sha
            string body, newTag;
            int status = TryGet(RawPrefix + "index.json", Tag(etag, "idx:"), MaxListingBytes, out body, out newTag);
            if (status == 304) { result.NotModified = true; return result; }
            if (status == 200)
            {
                result.Etag = newTag == null ? null : "idx:" + newTag;
                var index = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
                object items;
                var list = index.TryGetValue("items", out items) ? items as System.Collections.IEnumerable : null;
                if (list != null) foreach (object item in list) Add(files, item as Dictionary<string, object>);
            }
            else if (status == 404)
            {
                status = TryGet(ListUrl, Tag(etag, "api:"), MaxListingBytes, out body, out newTag);
                if (status == 304) { result.NotModified = true; return result; }
                if (status == 404) return result; // no community folder yet
                if (status == 403 || status == 429) throw new Exception("GitHub 호출 한도에 도달했어요. 잠시 뒤 다시 시도합니다.");
                if (status != 200) throw new Exception("GitHub에 연결하지 못했어요. 인터넷 연결을 확인하세요.");
                result.Etag = newTag == null ? null : "api:" + newTag;
                foreach (var entry in new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(body)) Add(files, entry);
            }
            else throw new Exception("GitHub에 연결하지 못했어요. 인터넷 연결을 확인하세요.");

            foreach (var file in files.Take(MaxItems))
            {
                string name = file.Key;
                SyncEntry previous = known == null ? null : known.FirstOrDefault(k => k.Name == name && k.Sha == file.Value);
                if (previous != null) { result.Entries.Add(previous); continue; } // unchanged: reuse what we already validated
                byte[] bytes; string ignored;
                // raw.githubusercontent.com caches each URL for minutes; a new sha in the query forces a fresh copy of the edited file.
                try { bytes = GetBytes(RawPrefix + name + "?v=" + file.Value, null, CommunityContent.MaxFileBytes, out ignored); }
                catch (WebException) { throw new Exception("공유 항목 '" + name + "'을 읽지 못했어요. 다음 동기화에서 다시 시도합니다."); }
                catch (InvalidDataException) { result.Rejected++; continue; }
                // If the CDN still served an older copy, saving it under the new sha would hide the edit forever.
                if (GitBlobSha(bytes) != file.Value) throw new Exception("공유 항목 '" + name + "'의 새 내용이 아직 반영되는 중이에요. 다음 동기화에서 다시 시도합니다.");
                string text = System.Text.Encoding.UTF8.GetString(bytes);
                try
                {
                    CommunityItem item = CommunityContent.Parse(text);
                    if (item.Id + ".json" != name) throw new ArgumentException("File name must match id.");
                    // Shared videos are checked once, when the item is first downloaded: they must still be reachable and embeddable.
                    foreach (CommunityVideo video in item.Videos) VerifyVideo(video.id);
                    result.Entries.Add(new SyncEntry { Name = name, Sha = file.Value, Text = text });
                }
                catch (ArgumentException) { result.Rejected++; }
            }
            return result;
        }

        private static string Tag(string etag, string prefix)
        {
            return etag != null && etag.StartsWith(prefix, StringComparison.Ordinal) ? etag.Substring(prefix.Length) : null;
        }

        // Accepts {"name": "...", "sha": "<40 hex>"} (index.json item or API entry); "type" is only checked when present.
        private static void Add(SortedDictionary<string, string> files, Dictionary<string, object> entry)
        {
            object type, name, sha;
            if (entry == null || !entry.TryGetValue("name", out name) || !entry.TryGetValue("sha", out sha)) return;
            if (entry.TryGetValue("type", out type) && Convert.ToString(type) != "file") return;
            string fileName = Convert.ToString(name), hash = Convert.ToString(sha);
            if (fileName != "index.json" && FileName.IsMatch(fileName) && ShaPattern.IsMatch(hash)) files[fileName] = hash;
        }

        // The id git itself gives a file, so it can be compared with what the index/API announced.
        private static string GitBlobSha(byte[] content)
        {
            byte[] header = System.Text.Encoding.ASCII.GetBytes("blob " + content.Length + "\0");
            var all = new byte[header.Length + content.Length];
            Buffer.BlockCopy(header, 0, all, 0, header.Length); Buffer.BlockCopy(content, 0, all, header.Length, content.Length);
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
                return BitConverter.ToString(sha1.ComputeHash(all)).Replace("-", "").ToLowerInvariant();
        }

        // YouTube's oEmbed answers 200 only for a public video that allows embedding (401/403/404 otherwise).
        // A definite refusal rejects the shared item; an unreachable network stops this sync so it is retried later.
        private static void VerifyVideo(string videoId)
        {
            string body, tag;
            int status = TryGet("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString("https://www.youtube.com/watch?v=" + videoId), null, 20000, out body, out tag);
            if (status == 200) return;
            if (status == 401 || status == 403 || status == 404 || status == 400) throw new ArgumentException("Video " + videoId + " cannot be played here.");
            throw new Exception("영상 접속을 확인하지 못했어요. 다음 동기화에서 다시 시도합니다.");
        }

        // Returns the HTTP status (200/304/404/...), or -1 when the server could not be reached.
        private static int TryGet(string url, string etag, int maxBytes, out string text, out string responseEtag)
        {
            text = null; responseEtag = null;
            try
            {
                byte[] bytes = GetBytes(url, etag, maxBytes, out responseEtag);
                if (bytes == null) return 304;
                text = System.Text.Encoding.UTF8.GetString(bytes); return 200;
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                return response == null ? -1 : (int)response.StatusCode;
            }
        }

        private static byte[] GetBytes(string url, string etag, int maxBytes, out string responseEtag)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "DeskHooker-Teum"; request.Accept = "application/vnd.github+json"; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
            request.AllowAutoRedirect = false;
            if (etag != null) request.Headers["If-None-Match"] = etag;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                responseEtag = response.Headers["ETag"];
                // With redirects off, .NET hands back 304 as a normal, body-less response instead of throwing.
                if (response.StatusCode == HttpStatusCode.NotModified) return null;
                if (response.StatusCode != HttpStatusCode.OK) throw new WebException("Unexpected HTTP status " + (int)response.StatusCode);
                using (var stream = response.GetResponseStream())
                {
                    var buffer = new MemoryStream(); var chunk = new byte[4096]; int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                        if (buffer.Length > maxBytes) throw new InvalidDataException("Response too large.");
                    }
                    return buffer.ToArray();
                }
            }
        }
    }

    internal sealed class WebWindow : Form
    {
        private readonly ReminderHost host;
        public readonly bool IsPopup;
        public readonly string ThemeId;
        // True while this calendar-alert window is an off-screen prewarm: the page mutes the video,
        // and the form must not steal focus when shown. Cleared at the reveal moment.
        public bool Prewarm;
        public readonly WebView2 Browser;
        public readonly TaskCompletionSource<bool> Ready=new TaskCompletionSource<bool>();
        protected override bool ShowWithoutActivation { get { return Prewarm; } }
        public WebWindow(ReminderHost owner, bool popup, string themeId)
        {
            host=owner; IsPopup=popup; ThemeId=themeId;
            Text=popup?"쉼 · 따라 하는 작은 휴식":"쉼 · 알림 간격과 주제 설정";
            BackColor=ColorTranslator.FromHtml("#f4f3ed");
            Icon=ReminderHost.AppIcon;
            if (popup)
            {
                FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.Manual;
                Bounds=Screen.PrimaryScreen.Bounds; TopMost=true; ShowInTaskbar=true;
                KeyPreview=true;
                KeyDown+=delegate(object sender, KeyEventArgs e) { if(e.KeyCode==Keys.Escape) Close(); };
            }
            else { StartPosition=FormStartPosition.CenterScreen; Size=new Size(1320,930); MinimumSize=new Size(950,720); }
            Browser=new WebView2 { Dock=DockStyle.Fill, DefaultBackgroundColor=BackColor };
            Controls.Add(Browser);
            Shown+=async delegate { await Initialize(); };
            FormClosing+=delegate(object sender, FormClosingEventArgs e) { if(!IsPopup && !host.Exiting) { e.Cancel=true; Hide(); } };
            FormClosed+=delegate { if(IsPopup) host.PopupClosed(this); Browser.Dispose(); };
        }
        private async Task Initialize()
        {
            try
            {
                // Videos in the popups start by themselves, so the browser must not wait for a click before playing.
                var environment=await CoreWebView2Environment.CreateAsync(null,host.ProfilePath,new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
                await Browser.EnsureCoreWebView2Async(environment);
                Browser.CoreWebView2.Settings.IsStatusBarEnabled=false;
                Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
                Browser.CoreWebView2.Settings.IsZoomControlEnabled=false;
                Browser.CoreWebView2.Settings.AreDevToolsEnabled=host.Testing;
                Browser.CoreWebView2.PermissionRequested+=delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State=CoreWebView2PermissionState.Deny; };
                Browser.CoreWebView2.NewWindowRequested+=delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled=true; };
                Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("teum.local",host.Root,CoreWebView2HostResourceAccessKind.DenyCors);
                Browser.CoreWebView2.NavigationStarting+=delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    Uri target;
                    if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out target) || target.Scheme!="https" || target.Host!="teum.local" || target.AbsolutePath!="/app.html") e.Cancel=true;
                };
                Browser.CoreWebView2.WebMessageReceived+=delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e)
                {
                    Uri source;
                    if(Uri.TryCreate(e.Source,UriKind.Absolute,out source) && source.Scheme=="https" && source.Host=="teum.local" && source.AbsolutePath=="/app.html")
                    {
                        host.Handle(this,e.TryGetWebMessageAsString());
                        Ready.TrySetResult(true);
                    }
                };
                Browser.Source=new Uri("https://teum.local/app.html"+(IsPopup?"#popup":"#settings"));
            }
            catch(Exception error)
            {
                Ready.TrySetException(error);
                if(host.Testing) { File.WriteAllText(Path.Combine(host.Root,"webview-error.txt"),error.ToString()); Environment.ExitCode=1; host.Quit(); }
                else { MessageBox.Show("화면을 열 수 없습니다. Microsoft Edge WebView2 Runtime을 확인하세요.\n"+error.Message,"쉼"); Close(); }
            }
        }
        public void Send(object payload)
        {
            if(Browser.CoreWebView2!=null && !IsDisposed) Browser.CoreWebView2.PostWebMessageAsJson(host.Json.Serialize(payload));
        }
    }
}
