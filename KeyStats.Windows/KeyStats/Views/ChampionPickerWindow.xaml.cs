using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeyStats.Helpers;
using KeyStats.Services;

namespace KeyStats.Views;

/// <summary>
/// 英雄选择器：搜索 + 头像网格，点选即返回。
/// 数据源走 LcuClient（LCU 在线 → ddragon 官方 CDN → 磁盘缓存），
/// 打开窗口时后台把缺失的官方头像批量补齐并就地刷新。
/// </summary>
public partial class ChampionPickerWindow : Window
{
    private List<ChampionCatalogEntry> _all = new();
    private readonly Dictionary<int, Image> _tileImageById = new();
    private readonly Dictionary<int, TextBlock> _tileFallbackById = new();

    /// <summary>点选的英雄；未选为 null。</summary>
    internal ChampionCatalogEntry? SelectedChampion { get; private set; }

    /// <summary>常用外号 → ddragon 英雄代码（搜索兜底；本名/称号/英文之外的第 4 条路）。</summary>
    private static readonly Dictionary<string, string[]> NicknamesByDdKey =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MissFortune"] = new[] { "女枪", "赏金" },
            ["Graves"] = new[] { "男枪" },
            ["Malphite"] = new[] { "石头人" },
            ["Kalista"] = new[] { "滑板鞋" },
            ["ChoGath"] = new[] { "大虫子" },
            ["KhaZix"] = new[] { "螳螂" },
            ["Rengar"] = new[] { "狮子狗" },
            ["Tryndamere"] = new[] { "蛮王", "蛮子" },
            ["MonkeyKing"] = new[] { "猴子" },
            ["Brand"] = new[] { "火男" },
            ["Sivir"] = new[] { "轮子妈" },
            ["Fizz"] = new[] { "小鱼人", "鱼人" },
            ["Hecarim"] = new[] { "人马" },
            ["Sejuani"] = new[] { "猪妹" },
            ["Anivia"] = new[] { "冰鸟", "凤凰" },
            ["Leona"] = new[] { "日女", "女坦" },
            ["RekSai"] = new[] { "挖掘机" },
            ["Nidalee"] = new[] { "豹女" },
            ["Rammus"] = new[] { "龙龟", "乌龟" },
            ["KogMaw"] = new[] { "大嘴" },
            ["Veigar"] = new[] { "小法" },
            ["Vladimir"] = new[] { "吸血鬼" },
            ["Mordekaiser"] = new[] { "铁男" },
            ["LeBlanc"] = new[] { "妖姬" },
            ["Irelia"] = new[] { "刀妹" },
            ["Camille"] = new[] { "青钢影" },
            ["Gangplank"] = new[] { "船长" },
            ["Caitlyn"] = new[] { "女警" },
            ["Swain"] = new[] { "乌鸦" },
            ["Maokai"] = new[] { "大树" },
            ["Warwick"] = new[] { "狼人" },
            ["Skarner"] = new[] { "蝎子" },
            ["Shyvana"] = new[] { "龙女" },
            ["Elise"] = new[] { "蜘蛛" },
            ["Soraka"] = new[] { "奶妈" },
            ["Yasuo"] = new[] { "快乐风男" },
            ["Garen"] = new[] { "德玛" },
            ["Darius"] = new[] { "诺手" },
            ["Tristana"] = new[] { "小炮" },
            ["Jax"] = new[] { "武器" },
            ["Ziggs"] = new[] { "炸弹人" },
            ["Heimerdinger"] = new[] { "大头" },
            ["Twitch"] = new[] { "老鼠" },
            ["Shaco"] = new[] { "小丑" },
            ["Urgot"] = new[] { "螃蟹" },
            ["Alistar"] = new[] { "牛头" },
            ["Blitzcrank"] = new[] { "机器人" },
            ["Singed"] = new[] { "炼金" },
            ["DrMundo"] = new[] { "蒙多" },
            ["Sona"] = new[] { "琴女" },
        };

    private static IReadOnlyList<string> GetNicknames(ChampionCatalogEntry champion)
    {
        return champion.DdKey != null && NicknamesByDdKey.TryGetValue(champion.DdKey, out var aliases)
            ? aliases
            : Array.Empty<string>();
    }

    public ChampionPickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SearchBox.TextChanged += (s, e) => RebuildTiles(SearchBox.Text.Trim());
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowBackdropHelper.Apply(this, NativeInterop.DwmSystemBackdropType.TransientWindow);

        try
        {
            _all = await GameSessionService.Instance.ChampionData.GetChampionCatalogAsync();
        }
        catch
        {
            _all = new List<ChampionCatalogEntry>();
        }

        if (_all.Count == 0)
        {
            StatusText.Text = "拿不到英雄列表（客户端未运行且官网不可达），稍后重试";
            return;
        }

        _all = _all.OrderBy(c => c.Id).ToList();
        RebuildTiles(SearchBox.Text.Trim());
        StatusText.Text = "共 " + _all.Count + " 个英雄";

        // 缺失头像后台补齐，每完成一个就地刷新
        var missing = _all.Where(c => !LcuClient.IsIconCached(c.Id)).Select(c => c.Id).ToList();
        if (missing.Count == 0)
        {
            StatusText.Text = "共 " + _all.Count + " 个英雄（头像已全部就绪）";
            return;
        }

        var done = 0;
        await Task.Run(async () =>
        {
            await GameSessionService.Instance.ChampionData.EnsureChampionIconsAsync(missing, id =>
            {
                done++;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusText.Text = "头像下载中 " + done + "/" + missing.Count;
                    UpdateTileImage(id);
                }));
            });
        });

        Dispatcher.BeginInvoke(new Action(() =>
            StatusText.Text = "共 " + _all.Count + " 个英雄（头像已全部就绪）"));
    }

    private void RebuildTiles(string filter)
    {
        TilesPanel.Children.Clear();
        _tileImageById.Clear();
        _tileFallbackById.Clear();

        foreach (var champion in _all)
        {
            if (filter.Length > 0 && !MatchSearch(champion, filter))
            {
                continue;
            }

            TilesPanel.Children.Add(BuildTile(champion));
        }
    }

    private static bool MatchSearch(ChampionCatalogEntry champion, string filter)
    {
        // 本名（亚索）/ 称号（疾风剑豪）/ 英文代码（Yasuo）/ 常用外号（快乐风男）都能搜到
        if (champion.ProperName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            champion.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (champion.DdKey?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        return GetNicknames(champion).Any(alias =>
            alias.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private Border BuildTile(ChampionCatalogEntry champion)
    {
        var fallback = new TextBlock
        {
            Text = "?",
            FontSize = 18,
            Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var image = new Image
        {
            Width = 42,
            Height = 42,
            Stretch = Stretch.UniformToFill,
            Visibility = Visibility.Collapsed,
        };

        var portrait = new Grid
        {
            Width = 44,
            Height = 44,
            ClipToBounds = true,
        };
        portrait.Children.Add(fallback);
        portrait.Children.Add(image);

        var nameText = new TextBlock
        {
            Text = champion.DisplayName,
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 58,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.Black,
        };

        var titleText = new TextBlock
        {
            Text = string.Equals(champion.Name, champion.ProperName, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : champion.Name,
            FontSize = 9,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 58,
            Margin = new Thickness(0, 0, 0, 2),
            Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
        };

        var stack = new StackPanel();
        stack.Children.Add(portrait);
        stack.Children.Add(nameText);
        if (titleText.Text.Length > 0)
        {
            stack.Children.Add(titleText);
        }

        var tile = new Border
        {
            Width = 66,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0, 6, 0, 4),
            Cursor = Cursors.Hand,
            Background = TryFindResource("DividerBrush") as Brush ?? Brushes.LightGray,
            Child = stack,
            ToolTip = champion.ProperName + " · " + champion.Name,
        };

        var captured = champion;
        tile.MouseLeftButtonDown += (s, e) =>
        {
            SelectedChampion = captured;
            DialogResult = true;
        };

        if (LcuClient.IsIconCached(champion.Id))
        {
            SetTileImageSource(image, champion.Id);
        }

        _tileImageById[champion.Id] = image;
        _tileFallbackById[champion.Id] = fallback;
        return tile;
    }

    private void UpdateTileImage(int championId)
    {
        if (_tileImageById.TryGetValue(championId, out var image) &&
            LcuClient.IsIconCached(championId))
        {
            SetTileImageSource(image, championId);
        }
    }

    private static void SetTileImageSource(Image image, int championId)
    {
        var source = LoadImage(GameSessionService.GetChampionIconPath(championId));
        if (source != null)
        {
            image.Source = source;
            image.Visibility = Visibility.Visible;
        }
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
