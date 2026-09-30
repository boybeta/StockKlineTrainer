using System;
using System.Windows;
using System.Windows.Input;
using BaozhuKLineTrainer.Services;

namespace BaozhuKLineTrainer
{
    /// <summary>
    /// 段位升级庆祝弹窗（2026-09-30）：
    /// 结算时本金跨档（如 1 万 → 10 万），由 MainViewModel 在结算弹窗关闭后弹出。
    /// 奖杯 + 段位徽章大图 + "恭喜升级到 XX账户"；点任意处或按回车关闭。
    /// </summary>
    public partial class RankUpWindow : Window
    {
        /// <summary>无参构造：供 StartupUri / XAML 设计器实例化（默认预览黄金账户 10 万）</summary>
        public RankUpWindow() : this(1, 100_000) { }

        public RankUpWindow(int rankIdx, double firecrackers)
        {
            InitializeComponent();
            TxtRankName.Text = RankService.Ranks[rankIdx].Name;
            TxtAmount.Text = $"当前火星币：{firecrackers:N0}";

            // 段位徽章图：images\{段位名}.png；缺失/加载失败 → 星星图标兜底，弹窗不空
            try
            {
                var bmp = RankService.LoadRankIcon(rankIdx);
                if (bmp != null)
                {
                    RankImage.Source = bmp;
                    RankImage.Visibility = Visibility.Visible;
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DIAG] 升级弹窗徽章加载失败: {ex.Message}");
            }
            FallbackIcon.Visibility = Visibility.Visible;
        }

        private void Continue_Click(object sender, RoutedEventArgs e) => Close();

        // 点击卡片任意空白处关闭（庆祝弹窗，不需要复杂交互）
        private void Border_MouseDown(object sender, MouseButtonEventArgs e) => Close();
    }
}