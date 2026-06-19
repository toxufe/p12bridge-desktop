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
    private readonly Dictionary<string, PageDefinition> _pages;
    private string? lastCertificateProjectDirectory;

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

        _pages = new Dictionary<string, PageDefinition>
        {
            ["Dashboard"] = new("工作台", "证书到上传总览", DashboardPage),
            ["Certificate"] = new("制作证书", "私钥、CSR、P12", CertificatePage),
            ["Profiles"] = new("描述文件", "Bundle、Team、有效期", ProfilesPage),
            ["IpaCheck"] = new("IPA 检查", "版本、签名、阻断项", IpaCheckPage),
            ["Upload"] = new("IPA 上传", "进度、日志、结果", UploadPage),
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
        }

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
        }

        if (!result.IsSuccess)
        {
            SetIpaStatus(FormatIpaIssues(result.Issues), isSuccess: false);
            return;
        }

        SetIpaStatus("检查通过", isSuccess: true);
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
    }

    private void ClearIpaResult()
    {
        IpaBundleIdTextBox.Text = string.Empty;
        IpaVersionTextBox.Text = string.Empty;
        IpaBuildTextBox.Text = string.Empty;
        IpaSizeTextBox.Text = string.Empty;
        IpaCodeSignTextBox.Text = string.Empty;
        IpaEmbeddedProfileTextBox.Text = string.Empty;
        IpaAppBundlePathTextBox.Text = string.Empty;
        IpaImportedPathTextBox.Text = string.Empty;
        IpaStatusText.Text = string.Empty;
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

    private static string FormatProfileStatus(ProvisioningProfileStatus status) =>
        status == ProvisioningProfileStatus.Active ? "有效" : "过期";

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
