using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using P12Bridge.Core;
using P12Bridge.Infrastructure;

namespace P12Bridge.Desktop;

public partial class MainWindow : Window
{
    private readonly ICertificateProjectService certificateProjectService;
    private readonly IProvisioningProfileImportService profileImportService;
    private readonly IIpaImportService ipaImportService;
    private readonly IUploadReadinessEvaluator uploadReadinessEvaluator;
    private readonly IUploadService uploadService;
    private readonly IAppleDeveloperAuthService appleDeveloperAuthService;
    private readonly IAppStoreConnectBundleIdLookupService appStoreConnectBundleIdLookupService;
    private readonly IAppStoreConnectAppLookupService appStoreConnectAppLookupService;
    private readonly IAppStoreConnectBuildLookupService appStoreConnectBuildLookupService;
    private readonly IAppStoreConnectProfileLookupService appStoreConnectProfileLookupService;
    private readonly IAppStoreConnectCertificateLookupService appStoreConnectCertificateLookupService;
    private readonly IAppStoreConnectDeviceLookupService appStoreConnectDeviceLookupService;
    private readonly IAppStoreConnectRemotePreflightService appStoreConnectRemotePreflightService;
    private readonly ILocalAssetLibraryService localAssetLibraryService;
    private readonly IOperationHistoryService operationHistoryService;
    private readonly ICertificateProjectBackupService certificateProjectBackupService;
    private readonly IUploadSettingsService uploadSettingsService;
    private readonly IAssetExpirationReminderService assetExpirationReminderService;
    private readonly Dictionary<string, PageDefinition> _pages;
    private string? lastCertificateProjectDirectory;
    private ProvisioningProfile? lastImportedProfile;
    private string lastImportedProfilePath = string.Empty;
    private IpaMetadata? lastIpaMetadata;
    private string lastIpaImportedPath = string.Empty;
    private UploadEnvironmentValidationResult? lastUploadEnvironmentValidation;
    private CancellationTokenSource? uploadVerificationCancellation;
    private bool isUploadVerificationRunning;
    private UploadExecutionMode activeUploadExecutionMode = UploadExecutionMode.Verify;
    private bool isAppleApiConnectionChecking;
    private bool isAppStoreBundleIdLookupRunning;
    private bool isAppStoreAppLookupRunning;
    private bool isAppStoreBuildLookupRunning;
    private bool isAppStoreProfileLookupRunning;
    private bool isAppStoreCertificateLookupRunning;
    private bool isAppStoreDeviceLookupRunning;
    private bool isAppStoreRemotePreflightRunning;

    public MainWindow()
    {
        InitializeComponent();

        certificateProjectService = new CertificateProjectService(
            new LocalCertificateService(),
            new SystemClock());
        profileImportService = new ProvisioningProfileImportService(
            new ProvisioningProfileParser(),
            new SystemClock());
        ipaImportService = new IpaImportService(
            new IpaInspector());
        uploadReadinessEvaluator = new UploadReadinessEvaluator();
        uploadService = new TransporterUploadService();
        appleDeveloperAuthService = new AppleDeveloperAuthService();
        appStoreConnectBundleIdLookupService = new AppStoreConnectBundleIdLookupService();
        appStoreConnectAppLookupService = new AppStoreConnectAppLookupService();
        appStoreConnectBuildLookupService = new AppStoreConnectBuildLookupService();
        appStoreConnectProfileLookupService = new AppStoreConnectProfileLookupService();
        appStoreConnectCertificateLookupService = new AppStoreConnectCertificateLookupService();
        appStoreConnectDeviceLookupService = new AppStoreConnectDeviceLookupService();
        appStoreConnectRemotePreflightService = new AppStoreConnectRemotePreflightService();
        localAssetLibraryService = new LocalAssetLibraryService();
        operationHistoryService = new InMemoryOperationHistoryService();
        certificateProjectBackupService = new CertificateProjectBackupService();
        uploadSettingsService = new JsonUploadSettingsService();
        assetExpirationReminderService = new AssetExpirationReminderService();

        _pages = new Dictionary<string, PageDefinition>
        {
            ["Dashboard"] = new("工作台", "证书到上传总览", DashboardPage),
            ["Certificate"] = new("制作证书", "私钥、CSR、P12", CertificatePage),
            ["Profiles"] = new("描述文件", "Bundle、Team、有效期", ProfilesPage),
            ["IpaCheck"] = new("IPA 检查", "版本、签名、阻断项", IpaCheckPage),
            ["Upload"] = new("IPA 上传", "上传前检查", UploadPage),
            ["Assets"] = new("资产库", "项目、文件、IPA", AssetsPage),
            ["History"] = new("历史", "操作与日志", HistoryPage),
            ["Settings"] = new("设置", "凭据、路径、隐私", SettingsPage),
        };

        ApplyLibraryDirectoryDefaults();

        LoadUploadSettings();
        ShowPage("Dashboard");
    }

