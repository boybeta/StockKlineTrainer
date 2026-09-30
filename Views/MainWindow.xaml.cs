using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Media.Imaging;
using BaozhuKLineTrainer.Services;


namespace BaozhuKLineTrainer
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        // ===== 导航选中态配色（与 XAML 默认高亮一致） =====
        private static readonly SolidColorBrush NavSelectedBg = new(Color.FromRgb(0xF7, 0xD8, 0xEE));
        private static readonly SolidColorBrush NavSelectedFg = new(Color.FromRgb(0xD8, 0x48, 0xA0));
        private static readonly SolidColorBrush NavNormalFg = new(Color.FromRgb(0x66, 0x66, 0x66));
        private static readonly SolidColorBrush NavNormalDot = new(Color.FromRgb(0x99, 0x99, 0x99));

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            // 主题切换：火星币曲线配色跟随（重刷首页数据时会按当前主题重设颜色）
            ThemeService.ThemeChanged += _ => RefreshHomeData();
        }

        /// <summary>导航菜单点击：切换选中高亮 + 分发功能</summary>
        private void NavItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border nav || nav.Tag is not string tag) return;
            string key = tag.StartsWith("nav:") ? tag.Substring(4) : tag;

            // 选中高亮：所有 nav: 项里只点亮当前（各分组独立，互不排除）
            foreach (var b in FindVisualChildren<Border>(this).Where(b => b.Tag is string s && s.StartsWith("nav:")))
                SetNavSelected(b, b == nav);

            switch (key)
            {
                case "SimTrain":   // 模拟训练 → 打开双盲训练配置窗
                    OpenDoubleBlindTrain_Click(sender, e);
                    break;
                case "Setting":    // 设置 → 打开设置窗（主题切换在里面）
                    new SettingsWindow { Owner = this }.ShowDialog();
                    break;
                default:           // 其余功能暂未实现
                    MessageBox.Show("该功能开发中，敬请期待！", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
        }

        /// <summary>选中态刷色：背景 + 文字 + 圆点</summary>
        private static void SetNavSelected(Border nav, bool selected)
        {
            nav.Background = selected ? NavSelectedBg : Brushes.Transparent;
            foreach (var tb in FindVisualChildren<TextBlock>(nav))
            {
                // 跳过组头/其他非菜单文字不用考虑——子项 Border 内只有一项 TextBlock
                tb.Foreground = selected ? NavSelectedFg : NavNormalFg;
            }
            foreach (var dot in FindVisualChildren<Ellipse>(nav))
                dot.Fill = selected ? NavSelectedFg : NavNormalDot;
        }

        /// <summary>视觉树遍历（找某类子元素）</summary>
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) yield return t;
                foreach (var nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }

        /// <summary>加载用户头像：E:\baozhu\userdata\avatar.png，不存在则保留默认图标</summary>
        private void LoadAvatar()
        {
            try
            {
                string path = Path.Combine(LoginWindow.UserDataDir, "avatar.png");
                if (!File.Exists(path)) return;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // 立即加载，避免文件被锁
                bmp.EndInit();
                AvatarImage.ImageSource = bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DIAG] 头像加载失败: {ex.Message}");
            }
        }

        /// <summary>段位徽章：程序目录 images\{段位名}.png（白银小账户.png / 黄金账户.png …），不存在则留空</summary>
        private void LoadRankIcon(int rankIdx)
        {
            try
            {
                var bmp = RankService.LoadRankIcon(rankIdx);
                if (bmp == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[DIAG] 段位徽章不存在: {RankService.GetRankIconPath(rankIdx)}");
                    RankIcon.Visibility = Visibility.Collapsed;
                    return;
                }
                RankIcon.Source = bmp;
                RankIcon.Visibility = Visibility.Visible;
                System.Diagnostics.Debug.WriteLine($"[DIAG] 段位徽章已加载: {RankService.GetRankIconPath(rankIdx)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DIAG] 段位徽章加载失败: {ex.Message}");
                RankIcon.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>点击【盲盘训练】跳转双盲训练窗口</summary>
        private void OpenDoubleBlindTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow();
            trainWindow.Owner = this;
            trainWindow.ShowDialog();
        }

        /// <summary>点击【涨停训练】→ 打开配置窗（涨停模式）</summary>
        private void OpenLimitUpTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow("LimitUp");
            trainWindow.Owner = this;
            trainWindow.ShowDialog();
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            LoadAvatar();        // 加载头像
            RefreshHomeData();   // 首次加载（段位徽章在内部按峰值加载）
        }

        /// <summary>点击【指数训练】→ 打开配置窗（指数模式）</summary>
        private void OpenIndexTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow("Index");
            trainWindow.Owner = this;
            trainWindow.ShowDialog();
        }

        /// <summary>点击【期货训练】→ 打开配置窗（期货模式）</summary>
        private void OpenFutureTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow("Future");
            trainWindow.Owner = this;
            trainWindow.ShowDialog();
        }

        /// <summary>点击【港股训练】→ 打开配置窗（港股模式）</summary>
        private void OpenHKTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow("HK");
            trainWindow.Owner = this;
            trainWindow.ShowDialog();
        }

        /// <summary>点击【美股训练】→ 打开配置窗（美股模式）</summary>
        private void OpenUSTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow("US");
            trainWindow.Owner = this;
            trainWindow.ShowDialog();
        }

        /// <summary>点击【可转债训练】→ 打开配置窗（可转债模式）</summary>
        private void OpenBondTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow("Bond");
            trainWindow.Owner = this;
            trainWindow.ShowDialog();
        }

        /// <summary>点击【ETF 训练】→ 打开配置窗（ETF 模式）</summary>
        private void OpenETFTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow("ETF");
            trainWindow.Owner = this;
            trainWindow.ShowDialog();
        }

        /// <summary>训练记录标签：config_json 里存类型标记 → 中文名；老记录为空兜底"双盲训练"</summary>
        private static string TrainTypeName(string trainType) => trainType switch
        {
            "LimitUp" => "涨停训练",
            "Index" => "指数训练",
            "Future" => "期货训练",
            "HK" => "港股训练",
            "US" => "美股训练",
            "Bond" => "可转债训练",
            "ETF" => "ETF 训练",
            "" => "双盲训练",   // 老记录（修复前结算的）没有类型标记
            _ => "盲盘训练",   // "Stock"
        };

        /// <summary>重新读取训练记录，刷新统计与火星币曲线（训练结算返回首页时调用）</summary>
        public void RefreshHomeData()
        {
            try
            {
                // 2026-09-30：数据库路径改走 AppPaths（与 MainViewModel 同一口径）
                string dbPath = AppPaths.GetDbPath();
                var db = new DatabaseService(dbPath);
                db.EnsureTrainingRecordTable();   // 幂等建表

                var (gameCount, latest) = db.GetHomeSummary();
                System.Diagnostics.Debug.WriteLine($"[DIAG] 统计读取：场次={gameCount} 最新值={latest:F0}");
                TxtTrainCount.Text = gameCount.ToString();
                TxtFirecracker.Text = $"火星币: {latest:N0}";

                // 训练数据 12 格
                var st = db.GetTrainingStats();
                TxtStatGames.Text = $"训练场次：{st.gameCount}";
                TxtStatWinRate.Text = $"训练胜率：{st.gameWinRate:F1}%";
                TxtStatBeatRate.Text = $"跑赢区间率：{st.beatIntervalRate:F1}%";
                TxtStatPLRatio.Text = $"训练盈亏比：{(st.profitLossRatio > 0 ? st.profitLossRatio.ToString("F2") : "--")}";
                TxtStatHoldTime.Text = $"持仓时间：{st.avgHoldDays:F1}";
                TxtStatHoldRate.Text = $"持仓率：{st.holdRate:F1}%";
                TxtStatHeavyRate.Text = $"重仓率：{st.heavyRate:F1}%";
                TxtStatElapsed.Text = $"训练耗时：{st.avgElapsedSec:F0}s";
                TxtStatOpens.Text = $"开仓次数：{st.totalOpens}";
                TxtStatOpenWinRate.Text = $"开仓胜率：{st.openWinRate:F1}%";
                TxtStatMaxProfit.Text = $"最大盈利：{st.maxProfitPct:F1}%";
                TxtTrainSummary.Text = $"训练结果：{st.winGames}胜 {st.gameCount - st.winGames}负";
                TxtCurveGames.Text = $"共 {st.gameCount} 场训练";
                // ★ 模拟训练卡片底部 4 项：持仓时间(平均)/总胜率/暴富次数/破产次数
                TxtHoldDays.Text = $"{st.avgHoldDays:F1}天";
                TxtTotalWinRate.Text = $"{st.gameWinRate:F1}%";
                var (brokeCount, boomCount) = db.GetBoomBrokeCounts();
                TxtBoomCount.Text = boomCount.ToString();
                TxtBrokeCount.Text = brokeCount.ToString();

                var records = db.GetRecentRecords(50);
                RecordList.ItemsSource = records.Select(r => new
                {
                    StockName = r.stockName,
                    StockCode = r.stockCode,
                    TrainTypeText = TrainTypeName(r.trainType),
                    TimeText = r.time,
                    HoldRateText = $"{r.holdRate:F2}%",
                    HeavyRateText = $"{r.heavyRate:F2}%",
                    OpenWinRateText = $"{r.openWinRate:F2}%",
                    IntervalText = $"{r.intervalPct:F2}%",
                    ProfitText = $"{r.profitPct:F2}%",
                    ProfitBrush = r.profitPct > 0
                                ? new SolidColorBrush(Color.FromRgb(0xE0, 0x45, 0x55))
                                : r.profitPct < 0
                                ? new SolidColorBrush(Color.FromRgb(0x1F, 0x9D, 0x72))
                                : Brushes.Gray,
                    // ↓↓↓ 新增两行，注意 ProfitBrush 那行末尾要补一个逗号 ↓↓↓
                    ElapsedText = r.elapsedSec >= 60 ? $"{r.elapsedSec / 60}分{r.elapsedSec % 60}秒" : $"{r.elapsedSec}秒",
                    DataRangeText = $"{r.startDate} ~ {r.endDate}"
                }).ToList();

                TxtRecordEmpty.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                var curve = db.GetFirecrackerCurve();

                // 最大回撤
                double maxDD = 0, peak = 0;
                foreach (var (_, v) in curve)
                {
                    if (v > peak) peak = v;
                    if (peak > 0)
                    {
                        double dd = (peak - v) / peak * 100;
                        if (dd > maxDD) maxDD = dd;
                    }
                }
                TxtStatMaxDD.Text = $"最大回撤：{maxDD:F1}%";
                // ===== 段位刷新：按曲线历史峰值定段，只升不降 =====
                int rankIdx = RankService.CalcRankIndex(latest);   // ★ 按历史峰值定段：破产/暴富重置后段位不回落
                // ★ 段位星标：点亮前 rankIdx+1 颗（黄金=2 颗…传奇=6 颗全亮）
                var starLit = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x70));    // 金色
                var starDim = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x99));    // 暗灰
                for (int i = 1; i <= 6; i++)
                {
                    if (FindName($"Star{i}") is MaterialDesignThemes.Wpf.PackIcon star)
                        star.Foreground = i <= rankIdx + 1 ? starLit : starDim;
                }
                TxtRankName.Text = RankService.Ranks[rankIdx].Name;
                LoadRankIcon(rankIdx);

                if (curve.Count >= 2)
                {
                    CurvePlot.Visibility = Visibility.Visible;
                    CurvePlot.Plot.Clear();
                    // 主题：火星币曲线图配色随当前主题
                    CurvePlot.Plot.FigureBackground.Color = ChartTheme.FigureBg(ThemeService.Current);
                    CurvePlot.Plot.DataBackground.Color = ChartTheme.DataBg(ThemeService.Current);
                    CurvePlot.Plot.Axes.Color(ChartTheme.Axis(ThemeService.Current));
                    CurvePlot.Plot.Grid.MajorLineColor = ChartTheme.Grid(ThemeService.Current);
                    double[] xs = Enumerable.Range(0, curve.Count).Select(i => (double)i).ToArray();
                    double[] ys = curve.Select(c => c.firecrackers).ToArray();
                    var sp = CurvePlot.Plot.Add.Scatter(xs, ys);
                    sp.LineWidth = 2;
                    sp.MarkerSize = 4;
                    sp.Color = ScottPlot.Color.FromHex("#E84070");

                    int step = Math.Max(1, curve.Count / 6);
                    var positions = new List<double>();
                    var labels = new List<string>();
                    for (int i = 0; i < curve.Count; i += step)
                    {
                        positions.Add(i);
                        labels.Add(curve[i].time.Substring(5, 5));   // "MM-dd"
                    }
                    if (positions.Last() != curve.Count - 1)
                    {
                        // 与上一个刻度太近时不追加，直接替换，避免末尾两个标签糊在一起（"09-269-26"）
                        if (curve.Count - 1 - positions.Last() >= step * 0.6)
                        {
                            positions.Add(curve.Count - 1);
                            labels.Add(curve[^1].time.Substring(5, 5));
                        }
                        else
                        {
                            positions[positions.Count - 1] = curve.Count - 1;
                            labels[labels.Count - 1] = curve[^1].time.Substring(5, 5);
                        }
                    }
                    CurvePlot.Plot.Axes.Bottom.TickGenerator =
                        new ScottPlot.TickGenerators.NumericManual(positions.ToArray(), labels.ToArray());

                    CurvePlot.Plot.Axes.AutoScale();
                    CurvePlot.Refresh();
                    TxtCurveEmpty.Visibility = Visibility.Collapsed;
                }
                else
                {
                    CurvePlot.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DIAG] 首页加载训练记录失败: {ex.Message}");
            }
        }

        /// <summary>自绘标题栏：按住拖动窗口</summary>
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();

        /// <summary>重置本金：清空全部训练记录，火星币回到 10000</summary>
        private void BtnResetFirecrackers_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要重置吗？\n\n将清空所有训练记录，火星币回到 10,000，此操作不可恢复！",
                "重置本金", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                // 2026-09-30：数据库路径改走 AppPaths（与 MainViewModel 同一口径）
                string dbPath = AppPaths.GetDbPath();
                var db = new DatabaseService(dbPath);
                db.ResetTrainingRecords();
                RefreshHomeData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("重置失败：" + ex.Message);
            }
        }
    }
}