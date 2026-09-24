using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using BaozhuKLineTrainer.Services;

namespace BaozhuKLineTrainer
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        /// <summary>
        /// 点击【盲盘训练】文字跳转双盲训练窗口
        /// </summary>
        private void OpenDoubleBlindTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow();
            trainWindow.Owner = this;
            trainWindow.ShowDialog();
        }

        /// <summary>首页加载：读取训练记录，刷新统计与爆竹数量曲线</summary>
        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            RefreshHomeData();   // 首次加载
        }

        /// <summary>重新读取训练记录，刷新统计与爆竹曲线（训练结算返回首页时调用）</summary>
        public void RefreshHomeData()
        {
            try
            {
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stockdata", "cy_stock.db");
                var db = new DatabaseService(dbPath);
                db.EnsureTrainingRecordTable();   // 幂等建表

                var (gameCount, latest) = db.GetHomeSummary();
                TxtTrainCount.Text = gameCount.ToString();
                TxtFirecracker.Text = $"爆竹: {latest:N0}";

                // ===== ① 插入点：训练数据 12 格（紧跟 TxtFirecracker 这行之后）=====
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

                var curve = db.GetFirecrackerCurve();

                // ===== ② 插入点：最大回撤（紧跟 var curve 这行之后）=====
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

                if (curve.Count >= 2)
                {
                    CurvePlot.Visibility = Visibility.Visible;   // 刷新时若之前被隐藏需恢复
                    CurvePlot.Plot.Clear();
                    double[] xs = Enumerable.Range(0, curve.Count).Select(i => (double)i).ToArray();
                    double[] ys = curve.Select(c => c.firecrackers).ToArray();
                    var sp = CurvePlot.Plot.Add.Scatter(xs, ys);
                    sp.LineWidth = 2;
                    sp.MarkerSize = 4;
                    sp.Color = ScottPlot.Color.FromHex("#E84070");

                    // X 轴显示结算日期（隔几个取一个，避免拥挤）
                    int step = Math.Max(1, curve.Count / 6);
                    var positions = new System.Collections.Generic.List<double>();
                    var labels = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < curve.Count; i += step)
                    {
                        positions.Add(i);
                        labels.Add(curve[i].time.Substring(5, 5));   // "MM-dd"
                    }
                    if (positions.Last() != curve.Count - 1)
                    {
                        positions.Add(curve.Count - 1);
                        labels.Add(curve[^1].time.Substring(5, 5));
                    }
                    CurvePlot.Plot.Axes.Bottom.TickGenerator =
                        new ScottPlot.TickGenerators.NumericManual(positions.ToArray(), labels.ToArray());

                    CurvePlot.Plot.Axes.AutoScale();
                    CurvePlot.Refresh();
                    TxtCurveEmpty.Visibility = Visibility.Collapsed;
                }
                else
                {
                    CurvePlot.Visibility = Visibility.Collapsed;   // 不足 2 条，显示"暂无"占位
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DIAG] 首页加载训练记录失败: {ex.Message}");
            }
        }
    }
}