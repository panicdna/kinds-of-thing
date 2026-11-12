using System;
using System.Collections.Generic;
using System.Globalization;

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
            // 03 카드: 5개를 무작위로 하나씩 — 스트레칭·명상·발성·허리·릴렉스 (310: 댄스 안무 제외).
            s.ThemeIds = new string[] { "stretch", "meditation", "voice", "back-standing", "relax" };
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

    // A validated, plain-text wellness item shared by other people through the
    // repository's community/ folder. Limits MUST match validateShare() in app-core.js.
    // A YouTube video shared with a community item. Only the 11-character id is stored, never a link.
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
