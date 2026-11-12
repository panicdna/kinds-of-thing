using System;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        Run("Next_BeforeStartTakesFirstSlot", Next_BeforeStartTakesFirstSlot);
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
        Run("VariantModes_DefaultsToEmptyAndOldConfigsStillLoad", VariantModes_DefaultsToEmptyAndOldConfigsStillLoad);
        Run("VariantModes_ValidateAcceptsRandomAndSequentialOnly", VariantModes_ValidateAcceptsRandomAndSequentialOnly);
        Run("VariantModes_JavaScriptSerializerRoundTrip", VariantModes_JavaScriptSerializerRoundTrip);
        Run("LoopModes_DefaultsToEmptyAndOldConfigsStillLoad", LoopModes_DefaultsToEmptyAndOldConfigsStillLoad);
        Run("LoopModes_ValidateAcceptsOnAndOffOnly", LoopModes_ValidateAcceptsOnAndOffOnly);
        Run("LoopModes_JavaScriptSerializerRoundTrip", LoopModes_JavaScriptSerializerRoundTrip);
        Run("Schedules_DefaultsToEmptyAndOldConfigsStillLoad", Schedules_DefaultsToEmptyAndOldConfigsStillLoad);
        Run("Schedules_TryParseClockAcceptsDayTimesOnly", Schedules_TryParseClockAcceptsDayTimesOnly);
        Run("Schedules_ValidateAcceptsKnownCardsWithDayTimes", Schedules_ValidateAcceptsKnownCardsWithDayTimes);
        Run("Schedules_TimeOnlySelectionIsValidAndRoundTrips", Schedules_TimeOnlySelectionIsValidAndRoundTrips);
        Run("ThemeMode_DefaultsToOrderedOne", ThemeMode_DefaultsToOrderedOne);
        Run("DefaultPreset_MatchesTheAnnouncedPreset", DefaultPreset_MatchesTheAnnouncedPreset);
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
        Run("Validate_AcceptsKnoxLeadMinutesMinimum", delegate { Validate(new ReminderSettings { KnoxLeadMinutes = 0 }, KnownThemes()); });
        Run("Validate_AcceptsKnoxLeadMinutesMaximum", delegate { Validate(new ReminderSettings { KnoxLeadMinutes = 60 }, KnownThemes()); });
        InvalidSettings("Validate_RejectsKnoxLeadMinutesNegative", delegate(ReminderSettings settings) { settings.KnoxLeadMinutes = -1; });
        InvalidSettings("Validate_RejectsKnoxLeadMinutesAbove60", delegate(ReminderSettings settings) { settings.KnoxLeadMinutes = 61; });
        Run("Validate_AcceptsEveryKnoxAutoCloseMode", delegate
        {
            foreach (string mode in new string[] { ReminderSettings.KnoxAutoCloseNone, ReminderSettings.KnoxAutoCloseStart, ReminderSettings.KnoxAutoCloseAfter, ReminderSettings.KnoxAutoCloseEnd })
                Validate(new ReminderSettings { KnoxAutoClose = mode }, KnownThemes());
        });
        InvalidSettings("Validate_RejectsUnknownKnoxAutoClose", delegate(ReminderSettings settings) { settings.KnoxAutoClose = "later"; });
        InvalidSettings("Validate_RejectsNullKnoxAutoClose", delegate(ReminderSettings settings) { settings.KnoxAutoClose = null; });
        InvalidSettings("Validate_RejectsKnoxAutoCloseMinutesZero", delegate(ReminderSettings settings) { settings.KnoxAutoCloseMinutes = 0; });
        InvalidSettings("Validate_RejectsKnoxAutoCloseMinutesAbove240", delegate(ReminderSettings settings) { settings.KnoxAutoCloseMinutes = 241; });
        Run("Validate_AcceptsIdleMinutesBounds", delegate
        {
            Validate(new ReminderSettings { PauseWhenIdle = true, IdleMinutes = 1 }, KnownThemes());
            Validate(new ReminderSettings { PauseWhenIdle = true, IdleMinutes = 240 }, KnownThemes());
        });
        InvalidSettings("Validate_RejectsIdleMinutesZero", delegate(ReminderSettings settings) { settings.IdleMinutes = 0; });
        InvalidSettings("Validate_RejectsIdleMinutesAbove240", delegate(ReminderSettings settings) { settings.IdleMinutes = 241; });
        Run("Away_OffByDefaultAndOnlyTripsAtTheThreshold", delegate
        {
            Equal(false, ReminderSchedule.IsAway(TimeSpan.FromMinutes(999), new ReminderSettings()), "Off by default");
            Equal(false, ReminderSchedule.IsAway(TimeSpan.FromMinutes(9), new ReminderSettings { PauseWhenIdle = true, IdleMinutes = 10 }), "Just under the threshold is not away");
            Equal(true, ReminderSchedule.IsAway(TimeSpan.FromMinutes(10), new ReminderSettings { PauseWhenIdle = true, IdleMinutes = 10 }), "At the threshold counts as away");
            Equal(true, ReminderSchedule.IsAway(TimeSpan.FromMinutes(11), new ReminderSettings { PauseWhenIdle = true, IdleMinutes = 10 }), "Past the threshold is away");
        });
        Run("Validate_AcceptsEveryAppearance", delegate
        {
            Validate(new ReminderSettings { Appearance = ReminderSettings.AppearanceLight }, KnownThemes());
            Validate(new ReminderSettings { Appearance = ReminderSettings.AppearanceDark }, KnownThemes());
            Validate(new ReminderSettings { Appearance = ReminderSettings.AppearanceSystem }, KnownThemes());
        });
        InvalidSettings("Validate_RejectsUnknownAppearance", delegate(ReminderSettings settings) { settings.Appearance = "auto"; });
        InvalidSettings("Validate_RejectsNullAppearance", delegate(ReminderSettings settings) { settings.Appearance = null; });
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
        Run("Next_AbsoluteTimetableStaysOnTheGrid", Next_AbsoluteTimetableStaysOnTheGrid);
        Run("Next_CrossesYearBoundary", Next_CrossesYearBoundary);
        Run("Next_AlwaysChoosesEarliestAllowedCandidate", Next_AlwaysChoosesEarliestAllowedCandidate);
        Run("Community_AcceptsValidItem", Community_AcceptsValidItem);
        Run("Community_RejectsMarkupLinksAndBadIds", Community_RejectsMarkupLinksAndBadIds);
        Run("Community_EnforcesLimits", Community_EnforcesLimits);
        Run("Community_RejectsUnknownSchemaAndNonObjects", Community_RejectsUnknownSchemaAndNonObjects);
        Run("Break_IsOffByDefaultAndChangesNothing", Break_IsOffByDefaultAndChangesNothing);
        Run("Break_PostponesAReminderThatLandsInsideIt", Break_PostponesAReminderThatLandsInsideIt);
        Run("Break_StartIsInsideEndIsOutside", Break_StartIsInsideEndIsOutside);
        Run("Break_ThatEndsAfterTheWindowRollsToTomorrow", Break_ThatEndsAfterTheWindowRollsToTomorrow);
        Run("Break_WorksWithoutAWindowAndOnWeekends", Break_WorksWithoutAWindowAndOnWeekends);
        Run("Break_IsAllowedHonoursTheBreak", Break_IsAllowedHonoursTheBreak);
        Run("Break_ValidateRejectsBadRanges", Break_ValidateRejectsBadRanges);
        Run("Breaks_ListSuppliesSeveralQuietPeriods", Breaks_ListSuppliesSeveralQuietPeriods);
        Run("Breaks_EmptyListMeansNoQuietPeriod", Breaks_EmptyListMeansNoQuietPeriod);
        Run("Breaks_ValidateRejectsCoverageByTwoBreaks", Breaks_ValidateRejectsCoverageByTwoBreaks);
        Run("Breaks_ValidateRejectsTooManyEntries", Breaks_ValidateRejectsTooManyEntries);
        Run("Community_AcceptsYouTubeVideoIds", Community_AcceptsYouTubeVideoIds);
        Run("Community_RejectsBadVideos", Community_RejectsBadVideos);
        Run("Community_VideoOnlyItemMayOmitInstructions", Community_VideoOnlyItemMayOmitInstructions);
        Run("Community_IconMustBeOneOfTheOfferedSymbols", Community_IconMustBeOneOfTheOfferedSymbols);
        Run("Community_IdIndexIsReservedForTheListFile", Community_IdIndexIsReservedForTheListFile);
        Run("Calendar_ParseIcsReadsUidTitleAndLocalStart", Calendar_ParseIcsReadsUidTitleAndLocalStart);
        Run("Calendar_ParseIcsConvertsUtcToLocal", Calendar_ParseIcsConvertsUtcToLocal);
        Run("Calendar_ParseIcsSkipsIncompleteEvents", Calendar_ParseIcsSkipsIncompleteEvents);
        Run("Calendar_ParseIcsUnescapesSummary", Calendar_ParseIcsUnescapesSummary);
        Run("Calendar_ParseIcsOfEmptyOrNullTextIsEmpty", Calendar_ParseIcsOfEmptyOrNullTextIsEmpty);
        Run("Calendar_DueEntriesFiresWithinLeadWindow", Calendar_DueEntriesFiresWithinLeadWindow);
        Run("Calendar_DueEntriesNotYetDueIsExcluded", Calendar_DueEntriesNotYetDueIsExcluded);
        Run("Calendar_DueEntriesAfterStartIsExcluded", Calendar_DueEntriesAfterStartIsExcluded);
        Run("Calendar_DueEntriesAlreadyFiredIsExcluded", Calendar_DueEntriesAlreadyFiredIsExcluded);
        Run("Calendar_DueEntriesRescheduledMeetingIsDueAgain", Calendar_DueEntriesRescheduledMeetingIsDueAgain);
        Run("Calendar_DueEntriesRejectsNullArguments", Calendar_DueEntriesRejectsNullArguments);
        Run("Calendar_ParseIcsReadsOptionalEnd", Calendar_ParseIcsReadsOptionalEnd);
        Run("KnoxSync_AppendLogTrimsToTheLimitKeepingNewest", KnoxSync_AppendLogTrimsToTheLimitKeepingNewest);
        Run("KnoxSync_AppendLogRejectsNullArguments", KnoxSync_AppendLogRejectsNullArguments);
        Run("KnoxSync_FirstLineSkipsBlanksAndTruncates", KnoxSync_FirstLineSkipsBlanksAndTruncates);
        Run("KnoxSync_ParseSchedulesReadsSubjectStartAndScheduleId", KnoxSync_ParseSchedulesReadsSubjectStartAndScheduleId);
        Run("KnoxSync_ParseSchedulesEndIsOptional", KnoxSync_ParseSchedulesEndIsOptional);
        Run("KnoxSync_BuildIcsWritesEndWhenPresentAndOmitsItWhenNot", KnoxSync_BuildIcsWritesEndWhenPresentAndOmitsItWhenNot);
        Run("KnoxSync_ParseSchedulesIgnoresAttendeesAndOtherFields", KnoxSync_ParseSchedulesIgnoresAttendeesAndOtherFields);
        Run("KnoxSync_ParseSchedulesSkipsEntriesMissingSubjectOrStart", KnoxSync_ParseSchedulesSkipsEntriesMissingSubjectOrStart);
        Run("KnoxSync_ParseSchedulesOfEmptyArrayIsEmpty", KnoxSync_ParseSchedulesOfEmptyArrayIsEmpty);
        Run("KnoxSync_ParseSchedulesRejectsMissingSchedulesArray", KnoxSync_ParseSchedulesRejectsMissingSchedulesArray);
        Run("KnoxSync_ParseSchedulesRejectsInvalidJson", KnoxSync_ParseSchedulesRejectsInvalidJson);
        Run("KnoxSync_ParseSchedulesRejectsNullOrEmptyJson", KnoxSync_ParseSchedulesRejectsNullOrEmptyJson);
        Run("KnoxSync_ToDtStampConvertsOffsetAndUtcTimes", KnoxSync_ToDtStampConvertsOffsetAndUtcTimes);
        Run("KnoxSync_ToDtStampRejectsAllDayAndUnreadableStarts", KnoxSync_ToDtStampRejectsAllDayAndUnreadableStarts);
        Run("KnoxSync_StartDisplayShowsWallClockForOffsetStarts", KnoxSync_StartDisplayShowsWallClockForOffsetStarts);
        Run("KnoxSync_StartDisplayConvertsUtcZStartsToPcLocalTime", KnoxSync_StartDisplayConvertsUtcZStartsToPcLocalTime);
        Run("KnoxSync_SummarizeSchedulesListsTimesAndSubjects", KnoxSync_SummarizeSchedulesListsTimesAndSubjects);
        Run("KnoxSync_SummarizeSchedulesShowsSubjectOnlyWithoutAClockTime", KnoxSync_SummarizeSchedulesShowsSubjectOnlyWithoutAClockTime);
        Run("KnoxSync_SummarizeSchedulesOfNothingIsEmpty", KnoxSync_SummarizeSchedulesOfNothingIsEmpty);
        Run("KnoxSync_SummarizeSchedulesTruncatesLongLists", KnoxSync_SummarizeSchedulesTruncatesLongLists);
        Run("KnoxSync_SummarizeSchedulesRejectsBadArguments", KnoxSync_SummarizeSchedulesRejectsBadArguments);
        Run("KnoxSync_BuildIcsWritesFixedFormatWithEscaping", KnoxSync_BuildIcsWritesFixedFormatWithEscaping);
        Run("KnoxSync_BuildIcsSkipsAllDayEntries", KnoxSync_BuildIcsSkipsAllDayEntries);
        Run("KnoxSync_BuildIcsOfEmptyListIsAnEventlessCalendar", KnoxSync_BuildIcsOfEmptyListIsAnEventlessCalendar);
        Run("KnoxSync_BuildIcsFallsBackToATimeBasedUidWithoutScheduleId", KnoxSync_BuildIcsFallsBackToATimeBasedUidWithoutScheduleId);
        Run("KnoxSync_BuildIcsRejectsNullSchedules", KnoxSync_BuildIcsRejectsNullSchedules);
        Run("KnoxSync_BuildIcsRoundTripsThroughCalendarScheduleParseIcs", KnoxSync_BuildIcsRoundTripsThroughCalendarScheduleParseIcs);
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
        PropertyDefault(type, settings, "KnoxCalendarEnabled", false);
        PropertyDefault(type, settings, "KnoxLeadMinutes", 5);
        PropertyDefault(type, settings, "KnoxAutoClose", "none");
        PropertyDefault(type, settings, "KnoxAutoCloseMinutes", 5);
        PropertyDefault(type, settings, "Appearance", "system");
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
        Equal(new DateTime(2026, 11, 9, 9, 35, 0, DateTimeKind.Local), actual, "Custom minutes land on the 09:00-anchored slot");
        Equal(DateTimeKind.Local, actual.Kind, "DateTime kind");
    }

    private static void Next_ClampsBeforeStartToNine()
    {
        Equal(new DateTime(2026, 11, 9, 9, 0, 0),
            Next(new DateTime(2026, 11, 9, 7, 30, 0), new ReminderSettings()), "Before start");
    }

    private static void Next_BeforeStartTakesFirstSlot()
    {
        // 08:30 닫힘(시작 1시간 전): 닫은 시각 + 간격(09:30)이 아니라 시각표의 첫 슬롯 09:00.
        Equal(new DateTime(2026, 11, 9, 9, 0, 0),
            Next(new DateTime(2026, 11, 9, 8, 30, 0), new ReminderSettings()), "The timetable's first slot, not close time plus interval");
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
        Equal(new DateTime(2026, 11, 10, 0, 0, 0),
            Next(new DateTime(2026, 11, 9, 23, 30, 0), settings), "No hour limit: the 09:00-anchored grid continues through the night");
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
        Equal(new DateTime(2026, 11, 10, 0, 0, 0),
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
        Equal(new DateTime(2026, 11, 9, 13, 0, 0), next, "Dismissal lands on the next fixed slot, whatever the close time was");
        Equal(false, IsDue(dismissed, next, settings, false, false, true), "No catch-up popup on dismissal");
        Equal(false, IsDue(next.AddTicks(-1), next, settings, false, false, true), "Full slot wait");
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

    private static void VariantModes_DefaultsToEmptyAndOldConfigsStillLoad()
    {
        Equal(0, new ReminderSettings().VariantModes.Count, "New settings have no per-card choice, so every card uses the random default");
        ReminderSettings old = new JavaScriptSerializer().Deserialize<ReminderSettings>("{\"IntervalMinutes\":25}");
        Equal(0, old.VariantModes.Count, "Old config without VariantModes");
        Validate(old, KnownThemes());
    }

    private static void VariantModes_ValidateAcceptsRandomAndSequentialOnly()
    {
        Validate(new ReminderSettings { VariantModes = new Dictionary<string, string> { { "relax", "random" }, { "dance", "sequential" } } }, KnownThemes());
        Validate(new ReminderSettings { VariantModes = null }, KnownThemes());
        foreach (string mode in new string[] { null, "", "Random", "all", "sequential " })
        {
            string bad = mode;
            ThrowsArgument(delegate { Validate(new ReminderSettings { VariantModes = new Dictionary<string, string> { { "relax", bad } } }, KnownThemes()); }, "Mode '" + mode + "'");
        }
        ThrowsArgument(delegate { Validate(new ReminderSettings { VariantModes = new Dictionary<string, string> { { " ", "random" } } }, KnownThemes()); }, "Blank card id");
    }

    private static void VariantModes_JavaScriptSerializerRoundTrip()
    {
        ReminderSettings settings = new ReminderSettings();
        settings.VariantModes["stretch"] = "sequential";
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        ReminderSettings back = serializer.Deserialize<ReminderSettings>(serializer.Serialize(settings));
        Equal("sequential", back.VariantModes["stretch"], "Saved choice survives the settings file");
    }

    private static void LoopModes_DefaultsToEmptyAndOldConfigsStillLoad()
    {
        Equal(0, new ReminderSettings().LoopModes.Count, "New settings have no per-card loop choice, so every card plays once");
        ReminderSettings old = new JavaScriptSerializer().Deserialize<ReminderSettings>("{\"IntervalMinutes\":25}");
        Equal(0, old.LoopModes.Count, "Old config without LoopModes");
        Validate(old, KnownThemes());
    }

    private static void LoopModes_ValidateAcceptsOnAndOffOnly()
    {
        Validate(new ReminderSettings { LoopModes = new Dictionary<string, string> { { "relax", "on" }, { "dance", "off" } } }, KnownThemes());
        Validate(new ReminderSettings { LoopModes = null }, KnownThemes());
        foreach (string mode in new string[] { null, "", "ON", "loop", "always" })
        {
            string bad = mode;
            ThrowsArgument(delegate { Validate(new ReminderSettings { LoopModes = new Dictionary<string, string> { { "relax", bad } } }, KnownThemes()); }, "LoopModes must map");
        }
        ThrowsArgument(delegate { Validate(new ReminderSettings { LoopModes = new Dictionary<string, string> { { " ", "on" } } }, KnownThemes()); }, "LoopModes must map");
    }

    private static void LoopModes_JavaScriptSerializerRoundTrip()
    {
        ReminderSettings settings = new ReminderSettings();
        settings.LoopModes["relax"] = "on";
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        ReminderSettings back = serializer.Deserialize<ReminderSettings>(serializer.Serialize(settings));
        Equal("on", back.LoopModes["relax"], "Saved loop choice survives the settings file");
    }

    private static void Schedules_DefaultsToEmptyAndOldConfigsStillLoad()
    {
        Equal(0, new ReminderSettings().Schedules.Count, "New settings carry no per-card run time");
        ReminderSettings old = new JavaScriptSerializer().Deserialize<ReminderSettings>("{\"IntervalMinutes\":25}");
        Equal(0, old.Schedules.Count, "Old config without Schedules");
        Validate(old, KnownThemes());
    }

    private static void Schedules_TryParseClockAcceptsDayTimesOnly()
    {
        int at;
        foreach (string good in new string[] { "00:00", "0:00", "9:05", "13:30", "23:59", "13:30:00", "13:30:45", "0:00:00", "23:59:59" })
        {
            if (!ReminderSettings.TryParseClock(good, out at)) throw new Exception("Should parse: " + good);
        }
        foreach (string bad in new string[] { null, "", "9:5", "24:00", "13:60", "-1:00", "1330", "13:30:5", "13:30:60", "13:60:30", "24:00:00", "13:30:00:00", "aa:bb", "9:", ":30" })
        {
            if (ReminderSettings.TryParseClock(bad, out at)) throw new Exception("Should reject: " + bad);
        }
        ReminderSettings.TryParseClock("13:30", out at);
        Equal(48600, at, "13:30 is 48600 seconds past midnight (minutes-only times read as :00)");
        ReminderSettings.TryParseClock("13:30:45", out at);
        Equal(48645, at, "13:30:45 is 48645 seconds past midnight");
    }

    private static void Schedules_ValidateAcceptsKnownCardsWithDayTimes()
    {
        Validate(new ReminderSettings { ThemeIds = new string[] { "eyes" }, Schedules = new Dictionary<string, string> { { "stretch", "13:30" } } }, KnownThemes());
        Validate(new ReminderSettings { ThemeIds = new string[] { "eyes" }, Schedules = new Dictionary<string, string> { { "stretch", "13:30:45" } } }, KnownThemes());
        Validate(new ReminderSettings { ThemeIds = new string[] { "eyes" }, Schedules = null }, KnownThemes());
        ThrowsArgument(delegate { Validate(new ReminderSettings { ThemeIds = new string[] { "eyes" }, Schedules = new Dictionary<string, string> { { "missing", "13:30" } } }, KnownThemes()); }, "Schedules must name");
        ThrowsArgument(delegate { Validate(new ReminderSettings { ThemeIds = new string[] { "eyes" }, Schedules = new Dictionary<string, string> { { "stretch", "24:00" } } }, KnownThemes()); }, "Schedules must map");
        ThrowsArgument(delegate { Validate(new ReminderSettings { ThemeIds = new string[] { "eyes" }, Schedules = new Dictionary<string, string> { { "stretch", "13:30" }, { "breathe", "9:5" } } }, KnownThemes()); }, "Schedules must map");
        ThrowsArgument(delegate { Validate(new ReminderSettings { ThemeIds = new string[] { "eyes" }, Schedules = new Dictionary<string, string> { { "stretch", "13:30:60" } } }, KnownThemes()); }, "Schedules must map");
        // 한 카드가 주기와 시각 실행에 함께 걸리면 안 된다(UI에서도 서로 회색 처리).
        ThrowsArgument(delegate { Validate(new ReminderSettings { ThemeIds = new string[] { "stretch" }, Schedules = new Dictionary<string, string> { { "stretch", "13:30" } } }, KnownThemes()); }, "never both");
        Dictionary<string, string> many = new Dictionary<string, string>();
        for (int i = 0; i < 65; i++) many.Add("k" + i, "13:30");
        ThrowsArgument(delegate { Validate(new ReminderSettings { ThemeIds = new string[] { "eyes" }, Schedules = many }, KnownThemes()); }, "too many");
    }

    private static void Schedules_TimeOnlySelectionIsValidAndRoundTrips()
    {
        // 시각 실행 카드만 있고 주기 선택이 비어 있어도 올바른 설정이다.
        ReminderSettings settings = new ReminderSettings { ThemeIds = new string[0], Schedules = new Dictionary<string, string> { { "stretch", "07:45" }, { "breathe", "07:46:30" } } };
        Validate(settings, KnownThemes());
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        ReminderSettings back = serializer.Deserialize<ReminderSettings>(serializer.Serialize(settings));
        Equal("07:45", back.Schedules["stretch"], "Saved run time survives the settings file");
        Equal("07:46:30", back.Schedules["breathe"], "Saved run time with seconds survives the settings file");
        Equal(0, back.ThemeIds.Length, "No periodic selection survives the settings file");
    }

    private const string VideoItem = "{\"schema\":1,\"id\":\"u-vid\",\"name\":\"N\",\"summary\":\"S\",\"safety\":\"Stop if it hurts.\",\"instructions\":[\"a\"],\"videos\":[{\"id\":\"dQw4w9WgXcQ\",\"title\":\"T\",\"author\":\"A\"},{\"id\":\"-yOiTWEgU_Y\",\"title\":\"U\"}]}";

    private static void Community_AcceptsYouTubeVideoIds()
    {
        CommunityItem item = CommunityContent.Parse(VideoItem);
        Equal(2, item.Videos.Length, "Two videos");
        Equal("dQw4w9WgXcQ", item.Videos[0].id, "First id");
        Equal("-yOiTWEgU_Y", item.Videos[1].id, "An id may start with a dash");
        Equal("", item.Videos[1].author, "Author is optional");
        Equal(0, CommunityContent.Parse(Good).Videos.Length, "Items without videos still parse");
    }

    private static void Community_RejectsBadVideos()
    {
        string[] bad = new string[] {
            VideoItem.Replace("dQw4w9WgXcQ", "short"),
            VideoItem.Replace("dQw4w9WgXcQ", "https://youtu.be/x"),
            VideoItem.Replace("\"id\":\"-yOiTWEgU_Y\"", "\"id\":\"dQw4w9WgXcQ\""),
            VideoItem.Replace("\"title\":\"T\"", "\"title\":\"see http://x.example\""),
            VideoItem.Replace("\"title\":\"T\"", "\"title\":\"\""),
            VideoItem.Replace("\"videos\":[", "\"videos\":{\"x\":["),
            VideoItem.Replace("[{\"id\":\"dQw4w9WgXcQ\",\"title\":\"T\",\"author\":\"A\"},", "[\"dQw4w9WgXcQ\","),
            "{\"schema\":1,\"id\":\"u-vid\",\"name\":\"N\",\"summary\":\"S\",\"safety\":\"x\",\"instructions\":[\"a\"],\"videos\":[" + String.Join(",", new string[] { "AAAAAAAAAAA", "BBBBBBBBBBB", "CCCCCCCCCCC", "DDDDDDDDDDD", "EEEEEEEEEEE", "FFFFFFFFFFF" }.Select(x => "{\"id\":\"" + x + "\",\"title\":\"t\"}").ToArray()) + "]}"
        };
        foreach (string json in bad)
        {
            string text = json;
            ThrowsArgument(delegate { CommunityContent.Parse(text); }, "Bad video accepted: " + text);
        }
    }

    private static void Community_IdIndexIsReservedForTheListFile()
    {
        ThrowsArgument(delegate { CommunityContent.Parse(Good.Replace("\"id\":\"u-abc\"", "\"id\":\"index\"")); }, "An item may not take the id of community/index.json");
        CommunityContent.Parse(Good.Replace("\"id\":\"u-abc\"", "\"id\":\"index-card\""));
    }

    private static void Community_IconMustBeOneOfTheOfferedSymbols()
    {
        Equal("", CommunityContent.Parse(Good).Icon, "No icon means the default tile symbol");
        foreach (string icon in CommunityContent.Icons)
            Equal(icon, CommunityContent.Parse(Good.Replace("\"schema\":1,", "\"schema\":1,\"icon\":\"" + icon + "\",")).Icon, "Offered icon " + icon);
        foreach (string bad in new string[] { "", "x", "<b>", "\ud83d\ude00", "✿✿", "https://x.example" })
        {
            string text = bad;
            ThrowsArgument(delegate { CommunityContent.Parse(Good.Replace("\"schema\":1,", "\"schema\":1,\"icon\":\"" + text + "\",")); }, "Icon '" + bad + "'");
        }
    }

    private static void Community_VideoOnlyItemMayOmitInstructions()
    {
        string videoOnly = "{\"schema\":1,\"id\":\"u-vid\",\"name\":\"N\",\"summary\":\"S\",\"safety\":\"Stop if it hurts.\",\"videos\":[{\"id\":\"dQw4w9WgXcQ\",\"title\":\"T\"}]}";
        CommunityItem item = CommunityContent.Parse(videoOnly);
        Equal(0, item.Instructions.Length, "No instructions");
        Equal(1, item.Videos.Length, "One video");
        ThrowsArgument(delegate { CommunityContent.Parse("{\"schema\":1,\"id\":\"u-vid\",\"name\":\"N\",\"summary\":\"S\",\"safety\":\"x\"}"); }, "Neither instructions nor videos");
        ThrowsArgument(delegate { CommunityContent.Parse(videoOnly.Replace("\"videos\"", "\"instructions\":\"oops\",\"videos\"")); }, "Instructions present but not an array");
    }

    private static void ThemeMode_DefaultsToOrderedOne()
    {
        Equal("ordered-one", new ReminderSettings().ThemeMode, "Default mode keeps the old one-at-a-time rotation");
        Equal("ordered-one", new JavaScriptSerializer().Deserialize<ReminderSettings>("{\"IntervalMinutes\":25}").ThemeMode, "Old config without ThemeMode");
    }

    // The first-run 기본 프리셋 (303~309): 09:00-18:00 평일 · 60분 간격 · 점심 12:00-13:00 · 로그인 자동 실행 ·
    // 10분 자리비움 · Knox 캘린더 5분 전(자동 닫힘 없음) · 카드 6개 무작위로 하나씩 · 퇴근버스 17:00.
    private static void DefaultPreset_MatchesTheAnnouncedPreset()
    {
        ReminderSettings s = ReminderSettings.DefaultPreset();
        Validate(s, new string[] { "stretch", "meditation", "voice", "back-standing", "dance", "relax", "recipe", "bus-home" });
        Equal(60, s.IntervalMinutes, "Preset: every 60 minutes");
        Equal(9, s.StartHour, "Preset: starts at 09:00");
        Equal(18, s.EndHour, "Preset: ends at 18:00");
        Equal(true, s.WeekdaysOnly, "Preset: weekdays only");
        Equal(true, s.LimitHours, "Preset: hours limited");
        Equal(true, s.BreakEnabled, "Preset: lunch break on");
        Equal(12, s.BreakStartHour, "Preset: lunch starts at 12:00");
        Equal(13, s.BreakEndHour, "Preset: lunch ends at 13:00");
        Equal(1, s.Breaks.Count, "Preset: one quiet period in the list");
        Equal(12, s.Breaks[0].StartHour, "Preset: list lunch starts at 12:00");
        Equal(13, s.Breaks[0].EndHour, "Preset: list lunch ends at 13:00");
        Equal(true, s.StartAtLogin, "Preset: starts at Windows login");
        Equal(true, s.PauseWhenIdle, "Preset: pauses when away");
        Equal(10, s.IdleMinutes, "Preset: away after 10 minutes");
        Equal(true, s.KnoxCalendarEnabled, "Preset: Knox calendar alerts on");
        Equal(5, s.KnoxLeadMinutes, "Preset: Knox 5 minutes before");
        Equal(ReminderSettings.KnoxAutoCloseNone, s.KnoxAutoClose, "Preset: no auto-close");
        Equal(ThemeRotation.RandomOne, s.ThemeMode, "Preset: one random card each time");
        Equal(String.Join(",", new string[] { "stretch", "meditation", "voice", "back-standing", "relax" }),
              String.Join(",", s.ThemeIds), "Preset: the five chosen cards (dance left out)");
        Equal(1, s.Schedules.Count, "Preset: one scheduled card");
        Equal("17:00:00", s.Schedules["bus-home"], "Preset: the bus-home card at 17:00");
        Equal(false, s.Enabled, "Preset: starts paused until the user turns it on");
        // The neutral constructor baseline must stay untouched for corrupted-settings recovery and the tests above.
        ReminderSettings blank = new ReminderSettings();
        Equal(0, blank.Schedules.Count, "Constructor: no schedules");
        Equal(1, blank.ThemeIds.Length, "Constructor: one card");
        Equal(ThemeRotation.OrderedOne, blank.ThemeMode, "Constructor: ordered rotation");
        Equal(false, blank.StartAtLogin, "Constructor: no auto start");
        Equal(false, blank.BreakEnabled, "Constructor: no lunch break");
        Equal(false, blank.KnoxCalendarEnabled, "Constructor: Knox off");
        Equal(false, blank.PauseWhenIdle, "Constructor: no idle pause");
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

    private static void Calendar_ParseIcsReadsUidTitleAndLocalStart()
    {
        string ics = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:evt-1\nSUMMARY:팀 회의\nDTSTART:20261109T140000\nEND:VEVENT\nEND:VCALENDAR";
        List<CalendarEntry> entries = CalendarSchedule.ParseIcs(ics);
        Equal(1, entries.Count, "One event parsed");
        Equal("evt-1", entries[0].Uid, "Uid");
        Equal("팀 회의", entries[0].Title, "Title");
        Equal(new DateTime(2026, 11, 9, 14, 0, 0), entries[0].StartLocal, "Local start");
    }

    private static void Calendar_ParseIcsConvertsUtcToLocal()
    {
        string ics = "BEGIN:VEVENT\nUID:evt-utc\nSUMMARY:UTC\nDTSTART:20261109T050000Z\nEND:VEVENT";
        List<CalendarEntry> entries = CalendarSchedule.ParseIcs(ics);
        Equal(1, entries.Count, "One event parsed");
        Equal(new DateTime(2026, 11, 9, 5, 0, 0, DateTimeKind.Utc).ToLocalTime(), entries[0].StartLocal, "UTC converted to local");
    }

    private static void Calendar_ParseIcsSkipsIncompleteEvents()
    {
        // Missing UID, missing DTSTART, and an event never closed are all skipped rather than failing the file.
        string ics = "BEGIN:VEVENT\nSUMMARY:No uid\nDTSTART:20261109T090000\nEND:VEVENT\n" +
            "BEGIN:VEVENT\nUID:evt-2\nSUMMARY:No start\nEND:VEVENT\n" +
            "BEGIN:VEVENT\nUID:evt-3\nSUMMARY:Open\nDTSTART:20261109T090000\n" +
            "BEGIN:VEVENT\nUID:evt-4\nSUMMARY:Good\nDTSTART:20261109T090000\nEND:VEVENT";
        List<CalendarEntry> entries = CalendarSchedule.ParseIcs(ics);
        Equal(1, entries.Count, "Only the complete event is kept");
        Equal("evt-4", entries[0].Uid, "Surviving event");
    }

    private static void Calendar_ParseIcsUnescapesSummary()
    {
        string ics = "BEGIN:VEVENT\nUID:evt-5\nSUMMARY:A\\, B\\; C\\nD\nDTSTART:20261109T090000\nEND:VEVENT";
        List<CalendarEntry> entries = CalendarSchedule.ParseIcs(ics);
        Equal("A, B; C D", entries[0].Title, "Escaped comma/semicolon/newline");
    }

    private static void Calendar_ParseIcsOfEmptyOrNullTextIsEmpty()
    {
        Equal(0, CalendarSchedule.ParseIcs(null).Count, "Null text");
        Equal(0, CalendarSchedule.ParseIcs("").Count, "Empty text");
    }

    private static CalendarEntry Event(string uid, DateTime start) { return new CalendarEntry { Uid = uid, Title = "t", StartLocal = start }; }

    private static void Calendar_DueEntriesFiresWithinLeadWindow()
    {
        CalendarEntry entry = Event("a", new DateTime(2026, 11, 9, 14, 0, 0));
        HashSet<string> fired = new HashSet<string>();
        Equal(1, CalendarSchedule.DueEntries(new DateTime(2026, 11, 9, 13, 55, 0), new[] { entry }, 5, fired).Count, "Exactly at lead time");
        Equal(1, CalendarSchedule.DueEntries(new DateTime(2026, 11, 9, 13, 59, 59), new[] { entry }, 5, fired).Count, "Just before start");
    }

    private static void Calendar_DueEntriesNotYetDueIsExcluded()
    {
        CalendarEntry entry = Event("a", new DateTime(2026, 11, 9, 14, 0, 0));
        HashSet<string> fired = new HashSet<string>();
        Equal(0, CalendarSchedule.DueEntries(new DateTime(2026, 11, 9, 13, 54, 59), new[] { entry }, 5, fired).Count, "Before the lead window opens");
    }

    private static void Calendar_DueEntriesAfterStartIsExcluded()
    {
        CalendarEntry entry = Event("a", new DateTime(2026, 11, 9, 14, 0, 0));
        HashSet<string> fired = new HashSet<string>();
        Equal(0, CalendarSchedule.DueEntries(new DateTime(2026, 11, 9, 14, 0, 0), new[] { entry }, 5, fired).Count, "At the meeting start");
        Equal(0, CalendarSchedule.DueEntries(new DateTime(2026, 11, 9, 14, 5, 0), new[] { entry }, 5, fired).Count, "After the meeting start");
    }

    private static void Calendar_DueEntriesAlreadyFiredIsExcluded()
    {
        CalendarEntry entry = Event("a", new DateTime(2026, 11, 9, 14, 0, 0));
        HashSet<string> fired = new HashSet<string> { CalendarSchedule.FiredKey(entry) };
        Equal(0, CalendarSchedule.DueEntries(new DateTime(2026, 11, 9, 13, 58, 0), new[] { entry }, 5, fired).Count, "Already fired is not due again");
    }

    private static void Calendar_DueEntriesRescheduledMeetingIsDueAgain()
    {
        // The user moves the SAME calendar entry to a later time to test; the fired key includes the
        // start time, so the moved meeting must ring again at its new lead-time moment.
        CalendarEntry first = Event("a", new DateTime(2026, 11, 9, 14, 0, 0));
        CalendarEntry moved = Event("a", new DateTime(2026, 11, 9, 16, 0, 0));
        HashSet<string> fired = new HashSet<string> { CalendarSchedule.FiredKey(first) };
        Equal(1, CalendarSchedule.DueEntries(new DateTime(2026, 11, 9, 15, 58, 0), new[] { moved }, 5, fired).Count, "The rescheduled meeting is due again");
        Equal("a|20261109160000", CalendarSchedule.FiredKey(moved), "The fired key is uid plus start");
    }

    private static void Calendar_DueEntriesRejectsNullArguments()
    {
        ThrowsArgument(delegate { CalendarSchedule.DueEntries(DateTime.Now, null, 5, new HashSet<string>()); }, "Null entries");
        ThrowsArgument(delegate { CalendarSchedule.DueEntries(DateTime.Now, new CalendarEntry[0], 5, null); }, "Null firedUids");
    }

    private static void Calendar_ParseIcsReadsOptionalEnd()
    {
        string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:a\r\nSUMMARY:With end\r\nDTSTART:20261110T140000\r\nDTEND:20261110T150000\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nUID:b\r\nSUMMARY:No end\r\nDTSTART:20261110T160000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        List<CalendarEntry> entries = CalendarSchedule.ParseIcs(ics);
        Equal(2, entries.Count, "Both events parse");
        Equal(new DateTime(2026, 11, 10, 15, 0, 0), entries[0].EndLocal.Value, "DTEND round-trips into EndLocal");
        Equal(false, entries[1].EndLocal.HasValue, "An event without DTEND has no EndLocal");
    }

    private static void KnoxSync_AppendLogTrimsToTheLimitKeepingNewest()
    {
        List<KnoxSyncLogEntry> log = new List<KnoxSyncLogEntry>();
        for (int i = 0; i < KnoxSync.LogLimit + 5; i++)
            KnoxSync.AppendLog(log, new KnoxSyncLogEntry { When = i.ToString() });
        Equal(KnoxSync.LogLimit, log.Count, "The log is capped at the limit");
        Equal("5", log[0].When, "The oldest entries were dropped");
        Equal((KnoxSync.LogLimit + 4).ToString(), log[log.Count - 1].When, "The newest entry is kept");
        // Appending again after trimming keeps the cap stable.
        KnoxSync.AppendLog(log, new KnoxSyncLogEntry { When = "last" });
        Equal(KnoxSync.LogLimit, log.Count, "The cap does not grow");
        Equal("last", log[log.Count - 1].When, "The new entry is last");
    }

    private static void KnoxSync_AppendLogRejectsNullArguments()
    {
        ThrowsArgument(delegate { KnoxSync.AppendLog(null, new KnoxSyncLogEntry()); }, "Null log");
        ThrowsArgument(delegate { KnoxSync.AppendLog(new List<KnoxSyncLogEntry>(), null); }, "Null entry");
    }

    private static void KnoxSync_FirstLineSkipsBlanksAndTruncates()
    {
        Equal("", KnoxSync.FirstLine(null, 80), "Null has no first line");
        Equal("", KnoxSync.FirstLine(" \n \r\n", 80), "Only blanks has no first line");
        Equal("could not reach the server", KnoxSync.FirstLine("\r\n  could not reach the server\nmore\n", 80), "CRLF and blanks are skipped");
        Equal(80, KnoxSync.FirstLine(new string('x', 200), 80).Length, "Long lines are truncated to maxChars");
        Equal(true, KnoxSync.FirstLine(new string('x', 200), 80).EndsWith("…", StringComparison.Ordinal), "Truncation is visible");
        Equal("short", KnoxSync.FirstLine("short", 80), "Short lines stay whole");
    }

    private const string ScheduleJson = "{\"knox_id\":\"jazzman.han\",\"window\":{},\"schedules\":[" +
        "{\"scheduleId\":\"SCH1\",\"subject\":\"Weekly sync\",\"start\":\"2026-11-10T14:00:00+09:00\",\"end\":\"2026-11-10T15:00:00+09:00\",\"place\":\"A2101\",\"attendees\":[\"a\",\"b\"],\"attendee_count\":2}" +
        "]}";

    private static void KnoxSync_ParseSchedulesReadsSubjectStartAndScheduleId()
    {
        List<KnoxSchedule> schedules = KnoxSync.ParseSchedules(ScheduleJson);
        Equal(1, schedules.Count, "One schedule");
        Equal("SCH1", schedules[0].ScheduleId, "scheduleId");
        Equal("Weekly sync", schedules[0].Subject, "subject");
        Equal("2026-11-10T14:00:00+09:00", schedules[0].Start, "start");
        Equal("2026-11-10T15:00:00+09:00", schedules[0].End, "end");
    }

    private static void KnoxSync_ParseSchedulesEndIsOptional()
    {
        List<KnoxSchedule> schedules = KnoxSync.ParseSchedules("{\"schedules\":[{\"scheduleId\":\"x\",\"subject\":\"No end\",\"start\":\"2026-11-10T09:00:00+09:00\"}]}");
        Equal(null, schedules[0].End, "A response without 'end' leaves End null");
    }

    private static void KnoxSync_BuildIcsWritesEndWhenPresentAndOmitsItWhenNot()
    {
        var withEnd = new List<KnoxSchedule> { new KnoxSchedule { ScheduleId = "SCH1", Subject = "A", Start = "2026-11-10T14:00:00+09:00", End = "2026-11-10T15:00:00+09:00" } };
        Equal(true, KnoxSync.BuildIcs(withEnd).Contains("DTEND:20261110T150000"), "A readable end becomes DTEND");
        var withoutEnd = new List<KnoxSchedule> { new KnoxSchedule { ScheduleId = "SCH2", Subject = "B", Start = "2026-11-10T14:00:00+09:00", End = null } };
        Equal(false, KnoxSync.BuildIcs(withoutEnd).Contains("DTEND"), "No end means no DTEND line");
    }

    private static void KnoxSync_ParseSchedulesIgnoresAttendeesAndOtherFields()
    {
        // Only ScheduleId/Subject/Start/End exist on KnoxSchedule at all; attendees/place are never read into it.
        Type type = typeof(KnoxSchedule);
        string[] fields = Array.ConvertAll(type.GetFields(), f => f.Name);
        Equal(4, fields.Length, "KnoxSchedule exposes exactly ScheduleId, Subject, Start and End");
    }

    private static void KnoxSync_ParseSchedulesSkipsEntriesMissingSubjectOrStart()
    {
        string json = "{\"schedules\":[{\"scheduleId\":\"x\",\"start\":\"2026-11-10T09:00:00+09:00\"},{\"scheduleId\":\"y\",\"subject\":\"No start\"},{\"scheduleId\":\"z\",\"subject\":\"Good\",\"start\":\"2026-11-10T10:00:00+09:00\"}]}";
        List<KnoxSchedule> schedules = KnoxSync.ParseSchedules(json);
        Equal(1, schedules.Count, "Only the entry with both subject and start is kept");
        Equal("Good", schedules[0].Subject, "The kept entry");
    }

    private static void KnoxSync_ParseSchedulesOfEmptyArrayIsEmpty()
    {
        Equal(0, KnoxSync.ParseSchedules("{\"schedules\":[]}").Count, "An empty window is not an error");
    }

    private static void KnoxSync_ParseSchedulesRejectsMissingSchedulesArray()
    {
        ThrowsArgument(delegate { KnoxSync.ParseSchedules("{\"knox_id\":\"x\"}"); }, "No 'schedules' key");
        ThrowsArgument(delegate { KnoxSync.ParseSchedules("{\"error\":\"KNOX_ID_NOT_REGISTERED\"}"); }, "Error body without 'schedules'");
    }

    private static void KnoxSync_ParseSchedulesRejectsInvalidJson()
    {
        ThrowsArgument(delegate { KnoxSync.ParseSchedules("not json"); }, "Garbage body");
        ThrowsArgument(delegate { KnoxSync.ParseSchedules("[1,2,3]"); }, "Array root");
    }

    private static void KnoxSync_ParseSchedulesRejectsNullOrEmptyJson()
    {
        ThrowsArgument(delegate { KnoxSync.ParseSchedules(null); }, "Null json");
        ThrowsArgument(delegate { KnoxSync.ParseSchedules(""); }, "Empty json");
    }

    private static void KnoxSync_ToDtStampConvertsOffsetAndUtcTimes()
    {
        Equal("20261110T140000", KnoxSync.ToDtStamp("2026-11-10T14:00:00+09:00"), "Offset time keeps its local digits, drops the offset");
        Equal("20261110T050000Z", KnoxSync.ToDtStamp("2026-11-10T05:00:00Z"), "A literal Z is kept as UTC");
    }

    private static void KnoxSync_ToDtStampRejectsAllDayAndUnreadableStarts()
    {
        Equal(null, KnoxSync.ToDtStamp("2026-11-10"), "Date-only (all-day) start has no time to count down to");
        Equal(null, KnoxSync.ToDtStamp("not a date"), "Unreadable start");
        Equal(null, KnoxSync.ToDtStamp(null), "Null start");
        Equal(null, KnoxSync.ToDtStamp(""), "Empty start");
    }

    private static void KnoxSync_StartDisplayShowsWallClockForOffsetStarts()
    {
        Equal("11.10 14:00", KnoxSync.StartDisplay("2026-11-10T14:00:00+09:00"), "Offset starts show their wall-clock digits, matching the reminder pipeline");
        Equal("", KnoxSync.StartDisplay("2026-11-10"), "Date-only (all-day) has no clock time");
        Equal("", KnoxSync.StartDisplay("not a date"), "Unreadable start");
        Equal("", KnoxSync.StartDisplay(null), "Null start");
    }

    private static void KnoxSync_StartDisplayConvertsUtcZStartsToPcLocalTime()
    {
        // Same conversion rule CalendarSchedule.ParseIcs applies to "Z" stamps, so the log never
        // disagrees with the popup about when a meeting starts.
        string expected = new DateTime(2026, 11, 10, 5, 0, 0).ToLocalTime().ToString("MM.dd HH:mm", CultureInfo.InvariantCulture);
        Equal(expected, KnoxSync.StartDisplay("2026-11-10T05:00:00Z"), "A literal Z is converted to the PC's local time");
    }

    private static void KnoxSync_SummarizeSchedulesListsTimesAndSubjects()
    {
        var schedules = new List<KnoxSchedule> {
            new KnoxSchedule { Subject = "Weekly sync", Start = "2026-11-10T14:00:00+09:00" },
            new KnoxSchedule { Subject = "1:1", Start = "2026-11-11T09:30:00+09:00" }
        };
        Equal("11.10 14:00 Weekly sync · 11.11 09:30 1:1", KnoxSync.SummarizeSchedules(schedules, 200), "Each fetched meeting shows its local time and subject");
    }

    private static void KnoxSync_SummarizeSchedulesShowsSubjectOnlyWithoutAClockTime()
    {
        var schedules = new List<KnoxSchedule> { new KnoxSchedule { Subject = "Day off", Start = "2026-11-10" } };
        Equal("Day off", KnoxSync.SummarizeSchedules(schedules, 200), "All-day entries keep their subject, just without a clock time");
    }

    private static void KnoxSync_SummarizeSchedulesOfNothingIsEmpty()
    {
        Equal("", KnoxSync.SummarizeSchedules(new List<KnoxSchedule>(), 200), "No schedules, no content");
    }

    private static void KnoxSync_SummarizeSchedulesTruncatesLongLists()
    {
        var schedules = new List<KnoxSchedule> { new KnoxSchedule { Subject = new string('x', 150), Start = "2026-11-10T14:00:00+09:00" } };
        string summary = KnoxSync.SummarizeSchedules(schedules, 40);
        Equal(40, summary.Length, "The summary is capped at maxChars");
        Equal(true, summary.EndsWith("…", StringComparison.Ordinal), "Truncation is visible");
    }

    private static void KnoxSync_SummarizeSchedulesRejectsBadArguments()
    {
        ThrowsArgument(delegate { KnoxSync.SummarizeSchedules(null, 40); }, "Null schedules");
        ThrowsArgument(delegate { KnoxSync.SummarizeSchedules(new List<KnoxSchedule>(), 0); }, "Non-positive maxChars");
    }

    private static void KnoxSync_BuildIcsWritesFixedFormatWithEscaping()
    {
        var schedules = new List<KnoxSchedule> { new KnoxSchedule { ScheduleId = "SCH1", Subject = "A, B; C", Start = "2026-11-10T14:00:00+09:00" } };
        string ics = KnoxSync.BuildIcs(schedules);
        string[] lines = ics.Split(new string[] { "\r\n" }, StringSplitOptions.None);
        Equal("BEGIN:VCALENDAR", lines[0], "Calendar opens");
        Equal("BEGIN:VEVENT", lines[3], "Event opens");
        Equal("UID:knox-SCH1", lines[4], "UID uses the scheduleId");
        Equal("SUMMARY:A\\, B\\; C", lines[5], "Commas and semicolons are escaped");
        Equal("DTSTART:20261110T140000", lines[6], "DTSTART uses the converted stamp");
        Equal("END:VEVENT", lines[7], "Event closes");
        Equal("END:VCALENDAR", lines[8], "Calendar closes");
    }

    private static void KnoxSync_BuildIcsSkipsAllDayEntries()
    {
        var schedules = new List<KnoxSchedule> {
            new KnoxSchedule { ScheduleId = "ALLDAY", Subject = "Day off", Start = "2026-11-10" },
            new KnoxSchedule { ScheduleId = "TIMED", Subject = "Meeting", Start = "2026-11-10T09:00:00+09:00" }
        };
        string ics = KnoxSync.BuildIcs(schedules);
        Equal(false, ics.Contains("ALLDAY"), "The all-day entry produced no VEVENT");
        Equal(true, ics.Contains("UID:knox-TIMED"), "The timed entry is kept");
    }

    private static void KnoxSync_BuildIcsOfEmptyListIsAnEventlessCalendar()
    {
        string ics = KnoxSync.BuildIcs(new List<KnoxSchedule>());
        Equal("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//DeskHooker//knox-ics-export//KO\r\nEND:VCALENDAR\r\n", ics, "No schedules is not an error");
    }

    private static void KnoxSync_BuildIcsFallsBackToATimeBasedUidWithoutScheduleId()
    {
        var schedules = new List<KnoxSchedule> { new KnoxSchedule { ScheduleId = null, Subject = "No id!", Start = "2026-11-10T09:00:00+09:00" } };
        string ics = KnoxSync.BuildIcs(schedules);
        Equal(true, ics.Contains("UID:knox-20261110T090000-Noid"), "The fallback UID combines the stamp and an ascii-only slug of the subject");
    }

    private static void KnoxSync_BuildIcsRejectsNullSchedules()
    {
        ThrowsArgument(delegate { KnoxSync.BuildIcs(null); }, "Null schedules");
    }

    private static void KnoxSync_BuildIcsRoundTripsThroughCalendarScheduleParseIcs()
    {
        var schedules = new List<KnoxSchedule> { new KnoxSchedule { ScheduleId = "SCH9", Subject = "회의", Start = "2026-11-10T14:00:00+09:00" } };
        List<CalendarEntry> entries = CalendarSchedule.ParseIcs(KnoxSync.BuildIcs(schedules));
        Equal(1, entries.Count, "One event round-trips");
        Equal("knox-SCH9", entries[0].Uid, "Uid round-trips");
        Equal("회의", entries[0].Title, "Title round-trips");
        Equal(new DateTime(2026, 11, 10, 14, 0, 0), entries[0].StartLocal, "StartLocal round-trips");
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
            Enabled = true, StartAtLogin = true, InhaleSeconds = 5, HoldSeconds = 7, ExhaleSeconds = 9,
            VariantModes = new Dictionary<string, string> { { "relax", "sequential" }, { "dance", "random" } },
            LoopModes = new Dictionary<string, string> { { "relax", "on" } },
            Schedules = new Dictionary<string, string> { { "stretch", "07:45" } }
        };
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        ReminderSettings restored = serializer.Deserialize<ReminderSettings>(serializer.Serialize(original));
        foreach (PropertyInfo property in typeof(ReminderSettings).GetProperties())
        {
            if (property.Name == "ThemeIds")
                Equal(String.Join(",", original.ThemeIds), String.Join(",", restored.ThemeIds), "ThemeIds round trip");
            else if (property.Name == "VariantModes")
                Equal("dance=random,relax=sequential", String.Join(",", new SortedDictionary<string, string>(restored.VariantModes).Select(e => e.Key + "=" + e.Value).ToArray()), "VariantModes round trip");
            else if (property.Name == "LoopModes")
                Equal("relax=on", String.Join(",", new SortedDictionary<string, string>(restored.LoopModes).Select(e => e.Key + "=" + e.Value).ToArray()), "LoopModes round trip");
            else if (property.Name == "Schedules")
                Equal("stretch=07:45", String.Join(",", new SortedDictionary<string, string>(restored.Schedules).Select(e => e.Key + "=" + e.Value).ToArray()), "Schedules round trip");
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
        Equal(6, type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Length,
            "Only the six requested public methods");
        Signature("Validate", typeof(void), typeof(ReminderSettings), typeof(IEnumerable<string>));
        Signature("IsAllowed", typeof(bool), typeof(DateTime), typeof(ReminderSettings));
        Signature("Next", typeof(DateTime), typeof(DateTime), typeof(ReminderSettings));
        Signature("IsDue", typeof(bool), typeof(DateTime), typeof(DateTime), typeof(ReminderSettings),
            typeof(bool), typeof(bool), typeof(bool));
        Signature("IsAway", typeof(bool), typeof(TimeSpan), typeof(ReminderSettings));
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

    private static ReminderSettings Lunch(int intervalMinutes)
    {
        // Weekdays 09:00-18:00 with a 12:00-13:00 lunch break.
        return new ReminderSettings { IntervalMinutes = intervalMinutes, BreakEnabled = true, BreakStartHour = 12, BreakStartMinute = 0, BreakEndHour = 13, BreakEndMinute = 0 };
    }

    private static void Break_IsOffByDefaultAndChangesNothing()
    {
        ReminderSettings plain = new ReminderSettings { IntervalMinutes = 60 };
        Equal(false, plain.BreakEnabled, "Default");
        Equal(new DateTime(2026, 11, 9, 12, 0, 0), Next(new DateTime(2026, 11, 9, 11, 0, 0), plain), "Lunch hour is a normal hour when the break is off");
        plain.BreakStartHour = 11; plain.BreakEndHour = 14; // values are ignored while disabled
        Equal(new DateTime(2026, 11, 9, 12, 0, 0), Next(new DateTime(2026, 11, 9, 11, 0, 0), plain), "Disabled break stays ignored");
    }

    private static void Break_PostponesAReminderThatLandsInsideIt()
    {
        // Monday 2026-11-09, timetable anchored 09:00. From 11:30 the next slot is 12:00, which is lunch, so it waits for 13:00.
        Equal(new DateTime(2026, 11, 9, 13, 0, 0), Next(new DateTime(2026, 11, 9, 11, 30, 0), Lunch(60)), "Lands in lunch");
        Equal(new DateTime(2026, 11, 9, 13, 0, 0), Next(new DateTime(2026, 11, 9, 11, 59, 0), Lunch(1)), "One minute before lunch");
        Equal(new DateTime(2026, 11, 9, 11, 0, 0), Next(new DateTime(2026, 11, 9, 10, 30, 0), Lunch(60)), "Before lunch takes the next fixed slot (11:00)");
        Equal(new DateTime(2026, 11, 9, 14, 0, 0), Next(new DateTime(2026, 11, 9, 13, 0, 0), Lunch(60)), "After lunch is untouched");
    }

    private static void Next_AbsoluteTimetableStaysOnTheGrid()
    {
        // 09:00 시작 · 60분 간격의 고정 시각표(10:00, 11:00, 12:00…).
        // 팝업을 늦게 닫아도 시각표가 밀리지 않는다 — 절대 시간 기준.
        ReminderSettings s = new ReminderSettings { IntervalMinutes = 60 };
        Equal(new DateTime(2026, 11, 9, 11, 0, 0), Next(new DateTime(2026, 11, 9, 10, 7, 0), s), "A late dismissal does not drift the timetable");
        Equal(new DateTime(2026, 11, 9, 11, 0, 0), Next(new DateTime(2026, 11, 9, 10, 59, 30), s), "Just before a slot takes that slot");
        Equal(new DateTime(2026, 11, 9, 10, 0, 0), Next(new DateTime(2026, 11, 9, 9, 0, 0), s), "Exactly on a slot goes to the next slot");
        Equal(new DateTime(2026, 11, 9, 9, 0, 0), Next(new DateTime(2026, 11, 9, 8, 30, 0), s), "Before the day's start waits for the first slot");
        Equal(new DateTime(2026, 11, 9, 13, 0, 0), Next(new DateTime(2026, 11, 9, 11, 55, 0), new ReminderSettings { IntervalMinutes = 240 }), "A 240-minute timetable keeps 09:00/13:00/17:00 slots");
    }

    private static void Break_StartIsInsideEndIsOutside()
    {
        ReminderSettings s = Lunch(60);
        Equal(new DateTime(2026, 11, 9, 13, 0, 0), Next(new DateTime(2026, 11, 9, 11, 0, 0), s), "Exactly at the break start is inside the break");
        Equal(new DateTime(2026, 11, 9, 13, 0, 0), Next(new DateTime(2026, 11, 9, 12, 0, 0), s), "From inside the break");
        Equal(new DateTime(2026, 11, 9, 13, 1, 0), Next(new DateTime(2026, 11, 9, 13, 0, 0), Lunch(1)), "Exactly at the break end is outside the break");
    }

    private static void Break_ThatEndsAfterTheWindowRollsToTomorrow()
    {
        // Window 09:00-18:00; break 17:00-19:00 runs past the end of the day.
        ReminderSettings s = new ReminderSettings { IntervalMinutes = 30, BreakEnabled = true, BreakStartHour = 17, BreakEndHour = 19 };
        Equal(new DateTime(2026, 11, 10, 9, 0, 0), Next(new DateTime(2026, 11, 9, 16, 45, 0), s), "17:15 is in the break, 19:00 is after the window");
        // Friday evening rolls over the weekend to Monday morning.
        Equal(new DateTime(2026, 11, 16, 9, 0, 0), Next(new DateTime(2026, 11, 13, 16, 45, 0), s), "Friday into Monday");
    }

    private static void Break_WorksWithoutAWindowAndOnWeekends()
    {
        ReminderSettings s = new ReminderSettings { IntervalMinutes = 30, LimitHours = false, WeekdaysOnly = false, BreakEnabled = true, BreakStartHour = 0, BreakEndHour = 6 };
        // Quiet night 00:00-06:00 on a Saturday.
        Equal(new DateTime(2026, 11, 14, 6, 0, 0), Next(new DateTime(2026, 11, 14, 0, 10, 0), s), "Night break on a Saturday");
        // A break ending at 24:00 pushes to the next midnight, which is itself outside the break.
        ReminderSettings late = new ReminderSettings { IntervalMinutes = 30, LimitHours = false, WeekdaysOnly = false, BreakEnabled = true, BreakStartHour = 22, BreakEndHour = 24 };
        Equal(new DateTime(2026, 11, 15, 0, 0, 0), Next(new DateTime(2026, 11, 14, 22, 0, 0), late), "Break ending at 24:00");
    }

    private static void Break_IsAllowedHonoursTheBreak()
    {
        ReminderSettings s = Lunch(60);
        Equal(true, ReminderSchedule.IsAllowed(new DateTime(2026, 11, 9, 11, 59, 0), s), "Just before lunch");
        Equal(false, ReminderSchedule.IsAllowed(new DateTime(2026, 11, 9, 12, 0, 0), s), "Lunch start");
        Equal(false, ReminderSchedule.IsAllowed(new DateTime(2026, 11, 9, 12, 59, 0), s), "Lunch last minute");
        Equal(true, ReminderSchedule.IsAllowed(new DateTime(2026, 11, 9, 13, 0, 0), s), "Lunch end");
    }

    private static void Break_ValidateRejectsBadRanges()
    {
        Validate(Lunch(60), KnownThemes());
        Validate(new ReminderSettings { BreakEnabled = true, BreakStartHour = 17, BreakEndHour = 24 }, KnownThemes());
        foreach (int[] bad in new int[][] {
            new int[] { 13, 0, 12, 0 }, new int[] { 12, 0, 12, 0 }, new int[] { 24, 0, 24, 0 }, new int[] { 12, 60, 13, 0 },
            new int[] { 12, 0, 13, 60 }, new int[] { 12, 0, 25, 0 }, new int[] { 12, 0, 24, 30 }, new int[] { -1, 0, 13, 0 } })
        {
            int[] r = bad;
            ThrowsArgument(delegate { Validate(new ReminderSettings { BreakEnabled = true, BreakStartHour = r[0], BreakStartMinute = r[1], BreakEndHour = r[2], BreakEndMinute = r[3] }, KnownThemes()); }, "Break " + String.Join(",", Array.ConvertAll(r, x => x.ToString())));
        }
        // A break may not swallow the whole allowed time.
        ThrowsArgument(delegate { Validate(new ReminderSettings { BreakEnabled = true, BreakStartHour = 8, BreakEndHour = 19 }, KnownThemes()); }, "Break covers the window");
        ThrowsArgument(delegate { Validate(new ReminderSettings { LimitHours = false, BreakEnabled = true, BreakStartHour = 0, BreakEndHour = 24 }, KnownThemes()); }, "Break covers the whole day");
        // Same ranges are fine while the break is switched off.
        Validate(new ReminderSettings { BreakEnabled = false, BreakStartHour = 13, BreakEndHour = 12 }, KnownThemes());
    }

    // Several quiet periods from the Breaks list (the 325 UI: ＋ adds a row, － removes it).
    // Each window is { startHour, startMinute, endHour, endMinute }.
    private static ReminderSettings Lunches(int intervalMinutes, params int[][] windows)
    {
        ReminderSettings s = new ReminderSettings { IntervalMinutes = intervalMinutes, BreakEnabled = true };
        s.Breaks = new List<BreakWindow>();
        foreach (int[] w in windows)
            s.Breaks.Add(new BreakWindow { StartHour = w[0], StartMinute = w[1], EndHour = w[2], EndMinute = w[3] });
        return s;
    }

    private static void Breaks_ListSuppliesSeveralQuietPeriods()
    {
        // Lunch 12:00-13:00 plus a tea break 15:00-15:30 on a 30-minute timetable anchored 09:00.
        ReminderSettings s = Lunches(30, new int[] { 12, 0, 13, 0 }, new int[] { 15, 0, 15, 30 });
        Equal(new DateTime(2026, 11, 9, 13, 0, 0), Next(new DateTime(2026, 11, 9, 11, 45, 0), s), "The 12:00 slot waits for lunch to end");
        Equal(new DateTime(2026, 11, 9, 15, 30, 0), Next(new DateTime(2026, 11, 9, 14, 45, 0), s), "The 15:00 slot waits for the tea break to end");
        Equal(false, ReminderSchedule.IsAllowed(new DateTime(2026, 11, 9, 15, 15, 0), s), "Inside the second period");
        Equal(true, ReminderSchedule.IsAllowed(new DateTime(2026, 11, 9, 15, 30, 0), s), "At the second period's end");
        Equal(true, ReminderSchedule.IsAllowed(new DateTime(2026, 11, 9, 14, 0, 0), s), "Between the two periods");
        // Back-to-back periods: 12:00-13:00 followed by 13:00-14:00 push a noon slot to 14:00.
        ReminderSettings chained = Lunches(60, new int[] { 12, 0, 13, 0 }, new int[] { 13, 0, 14, 0 });
        Equal(new DateTime(2026, 11, 9, 14, 0, 0), Next(new DateTime(2026, 11, 9, 11, 30, 0), chained), "Chained periods both apply");
        Validate(s, KnownThemes());
        Validate(chained, KnownThemes());
    }

    private static void Breaks_EmptyListMeansNoQuietPeriod()
    {
        // An explicit empty list means "the feature is on but nothing is excluded" — unlike null,
        // which stands for an old single-period settings file and falls back to the legacy fields.
        ReminderSettings s = new ReminderSettings { IntervalMinutes = 60, BreakEnabled = true, Breaks = new List<BreakWindow>() };
        Equal(new DateTime(2026, 11, 9, 12, 0, 0), Next(new DateTime(2026, 11, 9, 11, 0, 0), s), "Noon stays a normal slot");
        Equal(true, ReminderSchedule.IsAllowed(new DateTime(2026, 11, 9, 12, 30, 0), s), "Noon is allowed");
        Validate(s, KnownThemes());
    }

    private static void Breaks_ValidateRejectsCoverageByTwoBreaks()
    {
        // Two periods that together swallow the window 09:00-18:00 are refused…
        ThrowsArgument(delegate { Validate(Lunches(60, new int[] { 8, 0, 12, 30 }, new int[] { 12, 30, 19, 0 }), KnownThemes()); }, "Two periods cover the window");
        // …but the same pair with a gap between them is fine.
        Validate(Lunches(60, new int[] { 8, 0, 12, 30 }, new int[] { 12, 45, 19, 0 }), KnownThemes());
        // Overlapping periods are merged for the same check.
        ThrowsArgument(delegate { Validate(Lunches(60, new int[] { 8, 0, 14, 0 }, new int[] { 13, 0, 19, 0 }), KnownThemes()); }, "Overlapping periods cover the window");
        // A single bad entry in the list is refused.
        ThrowsArgument(delegate { Validate(Lunches(60, new int[] { 12, 0, 13, 0 }, new int[] { 14, 0, 13, 0 }), KnownThemes()); }, "Second period ends before it starts");
    }

    private static void Breaks_ValidateRejectsTooManyEntries()
    {
        ReminderSettings s = new ReminderSettings { IntervalMinutes = 60, BreakEnabled = true, Breaks = new List<BreakWindow>() };
        for (int i = 0; i < 9; i++)
            s.Breaks.Add(new BreakWindow { StartHour = 9 + i, StartMinute = 0, EndHour = 9 + i, EndMinute = 30 });
        ThrowsArgument(delegate { Validate(s, KnownThemes()); }, "Nine quiet periods");
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
            // 절대 시각표: 시작 시각에 간격 배수를 붙인 슬롯들. slot = from 다음의 첫 슬롯(규칙 적용 전).
            DateTime anchor = from.Date + new TimeSpan(settings.StartHour, settings.StartMinute, 0);
            long intervalTicks = TimeSpan.FromMinutes(interval).Ticks;
            long diff = from.Ticks - anchor.Ticks;
            long steps = diff / intervalTicks;
            if (diff % intervalTicks != 0 && diff < 0) steps -= 1;
            DateTime slot = anchor.AddTicks((steps + 1) * intervalTicks);
            DateTime actual = Next(from, settings);
            Equal(true, actual > from, "Next must be strictly after from");
            Equal(true, IsAllowed(actual, settings), "Next must be allowed");
            Equal(true, actual >= slot, "Next must not skip the timetable slot it owes");
            Equal(from.Kind, actual.Kind, "Sweep preserves kind");
            if (actual > slot)
                Equal(false, IsAllowed(actual.AddTicks(-1), settings), "Clamping must choose the first allowed instant");
            else
                Equal(true, IsAllowed(slot, settings), "An allowed slot is not shifted");
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
