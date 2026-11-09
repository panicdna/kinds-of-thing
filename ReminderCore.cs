using System;
using System.Collections.Generic;

namespace DeskHooker
{
    // Plain get/set properties and constructor defaults support JavaScriptSerializer.
    // Constructing or validating settings never enables reminders or OS startup.
    // A card (theme) may be started at an exact time of day, independently of the interval. Enabled switches it on.
    public sealed class CardSchedule
    {
        public bool Enabled;
        public string Time; // "HH:mm:ss", 24-hour
    }

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
        // A quiet period inside the day (e.g. lunch): reminders that would fall in [BreakStart, BreakEnd) wait until it ends.
        // Per card id: start it at an exact time of day (see ReminderSchedule.DueCards).
        public Dictionary<string, CardSchedule> CardSchedules { get; set; }
        public bool BreakEnabled { get; set; }
        public int BreakStartHour { get; set; }
        public int BreakStartMinute { get; set; }
        public int BreakEndHour { get; set; }
        public int BreakEndMinute { get; set; }

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
            CardSchedules = new Dictionary<string, CardSchedule>();
            BreakEnabled = false;
            BreakStartHour = 12; BreakStartMinute = 0;
            BreakEndHour = 13; BreakEndMinute = 0;
        }

        public const string VariantRandom = "random", VariantSequential = "sequential";
        public static bool IsValidVariantMode(string mode) { return mode == VariantRandom || mode == VariantSequential; }
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
            if (settings.CardSchedules != null)
            {
                if (settings.CardSchedules.Count > 64)
                    throw new ArgumentException("CardSchedules has too many entries.", "settings");
                foreach (KeyValuePair<string, CardSchedule> entry in settings.CardSchedules)
                {
                    if (String.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length > 64 || entry.Value == null)
                        throw new ArgumentException("CardSchedules must map a card id to a schedule.", "settings");
                    TimeSpan ignored;
                    if ((entry.Value.Enabled || entry.Value.Time != null) && !CardTimes.TryParseClock(entry.Value.Time, out ignored))
                        throw new ArgumentException("A card start time must look like 14:05:30 (00:00:00-23:59:59).", "settings");
                }
            }
            if (settings.BreakEnabled)
            {
                if (settings.BreakStartHour < 0 || settings.BreakStartHour > 23 || settings.BreakStartMinute < 0 || settings.BreakStartMinute > 59 ||
                    settings.BreakEndMinute < 0 || settings.BreakEndMinute > 59 || settings.BreakEndHour < 0 || settings.BreakEndHour > 24 ||
                    (settings.BreakEndHour == 24 && settings.BreakEndMinute != 0))
                    throw new ArgumentException("Break times must be 00:00-23:59 (the end may be 24:00).", "settings");
                if (BreakEnd(settings) <= BreakStart(settings))
                    throw new ArgumentException("The break must end after it starts.", "settings");
                // A break that swallows the whole allowed time would leave no moment to remind.
                TimeSpan allowedStart = settings.LimitHours ? WindowStart(settings) : TimeSpan.Zero;
                TimeSpan allowedEnd = settings.LimitHours ? WindowEnd(settings) : TimeSpan.FromHours(24);
                if (BreakStart(settings) <= allowedStart && BreakEnd(settings) >= allowedEnd)
                    throw new ArgumentException("The break must leave some time for reminders.", "settings");
            }
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
            // Cards for the interval rotation may be left empty when at least one card starts at a fixed time.
            if (settings.ThemeIds == null || (settings.ThemeIds.Length == 0 && !CardTimes.HasScheduledCard(settings)))
                throw new ArgumentException("At least one theme must be selected.", "settings");
            HashSet<string> known = new HashSet<string>(knownThemeIds, StringComparer.Ordinal);
            HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (string theme in settings.ThemeIds)
            {
                if (String.IsNullOrWhiteSpace(theme) || !known.Contains(theme) || !selected.Add(theme))
                    throw new ArgumentException("ThemeIds must contain distinct, nonempty, known IDs.", "settings");
            }
        }

        private static TimeSpan WindowStart(ReminderSettings s) { return new TimeSpan(s.StartHour, s.StartMinute, 0); }
        private static TimeSpan WindowEnd(ReminderSettings s) { return new TimeSpan(s.EndHour, s.EndMinute, 0); }
        private static TimeSpan BreakStart(ReminderSettings s) { return new TimeSpan(s.BreakStartHour, s.BreakStartMinute, 0); }
        private static TimeSpan BreakEnd(ReminderSettings s) { return new TimeSpan(s.BreakEndHour, s.BreakEndMinute, 0); }
        private static bool InBreak(ReminderSettings s, TimeSpan timeOfDay) { return s.BreakEnabled && timeOfDay >= BreakStart(s) && timeOfDay < BreakEnd(s); }

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

        // Add the full interval first, then clamp to the earliest allowed instant.
        // On dismissal use Next(dismissedAt, settings), not Next(previousDue, settings).
        // No clock reads, timers, startup registration, or other side effects.
        public static DateTime Next(DateTime from, ReminderSettings settings)
        {
            DateTime candidate = from.AddMinutes(settings.IntervalMinutes);
            // Each rule can push the time into another rule's forbidden zone (a break ending after the window, a weekend...),
            // so repeat until nothing moves. Validate() guarantees that some allowed time exists, so this ends quickly.
            for (int pass = 0; pass < 16; pass++)
            {
                DateTime before = candidate;
                if (settings.LimitHours && candidate.TimeOfDay >= WindowEnd(settings))
                    candidate = candidate.Date.AddDays(1) + WindowStart(settings);
                if (settings.LimitHours && candidate.TimeOfDay < WindowStart(settings))
                    candidate = candidate.Date + WindowStart(settings);
                if (InBreak(settings, candidate.TimeOfDay))
                    candidate = candidate.Date + BreakEnd(settings);
                while (settings.WeekdaysOnly &&
                    (candidate.DayOfWeek == DayOfWeek.Saturday || candidate.DayOfWeek == DayOfWeek.Sunday))
                    candidate = candidate.Date.AddDays(1) + (settings.LimitHours ? WindowStart(settings) : TimeSpan.Zero);
                if (candidate == before)
                    break;
            }
            return candidate;
        }
    }
    // Cards that start at an exact time of day. Kept apart from ReminderSchedule so that class keeps its small public surface.
    public static class CardTimes
    {
        private static readonly System.Text.RegularExpressions.Regex ClockPattern =
            new System.Text.RegularExpressions.Regex("^([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9]$");

        // Strict 24-hour "HH:mm:ss".
        public static bool TryParseClock(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (text == null || !ClockPattern.IsMatch(text))
                return false;
            time = new TimeSpan(Int32.Parse(text.Substring(0, 2)), Int32.Parse(text.Substring(3, 2)), Int32.Parse(text.Substring(6, 2)));
            return true;
        }

        public static bool HasScheduledCard(ReminderSettings settings)
        {
            if (settings == null || settings.CardSchedules == null)
                return false;
            foreach (CardSchedule schedule in settings.CardSchedules.Values)
            {
                TimeSpan ignored;
                if (schedule != null && schedule.Enabled && TryParseClock(schedule.Time, out ignored))
                    return true;
            }
            return false;
        }

        // Cards whose fixed start time was passed in the half-open span (previous, now], in time order.
        // Like the interval timer, nothing is returned after a long gap (sleep, lock, clock change) so missed
        // cards are not replayed in a burst. A fixed time is an explicit request, so the hours window and the
        // quiet break do not hold it back; reminders being switched on and the weekday rule do.
        public static string[] DueCards(DateTime previous, DateTime now, ReminderSettings settings, IEnumerable<string> knownThemeIds)
        {
            if (settings == null || knownThemeIds == null)
                throw new ArgumentException("Settings and known theme IDs are required.");
            List<string> due = new List<string>();
            if (!settings.Enabled || settings.CardSchedules == null || now <= previous || (now - previous).TotalSeconds > 15)
                return due.ToArray();
            List<KeyValuePair<DateTime, string>> hits = new List<KeyValuePair<DateTime, string>>();
            foreach (string id in knownThemeIds)
            {
                CardSchedule schedule;
                TimeSpan time;
                if (!settings.CardSchedules.TryGetValue(id, out schedule) || schedule == null || !schedule.Enabled || !TryParseClock(schedule.Time, out time))
                    continue;
                for (DateTime day = previous.Date; day <= now.Date; day = day.AddDays(1))
                {
                    if (settings.WeekdaysOnly && (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday))
                        continue;
                    DateTime fire = day + time;
                    if (fire > previous && fire <= now)
                        hits.Add(new KeyValuePair<DateTime, string>(fire, id));
                }
            }
            hits.Sort(delegate(KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b) { return a.Key.CompareTo(b.Key); });
            foreach (KeyValuePair<DateTime, string> hit in hits)
                if (!due.Contains(hit.Value))
                    due.Add(hit.Value);
            return due.ToArray();
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
