using System.Resources;

namespace KeyStats.Properties;

// Hand-written accessor class. Each migration task adds new properties here.
// Returns the key name as fallback if a translation is missing — surfaces gaps loudly.
public static class Strings
{
    private static readonly ResourceManager Rm =
        new ResourceManager("KeyStats.Properties.Strings", typeof(Strings).Assembly);

    private static string Get(string key) => Rm.GetString(key) ?? key;

    public static string App_Name => Get(nameof(App_Name));
    public static string Common_Cancel => Get(nameof(Common_Cancel));
    public static string Common_Open => Get(nameof(Common_Open));
    public static string Common_Import => Get(nameof(Common_Import));
    public static string Common_Export => Get(nameof(Common_Export));
    public static string Error_AppAlreadyRunning => Get(nameof(Error_AppAlreadyRunning));
    public static string Error_StartupFailedFormat => Get(nameof(Error_StartupFailedFormat));
    public static string Error_AppErrorTitle => Get(nameof(Error_AppErrorTitle));
    public static string Error_ImportInvalidFormat => Get(nameof(Error_ImportInvalidFormat));
    public static string Error_ImportUnsupportedVersion => Get(nameof(Error_ImportUnsupportedVersion));
    public static string Error_ImportEmpty => Get(nameof(Error_ImportEmpty));
    public static string Error_ImportDisabledWhileSyncing => Get(nameof(Error_ImportDisabledWhileSyncing));

    public static string Notif_ThresholdTitle => Get(nameof(Notif_ThresholdTitle));
    public static string Notif_KeyThresholdReachedFormat => Get(nameof(Notif_KeyThresholdReachedFormat));
    public static string Notif_ClickThresholdReachedFormat => Get(nameof(Notif_ClickThresholdReachedFormat));

    public static string Tray_OpenMainWindow => Get(nameof(Tray_OpenMainWindow));
    public static string Tray_ShowFloatingStats => Get(nameof(Tray_ShowFloatingStats));
    public static string Tray_Settings => Get(nameof(Tray_Settings));
    public static string Tray_StartAtLogin => Get(nameof(Tray_StartAtLogin));
    public static string Tray_KeyHistory => Get(nameof(Tray_KeyHistory));
    public static string Tray_Quit => Get(nameof(Tray_Quit));

    public static string Toast_ExportSuccess_Title => Get(nameof(Toast_ExportSuccess_Title));
    public static string Toast_ExportSuccess_BodyFormat => Get(nameof(Toast_ExportSuccess_BodyFormat));
    public static string Toast_ExportFailed_Title => Get(nameof(Toast_ExportFailed_Title));
    public static string Toast_ExportFailed_BodyFormat => Get(nameof(Toast_ExportFailed_BodyFormat));
    public static string Toast_ImportSuccess_Title => Get(nameof(Toast_ImportSuccess_Title));
    public static string Toast_ImportSuccess_BodyFormat => Get(nameof(Toast_ImportSuccess_BodyFormat));
    public static string Toast_ImportFailed_Title => Get(nameof(Toast_ImportFailed_Title));
    public static string Toast_ImportFailed_BodyFormat => Get(nameof(Toast_ImportFailed_BodyFormat));
    public static string ImportMode_Overwrite => Get(nameof(ImportMode_Overwrite));
    public static string ImportMode_Merge => Get(nameof(ImportMode_Merge));

    public static string Shortcut_Description => Get(nameof(Shortcut_Description));

    public static string Dialog_ExportTitle => Get(nameof(Dialog_ExportTitle));
    public static string Dialog_ImportTitle => Get(nameof(Dialog_ImportTitle));
    public static string Dialog_JsonFilter => Get(nameof(Dialog_JsonFilter));

    public static string Settings_Language => Get(nameof(Settings_Language));
    public static string Settings_LanguageDescription => Get(nameof(Settings_LanguageDescription));
    public static string Settings_Language_System => Get(nameof(Settings_Language_System));
    public static string Language_RestartPromptTitle => Get(nameof(Language_RestartPromptTitle));
    public static string Language_RestartPromptMessage => Get(nameof(Language_RestartPromptMessage));

