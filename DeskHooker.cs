using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Linq;
using System.Runtime.InteropServices;
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
        public string[] instructions;
        public CommunityVideo[] videos;
    }

    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread] private static void Main(string[] args)
        {
            bool testing = args.Contains("--ui-test");
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\DeskHooker.Teum" + (testing ? ".Test" : ""), out created))
            {
                if (!created) { MessageBox.Show("이미 실행 중입니다. 작업 표시줄의 틈 아이콘에서 설정을 여세요.", "틈"); return; }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { Application.Run(new ReminderHost(testing, args.Contains("--background"))); }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.txt"), error.ToString());
                    Environment.ExitCode = 1;
                    if (!testing) MessageBox.Show(error.Message, "틈 시작 오류");
                }
            }
        }
    }

    internal sealed class ReminderHost : ApplicationContext
    {
        public readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        public readonly bool Testing;
        public readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public Theme[] Themes;
        private Theme[] baseThemes;
        private const string CommunityNotice = "다른 사람이 공유한 내용으로 검증된 전문 안내가 아닙니다. 몸이 보내는 신호를 먼저 따르세요.";
        private volatile SyncResult pendingSync;
        private bool syncing;
        private string syncEtag, syncError;
        private DateTime? lastSync;
        private int communityCount, communityRejected;
        private List<SyncEntry> syncEntries = new List<SyncEntry>(); // what the last sync downloaded, keyed by file name + git sha
        private System.Windows.Forms.Timer syncTimer;
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
        private readonly Queue<string> scheduledCards = new Queue<string>(); // cards whose fixed start time has come, waiting for the screen
        private readonly Queue<string> pendingRound = new Queue<string>(); // themes still to show in this reminder's round
        private string lastShown;
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
            int cachedRejected;
            syncEntries = LoadCommunityCache();
            var cached = ToThemes(syncEntries.Select(e => e.Text), out cachedRejected);
            communityCount = cached.Count; communityRejected = cachedRejected;
            Themes = baseThemes.Concat(cached).ToArray();
            foreach (var theme in Themes.Where(t => !String.IsNullOrEmpty(t.file)))
            {
                string full = Path.GetFullPath(Path.Combine(Root, theme.file));
                if (!full.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new FileNotFoundException("주제 이미지가 없습니다: " + theme.file);
            }
            Settings = new ReminderSettings();
            string config = Path.Combine(dataFolder, "settings.json");
            if (!testing && File.Exists(config))
            {
                try { Settings = Json.Deserialize<ReminderSettings>(File.ReadAllText(config)); ReminderSchedule.Validate(Settings, Themes.Select(t => t.id)); }
                catch { Settings = new ReminderSettings(); notice = "이전 설정을 읽지 못해 안전한 기본값으로 시작했습니다. 설정을 다시 저장하세요."; }
            }
            // A card is either in the interval rotation or starts at a fixed time. Settings saved while both were possible keep the fixed time.
            if (Settings.CardSchedules != null && Settings.ThemeIds != null)
                Settings.ThemeIds = Settings.ThemeIds.Where(id => { CardSchedule cs; return !(Settings.CardSchedules.TryGetValue(id, out cs) && cs != null && cs.Enabled); }).ToArray();
            // The "all at once" run modes were removed from the menu; settings saved with them keep working as the one-per-reminder modes.
            if (Settings.ThemeMode == ThemeRotation.SequentialAll) Settings.ThemeMode = ThemeRotation.OrderedOne;
            else if (Settings.ThemeMode == ThemeRotation.RandomAll) Settings.ThemeMode = ThemeRotation.RandomOne;
            NextDue = ReminderSchedule.Next(DateTime.Now, Settings);
            tray = new NotifyIcon { Icon=SystemIcons.Information, Text="틈 · 스트레칭과 작은 쉼", Visible=true };
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
                Themes = baseThemes.Concat(list).ToArray();
                syncEntries = result.Entries;
                SaveCommunityCache(result.Entries);
                PruneRemovedSelections();
            }
            SendThemes();
        }

        // A shared item can be deleted upstream while it is still ticked; settings must never keep an unknown id.
        private void PruneRemovedSelections()
        {
            var known = new HashSet<string>(Themes.Select(t => t.id));
            string[] kept = Settings.ThemeIds.Where(id => known.Contains(id)).ToArray();
            if (kept.Length == Settings.ThemeIds.Length) return;
            Settings.ThemeIds = kept.Length > 0 ? kept : new string[] { "stretch" };
            cursor = 0; pendingRound.Clear(); Persist();
            notice = "공유가 삭제된 주제를 선택 목록에서 뺐습니다.";
        }

        private object SyncInfo()
        {
            return new { syncing = syncing, last = lastSync.HasValue ? lastSync.Value.ToString("o") : null, count = communityCount, rejected = communityRejected, error = syncError, offline = Testing };
        }

        private void SendThemes()
        {
            if (SettingsWindow == null || SettingsWindow.IsDisposed) return;
            SettingsWindow.Send(new { type = "themes", themes = Themes, settings = Settings, sync = SyncInfo(), notice = notice });
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
        }

        public void ShowSettings()
        {
            if (SettingsWindow.IsDisposed) SettingsWindow=new WebWindow(this,false,null);
            SettingsWindow.Show(); SettingsWindow.WindowState=FormWindowState.Normal; SettingsWindow.Activate(); SendState(SettingsWindow);
        }

        public void ShowPopup(string id)
        {
            if (Popup!=null) { Popup.Activate(); return; }
            if (!Themes.Any(t => t.id==id)) throw new ArgumentException("알 수 없는 주제입니다.");
            Popup=new WebWindow(this,true,id);
            Popup.Show(); Popup.Activate();
        }

        // Plans one reminder from the saved selection and mode, then shows its first theme.
        // In the "all" modes the remaining themes follow as each popup is closed.
        private void StartRound()
        {
            if (Popup!=null) { Popup.Activate(); return; }
            if (Settings.ThemeIds==null || Settings.ThemeIds.Length==0) return; // nothing in the interval rotation; cards may still start at fixed times
            pendingRound.Clear();
            foreach (var id in ThemeRotation.PlanRound(Settings.ThemeIds,Settings.ThemeMode,ref cursor,rng,lastShown)) pendingRound.Enqueue(id);
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

        // Cards that reached their fixed start time wait here while another popup is open, and open next in line.
        private bool ShowNextScheduled()
        {
            while (scheduledCards.Count>0)
            {
                string id=scheduledCards.Dequeue();
                if (!Themes.Any(t => t.id==id)) continue;
                lastShown=id; ShowPopup(id); return true;
            }
            return false;
        }

        public void PopupClosed(WebWindow window)
        {
            if (Popup==window) Popup=null;
            if (!Exiting)
            {
                NextDue=ReminderSchedule.Next(DateTime.Now,Settings);
                if (!ShowNextScheduled()) ShowNextInRound();
                UpdateStatus();
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
            DateTime previousTick=lastTick;
            if ((now-lastTick).TotalSeconds>15 || now<lastTick) NextDue=ReminderSchedule.Next(now,Settings);
            lastTick=now;
            // Cards with a fixed start time (HH:MM:SS): opened when their second is passed, never replayed after sleep or lock.
            if (!locked && Environment.UserInteractive)
            {
                foreach (string id in CardTimes.DueCards(previousTick,now,Settings,Themes.Select(t => t.id)))
                    if (!scheduledCards.Contains(id)) scheduledCards.Enqueue(id);
                if (Popup==null && scheduledCards.Count>0) ShowNextScheduled();
            }
            var finished=pendingSync; if (finished!=null) { pendingSync=null; ApplySync(finished); }
            if (ReminderSchedule.IsDue(now,NextDue,Settings,Popup!=null,false,!locked && Environment.UserInteractive))
            {
                if (Settings.ThemeIds==null || Settings.ThemeIds.Length==0) NextDue=ReminderSchedule.Next(now,Settings);
                else StartRound();
            }
            UpdateStatus();
        }
        private void UpdateStatus()
        {
            if (SettingsWindow!=null && !SettingsWindow.IsDisposed && SettingsWindow.IsHandleCreated)
                SettingsWindow.Send(new { type="tick", enabled=Settings.Enabled, next=NextDue.ToString("o"), now=DateTime.Now.ToString("o") });
        }
        public void SendState(WebWindow target)
        {
            if (target==null || target.IsDisposed) return;
            target.Send(new { type="state", mode=target.IsPopup?"popup":"settings", themeId=target.ThemeId,
                settings=Settings, themes=Themes, sync=SyncInfo(), next=NextDue.ToString("o"), now=DateTime.Now.ToString("o"), notice=notice, testing=Testing, remaining=pendingRound.Count });
        }

        public void Handle(WebWindow source, string message)
        {
            try
            {
                var msg=Json.Deserialize<Dictionary<string,object>>(message);
                string action=Convert.ToString(msg["action"]);
                if (action=="ready") { SendState(source); if (Testing && !source.IsPopup && !smokeStarted) { smokeStarted=true; RunSmoke(); } return; }
                if (action=="close") { if (source.IsPopup) { pendingRound.Clear(); source.Close(); } else source.Hide(); return; }
                if (action=="next") { if (source.IsPopup) source.Close(); return; }
                if (action=="save")
                {
                    if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 변경할 수 있습니다.");
                    var settings=Json.Deserialize<ReminderSettings>(Json.Serialize(msg["settings"]));
                    // The settings window no longer edits the breathing timings; keep the stored ones (the file format is unchanged).
                    settings.VariantModes=Settings.VariantModes; settings.InhaleSeconds=Settings.InhaleSeconds; settings.HoldSeconds=Settings.HoldSeconds; settings.ExhaleSeconds=Settings.ExhaleSeconds;
                    ReminderSchedule.Validate(settings,Themes.Select(t => t.id));
                    bool previousStartup=Settings.StartAtLogin;
                    Settings=settings;
                    if (Settings.StartAtLogin!=previousStartup) Startup(Settings.StartAtLogin);
                    Persist(); cursor=0; pendingRound.Clear(); NextDue=ReminderSchedule.Next(DateTime.Now,Settings);
                    notice=Settings.Enabled?"알림이 시작됐습니다. 창을 닫아도 트레이에서 계속 동작합니다.":"설정이 저장됐습니다. 알림은 일시정지 상태입니다.";
                    SendState(source); return;
                }
                if (action=="save-variant-mode")
                {
                    // A card's sub-item order ("보여 주는 순서") is chosen in that card's popup and remembered in settings.json.
                    if (!source.IsPopup) throw new InvalidOperationException("보여 주는 순서는 카드 팝업에서 바꿉니다.");
                    string cardId=Convert.ToString(msg["themeId"]), mode=Convert.ToString(msg["mode"]);
                    if (!Themes.Any(t => t.id==cardId) || !ReminderSettings.IsValidVariantMode(mode)) throw new ArgumentException("알 수 없는 카드이거나 순서 값이 올바르지 않습니다.");
                    var modes=new Dictionary<string,string>(Settings.VariantModes ?? new Dictionary<string,string>());
                    modes[cardId]=mode;
                    Settings.VariantModes=modes; Persist(); return;
                }
                if (action=="sync") { if (!source.IsPopup) StartSync(true); return; }
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
            SystemEvents.SessionSwitch-=SessionChanged; SystemEvents.PowerModeChanged-=PowerChanged;
            tray.Visible=false; tray.Dispose();
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
                // Quiet period (lunch): off by default; when on, it is saved with the other settings and bad ranges are refused.
                if (await set24.ExecuteScriptAsync("document.querySelector('#break-on').checked")!="false" || await set24.ExecuteScriptAsync("document.querySelector('#break-start').disabled")!="true") throw new Exception("The break should start switched off with its times disabled.");
                await set24.ExecuteScriptAsync("document.querySelector('#break-on').click(); document.querySelector('#break-start').value='12:00'; document.querySelector('#break-end').value='13:30'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (!Settings.BreakEnabled || Settings.BreakStartHour!=12 || Settings.BreakStartMinute!=0 || Settings.BreakEndHour!=13 || Settings.BreakEndMinute!=30) throw new Exception("The break was not saved.");
                if (await set24.ExecuteScriptAsync("document.querySelector('#break-on').checked && !document.querySelector('#break-start').disabled && document.querySelector('#break-end').value")!="\"13:30\"") throw new Exception("The saved break is not shown again after saving.");
                await set24.ExecuteScriptAsync("document.querySelector('#break-end').value='11:00'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.BreakEndHour!=13) throw new Exception("A break that ends before it starts was accepted.");
                await set24.ExecuteScriptAsync("document.querySelector('#break-start').value='08:00'; document.querySelector('#break-end').value='18:00'; document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.BreakStartHour!=12) throw new Exception("A break covering the whole reminder window was accepted.");
                await set24.ExecuteScriptAsync("document.querySelector('#break-on').click(); document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.BreakEnabled) throw new Exception("Switching the break off was not saved.");
                result["breakTimes"]=true;
                // Fixed start time per card: every tile has a second checkbox plus an HH:MM:SS input; they are saved with the settings.
                var cardUi=SettingsWindow.Browser.CoreWebView2;
                if (await cardUi.ExecuteScriptAsync("document.querySelectorAll('[data-ct-on]').length>0 && document.querySelectorAll('[data-ct-on]').length===document.querySelectorAll('#theme-grid .theme-card').length && document.querySelectorAll('[data-ct-time]').length===document.querySelectorAll('[data-ct-on]').length && document.querySelectorAll('#theme-grid .rotation-label').length===document.querySelectorAll('#theme-grid .theme-card').length")!="true") throw new Exception("Every tile should show the interval-rotation caption and the fixed-start-time checkbox with its time input.");
                if (await cardUi.ExecuteScriptAsync("document.querySelector('[data-ct-time=\"relax\"]').disabled")!="true") throw new Exception("The time input should be disabled until its checkbox is on.");
                // Exclusive per card: fixed start time on -> rotation off; rotation on -> fixed start time off (and its input disabled again).
                await cardUi.ExecuteScriptAsync("window.__sel=document.querySelector('input[name=theme][value=\"stretch\"]'); window.__on=document.querySelector('[data-ct-on=\"stretch\"]'); window.__time=document.querySelector('[data-ct-time=\"stretch\"]'); window.__sel.checked=true; window.__sel.dispatchEvent(new Event('change')); window.__on.click();");
                if (await cardUi.ExecuteScriptAsync("[window.__sel.checked, window.__on.checked, window.__time.disabled].join('|')")!="\"false|true|false\"") throw new Exception("Turning the fixed start time on should turn the interval rotation off for that card.");
                await cardUi.ExecuteScriptAsync("window.__sel.click();");
                if (await cardUi.ExecuteScriptAsync("[window.__sel.checked, window.__on.checked, window.__time.disabled].join('|')")!="\"true|false|true\"") throw new Exception("Turning the interval rotation on should turn the fixed start time off for that card.");
                if (await cardUi.ExecuteScriptAsync("document.querySelector('#selected-count').textContent.indexOf('주기 실행')===0")!="true") throw new Exception("The selected-card counter should count interval-rotation cards.");
                await cardUi.ExecuteScriptAsync("window.__sel.click();"); // leave the 'stretch' tile as it was (rotation off in the test settings)
                await cardUi.ExecuteScriptAsync("document.querySelector('[data-ct-on=\"relax\"]').click(); var t=document.querySelector('[data-ct-time=\"relax\"]'); t.value='9:05'; t.dispatchEvent(new Event('blur')); document.querySelector('#save-settings').click();");
                await Task.Delay(400);
                if (!Settings.CardSchedules.ContainsKey("relax") || !Settings.CardSchedules["relax"].Enabled || Settings.CardSchedules["relax"].Time!="09:05:00") throw new Exception("The card start time (9:05 -> 09:05:00) was not saved.");
                if (await cardUi.ExecuteScriptAsync("document.querySelector('[data-ct-on=\"relax\"]').checked && document.querySelector('[data-ct-time=\"relax\"]').value")!="\"09:05:00\"") throw new Exception("The saved start time is not shown again.");
                await cardUi.ExecuteScriptAsync("document.querySelector('[data-ct-time=\"relax\"]').value='25:00:00'; document.querySelector('#save-settings').click();");
                await Task.Delay(400);
                if (Settings.CardSchedules["relax"].Time!="09:05:00") throw new Exception("An impossible start time (25:00:00) was accepted.");
                // The interval rotation may be empty when a card starts at a fixed time.
                await cardUi.ExecuteScriptAsync("document.querySelector('[data-ct-time=\"relax\"]').value='09:05:00'; document.querySelectorAll('input[name=theme]').forEach(x=>x.checked=false); document.querySelector('#save-settings').click();");
                await Task.Delay(400);
                if (Settings.ThemeIds.Length!=0) throw new Exception("An empty interval rotation should be accepted while a card has a fixed start time.");
                await cardUi.ExecuteScriptAsync("document.querySelector('[data-ct-on=\"relax\"]').click(); document.querySelector('#save-settings').click();");
                await Task.Delay(400);
                if (!Settings.CardSchedules["relax"].Enabled) throw new Exception("Saving with no rotation and no fixed card should have been refused (the earlier fixed card must still be on).");
                await cardUi.ExecuteScriptAsync("document.querySelectorAll('input[name=theme]').forEach(x=>x.checked=['meditation','voice'].includes(x.value)); document.querySelector('#save-settings').click();");
                await Task.Delay(400);
                if (!Settings.ThemeIds.SequenceEqual(new string[]{"meditation","voice"})) throw new Exception("Restoring the test rotation failed.");
                // Real time: a card whose second arrives opens by itself, even though it is not in the rotation.
                bool oldWeekdays=Settings.WeekdaysOnly, oldEnabled=Settings.Enabled;
                Settings.WeekdaysOnly=false; Settings.Enabled=true;
                Settings.CardSchedules["relax"]=new CardSchedule { Enabled=true, Time=DateTime.Now.AddSeconds(3).ToString("HH:mm:ss") };
                if (Popup!=null) throw new Exception("A popup was open before the fixed-time check.");
                await Task.Delay(6500);
                if (Popup==null || Popup.ThemeId!="relax") throw new Exception("The card with a fixed start time did not open by itself.");
                Popup.Close(); await Task.Delay(400);
                if (Popup!=null) throw new Exception("The fixed-time popup did not close.");
                Settings.CardSchedules.Remove("relax"); Settings.WeekdaysOnly=oldWeekdays; Settings.Enabled=oldEnabled;
                result["cardFixedTimes"]=true;
                // Immediate-run button: opens the next theme in saved order right away, regardless of schedule.
                if (Popup!=null) throw new Exception("A popup was open before the immediate-run check.");
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('#run-now'))")!="true") throw new Exception("Immediate-run button is missing.");
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
                result["immediateRunButton"]=true;
                // Run-mode menu lives in the left sidebar (and only there), with its two choices.
                string sideMenu=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.sidebar input[name=theme-mode]').length+'|'+document.querySelectorAll('.main input[name=theme-mode]').length");
                if (sideMenu!="\"2|0\"") throw new Exception("Run-mode menu is not exactly in the sidebar: " + sideMenu);
                result["sidebarRunMode"]=true;
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
                // Voice theme: only videos (two), no picture, no timer, no steps.
                ShowPopup("voice"); await Popup.Ready.Task; await Task.Delay(400);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('[data-variant]').length")!="5") throw new Exception("Voice sub-item list should have 5 items.");
                string[] voiceIds={"88TBCVp-Tlc","x6y6SPzCQXQ","YdBUnWJBYAo","YLfFw6IuVqc","43MKF1bL_mM"};
                for (int v=0; v<5; v++)
                {
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-variant=\""+v+"\"]').click()");
                    await Task.Delay(300);
                    if (!(await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''")).Contains("youtube-nocookie.com/embed/"+voiceIds[v])) throw new Exception("Voice item #"+(v+1)+" did not embed its video.");
                    if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image, .instruction-panel, .timer-box'))")!="false") throw new Exception("Voice should show only the video.");
                }
                Popup.Close(); await Task.Delay(300);
                result["voiceVideoOnly"]=true;
                ShowPopup("stretch"); await Popup.Ready.Task; await Task.Delay(700);
                // Sub-item list: 4 items, direct picking switches between the original GIF and the embedded videos.
                var stretchCdp=Popup.Browser.CoreWebView2;
                if (await stretchCdp.ExecuteScriptAsync("document.querySelectorAll('[data-variant]').length")!="15") throw new Exception("Stretch sub-item list should have 15 items.");
                if (await stretchCdp.ExecuteScriptAsync("Boolean(document.querySelector('#variant-mode'))")!="true") throw new Exception("Stretch order selector is missing.");
                string[] expectIds={"Md24PbVP72U","-yOiTWEgU_Y","-_Oqek7ARlk","2DRvc74GgYM","5fnEEzi_ev0","QteVMsNU3dk","ngbWg9WUMoE","VF20iXwq29U","dNZ2aJp2rwI","02pSlIL7lig","A50ClACVyFE","Q7ogtZAplLM","u5GPq0CC7U8","HCjvbjbCSsk"};
                for (int v=1; v<=14; v++)
                {
                    await stretchCdp.ExecuteScriptAsync("document.querySelector('[data-variant=\""+v+"\"]').click()");
                    await Task.Delay(300);
                    string frameSrc=await stretchCdp.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!frameSrc.Contains("youtube-nocookie.com/embed/"+expectIds[v-1])) throw new Exception("Stretch item #"+(v+1)+" did not embed its video: "+frameSrc);
                }
                await stretchCdp.ExecuteScriptAsync("document.querySelector('[data-variant=\"0\"]').click()");
                await Task.Delay(500);
                result["stretchSubItems"]=true;
                Popup.Close(); await Task.Delay(300);
                // "일어나 움직이기" also has a sub-item list: 4 videos first, the original picture last.
                ShowPopup("back-standing"); await Popup.Ready.Task; await Task.Delay(500);
                var standCdp=Popup.Browser.CoreWebView2;
                if (await standCdp.ExecuteScriptAsync("document.querySelectorAll('[data-variant]').length")!="7") throw new Exception("Standing sub-item list should have 7 items.");
                string[] standIds={"N-V53dmXJ9M","qB7oUZeiV98","exQ8fzkiZjg","uw1aUHnMfo8","EF_jq5ZaZMI","EN6IpNi45_E"};
                for (int v=0; v<6; v++)
                {
                    await standCdp.ExecuteScriptAsync("document.querySelector('[data-variant=\""+v+"\"]').click()");
                    await Task.Delay(300);
                    string standSrc=await standCdp.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!standSrc.Contains("youtube-nocookie.com/embed/"+standIds[v])) throw new Exception("Standing item #"+(v+1)+" did not embed its video: "+standSrc);
                }
                await standCdp.ExecuteScriptAsync("document.querySelector('[data-variant=\"6\"]').click()");
                await Task.Delay(400);
                if (await standCdp.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image'))")!="true") throw new Exception("Standing original picture did not show.");
                Popup.Close(); await Task.Delay(300);
                result["standingSubItems"]=true;
                // Meditation shows only videos (two): a picker, but no original picture and no step list.
                ShowPopup("meditation"); await Popup.Ready.Task; await Task.Delay(500);
                var medCdp=Popup.Browser.CoreWebView2;
                if (await medCdp.ExecuteScriptAsync("document.querySelectorAll('[data-variant]').length")!="3") throw new Exception("Meditation sub-item list should have 3 items.");
                string[] medIds={"6Qu5E3MrVH8","LaM3-_ntnjU","Og_Qli3M_nE"};
                for (int v=0; v<3; v++)
                {
                    await medCdp.ExecuteScriptAsync("document.querySelector('[data-variant=\""+v+"\"]').click()");
                    await Task.Delay(300);
                    string medSrc=await medCdp.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!medSrc.Contains("youtube-nocookie.com/embed/"+medIds[v])) throw new Exception("Meditation item #"+(v+1)+" did not embed its video: "+medSrc);
                    if (await medCdp.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image, .instruction-panel'))")!="false") throw new Exception("Meditation should show only the video.");
                }
                Popup.Close(); await Task.Delay(300);
                result["meditationVideoOnly"]=true;
                ShowPopup("stretch"); await Popup.Ready.Task; await Task.Delay(500);
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-variant=\"0\"]').click()");
                await Task.Delay(300);
                if (Popup.Bounds!=Screen.PrimaryScreen.Bounds) throw new Exception("Popup is not fullscreen.");
                var active=Popup;
                string loaded=await active.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image') && document.querySelector('.motion-image').naturalWidth>0)");
                if (loaded!="true") throw new Exception("Animated GIF failed to load.");
                await Capture(active,Path.Combine(folder,"popup-stretch.png"));
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
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('[data-variant]').length")!="3") throw new Exception("Relax sub-item list should have 3 items.");
                string[] relaxIds={"5yApImDt_Fk","srz5h0UQ6tc","Yma-nUogjao"};
                for (int v=0; v<3; v++)
                {
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-variant=\""+v+"\"]').click()");
                    await Task.Delay(300);
                    string relaxSrc=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!relaxSrc.Contains("youtube-nocookie.com/embed/"+relaxIds[v]) || !relaxSrc.Contains("autoplay=1")) throw new Exception("Relax item #"+(v+1)+" did not embed its autoplaying video: "+relaxSrc);
                    if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image, .instruction-panel'))")!="false") throw new Exception("Relax should show only the video.");
                }
                Popup.Close(); await Task.Delay(300);
                result["relaxVideoOnly"]=true;
                // 요리 레시피: videos only (two), started by itself; no picture and no step list.
                ShowPopup("recipe"); await Popup.Ready.Task; await Task.Delay(500);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('[data-variant]').length")!="5") throw new Exception("Recipe sub-item list should have 5 items.");
                string[] recipeIds={"s10KwYn064E","Rjfzpzj3bug","kLT591PuXB4","X8oI4ULOELE","iW92Cxhg9h0"};
                for (int v=0; v<5; v++)
                {
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-variant=\""+v+"\"]').click()");
                    await Task.Delay(300);
                    string recipeSrc=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''");
                    if (!recipeSrc.Contains("youtube-nocookie.com/embed/"+recipeIds[v]) || !recipeSrc.Contains("autoplay=1")) throw new Exception("Recipe item #"+(v+1)+" did not embed its autoplaying video: "+recipeSrc);
                    // Vertical Shorts repeat by themselves (loop=1&playlist=<id>); wide videos play once.
                    if (recipeSrc.Contains("loop=1&playlist="+recipeIds[v]) != (v!=2)) throw new Exception("Recipe item #"+(v+1)+" has the wrong repeat setting: "+recipeSrc);
                    if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image, .instruction-panel'))")!="false") throw new Exception("Recipe should show only the video.");
                }
                Popup.Close(); await Task.Delay(300);
                result["recipeVideoOnly"]=true;
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
                if (await script.ExecuteScriptAsync("document.querySelector('#share-panel').open") != "false") throw new Exception("Share dialog should be closed until its button is clicked.");
                await script.ExecuteScriptAsync("document.querySelector('#share-open').click()");
                await Task.Delay(300);
                if (await script.ExecuteScriptAsync("document.querySelector('#share-panel').open") != "true") throw new Exception("Share button did not open the dialog.");
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
                    await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-variant=\""+v+"\"]').click()");
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
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('[data-delete]').length")!="2") throw new Exception("Each of the two shared tiles should offer a delete button.");
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[value=stretch]').closest('.theme-card').querySelector('[data-delete]')===null && document.querySelector('#share-preview [data-delete]')===null")!="true") throw new Exception("Built-in tiles and the share preview must not offer deletion.");
                // The chosen icon is the tile's symbol (shared items default to ✿).
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[value=\"c-u-test-video\"]').closest('.theme-card').querySelector('.theme-symbol').textContent")!="\"♪\"") throw new Exception("The shared item's icon is not shown on its tile.");
                if (await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[value=\"c-u-eye-20-20-20\"]').closest('.theme-card').querySelector('.theme-symbol').textContent")!="\"✿\"") throw new Exception("A shared item without an icon should keep the default symbol.");
                ShowPopup("c-u-test-video"); await Popup.Ready.Task; await Task.Delay(500);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('[data-variant]').length")!="2") throw new Exception("A shared item with one video should list its steps plus the video.");
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-variant=\"1\"]').click()");
                await Task.Delay(300);
                if (!(await Popup.Browser.CoreWebView2.ExecuteScriptAsync("(document.querySelector('#variant-frame')||{}).src||''")).Contains("youtube-nocookie.com/embed/x6y6SPzCQXQ")) throw new Exception("The shared video did not embed.");
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-variant=\"0\"]').click()");
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
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-variant=\"2\"]').click()");
                await Task.Delay(300);
                await Capture(Popup,Path.Combine(folder,"dance-player.png"));
                Popup.Close();
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
        public readonly WebView2 Browser;
        public readonly TaskCompletionSource<bool> Ready=new TaskCompletionSource<bool>();
        public WebWindow(ReminderHost owner, bool popup, string themeId)
        {
            host=owner; IsPopup=popup; ThemeId=themeId;
            Text=popup?"틈 · 따라 하는 작은 쉼":"틈 · 알림 간격과 주제 설정";
            BackColor=ColorTranslator.FromHtml("#f4f3ed");
            Icon=SystemIcons.Information;
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
                else { MessageBox.Show("화면을 열 수 없습니다. Microsoft Edge WebView2 Runtime을 확인하세요.\n"+error.Message,"틈"); Close(); }
            }
        }
        public void Send(object payload)
        {
            if(Browser.CoreWebView2!=null && !IsDisposed) Browser.CoreWebView2.PostWebMessageAsJson(host.Json.Serialize(payload));
        }
    }
}
