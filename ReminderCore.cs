using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskHooker
{
    // One quiet period inside the day (e.g. lunch): reminders that would fall in
    // [Start, End) wait until it ends. Plain get/set properties support JavaScriptSerializer.
    public class BreakWindow
    {
        public int StartHour { get; set; }
        public int StartMinute { get; set; }
        public int EndHour { get; set; }
        public int EndMinute { get; set; }
    }

    // Plain get/set properties and constructor defaults support JavaScriptSerializer.
    // Constructing or validating settings never enables reminders or OS startup.
    public class ReminderSettings
    {
        public int IntervalMinutes { get; set; }
        public bool WeekdaysOnly { get; set; }
        public bool LimitHours { get; set; }
        public int StartHour { get; set; }
        public int EndHour { get; set; }
        public int StartMinute { get; set; }
        public int EndMinute { get; set; }
        public string[] ThemeIds { get; set; }
        public bool Enabled { get; set; }
        public bool StartAtLogin { get; set; }
        public int InhaleSeconds { get; set; }
        public int HoldSeconds { get; set; }
        public int ExhaleSeconds { get; set; }
        // How several selected themes are used per reminder; see ThemeRotation.
        public string ThemeMode { get; set; }
        // Per card (theme id): how that card's sub-items are picked, "random" (default when absent) or "sequential".
        public Dictionary<string, string> VariantModes { get; set; }
        // Per card (theme id): whether that card's video replays from the start when it ends,
        // "on" (loop) or "off" (default when absent — play once and stop). Pure presentation.
        public Dictionary<string, string> LoopModes { get; set; }
        // Per card (theme id): an absolute daily run time, "H:MM" or "H:MM:SS" (00:00:00-23:59:59;
        // seconds optional so old "H:MM" configs keep loading). A card listed here
        // runs once a day at that clock time instead of taking part in the periodic rotation.
        public Dictionary<string, string> Schedules { get; set; }
        // Quiet periods inside the day (e.g. lunch): reminders that would fall inside any of them
        // wait until that period ends. BreakEnabled is the master switch for the whole list.
        // A null list means "old settings file" — the legacy single-period fields below are used
        // instead; an empty list means "the feature is on but no period is set".
        public List<BreakWindow> Breaks { get; set; }
        // Legacy single quiet period from older settings files; only read while Breaks is null.
        public bool BreakEnabled { get; set; }
        public int BreakStartHour { get; set; }
        public int BreakStartMinute { get; set; }
        public int BreakEndHour { get; set; }
        public int BreakEndMinute { get; set; }
        // Knox 캘린더 일정 알림: knox-ics-export가 써 둔 ics 파일을 읽어, 일정 시작 전 LeadMinutes분에 별도 팝업을 띄운다.
        // 근무시간/요일/점심 쉬는시간 규칙(ReminderSchedule.IsAllowed)의 대상이 아니다.
        public bool KnoxCalendarEnabled { get; set; }
        public int KnoxLeadMinutes { get; set; }
        // When a meeting-alert popup closes itself: none (the user closes it), start (at the meeting's
        // start time), after (KnoxAutoCloseMinutes past the start), or end (at the meeting's end time,
        // only when the calendar carries an end). Pure presentation timing; nothing else reads it.
        public string KnoxAutoClose { get; set; }
        public int KnoxAutoCloseMinutes { get; set; }
        // Knox 수신 메일 회의 초대 알림: 켜면 POP3(pop3.samsung.net)로 오늘 받은 "[지원WP] [Invitation]"
        // 초대 메일을 읽어(비밀번호는 Windows 자격 증명 관리자에만 저장된다) 회의를 Knox 캘린더 일정
        // 알림에 병합한다. 비밀번호는 settings.json 에 절대 실리지 않는다.
        public bool KnoxMailEnabled { get; set; }
        // 373: 회의 시작 전 알림을 SLSI Avatar 메신저(내 1:1 방)로도 한 통 보낸다 — 보내기 전용.
        // 연결(SSO 로그인으로 받은 데스크톱 토큰)은 Windows 자격 증명 관리자에만 저장된다.
        // 자리비움·화면 잠금과 무관하게 보낸다(자리에 없을 때 쓰라고 있는 알림이다).
        public bool MessengerNotifyEnabled { get; set; }
        // "Away from the desk": when on, no popup of any kind (break or meeting alert) opens while the
        // mouse/keyboard have been silent for IdleMinutes — the user is treated as out. An already
        // open popup stays; only new popups are held back.
        public bool PauseWhenIdle { get; set; }
        public int IdleMinutes { get; set; }
        // The whole-app color scheme: light (the original paper look), dark (near-black), or system
        // (follow the Windows dark/light mode at render time). Pure presentation; nothing else reads it.
        public string Appearance { get; set; }

        public ReminderSettings()
        {
            IntervalMinutes = 60;
            WeekdaysOnly = true;
            LimitHours = true;
            StartHour = 9;
            EndHour = 18;
            StartMinute = 0;
            EndMinute = 0;
            ThemeIds = new string[] { "stretch" };
            Enabled = false;
            StartAtLogin = false;
            InhaleSeconds = 4;
            HoldSeconds = 0;
            ExhaleSeconds = 6;
            ThemeMode = ThemeRotation.OrderedOne;
            VariantModes = new Dictionary<string, string>();
            LoopModes = new Dictionary<string, string>();
            Schedules = new Dictionary<string, string>();
            BreakEnabled = false;
            BreakStartHour = 12; BreakStartMinute = 0;
            BreakEndHour = 13; BreakEndMinute = 0;
            KnoxCalendarEnabled = false;
            KnoxLeadMinutes = 5;
            KnoxAutoClose = KnoxAutoCloseNone;
            KnoxAutoCloseMinutes = 5;
            KnoxMailEnabled = false;
            MessengerNotifyEnabled = false;
            PauseWhenIdle = false;
            IdleMinutes = 10;
            Appearance = AppearanceSystem;
        }

        // The first-run "기본 프리셋" (303~308): what a fresh install starts with. The constructor above
        // stays neutral (blank slate) so corrupted-settings recovery and unit tests keep a safe baseline.
        public static ReminderSettings DefaultPreset()
        {
            ReminderSettings s = new ReminderSettings();
            // 01 간격: 09:00부터 60분마다 · 02: 18:00 전까지, 평일만, 점심 12:00-13:00 쉬는 시간.
            s.StartAtLogin = true;                 // Windows 로그인 때 자동 실행
            s.BreakEnabled = true;                 // 점심시간 알림 끄기 (12:00-13:00)
            s.Breaks = new List<BreakWindow> { new BreakWindow { StartHour = 12, StartMinute = 0, EndHour = 13, EndMinute = 0 } };
            s.PauseWhenIdle = true;                // 10분 자리비움 알림 멈춤 (IdleMinutes 기본 10)
            s.KnoxCalendarEnabled = true;          // Knox 캘린더 일정 알림
            s.KnoxLeadMinutes = 5;                 // 시작 5분 전에
            s.KnoxAutoClose = KnoxAutoCloseNone;   // 일정 알림 자동 닫힘 없음
            // 03 카드: 4개를 무작위로 하나씩 — 스트레칭·명상·발성·허리 (337: shim-cards-20261113-2228.json
            // 내보내기 프리셋 기준. 릴렉스는 목록에 남지만 선택에서 빠진다. 댄스 안무도 제외(310)).
            s.ThemeIds = new string[] { "stretch", "meditation", "voice", "back-standing" };
            s.ThemeMode = ThemeRotation.RandomOne;
            // 퇴근버스 카드: 17:00에 하루 한 번 (주기 실행에는 포함되지 않는다).
            s.Schedules = new Dictionary<string, string> { { "bus-home", "17:00:00" } };
            return s;
        }

        public const string VariantRandom = "random", VariantSequential = "sequential";
        public static bool IsValidVariantMode(string mode) { return mode == VariantRandom || mode == VariantSequential; }

        public const string LoopOn = "on", LoopOff = "off";
        public static bool IsValidLoopMode(string mode) { return mode == LoopOn || mode == LoopOff; }

        // A card's absolute run time, as written by the settings window: "H:MM" or "H:MM:SS"
        // (seconds optional, so old "H:MM" configs keep loading; a missing ":SS" reads as :00),
        // 00:00:00-23:59:59. Returns seconds since midnight.
        public static bool TryParseClock(string clock, out int secondsSinceMidnight)
        {
            secondsSinceMidnight = 0;
            if (String.IsNullOrEmpty(clock)) return false;
            string[] parts = clock.Split(':');
            if (parts.Length < 2 || parts.Length > 3 || parts[0].Length < 1 || parts[0].Length > 2 || parts[1].Length != 2 || (parts.Length == 3 && parts[2].Length != 2)) return false;
            int hour, minute, second = 0;
            if (!Int32.TryParse(parts[0], out hour) || !Int32.TryParse(parts[1], out minute)) return false;
            if (parts.Length == 3 && !Int32.TryParse(parts[2], out second)) return false;
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59 || second < 0 || second > 59) return false;
            secondsSinceMidnight = hour * 3600 + minute * 60 + second;
            return true;
        }

        public const string KnoxAutoCloseNone = "none", KnoxAutoCloseStart = "start", KnoxAutoCloseAfter = "after", KnoxAutoCloseEnd = "end";
        public static bool IsValidKnoxAutoClose(string mode)
        {
            return mode == KnoxAutoCloseNone || mode == KnoxAutoCloseStart || mode == KnoxAutoCloseAfter || mode == KnoxAutoCloseEnd;
        }

        public const string AppearanceLight = "light", AppearanceDark = "dark", AppearanceSystem = "system";
        public static bool IsValidAppearance(string appearance)
        {
            return appearance == AppearanceLight || appearance == AppearanceDark || appearance == AppearanceSystem;
        }
    }

    public static class ReminderSchedule
    {
        // Call after deserialization and before using settings. Does not mutate them.
        // Theme IDs are exact, ordinal, case-sensitive catalog identifiers.
        public static void Validate(ReminderSettings settings, IEnumerable<string> knownThemeIds)
        {
            if (settings == null)
                throw new ArgumentException("Settings are required.", "settings");
            if (knownThemeIds == null)
                throw new ArgumentException("Known theme IDs are required.", "knownThemeIds");
            if (settings.IntervalMinutes < 1 || settings.IntervalMinutes > 240)
                throw new ArgumentException("IntervalMinutes must be between 1 and 240.", "settings");
            if (settings.StartHour < 0 || settings.StartHour > 23)
                throw new ArgumentException("StartHour must be between 0 and 23.", "settings");
            if (settings.StartMinute < 0 || settings.StartMinute > 59 || settings.EndMinute < 0 || settings.EndMinute > 59)
                throw new ArgumentException("StartMinute and EndMinute must be between 0 and 59.", "settings");
            if (settings.EndHour < 1 || settings.EndHour > 24 || (settings.EndHour == 24 && settings.EndMinute != 0) ||
                WindowEnd(settings) <= WindowStart(settings))
                throw new ArgumentException("End time must be later than start time and not after 24:00.", "settings");
            if (settings.BreakEnabled)
            {
                List<BreakWindow> breaks = EffectiveBreaks(settings);
                if (breaks.Count > 8)
                    throw new ArgumentException("At most 8 quiet periods can be set.", "settings");
                foreach (BreakWindow b in breaks)
                {
                    if (b == null)
                        throw new ArgumentException("Quiet periods must not be empty entries.", "settings");
                    if (b.StartHour < 0 || b.StartHour > 23 || b.StartMinute < 0 || b.StartMinute > 59 ||
                        b.EndMinute < 0 || b.EndMinute > 59 || b.EndHour < 0 || b.EndHour > 24 ||
                        (b.EndHour == 24 && b.EndMinute != 0))
                        throw new ArgumentException("Quiet-period times must be 00:00-23:59 (the end may be 24:00).", "settings");
                    if (End(b) <= Start(b))
                        throw new ArgumentException("Every quiet period must end after it starts.", "settings");
                }
                // Quiet periods that together swallow the whole allowed time would leave no moment to
                // remind, so merge overlapping/adjacent ones and look for a gap.
                TimeSpan allowedStart = settings.LimitHours ? WindowStart(settings) : TimeSpan.Zero;
                TimeSpan allowedEnd = settings.LimitHours ? WindowEnd(settings) : TimeSpan.FromHours(24);
                TimeSpan[] starts = new TimeSpan[breaks.Count], ends = new TimeSpan[breaks.Count];
                for (int i = 0; i < breaks.Count; i++) { starts[i] = Start(breaks[i]); ends[i] = End(breaks[i]); }
                Array.Sort(starts, ends);
                TimeSpan cursor = allowedStart;
                bool gap = false;
                for (int i = 0; i < starts.Length; i++)
                {
                    if (ends[i] <= cursor) continue;
                    if (starts[i] > cursor) { gap = true; break; }
                    cursor = ends[i];
                }
                if (!gap && cursor >= allowedEnd)
                    throw new ArgumentException("The quiet periods must leave some time for reminders.", "settings");
            }
            if (settings.KnoxLeadMinutes < 0 || settings.KnoxLeadMinutes > 60)
                throw new ArgumentException("KnoxLeadMinutes must be between 0 and 60.", "settings");
            if (!ReminderSettings.IsValidKnoxAutoClose(settings.KnoxAutoClose))
                throw new ArgumentException("KnoxAutoClose must be none, start, after or end.", "settings");
            if (settings.KnoxAutoCloseMinutes < 1 || settings.KnoxAutoCloseMinutes > 240)
                throw new ArgumentException("KnoxAutoCloseMinutes must be between 1 and 240.", "settings");
            if (settings.IdleMinutes < 1 || settings.IdleMinutes > 240)
                throw new ArgumentException("IdleMinutes must be between 1 and 240.", "settings");
            if (!ReminderSettings.IsValidAppearance(settings.Appearance))
                throw new ArgumentException("Appearance must be light, dark or system.", "settings");
            if (settings.InhaleSeconds < 1 || settings.InhaleSeconds > 10)
                throw new ArgumentException("InhaleSeconds must be between 1 and 10.", "settings");
            if (settings.HoldSeconds < 0 || settings.HoldSeconds > 30)
                throw new ArgumentException("HoldSeconds must be between 0 and 30.", "settings");
            if (settings.ExhaleSeconds < 1 || settings.ExhaleSeconds > 30)
                throw new ArgumentException("ExhaleSeconds must be between 1 and 30.", "settings");
            if (!ThemeRotation.IsValidMode(settings.ThemeMode))
                throw new ArgumentException("ThemeMode must be sequential-all, random-all, ordered-one or random-one.", "settings");
            if (settings.VariantModes != null)
            {
                if (settings.VariantModes.Count > 64)
                    throw new ArgumentException("VariantModes has too many entries.", "settings");
                foreach (KeyValuePair<string, string> entry in settings.VariantModes)
                    if (String.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length > 64 || !ReminderSettings.IsValidVariantMode(entry.Value))
                        throw new ArgumentException("VariantModes must map a theme id to random or sequential.", "settings");
            }
            if (settings.LoopModes != null)
            {
                if (settings.LoopModes.Count > 64)
                    throw new ArgumentException("LoopModes has too many entries.", "settings");
                foreach (KeyValuePair<string, string> entry in settings.LoopModes)
                    if (String.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length > 64 || !ReminderSettings.IsValidLoopMode(entry.Value))
                        throw new ArgumentException("LoopModes must map a theme id to on or off.", "settings");
            }
            // "몇 시에 실행" cards: { theme id → "H:MM" or "H:MM:SS" }. They must name real cards, carry a
            // valid clock time, and never double as periodic selections (the UI greys the other way out).
            if (settings.Schedules != null)
            {
                if (settings.Schedules.Count > 64)
                    throw new ArgumentException("Schedules has too many entries.", "settings");
                foreach (KeyValuePair<string, string> entry in settings.Schedules)
                {
                    int at;
                    if (String.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length > 64 || !ReminderSettings.TryParseClock(entry.Value, out at))
                        throw new ArgumentException("Schedules must map a theme id to a time of day (HH:MM or HH:MM:SS, 00:00:00-23:59:59).", "settings");
                }
            }
            // At least one way of running must be chosen: a periodic selection, or a card with a time.
            if (settings.ThemeIds == null ||
                (settings.ThemeIds.Length == 0 && (settings.Schedules == null || settings.Schedules.Count == 0)))
                throw new ArgumentException("At least one theme must be selected.", "settings");
            HashSet<string> known = new HashSet<string>(knownThemeIds, StringComparer.Ordinal);
            HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (string theme in settings.ThemeIds)
            {
                if (String.IsNullOrWhiteSpace(theme) || !known.Contains(theme) || !selected.Add(theme))
                    throw new ArgumentException("ThemeIds must contain distinct, nonempty, known IDs.", "settings");
            }
            if (settings.Schedules != null)
            {
                foreach (KeyValuePair<string, string> entry in settings.Schedules)
                {
                    if (!known.Contains(entry.Key))
                        throw new ArgumentException("Schedules must name known theme IDs.", "settings");
                    if (selected.Contains(entry.Key))
                        throw new ArgumentException("A card runs periodically or at a set time, never both.", "settings");
                }
            }
        }

        private static TimeSpan WindowStart(ReminderSettings s) { return new TimeSpan(s.StartHour, s.StartMinute, 0); }
        private static TimeSpan WindowEnd(ReminderSettings s) { return new TimeSpan(s.EndHour, s.EndMinute, 0); }
        private static TimeSpan Start(BreakWindow b) { return new TimeSpan(b.StartHour, b.StartMinute, 0); }
        private static TimeSpan End(BreakWindow b) { return new TimeSpan(b.EndHour, b.EndMinute, 0); }
        // The quiet periods to apply: the Breaks list when the settings carry one (an empty list is
        // "nothing set"), otherwise the legacy single-period fields of an older settings file.
        private static List<BreakWindow> EffectiveBreaks(ReminderSettings s)
        {
            if (s.Breaks != null) return s.Breaks;
            return new List<BreakWindow> { new BreakWindow { StartHour = s.BreakStartHour, StartMinute = s.BreakStartMinute, EndHour = s.BreakEndHour, EndMinute = s.BreakEndMinute } };
        }
        // The first instant at or after timeOfDay that no enabled quiet period covers: the end of the
        // latest period containing timeOfDay, or timeOfDay itself when it is outside every period.
        private static TimeSpan ExitBreak(ReminderSettings s, TimeSpan timeOfDay)
        {
            TimeSpan exit = timeOfDay;
            if (!s.BreakEnabled) return exit;
            foreach (BreakWindow b in EffectiveBreaks(s))
                if (timeOfDay >= Start(b) && timeOfDay < End(b) && End(b) > exit) exit = End(b);
            return exit;
        }
        private static bool InBreak(ReminderSettings s, TimeSpan timeOfDay) { return ExitBreak(s, timeOfDay) != timeOfDay; }

        // Schedule-only predicate: Enabled is checked separately by IsDue.
        // All supplied DateTimes must use the caller's consistent wall-clock basis.
        public static bool IsAllowed(DateTime when, ReminderSettings settings)
        {
            if (settings.WeekdaysOnly &&
                (when.DayOfWeek == DayOfWeek.Saturday || when.DayOfWeek == DayOfWeek.Sunday))
                return false;
            if (InBreak(settings, when.TimeOfDay))
                return false;
            return !settings.LimitHours ||
                (when.TimeOfDay >= WindowStart(settings) && when.TimeOfDay < WindowEnd(settings));
        }

        // Stateless: mark popupOpen before the next timer tick. sessionActive=false
        // represents a locked/inactive session. No missed-popup queue is maintained.
        public static bool IsDue(DateTime now, DateTime next, ReminderSettings settings,
            bool popupOpen, bool paused, bool sessionActive)
        {
            return settings.Enabled && !popupOpen && !paused && sessionActive &&
                now >= next && IsAllowed(now, settings);
        }

        // "Away from the desk" gate: true when the away feature is on and the last mouse/keyboard
        // input is at least IdleMinutes old. Pure time arithmetic; the caller supplies both clocks.
        public static bool IsAway(TimeSpan idleFor, ReminderSettings settings)
        {
            if (settings == null)
                throw new ArgumentException("Settings are required.", "settings");
            if (!settings.PauseWhenIdle)
                return false;
            return idleFor >= TimeSpan.FromMinutes(settings.IdleMinutes);
        }

        // cursor is the zero-based index of the next item; initialize it to zero.
        public static string NextTheme(string[] selected, ref int cursor)
        {
            if (selected == null || selected.Length == 0)
                throw new ArgumentException("At least one theme must be selected.", "selected");
            int index = cursor < 0 ? 0 : cursor % selected.Length;
            string theme = selected[index];
            cursor = (index + 1) % selected.Length;
            return theme;
        }

        // Absolute timetable: the day's slots sit at start + k×interval (anchor = StartHour:StartMinute,
        // continuing around the clock when the time window is off). Next(from) is the first slot
        // STRICTLY AFTER "from", then clamped to the earliest allowed instant. Because the slots are
        // fixed to the wall clock, dismissing a popup late never drifts the schedule: 09:00 with a
        // 60-minute interval stays 10:00, 11:00, 12:00 whatever the close time was.
        // No clock reads, timers, startup registration, or other side effects.
        public static DateTime Next(DateTime from, ReminderSettings settings)
        {
            DateTime anchor = from.Date + WindowStart(settings);
            long interval = TimeSpan.FromMinutes(settings.IntervalMinutes).Ticks;
            long diff = from.Ticks - anchor.Ticks;
            long steps = diff / interval;                                   // truncates toward zero
            if (diff % interval != 0 && diff < 0) steps -= 1;               // floor for before-anchor times
            DateTime candidate = anchor.AddTicks((steps + 1) * interval);   // first slot strictly after "from"
            // Each rule can push the time into another rule's forbidden zone (a break ending after the window,
            // a weekend, a second break chained after the first...), so repeat until nothing moves. Validate()
            // guarantees that some allowed time exists, so this ends quickly.
            int maxPasses = 16 + (settings.Breaks == null ? 0 : 4 * settings.Breaks.Count);
            for (int pass = 0; pass < maxPasses; pass++)
            {
                DateTime before = candidate;
                if (settings.LimitHours && candidate.TimeOfDay >= WindowEnd(settings))
                    candidate = candidate.Date.AddDays(1) + WindowStart(settings);
                if (settings.LimitHours && candidate.TimeOfDay < WindowStart(settings))
                    candidate = candidate.Date + WindowStart(settings);
                TimeSpan exit = ExitBreak(settings, candidate.TimeOfDay);
                if (exit != candidate.TimeOfDay)
                    candidate = candidate.Date + exit;
                while (settings.WeekdaysOnly &&
                    (candidate.DayOfWeek == DayOfWeek.Saturday || candidate.DayOfWeek == DayOfWeek.Sunday))
                    candidate = candidate.Date.AddDays(1) + (settings.LimitHours ? WindowStart(settings) : TimeSpan.Zero);
                if (candidate == before)
                    break;
            }
            return candidate;
        }
    }
    // Decides which of the selected themes one reminder shows, and in what order.
    // Kept apart from ReminderSchedule so that class keeps its small public surface.
    public static class ThemeRotation
    {
        public const string SequentialAll = "sequential-all"; // every theme, in list order, back to back
        public const string RandomAll = "random-all";         // every theme, shuffled, back to back
        public const string OrderedOne = "ordered-one";       // one theme per reminder, in list order (default)
        public const string RandomOne = "random-one";         // one random theme per reminder

        public static bool IsValidMode(string mode)
        {
            return mode == SequentialAll || mode == RandomAll || mode == OrderedOne || mode == RandomOne;
        }

        // Returns the themes to show for one reminder, first to last. cursor is only used and advanced
        // by OrderedOne. previous (may be null) is the theme shown last; random modes avoid starting
        // with it when there is a choice, so the same tile is not repeated back to back.
        public static string[] PlanRound(string[] selected, string mode, ref int cursor, Random random, string previous)
        {
            if (selected == null || selected.Length == 0)
                throw new ArgumentException("At least one theme must be selected.", "selected");
            if (!IsValidMode(mode))
                throw new ArgumentException("Unknown theme mode.", "mode");
            if (random == null)
                throw new ArgumentException("A random source is required.", "random");
            if (mode == OrderedOne)
                return new string[] { ReminderSchedule.NextTheme(selected, ref cursor) };
            if (mode == SequentialAll)
                return (string[])selected.Clone();
            string[] order = (string[])selected.Clone();
            if (mode == RandomOne)
            {
                List<string> pool = new List<string>(order);
                if (pool.Count > 1) pool.RemoveAll(delegate(string id) { return id == previous; });
                return new string[] { pool[random.Next(pool.Count)] };
            }
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                string swap = order[i]; order[i] = order[j]; order[j] = swap;
            }
            if (order.Length > 1 && order[0] == previous)
            {
                int j = 1 + random.Next(order.Length - 1);
                string swap = order[0]; order[0] = order[j]; order[j] = swap;
            }
            return order;
        }
    }

    // One Knox calendar event, as parsed from the ics file knox sync writes to disk.
    // EndLocal is null when the event carries no readable DTEND (only the "일정끝난시간에" auto-close mode reads it).
    public sealed class CalendarEntry
    {
        public string Uid, Title;
        public DateTime StartLocal;
        public DateTime? EndLocal;
    }

    // Reads the small subset of iCalendar used for Knox meeting reminders, and decides which events are due.
    // Pure and stateless, like ReminderSchedule: no clock reads, no file or process access.
    public static class CalendarSchedule
    {
        private static readonly string[] StampFormats = { "yyyyMMdd'T'HHmmss", "yyyyMMdd" };

        // Only BEGIN/END:VEVENT, UID, SUMMARY, DTSTART and DTEND are understood; everything else in the file is ignored.
        // Events missing a UID or a readable DTSTART are skipped rather than failing the whole file.
        public static List<CalendarEntry> ParseIcs(string text)
        {
            List<CalendarEntry> entries = new List<CalendarEntry>();
            if (String.IsNullOrEmpty(text))
                return entries;
            string uid = null, summary = null;
            DateTime? start = null, end = null;
            bool inEvent = false;
            foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line == "BEGIN:VEVENT") { inEvent = true; uid = null; summary = null; start = null; end = null; continue; }
                if (line == "END:VEVENT")
                {
                    inEvent = false;
                    if (!String.IsNullOrEmpty(uid) && start.HasValue)
                        entries.Add(new CalendarEntry { Uid = uid, Title = summary ?? "", StartLocal = start.Value, EndLocal = end });
                    continue;
                }
                if (!inEvent)
                    continue;
                int colon = line.IndexOf(':');
                if (colon < 0)
                    continue;
                string key = line.Substring(0, colon), value = line.Substring(colon + 1);
                int semicolon = key.IndexOf(';');
                string name = semicolon < 0 ? key : key.Substring(0, semicolon);
                if (name == "UID") uid = value.Trim();
                else if (name == "SUMMARY") summary = Unescape(value);
                else if (name == "DTSTART") start = ParseStamp(value);
                else if (name == "DTEND") end = ParseStamp(value);
            }
            return entries;
        }

        private static string Unescape(string value)
        {
            return value.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
        }

        // Accepts "yyyyMMddTHHmmss" (floating/local) and "yyyyMMddTHHmmssZ" (UTC, converted to local time).
        private static DateTime? ParseStamp(string value)
        {
            value = value.Trim();
            bool utc = value.EndsWith("Z", StringComparison.Ordinal);
            string core = utc ? value.Substring(0, value.Length - 1) : value;
            DateTime parsed;
            DateTimeStyles style = utc ? DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal : DateTimeStyles.AssumeLocal;
            if (!DateTime.TryParseExact(core, StampFormats, CultureInfo.InvariantCulture, style, out parsed))
                return null;
            return utc ? parsed.ToLocalTime() : parsed;
        }

        // The per-day "already reminded" key: the meeting's uid PLUS its start time. A uid alone would
        // permanently silence a meeting the user moves around the same day (the common way people test);
        // keying on the start too re-arms the alert whenever the meeting is rescheduled to a later time.
        public static string FiredKey(CalendarEntry entry)
        {
            if (entry == null)
                throw new ArgumentException("entry is required.", "entry");
            return entry.Uid + "|" + entry.StartLocal.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        // Entries whose popup moment (start - leadMinutes) has arrived, that have not started yet, and that have not
        // already fired. The caller is responsible for remembering which keys it marks fired (see FiredKey).
        public static List<CalendarEntry> DueEntries(DateTime now, IEnumerable<CalendarEntry> entries, int leadMinutes, ISet<string> firedUids)
        {
            if (entries == null)
                throw new ArgumentException("entries are required.", "entries");
            if (firedUids == null)
                throw new ArgumentException("firedUids is required.", "firedUids");
            List<CalendarEntry> due = new List<CalendarEntry>();
            foreach (CalendarEntry entry in entries)
            {
                if (String.IsNullOrEmpty(entry.Uid) || firedUids.Contains(FiredKey(entry)))
                    continue;
                DateTime popupAt = entry.StartLocal.AddMinutes(-leadMinutes);
                if (now >= popupAt && now < entry.StartLocal)
                    due.Add(entry);
            }
            return due;
        }
    }

    // One Knox schedule as returned by the chatbot2 bot server's /api/knox/calendar/schedules endpoint.
    // Start is required; End is optional (only the "일정끝난시간에" auto-close mode reads it).
    // Attendees, place and anything else in the response are ignored.
    public sealed class KnoxSchedule
    {
        public string ScheduleId, Subject, Start, End;
    }

    // One line of the Knox sync history shown in the settings window.
    public sealed class KnoxSyncLogEntry
    {
        public string When, Detail;
        public bool Ok;
    }

    // Pure helpers for Knox calendar sync: parsing the chatbot2 server's schedule JSON and building the
    // fixed ics text CalendarSchedule.ParseIcs reads back. No network or file access here; DeskHooker.cs
    // does the HTTP GET and the atomic file write. This replaced an earlier opencode/skill-based design:
    // the fetch-and-format work is fully deterministic, so there is nothing for an LLM agent to decide,
    // and running it headlessly through one risked hanging forever on an unanswerable permission prompt.
    public static class KnoxSync
    {
        public const string ApiUrl = "http://12.81.222.41:17001/api/knox/calendar/schedules?knox_id=";
        public const int LogLimit = 40;

        // Appends one entry and drops the oldest past LogLimit. The caller owns locking and persistence.
        public static List<KnoxSyncLogEntry> AppendLog(List<KnoxSyncLogEntry> log, KnoxSyncLogEntry entry)
        {
            if (log == null)
                throw new ArgumentException("log is required.", "log");
            if (entry == null)
                throw new ArgumentException("entry is required.", "entry");
            log.Add(entry);
            if (log.Count > LogLimit)
                log.RemoveRange(0, log.Count - LogLimit);
            return log;
        }

        // First non-empty line of error text, trimmed to fit the log and the settings window.
        public static string FirstLine(string text, int maxChars)
        {
            if (String.IsNullOrEmpty(text))
                return "";
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (line.Length > maxChars)
                    return line.Substring(0, Math.Max(0, maxChars - 1)) + "…";
                return line;
            }
            return "";
        }

        // Reads {"schedules":[{"scheduleId","subject","start",...}, ...]}. Entries missing "subject" or
        // "start" are skipped rather than failing the whole response. Throws ArgumentException when the
        // response is not valid JSON or has no "schedules" array at all.
        public static List<KnoxSchedule> ParseSchedules(string json)
        {
            if (String.IsNullOrEmpty(json))
                throw new ArgumentException("json is required.", "json");
            Dictionary<string, object> map;
            try { map = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch (Exception error) { throw new ArgumentException("Response is not valid JSON: " + error.Message); }
            object raw;
            System.Collections.IEnumerable items = map != null && map.TryGetValue("schedules", out raw) ? raw as System.Collections.IEnumerable : null;
            if (items == null)
                throw new ArgumentException("Response is missing a 'schedules' array.");
            var schedules = new List<KnoxSchedule>();
            foreach (object entry in items)
            {
                Dictionary<string, object> row = entry as Dictionary<string, object>;
                object subject, start, scheduleId, end;
                if (row == null || !row.TryGetValue("subject", out subject) || !row.TryGetValue("start", out start))
                    continue;
                row.TryGetValue("scheduleId", out scheduleId);
                row.TryGetValue("end", out end);
                schedules.Add(new KnoxSchedule { ScheduleId = scheduleId as string, Subject = Convert.ToString(subject), Start = Convert.ToString(start), End = end == null ? null : Convert.ToString(end) });
            }
            return schedules;
        }

        private static readonly System.Text.RegularExpressions.Regex StartPattern =
            new System.Text.RegularExpressions.Regex(@"^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(Z|[+-]\d{2}:\d{2})?$");

        // Converts one schedule's "start" to the "yyyyMMdd'T'HHmmss"["Z"] stamp CalendarSchedule.ParseIcs
        // expects, or null for a date-only (all-day) start or anything unreadable: a meeting without a
        // time to count down to is never shown as a lead-time popup.
        public static string ToDtStamp(string start)
        {
            if (String.IsNullOrEmpty(start))
                return null;
            System.Text.RegularExpressions.Match match = StartPattern.Match(start);
            if (!match.Success)
                return null;
            string stamp = match.Groups[1].Value + match.Groups[2].Value + match.Groups[3].Value + "T" + match.Groups[4].Value + match.Groups[5].Value + match.Groups[6].Value;
            return match.Groups[7].Value == "Z" ? stamp + "Z" : stamp;
        }

        // The "11.10 14:00" clock text for one schedule's start, as shown in the sync log. Follows the
        // same time rule as the reminder pipeline (ToDtStamp + CalendarSchedule.ParseIcs): a start
        // with an offset shows its wall-clock digits, a literal Z is converted to the PC's local time.
        // Date-only (all-day) and unreadable starts have no clock time, so "" is returned for them.
        public static string StartDisplay(string start)
        {
            if (String.IsNullOrEmpty(start))
                return "";
            System.Text.RegularExpressions.Match match = StartPattern.Match(start);
            if (!match.Success)
                return "";
            var wall = new DateTime(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture));
            DateTime shown = match.Groups[7].Value == "Z" ? wall.ToLocalTime() : wall;
            return shown.ToString("MM.dd HH:mm", CultureInfo.InvariantCulture);
        }

        // The content half of a success log line: every fetched meeting as "11.10 14:00 제목" joined
        // with " · ", truncated to maxChars so a long calendar cannot blow up one log line or its
        // JSON file. All-day or unreadable starts still show their subject, just without a clock time.
        public static string SummarizeSchedules(IEnumerable<KnoxSchedule> schedules, int maxChars)
        {
            if (schedules == null)
                throw new ArgumentException("schedules is required.", "schedules");
            if (maxChars <= 0)
                throw new ArgumentException("maxChars must be positive.", "maxChars");
            var parts = new List<string>();
            foreach (KnoxSchedule schedule in schedules)
            {
                string subject = FirstLine(schedule.Subject, 60);
                string when = StartDisplay(schedule.Start);
                parts.Add(String.IsNullOrEmpty(when) ? subject : when + " " + subject);
            }
            string joined = String.Join(" · ", parts.ToArray());
            if (joined.Length > maxChars)
                return joined.Substring(0, Math.Max(0, maxChars - 1)) + "…";
            return joined;
        }

        private static string EscapeSummary(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;").Replace("\r\n", "\\n").Replace("\n", "\\n");
        }

        // Ascii letters and digits only, up to max chars: a stable fallback UID ingredient when a
        // schedule carries no scheduleId.
        private static string AsciiSlug(string value, int max)
        {
            var slug = new System.Text.StringBuilder();
            foreach (char c in value ?? "")
            {
                if (slug.Length >= max)
                    break;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                    slug.Append(c);
            }
            return slug.ToString();
        }

        // Builds the fixed ics text (CRLF line endings) CalendarSchedule.ParseIcs reads back. An empty or
        // all-day-only schedule list still produces a valid, event-less calendar: that is not an error,
        // just a 48-hour window with no timed meetings.
        public static string BuildIcs(IEnumerable<KnoxSchedule> schedules)
        {
            if (schedules == null)
                throw new ArgumentException("schedules is required.", "schedules");
            var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//DeskHooker//knox-ics-export//KO" };
            foreach (KnoxSchedule schedule in schedules)
            {
                string stamp = ToDtStamp(schedule.Start);
                if (stamp == null)
                    continue;
                string uid = !String.IsNullOrEmpty(schedule.ScheduleId)
                    ? "knox-" + schedule.ScheduleId
                    : "knox-" + stamp.TrimEnd('Z') + "-" + AsciiSlug(schedule.Subject, 40);
                string endStamp = ToDtStamp(schedule.End);
                lines.Add("BEGIN:VEVENT");
                lines.Add("UID:" + uid);
                lines.Add("SUMMARY:" + EscapeSummary(schedule.Subject));
                lines.Add("DTSTART:" + stamp);
                if (endStamp != null)
                    lines.Add("DTEND:" + endStamp); // only the "일정끝난시간에" auto-close mode reads this back
                lines.Add("END:VEVENT");
            }
            lines.Add("END:VCALENDAR");
            return String.Join("\r\n", lines) + "\r\n";
        }
    }

    // One "[지원WP] [Invitation]" meeting invitation, structured out of the mail body's fixed form.
    // StartsAt/EndsAt are the PC's local time; a "회의일시" that could not be parsed keeps its raw
    // text in When (and both date fields stay null) instead of inventing a time.
    public sealed class KnoxInvitation
    {
        public string Title, When, Location, Organizer, Description;
        public DateTime? StartsAt, EndsAt;
        public List<string> RequiredAttendees, OptionalAttendees;
        public KnoxInvitation()
        {
            RequiredAttendees = new List<string>();
            OptionalAttendees = new List<string>();
        }
    }

    // One received mail as parsed from its raw POP3 bytes. Body is truncated for display; the
    // invitation, when the subject marks one, is parsed from the FULL text before that truncation.
    public sealed class KnoxMailMessage
    {
        public string Subject, Sender, SenderEmail, Body;
        public DateTime? Received;
        public List<string> AttachmentNames;
        public KnoxInvitation Invitation;
    }

    // One step of the reverse POP3 scan: mails are numbered oldest→newest, the scan walks them
    // newest-first, and this is the per-mail decision from the Date header it just read.
    public enum KnoxMailScanStep { Fetch, Skip, Stop }

    // Pure helpers for the "knox mail" feature: RFC 2047 header decoding, RFC 2822 dates, HTML →
    // text, MIME multipart walking and the "[지원WP] [Invitation]" form parser, ported from
    // chatbot2's app/mail_pop3_client.py (docs/mail_pop3_inbox.md 계약). No network or file access
    // here — DeskHooker.cs owns the POP3 session and the Windows credential manager.
    public static class KnoxMail
    {
        public const string Host = "pop3.samsung.net";
        public const int Port = 995, TimeoutMs = 12000, MaxScan = 500, BodyDefaultChars = 4000;
        // Only mails whose subject starts with this prefix are parsed as meeting invitations.
        public const string InvitationPrefix = "[지원WP] [Invitation]";
        // The invitation's last field (회의내용) swallows every following line, so the mail's footer
        // comes along — keep only what is plausible meeting text.
        public const int DescriptionMaxChars = 2000;

        // ------------------------------------------------------------------ scan decision

        // targetDate is "today" in the PC's zone (the PC runs in KST, which is what chatbot2 fixed
        // as KST): unreadable Date → keep scanning, older than the target day → stop (every lower
        // message number is older still), target day → fetch, future (clock skew) → keep scanning.
        public static KnoxMailScanStep DecideScanStep(DateTime targetDate, DateTime? received)
        {
            if (!received.HasValue)
                return KnoxMailScanStep.Skip;
            if (received.Value.Date < targetDate)
                return KnoxMailScanStep.Stop;
            if (received.Value.Date == targetDate)
                return KnoxMailScanStep.Fetch;
            return KnoxMailScanStep.Skip;
        }

        // ------------------------------------------------------------------ headers

        private static readonly Regex EncodedWordPattern = new Regex("=\\?([^?]+)\\?([BbQq])\\?([^?]*)\\?=");
        private static readonly Regex AdjacentWordSpace = new Regex("(?<=\\?=)[ \\t]+(?==\\?)");

        // RFC 2047: "=?utf-8?B?...?=" words (B = base64, Q = quoted-printable with '_' as space),
        // possibly several in a row. Whitespace BETWEEN two adjacent encoded words is dropped; a
        // header with no encoded words passes through unchanged. A header that carries RAW 8-bit
        // Korean (senders that skip RFC 2047 — the Latin-1 view came 1:1 from the wire) is decoded
        // with the utf-8 → euc-kr → Latin-1 fallback, like Python's decode_header.
        public static string DecodeHeader(string value)
        {
            if (String.IsNullOrEmpty(value))
                return "";
            string text = value.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            if (EncodedWordPattern.IsMatch(text))
            {
                text = AdjacentWordSpace.Replace(text, "");
                text = EncodedWordPattern.Replace(text, delegate(Match match) { return DecodeEncodedWord(match); });
            }
            else if (HasRaw8Bit(text))
            {
                byte[] bytes = new byte[text.Length];
                for (int i = 0; i < text.Length; i++)
                    bytes[i] = (byte)(text[i] & 0xFF);
                text = DecodeBytes(bytes, null);
            }
            return text.Trim();
        }

        private static bool HasRaw8Bit(string text)
        {
            foreach (char c in text)
                if (c > 0x7F)
                    return true;
            return false;
        }

        private static string DecodeEncodedWord(Match match)
        {
            string charset = match.Groups[1].Value;
            string encoding = match.Groups[2].Value.ToUpperInvariant();
            string data = match.Groups[3].Value;
            byte[] bytes;
            if (encoding == "B")
            {
                StringBuilder compact = new StringBuilder();
                foreach (char c in data)
                    if (!Char.IsWhiteSpace(c))
                        compact.Append(c);
                try { bytes = Convert.FromBase64String(compact.ToString()); }
                catch (FormatException) { return match.Value; } // not really base64 — keep the raw word
            }
            else if (encoding == "Q")
                bytes = DecodeQuotedPrintable(data.Replace('_', ' '));
            else
                return match.Value;
            return DecodeBytes(bytes, charset);
        }

        private static readonly Regex MailDatePattern = new Regex(
            "^\\s*(?:[A-Za-z]{3,9}\\s*,\\s*)?(\\d{1,2})\\s+([A-Za-z]{3})[A-Za-z]*\\s+(\\d{2,4})\\s+(\\d{1,2}):(\\d{2})(?::(\\d{2}))?\\s*([+-]\\d{4})?\\s*$");
        private static readonly string[] MonthNames =
            { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        // RFC 2822 "Date:" header → the PC's local time. An explicit numeric offset is honoured
        // (wall time minus the offset is UTC, converted with ToLocalTime); a header without one is
        // taken as already-local, matching chatbot2's "KST when no timezone" rule.
        public static DateTime? ParseMailDate(string value)
        {
            if (String.IsNullOrEmpty(value))
                return null;
            Match match = MailDatePattern.Match(value.Trim());
            if (!match.Success)
                return null;
            int day, month, year, hour, minute, second = 0;
            if (!Int32.TryParse(match.Groups[1].Value, out day) ||
                !Int32.TryParse(match.Groups[3].Value, out year) ||
                !Int32.TryParse(match.Groups[4].Value, out hour) ||
                !Int32.TryParse(match.Groups[5].Value, out minute))
                return null;
            month = 1 + Array.IndexOf(MonthNames, match.Groups[2].Value.ToLowerInvariant());
            if (match.Groups[6].Success && !Int32.TryParse(match.Groups[6].Value, out second))
                return null;
            if (year < 100)
                year += year < 50 ? 2000 : 1900;
            if (month < 1 || day < 1 || hour > 23 || minute > 59 || second > 59)
                return null;
            DateTime wall;
            try { wall = new DateTime(year, month, day, hour, minute, second); }
            catch (ArgumentOutOfRangeException) { return null; }
            if (match.Groups[7].Success)
            {
                string offsetText = match.Groups[7].Value;
                int offsetHours, offsetMinutes;
                if (!Int32.TryParse(offsetText.Substring(1, 2), out offsetHours) ||
                    !Int32.TryParse(offsetText.Substring(3, 2), out offsetMinutes))
                    return null;
                int sign = offsetText[0] == '-' ? -1 : 1;
                DateTime utc = wall.AddHours(-sign * offsetHours).AddMinutes(-sign * offsetMinutes);
                return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
            }
            return DateTime.SpecifyKind(wall, DateTimeKind.Local);
        }

        // The Date header out of a "TOP n 0" header block (the raw bytes POP3 returned), or null
        // when the block carries no readable Date. Everything past the first blank line is ignored.
        public static DateTime? DateFromHeaderBlock(byte[] raw)
        {
            if (raw == null || raw.Length == 0)
                return null;
            string head, body;
            SplitHeadBody(Encoding.GetEncoding(28591).GetString(raw).Replace("\r\n", "\n"), out head, out body);
            return ParseMailDate(HeaderValue(SplitHeaders(head), "Date"));
        }

        // ------------------------------------------------------------------ transfer decoding

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static int HexValue(char c)
        {
            if (c <= '9') return c - '0';
            return (c <= 'F' ? c - 'A' : c - 'a') + 10;
        }

        // Quoted-printable "=XX" hex escapes over the Latin-1 view of the text. Soft line breaks
        // ("=" at end of line) vanish. Header Q-words arrive with '_' already turned into spaces.
        private static byte[] DecodeQuotedPrintable(string text)
        {
            List<byte> bytes = new List<byte>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '=')
                {
                    if (i + 2 < text.Length && IsHex(text[i + 1]) && IsHex(text[i + 2]))
                    {
                        bytes.Add((byte)(HexValue(text[i + 1]) * 16 + HexValue(text[i + 2])));
                        i += 2;
                        continue;
                    }
                    if (i + 1 >= text.Length || text[i + 1] == '\n')
                        continue;
                    if (text[i + 1] == '\r' && (i + 2 >= text.Length || text[i + 2] == '\n'))
                    {
                        i += i + 2 < text.Length ? 2 : 1;
                        continue;
                    }
                }
                bytes.Add((byte)(c & 0xFF));
            }
            return bytes.ToArray();
        }

        // Decodes bytes with the declared charset, falling back utf-8 → euc-kr → Latin-1 like the
        // Python original. The first two use throw-on-invalid so a wrong guess falls through
        // instead of silently producing mojibake.
        private static string DecodeBytes(byte[] bytes, string charset)
        {
            if (bytes == null || bytes.Length == 0)
                return "";
            List<string> candidates = new List<string>();
            if (!String.IsNullOrEmpty(charset))
                candidates.Add(charset);
            candidates.Add("utf-8");
            candidates.Add("euc-kr");
            foreach (string name in candidates)
            {
                try
                {
                    Encoding encoding = String.Equals(name, "utf-8", StringComparison.OrdinalIgnoreCase)
                        ? (Encoding)new UTF8Encoding(false, true)
                        : Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    return encoding.GetString(bytes);
                }
                catch (ArgumentException) { } // unknown charset name, or a wrong guess: the strict
                                              // decoders throw DecoderFallbackException (and
                                              // NotSupportedException), both ArgumentException subclasses
            }
            return Encoding.GetEncoding(28591).GetString(bytes);
        }

        // RFC 2231 "filename*=utf-8''%ED%9A%8C..." → decoded with the declared charset.
        private static string DecodeRfc2231(string value)
        {
            string charset = null, encoded = value;
            int quote = value.IndexOf("''", StringComparison.Ordinal);
            if (quote >= 0)
            {
                charset = value.Substring(0, quote);
                encoded = value.Substring(quote + 2);
            }
            List<byte> bytes = new List<byte>(encoded.Length);
            for (int i = 0; i < encoded.Length; i++)
            {
                char c = encoded[i];
                if (c == '%' && i + 2 < encoded.Length && IsHex(encoded[i + 1]) && IsHex(encoded[i + 2]))
                {
                    bytes.Add((byte)(HexValue(encoded[i + 1]) * 16 + HexValue(encoded[i + 2])));
                    i += 2;
                }
                else
                    bytes.Add((byte)(c & 0xFF));
            }
            return DecodeBytes(bytes.ToArray(), charset);
        }

        // ------------------------------------------------------------------ HTML → text

        private static readonly Regex ScriptStylePattern = new Regex(@"(?is)<(script|style)\b.*?</\1\s*>");
        private static readonly Regex CellEndPattern = new Regex(@"(?i)</(td|th)\s*>");
        private static readonly Regex BlockEndPattern = new Regex(@"(?i)</(p|div|tr|table|ul|ol|li|h[1-6])\s*>");
        private static readonly Regex BrPattern = new Regex(@"(?i)<br\s*/?>");
        private static readonly Regex AnyTagPattern = new Regex("<[^>]*>");
        private static readonly Regex EntityPattern = new Regex("&(#[xX]?[0-9a-fA-F]+|[a-zA-Z][a-zA-Z0-9]{1,10});");

        // HTML → text. Table cells (</td>) become tabs and block ends become newlines so the
        // invitation's "회의제목\t회의" one-line form survives the conversion. System.Web is
        // deliberately not referenced, so a minimal entity decoder runs instead of HttpUtility.
        public static string HtmlToText(string html)
        {
            if (String.IsNullOrEmpty(html))
                return "";
            string text = ScriptStylePattern.Replace(html, " ");
            text = CellEndPattern.Replace(text, "\t");
            text = BlockEndPattern.Replace(text, "\n");
            text = BrPattern.Replace(text, "\n");
            text = AnyTagPattern.Replace(text, "");
            return DecodeEntities(text).Trim();
        }

        // The numeric forms plus the handful of named entities that actually show up in these
        // mails; an unknown name stays literal rather than disappearing.
        private static string DecodeEntities(string text)
        {
            return EntityPattern.Replace(text, delegate(Match match)
            {
                string body = match.Groups[1].Value;
                if (body[0] == '#')
                {
                    bool hex = body.Length > 1 && (body[1] == 'x' || body[1] == 'X');
                    int code;
                    if (!Int32.TryParse(body.Substring(hex ? 2 : 1), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
                        return match.Value;
                    if (code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
                        return match.Value;
                    return Char.ConvertFromUtf32(code);
                }
                switch (body)
                {
                    case "amp": return "&";
                    case "lt": return "<";
                    case "gt": return ">";
                    case "quot": return "\"";
                    case "apos": return "'";
                    case "nbsp": return " ";
                    case "copy": return "©";
                    case "reg": return "®";
                    default: return match.Value;
                }
            });
        }

        // ------------------------------------------------------------------ MIME structure

        private sealed class MimePart
        {
            public List<string[]> Headers;
            public string Body;
        }

        private static void SplitHeadBody(string text, out string head, out string body)
        {
            int splitAt = text == null ? -1 : text.IndexOf("\n\n", StringComparison.Ordinal);
            if (splitAt < 0) { head = text ?? ""; body = ""; }
            else { head = text.Substring(0, splitAt); body = text.Substring(splitAt + 2); }
        }

        // Header lines with RFC 822 unfolding: a line starting with space/tab continues the
        // previous one. Returns [name, value] pairs with names as written (lookups ignore case).
        private static List<string[]> SplitHeaders(string head)
        {
            List<string[]> headers = new List<string[]>();
            foreach (string rawLine in (head ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Trim().Length == 0)
                    continue;
                if ((line[0] == ' ' || line[0] == '\t') && headers.Count > 0)
                {
                    headers[headers.Count - 1][1] = headers[headers.Count - 1][1] + " " + line.Trim();
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon < 0)
                    continue;
                headers.Add(new string[] { line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim() });
            }
            return headers;
        }

        private static string HeaderValue(List<string[]> headers, string name)
        {
            foreach (string[] header in headers)
                if (String.Equals(header[0], name, StringComparison.OrdinalIgnoreCase))
                    return header[1];
            return null;
        }

        // "text/plain; charset=\"utf-8\"" → the value of one parameter, or null. "name*" lookups
        // stay distinct from "name" because the key keeps its asterisk (RFC 2231).
        private static string HeaderParam(string header, string name)
        {
            if (String.IsNullOrEmpty(header))
                return null;
            foreach (string piece in header.Split(';'))
            {
                string part = piece.Trim();
                int equals = part.IndexOf('=');
                if (equals < 0)
                    continue;
                if (!String.Equals(part.Substring(0, equals).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    continue;
                string value = part.Substring(equals + 1).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    value = value.Substring(1, value.Length - 2);
                return value;
            }
            return null;
        }

        private static string PartFilename(List<string[]> headers)
        {
            string disposition = HeaderValue(headers, "Content-Disposition");
            string extended = disposition == null ? null : HeaderParam(disposition, "filename*");
            if (extended != null)
                return DecodeRfc2231(extended);
            string plain = disposition == null ? null : HeaderParam(disposition, "filename");
            if (plain == null)
            {
                string contentType = HeaderValue(headers, "Content-Type");
                extended = contentType == null ? null : HeaderParam(contentType, "name*");
                if (extended != null)
                    return DecodeRfc2231(extended);
                plain = contentType == null ? null : HeaderParam(contentType, "name");
            }
            return plain == null ? null : DecodeHeader(plain);
        }

        // Splits a multipart body on its boundary lines, returning the sections between them (the
        // preamble and the epilogue are dropped). Nested multiparts recurse via CollectParts.
        private static List<string> SplitOnBoundary(string body, string boundary)
        {
            List<string> sections = new List<string>();
            StringBuilder current = new StringBuilder();
            string dashBoundary = "--" + boundary;
            bool inSection = false;
            foreach (string rawLine in (body ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.TrimEnd('\r').Trim();
                if (line == dashBoundary || line == dashBoundary + "--")
                {
                    if (inSection)
                        sections.Add(current.ToString());
                    current.Length = 0;
                    inSection = line == dashBoundary;
                    continue;
                }
                if (inSection)
                    current.Append(rawLine.TrimEnd('\r')).Append('\n');
            }
            if (inSection && current.Length > 0)
                sections.Add(current.ToString());
            return sections;
        }

        private static void CollectParts(string body, string boundary, int depth, List<MimePart> parts)
        {
            if (depth > 3)
                return;
            foreach (string section in SplitOnBoundary(body, boundary))
            {
                string head, sectionBody;
                SplitHeadBody(section, out head, out sectionBody);
                List<string[]> headers = SplitHeaders(head);
                string contentType = HeaderValue(headers, "Content-Type") ?? "";
                string innerBoundary = HeaderParam(contentType, "boundary");
                if (contentType.TrimStart().StartsWith("multipart", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrEmpty(innerBoundary))
                    CollectParts(sectionBody, innerBoundary, depth + 1, parts);
                else
                    parts.Add(new MimePart { Headers = headers, Body = sectionBody });
            }
        }

        // Content-Transfer-Encoding of one leaf part → its raw bytes. The body text travels through
        // a Latin-1 string view (1 byte = 1 char) until here, so 7bit/8bit is the chars cast back.
        private static byte[] DecodeTransfer(List<string[]> headers, string body)
        {
            string encoding = (HeaderValue(headers, "Content-Transfer-Encoding") ?? "7bit").Trim().ToLowerInvariant();
            string text = (body ?? "").Replace("\r\n", "\n");
            if (encoding == "base64")
            {
                StringBuilder compact = new StringBuilder();
                foreach (char c in text)
                    if (!Char.IsWhiteSpace(c))
                        compact.Append(c);
                try { return Convert.FromBase64String(compact.ToString()); }
                catch (FormatException) { return new byte[0]; }
            }
            if (encoding == "quoted-printable")
                return DecodeQuotedPrintable(text);
            byte[] bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
                bytes[i] = (byte)(text[i] & 0xFF);
            return bytes;
        }

        private static void ParseAddress(string value, out string name, out string address)
        {
            name = "";
            address = "";
            if (String.IsNullOrEmpty(value))
                return;
            int open = value.LastIndexOf('<');
            int close = value.LastIndexOf('>');
            if (open >= 0 && close > open)
            {
                address = value.Substring(open + 1, close - open - 1).Trim();
                name = value.Substring(0, open).Trim().Trim('"').Trim();
            }
            else
                address = value.Trim();
        }

        public static bool IsInvitationSubject(string subject)
        {
            return subject != null && subject.StartsWith(InvitationPrefix, StringComparison.Ordinal);
        }

        // One raw message (as RETR returned it) → one KnoxMailMessage. text/plain beats text/html
        // (an html-only mail is run through HtmlToText), attachments keep just their filenames,
        // and the invitation form is parsed from the full text before Body is truncated.
        public static KnoxMailMessage ParseMailMessage(byte[] raw, int bodyChars)
        {
            if (raw == null)
                throw new ArgumentException("raw is required.", "raw");
            string head, body;
            SplitHeadBody(Encoding.GetEncoding(28591).GetString(raw).Replace("\r\n", "\n"), out head, out body);
            List<string[]> headers = SplitHeaders(head);
            string contentType = HeaderValue(headers, "Content-Type") ?? "";
            string boundary = HeaderParam(contentType, "boundary");
            List<MimePart> parts = new List<MimePart>();
            if (contentType.TrimStart().StartsWith("multipart", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrEmpty(boundary))
                CollectParts(body, boundary, 0, parts);
            else
                parts.Add(new MimePart { Headers = headers, Body = body });

            string plain = null, html = null;
            List<string> attachments = new List<string>();
            foreach (MimePart part in parts)
            {
                string filename = PartFilename(part.Headers);
                string disposition = (HeaderValue(part.Headers, "Content-Disposition") ?? "").ToLowerInvariant();
                if (filename != null || disposition.IndexOf("attachment", StringComparison.Ordinal) >= 0)
                {
                    if (filename != null && filename.Trim().Length > 0)
                        attachments.Add(filename);
                    continue;
                }
                string partTypeFull = HeaderValue(part.Headers, "Content-Type") ?? "text/plain";
                string partType = partTypeFull;
                int semicolon = partTypeFull.IndexOf(';');
                if (semicolon >= 0)
                    partType = partTypeFull.Substring(0, semicolon);
                partType = partType.Trim().ToLowerInvariant();
                string decoded = DecodeBytes(DecodeTransfer(part.Headers, part.Body), HeaderParam(partTypeFull, "charset"));
                if (partType == "text/plain" && plain == null)
                    plain = decoded;
                else if (partType == "text/html" && html == null)
                    html = decoded;
            }
            string fullText = plain != null && plain.Trim().Length > 0 ? plain : (html != null ? HtmlToText(html) : "");

            string subject = DecodeHeader(HeaderValue(headers, "Subject"));
            string senderName, senderEmail;
            ParseAddress(DecodeHeader(HeaderValue(headers, "From")), out senderName, out senderEmail);
            int limit = Math.Max(0, bodyChars);
            return new KnoxMailMessage
            {
                Subject = subject,
                Sender = senderName,
                SenderEmail = senderEmail,
                Received = ParseMailDate(HeaderValue(headers, "Date")),
                Body = fullText.Length > limit ? fullText.Substring(0, limit) : fullText,
                AttachmentNames = attachments,
                Invitation = IsInvitationSubject(subject) ? ParseInvitationBody(fullText) : null
            };
        }

        // ------------------------------------------------------------------ invitation form

        // 실물 양식(2026-11-16 수신 메일 관측) — 라벨과 값이 탭(또는 콜론·공백)으로 나뉜 한 줄씩:
        //   회의제목\t회의
        //   회의일시\t2026-11-16 14:00 ~ 2026-11-16 15:00
        //   회의장소\t화성 DSR C타워 21층 ... A2103
        //   주관자\t정성문 [AX/PI팀(S.LSI)]
        //   참석자\t필수참석자 : 고재명,김주영,…
        //   선택참석자 :        ← 참석자 칸의 이어진 줄이므로 라벨이 아닌 이어진 줄로 흡수한다
        //   회의내용\t회의
        private static readonly Regex InvitationLabelLine =
            new Regex("^\\s*(회의제목|회의일시|회의장소|주관자|참석자|회의내용)\\s*[\\t:：]?\\s*(.*)$");
        private static readonly Regex RequiredAttendeeLine = new Regex("필수참석자\\s*[:：]?\\s*([^\\n]*)");
        private static readonly Regex OptionalAttendeeLine = new Regex("선택참석자\\s*[:：]?\\s*([^\\n]*)");
        private static readonly Regex MeetingDateTimePattern = new Regex("(\\d{4})-(\\d{1,2})-(\\d{1,2})\\s+(\\d{1,2}):(\\d{2})");
        private static readonly Regex MeetingTimeOnlyPattern = new Regex("^(\\d{1,2}):(\\d{2})$");

        // Structures the invitation form out of a mail body. No label line at all → null; a label
        // found twice keeps only its last section; continuation lines (like "선택참석자 :") join the
        // current section. Fields that are missing stay empty rather than failing the whole parse.
        public static KnoxInvitation ParseInvitationBody(string body)
        {
            if (String.IsNullOrEmpty(body))
                return null;
            Dictionary<string, List<string>> sections = new Dictionary<string, List<string>>();
            string current = null;
            foreach (string rawLine in body.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;
                Match match = InvitationLabelLine.Match(line);
                if (match.Success)
                {
                    current = match.Groups[1].Value;
                    string value = match.Groups[2].Value.Trim();
                    List<string> lines = new List<string>();
                    if (value.Length > 0)
                        lines.Add(value);
                    sections[current] = lines;
                    continue;
                }
                if (current != null)
                    sections[current].Add(line);
            }
            if (sections.Count == 0)
                return null;

            KnoxInvitation meeting = new KnoxInvitation();
            meeting.Title = SectionText(sections, "회의제목");
            meeting.When = SectionText(sections, "회의일시");
            DateTime? start, end;
            ParseMeetingWindow(meeting.When, out start, out end);
            meeting.StartsAt = start;
            meeting.EndsAt = end;
            meeting.Location = SectionText(sections, "회의장소");
            meeting.Organizer = SectionText(sections, "주관자");
            string attendeeText = SectionText(sections, "참석자");
            Match required = RequiredAttendeeLine.Match(attendeeText);
            meeting.RequiredAttendees = required.Success ? SplitNames(required.Groups[1].Value) : new List<string>();
            Match optional = OptionalAttendeeLine.Match(attendeeText);
            meeting.OptionalAttendees = optional.Success ? SplitNames(optional.Groups[1].Value) : new List<string>();
            meeting.Description = SectionText(sections, "회의내용");
            if (meeting.Description.Length > DescriptionMaxChars)
                meeting.Description = meeting.Description.Substring(0, DescriptionMaxChars);
            return meeting;
        }

        private static string SectionText(Dictionary<string, List<string>> sections, string label)
        {
            List<string> lines;
            return sections.TryGetValue(label, out lines) ? String.Join("\n", lines.ToArray()).Trim() : "";
        }

        // "2026-11-16 14:00 ~ 2026-11-16 15:00" (or "~ 15:00" with the end date left out) → local
        // start/end. An unreadable window yields two nulls; the raw text stays in When.
        private static void ParseMeetingWindow(string whenRaw, out DateTime? start, out DateTime? end)
        {
            start = null;
            end = null;
            if (String.IsNullOrEmpty(whenRaw))
                return;
            string[] parts = whenRaw.Split('~');
            if (parts.Length < 2)
                return;
            DateTime? parsedStart = ParseDtOrTime(parts[0].Trim(), null);
            if (!parsedStart.HasValue)
                return;
            start = parsedStart;
            end = ParseDtOrTime(parts[1].Trim(), parsedStart.Value);
        }

        // One side of the window: "YYYY-MM-DD HH:MM", or bare "HH:MM" which inherits dateFrom's date.
        private static DateTime? ParseDtOrTime(string text, DateTime? dateFrom)
        {
            if (String.IsNullOrEmpty(text))
                return null;
            Match match = MeetingDateTimePattern.Match(text);
            if (match.Success)
            {
                int year, month, day, hour, minute;
                if (!Int32.TryParse(match.Groups[1].Value, out year) ||
                    !Int32.TryParse(match.Groups[2].Value, out month) ||
                    !Int32.TryParse(match.Groups[3].Value, out day) ||
                    !Int32.TryParse(match.Groups[4].Value, out hour) ||
                    !Int32.TryParse(match.Groups[5].Value, out minute))
                    return null;
                try { return new DateTime(year, month, day, hour, minute, 0); }
                catch (ArgumentOutOfRangeException) { return null; }
            }
            if (dateFrom.HasValue)
            {
                Match timeOnly = MeetingTimeOnlyPattern.Match(text);
                int hour, minute;
                if (timeOnly.Success && Int32.TryParse(timeOnly.Groups[1].Value, out hour) && Int32.TryParse(timeOnly.Groups[2].Value, out minute)
                    && hour <= 23 && minute <= 59)
                    return new DateTime(dateFrom.Value.Year, dateFrom.Value.Month, dateFrom.Value.Day, hour, minute, 0);
            }
            return null;
        }

        // "고재명,김주영" → ["고재명", "김주영"]; empty pieces are dropped.
        private static List<string> SplitNames(string text)
        {
            List<string> names = new List<string>();
            if (String.IsNullOrEmpty(text))
                return names;
            foreach (string piece in text.Split(new char[] { ',', '，', '、', ';' }))
            {
                string name = piece.Trim();
                if (name.Length > 0)
                    names.Add(name);
            }
            return names;
        }

        // ------------------------------------------------------------------ meeting merge

        private static uint Fnv1a(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text ?? "")
            {
                hash = hash ^ c;
                hash = hash * 16777619;
            }
            return hash;
        }

        // The popup-facing form of one invitation mail. The uid is stable for the same
        // subject+window, so fired keys and dedup survive repeated syncs. A meeting without a
        // start time or without a title is not worth a popup — null.
        public static CalendarEntry InvitationToCalendarEntry(KnoxMailMessage mail)
        {
            if (mail == null || mail.Invitation == null)
                return null;
            KnoxInvitation meeting = mail.Invitation;
            if (!meeting.StartsAt.HasValue || String.IsNullOrWhiteSpace(meeting.Title))
                return null;
            string seed = mail.Subject + "\n"
                + meeting.StartsAt.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "\n"
                + (meeting.EndsAt.HasValue ? meeting.EndsAt.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) : "-");
            return new CalendarEntry
            {
                Uid = "knoxmail-" + meeting.StartsAt.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + Fnv1a(seed).ToString("x8"),
                Title = meeting.Title,
                StartLocal = meeting.StartsAt.Value,
                EndLocal = meeting.EndsAt
            };
        }

        // Appends mail meeting entries to the Knox calendar list, dropping ones the calendar
        // already covers: the same start time with an equal (or containing) title means the same
        // meeting was invited AND written to the calendar. Duplicates inside the mail list itself
        // collapse by uid. The result keeps the calendar's order first, mail entries appended.
        public static List<CalendarEntry> MergeMeetingEntries(IEnumerable<CalendarEntry> calendar, IEnumerable<CalendarEntry> meetings)
        {
            List<CalendarEntry> merged = new List<CalendarEntry>();
            HashSet<string> uids = new HashSet<string>(StringComparer.Ordinal);
            if (calendar != null)
                foreach (CalendarEntry entry in calendar)
                    if (entry != null && !String.IsNullOrEmpty(entry.Uid))
                    {
                        merged.Add(entry);
                        uids.Add(entry.Uid);
                    }
            if (meetings != null)
                foreach (CalendarEntry meeting in meetings)
                {
                    if (meeting == null || String.IsNullOrEmpty(meeting.Uid) || uids.Contains(meeting.Uid))
                        continue;
                    bool duplicate = false;
                    foreach (CalendarEntry entry in merged)
                        if (entry.StartLocal == meeting.StartLocal && TitlesOverlap(entry.Title, meeting.Title))
                        {
                            duplicate = true;
                            break;
                        }
                    if (!duplicate)
                    {
                        merged.Add(meeting);
                        uids.Add(meeting.Uid);
                    }
                }
            return merged;
        }

        private static bool TitlesOverlap(string a, string b)
        {
            if (String.IsNullOrEmpty(a) || String.IsNullOrEmpty(b))
                return String.IsNullOrEmpty(a) && String.IsNullOrEmpty(b);
            return String.Equals(a, b, StringComparison.Ordinal)
                || a.IndexOf(b, StringComparison.Ordinal) >= 0
                || b.IndexOf(a, StringComparison.Ordinal) >= 0;
        }
    }

    // A validated, plain-text wellness item shared by other people through the
    // repository's community/ folder. Limits MUST match validateShare() in app-core.js.
    // A YouTube video shared with a community item. Only the 11-character id is stored, never a link.
    // 373: 회의 시작 전 알림의 SLSI Avatar 메신저 송신 — 순수 부분(주소·응답 해석·글 만들기).
    // 흐름: 브라우저로 alpha 서버 SSO(desktop/start, cb=이 PC loopback) → 원타임 code → exchange 로
    // SSO access 토큰 → /api/me/desktop-token 으로 만료 없는 데스크톱 토큰(aa_…) → 아바타 봇 서버
    // /api/desktop/messenger/send 에 Bearer 로 실어 토큰 주인 본인의 1:1 방으로 보낸다. 받기는 없다.
    public static class AvatarMessenger
    {
        public const string AlphaBase = "https://slsi.agent.samsungds.net:4100";
        public const string BotBase = "http://12.81.222.41:17001";
        public const string StartPath = "/api/auth/desktop/start";
        public const string ExchangePath = "/api/auth/desktop/exchange";
        public const string DesktopTokenPath = "/api/me/desktop-token";
        public const string SendPath = "/api/desktop/messenger/send";
        public const string CallbackPath = "/cb";
        public const string TokenPrefix = "aa_";
        public const int MaxChars = 2000;      // 서버 상한(chatbot2 SEND_MAX_CHARS)과 같다
        private const int MaxTitleChars = 120;

        public static string CallbackUrl(int port)
        {
            if (port <= 0 || port > 65535)
                throw new ArgumentException("port must be 1-65535.", "port");
            return "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + CallbackPath;
        }

        public static string LoginUrl(int port)
        {
            return AlphaBase + StartPath + "?cb=" + Uri.EscapeDataString(CallbackUrl(port));
        }

        // The first line of the browser's request to the loopback listener, e.g. "GET /cb?code=ab12 HTTP/1.1".
        // Returns the one-time code, or null when the request is not our callback (favicon, other paths)
        // or carries no code.
        public static string CodeFromRequestLine(string requestLine)
        {
            if (String.IsNullOrEmpty(requestLine)) return null;
            string[] parts = requestLine.Split(' ');
            if (parts.Length < 2 || parts[0] != "GET") return null;
            string target = parts[1];
            int q = target.IndexOf('?');
            string path = q < 0 ? target : target.Substring(0, q);
            if (path != CallbackPath || q < 0) return null;
            foreach (string pair in target.Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0 || pair.Substring(0, eq) != "code") continue;
                string code = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')).Trim();
                return code.Length == 0 ? null : code;
            }
            return null;
        }

        private static Dictionary<string, object> ParseObject(string json)
        {
            if (String.IsNullOrEmpty(json))
                throw new ArgumentException("Response is empty.");
            Dictionary<string, object> map;
            try { map = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch (Exception error) { throw new ArgumentException("Response is not valid JSON: " + error.Message); }
            if (map == null) throw new ArgumentException("Response is not a JSON object.");
            return map;
        }

        // /api/auth/desktop/exchange → {claims:{loginid,…}, access_token, …}. Returns the access token;
        // knoxId gets the login id from the claims (same fallback order as slsi-agent-desktop auth.py).
        public static string AccessTokenFromExchange(string json, out string knoxId)
        {
            var map = ParseObject(json);
            object raw;
            string token = map.TryGetValue("access_token", out raw) ? Convert.ToString(raw) : "";
            if (String.IsNullOrEmpty(token))
                throw new ArgumentException("Response is missing access_token.");
            knoxId = "";
            var claims = map.TryGetValue("claims", out raw) ? raw as Dictionary<string, object> : null;
            if (claims != null)
            {
                foreach (string key in new[] { "loginid", "userid" })
                {
                    object v;
                    if (claims.TryGetValue(key, out v) && !String.IsNullOrEmpty(Convert.ToString(v))) { knoxId = Convert.ToString(v); break; }
                }
                object mail;
                if (knoxId.Length == 0 && (claims.TryGetValue("mail", out mail) || claims.TryGetValue("email", out mail)))
                {
                    string m = Convert.ToString(mail) ?? "";
                    int at = m.IndexOf('@');
                    knoxId = at > 0 ? m.Substring(0, at) : m;
                }
            }
            return token;
        }

        // 377: 알림은 이 PC 사용자 본인에게만 간다 — SSO 로 로그인한 계정(knox id)이 Windows 로그인 계정
        // 이름과 같을 때만 연결한다(Knox 일정 조회도 같은 이름을 knox id 로 쓴다). 대소문자는 무시한다.
        public static bool AccountMatchesPcUser(string knoxId, string pcUser)
        {
            if (String.IsNullOrEmpty(knoxId) || String.IsNullOrEmpty(pcUser)) return false;
            return String.Equals(knoxId.Trim(), pcUser.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // /api/me/desktop-token → {ok, token:"aa_…"}. Anything else is refused so a wrong-shaped answer
        // never ends up stored as a credential.
        public static string DesktopTokenFromResponse(string json)
        {
            var map = ParseObject(json);
            object raw;
            string token = map.TryGetValue("token", out raw) ? Convert.ToString(raw) : "";
            if (String.IsNullOrEmpty(token) || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
                throw new ArgumentException("Response is missing a desktop token.");
            return token;
        }

        public static string JsonBody(string key, string value)
        {
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object> { { key, value } });
        }

        // The messenger text for meetings whose lead-time moment has arrived, soonest first (382: 이모지 —
        // Knox 스티커 코드는 TEXT 메시지에서 글자 그대로 보여서 유니코드 이모지만 쓴다):
        //   ⏰ [쉼] 곧 회의가 시작돼요
        //   📅 10:00~11:00 주간 회의 · ⏳ 5분 뒤      (캘린더 일정)
        //   📩 10:30 초대 메일 회의 · ⏳ 35분 뒤      (메일 초대에서 온 회의, uid "knoxmail-")
        //   📅 09:55 바로 시작하는 회의 · 🔔 지금 시작
        public static string MeetingText(IEnumerable<CalendarEntry> due, DateTime now)
        {
            if (due == null)
                throw new ArgumentException("due is required.", "due");
            var list = new List<CalendarEntry>(due);
            list.Sort((a, b) => a.StartLocal.CompareTo(b.StartLocal));
            var text = new StringBuilder("⏰ [쉼] 곧 회의가 시작돼요");
            foreach (CalendarEntry e in list)
            {
                string title = String.IsNullOrEmpty(e.Title) ? "제목 없는 일정" : e.Title.Trim();
                if (title.Length > MaxTitleChars) title = title.Substring(0, MaxTitleChars) + "…";
                string when = e.StartLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
                if (e.EndLocal.HasValue && e.EndLocal.Value > e.StartLocal)
                    when += "~" + e.EndLocal.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
                int minutes = (int)Math.Ceiling((e.StartLocal - now).TotalMinutes);
                string left = minutes <= 0 ? "🔔 지금 시작" : "⏳ " + minutes.ToString(CultureInfo.InvariantCulture) + "분 뒤";
                bool fromMail = e.Uid != null && e.Uid.StartsWith("knoxmail-", StringComparison.Ordinal);
                string line = "\n" + (fromMail ? "📩 " : "📅 ") + when + " " + title + " · " + left;
                if (text.Length + line.Length > MaxChars) break;
                text.Append(line);
            }
            return text.ToString();
        }
    }

    public sealed class CommunityVideo
    {
        public string id, title, author;
    }

    public sealed class CommunityItem
    {
        public string Id, Name, Summary, Safety, Author, Icon;
        public string[] Instructions;
        public CommunityVideo[] Videos;
    }

    public static class CommunityContent
    {
        public const int MaxFileBytes = 6000;
        public const int MaxName = 30, MaxSummary = 60, MaxStep = 60, MaxSteps = 6, MaxSafety = 150, MaxAuthor = 20;
        public const int MaxVideos = 5, MaxVideoTitle = 60, MaxVideoAuthor = 30;
        // The tile symbols a shared item may choose from (the first is the default). MUST match ICONS in app-core.js.
        public static readonly string[] Icons = new string[] { "✿","❀","☾","☀","★","♥","♪","♫","◎","◌","↟","↗","≈","♧","☕","✦" };
        private static readonly System.Text.RegularExpressions.Regex VideoIdPattern =
            new System.Text.RegularExpressions.Regex("^[A-Za-z0-9_-]{11}$");
        private static readonly System.Text.RegularExpressions.Regex IdPattern =
            new System.Text.RegularExpressions.Regex("^[a-z0-9][a-z0-9-]{2,39}$");

        // Throws ArgumentException describing the first rule broken. Never returns partial data.
        public static CommunityItem Parse(string json)
        {
            if (String.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Empty community item.");
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
                throw new ArgumentException("Community item is too large.");
            System.Web.Script.Serialization.JavaScriptSerializer serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            Dictionary<string, object> map;
            try { map = serializer.Deserialize<Dictionary<string, object>>(json); }
            catch (Exception error) { throw new ArgumentException("Community item is not valid JSON: " + error.Message); }
            if (map == null)
                throw new ArgumentException("Community item must be a JSON object.");
            object schema;
            if (!map.TryGetValue("schema", out schema) || !(schema is int) || (int)schema != 1)
                throw new ArgumentException("Unsupported schema version.");

            CommunityItem item = new CommunityItem();
            item.Id = Text(map, "id", 40, true);
            if (!IdPattern.IsMatch(item.Id))
                throw new ArgumentException("id must be 3-40 chars of a-z, 0-9 and '-'.");
            if (item.Id == "index")
                throw new ArgumentException("id 'index' is reserved for community/index.json.");
            item.Name = Text(map, "name", MaxName, true);
            item.Summary = Text(map, "summary", MaxSummary, true);
            item.Safety = Text(map, "safety", MaxSafety, true);
            item.Author = Text(map, "author", MaxAuthor, false);
            object rawIcon;
            item.Icon = "";
            if (map.TryGetValue("icon", out rawIcon) && rawIcon != null)
            {
                string icon = rawIcon as string;
                if (icon == null || Array.IndexOf(Icons, icon) < 0)
                    throw new ArgumentException("icon must be one of the offered symbols.");
                item.Icon = icon;
            }

            // Optional YouTube videos (ids only). Each one is also checked for reachability before it is accepted (see CommunitySync).
            List<CommunityVideo> videos = new List<CommunityVideo>();
            object rawVideos;
            if (map.TryGetValue("videos", out rawVideos) && rawVideos != null)
            {
                System.Collections.IEnumerable entries = rawVideos as System.Collections.IEnumerable;
                if (entries == null || rawVideos is string || rawVideos is Dictionary<string, object>)
                    throw new ArgumentException("videos must be an array.");
                HashSet<string> seenVideos = new HashSet<string>(StringComparer.Ordinal);
                foreach (object entry in entries)
                {
                    if (videos.Count >= MaxVideos)
                        throw new ArgumentException("Too many videos.");
                    Dictionary<string, object> video = entry as Dictionary<string, object>;
                    if (video == null)
                        throw new ArgumentException("Each video must be an object.");
                    object rawId;
                    string id = video.TryGetValue("id", out rawId) ? rawId as string : null;
                    if (id == null || !VideoIdPattern.IsMatch(id))
                        throw new ArgumentException("video id must be an 11-character YouTube id.");
                    if (!seenVideos.Add(id))
                        throw new ArgumentException("Duplicate video id.");
                    CommunityVideo clean = new CommunityVideo();
                    clean.id = id;
                    clean.title = Text(video, "title", MaxVideoTitle, true);
                    clean.author = Text(video, "author", MaxVideoAuthor, false);
                    videos.Add(clean);
                }
            }
            item.Videos = videos.ToArray();

            object rawSteps;
            System.Collections.IEnumerable steps = map.TryGetValue("instructions", out rawSteps) ? rawSteps as System.Collections.IEnumerable : null;
            List<string> list = new List<string>();
            if (steps == null || rawSteps is string)
            {
                // A video-only item may leave the instructions out entirely.
                if (videos.Count == 0 || map.ContainsKey("instructions"))
                    throw new ArgumentException("instructions must be an array.");
            }
            else
            {
                foreach (object step in steps)
                {
                    if (list.Count >= MaxSteps)
                        throw new ArgumentException("Too many instructions.");
                    list.Add(Clean(step as string, MaxStep, true, "instruction"));
                }
            }
            if (list.Count == 0 && videos.Count == 0)
                throw new ArgumentException("At least one instruction is required.");
            item.Instructions = list.ToArray();
            return item;
        }

        private static string Text(Dictionary<string, object> map, string key, int max, bool required)
        {
            object value;
            map.TryGetValue(key, out value);
            return Clean(value as string, max, required, key);
        }

        // Plain text only: no markup, no links, no control characters.
        private static string Clean(string value, int max, bool required, string field)
        {
            value = value == null ? "" : value.Normalize(System.Text.NormalizationForm.FormC).Trim();
            if (value.Length == 0)
            {
                if (required) throw new ArgumentException(field + " is required.");
                return "";
            }
            if (value.Length > max)
                throw new ArgumentException(field + " is longer than " + max + " characters.");
            foreach (char c in value)
                if (Char.IsControl(c) || c == '<' || c == '>')
                    throw new ArgumentException(field + " contains markup or control characters.");
            if (value.IndexOf("://", StringComparison.Ordinal) >= 0 || value.IndexOf("www.", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new ArgumentException(field + " must not contain links.");
            return value;
        }
    }
}
