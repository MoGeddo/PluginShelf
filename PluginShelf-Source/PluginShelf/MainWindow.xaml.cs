using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
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
    private List<PluginGroup> _groups = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _isArabic;
    private bool _hasScanned;

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

        RootsGrid.ItemsSource = _settings.Roots;
        QuarantineGrid.ItemsSource = _quarantineRows;
        OverviewGroupsGrid.ItemsSource = _groups;
        QuarantineGrid.SelectionChanged += (_, _) => UpdateRestoreButton();

        UpdateLanguage();
        RefreshQuarantine();
        UpdateMetrics(0, 0);
        NavigateTo("Overview");
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
        RootLayout.FlowDirection = _isArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        OverviewGroupsGrid.FlowDirection = FlowDirection.LeftToRight;
        QuarantineGrid.FlowDirection = FlowDirection.LeftToRight;
        RootsGrid.FlowDirection = FlowDirection.LeftToRight;

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
        ReviewFooterText.Text = L("ReviewFooter");
        QuarantineTitle.Text = L("QuarantineTitle");
        QuarantineSubtitle.Text = L("QuarantineSubtitle");
        QuarantineFooterText.Text = L("QuarantineFooter");
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
        AddFolderButton.Content = L("AddFolder");
        RemoveFolderButton.Content = L("RemoveFolder");
        SaveSettingsButton.Content = L("SaveSettings");

        if (!_hasScanned)
            ScanStatusText.Text = L("Ready");
        RefreshOverviewGroupRows();
        UpdateMetrics(_groups.Count == 0 ? 0 : _groups.Sum(g => g.Candidates.Count), _groups.Count);
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
            "PriorityTitle" => ("ترتيب الترشيح", "Recommendation order"),
            "PriorityHint" => ("64-bit أولًا (و32-bit عند غيابه)؛ ثم VST3 > VST/DLL > CLAP؛ ثم أحدث إصدار داخل هذه الأولوية ونفس الجيل ونسخة I/O. لا تُستخدم تواريخ الملفات.", "x64 first (x86 only if absent); then VST3 > VST/DLL > CLAP; then newest release within that tier and the same generation/I/O identity. File dates are ignored."),
            "WavesExcluded" => ("Waves مستثنى بالكامل", "Waves is fully excluded"),
            "OverviewTitle" => ("مكتبة البلجنز تحت السيطرة", "Your plug-in library, under control"),
            "OverviewSubtitle" => ("اكتشف تكرار الصيغ، راجع كل اقتراح، واسترجع أي عنصر من الحجر.", "Find format duplicates, review every proposal, and restore anything from quarantine."),
            "ScanCardTitle" => ("ابدأ بفحص للقراءة فقط", "Start with a read-only scan"),
            "ScanCardDescription" => ("الفحص يقرأ معلومات الملفات فقط. لا يشغّل كود أي بلجن ولا ينقل ملفات.", "The scan reads plug-in metadata only. It never loads plug-in code and never moves files."),
            "Ready" => ("جاهز للفحص.", "Ready when you are."),
            "Scanning" => ("جاري فحص المجلدات…", "Scanning folders…"),
            "ScanButton" => ("افحص المجلدات", "Scan folders"),
            "CancelScan" => ("إلغاء الفحص", "Cancel scan"),
            "PathsMetricLabel" => ("مجلدات البحث", "SCAN FOLDERS"),
            "Configured" => ("مُعدّة للفحص", "configured"),
            "CandidatesMetricLabel" => ("عناصر معروفة", "CANDIDATES"),
            "RecognizedItems" => ("ملفات أو حزم محتملة", "recognized items"),
            "GroupsMetricLabel" => ("مجموعات متكررة", "DUPLICATE GROUPS"),
            "NeedReview" => ("تحتاج مراجعة", "need a review"),
            "QuarantineMetricLabel" => ("في الحجر", "QUARANTINED"),
            "RestorableItems" => ("عناصر قابلة للاسترجاع", "restorable items"),
            "RecentGroupsTitle" => ("قائمة المراجعة", "Review queue"),
            "RecentGroupsSubtitle" => ("لن تدخل مجموعة ملتبسة في خطة الحجر إلا بعد تأكيدك.", "Ambiguous groups stay untouched until you confirm them."),
            "OpenReview" => ("افتح المراجعة", "Open review"),
            "PlugInColumn" => ("البلجن", "Plug-in"),
            "VendorColumn" => ("الشركة", "Vendor"),
            "VariantsColumn" => ("النسخ", "Variants"),
            "MatchColumn" => ("سبب المطابقة", "Match"),
            "ReviewTitle" => ("راجع نسخ الصيغ", "Review format variants"),
            "ReviewSubtitle" => ("النسخة المقترحة مجرد توصية؛ ضمّن المجموعات التي تريد تنظيفها بنفسك.", "The suggested keeper is only a recommendation; opt in to the groups you want cleaned."),
            "SelectRecommended" => ("حدّد التوصيات الواضحة", "Select strong recommendations"),
            "IncludeGroup" => ("ضمّن هذه المجموعة في خطة الحجر", "Include this group in the quarantine plan"),
            "ReviewFooter" => ("المجموعات غير المحددة ستظل كما هي. الأسماء الملتبسة تحتاج تأكيدًا منفصلًا.", "Unselected groups stay untouched. Ambiguous aliases need separate confirmation."),
            "QuarantineTitle" => ("حجر قابل للاسترجاع", "Recoverable quarantine"),
            "QuarantineSubtitle" => ("لا يوجد حذف نهائي. الاسترجاع لا يستبدل ملفًا موجودًا في المسار الأصلي.", "Items are moved, never permanently deleted. Restore will not overwrite an existing original path."),
            "QuarantineFooter" => ("اختر عنصرًا أو أكثر لاسترجاعه.", "Select one or more entries to restore."),
            "RestoreSelected" => ("استرجع المحدد", "Restore selected"),
            "FormatColumn" => ("الصيغة", "Format"),
            "ArchitectureColumn" => ("المعمارية", "Architecture"),
            "MovedColumn" => ("تاريخ النقل", "Moved"),
            "OriginalPathColumn" => ("المسار الأصلي", "Original path"),
            "SettingsTitle" => ("إعدادات الفحص", "Scan settings"),
            "SettingsSubtitle" => ("أضف أو احذف مجلدات البحث. هذا الإصدار يتعرف على VST وVST3 وCLAP.", "Add or remove scan folders. VST, VST3 and CLAP are recognized in this release."),
            "EnabledColumn" => ("مفعّل", "On"),
            "FolderPathColumn" => ("مسار المجلد", "Folder path"),
            "FormatScopeColumn" => ("أنواع الملفات", "Known format scope"),
            "SettingsSafety" => ("يُستثنى WPAPI وWaves بالكامل. المجلدات المخصصة تستخدم الصيغ المعروفة فقط.", "WPAPI and all Waves-related paths/items are excluded. Custom folders use known formats only."),
            "AddFolder" => ("أضف مجلدًا", "Add folder"),
            "RemoveFolder" => ("احذف المحدد", "Remove selected"),
            "SaveSettings" => ("احفظ الإعدادات", "Save settings"),
            "NoGroupsTitle" => ("لا توجد مجموعات تكرار جاهزة للمراجعة", "No duplicate groups are ready for review"),
            "NoGroupsBody" => ("نفّذ فحصًا أولًا. العناصر الفريدة، وWaves، والملفات غير المؤكدة لن تُنقل.", "Run a scan first. Unique items, Waves, and unconfirmed files will not be moved."),
            "AliasWarning" => ("مطابقة اسم محتملة — تحتاج تأكيدك", "Possible name alias — your confirmation is required"),
            "StrongMatch" => ("تطابق اسم الشركة والمنتج", "Exact vendor and product-name match"),
            "ConfirmIdentity" => ("أؤكد أن هذه العناصر لنفس البلجن والجيل نفسه", "I confirm these are the same plug-in and product generation"),
            "ChooseKeep" => ("احتفظ بهذه النسخة", "Keep this variant"),
            "Recommended" => ("مقترحة", "RECOMMENDED"),
            "VersionLabel" => ("الإصدار", "Version"),
            "NoVersion" => ("غير متاح", "not reported"),
            "ChooseTie" => ("الإصدارات متساوية أو غير مكتملة ضمن أعلى أولوية؛ اختر نسخة يدويًا.", "Versions tie or are incomplete within the winning tier; choose one manually."),
            "KeepUntouched" => ("لا توجد نسخة مؤكدة للاحتفاظ بها؛ لن تُنقل هذه المجموعة.", "No confirmed keeper is selected; this group will not be moved."),
            "ScanComplete" => ("اكتمل الفحص.", "Scan complete."),
            "ScanCanceled" => ("تم إلغاء الفحص؛ لم تُعدّل ملفات.", "Scan canceled; no files were changed."),
            "NoAction" => ("لا توجد نسخ محددة للنقل إلى الحجر.", "There are no selected variants to quarantine."),
            "ConfirmApplyTitle" => ("تأكيد النقل إلى الحجر", "Confirm quarantine"),
            "ConfirmApply" => ("سيتم نقل {0} عنصرًا إلى مجلد حجر قابل للاسترجاع. لن يُحذف شيء نهائيًا. هل تريد المتابعة؟", "{0} item(s) will be moved to a recoverable quarantine folder. Nothing will be permanently deleted. Continue?"),
            "ApplyDone" => ("تم نقل {0} عنصرًا إلى الحجر. شغّل فحصًا جديدًا لتحديث النتائج.", "Moved {0} item(s) to quarantine. Run a new scan to refresh results."),
            "ApplyPartial" => ("نُقل {0} عنصرًا، وتعذر نقل {1}. راجع الرسائل.", "Moved {0} item(s); {1} could not be moved. Review the messages."),
            "ApplyButton" => ("انقل المحدد إلى الحجر", "Move selected variants to quarantine"),
            "AdminNotice" => ("قد يحتاج النقل من Program Files إلى موافقة مسؤول ويندوز. سيظهر طلب UAC؛ لا يبدأ النقل إلا بعد موافقتك.", "Moving items from Program Files may need Windows administrator approval. A UAC prompt will appear; nothing proceeds unless you approve."),
            "AdminCanceled" => ("لم تتم الموافقة على صلاحيات المسؤول؛ لم يتم تنفيذ النقل.", "Administrator approval was canceled; no items were moved."),
            "RestoreConfirm" => ("سيتم استرجاع {0} عنصرًا إلى مساراتها الأصلية. لن يتم استبدال أي ملف موجود. متابعة؟", "Restore {0} item(s) to their original paths? Existing files will never be overwritten."),
            "RestoreDone" => ("تم استرجاع {0} عنصرًا.", "Restored {0} item(s)."),
            "RestorePartial" => ("تم استرجاع {0} عنصرًا، وتعذر استرجاع {1}.", "Restored {0} item(s); {1} could not be restored."),
            "FolderPicker" => ("اختر مجلد بلجنز", "Choose a plug-in folder"),
            "ProtectedFolder" => ("هذا مسار Waves أو WPAPI محمي ولن يُضاف للفحص.", "This Waves or WPAPI location is protected and cannot be added to scanning."),
            "FolderExists" => ("هذا المجلد موجود بالفعل في القائمة.", "This folder is already in the list."),
            "SettingsSaved" => ("تم حفظ الإعدادات.", "Settings saved."),
            "SelectFolder" => ("حدد مجلدًا أولًا.", "Select a folder first."),
            "NoScanYet" => ("افحص المجلدات أولًا لعرض مجموعات المراجعة.", "Scan folders first to populate the review queue."),
            "MissingRoots" => ("مجلد بحث غير موجود أو غير قابل للقراءة", "scan folder(s) were missing or unreadable"),
            "CandidatesFound" => ("عناصر محتملة", "candidate item(s)"),
            "GroupsFound" => ("مجموعات", "group(s)"),
            "VariantsLabel" => ("نسخ", "variants"),
            "ExactMatchDetail" => ("تطابق موثوق للمنتج والجيل ونسخة I/O؛ تُطبّق أولوية المعمارية ثم الصيغة، ثم أحدث إصدار داخل الفئة الفائزة.", "High-confidence product, generation, and I/O identity; architecture and format rank first, then the newest release within the winning tier."),
            "AliasMatchDetail" => ("تشابه اسم محتمل فقط؛ لن يُنقل شيء إلا بعد تأكيدك.", "Possible name alias only; nothing moves unless you confirm it."),
            "TieMatchDetail" => ("تتعادل النسخ أو يتعذر مقارنة إصداراتها ضمن أعلى أولوية معمارية وصيغة؛ اختر يدويًا.", "Candidates tie or their releases cannot be compared within the top architecture/format tier; choose manually."),
            "ApplyErrorTitle" => ("تعذر تنفيذ العملية بالكامل", "Some items could not be moved"),
            "RestoreErrorTitle" => ("تعذر الاسترجاع بالكامل", "Some items could not be restored"),
            "ProtectedWarning" => ("أي مسار يحتوي Waves/WaveShell أو WPAPI مستثنى دائمًا.", "Paths/items related to Waves, WaveShell, or WPAPI are always excluded."),
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
            _groups = PluginGroupBuilder.Build(result.Candidates);
            _hasScanned = true;
            RefreshOverviewGroupRows();
            RenderGroups();
            var recognizedCount = result.Candidates.Count(c => c.IsLikelyPlugin);
            UpdateMetrics(recognizedCount, _groups.Count);
            ScanProgressBar.Value = 100;
            ScanStatusText.Text = $"{L("ScanComplete")} {result.RootsScanned} / {_settings.Roots.Count} · {recognizedCount} {L("CandidatesFound")} · {_groups.Count} {L("GroupsFound")}";

            var warningCount = result.Warnings.Count + result.RootsMissing;
            if (warningCount > 0)
            {
                ScanStatusText.Text += $" · {warningCount} {L("MissingRoots")}";
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

    private void RenderGroups()
    {
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

        foreach (var group in _groups)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                BorderBrush = (Brush)FindResource("StrokeBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var body = new StackPanel();
            card.Child = body;

            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            var candidateCount = new TextBlock
            {
                Text = $"{group.Candidates.Count} {L("VariantsLabel")}",
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(candidateCount, _isArabic ? Dock.Right : Dock.Right);
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

            var matchText = new TextBlock
            {
                Text = group.NeedsIdentityConfirmation ? L("AliasWarning") : L("StrongMatch"),
                Foreground = group.NeedsIdentityConfirmation
                    ? (Brush)FindResource("WarningBrush")
                    : (Brush)FindResource("AccentBrush"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
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
                    Margin = new Thickness(0, 2, 0, 10),
                    FontSize = 12
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

            var includeGroup = new CheckBox
            {
                Content = L("IncludeGroup"),
                IsChecked = group.IncludeInPlan,
                IsEnabled = !group.NeedsIdentityConfirmation || group.IsIdentityConfirmed,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 8)
            };
            includeGroup.Checked += (_, _) => { group.IncludeInPlan = true; UpdateApplyButton(); };
            includeGroup.Unchecked += (_, _) => { group.IncludeInPlan = false; UpdateApplyButton(); };
            body.Children.Add(includeGroup);

            foreach (var candidate in group.Candidates)
            {
                var radio = new RadioButton
                {
                    GroupName = "keep-" + group.Id.ToString("N"),
                    IsChecked = group.SelectedKeepId == candidate.Id,
                    IsEnabled = !group.NeedsIdentityConfirmation || group.IsIdentityConfirmed,
                    Margin = new Thickness(0, 4, 0, 5),
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    Tag = candidate,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    ToolTip = candidate.DetectionNote
                };

                var candidateLayout = new StackPanel();
                candidateLayout.Children.Add(new TextBlock
                {
                    Text = candidate.Name,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                if (!string.IsNullOrWhiteSpace(candidate.Vendor) &&
                    !string.Equals(candidate.Vendor, group.Vendor, StringComparison.OrdinalIgnoreCase))
                    candidateLayout.Children.Add(new TextBlock
                    {
                        Text = candidate.Vendor,
                        FontSize = 10,
                        Foreground = (Brush)FindResource("TextMutedBrush"),
                        Margin = new Thickness(0, 1, 0, 0)
                    });
                var firstLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                firstLine.Children.Add(new TextBlock
                {
                    Text = candidate.FormatLabel,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextPrimaryBrush")
                });
                firstLine.Children.Add(new TextBlock
                {
                    Text = "  ·  " + candidate.ArchitectureLabel,
                    FontSize = 10,
                    Foreground = (Brush)FindResource("TextMutedBrush")
                });
                if (candidate.Id == group.RecommendedKeepId)
                {
                    firstLine.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(24, 63, 61)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(7, 2, 7, 2),
                        Margin = new Thickness(9, 0, 0, 0),
                        Child = new TextBlock
                        {
                            Text = L("Recommended"),
                            Foreground = (Brush)FindResource("AccentBrush"),
                            FontSize = 9,
                            FontWeight = FontWeights.Bold
                        }
                    });
                }
                candidateLayout.Children.Add(firstLine);
                var version = string.IsNullOrWhiteSpace(candidate.Version) ? L("NoVersion") : candidate.Version;
                candidateLayout.Children.Add(new TextBlock
                {
                    Text = $"{L("VersionLabel")}: {version}",
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                candidateLayout.Children.Add(new TextBlock
                {
                    Text = candidate.Path,
                    Foreground = new SolidColorBrush(Color.FromRgb(137, 157, 182)),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                radio.Content = candidateLayout;
                radio.Checked += (_, _) =>
                {
                    group.SelectedKeepId = candidate.Id;
                    UpdateApplyButton();
                };
                body.Children.Add(radio);
            }
            GroupsPanel.Children.Add(card);
        }
        UpdateApplyButton();
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
        UpdateMetrics(_groups.Sum(g => g.Candidates.Count), _groups.Count);
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
        UpdateMetrics(_groups.Sum(g => g.Candidates.Count), _groups.Count);
        SettingsService.Save(_settings);
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        RootsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RootsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _settings.Language = _isArabic ? "ar" : "en";
        SettingsService.Save(_settings);
        UpdateMetrics(_groups.Sum(g => g.Candidates.Count), _groups.Count);
        MessageBox.Show(L("SettingsSaved"), "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SelectRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in _groups.Where(g => !g.NeedsIdentityConfirmation &&
                     !g.HasConflictingTopCandidates && g.RecommendedKeepId is not null))
        {
            group.IncludeInPlan = true;
            group.SelectedKeepId ??= group.RecommendedKeepId;
        }
        RenderGroups();
    }

    private void OpenReviewButton_Click(object sender, RoutedEventArgs e) => NavigateTo("Review");
}