    public static string Settings_WindowTitle => Get(nameof(Settings_WindowTitle));
    public static string Settings_HeaderTitle => Get(nameof(Settings_HeaderTitle));
    public static string Settings_HeaderSubtitle => Get(nameof(Settings_HeaderSubtitle));
    public static string Settings_GitHubTooltip => Get(nameof(Settings_GitHubTooltip));
    public static string Settings_DataImportExport => Get(nameof(Settings_DataImportExport));
    public static string Settings_DataImportExportDesc => Get(nameof(Settings_DataImportExportDesc));
    public static string Settings_Notifications => Get(nameof(Settings_Notifications));
    public static string Settings_NotificationsDesc => Get(nameof(Settings_NotificationsDesc));
    public static string Settings_DistanceCalibration => Get(nameof(Settings_DistanceCalibration));
    public static string Settings_DistanceCalibrationDesc => Get(nameof(Settings_DistanceCalibrationDesc));
    public static string Settings_VersionFormat => Get(nameof(Settings_VersionFormat));
    public static string Settings_OpenGitHubFailedMessage => Get(nameof(Settings_OpenGitHubFailedMessage));
    public static string Settings_Sync => Get(nameof(Settings_Sync));
    public static string Settings_SyncDesc => Get(nameof(Settings_SyncDesc));
    public static string Settings_SyncUnavailable => Get(nameof(Settings_SyncUnavailable));
    public static string Settings_FloatingStats => Get(nameof(Settings_FloatingStats));
    public static string Settings_FloatingStatsDesc => Get(nameof(Settings_FloatingStatsDesc));

