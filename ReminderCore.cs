using System;
using System.Collections.Generic;

namespace DeskHooker
{
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
            if (settings.ThemeIds == null || settings.ThemeIds.Length == 0)
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

        // Schedule-only predicate: Enabled is checked separately by IsDue.
        // All supplied DateTimes must use the caller's consistent wall-clock basis.
        public static bool IsAllowed(DateTime when, ReminderSettings settings)
        {
            if (settings.WeekdaysOnly &&
                (when.DayOfWeek == DayOfWeek.Saturday || when.DayOfWeek == DayOfWeek.Sunday))
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
            if (settings.LimitHours && candidate.TimeOfDay >= WindowEnd(settings))
                candidate = candidate.Date.AddDays(1) + WindowStart(settings);
            if (settings.LimitHours && candidate.TimeOfDay < WindowStart(settings))
                candidate = candidate.Date + WindowStart(settings);
            while (settings.WeekdaysOnly &&
                (candidate.DayOfWeek == DayOfWeek.Saturday || candidate.DayOfWeek == DayOfWeek.Sunday))
                candidate = candidate.Date.AddDays(1) + (settings.LimitHours ? WindowStart(settings) : TimeSpan.Zero);
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

    // A validated, plain-text wellness item shared by other people through the
    // repository's community/ folder. Limits MUST match validateShare() in app-core.js.
    public sealed class CommunityItem
    {
        public string Id, Name, Summary, Safety, Author;
        public string[] Instructions;
    }

    public static class CommunityContent
    {
        public const int MaxFileBytes = 6000;
        public const int MaxName = 30, MaxSummary = 60, MaxStep = 60, MaxSteps = 6, MaxSafety = 150, MaxAuthor = 20;
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
            item.Name = Text(map, "name", MaxName, true);
            item.Summary = Text(map, "summary", MaxSummary, true);
            item.Safety = Text(map, "safety", MaxSafety, true);
            item.Author = Text(map, "author", MaxAuthor, false);

            object rawSteps;
            System.Collections.IEnumerable steps = map.TryGetValue("instructions", out rawSteps) ? rawSteps as System.Collections.IEnumerable : null;
            if (steps == null || rawSteps is string)
                throw new ArgumentException("instructions must be an array.");
            List<string> list = new List<string>();
            foreach (object step in steps)
            {
                if (list.Count >= MaxSteps)
                    throw new ArgumentException("Too many instructions.");
                list.Add(Clean(step as string, MaxStep, true, "instruction"));
            }
            if (list.Count == 0)
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
