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
            ["Dashboard"] = new("工作台", "证书、描述文件、IPA 检查和上传流程总览", DashboardPage),
            ["Certificate"] = new("制作证书", "本地生成私钥、CSR，导入 CER 后导出 P12", CertificatePage),
            ["Profiles"] = new("描述文件", "导入和校验 mobileprovision 的关键信息", ProfilesPage),
            ["IpaCheck"] = new("IPA 检查", "读取 IPA 元数据并执行上传前校验", IpaCheckPage),
            ["Upload"] = new("IPA 上传", "通过隔离的上传适配器提交到 App Store Connect", UploadPage),
            ["Assets"] = new("资产库", "集中管理本地证书项目、描述文件、备份和历史记录", AssetsPage),
            ["Settings"] = new("设置", "配置 API Key、应用专用密码、Transporter 路径和隐私策略", SettingsPage),
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