    public static string Sync_WindowTitle => Get(nameof(Sync_WindowTitle));
    public static string Sync_HeaderTitle => Get(nameof(Sync_HeaderTitle));
    public static string Sync_HeaderSubtitle => Get(nameof(Sync_HeaderSubtitle));
    public static string Sync_ServiceUnavailable => Get(nameof(Sync_ServiceUnavailable));
    public static string Sync_CreateTitle => Get(nameof(Sync_CreateTitle));
    public static string Sync_CreateDescription => Get(nameof(Sync_CreateDescription));
    public static string Sync_CreateButton => Get(nameof(Sync_CreateButton));
    public static string Sync_JoinTitle => Get(nameof(Sync_JoinTitle));
    public static string Sync_JoinDescription => Get(nameof(Sync_JoinDescription));
    public static string Sync_BeginPairingButton => Get(nameof(Sync_BeginPairingButton));
    public static string Sync_CompletePairingButton => Get(nameof(Sync_CompletePairingButton));
    public static string Sync_RecoverTitle => Get(nameof(Sync_RecoverTitle));
    public static string Sync_RecoverDescription => Get(nameof(Sync_RecoverDescription));
    public static string Sync_RecoverButton => Get(nameof(Sync_RecoverButton));
    public static string Sync_StatusTitle => Get(nameof(Sync_StatusTitle));
    public static string Sync_NowButton => Get(nameof(Sync_NowButton));
    public static string Sync_InProgressStatus => Get(nameof(Sync_InProgressStatus));
    public static string Sync_ProgressFormat => Get(nameof(Sync_ProgressFormat));
    public static string Sync_StatusServiceNotConfigured => Get(nameof(Sync_StatusServiceNotConfigured));
    public static string Sync_StatusServiceNotConfiguredDetail => Get(nameof(Sync_StatusServiceNotConfiguredDetail));
    public static string Sync_StatusNeedsRepair => Get(nameof(Sync_StatusNeedsRepair));
    public static string Sync_StatusNeedsRepairDetail => Get(nameof(Sync_StatusNeedsRepairDetail));
    public static string Sync_StatusSyncingDetail => Get(nameof(Sync_StatusSyncingDetail));
    public static string Sync_StatusFailed => Get(nameof(Sync_StatusFailed));
    public static string Sync_StatusOff => Get(nameof(Sync_StatusOff));
    public static string Sync_StatusOffDetail => Get(nameof(Sync_StatusOffDetail));
    public static string Sync_StatusSingleDevice => Get(nameof(Sync_StatusSingleDevice));
    public static string Sync_StatusSingleDeviceDetail => Get(nameof(Sync_StatusSingleDeviceDetail));
    public static string Sync_StatusOn => Get(nameof(Sync_StatusOn));
    public static string Sync_StatusLastSyncFormat => Get(nameof(Sync_StatusLastSyncFormat));
    public static string Sync_StatusNotYetSynced => Get(nameof(Sync_StatusNotYetSynced));
    public static string Sync_CooldownFormat => Get(nameof(Sync_CooldownFormat));
    public static string Sync_BootstrapPendingStatus => Get(nameof(Sync_BootstrapPendingStatus));
    public static string Sync_RetryBootstrapButton => Get(nameof(Sync_RetryBootstrapButton));
    public static string Sync_RepairRequired => Get(nameof(Sync_RepairRequired));
    public static string Sync_ClearRepairButton => Get(nameof(Sync_ClearRepairButton));
    public static string Sync_ClearRepairConfirm => Get(nameof(Sync_ClearRepairConfirm));
    public static string Sync_RecoveryReplaceTitle => Get(nameof(Sync_RecoveryReplaceTitle));
    public static string Sync_RecoveryReplaceMessage => Get(nameof(Sync_RecoveryReplaceMessage));
    public static string Sync_RecoveryReplaceConfirm => Get(nameof(Sync_RecoveryReplaceConfirm));
    public static string Sync_ApproveTitle => Get(nameof(Sync_ApproveTitle));
    public static string Sync_ApproveDescription => Get(nameof(Sync_ApproveDescription));
    public static string Sync_ApproveButton => Get(nameof(Sync_ApproveButton));
    public static string Sync_RecoveryCodeTitle => Get(nameof(Sync_RecoveryCodeTitle));
    public static string Sync_RecoveryCodeWarning => Get(nameof(Sync_RecoveryCodeWarning));
    public static string Sync_ShowRecoveryCodeButton => Get(nameof(Sync_ShowRecoveryCodeButton));
    public static string Sync_LeaveButton => Get(nameof(Sync_LeaveButton));
    public static string Sync_LeaveConfirm => Get(nameof(Sync_LeaveConfirm));
    public static string Sync_DeleteVaultButton => Get(nameof(Sync_DeleteVaultButton));
    public static string Sync_DeleteVaultConfirm => Get(nameof(Sync_DeleteVaultConfirm));
    public static string Sync_SingleDeviceStatus => Get(nameof(Sync_SingleDeviceStatus));
    public static string Sync_DeviceCountFormat => Get(nameof(Sync_DeviceCountFormat));
    public static string Sync_LastSuccessFormat => Get(nameof(Sync_LastSuccessFormat));
    public static string Sync_NeverSynced => Get(nameof(Sync_NeverSynced));
    public static string Sync_DevicesTitle => Get(nameof(Sync_DevicesTitle));
    public static string Sync_ThisDevice => Get(nameof(Sync_ThisDevice));
    public static string Sync_RevokeButton => Get(nameof(Sync_RevokeButton));
    public static string Sync_RevokeConfirmFormat => Get(nameof(Sync_RevokeConfirmFormat));
    public static string Sync_SafetyCodeTitle => Get(nameof(Sync_SafetyCodeTitle));
    public static string Sync_SafetyCodeConfirmFormat => Get(nameof(Sync_SafetyCodeConfirmFormat));
    public static string Sync_ApprovalComplete => Get(nameof(Sync_ApprovalComplete));
    public static string Sync_GenericError => Get(nameof(Sync_GenericError));

