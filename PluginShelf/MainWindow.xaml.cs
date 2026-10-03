using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PluginShelf.Models;
using PluginShelf.Services;
using WinForms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;

namespace PluginShelf;

public partial class MainWindow : Window
{
    private readonly PluginScanner _scanner = new();
    private readonly ObservableCollection<QuarantineEntry> _quarantineRows = new();
    private AppSettings _settings;
    private List<PluginCandidate> _lastCandidates = new();
    private List<PluginGroup> _groups = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _isArabic;
    private bool _hasScanned;
    private bool _hideIncludedGroups;
    private string _reviewFilter = "";

    private sealed class OverviewGroupRow
    {
        public string DisplayName { get; init; } = "";
        public string Vendor { get; init; } = "";
        public string CandidateCountLabel { get; init; } = "";
        public string MatchSummary { get; init; } = "";
    }

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();
        _isArabic = _settings.Language != "en";

        FitToScreenWorkArea();
        ApplyUiScale(_settings.UiScale, persist: false);

        RootsGrid.ItemsSource = _settings.Roots;
        QuarantineGrid.ItemsSource = _quarantineRows;
        OverviewGroupsGrid.ItemsSource = _groups;
        QuarantineGrid.SelectionChanged += (_, _) => UpdateRestoreButton();

