using System;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Web.Script.Serialization;
using DeskHooker;

internal static class ReminderCoreTests
{
    private static int passed;
    private static int failed;

    private static int Main()
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        Console.WriteLine("Runtime: {0}; OS: {1}", Environment.Version, Environment.OSVersion.VersionString);
        Run("Settings_DefaultsAreSafeAndDisabled", Settings_DefaultsAreSafeAndDisabled);
        Run("Next_AddsDefaultInterval", Next_AddsDefaultInterval);
        Run("Next_UsesMinutesAndPreservesPrecisionAndKind", Next_UsesMinutesAndPreservesPrecisionAndKind);
        Run("Next_ClampsBeforeStartToNine", Next_ClampsBeforeStartToNine);
        Run("Next_AddsIntervalBeforeClamping", Next_AddsIntervalBeforeClamping);
        Run("Next_EndIsExclusive", Next_EndIsExclusive);
        Run("Next_AfterHoursUsesNextDayStart", Next_AfterHoursUsesNextDayStart);
        Run("Next_UsesCustomHours", Next_UsesCustomHours);
        Run("Next_UsesMinuteWindow", Next_UsesMinuteWindow);
        Run("Next_FridayEndSkipsToMonday", Next_FridayEndSkipsToMonday);
        Run("Next_WeekendSkipsToMonday", Next_WeekendSkipsToMonday);
        Run("Next_UnlimitedHoursStillSkipsWeekends", Next_UnlimitedHoursStillSkipsWeekends);
        Run("Next_AllDaysAllowsSaturday", Next_AllDaysAllowsSaturday);
        Run("Next_UnlimitedHoursAllowsNight", Next_UnlimitedHoursAllowsNight);
        Run("IsAllowed_StartInclusive", IsAllowed_StartInclusive);
        Run("IsAllowed_EndExclusiveToTheTick", IsAllowed_EndExclusiveToTheTick);
        Run("IsAllowed_WeekendsExcluded", IsAllowed_WeekendsExcluded);
        Run("IsAllowed_UnlimitedHoursStillWeekdays", IsAllowed_UnlimitedHoursStillWeekdays);
        Run("IsAllowed_AllDaysAndAllHours", IsAllowed_AllDaysAndAllHours);
        Run("IsAllowed_End24CoversLastTick", IsAllowed_End24CoversLastTick);
        Run("IsAllowed_DoesNotDependOnEnabled", IsAllowed_DoesNotDependOnEnabled);
        Run("IsDue_FutureIsNotDue", IsDue_FutureIsNotDue);
        Run("IsDue_ExactDeadlineIsDue", IsDue_ExactDeadlineIsDue);
        Run("IsDue_OverdueIsDue", IsDue_OverdueIsDue);
        Run("IsDue_DisabledBlocked", IsDue_DisabledBlocked);
        Run("IsDue_PausedBlocked", IsDue_PausedBlocked);
        Run("IsDue_OpenPopupBlocked", IsDue_OpenPopupBlocked);
        Run("IsDue_LockedOrInactiveBlocked", IsDue_LockedOrInactiveBlocked);
        Run("IsDue_OutsideHoursBlocked", IsDue_OutsideHoursBlocked);
        Run("IsDue_WeekendBlockedWithoutHourLimit", IsDue_WeekendBlockedWithoutHourLimit);
        Run("IsDue_DismissalResetsWithoutCatchUpBurst", IsDue_DismissalResetsWithoutCatchUpBurst);
        Run("IsDue_FridayDismissalSchedulesMonday", IsDue_FridayDismissalSchedulesMonday);
        Run("NextTheme_RotatesInSelectedOrderAndWraps", NextTheme_RotatesInSelectedOrderAndWraps);
        Run("NextTheme_SingleSelectionAlwaysWrapsToZero", NextTheme_SingleSelectionAlwaysWrapsToZero);
        Run("ThemeMode_DefaultsToOrderedOne", ThemeMode_DefaultsToOrderedOne);
        Run("ThemeMode_ValidateAcceptsAllFourAndRejectsOthers", ThemeMode_ValidateAcceptsAllFourAndRejectsOthers);
        Run("PlanRound_SequentialAllKeepsListOrder", PlanRound_SequentialAllKeepsListOrder);
        Run("PlanRound_RandomAllIsPermutationAndAvoidsPrevious", PlanRound_RandomAllIsPermutationAndAvoidsPrevious);
        Run("PlanRound_OrderedOneRotatesLikeNextTheme", PlanRound_OrderedOneRotatesLikeNextTheme);
        Run("PlanRound_RandomOneStaysInSelectionAndAvoidsPrevious", PlanRound_RandomOneStaysInSelectionAndAvoidsPrevious);
        Run("PlanRound_SingleThemeAlwaysWorks", PlanRound_SingleThemeAlwaysWorks);
        Run("PlanRound_RejectsBadInput", PlanRound_RejectsBadInput);
        Run("Validate_AcceptsDefaults", delegate { Validate(new ReminderSettings(), KnownThemes()); });
        Run("Validate_AcceptsMinimumInterval", delegate { Validate(new ReminderSettings { IntervalMinutes = 1 }, KnownThemes()); });
        Run("Validate_AcceptsMaximumInterval", delegate { Validate(new ReminderSettings { IntervalMinutes = 240 }, KnownThemes()); });
        InvalidSettings("Validate_RejectsZeroInterval", delegate(ReminderSettings settings) { settings.IntervalMinutes = 0; });
        InvalidSettings("Validate_RejectsNegativeInterval", delegate(ReminderSettings settings) { settings.IntervalMinutes = -1; });
        InvalidSettings("Validate_RejectsIntervalAbove240", delegate(ReminderSettings settings) { settings.IntervalMinutes = 241; });
        Run("Validate_AcceptsFullDay", delegate { Validate(new ReminderSettings { StartHour = 0, EndHour = 24 }, KnownThemes()); });
        Run("Validate_AcceptsFirstHour", delegate { Validate(new ReminderSettings { StartHour = 0, EndHour = 1 }, KnownThemes()); });
        Run("Validate_AcceptsLastHour", delegate { Validate(new ReminderSettings { StartHour = 23, EndHour = 24 }, KnownThemes()); });
        InvalidSettings("Validate_RejectsNegativeStart", delegate(ReminderSettings settings) { settings.StartHour = -1; });
        InvalidSettings("Validate_RejectsStart24", delegate(ReminderSettings settings) { settings.StartHour = 24; });
        InvalidSettings("Validate_RejectsEndZero", delegate(ReminderSettings settings) { settings.EndHour = 0; });
        InvalidSettings("Validate_RejectsEnd25", delegate(ReminderSettings settings) { settings.EndHour = 25; });
        InvalidSettings("Validate_RejectsEqualHours", delegate(ReminderSettings settings) { settings.EndHour = settings.StartHour; });
        InvalidSettings("Validate_RejectsReversedHours", delegate(ReminderSettings settings) { settings.StartHour = 19; });
        InvalidSettings("Validate_RejectsInvalidHoursEvenWithoutHourLimit", delegate(ReminderSettings settings)
            { settings.LimitHours = false; settings.StartHour = 24; });
        Run("Validate_AcceptsBreathingMinima", delegate
            { Validate(new ReminderSettings { InhaleSeconds = 1, HoldSeconds = 0, ExhaleSeconds = 1 }, KnownThemes()); });
        Run("Validate_AcceptsBreathingMaxima", delegate
            { Validate(new ReminderSettings { InhaleSeconds = 10, HoldSeconds = 30, ExhaleSeconds = 30 }, KnownThemes()); });
        InvalidSettings("Validate_RejectsInhaleZero", delegate(ReminderSettings settings) { settings.InhaleSeconds = 0; });
        InvalidSettings("Validate_RejectsInhaleAbove10", delegate(ReminderSettings settings) { settings.InhaleSeconds = 11; });
        InvalidSettings("Validate_RejectsHoldNegative", delegate(ReminderSettings settings) { settings.HoldSeconds = -1; });
        InvalidSettings("Validate_RejectsHoldAbove30", delegate(ReminderSettings settings) { settings.HoldSeconds = 31; });
        InvalidSettings("Validate_RejectsExhaleZero", delegate(ReminderSettings settings) { settings.ExhaleSeconds = 0; });
        InvalidSettings("Validate_RejectsExhaleAbove30", delegate(ReminderSettings settings) { settings.ExhaleSeconds = 31; });
        Run("Validate_RejectsNullSettings", delegate
            { ThrowsArgument(delegate { Validate(null, KnownThemes()); }, "Null settings"); });
        Run("Validate_RejectsNullCatalog", delegate
            { ThrowsArgument(delegate { Validate(new ReminderSettings(), null); }, "Null catalog"); });
        Run("Validate_RejectsEmptyCatalog", delegate
            { ThrowsArgument(delegate { Validate(new ReminderSettings(), new string[0]); }, "Empty catalog"); });
        InvalidSettings("Validate_RejectsNullThemes", delegate(ReminderSettings settings) { settings.ThemeIds = null; });
        InvalidSettings("Validate_RejectsEmptyThemes", delegate(ReminderSettings settings) { settings.ThemeIds = new string[0]; });
        InvalidSettings("Validate_RejectsUnknownTheme", delegate(ReminderSettings settings) { settings.ThemeIds = new string[] { "missing" }; });
        InvalidSettings("Validate_RejectsDuplicateThemes", delegate(ReminderSettings settings)
            { settings.ThemeIds = new string[] { "stretch", "stretch" }; });
        InvalidSettings("Validate_RejectsNullThemeEntry", delegate(ReminderSettings settings) { settings.ThemeIds = new string[] { null }; });
        InvalidSettings("Validate_RejectsEmptyThemeEntry", delegate(ReminderSettings settings) { settings.ThemeIds = new string[] { "" }; });
        InvalidSettings("Validate_RejectsWhitespaceThemeEntry", delegate(ReminderSettings settings) { settings.ThemeIds = new string[] { " " }; });
        InvalidSettings("Validate_ThemeIdsAreOrdinalCaseSensitive", delegate(ReminderSettings settings) { settings.ThemeIds = new string[] { "Stretch" }; });
        InvalidSettings("Validate_DoesNotSilentlyTrimThemeIds", delegate(ReminderSettings settings) { settings.ThemeIds = new string[] { "stretch " }; });
        Run("Validate_AcceptsMultipleKnownThemesWithoutMutating", Validate_AcceptsMultipleKnownThemesWithoutMutating);
        Run("NextTheme_RecoversAfterSelectionShrinks", NextTheme_RecoversAfterSelectionShrinks);
        Run("NextTheme_NegativeCursorRestarts", NextTheme_NegativeCursorRestarts);
        Run("NextTheme_MaximumCursorDoesNotOverflow", NextTheme_MaximumCursorDoesNotOverflow);
        Run("NextTheme_RejectsNullSelection", NextTheme_RejectsNullSelection);
        Run("NextTheme_RejectsEmptySelection", NextTheme_RejectsEmptySelection);
        Run("Settings_ThemeArraysAreIndependent", Settings_ThemeArraysAreIndependent);
        Run("Settings_JavaScriptSerializerRoundTrip", Settings_JavaScriptSerializerRoundTrip);
        Run("Settings_PartialJsonRetainsConstructorDefaults", Settings_PartialJsonRetainsConstructorDefaults);
        Run("Api_ExactPublicScheduleSignatures", Api_ExactPublicScheduleSignatures);
        Run("Next_MinimumAndMaximumIntervalsAreMinutes", Next_MinimumAndMaximumIntervalsAreMinutes);
        Run("Next_CrossesYearBoundary", Next_CrossesYearBoundary);
        Run("Next_AlwaysChoosesEarliestAllowedCandidate", Next_AlwaysChoosesEarliestAllowedCandidate);
        Run("Community_AcceptsValidItem", Community_AcceptsValidItem);
        Run("Community_RejectsMarkupLinksAndBadIds", Community_RejectsMarkupLinksAndBadIds);
        Run("Community_EnforcesLimits", Community_EnforcesLimits);
        Run("Community_RejectsUnknownSchemaAndNonObjects", Community_RejectsUnknownSchemaAndNonObjects);
        Console.WriteLine("RESULT: {0} passed, {1} failed", passed, failed);
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error.Message);
        }
    }

    private const string Good = "{\"schema\":1,\"id\":\"u-abc\",\"name\":\"N\",\"summary\":\"S\",\"instructions\":[\"a\",\"b\"],\"safety\":\"stop if pain\",\"author\":\"me\"}";

    private static void ExpectReject(string json, string label)
    {
        try { CommunityContent.Parse(json); }
        catch (ArgumentException) { return; }
        throw new Exception(label + " should have been rejected");
    }

    private static void Community_AcceptsValidItem()
    {
        CommunityItem item = CommunityContent.Parse(Good);
        Equal("u-abc", item.Id, "id");
        Equal(2, item.Instructions.Length, "steps");
        Equal("me", item.Author, "author");
        Equal("", CommunityContent.Parse(Good.Replace(",\"author\":\"me\"", "")).Author, "optional author");
    }

    private static void Community_RejectsMarkupLinksAndBadIds()
    {
        ExpectReject(Good.Replace("\"N\"", "\"<b>x</b>\""), "markup in name");
        ExpectReject(Good.Replace("\"S\"", "\"see https://evil.example\""), "link in summary");
        ExpectReject(Good.Replace("\"S\"", "\"see www.evil.example\""), "www link");
        ExpectReject(Good.Replace("u-abc", "U_ABC"), "bad id characters");
        ExpectReject(Good.Replace("u-abc", "ab"), "short id");
        ExpectReject(Good.Replace("\"a\"", "\"line\\u0007bell\""), "control char");
    }

    private static void Community_EnforcesLimits()
    {
        ExpectReject(Good.Replace("\"N\"", "\"" + new string('x', CommunityContent.MaxName + 1) + "\""), "long name");
        Equal(CommunityContent.MaxName, CommunityContent.Parse(Good.Replace("\"N\"", "\"" + new string('x', CommunityContent.MaxName) + "\"")).Name.Length, "name at limit");
        ExpectReject(Good.Replace("[\"a\",\"b\"]", "[]"), "no steps");
        ExpectReject(Good.Replace("[\"a\",\"b\"]", "[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\"]"), "too many steps");
        ExpectReject(Good.Replace("\"stop if pain\"", "\"\""), "missing safety");
        ExpectReject(Good + new string(' ', CommunityContent.MaxFileBytes), "oversized file");
    }

    private static void Community_RejectsUnknownSchemaAndNonObjects()
    {
        ExpectReject(Good.Replace("\"schema\":1", "\"schema\":2"), "schema 2");
        ExpectReject(Good.Replace("\"schema\":1,", ""), "missing schema");
        ExpectReject("[1,2]", "array root");
        ExpectReject("not json", "garbage");
        ExpectReject("", "empty");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Object.Equals(expected, actual))
            throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }

    private static void Settings_DefaultsAreSafeAndDisabled()
    {
        Type type = Assembly.GetExecutingAssembly().GetType("DeskHooker.ReminderSettings");
        if (type == null || !type.IsPublic)
            throw new Exception("Missing public DeskHooker.ReminderSettings");
        object settings = Activator.CreateInstance(type);
        PropertyDefault(type, settings, "IntervalMinutes", 60);
        PropertyDefault(type, settings, "WeekdaysOnly", true);
        PropertyDefault(type, settings, "LimitHours", true);
        PropertyDefault(type, settings, "StartHour", 9);
        PropertyDefault(type, settings, "EndHour", 18);
        PropertyDefault(type, settings, "Enabled", false);
        PropertyDefault(type, settings, "StartAtLogin", false);
        PropertyDefault(type, settings, "InhaleSeconds", 4);
        PropertyDefault(type, settings, "HoldSeconds", 0);
        PropertyDefault(type, settings, "ExhaleSeconds", 6);
        PropertyInfo themesProperty = type.GetProperty("ThemeIds");
        if (themesProperty == null || themesProperty.PropertyType != typeof(string[]) ||
            themesProperty.GetGetMethod() == null || themesProperty.GetSetMethod() == null)
            throw new Exception("ThemeIds must be a public read/write string[] property");
        string[] themes = (string[])themesProperty.GetValue(settings, null);
        Equal(1, themes.Length, "Default theme count");
        Equal("stretch", themes[0], "Default theme");
    }

    private static DateTime Next(DateTime from, ReminderSettings settings)
    {
        return ReminderSchedule.Next(from, settings);
    }

    private static void Next_AddsDefaultInterval()
    {
        Equal(new DateTime(2026, 11, 9, 11, 0, 0),
            Next(new DateTime(2026, 11, 9, 10, 0, 0), new ReminderSettings()), "Default interval");
    }

    private static void Next_UsesMinutesAndPreservesPrecisionAndKind()
    {
        ReminderSettings settings = new ReminderSettings();
        settings.IntervalMinutes = 35;
        DateTime actual = Next(new DateTime(2026, 11, 9, 9, 1, 23, 456, DateTimeKind.Local), settings);
        Equal(new DateTime(2026, 11, 9, 9, 36, 23, 456, DateTimeKind.Local), actual, "Custom minutes");
        Equal(DateTimeKind.Local, actual.Kind, "DateTime kind");
    }

    private static void Next_ClampsBeforeStartToNine()
    {
        Equal(new DateTime(2026, 11, 9, 9, 0, 0),
            Next(new DateTime(2026, 11, 9, 7, 30, 0), new ReminderSettings()), "Before start");
    }

    private static void Next_AddsIntervalBeforeClamping()
    {
        Equal(new DateTime(2026, 11, 9, 9, 30, 0),
            Next(new DateTime(2026, 11, 9, 8, 30, 0), new ReminderSettings()), "Add first, not clamp then add");
    }

    private static void Next_EndIsExclusive()
    {
        Equal(new DateTime(2026, 11, 10, 9, 0, 0),
            Next(new DateTime(2026, 11, 9, 17, 0, 0), new ReminderSettings()), "18:00 is excluded");
    }

    private static void Next_AfterHoursUsesNextDayStart()
    {
        Equal(new DateTime(2026, 11, 10, 9, 0, 0),
            Next(new DateTime(2026, 11, 9, 18, 42, 0), new ReminderSettings()), "After end");
    }

    private static void Next_UsesCustomHours()
    {
        ReminderSettings settings = new ReminderSettings { StartHour = 13, EndHour = 15, IntervalMinutes = 30 };
        Equal(new DateTime(2026, 11, 10, 13, 0, 0),
            Next(new DateTime(2026, 11, 9, 14, 30, 0), settings), "Custom next-day start");
    }

    private static void Next_UsesMinuteWindow()
    {
        ReminderSettings settings = new ReminderSettings { StartHour = 9, StartMinute = 30, EndHour = 17, EndMinute = 45, IntervalMinutes = 20 };
        Equal(new DateTime(2026, 11, 10, 9, 30, 0), Next(new DateTime(2026, 11, 9, 17, 30, 0), settings), "Minute window next-day start");
        Equal(new DateTime(2026, 11, 9, 9, 30, 0), Next(new DateTime(2026, 11, 9, 8, 0, 0), settings), "Minute window same-day start");
        Equal(false, IsAllowed(new DateTime(2026, 11, 9, 17, 45, 0), settings), "End minute is exclusive");
        Equal(true, IsAllowed(new DateTime(2026, 11, 9, 9, 30, 0), settings), "Start minute is inclusive");
        Validate(settings, KnownThemes());
        InvalidSettings("Validate_RejectsEndBeforeStartByMinute", delegate(ReminderSettings x) { x.StartHour = 10; x.StartMinute = 30; x.EndHour = 10; x.EndMinute = 15; });
        InvalidSettings("Validate_RejectsMinute60", delegate(ReminderSettings x) { x.StartMinute = 60; });
        InvalidSettings("Validate_Rejects2430", delegate(ReminderSettings x) { x.EndHour = 24; x.EndMinute = 30; });
    }

    private static void Next_FridayEndSkipsToMonday()
    {
        DateTime actual = Next(new DateTime(2026, 11, 13, 17, 0, 0, DateTimeKind.Utc), new ReminderSettings());
        Equal(new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc), actual, "Friday to Monday");
        Equal(DateTimeKind.Utc, actual.Kind, "Clamped kind");
    }

    private static void Next_WeekendSkipsToMonday()
    {
        Equal(new DateTime(2026, 11, 16, 9, 0, 0),
            Next(new DateTime(2026, 11, 14, 12, 0, 0), new ReminderSettings()), "Saturday");
        Equal(new DateTime(2026, 11, 16, 9, 0, 0),
            Next(new DateTime(2026, 11, 15, 6, 0, 0), new ReminderSettings()), "Sunday");
    }

    private static void Next_UnlimitedHoursStillSkipsWeekends()
    {
        ReminderSettings settings = new ReminderSettings { LimitHours = false };
        Equal(new DateTime(2026, 11, 16, 0, 0, 0),
            Next(new DateTime(2026, 11, 13, 23, 30, 0), settings), "Unlimited hours still weekdays only");
    }

    private static void Next_AllDaysAllowsSaturday()
    {
        ReminderSettings settings = new ReminderSettings { WeekdaysOnly = false };
        Equal(new DateTime(2026, 11, 14, 9, 0, 0),
            Next(new DateTime(2026, 11, 13, 17, 0, 0), settings), "All days");
    }

    private static void Next_UnlimitedHoursAllowsNight()
    {
        ReminderSettings settings = new ReminderSettings { LimitHours = false };
        Equal(new DateTime(2026, 11, 10, 0, 30, 0),
            Next(new DateTime(2026, 11, 9, 23, 30, 0), settings), "No hour limit");
    }

    private static bool IsAllowed(DateTime when, ReminderSettings settings)
    {
        return ReminderSchedule.IsAllowed(when, settings);
    }

    private static void IsAllowed_StartInclusive()
    {
        DateTime start = new DateTime(2026, 11, 9, 9, 0, 0);
        Equal(true, IsAllowed(start, new ReminderSettings()), "Start is included");
        Equal(false, IsAllowed(start.AddTicks(-1), new ReminderSettings()), "Before start is excluded");
    }

    private static void IsAllowed_EndExclusiveToTheTick()
    {
        DateTime end = new DateTime(2026, 11, 9, 18, 0, 0);
        Equal(true, IsAllowed(end.AddTicks(-1), new ReminderSettings()), "Last allowed tick");
        Equal(false, IsAllowed(end, new ReminderSettings()), "End is excluded");
    }

    private static void IsAllowed_WeekendsExcluded()
    {
        Equal(false, IsAllowed(new DateTime(2026, 11, 14, 12, 0, 0), new ReminderSettings()), "Saturday");
        Equal(false, IsAllowed(new DateTime(2026, 11, 15, 12, 0, 0), new ReminderSettings()), "Sunday");
    }

    private static void IsAllowed_UnlimitedHoursStillWeekdays()
    {
        ReminderSettings settings = new ReminderSettings { LimitHours = false };
        Equal(true, IsAllowed(new DateTime(2026, 11, 13, 23, 59, 0), settings), "Friday night");
        Equal(false, IsAllowed(new DateTime(2026, 11, 14, 0, 0, 0), settings), "Saturday midnight");
        Equal(false, IsAllowed(new DateTime(2026, 11, 15, 23, 59, 0), settings), "Sunday night");
        Equal(true, IsAllowed(new DateTime(2026, 11, 16, 0, 0, 0), settings), "Monday midnight");
    }

    private static void IsAllowed_AllDaysAndAllHours()
    {
        ReminderSettings settings = new ReminderSettings { WeekdaysOnly = false, LimitHours = false };
        Equal(true, IsAllowed(new DateTime(2026, 11, 14, 2, 0, 0), settings), "Saturday night");
    }

    private static void IsAllowed_End24CoversLastTick()
    {
        ReminderSettings settings = new ReminderSettings { StartHour = 0, EndHour = 24 };
        Equal(true, IsAllowed(new DateTime(2026, 11, 9, 0, 0, 0), settings), "Midnight");
        Equal(true, IsAllowed(new DateTime(2026, 11, 10, 0, 0, 0).AddTicks(-1), settings), "End 24");
        Equal(new DateTime(2026, 11, 10, 0, 30, 0),
            Next(new DateTime(2026, 11, 9, 23, 30, 0), settings), "End 24 crosses midnight normally");
    }

    private static void IsAllowed_DoesNotDependOnEnabled()
    {
        Equal(true, IsAllowed(new DateTime(2026, 11, 9, 12, 0, 0), new ReminderSettings()),
            "Schedule calculation is independent of disabled state");
    }

    private static bool IsDue(DateTime now, DateTime next, ReminderSettings settings,
        bool popupOpen, bool paused, bool sessionActive)
    {
        return ReminderSchedule.IsDue(now, next, settings, popupOpen, paused, sessionActive);
    }

    private static void IsDue_FutureIsNotDue()
    {
        DateTime next = new DateTime(2026, 11, 9, 12, 0, 0);
        Equal(false, IsDue(next.AddTicks(-1), next, new ReminderSettings { Enabled = true }, false, false, true),
            "Future deadline");
    }

    private static void IsDue_ExactDeadlineIsDue()
    {
        DateTime next = new DateTime(2026, 11, 9, 12, 0, 0);
        Equal(true, IsDue(next, next, new ReminderSettings { Enabled = true }, false, false, true), "Exact deadline");
    }

    private static void IsDue_OverdueIsDue()
    {
        Equal(true, IsDue(new DateTime(2026, 11, 9, 14, 0, 0), new DateTime(2026, 11, 9, 12, 0, 0),
            new ReminderSettings { Enabled = true }, false, false, true), "Overdue once");
    }

    private static void IsDue_DisabledBlocked()
    {
        DateTime now = new DateTime(2026, 11, 9, 12, 0, 0);
        Equal(false, IsDue(now, now, new ReminderSettings(), false, false, true), "Disabled by default");
    }

    private static void IsDue_PausedBlocked()
    {
        DateTime now = new DateTime(2026, 11, 9, 12, 0, 0);
        Equal(false, IsDue(now, now, new ReminderSettings { Enabled = true }, false, true, true), "Paused");
    }

    private static void IsDue_OpenPopupBlocked()
    {
        DateTime now = new DateTime(2026, 11, 9, 12, 0, 0);
        Equal(false, IsDue(now, now, new ReminderSettings { Enabled = true }, true, false, true), "Duplicate popup");
    }

    private static void IsDue_LockedOrInactiveBlocked()
    {
        DateTime now = new DateTime(2026, 11, 9, 12, 0, 0);
        Equal(false, IsDue(now, now, new ReminderSettings { Enabled = true }, false, false, false), "Inactive session");
    }

    private static void IsDue_OutsideHoursBlocked()
    {
        ReminderSettings settings = new ReminderSettings { Enabled = true };
        Equal(false, IsDue(new DateTime(2026, 11, 9, 8, 59, 0), new DateTime(2026, 11, 6, 17, 0, 0),
            settings, false, false, true), "Before hours despite stale deadline");
        Equal(false, IsDue(new DateTime(2026, 11, 9, 18, 0, 0), new DateTime(2026, 11, 9, 17, 0, 0),
            settings, false, false, true), "End excluded despite overdue");
    }

    private static void IsDue_WeekendBlockedWithoutHourLimit()
    {
        Equal(false, IsDue(new DateTime(2026, 11, 14, 12, 0, 0), new DateTime(2026, 11, 13, 12, 0, 0),
            new ReminderSettings { Enabled = true, LimitHours = false }, false, false, true), "Weekend");
    }

    private static void IsDue_DismissalResetsWithoutCatchUpBurst()
    {
        ReminderSettings settings = new ReminderSettings { Enabled = true };
        DateTime stale = new DateTime(2026, 11, 6, 10, 0, 0);
        DateTime resumed = new DateTime(2026, 11, 9, 12, 0, 0);
        Equal(true, IsDue(resumed, stale, settings, false, false, true), "At most one overdue popup starts");
        for (int seconds = 0; seconds < 5; seconds++)
            Equal(false, IsDue(resumed.AddSeconds(seconds), stale, settings, true, false, true), "Already open");
        DateTime dismissed = new DateTime(2026, 11, 9, 12, 17, 0);
        DateTime next = Next(dismissed, settings);
        Equal(new DateTime(2026, 11, 9, 13, 17, 0), next, "Dismissal, not stale due time, is the anchor");
        Equal(false, IsDue(dismissed, next, settings, false, false, true), "No catch-up popup on dismissal");
        Equal(false, IsDue(next.AddTicks(-1), next, settings, false, false, true), "Full new interval");
        Equal(true, IsDue(next, next, settings, false, false, true), "Next normal popup");
    }

    private static void IsDue_FridayDismissalSchedulesMonday()
    {
        Equal(new DateTime(2026, 11, 16, 9, 0, 0),
            Next(new DateTime(2026, 11, 13, 17, 15, 0), new ReminderSettings()), "Friday dismissal");
    }

    private static string NextTheme(string[] selected, ref int cursor)
    {
        return ReminderSchedule.NextTheme(selected, ref cursor);
    }

    private static void NextTheme_RotatesInSelectedOrderAndWraps()
    {
        string[] selected = new string[] { "eyes", "stretch", "breathe" };
        int cursor = 0;
        Equal("eyes", NextTheme(selected, ref cursor), "First");
        Equal(1, cursor, "Advance");
        Equal("stretch", NextTheme(selected, ref cursor), "Second");
        Equal("breathe", NextTheme(selected, ref cursor), "Third");
        Equal(0, cursor, "Wrap");
        Equal("eyes", NextTheme(selected, ref cursor), "Restart");
        Equal("eyes", selected[0], "Caller selection order is not changed");
    }

    private static void NextTheme_SingleSelectionAlwaysWrapsToZero()
    {
        int cursor = 0;
        Equal("stretch", NextTheme(new string[] { "stretch" }, ref cursor), "Single theme");
        Equal(0, cursor, "Single wrap");
    }

    private static void NextTheme_RecoversAfterSelectionShrinks()
    {
        int cursor = 2;
        Equal("stretch", NextTheme(new string[] { "stretch", "eyes" }, ref cursor), "Normalize to selected length");
        Equal(1, cursor, "Advance normalized cursor");
    }

    private static void NextTheme_NegativeCursorRestarts()
    {
        int cursor = -1;
        Equal("stretch", NextTheme(new string[] { "stretch", "eyes" }, ref cursor), "Restart negative cursor");
        Equal(1, cursor, "Advance restarted cursor");
    }

    private static void NextTheme_MaximumCursorDoesNotOverflow()
    {
        int cursor = Int32.MaxValue;
        Equal("stretch", NextTheme(new string[] { "stretch" }, ref cursor), "Normalize before increment");
        Equal(0, cursor, "No overflow");
    }

    private static void ThemeMode_DefaultsToOrderedOne()
    {
        Equal("ordered-one", new ReminderSettings().ThemeMode, "Default mode keeps the old one-at-a-time rotation");
        Equal("ordered-one", new JavaScriptSerializer().Deserialize<ReminderSettings>("{\"IntervalMinutes\":25}").ThemeMode, "Old config without ThemeMode");
    }

    private static void ThemeMode_ValidateAcceptsAllFourAndRejectsOthers()
    {
        foreach (string mode in new string[] { "sequential-all", "random-all", "ordered-one", "random-one" })
            Validate(new ReminderSettings { ThemeMode = mode }, KnownThemes());
        foreach (string mode in new string[] { null, "", "Random-One", "all", "random-one " })
        {
            string bad = mode;
            ThrowsArgument(delegate { Validate(new ReminderSettings { ThemeMode = bad }, KnownThemes()); }, "Mode '" + mode + "'");
        }
    }

    private static void PlanRound_SequentialAllKeepsListOrder()
    {
        int cursor = 0;
        string[] selected = new string[] { "c", "a", "b" };
        string[] round = ThemeRotation.PlanRound(selected, "sequential-all", ref cursor, new Random(1), null);
        Equal("c,a,b", String.Join(",", round), "List order");
        round[0] = "x";
        Equal("c", selected[0], "Plan must be a copy");
        Equal(0, cursor, "Cursor untouched");
    }

    private static void PlanRound_RandomAllIsPermutationAndAvoidsPrevious()
    {
        string[] selected = new string[] { "a", "b", "c", "d" };
        bool changedOrder = false;
        for (int seed = 0; seed < 200; seed++)
        {
            int cursor = 0;
            string[] round = ThemeRotation.PlanRound(selected, "random-all", ref cursor, new Random(seed), "a");
            string[] sorted = (string[])round.Clone(); Array.Sort(sorted, StringComparer.Ordinal);
            Equal("a,b,c,d", String.Join(",", sorted), "Every theme exactly once");
            if (round[0] == "a") throw new Exception("Round started with the previous theme at seed " + seed);
            if (String.Join(",", round) != "a,b,c,d") changedOrder = true;
        }
        Equal(true, changedOrder, "Shuffle actually reorders");
    }

    private static void PlanRound_OrderedOneRotatesLikeNextTheme()
    {
        int cursor = 0;
        string[] selected = new string[] { "x", "y", "z" };
        string seen = "";
        for (int i = 0; i < 4; i++)
        {
            string[] round = ThemeRotation.PlanRound(selected, "ordered-one", ref cursor, new Random(1), null);
            Equal(1, round.Length, "One theme per reminder");
            seen += round[0];
        }
        Equal("xyzx", seen, "Rotation and wrap");
    }

    private static void PlanRound_RandomOneStaysInSelectionAndAvoidsPrevious()
    {
        string[] selected = new string[] { "p", "q", "r" };
        Random random = new Random(7);
        int cursor = 0;
        string previous = "p";
        System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 300; i++)
        {
            string[] round = ThemeRotation.PlanRound(selected, "random-one", ref cursor, random, previous);
            Equal(1, round.Length, "One theme per reminder");
            if (Array.IndexOf(selected, round[0]) < 0) throw new Exception("Picked an unselected theme: " + round[0]);
            if (round[0] == previous) throw new Exception("Repeated the previous theme at step " + i);
            seen.Add(round[0]); previous = round[0];
        }
        Equal(3, seen.Count, "All themes eventually appear");
        Equal(0, cursor, "Cursor untouched");
    }

    private static void PlanRound_SingleThemeAlwaysWorks()
    {
        foreach (string mode in new string[] { "sequential-all", "random-all", "ordered-one", "random-one" })
        {
            int cursor = 0;
            string[] round = ThemeRotation.PlanRound(new string[] { "only" }, mode, ref cursor, new Random(3), "only");
            Equal("only", String.Join(",", round), mode);
        }
    }

    private static void PlanRound_RejectsBadInput()
    {
        int cursor = 0;
        ThrowsArgument(delegate { int c = 0; ThemeRotation.PlanRound(null, "random-all", ref c, new Random(1), null); }, "Null selection");
        ThrowsArgument(delegate { int c = 0; ThemeRotation.PlanRound(new string[0], "random-all", ref c, new Random(1), null); }, "Empty selection");
        ThrowsArgument(delegate { int c = 0; ThemeRotation.PlanRound(new string[] { "a" }, "nope", ref c, new Random(1), null); }, "Unknown mode");
        ThrowsArgument(delegate { int c = 0; ThemeRotation.PlanRound(new string[] { "a" }, "random-all", ref c, null, null); }, "No random source");
        Equal(0, cursor, "Unused");
    }

    private static void NextTheme_RejectsNullSelection()
    {
        int cursor = 4;
        ThrowsArgument(delegate { NextTheme(null, ref cursor); }, "Null selection");
        Equal(4, cursor, "Failure preserves cursor");
    }

    private static void NextTheme_RejectsEmptySelection()
    {
        int cursor = 4;
        ThrowsArgument(delegate { NextTheme(new string[0], ref cursor); }, "Empty selection");
        Equal(4, cursor, "Failure preserves cursor");
    }

    private static void Validate_AcceptsMultipleKnownThemesWithoutMutating()
    {
        ReminderSettings settings = new ReminderSettings { ThemeIds = new string[] { "breathe", "stretch", "eyes" } };
        string[] selected = settings.ThemeIds;
        Validate(settings, KnownThemes());
        Equal(selected, settings.ThemeIds, "Selection reference");
        Equal("breathe", selected[0], "Selection order");
        Equal(false, settings.Enabled, "Validation does not enable reminders");
        Equal(false, settings.StartAtLogin, "Validation does not enable startup");
    }

    private static IEnumerable<string> KnownThemes()
    {
        yield return "stretch";
        yield return "eyes";
        yield return "breathe";
    }

    private static void Validate(ReminderSettings settings, IEnumerable<string> knownThemeIds)
    {
        ReminderSchedule.Validate(settings, knownThemeIds);
    }

    private static void Settings_ThemeArraysAreIndependent()
    {
        ReminderSettings first = new ReminderSettings();
        ReminderSettings second = new ReminderSettings();
        first.ThemeIds[0] = "eyes";
        Equal("stretch", second.ThemeIds[0], "One settings instance must not mutate another");
    }

    private static void Settings_JavaScriptSerializerRoundTrip()
    {
        ReminderSettings original = new ReminderSettings
        {
            IntervalMinutes = 37, WeekdaysOnly = false, LimitHours = false,
            StartHour = 2, EndHour = 23, ThemeIds = new string[] { "eyes", "breathe" }, ThemeMode = "random-all",
            Enabled = true, StartAtLogin = true, InhaleSeconds = 5, HoldSeconds = 7, ExhaleSeconds = 9
        };
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        ReminderSettings restored = serializer.Deserialize<ReminderSettings>(serializer.Serialize(original));
        foreach (PropertyInfo property in typeof(ReminderSettings).GetProperties())
        {
            if (property.Name == "ThemeIds")
                Equal(String.Join(",", original.ThemeIds), String.Join(",", restored.ThemeIds), "ThemeIds round trip");
            else
                Equal(property.GetValue(original, null), property.GetValue(restored, null), property.Name + " round trip");
        }
        Validate(restored, KnownThemes());
    }

    private static void Settings_PartialJsonRetainsConstructorDefaults()
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        ReminderSettings settings = serializer.Deserialize<ReminderSettings>("{\"IntervalMinutes\":25}");
        Equal(25, settings.IntervalMinutes, "Saved interval");
        Equal(false, settings.Enabled, "Not enabled until explicitly saved");
        Equal(false, settings.StartAtLogin, "No implicit startup");
        Equal(true, settings.WeekdaysOnly, "Default weekdays");
        Equal(true, settings.LimitHours, "Default hours enabled");
        Equal(9, settings.StartHour, "Default start");
        Equal(18, settings.EndHour, "Default end");
        Equal("stretch", settings.ThemeIds[0], "Default theme");
        Equal(4, settings.InhaleSeconds, "Default inhale");
        Equal(0, settings.HoldSeconds, "Default hold");
        Equal(6, settings.ExhaleSeconds, "Default exhale");
        Validate(settings, KnownThemes());
    }

    private static void Api_ExactPublicScheduleSignatures()
    {
        Type type = typeof(ReminderSchedule);
        Equal(true, type.IsPublic && type.IsAbstract && type.IsSealed, "Public static schedule class");
        Equal("DeskHooker", type.Namespace, "Namespace");
        Equal(5, type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Length,
            "Only the five requested public methods");
        Signature("Validate", typeof(void), typeof(ReminderSettings), typeof(IEnumerable<string>));
        Signature("IsAllowed", typeof(bool), typeof(DateTime), typeof(ReminderSettings));
        Signature("Next", typeof(DateTime), typeof(DateTime), typeof(ReminderSettings));
        Signature("IsDue", typeof(bool), typeof(DateTime), typeof(DateTime), typeof(ReminderSettings),
            typeof(bool), typeof(bool), typeof(bool));
        Signature("NextTheme", typeof(string), typeof(string[]), typeof(int).MakeByRefType());
    }

    private static void Signature(string name, Type returnType, params Type[] parameters)
    {
        MethodInfo method = typeof(ReminderSchedule).GetMethod(name, parameters);
        if (method == null || !method.IsPublic || !method.IsStatic || method.ReturnType != returnType)
            throw new Exception("Incorrect public API signature: " + name);
    }

    private static void Next_MinimumAndMaximumIntervalsAreMinutes()
    {
        DateTime from = new DateTime(2026, 11, 9, 9, 0, 0);
        Equal(new DateTime(2026, 11, 9, 9, 1, 0), Next(from, new ReminderSettings { IntervalMinutes = 1 }), "One minute");
        Equal(new DateTime(2026, 11, 9, 13, 0, 0), Next(from, new ReminderSettings { IntervalMinutes = 240 }), "240 minutes");
    }

    private static void Next_CrossesYearBoundary()
    {
        Equal(new DateTime(2027, 1, 1, 9, 0, 0),
            Next(new DateTime(2026, 12, 31, 17, 0, 0), new ReminderSettings()), "Year rollover without holiday rules");
    }

    private static void Next_AlwaysChoosesEarliestAllowedCandidate()
    {
        DateTime origin = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Unspecified);
        int[] intervals = new int[] { 1, 60, 240 };
        int[] starts = new int[] { 0, 9, 23 };
        int[] ends = new int[] { 24, 18, 24 };
        for (int days = 0; days < 12; days++)
        for (int hour = 0; hour < 24; hour += 2)
        for (int flags = 0; flags < 4; flags++)
        for (int window = 0; window < starts.Length; window++)
        foreach (int interval in intervals)
        {
            ReminderSettings settings = new ReminderSettings
            {
                IntervalMinutes = interval, WeekdaysOnly = (flags & 1) != 0, LimitHours = (flags & 2) != 0,
                StartHour = starts[window], EndHour = ends[window]
            };
            DateTime from = origin.AddDays(days).AddHours(hour).AddSeconds(17);
            DateTime candidate = from.AddMinutes(interval);
            DateTime actual = Next(from, settings);
            Equal(true, actual >= candidate, "Next must not precede the full interval");
            Equal(true, IsAllowed(actual, settings), "Next must be allowed");
            Equal(from.Kind, actual.Kind, "Sweep preserves kind");
            if (actual > candidate)
                Equal(false, IsAllowed(actual.AddTicks(-1), settings), "Clamping must choose the first allowed instant");
            else
                Equal(true, IsAllowed(candidate, settings), "Allowed candidate is not shifted");
        }
    }

    private static void ThrowsArgument(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception(message + ": expected ArgumentException");
    }

    private static void InvalidSettings(string name, Action<ReminderSettings> change)
    {
        Run(name, delegate
        {
            ReminderSettings settings = new ReminderSettings();
            change(settings);
            ThrowsArgument(delegate { Validate(settings, KnownThemes()); }, name);
        });
    }

    private static void PropertyDefault<T>(Type type, object settings, string name, T expected)
    {
        PropertyInfo property = type.GetProperty(name);
        if (property == null || property.PropertyType != typeof(T) ||
            property.GetGetMethod() == null || property.GetSetMethod() == null)
            throw new Exception(name + " must be a public read/write " + typeof(T).Name + " property");
        Equal(expected, (T)property.GetValue(settings, null), name);
    }
}