    public static string Stats_TodayHeader => Get(nameof(Stats_TodayHeader));
    public static string Stats_KeyPresses => Get(nameof(Stats_KeyPresses));
    public static string Stats_MouseClicks => Get(nameof(Stats_MouseClicks));
    public static string Stats_MouseClickDetail => Get(nameof(Stats_MouseClickDetail));
    public static string Click_Left => Get(nameof(Click_Left));
    public static string Click_Middle => Get(nameof(Click_Middle));
    public static string Click_Right => Get(nameof(Click_Right));
    public static string Click_Back => Get(nameof(Click_Back));
    public static string Click_Forward => Get(nameof(Click_Forward));
    public static string Stats_MouseDistance => Get(nameof(Stats_MouseDistance));
    public static string Stats_ScrollDistance => Get(nameof(Stats_ScrollDistance));
    public static string Stats_KeyBreakdown => Get(nameof(Stats_KeyBreakdown));
    public static string KeyBreakdown_Empty => Get(nameof(KeyBreakdown_Empty));
    public static string Stats_KeyboardHeatmap => Get(nameof(Stats_KeyboardHeatmap));
    public static string Stats_KeyHistory => Get(nameof(Stats_KeyHistory));
    public static string Stats_ActiveApps => Get(nameof(Stats_ActiveApps));
    public static string Stats_AppStatsDetail => Get(nameof(Stats_AppStatsDetail));
    public static string Stats_HistoryHeader => Get(nameof(Stats_HistoryHeader));
    public static string Chart_Line => Get(nameof(Chart_Line));
    public static string Chart_Bar => Get(nameof(Chart_Bar));
    public static string Range_7Days => Get(nameof(Range_7Days));
    public static string Range_30Days => Get(nameof(Range_30Days));
    public static string Metric_Clicks => Get(nameof(Metric_Clicks));
    public static string Metric_Keys => Get(nameof(Metric_Keys));
    public static string Metric_Move => Get(nameof(Metric_Move));
    public static string Metric_Scroll => Get(nameof(Metric_Scroll));
    public static string Stats_PeakKpsTooltipLabel => Get(nameof(Stats_PeakKpsTooltipLabel));
    public static string Stats_PeakCpsTooltipLabel => Get(nameof(Stats_PeakCpsTooltipLabel));
    public static string FloatingStats_WindowTitle => Get(nameof(FloatingStats_WindowTitle));
    public static string FloatingStats_Today => Get(nameof(FloatingStats_Today));
    public static string FloatingStats_PrimaryMetric => Get(nameof(FloatingStats_PrimaryMetric));
    public static string FloatingStats_SecondaryMetric => Get(nameof(FloatingStats_SecondaryMetric));
    public static string FloatingStats_Layout => Get(nameof(FloatingStats_Layout));
    public static string FloatingStats_FontSize => Get(nameof(FloatingStats_FontSize));
    public static string FloatingStats_SingleRow => Get(nameof(FloatingStats_SingleRow));
    public static string FloatingStats_DoubleRow => Get(nameof(FloatingStats_DoubleRow));
    public static string FloatingStats_AlwaysOnTop => Get(nameof(FloatingStats_AlwaysOnTop));
    public static string FloatingStats_LockPosition => Get(nameof(FloatingStats_LockPosition));
    public static string FloatingStats_OpenDetails => Get(nameof(FloatingStats_OpenDetails));
    public static string FloatingStats_Hide => Get(nameof(FloatingStats_Hide));

    public static string AppStats_WindowTitle => Get(nameof(AppStats_WindowTitle));
    public static string AppStats_HeaderTitle => Get(nameof(AppStats_HeaderTitle));
    public static string AppStats_HeaderSubtitle => Get(nameof(AppStats_HeaderSubtitle));
    public static string AppStats_RangeToday => Get(nameof(AppStats_RangeToday));
    public static string AppStats_Range7Days => Get(nameof(AppStats_Range7Days));
    public static string AppStats_Range30Days => Get(nameof(AppStats_Range30Days));
    public static string AppStats_RangeAll => Get(nameof(AppStats_RangeAll));
    public static string AppStats_ColumnApp => Get(nameof(AppStats_ColumnApp));
    public static string AppStats_ColumnKeys => Get(nameof(AppStats_ColumnKeys));
    public static string AppStats_ColumnClicks => Get(nameof(AppStats_ColumnClicks));
    public static string AppStats_ColumnScroll => Get(nameof(AppStats_ColumnScroll));
    public static string AppStats_SummaryFormat => Get(nameof(AppStats_SummaryFormat));
    public static string AppStats_Empty => Get(nameof(AppStats_Empty));
    public static string AppStats_UnknownApp => Get(nameof(AppStats_UnknownApp));
    public static string History_TotalFormat => Get(nameof(History_TotalFormat));
    public static string History_SeriesSynced => Get(nameof(History_SeriesSynced));
    public static string KeyHistory_Empty => Get(nameof(KeyHistory_Empty));
    public static string KeyHistory_SummaryFormat => Get(nameof(KeyHistory_SummaryFormat));
    public static string PieChart_Empty => Get(nameof(PieChart_Empty));
    public static string PieChart_CountFormat => Get(nameof(PieChart_CountFormat));
    public static string PieChart_PercentFormat => Get(nameof(PieChart_PercentFormat));