        UpdateLanguage();
        RefreshQuarantine();
        UpdateMetrics(0, 0);
        NavigateTo("Overview");
    }

    private void FitToScreenWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        if (workArea.Width > 0 && workArea.Height > 0)
        {
            Width = Math.Min(Width, Math.Max(MinWidth, Math.Floor(workArea.Width * 0.94)));
            Height = Math.Min(Height, Math.Max(MinHeight, Math.Floor(workArea.Height * 0.92)));
        }
    }

    private void ApplyUiScale(double scale, bool persist)
    {
        var clamped = Math.Round(Math.Clamp(double.IsFinite(scale) ? scale : 1.0, 0.85, 1.40), 2);
        _settings.UiScale = clamped;
        if (RootScaleTransform is not null)
        {
            RootScaleTransform.ScaleX = clamped;
            RootScaleTransform.ScaleY = clamped;
        }
        if (ZoomResetButton is not null)
        {
            ZoomResetButton.Content = $"{(int)Math.Round(clamped * 100)}%";
        }
        if (persist)
        {
            SettingsService.Save(_settings);
        }
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) =>
        ApplyUiScale(_settings.UiScale + 0.10, persist: true);

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) =>
        ApplyUiScale(_settings.UiScale - 0.10, persist: true);

    private void ZoomResetButton_Click(object sender, RoutedEventArgs e) =>
        ApplyUiScale(1.0, persist: true);

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key is Key.OemPlus or Key.Add)
        {
            ApplyUiScale(_settings.UiScale + 0.10, persist: true);
            e.Handled = true;
        }
        else if (e.Key is Key.OemMinus or Key.Subtract)
        {
            ApplyUiScale(_settings.UiScale - 0.10, persist: true);
            e.Handled = true;
        }
        else if (e.Key is Key.D0 or Key.NumPad0)
        {
            ApplyUiScale(1.0, persist: true);
            e.Handled = true;
        }
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        _isArabic = !_isArabic;
        _settings.Language = _isArabic ? "ar" : "en";
        SettingsService.Save(_settings);
        UpdateLanguage();
        RenderGroups();
    }

    private void UpdateLanguage()
    {
        var flow = _isArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        RootLayout.FlowDirection = flow;
        OverviewGroupsGrid.FlowDirection = flow;
        QuarantineGrid.FlowDirection = flow;
        RootsGrid.FlowDirection = flow;

        AppNameText.Text = "Plugin Shelf";
        AppTaglineText.Text = L("AppTagline");
        TopStatusText.Text = L("PreviewStatus");
        SafeModeText.Text = L("SafeMode");
        LanguageButton.Content = _isArabic ? "English" : "العربية";
        WorkspaceLabel.Text = L("Workspace");
        OverviewNav.Content = "⌂    " + L("OverviewNav");
        ReviewNav.Content = "◉    " + L("ReviewNav");
        QuarantineNav.Content = "↺    " + L("QuarantineNav");
        SettingsNav.Content = "⚙    " + L("SettingsNav");
        PriorityTitleText.Text = L("PriorityTitle");
        PriorityHintText.Text = L("PriorityHint");
        WavesHintText.Text = L("WavesExcluded");

        OverviewTitle.Text = L("OverviewTitle");
        OverviewSubtitle.Text = L("OverviewSubtitle");
        ScanCardTitle.Text = L("ScanCardTitle");
        ScanCardDescription.Text = L("ScanCardDescription");
        ScanButton.Content = L("ScanButton");
        CancelScanButton.Content = L("CancelScan");
        PathsMetricLabel.Text = L("PathsMetricLabel");
        PathsMetricHint.Text = L("Configured");
        CandidatesMetricLabel.Text = L("CandidatesMetricLabel");
        CandidatesMetricHint.Text = L("RecognizedItems");
        GroupsMetricLabel.Text = L("GroupsMetricLabel");
        GroupsMetricHint.Text = L("NeedReview");
        QuarantineMetricLabel.Text = L("QuarantineMetricLabel");
        QuarantineMetricHint.Text = L("RestorableItems");
        RecentGroupsTitle.Text = L("RecentGroupsTitle");
        RecentGroupsSubtitle.Text = L("RecentGroupsSubtitle");
        OpenReviewButton.Content = L("OpenReview");
        OverviewNameColumn.Header = L("PlugInColumn");
        OverviewVendorColumn.Header = L("VendorColumn");
        OverviewCountColumn.Header = L("VariantsColumn");
        OverviewMatchColumn.Header = L("MatchColumn");

        ReviewTitle.Text = L("ReviewTitle");
        ReviewSubtitle.Text = L("ReviewSubtitle");
        SelectRecommendedButton.Content = L("SelectRecommended");
        ClearSelectionButton.Content = L("ClearSelection");
        ReviewRulesBannerText.Text = L("ReviewRulesBanner");
        ReviewSearchBox.ToolTip = L("SearchHint");
        ReviewFooterText.Text = L("ReviewFooter");

        QuarantineTitle.Text = L("QuarantineTitle");
        QuarantineSubtitle.Text = L("QuarantineSubtitle");
        QuarantineFooterText.Text = L("QuarantineFooter");
        RefreshQuarantineButton.Content = L("RefreshQuarantine");
        RestoreButton.Content = L("RestoreSelected");
        QNameColumn.Header = L("PlugInColumn");
        QFormatColumn.Header = L("FormatColumn");
        QArchColumn.Header = L("ArchitectureColumn");
        QMovedColumn.Header = L("MovedColumn");
        QPathColumn.Header = L("OriginalPathColumn");

        SettingsTitle.Text = L("SettingsTitle");
        SettingsSubtitle.Text = L("SettingsSubtitle");
        RootEnabledColumn.Header = L("EnabledColumn");
        RootPathColumn.Header = L("FolderPathColumn");
        RootKindColumn.Header = L("FormatScopeColumn");
        SettingsSafetyText.Text = L("SettingsSafety");
        ResetDefaultsButton.Content = L("ResetDefaults");
        AddFolderButton.Content = L("AddFolder");
        RemoveFolderButton.Content = L("RemoveFolder");
        SaveSettingsButton.Content = L("SaveSettings");

        if (!_hasScanned)
            ScanStatusText.Text = L("Ready");
        RefreshOverviewGroupRows();
        UpdateMetrics(_hasScanned ? _lastCandidates.Count(c => c.IsLikelyPlugin) : 0, _groups.Count);
        UpdateApplyButton();
        UpdateRestoreButton();
    }

    private string L(string key)
    {
        var pair = key switch
        {
            "AppTagline" => ("تنظيم آمن وواضح لمكتبة البلجنز", "A safer, clearer plug-in library"),
            "PreviewStatus" => ("راجع كل شيء أولًا · لا تتحرك الملفات دون موافقتك", "Preview first · nothing moves until you approve"),
            "SafeMode" => ("وضع آمن", "SAFE MODE"),
            "Workspace" => ("مساحة العمل", "WORKSPACE"),
            "OverviewNav" => ("نظرة عامة", "Overview"),
            "ReviewNav" => ("مراجعة الصيغ", "Review variants"),
            "QuarantineNav" => ("الحجر والاسترجاع", "Quarantine"),
            "SettingsNav" => ("الإعدادات", "Settings"),
            "PriorityTitle" => ("ترتيب اختيار النسخة", "Selection rules"),
            "PriorityHint" => (
                "1) المعمارية: x64 أولًا (وx86 فقط عند غياب x64)\n2) الصيغة: VST3 ← VST/DLL ← CLAP\n3) أحدث إصدار مقروء داخل الفئة الفائزة لنفس الجيل وI/O\n4) التعادل يُحسم تلقائيًا: الإصدار المقروء ثم المسار الأقرب؛ الأسماء الملتبسة ← مراجعة يدوية",
                "1) Architecture: x64 first (x86 only if absent)\n2) Format: VST3 → VST/DLL → CLAP\n3) Newest readable release in winning tier (same generation & I/O)\n4) Ties auto-resolve: readable release → nearest path; aliases → manual review"),
            "WavesExcluded" => ("Waves / WaveShell / WPAPI مستثناة · لا تُستخدم تواريخ الملفات", "Waves / WaveShell / WPAPI excluded · File dates are never used"),
            "OverviewTitle" => ("مكتبة البلجنز تحت السيطرة", "Your plug-in library, under control"),
            "OverviewSubtitle" => ("اكتشف تكرار الصيغ، راجع كل اقتراح يدويًا، واسترجع أي عنصر من الحجر القابل للاسترجاع.", "Find format duplicates, review every proposal manually, and restore anything from quarantine."),
            "ScanCardTitle" => ("ابدأ بفحص للقراءة فقط", "Start with a read-only scan"),
            "ScanCardDescription" => ("الفحص يقرأ بيانات الملفات والحزم فقط (PE وmoduleinfo.json). لا يشغّل كود أي بلجن ولا ينقل أو يحذف أي ملف.", "The scan reads static metadata only (PE headers and moduleinfo.json). It never loads plug-in code and never moves or deletes files."),
            "Ready" => ("جاهز للفحص.", "Ready when you are."),
            "Scanning" => ("جاري فحص المجلدات…", "Scanning folders…"),
            "ScanButton" => ("افحص المجلدات", "Scan folders"),
            "CancelScan" => ("إلغاء الفحص", "Cancel scan"),
            "PathsMetricLabel" => ("مجلدات البحث", "SCAN FOLDERS"),
            "Configured" => ("مُفعّلة للفحص", "enabled for scan"),
            "CandidatesMetricLabel" => ("عناصر معروفة", "CANDIDATES"),
            "RecognizedItems" => ("إضافات صوتية مكتشفة", "recognized plug-ins"),
            "GroupsMetricLabel" => ("مجموعات متكررة", "DUPLICATE GROUPS"),
            "NeedReview" => ("تحتاج مراجعتك", "ready for review"),
            "QuarantineMetricLabel" => ("في الحجر", "QUARANTINED"),
            "RestorableItems" => ("عناصر قابلة للاسترجاع", "restorable items"),
            "RecentGroupsTitle" => ("قائمة المراجعة", "Review queue"),
            "RecentGroupsSubtitle" => ("لن تدخل أي مجموعة في خطة الحجر إلا بعد تحديدها وموافقتك.", "No group enters the quarantine plan until you select and approve it."),
            "OpenReview" => ("افتح المراجعة", "Open review"),
            "PlugInColumn" => ("البلجن", "Plug-in"),
            "VendorColumn" => ("الشركة", "Vendor"),
            "VariantsColumn" => ("النسخ", "Variants"),
            "MatchColumn" => ("حالة المطابقة والترشيح", "Match & recommendation status"),
            "ReviewTitle" => ("مراجعة نسخ الصيغ", "Review format variants"),
            "ReviewSubtitle" => ("النسخة المقترحة مجرد توصية؛ ضمّن المجموعات التي تريد تنظيفها بنفسك ولا يتم النقل إلا بعد موافقتك.", "The suggested keeper is only a recommendation; opt in to the groups you want cleaned. Nothing moves without your approval."),
            "SelectRecommended" => ("حدّد التوصيات الواضحة", "Select strong recommendations"),
            "ClearSelection" => ("إلغاء التحديد", "Clear selection"),
            "HideIncluded" => ("إخفاء المجموعات المُضمّنة", "Hide included groups"),
            "ShowIncluded" => ("إظهار المجموعات المُضمّنة", "Show included groups"),
            "AllIncludedHidden" => ("كل المجموعات المطابقة مُضمّنة في الخطة ومخفية علشان تركّز على اللي فاضل للمراجعة اليدوية. اضغط «إظهار المجموعات المُضمّنة» لو حبيت تراجعها.", "All matching groups are included in the plan and hidden so you can focus on what still needs manual review. Press \"Show included groups\" to review them."),
            "ReviewRulesBanner" => (
                "القواعد الثابتة: x64 أولًا ← VST3 ثم VST/DLL ثم CLAP (حتى لو كان إصدار صيغة أدنى أعلى) ← أحدث إصدار مقروء بنفس الجيل ونوع I/O (Mono / Stereo / SC منفصلة). التعادل يُحسم تلقائيًا: الإصدار المقروء ثم المسار الأقرب/الأقصر؛ الأسماء الملتبسة فقط للمراجعة اليدوية. تواريخ الملفات لا تُستخدم أبدًا.",
                "Strict rules: x64 first → VST3 > VST/DLL > CLAP (even if a lower format has a higher version) → newest readable release for the exact generation & I/O (Mono / Stereo / SC stay separate). Ties auto-resolve: readable release first, then the nearest/shorter path; only ambiguous aliases need manual review. File dates are never used."),
            "SearchHint" => ("صفِّ النتائج حسب اسم البلجن أو الشركة…", "Filter groups by plug-in or vendor name…"),
            "IncludeGroup" => ("ضمّن هذه المجموعة في خطة الحجر (نقل النسخ غير المختارة إلى الحجر)", "Include this group in the quarantine plan (move non-selected variants to quarantine)"),
            "ReviewFooter" => ("المجموعات غير المحددة ستظل كما هي. الأسماء الملتبسة فقط هي ما تحتاج قرارًا يدويًا.", "Unselected groups stay untouched. Only ambiguous aliases require a manual decision."),
            "QuarantineTitle" => ("حجر قابل للاسترجاع", "Recoverable quarantine"),
            "QuarantineSubtitle" => ("لا يوجد حذف نهائي. يحتفظ السجل بالمسار الأصلي، والاسترجاع لا يستبدل أي ملف موجود.", "No permanent deletion. Original paths are recorded, and restore never overwrites an existing file."),
            "QuarantineFooter" => ("اختر عنصرًا أو أكثر لاسترجاعه إلى مساره الأصلي.", "Select one or more entries to restore to their original path."),
            "RefreshQuarantine" => ("تحديث السجل", "Refresh"),
            "RestoreSelected" => ("استرجع المحدد", "Restore selected"),
            "FormatColumn" => ("الصيغة", "Format"),
            "ArchitectureColumn" => ("المعمارية", "Architecture"),
            "MovedColumn" => ("تاريخ النقل", "Moved"),
            "OriginalPathColumn" => ("المسار الأصلي", "Original path"),
            "SettingsTitle" => ("إعدادات الفحص", "Scan settings"),
            "SettingsSubtitle" => ("أضف أو احذف مجلدات البحث المخصصة. يدعم الفحص التكراري صيغ VST/DLL وVST3 وCLAP.", "Add or remove custom scan folders. Recursive scanning supports VST/DLL, VST3, and CLAP."),
            "EnabledColumn" => ("مفعّل", "On"),
            "FolderPathColumn" => ("مسار المجلد", "Folder path"),
            "FormatScopeColumn" => ("نطاق الصيغ", "Known format scope"),
            "SettingsSafety" => ("يُستثنى Waves وWaveShell وWPAPI بالكامل. المجلدات المخصصة تُفحص تكراريًا للصيغ المدعومة فقط.", "Waves, WaveShell, and WPAPI are completely excluded. Custom folders are scanned recursively for supported formats only."),
            "ResetDefaults" => ("استعادة الافتراضي", "Reset defaults"),
            "AddFolder" => ("أضف مجلدًا", "Add folder"),
            "RemoveFolder" => ("احذف المحدد", "Remove selected"),
            "SaveSettings" => ("احفظ الإعدادات", "Save settings"),
            "NoGroupsTitle" => ("لا توجد مجموعات تكرار جاهزة للمراجعة", "No duplicate groups are ready for review"),
            "NoGroupsBody" => ("نفّذ فحصًا أولًا. العناصر الفريدة، وWaves، والملفات غير المؤكدة لن تُنقل.", "Run a scan first. Unique items, Waves, and unconfirmed files will not be moved."),
            "NoFilterMatches" => ("لا توجد مجموعات تطابق نص البحث الحالي.", "No groups match the current filter text."),
            "AliasWarning" => ("اسم ملتبس محتمل — يتطلب مراجعتك وتأكيدك اليدوي", "Possible name alias — requires your manual review and confirmation"),
            "TieWarningBadge" => ("لا يمكن حسم النسخة المُبقاة تلقائيًا — اختر يدويًا", "Keeper cannot be resolved automatically — manual choice required"),
            "StrongMatch" => ("تطابق موثوق للمنتج والجيل ونوع I/O", "Exact vendor, product, generation, and I/O match"),
            "ConfirmIdentity" => ("أؤكد يدويًا أن هذه العناصر لنفس البلجن ونفس الجيل ونفس هوية I/O", "I manually confirm these belong to the exact same plug-in, generation, and I/O variant"),
            "Recommended" => ("مقترحة للاحتفاظ", "RECOMMENDED KEEPER"),
            "WillKeepBadge" => ("سيتم الاحتفاظ بها", "KEEP"),
            "WillQuarantineBadge" => ("ستُنقل إلى الحجر", "TO QUARANTINE"),
            "VersionLabel" => ("الإصدار", "Version"),
            "NoVersion" => ("غير متاح", "not reported"),
            "ChooseTie" => ("تعذّر حسم النسخة المُبقاة تلقائيًا (مثلًا: معمارية غير معروفة)؛ اختر نسخة يدويًا للاحتفاظ بها (لا تُستخدم تواريخ الملفات).", "The keeper could not be resolved automatically (e.g. unknown architecture); choose one manually (file dates are never used)."),
            "ScanComplete" => ("اكتمل الفحص.", "Scan complete."),
            "ScanCanceled" => ("تم إلغاء الفحص؛ لم تُعدّل أي ملفات.", "Scan canceled; no files were changed."),
            "NoAction" => ("لا توجد نسخ محددة للنقل إلى الحجر.", "There are no selected variants to quarantine."),
            "ConfirmApplyTitle" => ("تأكيد النقل إلى الحجر القابل للاسترجاع", "Confirm move to recoverable quarantine"),
            "ConfirmApply" => ("سيتم نقل {0} عنصرًا إلى مجلد الحجر القابل للاسترجاع مع حفظ المسار الأصلي لكل ملف. لن يُحذف أي ملف نهائيًا.\n\nهل تريد المتابعة؟", "{0} item(s) will be moved to the recoverable quarantine folder with their original paths recorded. Nothing will be permanently deleted.\n\nContinue?"),
            "ApplyDone" => ("تم نقل {0} عنصرًا إلى الحجر القابل للاسترجاع بنجاح.", "Moved {0} item(s) to recoverable quarantine."),
            "ApplyPartial" => ("نُقل {0} عنصرًا، وتعذر نقل {1}. راجع الرسائل.", "Moved {0} item(s); {1} could not be moved. Review the messages."),
            "ApplyButton" => ("انقل المحدد إلى الحجر", "Move selected variants to quarantine"),
            "AdminNotice" => ("ملاحظة: قد يحتاج النقل من مجلدات النظام (مثل Program Files) إلى موافقة مسؤول ويندوز (UAC).", "Note: Moving items from system folders (such as Program Files) may prompt for Windows Administrator (UAC) approval."),
            "AdminCanceled" => ("تم إلغاء موافقة المسؤول (UAC)؛ لم يتم نقل أي ملف.", "Administrator approval (UAC) was canceled; no items were moved."),
            "RestoreConfirm" => ("سيتم استرجاع {0} عنصرًا إلى مساراتها الأصلية. لن يتم استبدال أي ملف موجود أبدًا. متابعة؟", "Restore {0} item(s) to their original paths? Existing files will never be overwritten."),
            "RestoreDone" => ("تم استرجاع {0} عنصرًا إلى مساراتها الأصلية.", "Restored {0} item(s) to their original paths."),
            "RestorePartial" => ("تم استرجاع {0} عنصرًا، وتعذر استرجاع {1}.", "Restored {0} item(s); {1} could not be restored."),
            "FolderPicker" => ("اختر مجلد بلجنز", "Choose a plug-in folder"),
            "ProtectedFolder" => ("هذا مسار Waves أو WPAPI محمي ومستثنى ولا يمكن إضافته للفحص.", "This Waves or WPAPI location is protected/excluded and cannot be added to scanning."),
            "FolderExists" => ("هذا المجلد موجود بالفعل في القائمة.", "This folder is already in the list."),
            "SettingsSaved" => ("تم حفظ الإعدادات.", "Settings saved."),
            "DefaultsRestored" => ("تمت استعادة مجلدات البحث الافتراضية.", "Default scan folders restored."),
            "SelectFolder" => ("حدد مجلدًا أولًا.", "Select a folder first."),
            "NoScanYet" => ("افحص المجلدات أولًا لعرض مجموعات المراجعة.", "Scan folders first to populate the review queue."),
            "MissingRoots" => ("مجلد بحث غير موجود أو متجاوز", "scan folder(s) missing or skipped"),
            "CandidatesFound" => ("بلجن مكتشف", "recognized plug-in(s)"),
            "GroupsFound" => ("مجموعة مراجعة", "review group(s)"),
            "VariantsLabel" => ("نسخ", "variants"),
            "ExactMatchDetail" => ("تطابق المنتج والجيل ونوع I/O؛ الأولوية: x64 أولًا، ثم VST3 ← VST/DLL ← CLAP، ثم أحدث إصدار مقروء ضمن الفئة الفائزة؛ التعادل يُحسم للإصدار المقروء ثم المسار الأقرب/الأقصر.", "Exact product, generation, and I/O identity; ranked by x64 first, then VST3 → VST/DLL → CLAP, then newest readable release in the winning tier; ties resolve to the readable version, then the nearest/shorter canonical path."),
            "AliasMatchDetail" => ("مقترح لمراجعة اسم ملتبس؛ لا يُدمج ولا يُنقل تلقائيًا إلا بعد تأكيدك اليدوي.", "Suggested name alias for review; never merged or moved automatically without your manual confirmation."),
            "TieMatchDetail" => ("تعذّر حسم النسخة المُبقاة تلقائيًا؛ الاختيار اليدوي مطلوب.", "Keeper could not be resolved automatically; manual choice required."),
            "ApplyErrorTitle" => ("تعذر تنفيذ العملية بالكامل", "Some items could not be moved"),
            "RestoreErrorTitle" => ("تعذر الاسترجاع بالكامل", "Some items could not be restored"),
            _ => (key, key)
        };
        return _isArabic ? pair.Item1 : pair.Item2;
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) NavigateTo(page);
    }

    private void NavigateTo(string page)
    {
        OverviewPage.Visibility = page == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        ReviewPage.Visibility = page == "Review" ? Visibility.Visible : Visibility.Collapsed;
        QuarantinePage.Visibility = page == "Quarantine" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        SetNavigationState(OverviewNav, page == "Overview");
        SetNavigationState(ReviewNav, page == "Review");
        SetNavigationState(QuarantineNav, page == "Quarantine");
        SetNavigationState(SettingsNav, page == "Settings");

        if (page == "Review") RenderGroups();
        if (page == "Quarantine") RefreshQuarantine();
    }

    private static void SetNavigationState(Button button, bool selected)
    {
        button.Background = selected ? new SolidColorBrush(Color.FromRgb(28, 48, 69)) : Brushes.Transparent;
        button.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(53, 103, 111)) : Brushes.Transparent;
        button.BorderThickness = selected ? new Thickness(1) : new Thickness(0);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCancellation is not null) return;
        RootsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RootsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _settings.Language = _isArabic ? "ar" : "en";
        SettingsService.Save(_settings);

        _scanCancellation = new CancellationTokenSource();
        ScanButton.IsEnabled = false;
        CancelScanButton.IsEnabled = true;
        ScanProgressBar.Value = 0;
        ScanStatusText.Text = L("Scanning");

        var progress = new Progress<ScanProgress>(p =>
        {
            var percent = p.RootsTotal == 0 ? 0 : (double)p.RootsCompleted / p.RootsTotal * 100.0;
            ScanProgressBar.Value = Math.Clamp(percent, 0, 100);
            ScanStatusText.Text = string.IsNullOrWhiteSpace(p.CurrentPath)
                ? L("Scanning")
                : $"{L("Scanning")} {Path.GetFileName(p.CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
            CandidatesMetric.Text = p.CandidatesFound.ToString();
        });

        try
        {
            var result = await _scanner.ScanAsync(_settings, progress, _scanCancellation.Token);
            _lastCandidates = result.Candidates.ToList();
            _groups = PluginGroupBuilder.Build(_lastCandidates);
            _hasScanned = true;
            RefreshOverviewGroupRows();
            RenderGroups();
            NavigateTo("Review");
            var recognizedCount = _lastCandidates.Count(c => c.IsLikelyPlugin);
            UpdateMetrics(recognizedCount, _groups.Count);
            ScanProgressBar.Value = 100;
            var enabledRootsCount = _settings.Roots.Count(r => r.Enabled);
            ScanStatusText.Text = $"{L("ScanComplete")} {result.RootsScanned} / {enabledRootsCount} · {recognizedCount} {L("CandidatesFound")} · {_groups.Count} {L("GroupsFound")}";

            if (result.Warnings.Count > 0)
            {
                ScanStatusText.Text += $" · {result.Warnings.Count} {L("MissingRoots")}";
            }
        }
        catch (OperationCanceledException)
        {
            ScanStatusText.Text = L("ScanCanceled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Error);
            ScanStatusText.Text = L("ScanCanceled");
        }
        finally
        {
            _scanCancellation.Dispose();
            _scanCancellation = null;
            ScanButton.IsEnabled = true;
            CancelScanButton.IsEnabled = false;
            UpdateApplyButton();
        }
    }

    private void CancelScanButton_Click(object sender, RoutedEventArgs e) => _scanCancellation?.Cancel();

    private void UpdateMetrics(int candidates, int groups)
    {
        PathsMetric.Text = _settings.Roots.Count(r => r.Enabled).ToString();
        if (candidates > 0 || _hasScanned) CandidatesMetric.Text = candidates.ToString();
        if (groups > 0 || _hasScanned) GroupsMetric.Text = groups.ToString();
        QuarantineMetric.Text = _quarantineRows.Count.ToString();
    }

    private void RefreshOverviewGroupRows()
    {
        OverviewGroupsGrid.ItemsSource = _groups.Select(group => new OverviewGroupRow
        {
            DisplayName = group.DisplayName,
            Vendor = group.Vendor,
            CandidateCountLabel = $"{group.Candidates.Count} {L("VariantsLabel")}",
            MatchSummary = group.NeedsIdentityConfirmation
                ? L("AliasMatchDetail")
                : group.HasConflictingTopCandidates ? L("TieMatchDetail") : L("ExactMatchDetail")
        }).ToList();
    }

    private void ReviewSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _reviewFilter = ReviewSearchBox.Text?.Trim() ?? "";
        RenderGroups();
    }

    private void RenderGroups()
    {
        if (GroupsPanel is null) return;
        GroupsPanel.Children.Clear();
        if (!_hasScanned)
        {
            GroupsPanel.Children.Add(CreateEmptyCard(L("NoScanYet")));
            UpdateApplyButton();
            return;
        }
        if (_groups.Count == 0)
        {
            GroupsPanel.Children.Add(CreateEmptyCard(L("NoGroupsTitle") + "\n" + L("NoGroupsBody")));
            UpdateApplyButton();
            return;
        }

        var searchedGroups = string.IsNullOrWhiteSpace(_reviewFilter)
            ? _groups
            : _groups.Where(g =>
                g.DisplayName.Contains(_reviewFilter, StringComparison.OrdinalIgnoreCase) ||
                g.Vendor.Contains(_reviewFilter, StringComparison.OrdinalIgnoreCase) ||
                g.Candidates.Any(c =>
                    c.Name.Contains(_reviewFilter, StringComparison.OrdinalIgnoreCase) ||
                    c.Path.Contains(_reviewFilter, StringComparison.OrdinalIgnoreCase)))
              .ToList();

        var visibleGroups = _hideIncludedGroups
            ? searchedGroups.Where(g => !g.IncludeInPlan).ToList()
            : searchedGroups;

        if (visibleGroups.Count == 0)
        {
            GroupsPanel.Children.Add(CreateEmptyCard(
                searchedGroups.Count > 0 ? L("AllIncludedHidden") : L("NoFilterMatches")));
            UpdateApplyButton();
            return;
        }

        foreach (var group in visibleGroups)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                BorderBrush = group.IncludeInPlan
                    ? new SolidColorBrush(Color.FromRgb(48, 110, 108))
                    : (Brush)FindResource("StrokeBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var body = new StackPanel();
            card.Child = body;

            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            var candidateCount = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 35, 56)),
                BorderBrush = (Brush)FindResource("StrokeBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = $"{group.Candidates.Count} {L("VariantsLabel")}",
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                }
            };
            DockPanel.SetDock(candidateCount, Dock.Right);
            header.Children.Add(candidateCount);

            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = group.DisplayName,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!string.IsNullOrWhiteSpace(group.Vendor))
                titleStack.Children.Add(new TextBlock
                {
                    Text = group.Vendor,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            header.Children.Add(titleStack);
            body.Children.Add(header);

            var statusLabel = group.NeedsIdentityConfirmation
                ? L("AliasWarning")
                : group.HasConflictingTopCandidates ? L("TieWarningBadge") : L("StrongMatch");
            var statusNeedsAttention = group.NeedsIdentityConfirmation || group.HasConflictingTopCandidates;
            var matchText = new TextBlock
            {
                Text = statusLabel,
                Foreground = statusNeedsAttention
                    ? (Brush)FindResource("WarningBrush")
                    : (Brush)FindResource("AccentBrush"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            body.Children.Add(matchText);

            var localizedSummary = group.NeedsIdentityConfirmation
                ? L("AliasMatchDetail")
                : group.HasConflictingTopCandidates ? L("TieMatchDetail") : L("ExactMatchDetail");
            body.Children.Add(new TextBlock
            {
                Text = localizedSummary,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            if (group.NeedsIdentityConfirmation)
            {
                var confirm = new CheckBox
                {
                    Content = L("ConfirmIdentity"),
                    IsChecked = group.IsIdentityConfirmed,
                    Foreground = (Brush)FindResource("WarningBrush"),
                    Margin = new Thickness(0, 2, 0, 8),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                };
                confirm.Checked += (_, _) =>
                {
                    group.IsIdentityConfirmed = true;
                    if (group.SelectedKeepId is null && group.RecommendedKeepId is Guid recommended)
                        group.SelectedKeepId = recommended;
                    RenderGroups();
                };
                confirm.Unchecked += (_, _) =>
                {
                    group.IsIdentityConfirmed = false;
                    group.IncludeInPlan = false;
                    group.SelectedKeepId = null;
                    RenderGroups();
                };
                body.Children.Add(confirm);
            }

            if (group.HasConflictingTopCandidates)
            {
                body.Children.Add(new TextBlock
                {
                    Text = L("ChooseTie"),
                    Foreground = (Brush)FindResource("WarningBrush"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }

            var canInteractWithGroup = !group.NeedsIdentityConfirmation || group.IsIdentityConfirmed;
            var includeGroup = new CheckBox
            {
                Content = L("IncludeGroup"),
                IsChecked = group.IncludeInPlan,
                IsEnabled = canInteractWithGroup && group.SelectedKeepId is not null,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 10)
            };
            includeGroup.Checked += (_, _) =>
            {
                group.IncludeInPlan = true;
                RenderGroups();
            };
            includeGroup.Unchecked += (_, _) =>
            {
                group.IncludeInPlan = false;
                RenderGroups();
            };
            body.Children.Add(includeGroup);

            foreach (var candidate in group.Candidates)
            {
                var isSelectedKeeper = group.SelectedKeepId == candidate.Id;
                var isQuarantineTarget = group.IncludeInPlan && group.SelectedKeepId is not null && !isSelectedKeeper;

                var candidateBox = new Border
                {
                    Background = isSelectedKeeper
                        ? new SolidColorBrush(Color.FromRgb(18, 40, 46))
                        : new SolidColorBrush(Color.FromRgb(16, 26, 43)),
                    BorderBrush = isSelectedKeeper
                        ? new SolidColorBrush(Color.FromRgb(56, 138, 126))
                        : (Brush)FindResource("StrokeBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(11, 9, 11, 9),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var radio = new RadioButton
                {
                    GroupName = "keep-" + group.Id.ToString("N"),
                    IsChecked = isSelectedKeeper,
                    IsEnabled = canInteractWithGroup,
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    Tag = candidate,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    ToolTip = candidate.DetectionNote
                };

                var candidateLayout = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
                var titleRow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                titleRow.Children.Add(new TextBlock
                {
                    Text = candidate.Name,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (candidate.Id == group.RecommendedKeepId)
                {
                    titleRow.Children.Add(CreateBadge(
                        L("Recommended"),
                        Color.FromRgb(24, 63, 61),
                        (Brush)FindResource("AccentBrush")));
                }

                if (isSelectedKeeper)
                {
                    titleRow.Children.Add(CreateBadge(
                        L("WillKeepBadge"),
                        Color.FromRgb(21, 55, 74),
                        new SolidColorBrush(Color.FromRgb(130, 216, 255))));
                }
                else if (isQuarantineTarget)
                {
                    titleRow.Children.Add(CreateBadge(
                        L("WillQuarantineBadge"),
                        Color.FromRgb(58, 38, 24),
                        (Brush)FindResource("WarningBrush")));
                }

                candidateLayout.Children.Add(titleRow);

                if (!string.IsNullOrWhiteSpace(candidate.Vendor) &&
                    !string.Equals(candidate.Vendor, group.Vendor, StringComparison.OrdinalIgnoreCase))
                {
                    candidateLayout.Children.Add(new TextBlock
                    {
                        Text = candidate.Vendor,
                        FontSize = 10,
                        Foreground = (Brush)FindResource("TextMutedBrush"),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }

                var version = string.IsNullOrWhiteSpace(candidate.Version) ? L("NoVersion") : candidate.Version;
                var metaLine = new TextBlock
                {
                    Text = $"{candidate.FormatLabel}  ·  {candidate.ArchitectureLabel}  ·  {L("VersionLabel")}: {version}",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    Margin = new Thickness(0, 3, 0, 0)
                };
                candidateLayout.Children.Add(metaLine);

                candidateLayout.Children.Add(new TextBlock
                {
                    Text = candidate.Path,
                    Foreground = new SolidColorBrush(Color.FromRgb(137, 157, 182)),
                    FontFamily = new FontFamily("Consolas, Segoe UI"),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    FlowDirection = FlowDirection.LeftToRight,
                    TextAlignment = TextAlignment.Left,
                    Margin = new Thickness(0, 3, 0, 0)
                });

                radio.Content = candidateLayout;
                radio.Checked += (_, _) =>
                {
                    if (group.SelectedKeepId == candidate.Id) return;
                    group.SelectedKeepId = candidate.Id;
                    RenderGroups();
                };

                candidateBox.Child = radio;
                body.Children.Add(candidateBox);
            }

            GroupsPanel.Children.Add(card);
        }
        UpdateApplyButton();
    }

    private static Border CreateBadge(string text, Color background, Brush foreground)
    {
        return new Border
        {
            Background = new SolidColorBrush(background),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            }
        };
    }

    private Border CreateEmptyCard(string text)
    {
        return new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("StrokeBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24),
            Child = new TextBlock
            {
                Text = text,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = _isArabic ? TextAlignment.Right : TextAlignment.Left
            }
        };
    }

    private List<ApplyPlanItem> BuildPlanItems()
    {
        var plan = new List<ApplyPlanItem>();
        foreach (var group in _groups)
        {
            if (!group.IncludeInPlan) continue;
            if (group.NeedsIdentityConfirmation && !group.IsIdentityConfirmed) continue;
            if (group.SelectedKeepId is not Guid keepId) continue;
            var keep = group.Candidates.FirstOrDefault(c => c.Id == keepId);
            if (keep is null) continue;

            foreach (var candidate in group.Candidates.Where(c => c.Id != keepId))
            {
                if (PluginSafety.IsProtectedCandidate(candidate.Path, candidate.Vendor, candidate.Name)) continue;
                plan.Add(new ApplyPlanItem
                {
                    OriginalPath = candidate.Path,
                    PluginName = candidate.Name,
                    Format = candidate.FormatLabel,
                    Architecture = candidate.ArchitectureLabel
                });
            }
        }
        return plan.DistinctBy(p => p.OriginalPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void UpdateApplyButton()
    {
        if (ApplyButton is null) return;
        var count = BuildPlanItems().Count;
        ApplyButton.IsEnabled = count > 0 && _scanCancellation is null;
        ApplyButton.Content = count > 0 ? $"{L("ApplyButton")} ({count})" : L("ApplyButton");
        if (SelectRecommendedButton is not null)
            SelectRecommendedButton.IsEnabled = _groups.Any(g => !g.NeedsIdentityConfirmation &&
                !g.HasConflictingTopCandidates && g.RecommendedKeepId is not null && !g.IncludeInPlan);
        if (ClearSelectionButton is not null)
            ClearSelectionButton.IsEnabled = _groups.Any(g => g.IncludeInPlan);
        if (ToggleIncludedButton is not null)
        {
            var includedCount = _groups.Count(g => g.IncludeInPlan);
            ToggleIncludedButton.Visibility = includedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ToggleIncludedButton.Content = _hideIncludedGroups
                ? $"{L("ShowIncluded")} ({includedCount})"
                : $"{L("HideIncluded")} ({includedCount})";
        }
    }

    private void PruneMovedCandidatesFromReview()
    {
        if (!_hasScanned) return;
        _lastCandidates = _lastCandidates
            .Where(c => File.Exists(c.Path) || Directory.Exists(c.Path))
            .ToList();
        _groups = PluginGroupBuilder.Build(_lastCandidates);
        RefreshOverviewGroupRows();
        RenderGroups();
        UpdateMetrics(_lastCandidates.Count(c => c.IsLikelyPlugin), _groups.Count);
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var items = BuildPlanItems();
        if (items.Count == 0)
        {
            MessageBox.Show(L("NoAction"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(string.Format(L("ConfirmApply"), items.Count) + "\n\n" + L("AdminNotice"),
            L("ConfirmApplyTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        ApplyButton.IsEnabled = false;
        try
        {
            QuarantineApplyResult result;
            if (QuarantineService.IsAdministrator())
            {
                result = await QuarantineService.ApplyAsync(new ApplyPlan { Items = items });
            }
            else
            {
                var planPath = QuarantineService.SavePlan(new ApplyPlan { Items = items });
                try
                {
                    using var process = QuarantineService.StartElevatedApply(planPath);
                    if (process is null) throw new InvalidOperationException("Could not start the elevated operation.");
                    await process.WaitForExitAsync();
                    RefreshQuarantine();
                    PruneMovedCandidatesFromReview();
                    ScanStatusText.Text = L("ScanComplete");
                    return;
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    try { File.Delete(planPath); } catch { }
                    MessageBox.Show(L("AdminCanceled"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            RefreshQuarantine();
            PruneMovedCandidatesFromReview();
            if (result.Errors.Count == 0)
                MessageBox.Show(string.Format(L("ApplyDone"), result.Moved.Count), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(string.Format(L("ApplyPartial"), result.Moved.Count, result.Errors.Count) + "\n\n" +
                                string.Join("\n", result.Errors.Take(8)), L("ApplyErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            ScanStatusText.Text = L("ScanComplete");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateApplyButton();
        }
    }

    private void RefreshQuarantine()
    {
        _quarantineRows.Clear();
        foreach (var entry in QuarantineService.LoadEntries().OrderByDescending(e => e.MovedAt))
            _quarantineRows.Add(entry);
        QuarantineMetric.Text = _quarantineRows.Count.ToString();
        UpdateRestoreButton();
    }

    private void RefreshQuarantineButton_Click(object sender, RoutedEventArgs e) => RefreshQuarantine();

    private void UpdateRestoreButton()
    {
        if (RestoreButton is null || QuarantineGrid is null) return;
        var count = QuarantineGrid.SelectedItems.Count;
        RestoreButton.IsEnabled = count > 0;
        RestoreButton.Content = count > 0 ? $"{L("RestoreSelected")} ({count})" : L("RestoreSelected");
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = QuarantineGrid.SelectedItems.Cast<QuarantineEntry>().ToList();
        if (selected.Count == 0) return;
        if (MessageBox.Show(string.Format(L("RestoreConfirm"), selected.Count), L("QuarantineTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        RestoreButton.IsEnabled = false;
        try
        {
            int restored;
            List<string> errors;
            if (QuarantineService.IsAdministrator())
            {
                (restored, errors) = await QuarantineService.RestoreAsync(selected);
            }
            else
            {
                var planPath = QuarantineService.SaveRestorePlan(selected);
                try
                {
                    using var process = QuarantineService.StartElevatedRestore(planPath);
                    if (process is null) throw new InvalidOperationException("Could not start the elevated restore operation.");
                    await process.WaitForExitAsync();
                    RefreshQuarantine();
                    return;
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    try { File.Delete(planPath); } catch { }
                    MessageBox.Show(L("AdminCanceled"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            RefreshQuarantine();
            if (errors.Count == 0)
                MessageBox.Show(string.Format(L("RestoreDone"), restored), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(string.Format(L("RestorePartial"), restored, errors.Count) + "\n\n" +
                                string.Join("\n", errors.Take(8)), L("RestoreErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateRestoreButton();
        }
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = L("FolderPicker"),
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
        if (PluginSafety.IsProtectedPath(dialog.SelectedPath))
        {
            MessageBox.Show(L("ProtectedFolder"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!PluginSafety.IsReasonableScanRoot(dialog.SelectedPath, out var rootProblem))
        {
            MessageBox.Show(rootProblem, "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_settings.Roots.Any(r => string.Equals(Path.GetFullPath(r.Path), Path.GetFullPath(dialog.SelectedPath), StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(L("FolderExists"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _settings.Roots.Add(new ScanRoot { Path = dialog.SelectedPath, Kind = RootKind.Auto, Enabled = true });
        RootsGrid.Items.Refresh();
        UpdateMetrics(_hasScanned ? _lastCandidates.Count(c => c.IsLikelyPlugin) : 0, _groups.Count);
        SettingsService.Save(_settings);
    }

    private void RemoveFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (RootsGrid.SelectedItem is not ScanRoot root)
        {
            MessageBox.Show(L("SelectFolder"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _settings.Roots.Remove(root);
        RootsGrid.Items.Refresh();
        UpdateMetrics(_hasScanned ? _lastCandidates.Count(c => c.IsLikelyPlugin) : 0, _groups.Count);
        SettingsService.Save(_settings);
    }

    private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Roots = AppSettings.CreateDefaultRoots();
        RootsGrid.ItemsSource = _settings.Roots;
        RootsGrid.Items.Refresh();
        SettingsService.Save(_settings);
        UpdateMetrics(_hasScanned ? _lastCandidates.Count(c => c.IsLikelyPlugin) : 0, _groups.Count);
        MessageBox.Show(L("DefaultsRestored"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        RootsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RootsGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var rejectedProtected = _settings.Roots.Any(r => PluginSafety.IsProtectedPath(r.Path));
        _settings.Roots = _settings.Roots
            .Where(r => !string.IsNullOrWhiteSpace(r.Path) && !PluginSafety.IsProtectedPath(r.Path))
            .ToList();
        RootsGrid.ItemsSource = _settings.Roots;
        RootsGrid.Items.Refresh();

        _settings.Language = _isArabic ? "ar" : "en";
        SettingsService.Save(_settings);
        UpdateMetrics(_hasScanned ? _lastCandidates.Count(c => c.IsLikelyPlugin) : 0, _groups.Count);

        if (rejectedProtected)
        {
            MessageBox.Show(L("ProtectedFolder"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(L("SettingsSaved"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SelectRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in _groups.Where(g => !g.NeedsIdentityConfirmation &&
                     !g.HasConflictingTopCandidates && g.RecommendedKeepId is not null))
        {
            group.IncludeInPlan = true;
            group.SelectedKeepId ??= group.RecommendedKeepId;
        }
        _hideIncludedGroups = true;
        RenderGroups();
    }

    private void ToggleIncludedButton_Click(object sender, RoutedEventArgs e)
    {
        _hideIncludedGroups = !_hideIncludedGroups;
        RenderGroups();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in _groups)
        {
            group.IncludeInPlan = false;
        }
        RenderGroups();
    }

    private void OpenReviewButton_Click(object sender, RoutedEventArgs e) => NavigateTo("Review");

    private void OverviewGroupsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OverviewGroupsGrid.SelectedItem is not null)
        {
            NavigateTo("Review");
        }
    }
}
