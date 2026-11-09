using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        public string id, name, summary, safety, file;
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
        public readonly Theme[] Themes;
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
            Themes = list.ToArray();
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
                settings=Settings, themes=Themes, next=NextDue.ToString("o"), now=DateTime.Now.ToString("o"), notice=notice, testing=Testing });
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
                if (action=="preview") { ShowPopup(Convert.ToString(msg["themeId"])); return; }
                if (action=="exit") { if (!source.IsPopup) Quit(); return; }
                if (action=="open-link")
                {
                    Uri uri;
                    if (!Uri.TryCreate(Convert.ToString(msg["url"]),UriKind.Absolute,out uri) || uri.Scheme!="https" || !(uri.Host=="www.youtube.com" || uri.Host=="youtube.com" || uri.Host=="youtu.be" || uri.Host=="ko.wikisource.org")) throw new ArgumentException("허용된 공식 영상·원문 링크만 열 수 있습니다.");
                    if (source.IsPopup) source.TopMost=false;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute=true }); return;
                }
            }
            catch (Exception error) { source.Send(new { type="error", message=error.Message }); }
        }

        public void Quit()
        {
            Exiting=true; timer.Stop(); timer.Dispose();
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
                string count=await SettingsWindow.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.theme-card').length");
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
