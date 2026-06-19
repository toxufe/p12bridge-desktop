using System.Windows;
using System.Windows.Controls;

namespace P12Bridge.Desktop;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, PageDefinition> _pages;

    public MainWindow()
    {
        InitializeComponent();

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