    public static string Heatmap_WindowTitle => Get(nameof(Heatmap_WindowTitle));
    public static string Heatmap_HeaderTitle => Get(nameof(Heatmap_HeaderTitle));
    public static string Heatmap_HeaderSubtitle => Get(nameof(Heatmap_HeaderSubtitle));
    public static string Heatmap_PrevDay => Get(nameof(Heatmap_PrevDay));
    public static string Heatmap_NextDay => Get(nameof(Heatmap_NextDay));
    public static string Heatmap_BackToToday => Get(nameof(Heatmap_BackToToday));
    public static string Heatmap_NoData => Get(nameof(Heatmap_NoData));
    public static string Heatmap_SummaryFormat => Get(nameof(Heatmap_SummaryFormat));
    public static string Heatmap_DatePickerTooltip => Get(nameof(Heatmap_DatePickerTooltip));

    public static string KeyHistory_WindowTitle => Get(nameof(KeyHistory_WindowTitle));
    public static string KeyHistory_HeaderTitle => Get(nameof(KeyHistory_HeaderTitle));
    public static string KeyHistory_HeaderSubtitle => Get(nameof(KeyHistory_HeaderSubtitle));
    public static string KeyHistory_PieChartTitle => Get(nameof(KeyHistory_PieChartTitle));
    public static string KeyHistory_BarChartTitle => Get(nameof(KeyHistory_BarChartTitle));
    public static string History_Range_Today => Get(nameof(History_Range_Today));
    public static string History_Range_Last7Days => Get(nameof(History_Range_Last7Days));
    public static string History_Range_Last30Days => Get(nameof(History_Range_Last30Days));
    public static string History_Range_All => Get(nameof(History_Range_All));

    public static string Calibration_WindowTitle => Get(nameof(Calibration_WindowTitle));
    public static string Calibration_HeaderTitle => Get(nameof(Calibration_HeaderTitle));
    public static string Calibration_Start => Get(nameof(Calibration_Start));
    public static string Calibration_Finish => Get(nameof(Calibration_Finish));

    public static string Calibration_InstructionEnter => Get(nameof(Calibration_InstructionEnter));
    public static string Calibration_LengthLabel => Get(nameof(Calibration_LengthLabel));
    public static string Calibration_StepsLabel => Get(nameof(Calibration_StepsLabel));
    public static string Calibration_StatusIdle => Get(nameof(Calibration_StatusIdle));
    public static string Calibration_StatusRecording => Get(nameof(Calibration_StatusRecording));
    public static string Calibration_StatusPressEnterFirst => Get(nameof(Calibration_StatusPressEnterFirst));
    public static string Calibration_StatusMovementTooShort => Get(nameof(Calibration_StatusMovementTooShort));
    public static string Calibration_StatusInvalidLength => Get(nameof(Calibration_StatusInvalidLength));
    public static string Calibration_StatusComplete => Get(nameof(Calibration_StatusComplete));
    public static string Calibration_DisplayUnitLabel => Get(nameof(Calibration_DisplayUnitLabel));
    public static string Calibration_UnitAuto => Get(nameof(Calibration_UnitAuto));
    public static string Calibration_UnitPixel => Get(nameof(Calibration_UnitPixel));
    public static string Calibration_CurrentResultLabel => Get(nameof(Calibration_CurrentResultLabel));
    public static string Calibration_PixelsLabelEmpty => Get(nameof(Calibration_PixelsLabelEmpty));
    public static string Calibration_PixelsLabelFormat => Get(nameof(Calibration_PixelsLabelFormat));
    public static string Calibration_ScaleLabelEmpty => Get(nameof(Calibration_ScaleLabelEmpty));
    public static string Calibration_ScaleLabelFormat => Get(nameof(Calibration_ScaleLabelFormat));
    public static string Calibration_TipFooter => Get(nameof(Calibration_TipFooter));

