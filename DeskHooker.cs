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
        public string id, name, summary, safety, file, author;
        public bool community;
        public string[] instructions;
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
        private DateTime lastTick = DateTime.Now;
        private string notice = "간격과 주제를 고른 뒤 알림 시작을 누르세요.";

        public ReminderHost(bool testing, bool background)
        {
            Testing = testing;
            dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), testing ? "DeskHooker-Test" : "DeskHooker");
            Directory.CreateDirectory(dataFolder);
            var themes = Json.Deserialize<Theme[]>(File.ReadAllText(Path.Combine(Root, "themes.json")));
            var list = new List<Theme>(themes);
            list.Add(new Theme { id="dance", name="걸그룹 안무", summary="공식 안무 영상으로 원본 동작 따라 보기", file="", safety="서서 하는 안무입니다. 충분한 공간을 확보하고 어려운 점프·비틀기는 건너뛰세요. 허리 통증이 있으면 이 주제 대신 저강도 자세 전환을 고르세요.", instructions=new string[] { "공식 채널의 연습 영상을 선택하세요.", "처음에는 느리게 보고, 편한 범위에서만 따라 하세요." } });
            list.Add(new Theme { id="poem", name="시 한 줄 채우기", summary="익숙한 서정시에서 사라진 한 줄을 기억해 보세요", file="", safety="정답은 선택한 공개 원문 판본 기준입니다. 띄어쓰기는 무시하지만 단어와 문장부호는 비교합니다.", instructions=new string[] { "빈 한 줄에 들어갈 문장을 입력하세요.", "제출하면 내 답과 원문을 나란히 비교할 수 있어요." } });
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
            NextDue = ReminderSchedule.Next(DateTime.Now, Settings);
            tray = new NotifyIcon { Icon=SystemIcons.Information, Text="틈 · 스트레칭과 작은 쉼", Visible=true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("간격·주제 설정", null, delegate { ShowSettings(); });
            menu.Items.Add("지금 한 번 보기", null, delegate { ShowPopup(Settings.ThemeIds[0]); });
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
                        safety = item.Safety + " " + CommunityNotice, file = "", author = item.Author, community = true });
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
            cursor = 0; Persist();
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

        public void PopupClosed(WebWindow window)
        {
            if (Popup==window) Popup=null;
            if (!Exiting) { NextDue=ReminderSchedule.Next(DateTime.Now,Settings); UpdateStatus(); }
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
            if (ReminderSchedule.IsDue(now,NextDue,Settings,Popup!=null,false,!locked && Environment.UserInteractive))
            {
                ShowPopup(ReminderSchedule.NextTheme(Settings.ThemeIds,ref cursor));
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
                settings=Settings, themes=Themes, sync=SyncInfo(), next=NextDue.ToString("o"), now=DateTime.Now.ToString("o"), notice=notice, testing=Testing });
        }

        public void Handle(WebWindow source, string message)
        {
            try
            {
                var msg=Json.Deserialize<Dictionary<string,object>>(message);
                string action=Convert.ToString(msg["action"]);
                if (action=="ready") { SendState(source); if (Testing && !source.IsPopup && !smokeStarted) { smokeStarted=true; RunSmoke(); } return; }
                if (action=="close") { if (source.IsPopup) source.Close(); else source.Hide(); return; }
                if (action=="save")
                {
                    if (source.IsPopup) throw new InvalidOperationException("설정 창에서만 변경할 수 있습니다.");
                    var settings=Json.Deserialize<ReminderSettings>(Json.Serialize(msg["settings"]));
                    ReminderSchedule.Validate(settings,Themes.Select(t => t.id));
                    bool consent=msg.ContainsKey("holdConsent") && Convert.ToBoolean(msg["holdConsent"]);
                    if (settings.HoldSeconds>0 && !consent) throw new ArgumentException("숨참기는 선택 사항입니다. 주의사항을 확인하거나 멈춤을 0초로 바꾸세요.");
                    bool previousStartup=Settings.StartAtLogin;
                    Settings=settings;
                    if (Settings.StartAtLogin!=previousStartup) Startup(Settings.StartAtLogin);
                    Persist(); cursor=0; NextDue=ReminderSchedule.Next(DateTime.Now,Settings);
                    notice=Settings.Enabled?"알림이 시작됐습니다. 창을 닫아도 트레이에서 계속 동작합니다.":"설정이 저장됐습니다. 알림은 일시정지 상태입니다.";
                    SendState(source); return;
                }
                if (action=="sync") { if (!source.IsPopup) StartSync(true); return; }
                if (action=="preview") { ShowPopup(Convert.ToString(msg["themeId"])); return; }
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
            if (uri.Scheme!="https") return false;
            if (uri.Host=="www.youtube.com" || uri.Host=="youtube.com" || uri.Host=="youtu.be" || uri.Host=="ko.wikisource.org") return true;
            // Sharing may only open GitHub's "new file" page for this project's community folder.
            return uri.Host=="github.com" && uri.AbsolutePath.StartsWith("/panicdna/kinds-of-thing/new/main/community", StringComparison.Ordinal);
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
                await Task.Delay(700);
                string count=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#theme-grid .theme-card').length");
                if (Int32.Parse(count)!=Themes.Length) throw new Exception("Theme cards are missing.");
                result["themeCards"]=Themes.Length;
                await Capture(SettingsWindow,Path.Combine(folder,"settings.png"));
                await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#interval').value='30'; document.querySelectorAll('input[name=theme]').forEach(x=>x.checked=['meditation','poem'].includes(x.value)); document.querySelector('#start-reminders').click();");
                await Task.Delay(400);
                if (Settings.IntervalMinutes!=30 || !Settings.Enabled || !Settings.ThemeIds.SequenceEqual(new string[]{"meditation","poem"})) throw new Exception("Saving interval/theme settings failed.");
                result["settingsRoundTrip"]=true;
                ShowPopup("stretch"); await Popup.Ready.Task; await Task.Delay(700);
                if (Popup.Bounds!=Screen.PrimaryScreen.Bounds) throw new Exception("Popup is not fullscreen.");
                var active=Popup;
                string loaded=await active.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.motion-image') && document.querySelector('.motion-image').naturalWidth>0)");
                if (loaded!="true") throw new Exception("Animated GIF failed to load.");
                await Capture(active,Path.Combine(folder,"popup-stretch.png"));
                await active.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.popup-close').click()");
                await Task.Delay(350);
                if (Popup!=null || !Settings.Enabled || NextDue<=DateTime.Now) throw new Exception("Closing popup stopped background reminders or failed to rearm.");
                result["fullscreenGifAndDismiss"]=true;
                ShowPopup("poem"); await Popup.Ready.Task; await Task.Delay(350);
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#poem-answer').value=window.teumGameQuestion.expected; document.querySelector('#poem-submit').click();");
                await Task.Delay(150);
                string correct=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#poem-result').dataset.correct");
                if (correct!="\"true\"") throw new Exception("Poem answer comparison failed.");
                await Capture(Popup,Path.Combine(folder,"poem-answer.png"));
                Popup.Close();
                result["poemInputComparison"]=true;
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
                await script.ExecuteScriptAsync("var d=document.querySelector('#share-panel'); d.open=true; document.querySelector('.shared-badge').scrollIntoView({block:'start'});");
                await Task.Delay(300);
                await Capture(SettingsWindow, Path.Combine(folder, "settings-community.png"));
                ShowPopup("c-u-eye-20-20-20"); await Popup.Ready.Task; await Task.Delay(350);
                if (await Popup.Browser.CoreWebView2.ExecuteScriptAsync("Boolean(document.querySelector('.text-stage')) && !document.querySelector('.motion-image') && document.querySelectorAll('.instruction-panel li').length===3") != "true") throw new Exception("Community popup did not render.");
                Popup.Close();
                result["communitySyncAndShare"] = true;
                ShowPopup("breathing"); await Popup.Ready.Task; await Task.Delay(350);
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#breath-start').click()");
                await Task.Delay(150);
                string started=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#breath-phase').textContent.includes('들이')");
                if (started!="true") throw new Exception("Breathing timer did not start.");
                await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#breath-stop').click()");
                string stopped=await Popup.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#breath-phase').textContent");
                if (!stopped.Contains("편안한")) throw new Exception("Breathing timer did not stop.");
                result["stoppableBreathingTimer"]=true;
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

        // Returns the HTTP status (200/304/404/...), or -1 when the server could not be reached.
        private static int TryGet(string url, string etag, int maxBytes, out string text, out string responseEtag)
        {
            text = null; responseEtag = null;
            try { text = System.Text.Encoding.UTF8.GetString(GetBytes(url, etag, maxBytes, out responseEtag)); return 200; }
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
            using (var stream = response.GetResponseStream())
            {
                responseEtag = response.Headers["ETag"];
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
                var environment=await CoreWebView2Environment.CreateAsync(null,host.ProfilePath);
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
