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
    private readonly Dictionary<string, PageDefinition> _pages;
    private string? lastCertificateProjectDirectory;
    private ProvisioningProfile? lastImportedProfile;
    private string lastImportedProfilePath = string.Empty;
    private IpaMetadata? lastIpaMetadata;
    private string lastIpaImportedPath = string.Empty;
    private UploadEnvironmentValidationResult? lastUploadEnvironmentValidation;

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

        _pages = new Dictionary<string, PageDefinition>
        {
            ["Dashboard"] = new("工作台", "证书到上传总览", DashboardPage),
            ["Certificate"] = new("制作证书", "私钥、CSR、P12", CertificatePage),
            ["Profiles"] = new("描述文件", "Bundle、Team、有效期", ProfilesPage),
            ["IpaCheck"] = new("IPA 检查", "版本、签名、阻断项", IpaCheckPage),
            ["Upload"] = new("IPA 上传", "上传前检查", UploadPage),
            ["Assets"] = new("资产库", "项目、备份、历史", AssetsPage),
            ["Settings"] = new("设置", "凭据、路径、隐私", SettingsPage),
        };

        CertificateBaseDirectoryTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "P12Bridge",
            "Certificates");
        ProfileBaseDirectoryTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "P12Bridge",
            "Profiles");
        IpaBaseDirectoryTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "P12Bridge",
            "IPAs");

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

        if (pageKey == "Settings")
        {
            RefreshUploadSettingsInputs();
            RefreshUploadEnvironmentStatus();
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
            return;
        }

        lastCertificateProjectDirectory = result.Artifacts.ProjectDirectory;
        CertificateProjectDirectoryTextBox.Text = result.Artifacts.ProjectDirectory;
        CertificatePrivateKeyPathTextBox.Text = result.Artifacts.PrivateKeyPath;
        CertificateCsrPathTextBox.Text = result.Artifacts.CertificateSigningRequestPath;
        OpenCertificateProjectButton.IsEnabled = true;
        SetCertificateStatus("已生成", isSuccess: true);
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
            return;
        }

        CertificateCerPathTextBox.Text = result.CertificatePath;
        CertificateP12PathTextBox.Text = result.P12Path;
        CertificateP12PasswordBox.Clear();
        SetCertificateStatus("P12 已导出", isSuccess: true);
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
            return;
        }

        SetProfileStatus("已导入", isSuccess: true);
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
            return;
        }

        SetIpaStatus("检查通过", isSuccess: true);
    }

    private void OnCheckUploadReadinessClick(object sender, RoutedEventArgs e)
    {
        EvaluateUploadReadiness();
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

    private void OnUploadCredentialModeChanged(object sender, SelectionChangedEventArgs e)
    {
        SetCredentialPanelsVisibility();
        lastUploadEnvironmentValidation = null;
        RefreshUploadEnvironmentStatus();
    }

    private void OnUploadEnvironmentInputChanged(object sender, RoutedEventArgs e)
    {
        lastUploadEnvironmentValidation = null;
        RefreshUploadEnvironmentStatus();
    }

    private void OnValidateUploadEnvironmentClick(object sender, RoutedEventArgs e)
    {
        ValidateUploadEnvironment();
    }

    private void EvaluateUploadReadiness()
    {
        RefreshUploadInputs();

        var result = uploadReadinessEvaluator.Evaluate(new UploadReadinessRequest(
            UploadTarget.AppStore,
            lastIpaMetadata,
            lastImportedProfile));

        ShowUploadReadiness(result);
    }

    private void ValidateUploadEnvironment()
    {
        RefreshUploadSettingsInputs();

        var result = uploadService.ValidateEnvironment(BuildUploadRequest());
        lastUploadEnvironmentValidation = result;
        ShowUploadEnvironment(result);
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

    private UploadRequest BuildUploadRequest()
    {
        var mode = ReadUploadCredentialMode();

        return new UploadRequest(
            TransporterPathTextBox.Text,
            lastIpaImportedPath,
            mode,
            ApiKeyId: mode == UploadCredentialMode.ApiKey ? OptionalText(UploadApiKeyIdTextBox.Text) : null,
            IssuerId: mode == UploadCredentialMode.ApiKey ? OptionalText(UploadIssuerIdTextBox.Text) : null,
            Jwt: mode == UploadCredentialMode.Jwt ? OptionalText(UploadJwtPasswordBox.Password) : null,
            Timeout: TimeSpan.FromMinutes(30));
    }

    private UploadCredentialMode ReadUploadCredentialMode() =>
        UploadCredentialModeComboBox.SelectedIndex == 1
            ? UploadCredentialMode.Jwt
            : UploadCredentialMode.ApiKey;

    private void SetCredentialPanelsVisibility()
    {
        if (ApiKeyCredentialPanel is null || JwtCredentialPanel is null || UploadCredentialModeComboBox is null)
        {
            return;
        }

        var isJwt = ReadUploadCredentialMode() == UploadCredentialMode.Jwt;
        ApiKeyCredentialPanel.Visibility = isJwt ? Visibility.Collapsed : Visibility.Visible;
        JwtCredentialPanel.Visibility = isJwt ? Visibility.Visible : Visibility.Collapsed;
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
            UploadErrorCodes.ApiKeyCredentialMissing => "API Key",
            UploadErrorCodes.JwtMissing => "JWT",
            UploadErrorCodes.ProcessStartFailed => "进程",
            UploadErrorCodes.ProcessTimedOut => "超时",
            UploadErrorCodes.ProcessCancelled => "取消",
            UploadErrorCodes.ProcessExitFailed => "验证",
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
            UploadErrorCodes.ApiKeyCredentialMissing => "填写凭据",
            UploadErrorCodes.JwtMissing => "填写 JWT",
            UploadErrorCodes.ProcessStartFailed => "检查权限",
            UploadErrorCodes.ProcessTimedOut => "重试",
            UploadErrorCodes.ProcessCancelled => "重试",
            UploadErrorCodes.ProcessExitFailed => "看日志",
            UploadErrorCodes.UnexpectedProcessResult => "重试",
            _ => "处理"
        };

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
}
