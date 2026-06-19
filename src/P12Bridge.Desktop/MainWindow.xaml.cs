using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using P12Bridge.Core;
using P12Bridge.Infrastructure;

namespace P12Bridge.Desktop;

public partial class MainWindow : Window
{
    private readonly ICertificateProjectService certificateProjectService;
    private readonly Dictionary<string, PageDefinition> _pages;
    private string? lastCertificateProjectDirectory;

    public MainWindow()
    {
        InitializeComponent();

        certificateProjectService = new CertificateProjectService(
            new LocalCertificateService(),
            new SystemClock());

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

    private void ClearCertificateResult()
    {
        lastCertificateProjectDirectory = null;
        OpenCertificateProjectButton.IsEnabled = false;
        CertificateProjectDirectoryTextBox.Text = string.Empty;
        CertificatePrivateKeyPathTextBox.Text = string.Empty;
        CertificateCsrPathTextBox.Text = string.Empty;
        CertificateStatusText.Text = string.Empty;
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
            _ => "生成失败"
        };

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