    public static string NotifSettings_WindowTitle => Get(nameof(NotifSettings_WindowTitle));
    public static string NotifSettings_HeaderTitle => Get(nameof(NotifSettings_HeaderTitle));
    public static string NotifSettings_HeaderSubtitle => Get(nameof(NotifSettings_HeaderSubtitle));
    public static string NotifSettings_Enable => Get(nameof(NotifSettings_Enable));
    public static string NotifSettings_KeyCardTitle => Get(nameof(NotifSettings_KeyCardTitle));
    public static string NotifSettings_ClickCardTitle => Get(nameof(NotifSettings_ClickCardTitle));
    public static string NotifSettings_EveryFormat => Get(nameof(NotifSettings_EveryFormat));
    public static string NotifSettings_Hint => Get(nameof(NotifSettings_Hint));

    public static string ImportDialog_HeaderTitle => Get(nameof(ImportDialog_HeaderTitle));
    public static string ImportDialog_Description => Get(nameof(ImportDialog_Description));
    public static string ImportDialog_OverwriteDesc => Get(nameof(ImportDialog_OverwriteDesc));
    public static string ImportDialog_MergeDesc => Get(nameof(ImportDialog_MergeDesc));

    public static string Confirm_DefaultHeaderTitle => Get(nameof(Confirm_DefaultHeaderTitle));
    public static string Confirm_DefaultMessage => Get(nameof(Confirm_DefaultMessage));
    public static string Confirm_DefaultTitle => Get(nameof(Confirm_DefaultTitle));
    public static string Confirm_DefaultConfirm => Get(nameof(Confirm_DefaultConfirm));

    // ===== Game session tracking =====

    public static string Game_LoL_Name => Get(nameof(Game_LoL_Name));

    public static string Tray_GameStats => Get(nameof(Tray_GameStats));
    public static string Tray_GameOverlay => Get(nameof(Tray_GameOverlay));

    public static string GameStats_WindowTitle => Get(nameof(GameStats_WindowTitle));
    public static string GameStats_HeaderTitle => Get(nameof(GameStats_HeaderTitle));
    public static string GameStats_HeaderSubtitle => Get(nameof(GameStats_HeaderSubtitle));

    public static string GameStats_StatusIdle => Get(nameof(GameStats_StatusIdle));
    public static string GameStats_StatusWaiting => Get(nameof(GameStats_StatusWaiting));
    public static string GameStats_StatusActive => Get(nameof(GameStats_StatusActive));

    public static string GameStats_Duration => Get(nameof(GameStats_Duration));
    public static string GameStats_Apm => Get(nameof(GameStats_Apm));
    public static string GameStats_Keys => Get(nameof(GameStats_Keys));
    public static string GameStats_Clicks => Get(nameof(GameStats_Clicks));
    public static string GameStats_MouseDistance => Get(nameof(GameStats_MouseDistance));
    public static string GameStats_AverageApm => Get(nameof(GameStats_AverageApm));
    public static string GameStats_LastMinuteApm => Get(nameof(GameStats_LastMinuteApm));
    public static string GameStats_PeakApm => Get(nameof(GameStats_PeakApm));
    public static string GameStats_PeakKpm => Get(nameof(GameStats_PeakKpm));
    public static string GameStats_PeakCpm => Get(nameof(GameStats_PeakCpm));
    public static string GameStats_PeakKps => Get(nameof(GameStats_PeakKps));

    public static string GameStats_SessionList => Get(nameof(GameStats_SessionList));
    public static string GameStats_Empty => Get(nameof(GameStats_Empty));
    public static string GameStats_ClearHistory => Get(nameof(GameStats_ClearHistory));
    public static string GameStats_ClearConfirmTitle => Get(nameof(GameStats_ClearConfirmTitle));
    public static string GameStats_ClearConfirmMessage => Get(nameof(GameStats_ClearConfirmMessage));
    public static string GameStats_SummaryFormat => Get(nameof(GameStats_SummaryFormat));
    public static string GameStats_TodayTotalsFormat => Get(nameof(GameStats_TodayTotalsFormat));
    public static string GameStats_SessionMetaFormat => Get(nameof(GameStats_SessionMetaFormat));
    public static string GameStats_DetailTitle => Get(nameof(GameStats_DetailTitle));
    public static string GameStats_TopKeysTitle => Get(nameof(GameStats_TopKeysTitle));
    public static string GameStats_TimelineTitle => Get(nameof(GameStats_TimelineTitle));
    public static string GameStats_HeatmapTitle => Get(nameof(GameStats_HeatmapTitle));
    public static string GameStats_NoTimeline => Get(nameof(GameStats_NoTimeline));
    public static string GameStats_MinuteTooltipFormat => Get(nameof(GameStats_MinuteTooltipFormat));
    public static string GameStats_DoubleClickHint => Get(nameof(GameStats_DoubleClickHint));