    private void OnNavigationChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string pageKey } || _pages is null)
        {
            return;
        }

        ShowPage(pageKey);
    }

    private void OnModuleShortcutClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string pageKey })
        {
            return;
        }

        SelectNavigation(pageKey);
        ShowPage(pageKey);
    }

    private void ShowPage(string pageKey)
    {
        if (!_pages.TryGetValue(pageKey, out PageDefinition? selectedPage))
        {
            return;
        }

        PageTitleText.Text = selectedPage.Title;
        PageSubtitleText.Text = selectedPage.Subtitle;

        foreach (PageDefinition page in _pages.Values)
        {
            page.Content.Visibility = ReferenceEquals(page.Content, selectedPage.Content)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (pageKey == "Upload")
        {
            EvaluateUploadReadiness();
            RefreshUploadEnvironmentStatus();
        }

        if (pageKey == "Dashboard")
        {
            RefreshAssets();
            RefreshExpirationReminders();
        }

        if (pageKey == "Settings")
        {
            RefreshUploadSettingsInputs();
            RefreshUploadEnvironmentStatus();
        }

        if (pageKey == "Assets")
        {
            RefreshAssets();
            RefreshExpirationReminders();
        }

        if (pageKey == "History")
        {
            RefreshHistory();
        }
    }

    private void SelectNavigation(string pageKey)
    {
        foreach (RadioButton navButton in FindVisualChildren<RadioButton>(this))
        {
            if (Equals(navButton.Tag, pageKey))
            {
                navButton.IsChecked = true;
                return;
            }
        }
    }

    private void OnCreateCertificateProjectClick(object sender, RoutedEventArgs e)
    {
        ClearCertificateResult();

        var request = new CertificateProjectCreateRequest(
            CertificateProjectNameTextBox.Text,
            ReadSelectedPurpose(),
            new CertificateSubject(
                CertificateCommonNameTextBox.Text,
                EmailAddress: OptionalText(CertificateEmailTextBox.Text),
                Organization: OptionalText(CertificateOrganizationTextBox.Text),
                CountryCode: OptionalText(CertificateCountryTextBox.Text)),
            CertificateBaseDirectoryTextBox.Text);

        var result = certificateProjectService.Create(request);
        if (!result.IsSuccess || result.Artifacts is null)
        {
            SetCertificateStatus(FormatIssues(result.Issues), isSuccess: false);
            RecordHistory("制作证书", OperationHistoryStatus.Failed, FormatIssues(result.Issues), FormatIssueDetail(result.Issues));
            return;
        }

        lastCertificateProjectDirectory = result.Artifacts.ProjectDirectory;
        CertificateProjectDirectoryTextBox.Text = result.Artifacts.ProjectDirectory;
        CertificatePrivateKeyPathTextBox.Text = result.Artifacts.PrivateKeyPath;
        CertificateCsrPathTextBox.Text = result.Artifacts.CertificateSigningRequestPath;
        OpenCertificateProjectButton.IsEnabled = true;
        SetCertificateStatus("已生成", isSuccess: true);
        RecordHistory(
            "制作证书",
            OperationHistoryStatus.Success,
            "已生成",
            $"项目: {result.Artifacts.ProjectDirectory}{Environment.NewLine}CSR: {result.Artifacts.CertificateSigningRequestPath}");
    }

    private void OnOpenCertificateProjectClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(lastCertificateProjectDirectory) || !Directory.Exists(lastCertificateProjectDirectory))
        {
            SetCertificateStatus("目录不存在", isSuccess: false);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = lastCertificateProjectDirectory,
            UseShellExecute = true
        });
    }

    private void OnSelectCertificateFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Apple Certificate (*.cer)|*.cer|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            CertificateCerPathTextBox.Text = dialog.FileName;
        }
    }

    private void OnExportP12Click(object sender, RoutedEventArgs e)
    {
        CertificateP12PathTextBox.Text = string.Empty;

        var result = certificateProjectService.ExportP12(new CertificateProjectP12ExportRequest(
            CertificateProjectDirectoryTextBox.Text,
            CertificateCerPathTextBox.Text,
            CertificateP12PasswordBox.Password));

        if (!result.IsSuccess)
        {
            SetCertificateStatus(FormatIssues(result.Issues), isSuccess: false);
            RecordHistory("导出 P12", OperationHistoryStatus.Failed, FormatIssues(result.Issues), FormatIssueDetail(result.Issues));
            return;
        }

        CertificateCerPathTextBox.Text = result.CertificatePath;
        CertificateP12PathTextBox.Text = result.P12Path;
        CertificateP12PasswordBox.Clear();
        SetCertificateStatus("P12 已导出", isSuccess: true);
        RecordHistory(
            "导出 P12",
            OperationHistoryStatus.Success,
            "已导出",
            $"P12: {result.P12Path}");
    }

    private void OnSelectProfileFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Provisioning Profile (*.mobileprovision)|*.mobileprovision|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            ProfileSourcePathTextBox.Text = dialog.FileName;
        }
    }

    private void OnImportProfileClick(object sender, RoutedEventArgs e)
    {
        ClearProfileResult();

        var result = profileImportService.Import(new ProvisioningProfileImportRequest(
            ProfileSourcePathTextBox.Text,
            ProfileBaseDirectoryTextBox.Text));

        if (result.Profile is not null)
        {
            ShowProfile(result.Profile, result.ImportedPath);
            lastImportedProfile = result.Profile;
            lastImportedProfilePath = result.ImportedPath;
        }

        RefreshUploadInputs();

        if (!result.IsSuccess)
        {
            SetProfileStatus(FormatProfileIssues(result.Issues), isSuccess: false);
            RecordHistory("导入描述", OperationHistoryStatus.Failed, FormatProfileIssues(result.Issues), FormatIssueDetail(result.Issues));
            return;
        }

        SetProfileStatus("已导入", isSuccess: true);
        RecordHistory(
            "导入描述",
            OperationHistoryStatus.Success,
            "已导入",
            result.Profile is null
                ? result.ImportedPath
                : $"{result.Profile.BundleIdentifier}{Environment.NewLine}{result.ImportedPath}");
    }

    private void OnSelectIpaFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "iOS App Package (*.ipa)|*.ipa|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            IpaSourcePathTextBox.Text = dialog.FileName;
        }
    }

    private void OnInspectIpaClick(object sender, RoutedEventArgs e)
    {
        ClearIpaResult();

        var result = ipaImportService.Import(new IpaImportRequest(
            IpaSourcePathTextBox.Text,
            IpaBaseDirectoryTextBox.Text));

        if (result.Metadata is not null)
        {
            ShowIpa(result.Metadata, result.ImportedPath);
            lastIpaMetadata = result.Metadata;
            lastIpaImportedPath = result.ImportedPath;
        }

        RefreshUploadInputs();

        if (!result.IsSuccess)
        {
            SetIpaStatus(FormatIpaIssues(result.Issues), isSuccess: false);
            RecordHistory("检查 IPA", OperationHistoryStatus.Failed, FormatIpaIssues(result.Issues), FormatIssueDetail(result.Issues));
            return;
        }

        SetIpaStatus("检查通过", isSuccess: true);
        RecordHistory(
            "检查 IPA",
            OperationHistoryStatus.Success,
            "检查通过",
            result.Metadata is null
                ? result.ImportedPath
                : $"{result.Metadata.BundleIdentifier} / {result.Metadata.ShortVersion} ({result.Metadata.BuildVersion}){Environment.NewLine}{result.ImportedPath}");
    }

    private void OnCheckUploadReadinessClick(object sender, RoutedEventArgs e)
    {
        var result = EvaluateUploadReadiness();
        RecordHistory(
            "上传检查",
            ToHistoryStatus(result.Status),
            FormatUploadStatus(result.Status),
            string.Join(Environment.NewLine, result.Checks.Select(check =>
                $"{FormatUploadCheckName(check.Code)}: {FormatUploadCheckStatus(check.Status)} / {FormatUploadCheckAction(check)}")));
    }

    private void OnUploadGoIpaClick(object sender, RoutedEventArgs e)
    {
        SelectNavigation("IpaCheck");
        ShowPage("IpaCheck");
    }

    private void OnUploadGoProfileClick(object sender, RoutedEventArgs e)
    {
        SelectNavigation("Profiles");
        ShowPage("Profiles");
    }

    private void OnUploadGoSettingsClick(object sender, RoutedEventArgs e)
    {
        SelectNavigation("Settings");
        ShowPage("Settings");
    }

    private void OnSettingsGoIpaClick(object sender, RoutedEventArgs e)
    {
        SelectNavigation("IpaCheck");
        ShowPage("IpaCheck");
    }

    private void OnRefreshAssetsClick(object sender, RoutedEventArgs e)
    {
        RefreshAssets(recordHistory: true);
        RefreshExpirationReminders(recordHistory: true);
    }

    private void OnRefreshHistoryClick(object sender, RoutedEventArgs e)
    {
        RefreshHistory();
    }

    private void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        operationHistoryService.Clear();
        RefreshHistory();
    }

    private void OnCopyHistoryClick(object sender, RoutedEventArgs e)
    {
        var text = HistoryListBox.SelectedItem is HistoryListItem selectedItem
            ? selectedItem.CopyText
            : FormatHistoryCopy(operationHistoryService.List().Items);

        if (string.IsNullOrWhiteSpace(text))
        {
            HistoryStatusText.Text = "暂无记录";
            HistoryStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
            return;
        }

        Clipboard.SetText(text);
        HistoryStatusText.Text = "已复制";
        HistoryStatusText.Foreground = (Brush)FindResource("SuccessBrush");
    }

    private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HistoryDetailTextBox.Text = HistoryListBox.SelectedItem is HistoryListItem selectedItem
            ? selectedItem.Detail
            : string.Empty;
    }

    private void OnOpenSelectedAssetClick(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not AssetListItem selectedAsset)
        {
            AssetStatusText.Text = "未选择";
            AssetStatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        var directory = selectedAsset.Type == LocalAssetType.CertificateProject
            ? selectedAsset.Path
            : Path.GetDirectoryName(selectedAsset.Path);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            AssetStatusText.Text = "目录不存在";
            AssetStatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }

    private void OnBackupSelectedAssetClick(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not AssetListItem selectedAsset)
        {
            AssetStatusText.Text = "未选择";
            AssetStatusText.Foreground = (Brush)FindResource("WarningBrush");
            RecordHistory("备份证书", OperationHistoryStatus.Failed, "未选择");
            return;
        }

        if (selectedAsset.Type != LocalAssetType.CertificateProject)
        {
            AssetStatusText.Text = "仅证书";
            AssetStatusText.Foreground = (Brush)FindResource("WarningBrush");
            RecordHistory("备份证书", OperationHistoryStatus.Failed, "仅证书", selectedAsset.Path);
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "选择备份目录",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = certificateProjectBackupService.Export(new CertificateProjectBackupRequest(
            selectedAsset.Path,
            dialog.FolderName));

        if (!result.IsSuccess)
        {
            var message = FormatBackupIssues(result.Issues);
            AssetStatusText.Text = message;
            AssetStatusText.Foreground = (Brush)FindResource("WarningBrush");
            RecordHistory("备份证书", OperationHistoryStatus.Failed, message, FormatIssueDetail(result.Issues));
            return;
        }

        AssetStatusText.Text = "已备份";
        AssetStatusText.Foreground = (Brush)FindResource("SuccessBrush");
        RecordHistory(
            "备份证书",
            OperationHistoryStatus.Success,
            "已备份",
            $"{Path.GetFileName(result.BackupPath)} / {result.FilesIncluded} 文件{Environment.NewLine}{result.BackupPath}");
    }

    private void OnSelectTransporterClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Transporter (*.cmd;*.bat;*.exe)|*.cmd;*.bat;*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            TransporterPathTextBox.Text = dialog.FileName;
            lastUploadEnvironmentValidation = null;
            RefreshUploadEnvironmentStatus();
        }
    }

    private void OnSelectUploadAssetDescriptionClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "AppStoreInfo (*.plist)|*.plist|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            UploadAssetDescriptionPathTextBox.Text = dialog.FileName;
            lastUploadEnvironmentValidation = null;
            RefreshUploadEnvironmentStatus();
        }
    }

    private void OnSelectAppleApiPrivateKeyClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "App Store Connect Key (*.p8)|*.p8|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            AppleApiPrivateKeyPathTextBox.Text = dialog.FileName;
            ClearAppleApiConnectionResult();
        }
    }

    private void OnUploadCredentialModeChanged(object sender, SelectionChangedEventArgs e)
    {
        SetCredentialPanelsVisibility();
        lastUploadEnvironmentValidation = null;
        RefreshUploadEnvironmentStatus();
    }

    private void OnUploadEnvironmentInputChanged(object sender, RoutedEventArgs e)
    {
        lastUploadEnvironmentValidation = null;
        ClearUploadRemotePreflightResult();
        RefreshUploadEnvironmentStatus();
    }

    private void OnValidateUploadEnvironmentClick(object sender, RoutedEventArgs e)
    {
        ValidateUploadEnvironment();
    }

    private void OnSaveUploadSettingsClick(object sender, RoutedEventArgs e)
    {
        SaveUploadSettings();
    }

    private void OnClearUploadSettingsClick(object sender, RoutedEventArgs e)
    {
        ClearUploadSettings();
    }

    private async void OnCheckAppleApiConnectionClick(object sender, RoutedEventArgs e)
    {
        if (isAppleApiConnectionChecking)
        {
            return;
        }

        SetAppleApiConnectionChecking(true);
        ClearAppleApiConnectionResult();
        SetAppleApiConnectionStatus("检查中", (Brush)FindResource("PrimaryBrush"));

        try
        {
            var credential = new AppleApiKeyCredential(
                UploadApiKeyIdTextBox.Text,
                UploadIssuerIdTextBox.Text,
                ReadAppleApiPrivateKeyPem());

            var result = await appleDeveloperAuthService.CheckConnectionAsync(credential);
            ShowAppleApiConnectionResult(result);
            RecordHistory(
                "账号检查",
                result.IsSuccess ? OperationHistoryStatus.Success : OperationHistoryStatus.Failed,
                result.IsSuccess ? "已连接" : "未连接",
                FormatIssueDetail(result.Issues));
        }
        finally
        {
            SetAppleApiConnectionChecking(false);
        }
    }

    private async void OnRunAppStoreRemotePreflightClick(object sender, RoutedEventArgs e)
    {
        if (isAppStoreRemotePreflightRunning)
        {
            return;
        }

        SetAppStoreRemotePreflightRunning(true);
        ClearAppStoreRemotePreflightResult();
        SetAppStoreRemotePreflightStatus("检查中", (Brush)FindResource("PrimaryBrush"));

        try
        {
            var credential = new AppleApiKeyCredential(
                UploadApiKeyIdTextBox.Text,
                UploadIssuerIdTextBox.Text,
                ReadAppleApiPrivateKeyPem());
            var request = new AppStoreConnectRemotePreflightRequest(
                credential,
                IpaBundleIdTextBox.Text);

            var result = await appStoreConnectRemotePreflightService.CheckAsync(request);
            ShowAppStoreRemotePreflightResult(result);
            RecordHistory(
                "远端检查",
                result.IsSuccess
                    ? result.HasWarnings ? OperationHistoryStatus.Warning : OperationHistoryStatus.Success
                    : OperationHistoryStatus.Failed,
                FormatAppStoreRemotePreflightSummary(result),
                result.IsSuccess
                    ? FormatAppStoreRemotePreflightDetail(result)
                    : FormatIssueDetail(result.Issues));
        }
        finally
        {
            SetAppStoreRemotePreflightRunning(false);
        }
    }

    private async void OnRunUploadRemotePreflightClick(object sender, RoutedEventArgs e)
    {
        if (isAppStoreRemotePreflightRunning)
        {
            return;
        }

        SetAppStoreRemotePreflightRunning(true);
        ClearUploadRemotePreflightResult();
        SetUploadRemotePreflightStatus("检查中", (Brush)FindResource("PrimaryBrush"));

        try
        {
            var credential = new AppleApiKeyCredential(
                UploadApiKeyIdTextBox.Text,
                UploadIssuerIdTextBox.Text,
                ReadAppleApiPrivateKeyPem());
            var request = new AppStoreConnectRemotePreflightRequest(
                credential,
                IpaBundleIdTextBox.Text);

            var result = await appStoreConnectRemotePreflightService.CheckAsync(request);
            ShowUploadRemotePreflightResult(result);
            RecordHistory(
                "远端检查",
                result.IsSuccess
                    ? result.HasWarnings ? OperationHistoryStatus.Warning : OperationHistoryStatus.Success
                    : OperationHistoryStatus.Failed,
                FormatAppStoreRemotePreflightSummary(result),
                result.IsSuccess
                    ? FormatAppStoreRemotePreflightDetail(result)
                    : FormatIssueDetail(result.Issues));
        }
        finally
        {
            SetAppStoreRemotePreflightRunning(false);
        }
    }

    private async void OnLookupAppStoreAppClick(object sender, RoutedEventArgs e)
    {
        if (isAppStoreAppLookupRunning)
        {
            return;
        }

        SetAppStoreAppLookupRunning(true);
        ClearAppStoreAppLookupResult();
        SetAppStoreAppLookupStatus("查询中", (Brush)FindResource("PrimaryBrush"));

        try
        {
            var credential = new AppleApiKeyCredential(
                UploadApiKeyIdTextBox.Text,
                UploadIssuerIdTextBox.Text,
                ReadAppleApiPrivateKeyPem());
            var request = new AppStoreConnectAppLookupRequest(
                credential,
                IpaBundleIdTextBox.Text);

            var result = await appStoreConnectAppLookupService.LookupByBundleIdAsync(request);
            ShowAppStoreAppLookupResult(result);
            RecordHistory(
                "App 查询",
                result.IsSuccess
                    ? OperationHistoryStatus.Success
                    : OperationHistoryStatus.Failed,
                result.IsSuccess
                    ? (result.IsFound ? "已找到" : "未找到")
                    : "查询失败",
                result.IsSuccess
                    ? FormatAppStoreAppLookupDetail(result)
                    : FormatIssueDetail(result.Issues));
        }
        finally
        {
            SetAppStoreAppLookupRunning(false);
        }
    }

    private async void OnLookupAppStoreBundleIdClick(object sender, RoutedEventArgs e)
    {
        if (isAppStoreBundleIdLookupRunning)
        {
            return;
        }

        SetAppStoreBundleIdLookupRunning(true);
        ClearAppStoreBundleIdLookupResult();
        SetAppStoreBundleIdLookupStatus("查询中", (Brush)FindResource("PrimaryBrush"));

        try
        {
            var credential = new AppleApiKeyCredential(
                UploadApiKeyIdTextBox.Text,
                UploadIssuerIdTextBox.Text,
                ReadAppleApiPrivateKeyPem());
            var request = new AppStoreConnectBundleIdLookupRequest(
                credential,
                IpaBundleIdTextBox.Text);

            var result = await appStoreConnectBundleIdLookupService.LookupByIdentifierAsync(request);
            ShowAppStoreBundleIdLookupResult(result);
            RecordHistory(
                "Bundle 查询",
                result.IsSuccess
                    ? OperationHistoryStatus.Success
                    : OperationHistoryStatus.Failed,
                result.IsSuccess
                    ? (result.IsFound ? "已找到" : "未找到")
                    : "查询失败",
                result.IsSuccess
                    ? FormatAppStoreBundleIdLookupDetail(result)
                    : FormatIssueDetail(result.Issues));
        }
        finally
        {
            SetAppStoreBundleIdLookupRunning(false);
        }
    }

    private async void OnLookupAppStoreBuildsClick(object sender, RoutedEventArgs e)
    {
        if (isAppStoreBuildLookupRunning)
        {
            return;
        }

        SetAppStoreBuildLookupRunning(true);
        ClearAppStoreBuildLookupResult();
        SetAppStoreBuildLookupStatus("查询中", (Brush)FindResource("PrimaryBrush"));

        try
        {
            var credential = new AppleApiKeyCredential(
                UploadApiKeyIdTextBox.Text,
                UploadIssuerIdTextBox.Text,
                ReadAppleApiPrivateKeyPem());
            var request = new AppStoreConnectBuildLookupRequest(
                credential,
                IpaBundleIdTextBox.Text);

            var result = await appStoreConnectBuildLookupService.LookupByBundleIdAsync(request);
            ShowAppStoreBuildLookupResult(result);
            RecordHistory(
                "构建查询",
                result.IsSuccess
                    ? OperationHistoryStatus.Success
                    : OperationHistoryStatus.Failed,
                result.IsSuccess
                    ? FormatAppStoreBuildLookupSummary(result)
                    : "查询失败",
                result.IsSuccess
                    ? FormatAppStoreBuildLookupDetail(result)
                    : FormatIssueDetail(result.Issues));
        }
        finally
        {
            SetAppStoreBuildLookupRunning(false);
        }
    }

    private async void OnLookupAppStoreProfilesClick(object sender, RoutedEventArgs e)
    {
        if (isAppStoreProfileLookupRunning)
        {
            return;
        }

        SetAppStoreProfileLookupRunning(true);
        ClearAppStoreProfileLookupResult();
        SetAppStoreProfileLookupStatus("查询中", (Brush)FindResource("PrimaryBrush"));

        try
        {
            var credential = new AppleApiKeyCredential(
                UploadApiKeyIdTextBox.Text,
                UploadIssuerIdTextBox.Text,
                ReadAppleApiPrivateKeyPem());
            var request = new AppStoreConnectProfileLookupRequest(
                credential,
                IpaBundleIdTextBox.Text);

            var result = await appStoreConnectProfileLookupService.LookupByBundleIdAsync(request);
            ShowAppStoreProfileLookupResult(result);
            RecordHistory(
                "描述查询",
                result.IsSuccess
                    ? OperationHistoryStatus.Success
                    : OperationHistoryStatus.Failed,
                result.IsSuccess
                    ? FormatAppStoreProfileLookupSummary(result)
                    : "查询失败",
                result.IsSuccess
                    ? FormatAppStoreProfileLookupDetail(result)
                    : FormatIssueDetail(result.Issues));
        }
        finally
        {
            SetAppStoreProfileLookupRunning(false);
        }
    }

    private async void OnLookupAppStoreCertificatesClick(object sender, RoutedEventArgs e)
    {
        if (isAppStoreCertificateLookupRunning)
        {
            return;
        }

        SetAppStoreCertificateLookupRunning(true);
        ClearAppStoreCertificateLookupResult();
        SetAppStoreCertificateLookupStatus("查询中", (Brush)FindResource("PrimaryBrush"));

        try
        {
            var credential = new AppleApiKeyCredential(
                UploadApiKeyIdTextBox.Text,
                UploadIssuerIdTextBox.Text,
                ReadAppleApiPrivateKeyPem());
            var request = new AppStoreConnectCertificateLookupRequest(credential);

            var result = await appStoreConnectCertificateLookupService.LookupAsync(request);
            ShowAppStoreCertificateLookupResult(result);
            RecordHistory(
                "证书查询",
                result.IsSuccess
                    ? OperationHistoryStatus.Success
                    : OperationHistoryStatus.Failed,
                result.IsSuccess
                    ? FormatAppStoreCertificateLookupSummary(result)
                    : "查询失败",
                result.IsSuccess
                    ? FormatAppStoreCertificateLookupDetail(result)
                    : FormatIssueDetail(result.Issues));
        }
        finally
        {
            SetAppStoreCertificateLookupRunning(false);
        }
    }

    private async void OnLookupAppStoreDevicesClick(object sender, RoutedEventArgs e)
    {
        if (isAppStoreDeviceLookupRunning)
        {
            return;
        }

        SetAppStoreDeviceLookupRunning(true);
        ClearAppStoreDeviceLookupResult();
        SetAppStoreDeviceLookupStatus("查询中", (Brush)FindResource("PrimaryBrush"));

        try
        {
            var credential = new AppleApiKeyCredential(
                UploadApiKeyIdTextBox.Text,
                UploadIssuerIdTextBox.Text,
                ReadAppleApiPrivateKeyPem());
            var request = new AppStoreConnectDeviceLookupRequest(credential);

            var result = await appStoreConnectDeviceLookupService.LookupAsync(request);
            ShowAppStoreDeviceLookupResult(result);
            RecordHistory(
                "设备查询",
                result.IsSuccess
                    ? OperationHistoryStatus.Success
                    : OperationHistoryStatus.Failed,
                result.IsSuccess
                    ? FormatAppStoreDeviceLookupSummary(result)
                    : "查询失败",
                result.IsSuccess
                    ? FormatAppStoreDeviceLookupDetail(result)
                    : FormatIssueDetail(result.Issues));
        }
        finally
        {
            SetAppStoreDeviceLookupRunning(false);
        }
    }

    private async void OnRunUploadVerifyClick(object sender, RoutedEventArgs e)
    {
        await RunUploadTransporterAsync(UploadExecutionMode.Verify);
    }

    private async void OnRunUploadClick(object sender, RoutedEventArgs e)
    {
        await RunUploadTransporterAsync(UploadExecutionMode.Upload);
    }

    private async Task RunUploadTransporterAsync(UploadExecutionMode executionMode)
    {
        if (isUploadVerificationRunning)
        {
            return;
        }

        activeUploadExecutionMode = executionMode;
        RefreshUploadSettingsInputs();
        ClearUploadVerifyResult();

        var request = BuildUploadRequest(executionMode);
        var cancellation = new CancellationTokenSource();
        uploadVerificationCancellation = cancellation;
        SetUploadVerificationRunning(true);

        try
        {
            var progress = new Progress<UploadProgress>(ShowUploadVerifyProgress);
            var result = await uploadService.UploadAsync(request, progress, cancellation.Token);
            ShowUploadVerifyResult(result);
            ShowUploadVerifyEnvironmentResult(result);
            RecordHistory(
                FormatUploadActionName(executionMode),
                result.IsSuccess ? OperationHistoryStatus.Success : OperationHistoryStatus.Failed,
                FormatUploadResultStatus(executionMode, result.IsSuccess),
                FormatUploadResultDetail(result));
        }
        catch (OperationCanceledException)
        {
            var cancelledResult = UploadResult.Failure(
                null,
                string.Empty,
                string.Empty,
                new ValidationIssue(
                    UploadErrorCodes.ProcessCancelled,
                    ValidationSeverity.Error,
                    "Transporter verification was cancelled.",
                    "Run the verification again when ready."));
            ShowUploadVerifyResult(cancelledResult);
            RecordHistory(
                FormatUploadActionName(executionMode),
                OperationHistoryStatus.Failed,
                FormatUploadResultStatus(executionMode, isSuccess: false),
                FormatUploadResultDetail(cancelledResult));
        }
        finally
        {
            if (ReferenceEquals(uploadVerificationCancellation, cancellation))
            {
                uploadVerificationCancellation = null;
            }

            cancellation.Dispose();
            SetUploadVerificationRunning(false);
        }
    }

    private void OnCancelUploadVerifyClick(object sender, RoutedEventArgs e)
    {
        if (!isUploadVerificationRunning)
        {
            return;
        }

        CancelUploadVerifyButton.IsEnabled = false;
        SetUploadVerifyStatus("取消中", (Brush)FindResource("WarningBrush"));
        uploadVerificationCancellation?.Cancel();
    }

    private void OnCopyUploadLogClick(object sender, RoutedEventArgs e)
    {
        var copyText = FormatCurrentUploadLog();
        if (string.IsNullOrWhiteSpace(copyText))
        {
            SetUploadVerifyStatus("无日志", (Brush)FindResource("WarningBrush"));
            RecordHistory("复制日志", OperationHistoryStatus.Failed, "无日志");
            return;
        }

        Clipboard.SetText(copyText);
        SetUploadVerifyStatus("已复制", (Brush)FindResource("SuccessBrush"));
        RecordHistory("复制日志", OperationHistoryStatus.Success, "已复制", copyText);
    }

    private UploadReadinessResult EvaluateUploadReadiness()
    {
        RefreshUploadInputs();

        var result = uploadReadinessEvaluator.Evaluate(new UploadReadinessRequest(
            UploadTarget.AppStore,
            lastIpaMetadata,
            lastImportedProfile));

        ShowUploadReadiness(result);
        return result;
    }

    private void ValidateUploadEnvironment()
    {
        RefreshUploadSettingsInputs();

        var result = uploadService.ValidateEnvironment(BuildUploadRequest(UploadExecutionMode.Verify));
        lastUploadEnvironmentValidation = result;
        ShowUploadEnvironment(result);
        RecordHistory(
            "环境验证",
            result.IsSuccess ? OperationHistoryStatus.Success : OperationHistoryStatus.Failed,
            result.IsSuccess ? "环境可用" : "环境异常",
            FormatIssueDetail(result.Issues));
    }

    private void ClearCertificateResult()
    {
        lastCertificateProjectDirectory = null;
        OpenCertificateProjectButton.IsEnabled = false;
        CertificateProjectDirectoryTextBox.Text = string.Empty;
        CertificatePrivateKeyPathTextBox.Text = string.Empty;
        CertificateCsrPathTextBox.Text = string.Empty;
        CertificateCerPathTextBox.Text = string.Empty;
        CertificateP12PathTextBox.Text = string.Empty;
        CertificateP12PasswordBox.Clear();
        CertificateStatusText.Text = string.Empty;
    }

    private void ClearProfileResult()
    {
        lastImportedProfile = null;
        lastImportedProfilePath = string.Empty;
        ProfileNameTextBox.Text = string.Empty;
        ProfileTypeTextBox.Text = string.Empty;
        ProfileParsedStatusTextBox.Text = string.Empty;
        ProfileDeviceCountTextBox.Text = string.Empty;
        ProfileTeamIdTextBox.Text = string.Empty;
        ProfileBundleIdTextBox.Text = string.Empty;
        ProfileUuidTextBox.Text = string.Empty;
        ProfileExpirationTextBox.Text = string.Empty;
        ProfileImportedPathTextBox.Text = string.Empty;
        ProfileStatusText.Text = string.Empty;
        RefreshUploadInputs();
    }

    private void ClearIpaResult()
    {
        lastIpaMetadata = null;
        lastIpaImportedPath = string.Empty;
        lastUploadEnvironmentValidation = null;
        ClearUploadRemotePreflightResult();
        ClearAppStoreAppLookupResult();
        IpaBundleIdTextBox.Text = string.Empty;
        IpaVersionTextBox.Text = string.Empty;
        IpaBuildTextBox.Text = string.Empty;
        IpaSizeTextBox.Text = string.Empty;
        IpaCodeSignTextBox.Text = string.Empty;
        IpaEmbeddedProfileTextBox.Text = string.Empty;
        IpaAppBundlePathTextBox.Text = string.Empty;
        IpaImportedPathTextBox.Text = string.Empty;
        IpaStatusText.Text = string.Empty;
        RefreshUploadInputs();
    }

    private void ShowProfile(ProvisioningProfile profile, string importedPath)
    {
        ProfileNameTextBox.Text = profile.Name;
        ProfileTypeTextBox.Text = FormatProfileType(profile.Type);
        ProfileParsedStatusTextBox.Text = profile.Status == ProvisioningProfileStatus.Active ? "有效" : "过期";
        ProfileDeviceCountTextBox.Text = profile.ProvisionedDeviceCount.ToString();
        ProfileTeamIdTextBox.Text = profile.TeamId;
        ProfileBundleIdTextBox.Text = profile.BundleIdentifier;
        ProfileUuidTextBox.Text = profile.Uuid;
        ProfileExpirationTextBox.Text = profile.ExpirationDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        ProfileImportedPathTextBox.Text = importedPath;
    }

    private void ShowIpa(IpaMetadata metadata, string importedPath)
    {
        IpaBundleIdTextBox.Text = metadata.BundleIdentifier;
        IpaVersionTextBox.Text = metadata.ShortVersion;
        IpaBuildTextBox.Text = metadata.BuildVersion;
        IpaSizeTextBox.Text = FormatFileSize(metadata.FileSizeBytes);
        IpaCodeSignTextBox.Text = metadata.SignaturePresence.HasCodeResources ? "存在" : "缺失";
        IpaEmbeddedProfileTextBox.Text = FormatEmbeddedProfile(metadata);
        IpaAppBundlePathTextBox.Text = metadata.AppBundlePath;
        IpaImportedPathTextBox.Text = importedPath;
    }

    private void RefreshUploadInputs()
    {
        UploadIpaTextBox.Text = FormatUploadIpa(lastIpaMetadata, lastIpaImportedPath);
        UploadProfileTextBox.Text = FormatUploadProfile(lastImportedProfile, lastImportedProfilePath);
        RefreshUploadSettingsInputs();
        RefreshUploadEnvironmentStatus();
    }

    private void RefreshAssets(bool recordHistory = false)
    {
        if (AssetListBox is null)
        {
            return;
        }

        var result = localAssetLibraryService.Scan(new LocalAssetLibraryRequest(
            CertificateBaseDirectoryTextBox.Text,
            ProfileBaseDirectoryTextBox.Text,
            IpaBaseDirectoryTextBox.Text));
        var items = result.Items.Select(AssetListItem.FromAsset).ToArray();

        AssetListBox.ItemsSource = items;
        AssetCountsText.Text = FormatAssetCounts(result.Items);
        AssetStatusText.Text = result.Issues.Count == 0 ? "已刷新" : "部分失败";
        AssetStatusText.Foreground = result.Issues.Count == 0
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("WarningBrush");

        UpdateDashboardAssetSummary(result, items);

        if (recordHistory)
        {
            RecordHistory(
                "刷新资产",
                result.Issues.Count == 0 ? OperationHistoryStatus.Success : OperationHistoryStatus.Warning,
                AssetStatusText.Text,
                result.Issues.Count == 0
                    ? FormatAssetCounts(result.Items)
                    : FormatIssueDetail(result.Issues));
        }
    }

    private void RefreshExpirationReminders(bool recordHistory = false)
    {
        if (DashboardExpirationSummaryText is null
            || DashboardExpirationCountsText is null
            || ExpirationReminderStatusText is null
            || ExpirationReminderListBox is null)
        {
            return;
        }

        var result = assetExpirationReminderService.Scan(new AssetExpirationReminderRequest(
            CertificateBaseDirectoryTextBox.Text,
            ProfileBaseDirectoryTextBox.Text));
        var items = result.Reminders
            .Select(ExpirationReminderListItem.FromReminder)
            .ToArray();

        ExpirationReminderListBox.ItemsSource = items;
        UpdateDashboardExpirationSummary(result.Reminders);

        if (items.Length == 0)
        {
            ExpirationReminderStatusText.Text = result.Issues.Count == 0 ? "暂无提醒" : "部分失败";
            ExpirationReminderStatusText.Foreground = result.Issues.Count == 0
                ? (Brush)FindResource("MutedTextBrush")
                : (Brush)FindResource("WarningBrush");
        }
        else
        {
            ExpirationReminderStatusText.Text = FormatExpirationReminderCounts(result.Reminders);
            ExpirationReminderStatusText.Foreground = result.Issues.Count == 0
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("WarningBrush");
        }

        if (recordHistory)
        {
            RecordHistory(
                "刷新到期",
                result.Issues.Count == 0 ? OperationHistoryStatus.Success : OperationHistoryStatus.Warning,
                ExpirationReminderStatusText.Text,
                result.Issues.Count == 0
                    ? FormatExpirationReminderCounts(result.Reminders)
                    : FormatIssueDetail(result.Issues));
        }
    }

    private void UpdateDashboardAssetSummary(LocalAssetLibraryResult result, IReadOnlyList<AssetListItem> items)
    {
        if (DashboardAssetTotalText is null
            || DashboardAssetCountsText is null
            || DashboardRecentAssetsStatusText is null
            || DashboardRecentAssetsListBox is null)
        {
            return;
        }

        DashboardAssetTotalText.Text = $"{result.Items.Count} 项";
        DashboardAssetCountsText.Text = FormatAssetCounts(result.Items);
        DashboardAssetTotalText.Foreground = result.Items.Count == 0
            ? (Brush)FindResource("MutedTextBrush")
            : (Brush)FindResource("TextBrush");

        var recentItems = items.Take(5).ToArray();
        DashboardRecentAssetsListBox.ItemsSource = recentItems;
        DashboardRecentAssetsStatusText.Text = recentItems.Length == 0 ? "暂无资产" : $"最近 {recentItems.Length}";
        DashboardRecentAssetsStatusText.Foreground = result.Issues.Count == 0
            ? (Brush)FindResource("MutedTextBrush")
            : (Brush)FindResource("WarningBrush");
    }

    private void UpdateDashboardExpirationSummary(IReadOnlyList<AssetExpirationReminder> reminders)
    {
        var expiredCount = reminders.Count(reminder => reminder.Status == AssetExpirationReminderStatus.Expired);
        var soonCount = reminders.Count(reminder => reminder.Status == AssetExpirationReminderStatus.ExpiringSoon);

        DashboardExpirationCountsText.Text = FormatExpirationReminderCounts(reminders);
        if (expiredCount > 0)
        {
            DashboardExpirationSummaryText.Text = $"过期 {expiredCount}";
            DashboardExpirationSummaryText.Foreground = (Brush)FindResource("DangerBrush");
            return;
        }

        if (soonCount > 0)
        {
            DashboardExpirationSummaryText.Text = $"临期 {soonCount}";
            DashboardExpirationSummaryText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        DashboardExpirationSummaryText.Text = "暂无提醒";
        DashboardExpirationSummaryText.Foreground = (Brush)FindResource("MutedTextBrush");
    }

    private void RefreshHistory()
    {
        if (HistoryListBox is null)
        {
            return;
        }

        var result = operationHistoryService.List();
        var items = result.Items
            .Select(item => HistoryListItem.FromHistory(
                item,
                GetHistoryStatusText(item.Status),
                GetHistoryStatusBrush(item.Status)))
            .ToArray();

        HistoryListBox.ItemsSource = items;
        HistoryCountsText.Text = $"{items.Length} 条";

        if (items.Length == 0)
        {
            HistoryStatusText.Text = "暂无记录";
            HistoryStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
            HistoryDetailTextBox.Text = string.Empty;
            return;
        }

        HistoryStatusText.Text = "已刷新";
        HistoryStatusText.Foreground = (Brush)FindResource("SuccessBrush");

        if (HistoryListBox.SelectedItem is null)
        {
            HistoryListBox.SelectedIndex = 0;
        }
    }

    private void RefreshUploadSettingsInputs()
    {
        if (UploadPackagePathTextBox is null)
        {
            return;
        }

        UploadPackagePathTextBox.Text = string.IsNullOrWhiteSpace(lastIpaImportedPath)
            ? "未检查"
            : lastIpaImportedPath;
        SetCredentialPanelsVisibility();
    }

    private void LoadUploadSettings()
    {
        var result = uploadSettingsService.Load();
        ApplyUploadSettings(result.Settings);

        if (result.Issues.Count > 0)
        {
            SetUploadSettingsStatus("加载异常", (Brush)FindResource("WarningBrush"));
            RecordHistory("加载设置", OperationHistoryStatus.Warning, "加载异常", FormatIssueDetail(result.Issues));
            return;
        }

        if (IsEmptyUploadSettings(result.Settings))
        {
            SetUploadSettingsStatus("未保存", (Brush)FindResource("MutedTextBrush"));
            RecordHistory("加载设置", OperationHistoryStatus.Warning, "未保存");
            return;
        }

        SetUploadSettingsStatus("已加载", (Brush)FindResource("MutedTextBrush"));
        RecordHistory("加载设置", OperationHistoryStatus.Success, "已加载", FormatUploadSettingsDetail(result.Settings));
    }

    private void SaveUploadSettings()
    {
        RefreshUploadSettingsInputs();

        var settings = ReadUploadSettings();
        var result = uploadSettingsService.Save(settings);
        if (!result.IsSuccess)
        {
            SetUploadSettingsStatus("保存失败", (Brush)FindResource("DangerBrush"));
            RecordHistory("保存设置", OperationHistoryStatus.Failed, "保存失败", FormatIssueDetail(result.Issues));
            return;
        }

        SetUploadSettingsStatus("已保存", (Brush)FindResource("SuccessBrush"));
        RecordHistory("保存设置", OperationHistoryStatus.Success, "已保存", FormatUploadSettingsDetail(settings));
    }

    private void ClearUploadSettings()
    {
        var result = uploadSettingsService.Clear();
        if (!result.IsSuccess)
        {
            SetUploadSettingsStatus("清除失败", (Brush)FindResource("DangerBrush"));
            RecordHistory("清除设置", OperationHistoryStatus.Failed, "清除失败", FormatIssueDetail(result.Issues));
            return;
        }

        ApplyUploadSettings(new UploadSettings());
        lastUploadEnvironmentValidation = null;
        RefreshUploadEnvironmentStatus();
        SetUploadSettingsStatus("已清除", (Brush)FindResource("SuccessBrush"));
        RecordHistory("清除设置", OperationHistoryStatus.Success, "已清除");
    }

    private UploadSettings ReadUploadSettings()
    {
        var mode = ReadUploadCredentialMode();

        return new UploadSettings(
            TransporterExecutablePath: TransporterPathTextBox.Text,
            PackagePath: lastIpaImportedPath,
            AssetDescriptionPath: UploadAssetDescriptionPathTextBox.Text,
            CredentialMode: mode,
            ApiKeyId: UploadApiKeyIdTextBox.Text,
            IssuerId: UploadIssuerIdTextBox.Text,
            PrivateKeyPath: AppleApiPrivateKeyPathTextBox.Text,
            AppleAccount: UploadAppleAccountTextBox.Text,
            SaveSensitiveValues: SaveSensitiveValuesCheckBox.IsChecked == true,
            Jwt: UploadJwtPasswordBox.Password,
            AppSpecificPassword: UploadAppSpecificPasswordBox.Password,
            CertificateDirectory: CertificateBaseDirectoryTextBox.Text,
            ProfileDirectory: ProfileBaseDirectoryTextBox.Text,
            IpaDirectory: IpaBaseDirectoryTextBox.Text);
    }

    private void ApplyUploadSettings(UploadSettings settings)
    {
        CertificateBaseDirectoryTextBox.Text = NonEmpty(settings.CertificateDirectory, DefaultCertificateDirectory());
        ProfileBaseDirectoryTextBox.Text = NonEmpty(settings.ProfileDirectory, DefaultProfileDirectory());
        IpaBaseDirectoryTextBox.Text = NonEmpty(settings.IpaDirectory, DefaultIpaDirectory());
        TransporterPathTextBox.Text = settings.TransporterExecutablePath;
        UploadAssetDescriptionPathTextBox.Text = settings.AssetDescriptionPath;
        UploadApiKeyIdTextBox.Text = settings.ApiKeyId;
        UploadIssuerIdTextBox.Text = settings.IssuerId;
        AppleApiPrivateKeyPathTextBox.Text = settings.PrivateKeyPath;
        UploadAppleAccountTextBox.Text = settings.AppleAccount;
        SaveSensitiveValuesCheckBox.IsChecked = settings.SaveSensitiveValues;
        SetUploadCredentialMode(settings.CredentialMode);

        UploadJwtPasswordBox.Password = settings.Jwt;
        UploadAppSpecificPasswordBox.Password = settings.AppSpecificPassword;
        lastIpaImportedPath = settings.PackagePath;
        RefreshUploadSettingsInputs();
        ClearAppleApiConnectionResult();
    }

    private void ApplyLibraryDirectoryDefaults()
    {
        CertificateBaseDirectoryTextBox.Text = DefaultCertificateDirectory();
        ProfileBaseDirectoryTextBox.Text = DefaultProfileDirectory();
        IpaBaseDirectoryTextBox.Text = DefaultIpaDirectory();
    }

    private void SetUploadSettingsStatus(string status, Brush foreground)
    {
        if (UploadSettingsStatusText is null)
        {
            return;
        }

        UploadSettingsStatusText.Text = status;
        UploadSettingsStatusText.Foreground = foreground;
    }

    private void RefreshUploadEnvironmentStatus()
    {
        if (UploadEnvironmentStatusText is null
            || SettingsUploadEnvironmentStatusText is null
            || UploadEnvironmentIssuesPanel is null)
        {
            return;
        }

        if (lastUploadEnvironmentValidation is null)
        {
            UploadEnvironmentStatusText.Text = "未验证";
            UploadEnvironmentStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
            SettingsUploadEnvironmentStatusText.Text = "未验证";
            SettingsUploadEnvironmentStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
            UploadEnvironmentIssuesPanel.Children.Clear();
            return;
        }

        ShowUploadEnvironment(lastUploadEnvironmentValidation);
    }

    private void ShowUploadReadiness(UploadReadinessResult result)
    {
        UploadStatusText.Text = FormatUploadStatus(result.Status);
        UploadStatusText.Foreground = GetUploadStatusBrush(result.Status);
        UploadChecksPanel.Children.Clear();

        foreach (UploadReadinessCheck check in result.Checks)
        {
            UploadChecksPanel.Children.Add(CreateUploadCheckRow(check));
        }
    }

    private void ClearUploadVerifyResult()
    {
        UploadVerifyExitCodeTextBox.Text = string.Empty;
        UploadVerifyStdoutTextBox.Text = string.Empty;
        UploadVerifyStderrTextBox.Text = string.Empty;
        UploadVerifyIssuesPanel.Children.Clear();
        SetUploadVerifyStatus(FormatUploadRunningStatus(activeUploadExecutionMode), (Brush)FindResource("PrimaryBrush"));
    }

    private void ShowUploadVerifyProgress(UploadProgress progress)
    {
        SetUploadVerifyStatus(
            FormatUploadVerifyPhase(progress.Phase),
            GetUploadVerifyPhaseBrush(progress.Phase));
    }

    private void ShowUploadVerifyResult(UploadResult result)
    {
        UploadVerifyExitCodeTextBox.Text = result.ExitCode?.ToString() ?? string.Empty;
        UploadVerifyStdoutTextBox.Text = result.StandardOutput;
        UploadVerifyStderrTextBox.Text = result.StandardError;
        UploadVerifyIssuesPanel.Children.Clear();

        SetUploadVerifyStatus(
            FormatUploadResultStatus(activeUploadExecutionMode, result.IsSuccess),
            result.IsSuccess
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("DangerBrush"));

        if (result.IsSuccess)
        {
            UploadVerifyIssuesPanel.Children.Add(CreateUploadIssueRow(FormatUploadActionName(activeUploadExecutionMode), "通过", true));
            return;
        }

        if (result.Issues.Count == 0)
        {
            UploadVerifyIssuesPanel.Children.Add(CreateUploadIssueRow(FormatUploadActionName(activeUploadExecutionMode), "失败", false));
            return;
        }

        foreach (ValidationIssue issue in result.Issues)
        {
            UploadVerifyIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatUploadIssueName(issue.Code),
                FormatUploadIssueAction(issue.Code),
                false));
        }
    }

    private void ShowUploadVerifyEnvironmentResult(UploadResult result)
    {
        var environmentIssues = result.Issues
            .Where(issue => IsUploadEnvironmentIssue(issue.Code))
            .ToArray();

        if (environmentIssues.Length > 0)
        {
            lastUploadEnvironmentValidation = UploadEnvironmentValidationResult.Failure(environmentIssues);
            ShowUploadEnvironment(lastUploadEnvironmentValidation);
            return;
        }

        lastUploadEnvironmentValidation = UploadEnvironmentValidationResult.Success();
        ShowUploadEnvironment(lastUploadEnvironmentValidation);
    }

    private void SetUploadVerificationRunning(bool isRunning)
    {
        isUploadVerificationRunning = isRunning;
        RunUploadVerifyButton.IsEnabled = !isRunning;
        RunUploadButton.IsEnabled = !isRunning;
        CancelUploadVerifyButton.IsEnabled = isRunning;
    }

    private void SetUploadVerifyStatus(string status, Brush foreground)
    {
        UploadVerifyStatusText.Text = status;
        UploadVerifyStatusText.Foreground = foreground;
    }

    private string FormatCurrentUploadLog()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(UploadVerifyStatusText.Text)
            && UploadVerifyStatusText.Text != "未校验")
        {
            parts.Add($"状态: {UploadVerifyStatusText.Text}");
        }

        if (!string.IsNullOrWhiteSpace(UploadVerifyExitCodeTextBox.Text))
        {
            parts.Add($"退出码: {UploadVerifyExitCodeTextBox.Text}");
        }

        if (!string.IsNullOrWhiteSpace(UploadVerifyStdoutTextBox.Text))
        {
            parts.Add($"输出:{Environment.NewLine}{UploadVerifyStdoutTextBox.Text}");
        }

        if (!string.IsNullOrWhiteSpace(UploadVerifyStderrTextBox.Text))
        {
            parts.Add($"错误:{Environment.NewLine}{UploadVerifyStderrTextBox.Text}");
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", parts);
    }

    private void ClearAppleApiConnectionResult()
    {
        SetAppleApiConnectionStatus("未检查", (Brush)FindResource("MutedTextBrush"));
        AppleApiConnectionIssuesPanel.Children.Clear();
        ClearAppStoreRemotePreflightResult();
        ClearUploadRemotePreflightResult();
        ClearAppStoreCertificateLookupResult();
        ClearAppStoreDeviceLookupResult();
        ClearAppStoreBundleIdLookupResult();
    }

    private void SetAppleApiConnectionStatus(string status, Brush foreground)
    {
        AppleApiConnectionStatusText.Text = status;
        AppleApiConnectionStatusText.Foreground = foreground;
    }

    private void ShowAppleApiConnectionResult(AppleDeveloperConnectionResult result)
    {
        SetAppleApiConnectionStatus(
            result.IsSuccess ? "已连接" : "未连接",
            result.IsSuccess
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("DangerBrush"));

        AppleApiConnectionIssuesPanel.Children.Clear();
        if (result.IsSuccess)
        {
            AppleApiConnectionIssuesPanel.Children.Add(CreateUploadIssueRow("账号", "通过", true));
            return;
        }

        foreach (ValidationIssue issue in result.Issues)
        {
            AppleApiConnectionIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatAppleAuthIssueName(issue.Code),
                FormatAppleAuthIssueAction(issue.Code),
                false));
        }
    }

    private void SetAppleApiConnectionChecking(bool isChecking)
    {
        isAppleApiConnectionChecking = isChecking;
        CheckAppleApiConnectionButton.IsEnabled = !isChecking;
    }

    private void ClearAppStoreRemotePreflightResult()
    {
        SetAppStoreRemotePreflightStatus("未检查", (Brush)FindResource("MutedTextBrush"));
        AppStoreRemotePreflightResultTextBox.Text = string.Empty;
        AppStoreRemotePreflightIssuesPanel.Children.Clear();
    }

    private void SetAppStoreRemotePreflightStatus(string status, Brush foreground)
    {
        AppStoreRemotePreflightStatusText.Text = status;
        AppStoreRemotePreflightStatusText.Foreground = foreground;
    }

    private void ShowAppStoreRemotePreflightResult(AppStoreConnectRemotePreflightResult result)
    {
        AppStoreRemotePreflightIssuesPanel.Children.Clear();
        AppStoreRemotePreflightResultTextBox.Text = FormatAppStoreRemotePreflightDetail(result);

        if (result.IsSuccess && !result.HasWarnings)
        {
            SetAppStoreRemotePreflightStatus("可用", (Brush)FindResource("SuccessBrush"));
            AppStoreRemotePreflightIssuesPanel.Children.Add(CreateUploadIssueRow("远端", "通过", true));
            return;
        }

        if (result.IsSuccess)
        {
            SetAppStoreRemotePreflightStatus("有提醒", (Brush)FindResource("WarningBrush"));
            foreach (ValidationIssue issue in result.Issues)
            {
                AppStoreRemotePreflightIssuesPanel.Children.Add(CreateUploadIssueRow(
                    FormatAppStoreRemotePreflightIssueName(issue.Code),
                    FormatAppStoreRemotePreflightIssueAction(issue.Code),
                    false));
            }

            return;
        }

        SetAppStoreRemotePreflightStatus("检查失败", (Brush)FindResource("DangerBrush"));
        foreach (ValidationIssue issue in result.Issues)
        {
            AppStoreRemotePreflightIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatAppStoreRemotePreflightIssueName(issue.Code),
                FormatAppStoreRemotePreflightIssueAction(issue.Code),
                false));
        }
    }

    private void SetAppStoreRemotePreflightRunning(bool isRunning)
    {
        isAppStoreRemotePreflightRunning = isRunning;
        if (RunAppStoreRemotePreflightButton is not null)
        {
            RunAppStoreRemotePreflightButton.IsEnabled = !isRunning;
        }

        if (RunUploadRemotePreflightButton is not null)
        {
            RunUploadRemotePreflightButton.IsEnabled = !isRunning;
        }
    }

    private void ClearUploadRemotePreflightResult()
    {
        if (UploadRemotePreflightStatusText is null
            || UploadRemotePreflightResultTextBox is null
            || UploadRemotePreflightIssuesPanel is null)
        {
            return;
        }

        SetUploadRemotePreflightStatus("未检查", (Brush)FindResource("MutedTextBrush"));
        UploadRemotePreflightResultTextBox.Text = string.Empty;
        UploadRemotePreflightIssuesPanel.Children.Clear();
    }

    private void SetUploadRemotePreflightStatus(string status, Brush foreground)
    {
        UploadRemotePreflightStatusText.Text = status;
        UploadRemotePreflightStatusText.Foreground = foreground;
    }

    private void ShowUploadRemotePreflightResult(AppStoreConnectRemotePreflightResult result)
    {
        UploadRemotePreflightIssuesPanel.Children.Clear();
        UploadRemotePreflightResultTextBox.Text = FormatAppStoreRemotePreflightDetail(result);

        if (result.IsSuccess && !result.HasWarnings)
        {
            SetUploadRemotePreflightStatus("可用", (Brush)FindResource("SuccessBrush"));
            UploadRemotePreflightIssuesPanel.Children.Add(CreateUploadIssueRow("远端", "通过", true));
            return;
        }

        if (result.IsSuccess)
        {
            SetUploadRemotePreflightStatus("有提醒", (Brush)FindResource("WarningBrush"));
            foreach (ValidationIssue issue in result.Issues)
            {
                UploadRemotePreflightIssuesPanel.Children.Add(CreateUploadIssueRow(
                    FormatAppStoreRemotePreflightIssueName(issue.Code),
                    FormatAppStoreRemotePreflightIssueAction(issue.Code),
                    false));
            }

            return;
        }

        SetUploadRemotePreflightStatus("检查失败", (Brush)FindResource("DangerBrush"));
        foreach (ValidationIssue issue in result.Issues)
        {
            UploadRemotePreflightIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatAppStoreRemotePreflightIssueName(issue.Code),
                FormatAppStoreRemotePreflightIssueAction(issue.Code),
                false));
        }
    }

    private void ClearAppStoreCertificateLookupResult()
    {
        SetAppStoreCertificateLookupStatus("未查询", (Brush)FindResource("MutedTextBrush"));
        AppStoreCertificateLookupResultTextBox.Text = string.Empty;
        AppStoreCertificateLookupIssuesPanel.Children.Clear();
    }

    private void SetAppStoreCertificateLookupStatus(string status, Brush foreground)
    {
        AppStoreCertificateLookupStatusText.Text = status;
        AppStoreCertificateLookupStatusText.Foreground = foreground;
    }

    private void ShowAppStoreCertificateLookupResult(AppStoreConnectCertificateLookupResult result)
    {
        AppStoreCertificateLookupIssuesPanel.Children.Clear();
        AppStoreCertificateLookupResultTextBox.Text = FormatAppStoreCertificateLookupDetail(result);

        if (result.IsSuccess && result.HasCertificates)
        {
            SetAppStoreCertificateLookupStatus("已找到", (Brush)FindResource("SuccessBrush"));
            AppStoreCertificateLookupIssuesPanel.Children.Add(CreateUploadIssueRow("证书", $"{result.Certificates.Count} 个", true));
            return;
        }

        if (result.IsSuccess)
        {
            SetAppStoreCertificateLookupStatus("无证书", (Brush)FindResource("WarningBrush"));
            AppStoreCertificateLookupIssuesPanel.Children.Add(CreateUploadIssueRow("证书", "无", false));
            return;
        }

        SetAppStoreCertificateLookupStatus("查询失败", (Brush)FindResource("DangerBrush"));
        foreach (ValidationIssue issue in result.Issues)
        {
            AppStoreCertificateLookupIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatAppStoreCertificateLookupIssueName(issue.Code),
                FormatAppStoreCertificateLookupIssueAction(issue.Code),
                false));
        }
    }

    private void SetAppStoreCertificateLookupRunning(bool isRunning)
    {
        isAppStoreCertificateLookupRunning = isRunning;
        LookupAppStoreCertificatesButton.IsEnabled = !isRunning;
    }

    private void ClearAppStoreDeviceLookupResult()
    {
        SetAppStoreDeviceLookupStatus("未查询", (Brush)FindResource("MutedTextBrush"));
        AppStoreDeviceLookupResultTextBox.Text = string.Empty;
        AppStoreDeviceLookupIssuesPanel.Children.Clear();
    }

    private void SetAppStoreDeviceLookupStatus(string status, Brush foreground)
    {
        AppStoreDeviceLookupStatusText.Text = status;
        AppStoreDeviceLookupStatusText.Foreground = foreground;
    }

    private void ShowAppStoreDeviceLookupResult(AppStoreConnectDeviceLookupResult result)
    {
        AppStoreDeviceLookupIssuesPanel.Children.Clear();
        AppStoreDeviceLookupResultTextBox.Text = FormatAppStoreDeviceLookupDetail(result);

        if (result.IsSuccess && result.HasDevices)
        {
            SetAppStoreDeviceLookupStatus("已找到", (Brush)FindResource("SuccessBrush"));
            AppStoreDeviceLookupIssuesPanel.Children.Add(CreateUploadIssueRow("设备", $"{result.Devices.Count} 个", true));
            return;
        }

        if (result.IsSuccess)
        {
            SetAppStoreDeviceLookupStatus("无设备", (Brush)FindResource("WarningBrush"));
            AppStoreDeviceLookupIssuesPanel.Children.Add(CreateUploadIssueRow("设备", "无", false));
            return;
        }

        SetAppStoreDeviceLookupStatus("查询失败", (Brush)FindResource("DangerBrush"));
        foreach (ValidationIssue issue in result.Issues)
        {
            AppStoreDeviceLookupIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatAppStoreDeviceLookupIssueName(issue.Code),
                FormatAppStoreDeviceLookupIssueAction(issue.Code),
                false));
        }
    }

    private void SetAppStoreDeviceLookupRunning(bool isRunning)
    {
        isAppStoreDeviceLookupRunning = isRunning;
        LookupAppStoreDevicesButton.IsEnabled = !isRunning;
    }

    private void ClearAppStoreBundleIdLookupResult()
    {
        SetAppStoreBundleIdLookupStatus("未查询", (Brush)FindResource("MutedTextBrush"));
        AppStoreBundleIdLookupResultTextBox.Text = string.Empty;
        AppStoreBundleIdLookupIssuesPanel.Children.Clear();
        ClearAppStoreAppLookupResult();
        ClearAppStoreProfileLookupResult();
    }

    private void SetAppStoreBundleIdLookupStatus(string status, Brush foreground)
    {
        AppStoreBundleIdLookupStatusText.Text = status;
        AppStoreBundleIdLookupStatusText.Foreground = foreground;
    }

    private void ShowAppStoreBundleIdLookupResult(AppStoreConnectBundleIdLookupResult result)
    {
        AppStoreBundleIdLookupIssuesPanel.Children.Clear();
        AppStoreBundleIdLookupResultTextBox.Text = FormatAppStoreBundleIdLookupDetail(result);

        if (result.IsSuccess && result.IsFound)
        {
            SetAppStoreBundleIdLookupStatus("已找到", (Brush)FindResource("SuccessBrush"));
            AppStoreBundleIdLookupIssuesPanel.Children.Add(CreateUploadIssueRow("Bundle", "存在", true));
            return;
        }

        if (result.IsSuccess)
        {
            SetAppStoreBundleIdLookupStatus("未找到", (Brush)FindResource("WarningBrush"));
            AppStoreBundleIdLookupIssuesPanel.Children.Add(CreateUploadIssueRow("Bundle", "未找到", false));
            return;
        }

        SetAppStoreBundleIdLookupStatus("查询失败", (Brush)FindResource("DangerBrush"));
        foreach (ValidationIssue issue in result.Issues)
        {
            AppStoreBundleIdLookupIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatAppStoreBundleIdLookupIssueName(issue.Code),
                FormatAppStoreBundleIdLookupIssueAction(issue.Code),
                false));
        }
    }

    private void SetAppStoreBundleIdLookupRunning(bool isRunning)
    {
        isAppStoreBundleIdLookupRunning = isRunning;
        LookupAppStoreBundleIdButton.IsEnabled = !isRunning;
    }

    private void ClearAppStoreAppLookupResult()
    {
        SetAppStoreAppLookupStatus("未查询", (Brush)FindResource("MutedTextBrush"));
        AppStoreAppLookupResultTextBox.Text = string.Empty;
        AppStoreAppLookupIssuesPanel.Children.Clear();
        ClearAppStoreBuildLookupResult();
    }

    private void SetAppStoreAppLookupStatus(string status, Brush foreground)
    {
        AppStoreAppLookupStatusText.Text = status;
        AppStoreAppLookupStatusText.Foreground = foreground;
    }

    private void ShowAppStoreAppLookupResult(AppStoreConnectAppLookupResult result)
    {
        AppStoreAppLookupIssuesPanel.Children.Clear();
        AppStoreAppLookupResultTextBox.Text = FormatAppStoreAppLookupDetail(result);

        if (result.IsSuccess && result.IsFound)
        {
            SetAppStoreAppLookupStatus("已找到", (Brush)FindResource("SuccessBrush"));
            AppStoreAppLookupIssuesPanel.Children.Add(CreateUploadIssueRow("App", "存在", true));
            return;
        }

        if (result.IsSuccess)
        {
            SetAppStoreAppLookupStatus("未找到", (Brush)FindResource("WarningBrush"));
            AppStoreAppLookupIssuesPanel.Children.Add(CreateUploadIssueRow("App", "未找到", false));
            return;
        }

        SetAppStoreAppLookupStatus("查询失败", (Brush)FindResource("DangerBrush"));
        foreach (ValidationIssue issue in result.Issues)
        {
            AppStoreAppLookupIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatAppStoreAppLookupIssueName(issue.Code),
                FormatAppStoreAppLookupIssueAction(issue.Code),
                false));
        }
    }

    private void SetAppStoreAppLookupRunning(bool isRunning)
    {
        isAppStoreAppLookupRunning = isRunning;
        LookupAppStoreAppButton.IsEnabled = !isRunning;
    }

    private void ClearAppStoreBuildLookupResult()
    {
        SetAppStoreBuildLookupStatus("未查询", (Brush)FindResource("MutedTextBrush"));
        AppStoreBuildLookupResultTextBox.Text = string.Empty;
        AppStoreBuildLookupIssuesPanel.Children.Clear();
    }

    private void SetAppStoreBuildLookupStatus(string status, Brush foreground)
    {
        AppStoreBuildLookupStatusText.Text = status;
        AppStoreBuildLookupStatusText.Foreground = foreground;
    }

    private void ShowAppStoreBuildLookupResult(AppStoreConnectBuildLookupResult result)
    {
        AppStoreBuildLookupIssuesPanel.Children.Clear();
        AppStoreBuildLookupResultTextBox.Text = FormatAppStoreBuildLookupDetail(result);

        if (result.IsSuccess && result.HasBuilds)
        {
            SetAppStoreBuildLookupStatus("已找到", (Brush)FindResource("SuccessBrush"));
            AppStoreBuildLookupIssuesPanel.Children.Add(CreateUploadIssueRow("构建", $"{result.Builds.Count} 个", true));
            return;
        }

        if (result.IsSuccess && !result.IsAppFound)
        {
            SetAppStoreBuildLookupStatus("App 未找到", (Brush)FindResource("WarningBrush"));
            AppStoreBuildLookupIssuesPanel.Children.Add(CreateUploadIssueRow("App", "未找到", false));
            return;
        }

        if (result.IsSuccess)
        {
            SetAppStoreBuildLookupStatus("无构建", (Brush)FindResource("WarningBrush"));
            AppStoreBuildLookupIssuesPanel.Children.Add(CreateUploadIssueRow("构建", "无", false));
            return;
        }

        SetAppStoreBuildLookupStatus("查询失败", (Brush)FindResource("DangerBrush"));
        foreach (ValidationIssue issue in result.Issues)
        {
            AppStoreBuildLookupIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatAppStoreBuildLookupIssueName(issue.Code),
                FormatAppStoreBuildLookupIssueAction(issue.Code),
                false));
        }
    }

    private void SetAppStoreBuildLookupRunning(bool isRunning)
    {
        isAppStoreBuildLookupRunning = isRunning;
        LookupAppStoreBuildsButton.IsEnabled = !isRunning;
    }

    private void ClearAppStoreProfileLookupResult()
    {
        SetAppStoreProfileLookupStatus("未查询", (Brush)FindResource("MutedTextBrush"));
        AppStoreProfileLookupResultTextBox.Text = string.Empty;
        AppStoreProfileLookupIssuesPanel.Children.Clear();
    }

    private void SetAppStoreProfileLookupStatus(string status, Brush foreground)
    {
        AppStoreProfileLookupStatusText.Text = status;
        AppStoreProfileLookupStatusText.Foreground = foreground;
    }

    private void ShowAppStoreProfileLookupResult(AppStoreConnectProfileLookupResult result)
    {
        AppStoreProfileLookupIssuesPanel.Children.Clear();
        AppStoreProfileLookupResultTextBox.Text = FormatAppStoreProfileLookupDetail(result);

        if (result.IsSuccess && result.HasProfiles)
        {
            SetAppStoreProfileLookupStatus("已找到", (Brush)FindResource("SuccessBrush"));
            AppStoreProfileLookupIssuesPanel.Children.Add(CreateUploadIssueRow("描述", $"{result.Profiles.Count} 个", true));
            return;
        }

        if (result.IsSuccess && !result.IsBundleIdFound)
        {
            SetAppStoreProfileLookupStatus("Bundle 未找到", (Brush)FindResource("WarningBrush"));
            AppStoreProfileLookupIssuesPanel.Children.Add(CreateUploadIssueRow("Bundle", "未找到", false));
            return;
        }

        if (result.IsSuccess)
        {
            SetAppStoreProfileLookupStatus("无描述", (Brush)FindResource("WarningBrush"));
            AppStoreProfileLookupIssuesPanel.Children.Add(CreateUploadIssueRow("描述", "无", false));
            return;
        }

        SetAppStoreProfileLookupStatus("查询失败", (Brush)FindResource("DangerBrush"));
        foreach (ValidationIssue issue in result.Issues)
        {
            AppStoreProfileLookupIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatAppStoreProfileLookupIssueName(issue.Code),
                FormatAppStoreProfileLookupIssueAction(issue.Code),
                false));
        }
    }

    private void SetAppStoreProfileLookupRunning(bool isRunning)
    {
        isAppStoreProfileLookupRunning = isRunning;
        LookupAppStoreProfilesButton.IsEnabled = !isRunning;
    }

    private void ShowUploadEnvironment(UploadEnvironmentValidationResult result)
    {
        var status = result.IsSuccess ? "环境可用" : "环境异常";
        var foreground = result.IsSuccess
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("DangerBrush");

        UploadEnvironmentStatusText.Text = status;
        UploadEnvironmentStatusText.Foreground = foreground;
        SettingsUploadEnvironmentStatusText.Text = status;
        SettingsUploadEnvironmentStatusText.Foreground = foreground;

        UploadEnvironmentIssuesPanel.Children.Clear();
        if (result.IsSuccess)
        {
            UploadEnvironmentIssuesPanel.Children.Add(CreateUploadIssueRow("环境", "通过", true));
            return;
        }

        foreach (ValidationIssue issue in result.Issues)
        {
            UploadEnvironmentIssuesPanel.Children.Add(CreateUploadIssueRow(
                FormatUploadIssueName(issue.Code),
                FormatUploadIssueAction(issue.Code),
                false));
        }
    }

    private void RecordHistory(
        string operation,
        OperationHistoryStatus status,
        string summary,
        string? detail = null)
    {
        operationHistoryService.Record(new OperationHistoryRecordRequest(
            operation,
            status,
            summary,
            detail));

        if (HistoryPage is not null && HistoryPage.Visibility == Visibility.Visible)
        {
            RefreshHistory();
        }
    }

    private UploadRequest BuildUploadRequest(UploadExecutionMode executionMode)
    {
        var mode = ReadUploadCredentialMode();

        return new UploadRequest(
            TransporterPathTextBox.Text,
            lastIpaImportedPath,
            mode,
            executionMode,
            OptionalText(UploadAssetDescriptionPathTextBox.Text),
            ApiKeyId: mode == UploadCredentialMode.ApiKey ? OptionalText(UploadApiKeyIdTextBox.Text) : null,
            IssuerId: mode == UploadCredentialMode.ApiKey ? OptionalText(UploadIssuerIdTextBox.Text) : null,
            Jwt: mode == UploadCredentialMode.Jwt ? OptionalText(UploadJwtPasswordBox.Password) : null,
            AppleAccount: mode == UploadCredentialMode.AppleIdAppPassword ? OptionalText(UploadAppleAccountTextBox.Text) : null,
            AppSpecificPassword: mode == UploadCredentialMode.AppleIdAppPassword ? OptionalText(UploadAppSpecificPasswordBox.Password) : null,
            Timeout: TimeSpan.FromMinutes(30));
    }

    private UploadCredentialMode ReadUploadCredentialMode() =>
        UploadCredentialModeComboBox.SelectedIndex switch
        {
            1 => UploadCredentialMode.Jwt,
            2 => UploadCredentialMode.AppleIdAppPassword,
            _ => UploadCredentialMode.ApiKey
        };

    private void SetUploadCredentialMode(UploadCredentialMode mode)
    {
        UploadCredentialModeComboBox.SelectedIndex = mode switch
        {
            UploadCredentialMode.Jwt => 1,
            UploadCredentialMode.AppleIdAppPassword => 2,
            _ => 0
        };

        SetCredentialPanelsVisibility();
    }

    private void SetCredentialPanelsVisibility()
    {
        if (ApiKeyCredentialPanel is null
            || JwtCredentialPanel is null
            || AppPasswordCredentialPanel is null
            || UploadCredentialModeComboBox is null)
        {
            return;
        }

        var mode = ReadUploadCredentialMode();
        ApiKeyCredentialPanel.Visibility = mode == UploadCredentialMode.ApiKey ? Visibility.Visible : Visibility.Collapsed;
        JwtCredentialPanel.Visibility = mode == UploadCredentialMode.Jwt ? Visibility.Visible : Visibility.Collapsed;
        AppPasswordCredentialPanel.Visibility = mode == UploadCredentialMode.AppleIdAppPassword ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement CreateUploadCheckRow(UploadReadinessCheck check)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        var statusPill = new Border
        {
            Background = GetUploadCheckBrush(check.Status),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = FormatUploadCheckStatus(check.Status),
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            }
        };
        Grid.SetColumn(statusPill, 0);

        var nameText = new TextBlock
        {
            Text = FormatUploadCheckName(check.Code),
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(nameText, 1);

        var actionText = new TextBlock
        {
            Text = FormatUploadCheckAction(check),
            Foreground = (Brush)FindResource("MutedTextBrush"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(actionText, 2);

        row.Children.Add(statusPill);
        row.Children.Add(nameText);
        row.Children.Add(actionText);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = row
        };
    }

    private FrameworkElement CreateUploadIssueRow(string name, string action, bool isSuccess)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        var statusPill = new Border
        {
            Background = isSuccess
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("DangerBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = isSuccess ? "通过" : "错误",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            }
        };
        Grid.SetColumn(statusPill, 0);

        var nameText = new TextBlock
        {
            Text = name,
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(nameText, 1);

        var actionText = new TextBlock
        {
            Text = action,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(actionText, 2);

        row.Children.Add(statusPill);
        row.Children.Add(nameText);
        row.Children.Add(actionText);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = row
        };
    }

    private SigningPurpose ReadSelectedPurpose() =>
        CertificatePurposeComboBox.SelectedIndex == 0
            ? SigningPurpose.Development
            : SigningPurpose.Distribution;

    private static string? OptionalText(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string ReadAppleApiPrivateKeyPem()
    {
        var path = AppleApiPrivateKeyPathTextBox.Text;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private void SetCertificateStatus(string message, bool isSuccess)
    {
        CertificateStatusText.Text = message;
        CertificateStatusText.Foreground = isSuccess
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("WarningBrush");
    }

    private void SetProfileStatus(string message, bool isSuccess)
    {
        ProfileStatusText.Text = message;
        ProfileStatusText.Foreground = isSuccess
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("WarningBrush");
    }

    private void SetIpaStatus(string message, bool isSuccess)
    {
        IpaStatusText.Text = message;
        IpaStatusText.Foreground = isSuccess
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("WarningBrush");
    }

    private static OperationHistoryStatus ToHistoryStatus(UploadReadinessStatus status) =>
        status switch
        {
            UploadReadinessStatus.Ready => OperationHistoryStatus.Success,
            UploadReadinessStatus.ReadyWithWarnings => OperationHistoryStatus.Warning,
            UploadReadinessStatus.Blocked => OperationHistoryStatus.Failed,
            _ => OperationHistoryStatus.Warning
        };

    private static string GetHistoryStatusText(OperationHistoryStatus status) =>
        status switch
        {
            OperationHistoryStatus.Success => "成功",
            OperationHistoryStatus.Warning => "警告",
            OperationHistoryStatus.Failed => "失败",
            _ => "未知"
        };

    private Brush GetHistoryStatusBrush(OperationHistoryStatus status) =>
        status switch
        {
            OperationHistoryStatus.Success => (Brush)FindResource("SuccessBrush"),
            OperationHistoryStatus.Warning => (Brush)FindResource("WarningBrush"),
            OperationHistoryStatus.Failed => (Brush)FindResource("DangerBrush"),
            _ => (Brush)FindResource("MutedTextBrush")
        };

    private static string FormatIssueDetail(IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, issues.Select(issue =>
        {
            var action = string.IsNullOrWhiteSpace(issue.SuggestedAction)
                ? string.Empty
                : $" / {issue.SuggestedAction}";
            return $"{issue.Code}: {issue.Message}{action}";
        }));
    }

    private static string FormatUploadResultDetail(UploadResult result)
    {
        var parts = new List<string>();

        if (result.ExitCode is not null)
        {
            parts.Add($"ExitCode: {result.ExitCode}");
        }

        if (result.Issues.Count > 0)
        {
            parts.Add(FormatIssueDetail(result.Issues));
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            parts.Add($"Output:{Environment.NewLine}{result.StandardOutput}");
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            parts.Add($"Error:{Environment.NewLine}{result.StandardError}");
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", parts);
    }

    private static string FormatHistoryCopy(IReadOnlyList<OperationHistoryItem> items) =>
        string.Join($"{Environment.NewLine}{Environment.NewLine}", items.Select(item =>
            $"{item.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} {GetHistoryStatusText(item.Status)} {item.Operation}{Environment.NewLine}{item.Summary}{Environment.NewLine}{item.Detail}".Trim()));

    private static string FormatIssues(IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Count == 0)
        {
            return "生成失败";
        }

        return string.Join("；", issues.Select(ToUserMessage).Distinct());
    }

    private static string ToUserMessage(ValidationIssue issue) =>
        issue.Code switch
        {
            CertificateProofErrorCodes.EmptyProjectName => "项目名必填",
            CertificateProofErrorCodes.MissingProjectDirectory => "目录必填",
            CertificateProofErrorCodes.EmptySubjectCommonName => "名称必填",
            CertificateProofErrorCodes.InvalidCountryCode => "国家填两位",
            CertificateProofErrorCodes.ProjectCreateFailed => "创建失败",
            CertificateProofErrorCodes.ProjectNotFound => "项目不存在",
            CertificateProofErrorCodes.MissingCertificate => "CER 必填",
            CertificateProofErrorCodes.InvalidCertificate => "证书无效",
            CertificateProofErrorCodes.EmptyP12Password => "密码必填",
            CertificateProofErrorCodes.MissingPrivateKey => "私钥缺失",
            CertificateProofErrorCodes.P12ExportFailed => "P12 失败",
            CertificateProofErrorCodes.ProjectExportFailed => "导出失败",
            _ => "生成失败"
        };

    private static string FormatProfileIssues(IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Count == 0)
        {
            return "导入失败";
        }

        return string.Join("；", issues.Select(ToProfileUserMessage).Distinct());
    }

    private static string ToProfileUserMessage(ValidationIssue issue) =>
        issue.Code switch
        {
            ProvisioningProfileErrorCodes.ImportFileMissing => "文件必填",
            ProvisioningProfileErrorCodes.ImportFileNotFound => "文件不存在",
            ProvisioningProfileErrorCodes.ImportDirectoryMissing => "目录必填",
            ProvisioningProfileErrorCodes.ImportFailed => "导入失败",
            ProvisioningProfileErrorCodes.EmptyPayload => "文件为空",
            ProvisioningProfileErrorCodes.PlistNotFound => "格式无效",
            ProvisioningProfileErrorCodes.MalformedPlist => "格式无效",
            ProvisioningProfileErrorCodes.MissingRequiredKey => "字段缺失",
            ProvisioningProfileErrorCodes.ExpiredProfile => "已过期",
            ProvisioningProfileErrorCodes.UnknownProfileType => "类型未知",
            _ => "导入失败"
        };

    private static string FormatProfileType(ProvisioningProfileType type) =>
        type switch
        {
            ProvisioningProfileType.Development => "开发",
            ProvisioningProfileType.AdHoc => "Ad Hoc",
            ProvisioningProfileType.AppStore => "App Store",
            ProvisioningProfileType.Enterprise => "企业",
            _ => "未知"
        };

    private static string FormatIpaIssues(IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Count == 0)
        {
            return "检查失败";
        }

        return string.Join("；", issues.Select(ToIpaUserMessage).Distinct());
    }

    private static string ToIpaUserMessage(ValidationIssue issue) =>
        issue.Code switch
        {
            IpaInspectionErrorCodes.ImportFileMissing => "文件必填",
            IpaInspectionErrorCodes.ImportFileNotFound => "文件不存在",
            IpaInspectionErrorCodes.ImportDirectoryMissing => "目录必填",
            IpaInspectionErrorCodes.ImportFailed => "导入失败",
            IpaInspectionErrorCodes.EmptyPayload => "文件为空",
            IpaInspectionErrorCodes.InvalidArchive => "IPA 无效",
            IpaInspectionErrorCodes.AppBundleMissing => "App 缺失",
            IpaInspectionErrorCodes.MultipleAppBundles => "App 过多",
            IpaInspectionErrorCodes.InfoPlistMissing => "Info 缺失",
            IpaInspectionErrorCodes.InfoPlistUnsupported => "格式不支持",
            IpaInspectionErrorCodes.InfoPlistMalformed => "Info 无效",
            IpaInspectionErrorCodes.MissingRequiredKey => "字段缺失",
            IpaInspectionErrorCodes.EmbeddedProfileInvalid => "描述无效",
            _ => "检查失败"
        };

    private static string FormatBackupIssues(IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Count == 0)
        {
            return "备份失败";
        }

        return string.Join("；", issues.Select(ToBackupUserMessage).Distinct());
    }

    private static string ToBackupUserMessage(ValidationIssue issue) =>
        issue.Code switch
        {
            CertificateProjectBackupErrorCodes.ProjectDirectoryMissing => "未选择",
            CertificateProjectBackupErrorCodes.ProjectNotFound => "项目不存在",
            CertificateProjectBackupErrorCodes.MetadataMissing => "项目无效",
            CertificateProjectBackupErrorCodes.OutputDirectoryMissing => "目录必填",
            CertificateProjectBackupErrorCodes.OutputDirectoryNotFound => "目录不存在",
            CertificateProjectBackupErrorCodes.ExportFailed => "备份失败",
            _ => "备份失败"
        };

    private static string FormatFileSize(long bytes)
    {
        var megabytes = bytes / 1024d / 1024d;
        return $"{megabytes:0.##} MB";
    }

    private static string FormatEmbeddedProfile(IpaMetadata metadata)
    {
        if (!metadata.HasEmbeddedProvisioningProfile)
        {
            return "缺失";
        }

        if (metadata.EmbeddedProvisioningProfile is null)
        {
            return "无效";
        }

        return $"{FormatProfileType(metadata.EmbeddedProvisioningProfile.Type)} / {FormatProfileStatus(metadata.EmbeddedProvisioningProfile.Status)}";
    }

    private static string FormatUploadIpa(IpaMetadata? metadata, string importedPath)
    {
        if (metadata is null)
        {
            return "未检查";
        }

        var bundle = NonEmpty(metadata.BundleIdentifier, "Bundle 缺失");
        var version = NonEmpty(metadata.ShortVersion, "版本缺失");
        var build = NonEmpty(metadata.BuildVersion, "Build 缺失");
        var path = string.IsNullOrWhiteSpace(importedPath) ? string.Empty : $" / {Path.GetFileName(importedPath)}";
        return $"{bundle} / {version} ({build}){path}";
    }

    private static string FormatUploadProfile(ProvisioningProfile? profile, string importedPath)
    {
        if (profile is null)
        {
            return "未导入";
        }

        var path = string.IsNullOrWhiteSpace(importedPath) ? string.Empty : $" / {Path.GetFileName(importedPath)}";
        return $"{FormatProfileType(profile.Type)} / {FormatProfileStatus(profile.Status)} / {profile.BundleIdentifier}{path}";
    }

    private static string FormatAssetCounts(IReadOnlyList<LocalAssetItem> items)
    {
        var certificateCount = items.Count(item => item.Type == LocalAssetType.CertificateProject);
        var profileCount = items.Count(item => item.Type == LocalAssetType.ProvisioningProfile);
        var ipaCount = items.Count(item => item.Type == LocalAssetType.Ipa);

        return $"证书 {certificateCount} / 描述 {profileCount} / IPA {ipaCount}";
    }

    private static string FormatExpirationReminderCounts(IReadOnlyList<AssetExpirationReminder> reminders)
    {
        var expiredCount = reminders.Count(reminder => reminder.Status == AssetExpirationReminderStatus.Expired);
        var soonCount = reminders.Count(reminder => reminder.Status == AssetExpirationReminderStatus.ExpiringSoon);

        return $"过期 {expiredCount} / 临期 {soonCount}";
    }

    private static string FormatLocalAssetType(LocalAssetType type) =>
        type switch
        {
            LocalAssetType.CertificateProject => "证书",
            LocalAssetType.ProvisioningProfile => "描述",
            LocalAssetType.Ipa => "IPA",
            _ => "资产"
        };

    private static string FormatExpirationReminderType(AssetExpirationReminderType type) =>
        type switch
        {
            AssetExpirationReminderType.Certificate => "证书",
            AssetExpirationReminderType.ProvisioningProfile => "描述",
            _ => "资产"
        };

    private static string FormatUploadActionName(UploadExecutionMode executionMode) =>
        executionMode == UploadExecutionMode.Upload ? "上传" : "校验";

    private static string FormatUploadSettingsDetail(UploadSettings settings)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(settings.TransporterExecutablePath))
        {
            parts.Add($"Transporter: {settings.TransporterExecutablePath}");
        }

        if (!string.IsNullOrWhiteSpace(settings.AssetDescriptionPath))
        {
            parts.Add($"AppStoreInfo: {settings.AssetDescriptionPath}");
        }

        if (!string.IsNullOrWhiteSpace(settings.PackagePath))
        {
            parts.Add($"IPA: {settings.PackagePath}");
        }

        if (!string.IsNullOrWhiteSpace(settings.CertificateDirectory))
        {
            parts.Add($"证书目录: {settings.CertificateDirectory}");
        }

        if (!string.IsNullOrWhiteSpace(settings.ProfileDirectory))
        {
            parts.Add($"描述目录: {settings.ProfileDirectory}");
        }

        if (!string.IsNullOrWhiteSpace(settings.IpaDirectory))
        {
            parts.Add($"IPA 目录: {settings.IpaDirectory}");
        }

        parts.Add($"凭据: {FormatUploadCredentialMode(settings.CredentialMode)}");
        parts.Add(settings.SaveSensitiveValues ? "敏感: 已保存" : "敏感: 未保存");
        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatUploadCredentialMode(UploadCredentialMode mode) =>
        mode switch
        {
            UploadCredentialMode.Jwt => "JWT",
            UploadCredentialMode.AppleIdAppPassword => "专用密码",
            _ => "API Key"
        };

    private static bool IsEmptyUploadSettings(UploadSettings settings) =>
        string.IsNullOrWhiteSpace(settings.TransporterExecutablePath)
        && string.IsNullOrWhiteSpace(settings.PackagePath)
        && string.IsNullOrWhiteSpace(settings.AssetDescriptionPath)
        && string.IsNullOrWhiteSpace(settings.ApiKeyId)
        && string.IsNullOrWhiteSpace(settings.IssuerId)
        && string.IsNullOrWhiteSpace(settings.PrivateKeyPath)
        && string.IsNullOrWhiteSpace(settings.AppleAccount)
        && string.IsNullOrWhiteSpace(settings.Jwt)
        && string.IsNullOrWhiteSpace(settings.AppSpecificPassword)
        && string.IsNullOrWhiteSpace(settings.CertificateDirectory)
        && string.IsNullOrWhiteSpace(settings.ProfileDirectory)
        && string.IsNullOrWhiteSpace(settings.IpaDirectory)
        && settings.CredentialMode == UploadCredentialMode.ApiKey
        && !settings.SaveSensitiveValues;

    private static string DefaultCertificateDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "P12Bridge",
            "Certificates");

    private static string DefaultProfileDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "P12Bridge",
            "Profiles");

    private static string DefaultIpaDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "P12Bridge",
            "IPAs");

    private static string FormatUploadRunningStatus(UploadExecutionMode executionMode) =>
        executionMode == UploadExecutionMode.Upload ? "上传中" : "校验中";

    private static string FormatUploadResultStatus(UploadExecutionMode executionMode, bool isSuccess)
    {
        if (executionMode == UploadExecutionMode.Upload)
        {
            return isSuccess ? "已上传" : "未上传";
        }

        return isSuccess ? "已通过" : "未通过";
    }

    private string FormatUploadStatus(UploadReadinessStatus status) =>
        status switch
        {
            UploadReadinessStatus.Ready => "可上传",
            UploadReadinessStatus.ReadyWithWarnings => "有警告",
            UploadReadinessStatus.Blocked => "已阻断",
            _ => "未检查"
        };

    private Brush GetUploadStatusBrush(UploadReadinessStatus status) =>
        status switch
        {
            UploadReadinessStatus.Ready => (Brush)FindResource("SuccessBrush"),
            UploadReadinessStatus.ReadyWithWarnings => (Brush)FindResource("WarningBrush"),
            UploadReadinessStatus.Blocked => (Brush)FindResource("DangerBrush"),
            _ => (Brush)FindResource("MutedTextBrush")
        };

    private Brush GetUploadCheckBrush(UploadReadinessCheckStatus status) =>
        status switch
        {
            UploadReadinessCheckStatus.Passed => (Brush)FindResource("SuccessBrush"),
            UploadReadinessCheckStatus.Warning => (Brush)FindResource("WarningBrush"),
            UploadReadinessCheckStatus.Blocked => (Brush)FindResource("DangerBrush"),
            _ => (Brush)FindResource("MutedTextBrush")
        };

    private string FormatUploadVerifyPhase(UploadPhase phase) =>
        phase switch
        {
            UploadPhase.ValidatingEnvironment => "检查环境",
            UploadPhase.BuildingCommand => "准备命令",
            UploadPhase.RunningTransporter => FormatUploadRunningStatus(activeUploadExecutionMode),
            UploadPhase.Completed => FormatUploadResultStatus(activeUploadExecutionMode, true),
            UploadPhase.Failed => FormatUploadResultStatus(activeUploadExecutionMode, false),
            _ => FormatUploadRunningStatus(activeUploadExecutionMode)
        };

    private Brush GetUploadVerifyPhaseBrush(UploadPhase phase) =>
        phase switch
        {
            UploadPhase.Completed => (Brush)FindResource("SuccessBrush"),
            UploadPhase.Failed => (Brush)FindResource("DangerBrush"),
            UploadPhase.ValidatingEnvironment => (Brush)FindResource("PrimaryBrush"),
            UploadPhase.BuildingCommand => (Brush)FindResource("PrimaryBrush"),
            UploadPhase.RunningTransporter => (Brush)FindResource("PrimaryBrush"),
            _ => (Brush)FindResource("MutedTextBrush")
        };

    private static string FormatUploadCheckStatus(UploadReadinessCheckStatus status) =>
        status switch
        {
            UploadReadinessCheckStatus.Passed => "通过",
            UploadReadinessCheckStatus.Warning => "警告",
            UploadReadinessCheckStatus.Blocked => "阻断",
            _ => "未知"
        };

    private static string FormatUploadCheckName(string code) =>
        code switch
        {
            UploadReadinessErrorCodes.AppStoreTargetSupported => "目标",
            UploadReadinessErrorCodes.IpaMetadataMissing => "IPA",
            UploadReadinessErrorCodes.IpaBundleIdMissing => "Bundle",
            UploadReadinessErrorCodes.IpaVersionMissing => "版本",
            UploadReadinessErrorCodes.IpaBuildMissing => "Build",
            UploadReadinessErrorCodes.IpaSignatureMarkerMissing => "代码签名",
            UploadReadinessErrorCodes.EmbeddedProfileMissing => "嵌入描述",
            UploadReadinessErrorCodes.EmbeddedProfileExpired => "嵌入有效期",
            UploadReadinessErrorCodes.EmbeddedProfileTypeInvalid => "嵌入类型",
            UploadReadinessErrorCodes.EmbeddedProfileBundleIdMismatch => "嵌入 Bundle",
            UploadReadinessErrorCodes.ImportedProfileMissing => "描述文件",
            UploadReadinessErrorCodes.ImportedProfileExpired => "导入有效期",
            UploadReadinessErrorCodes.ImportedProfileTypeInvalid => "导入类型",
            UploadReadinessErrorCodes.ImportedProfileBundleIdMismatch => "导入 Bundle",
            UploadReadinessErrorCodes.ImportedProfileTeamIdMismatch => "Team",
            UploadReadinessErrorCodes.ImportedProfileUuidMismatch => "UUID",
            _ => "检查项"
        };

    private static string FormatUploadCheckAction(UploadReadinessCheck check)
    {
        if (check.Status == UploadReadinessCheckStatus.Passed)
        {
            return check.Code == UploadReadinessErrorCodes.AppStoreTargetSupported ? "支持" : "正常";
        }

        return check.Code switch
        {
            UploadReadinessErrorCodes.IpaMetadataMissing => "检查 IPA",
            UploadReadinessErrorCodes.IpaBundleIdMissing => "重打包",
            UploadReadinessErrorCodes.IpaVersionMissing => "重打包",
            UploadReadinessErrorCodes.IpaBuildMissing => "重打包",
            UploadReadinessErrorCodes.IpaSignatureMarkerMissing => "重签名",
            UploadReadinessErrorCodes.EmbeddedProfileMissing => "重签名",
            UploadReadinessErrorCodes.EmbeddedProfileExpired => "更新描述",
            UploadReadinessErrorCodes.EmbeddedProfileTypeInvalid => "换描述",
            UploadReadinessErrorCodes.EmbeddedProfileBundleIdMismatch => "匹配 Bundle",
            UploadReadinessErrorCodes.ImportedProfileMissing => "导入描述",
            UploadReadinessErrorCodes.ImportedProfileExpired => "更新描述",
            UploadReadinessErrorCodes.ImportedProfileTypeInvalid => "换描述",
            UploadReadinessErrorCodes.ImportedProfileBundleIdMismatch => "匹配 Bundle",
            UploadReadinessErrorCodes.ImportedProfileTeamIdMismatch => "匹配 Team",
            UploadReadinessErrorCodes.ImportedProfileUuidMismatch => "可继续",
            _ => "处理"
        };
    }

    private static string FormatUploadIssueName(string code) =>
        code switch
        {
            UploadErrorCodes.TransporterPathMissing => "Transporter",
            UploadErrorCodes.TransporterNotFound => "Transporter",
            UploadErrorCodes.PackagePathMissing => "IPA",
            UploadErrorCodes.PackageNotFound => "IPA",
            UploadErrorCodes.AssetDescriptionPathMissing => "AppStoreInfo",
            UploadErrorCodes.AssetDescriptionNotFound => "AppStoreInfo",
            UploadErrorCodes.ApiKeyCredentialMissing => "API Key",
            UploadErrorCodes.JwtMissing => "JWT",
            UploadErrorCodes.AppleAccountMissing => "Apple 账号",
            UploadErrorCodes.AppSpecificPasswordMissing => "专用密码",
            UploadErrorCodes.ProcessStartFailed => "进程",
            UploadErrorCodes.ProcessTimedOut => "超时",
            UploadErrorCodes.ProcessCancelled => "取消",
            UploadErrorCodes.ProcessExitFailed => "Transporter",
            UploadErrorCodes.UnexpectedProcessResult => "结果",
            _ => "环境"
        };

    private static string FormatUploadIssueAction(string code) =>
        code switch
        {
            UploadErrorCodes.TransporterPathMissing => "选择文件",
            UploadErrorCodes.TransporterNotFound => "重新选择",
            UploadErrorCodes.PackagePathMissing => "检查 IPA",
            UploadErrorCodes.PackageNotFound => "检查 IPA",
            UploadErrorCodes.AssetDescriptionPathMissing => "选元数据",
            UploadErrorCodes.AssetDescriptionNotFound => "选元数据",
            UploadErrorCodes.ApiKeyCredentialMissing => "填写凭据",
            UploadErrorCodes.JwtMissing => "填写 JWT",
            UploadErrorCodes.AppleAccountMissing => "填写账号",
            UploadErrorCodes.AppSpecificPasswordMissing => "填写密码",
            UploadErrorCodes.ProcessStartFailed => "检查权限",
            UploadErrorCodes.ProcessTimedOut => "重试",
            UploadErrorCodes.ProcessCancelled => "重试",
            UploadErrorCodes.ProcessExitFailed => "看日志",
            UploadErrorCodes.UnexpectedProcessResult => "重试",
            _ => "处理"
        };

    private static string FormatAppleAuthIssueName(string code) =>
        code switch
        {
            AppleDeveloperAuthErrorCodes.MissingKeyId => "Key ID",
            AppleDeveloperAuthErrorCodes.MissingIssuerId => "Issuer ID",
            AppleDeveloperAuthErrorCodes.MissingPrivateKey => "P8 私钥",
            AppleDeveloperAuthErrorCodes.InvalidPrivateKey => "P8 私钥",
            AppleDeveloperAuthErrorCodes.AppleUnauthorized => "凭据",
            AppleDeveloperAuthErrorCodes.AppleForbidden => "权限",
            AppleDeveloperAuthErrorCodes.AppleApiUnavailable => "Apple",
            AppleDeveloperAuthErrorCodes.NetworkFailure => "网络",
            AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse => "Apple",
            _ => "账号"
        };

    private static string FormatAppleAuthIssueAction(string code) =>
        code switch
        {
            AppleDeveloperAuthErrorCodes.MissingKeyId => "填写",
            AppleDeveloperAuthErrorCodes.MissingIssuerId => "填写",
            AppleDeveloperAuthErrorCodes.MissingPrivateKey => "选择",
            AppleDeveloperAuthErrorCodes.InvalidPrivateKey => "重选",
            AppleDeveloperAuthErrorCodes.AppleUnauthorized => "核对",
            AppleDeveloperAuthErrorCodes.AppleForbidden => "查权限",
            AppleDeveloperAuthErrorCodes.AppleApiUnavailable => "稍后重试",
            AppleDeveloperAuthErrorCodes.NetworkFailure => "查网络",
            AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse => "重试",
            _ => "处理"
        };

    private static string FormatAppStoreRemotePreflightIssueName(string code) =>
        code switch
        {
            AppStoreConnectRemotePreflightErrorCodes.BundleIdMissing => "Bundle",
            AppStoreConnectRemotePreflightErrorCodes.AppMissing => "App",
            AppStoreConnectRemotePreflightErrorCodes.BundleIdNotRegistered => "Bundle",
            AppStoreConnectBundleIdLookupErrorCodes.ResponseMalformed => "Bundle 响应",
            AppStoreConnectAppLookupErrorCodes.ResponseMalformed => "App 响应",
            AppStoreConnectBuildLookupErrorCodes.ResponseMalformed => "构建响应",
            AppStoreConnectProfileLookupErrorCodes.ResponseMalformed => "描述响应",
            AppStoreConnectCertificateLookupErrorCodes.ResponseMalformed => "证书响应",
            AppStoreConnectDeviceLookupErrorCodes.ResponseMalformed => "设备响应",
            _ => FormatAppStoreAppLookupIssueName(code)
        };

    private static string FormatAppStoreRemotePreflightIssueAction(string code) =>
        code switch
        {
            AppStoreConnectRemotePreflightErrorCodes.BundleIdMissing => "检查 IPA",
            AppStoreConnectRemotePreflightErrorCodes.AppMissing => "建 App",
            AppStoreConnectRemotePreflightErrorCodes.BundleIdNotRegistered => "建 Bundle",
            AppStoreConnectBundleIdLookupErrorCodes.ResponseMalformed => "重试",
            AppStoreConnectAppLookupErrorCodes.ResponseMalformed => "重试",
            AppStoreConnectBuildLookupErrorCodes.ResponseMalformed => "重试",
            AppStoreConnectProfileLookupErrorCodes.ResponseMalformed => "重试",
            AppStoreConnectCertificateLookupErrorCodes.ResponseMalformed => "重试",
            AppStoreConnectDeviceLookupErrorCodes.ResponseMalformed => "重试",
            _ => FormatAppStoreAppLookupIssueAction(code)
        };

    private static string FormatAppStoreRemotePreflightSummary(AppStoreConnectRemotePreflightResult result)
    {
        if (!result.IsSuccess)
        {
            return "检查失败";
        }

        return result.HasWarnings ? "有提醒" : "可用";
    }

    private static string FormatAppStoreRemotePreflightDetail(AppStoreConnectRemotePreflightResult result)
    {
        if (!result.IsSuccess)
        {
            return FormatIssueDetail(result.Issues);
        }

        var parts = new List<string>
        {
            $"App: {(result.Summary.AppFound ? "存在" : "未找到")}",
            $"Bundle: {(result.Summary.BundleIdFound ? "存在" : "未找到")}",
            $"构建: {result.Summary.BuildCount}",
            $"描述: {result.Summary.ProfileCount}",
            $"证书: {result.Summary.CertificateCount}",
            $"设备: {result.Summary.DeviceCount}"
        };

        if (result.Issues.Count > 0)
        {
            parts.Add(string.Empty);
            parts.Add(FormatIssueDetail(result.Issues));
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatAppStoreBundleIdLookupIssueName(string code) =>
        code switch
        {
            AppStoreConnectBundleIdLookupErrorCodes.BundleIdMissing => "Bundle",
            AppStoreConnectBundleIdLookupErrorCodes.ResponseMalformed => "响应",
            _ => FormatAppleAuthIssueName(code)
        };

    private static string FormatAppStoreBundleIdLookupIssueAction(string code) =>
        code switch
        {
            AppStoreConnectBundleIdLookupErrorCodes.BundleIdMissing => "检查 IPA",
            AppStoreConnectBundleIdLookupErrorCodes.ResponseMalformed => "重试",
            _ => FormatAppleAuthIssueAction(code)
        };

    private static string FormatAppStoreBundleIdLookupDetail(AppStoreConnectBundleIdLookupResult result)
    {
        if (!result.IsSuccess)
        {
            return FormatIssueDetail(result.Issues);
        }

        if (result.BundleId is null)
        {
            return "未找到";
        }

        var parts = new List<string>
        {
            $"名称: {result.BundleId.Name}",
            $"Bundle: {result.BundleId.Identifier}",
            $"平台: {FormatBundlePlatform(result.BundleId.Platform)}",
            $"记录 ID: {result.BundleId.Id}"
        };

        if (!string.IsNullOrWhiteSpace(result.BundleId.SeedId))
        {
            parts.Add($"Seed: {result.BundleId.SeedId}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatBundlePlatform(string platform) =>
        platform switch
        {
            "IOS" => "iOS",
            "MAC_OS" => "macOS",
            "UNIVERSAL" => "通用",
            _ => platform
        };

    private static string FormatAppStoreAppLookupIssueName(string code) =>
        code switch
        {
            AppStoreConnectAppLookupErrorCodes.BundleIdMissing => "Bundle",
            AppStoreConnectAppLookupErrorCodes.ResponseMalformed => "响应",
            _ => FormatAppleAuthIssueName(code)
        };

    private static string FormatAppStoreAppLookupIssueAction(string code) =>
        code switch
        {
            AppStoreConnectAppLookupErrorCodes.BundleIdMissing => "检查 IPA",
            AppStoreConnectAppLookupErrorCodes.ResponseMalformed => "重试",
            _ => FormatAppleAuthIssueAction(code)
        };

    private static string FormatAppStoreAppLookupDetail(AppStoreConnectAppLookupResult result)
    {
        if (!result.IsSuccess)
        {
            return FormatIssueDetail(result.Issues);
        }

        if (result.App is null)
        {
            return "未找到";
        }

        var parts = new List<string>
        {
            $"名称: {result.App.Name}",
            $"Bundle: {result.App.BundleIdentifier}",
            $"App ID: {result.App.Id}"
        };

        if (!string.IsNullOrWhiteSpace(result.App.Sku))
        {
            parts.Add($"SKU: {result.App.Sku}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatAppStoreBuildLookupIssueName(string code) =>
        code switch
        {
            AppStoreConnectBuildLookupErrorCodes.ResponseMalformed => "响应",
            _ => FormatAppStoreAppLookupIssueName(code)
        };

    private static string FormatAppStoreBuildLookupIssueAction(string code) =>
        code switch
        {
            AppStoreConnectBuildLookupErrorCodes.ResponseMalformed => "重试",
            _ => FormatAppStoreAppLookupIssueAction(code)
        };

    private static string FormatAppStoreBuildLookupSummary(AppStoreConnectBuildLookupResult result)
    {
        if (!result.IsAppFound)
        {
            return "App 未找到";
        }

        return result.HasBuilds ? "已找到" : "无构建";
    }

    private static string FormatAppStoreBuildLookupDetail(AppStoreConnectBuildLookupResult result)
    {
        if (!result.IsSuccess)
        {
            return FormatIssueDetail(result.Issues);
        }

        if (result.App is null)
        {
            return "App 未找到";
        }

        if (result.Builds.Count == 0)
        {
            return "无构建";
        }

        return string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            result.Builds.Select(FormatAppStoreBuild));
    }

    private static string FormatAppStoreBuild(AppStoreConnectBuild build)
    {
        var parts = new List<string>
        {
            $"版本: {build.Version}",
            $"状态: {FormatBuildProcessingState(build.ProcessingState)}",
            $"构建 ID: {build.Id}"
        };

        if (build.UploadedDate is not null)
        {
            parts.Add($"上传: {build.UploadedDate.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
        }

        if (build.Expired is not null)
        {
            parts.Add($"过期: {(build.Expired.Value ? "是" : "否")}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatBuildProcessingState(string state) =>
        state switch
        {
            "VALID" => "有效",
            "PROCESSING" => "处理中",
            "FAILED" => "失败",
            "INVALID" => "无效",
            _ => state
        };

    private static string FormatAppStoreCertificateLookupIssueName(string code) =>
        code switch
        {
            AppStoreConnectCertificateLookupErrorCodes.ResponseMalformed => "响应",
            _ => FormatAppleAuthIssueName(code)
        };

    private static string FormatAppStoreCertificateLookupIssueAction(string code) =>
        code switch
        {
            AppStoreConnectCertificateLookupErrorCodes.ResponseMalformed => "重试",
            _ => FormatAppleAuthIssueAction(code)
        };

    private static string FormatAppStoreCertificateLookupSummary(AppStoreConnectCertificateLookupResult result) =>
        result.HasCertificates ? "已找到" : "无证书";

    private static string FormatAppStoreCertificateLookupDetail(AppStoreConnectCertificateLookupResult result)
    {
        if (!result.IsSuccess)
        {
            return FormatIssueDetail(result.Issues);
        }

        if (result.Certificates.Count == 0)
        {
            return "无证书";
        }

        return string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            result.Certificates.Select(FormatAppStoreCertificate));
    }

    private static string FormatAppStoreCertificate(AppStoreConnectCertificate certificate)
    {
        var displayName = !string.IsNullOrWhiteSpace(certificate.DisplayName)
            ? certificate.DisplayName
            : NonEmpty(certificate.Name, certificate.Id);
        var parts = new List<string>
        {
            $"名称: {displayName}",
            $"类型: {FormatCertificateType(certificate.CertificateType)}"
        };

        if (!string.IsNullOrWhiteSpace(certificate.Platform))
        {
            parts.Add($"平台: {FormatBundlePlatform(certificate.Platform)}");
        }

        if (!string.IsNullOrWhiteSpace(certificate.SerialNumber))
        {
            parts.Add($"序列号: {certificate.SerialNumber}");
        }

        if (certificate.Activated is not null)
        {
            parts.Add($"启用: {(certificate.Activated.Value ? "是" : "否")}");
        }

        if (certificate.ExpirationDate is not null)
        {
            parts.Add($"到期: {certificate.ExpirationDate.Value.ToLocalTime():yyyy-MM-dd}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatCertificateType(string type) =>
        type switch
        {
            "IOS_DEVELOPMENT" => "iOS 开发",
            "IOS_DISTRIBUTION" => "iOS 发布",
            "MAC_APP_DISTRIBUTION" => "Mac 发布",
            "MAC_INSTALLER_DISTRIBUTION" => "Mac 安装包",
            "MAC_APP_DEVELOPMENT" => "Mac 开发",
            "DEVELOPER_ID_KEXT" => "Kext",
            "DEVELOPER_ID_APPLICATION" => "Developer ID",
            "DEVELOPMENT" => "开发",
            "DISTRIBUTION" => "发布",
            "PASS_TYPE_ID" => "Pass",
            _ => type
        };

    private static string FormatAppStoreDeviceLookupIssueName(string code) =>
        code switch
        {
            AppStoreConnectDeviceLookupErrorCodes.ResponseMalformed => "响应",
            _ => FormatAppleAuthIssueName(code)
        };

    private static string FormatAppStoreDeviceLookupIssueAction(string code) =>
        code switch
        {
            AppStoreConnectDeviceLookupErrorCodes.ResponseMalformed => "重试",
            _ => FormatAppleAuthIssueAction(code)
        };

    private static string FormatAppStoreDeviceLookupSummary(AppStoreConnectDeviceLookupResult result) =>
        result.HasDevices ? "已找到" : "无设备";

    private static string FormatAppStoreDeviceLookupDetail(AppStoreConnectDeviceLookupResult result)
    {
        if (!result.IsSuccess)
        {
            return FormatIssueDetail(result.Issues);
        }

        if (result.Devices.Count == 0)
        {
            return "无设备";
        }

        return string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            result.Devices.Select(FormatAppStoreDevice));
    }

    private static string FormatAppStoreDevice(AppStoreConnectDevice device)
    {
        var parts = new List<string>
        {
            $"名称: {NonEmpty(device.Name, device.Id)}",
            $"UDID: {device.Udid}",
            $"平台: {FormatBundlePlatform(device.Platform)}",
            $"状态: {FormatDeviceStatus(device.Status)}"
        };

        if (!string.IsNullOrWhiteSpace(device.DeviceClass))
        {
            parts.Add($"类型: {FormatDeviceClass(device.DeviceClass)}");
        }

        if (!string.IsNullOrWhiteSpace(device.Model))
        {
            parts.Add($"型号: {device.Model}");
        }

        if (device.AddedDate is not null)
        {
            parts.Add($"添加: {device.AddedDate.Value.ToLocalTime():yyyy-MM-dd}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatDeviceClass(string deviceClass) =>
        deviceClass switch
        {
            "IPHONE" => "iPhone",
            "IPAD" => "iPad",
            "IPOD" => "iPod",
            "APPLE_WATCH" => "Watch",
            "APPLE_TV" => "TV",
            _ => deviceClass
        };

    private static string FormatDeviceStatus(string status) =>
        status switch
        {
            "ENABLED" => "启用",
            "DISABLED" => "停用",
            _ => status
        };

    private static string FormatAppStoreProfileLookupIssueName(string code) =>
        code switch
        {
            AppStoreConnectProfileLookupErrorCodes.ResponseMalformed => "响应",
            _ => FormatAppStoreBundleIdLookupIssueName(code)
        };

    private static string FormatAppStoreProfileLookupIssueAction(string code) =>
        code switch
        {
            AppStoreConnectProfileLookupErrorCodes.ResponseMalformed => "重试",
            _ => FormatAppStoreBundleIdLookupIssueAction(code)
        };

    private static string FormatAppStoreProfileLookupSummary(AppStoreConnectProfileLookupResult result)
    {
        if (!result.IsBundleIdFound)
        {
            return "Bundle 未找到";
        }

        return result.HasProfiles ? "已找到" : "无描述";
    }

    private static string FormatAppStoreProfileLookupDetail(AppStoreConnectProfileLookupResult result)
    {
        if (!result.IsSuccess)
        {
            return FormatIssueDetail(result.Issues);
        }

        if (result.BundleId is null)
        {
            return "Bundle 未找到";
        }

        if (result.Profiles.Count == 0)
        {
            return "无描述";
        }

        return string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            result.Profiles.Select(FormatAppStoreProfile));
    }

    private static string FormatAppStoreProfile(AppStoreConnectProfile profile)
    {
        var parts = new List<string>
        {
            $"名称: {profile.Name}",
            $"类型: {FormatProfileType(profile.ProfileType)}",
            $"状态: {FormatProfileState(profile.ProfileState)}",
            $"UUID: {profile.Uuid}",
            $"平台: {FormatBundlePlatform(profile.Platform)}"
        };

        if (profile.ExpirationDate is not null)
        {
            parts.Add($"到期: {profile.ExpirationDate.Value.ToLocalTime():yyyy-MM-dd}");
        }

        if (profile.CreatedDate is not null)
        {
            parts.Add($"创建: {profile.CreatedDate.Value.ToLocalTime():yyyy-MM-dd}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatProfileType(string type) =>
        type switch
        {
            "IOS_APP_DEVELOPMENT" => "开发",
            "IOS_APP_STORE" => "商店",
            "IOS_APP_ADHOC" => "Ad Hoc",
            "IOS_APP_INHOUSE" => "企业",
            "MAC_APP_DEVELOPMENT" => "Mac 开发",
            "MAC_APP_STORE" => "Mac 商店",
            "MAC_APP_DIRECT" => "Mac 直发",
            "TVOS_APP_DEVELOPMENT" => "tvOS 开发",
            "TVOS_APP_STORE" => "tvOS 商店",
            "TVOS_APP_ADHOC" => "tvOS Ad Hoc",
            "TVOS_APP_INHOUSE" => "tvOS 企业",
            _ => type
        };

    private static string FormatProfileState(string state) =>
        state switch
        {
            "ACTIVE" => "有效",
            "EXPIRED" => "过期",
            "INVALID" => "无效",
            _ => state
        };

    private static bool IsUploadEnvironmentIssue(string code) =>
        code is UploadErrorCodes.TransporterPathMissing
            or UploadErrorCodes.TransporterNotFound
            or UploadErrorCodes.PackagePathMissing
            or UploadErrorCodes.PackageNotFound
            or UploadErrorCodes.AssetDescriptionPathMissing
            or UploadErrorCodes.AssetDescriptionNotFound
            or UploadErrorCodes.ApiKeyCredentialMissing
            or UploadErrorCodes.JwtMissing
            or UploadErrorCodes.AppleAccountMissing
            or UploadErrorCodes.AppSpecificPasswordMissing;

    private static string FormatProfileStatus(ProvisioningProfileStatus status) =>
        status == ProvisioningProfileStatus.Active ? "有效" : "过期";

    private static string NonEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record PageDefinition(string Title, string Subtitle, FrameworkElement Content);

    private sealed record AssetListItem(
        LocalAssetType Type,
        string TypeText,
        string Name,
        string Path,
        string ModifiedText)
    {
        public static AssetListItem FromAsset(LocalAssetItem item) =>
            new(
                item.Type,
                FormatLocalAssetType(item.Type),
                item.Name,
                item.Path,
                item.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
    }

    private sealed record ExpirationReminderListItem(
        string StatusText,
        Brush StatusBrush,
        string Name,
        string Path,
        string ExpirationText)
    {
        public static ExpirationReminderListItem FromReminder(AssetExpirationReminder reminder) =>
            new(
                reminder.Status == AssetExpirationReminderStatus.Expired ? "过期" : "临期",
                reminder.Status == AssetExpirationReminderStatus.Expired
                    ? new SolidColorBrush(Color.FromRgb(180, 35, 24))
                    : new SolidColorBrush(Color.FromRgb(152, 106, 0)),
                $"{FormatExpirationReminderType(reminder.Type)} / {reminder.Name}",
                reminder.Path,
                reminder.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd"));
    }

    private sealed record HistoryListItem(
        string Operation,
        string StatusText,
        Brush StatusBrush,
        string Summary,
        string Detail,
        string TimeText,
        string CopyText)
    {
        public static HistoryListItem FromHistory(
            OperationHistoryItem item,
            string statusText,
            Brush statusBrush)
        {
            var localTime = item.OccurredAt.ToLocalTime();
            var detail = string.IsNullOrWhiteSpace(item.Detail) ? item.Summary : item.Detail;
            var copyText = $"{localTime:yyyy-MM-dd HH:mm:ss} {statusText} {item.Operation}{Environment.NewLine}{item.Summary}";

            if (!string.IsNullOrWhiteSpace(item.Detail))
            {
                copyText = $"{copyText}{Environment.NewLine}{item.Detail}";
            }

            return new HistoryListItem(
                item.Operation,
                statusText,
                statusBrush,
                item.Summary,
                detail,
                localTime.ToString("MM-dd HH:mm"),
                copyText);
        }
    }
}