    public static string GameStats_OptionsTitle => Get(nameof(GameStats_OptionsTitle));
    public static string GameStats_OverlayToggle => Get(nameof(GameStats_OverlayToggle));
    public static string GameStats_FrontmostToggle => Get(nameof(GameStats_FrontmostToggle));
    public static string GameStats_MergeToggle => Get(nameof(GameStats_MergeToggle));
    public static string GameStats_MergeHint => Get(nameof(GameStats_MergeHint));

    public static string GameStats_CaptureHealth => Get(nameof(GameStats_CaptureHealth));
    public static string GameStats_ChannelFormat => Get(nameof(GameStats_ChannelFormat));
    public static string GameStats_ChannelHook => Get(nameof(GameStats_ChannelHook));
    public static string GameStats_ChannelRawInput => Get(nameof(GameStats_ChannelRawInput));
    public static string GameStats_CaptureHealthy => Get(nameof(GameStats_CaptureHealthy));
    public static string GameStats_HookReinstalledFormat => Get(nameof(GameStats_HookReinstalledFormat));
    public static string GameStats_LastInputFormat => Get(nameof(GameStats_LastInputFormat));
    public static string GameStats_NoInputWarning => Get(nameof(GameStats_NoInputWarning));

    public static string GameStats_ElevatedBadge => Get(nameof(GameStats_ElevatedBadge));
    public static string GameStats_NotElevatedBadge => Get(nameof(GameStats_NotElevatedBadge));
    public static string GameStats_RestartElevated => Get(nameof(GameStats_RestartElevated));

    public static string GameOverlay_WindowTitle => Get(nameof(GameOverlay_WindowTitle));

    // ===== Daily insights =====

    public static string Tray_Insights => Get(nameof(Tray_Insights));
    public static string Insights_WindowTitle => Get(nameof(Insights_WindowTitle));
    public static string Insights_HeaderTitle => Get(nameof(Insights_HeaderTitle));
    public static string Insights_HeaderSubtitle => Get(nameof(Insights_HeaderSubtitle));

    public static string Insights_TypingTitle => Get(nameof(Insights_TypingTitle));
    public static string Insights_BackspaceRatio => Get(nameof(Insights_BackspaceRatio));
    public static string Insights_BackspaceDetailFormat => Get(nameof(Insights_BackspaceDetailFormat));
    public static string Insights_ShortcutKeys => Get(nameof(Insights_ShortcutKeys));

    public static string Insights_FocusTitle => Get(nameof(Insights_FocusTitle));
    public static string Insights_AppSwitches => Get(nameof(Insights_AppSwitches));
    public static string Insights_LongestFocus => Get(nameof(Insights_LongestFocus));
    public static string Insights_CurrentStreak => Get(nameof(Insights_CurrentStreak));
    public static string Insights_LongestStreak => Get(nameof(Insights_LongestStreak));

    public static string Insights_HourlyTitle => Get(nameof(Insights_HourlyTitle));
    public static string Insights_TopAppsTitle => Get(nameof(Insights_TopAppsTitle));
    public static string Insights_ActiveTimeFormat => Get(nameof(Insights_ActiveTimeFormat));
    public static string Insights_HourTooltipFormat => Get(nameof(Insights_HourTooltipFormat));
    public static string Insights_Empty => Get(nameof(Insights_Empty));

    public static string Insights_OptionsTitle => Get(nameof(Insights_OptionsTitle));
    public static string Insights_BreakToggle => Get(nameof(Insights_BreakToggle));
    public static string Insights_BreakThresholdFormat => Get(nameof(Insights_BreakThresholdFormat));
    public static string Insights_BreakReminderTitle => Get(nameof(Insights_BreakReminderTitle));
    public static string Insights_BreakReminderBodyFormat => Get(nameof(Insights_BreakReminderBodyFormat));
    public static string Insights_MinutesFormat => Get(nameof(Insights_MinutesFormat));
    public static string Insights_HoursMinutesFormat => Get(nameof(Insights_HoursMinutesFormat));
}
