using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace BaozhuKLineTrainer
{
    public partial class DoubleBlindTrainWindow : Window
    {
        // ★ 训练目标：Stock=个股盲盘 / Index=指数训练（首页按钮传入）
        private readonly string _trainTarget = "Stock";

        public DoubleBlindTrainWindow()
        {
            InitializeComponent();
        }

        /// <summary>指数训练入口："Index" 时禁用个股专属配置</summary>
        public DoubleBlindTrainWindow(string trainTarget) : this()
        {
            _trainTarget = trainTarget;
            if (trainTarget == "Index")
            {
                Title = "指数训练";
                // 指数无市场/时间段/ST 概念：禁用相关控件（保留布局不动）
                RdoMarketAll.IsEnabled = RdoMarketMain.IsEnabled = RdoMarketCyb.IsEnabled = RdoMarketKcb.IsEnabled = false;
                RdoPeriodAll.IsEnabled = RdoPeriod5Y.IsEnabled = RdoPeriod10Y.IsEnabled = RdoPeriodBefore10Y.IsEnabled = false;
                TglRemoveST.IsEnabled = false;
            }
            if (trainTarget == "Future")
            {
                Title = "期货训练";
                RdoMarketAll.IsEnabled = RdoMarketMain.IsEnabled = RdoMarketCyb.IsEnabled = RdoMarketKcb.IsEnabled = false;
                RdoPeriodAll.IsEnabled = RdoPeriod5Y.IsEnabled = RdoPeriod10Y.IsEnabled = RdoPeriodBefore10Y.IsEnabled = false;
                TglRemoveST.IsEnabled = false;
            }
            if (trainTarget == "HK")
            {
                Title = "港股训练";
                RdoMarketAll.IsEnabled = RdoMarketMain.IsEnabled = RdoMarketCyb.IsEnabled = RdoMarketKcb.IsEnabled = false;
                RdoPeriodAll.IsEnabled = RdoPeriod5Y.IsEnabled = RdoPeriod10Y.IsEnabled = RdoPeriodBefore10Y.IsEnabled = false;
                TglRemoveST.IsEnabled = false;
            }
            if (trainTarget == "US")
            {
                Title = "美股训练";
                RdoMarketAll.IsEnabled = RdoMarketMain.IsEnabled = RdoMarketCyb.IsEnabled = RdoMarketKcb.IsEnabled = false;
                RdoPeriodAll.IsEnabled = RdoPeriod5Y.IsEnabled = RdoPeriod10Y.IsEnabled = RdoPeriodBefore10Y.IsEnabled = false;
                TglRemoveST.IsEnabled = false;
            }
            if (trainTarget == "Bond")
            {
                Title = "可转债训练";
                RdoMarketAll.IsEnabled = RdoMarketMain.IsEnabled = RdoMarketCyb.IsEnabled = RdoMarketKcb.IsEnabled = false;
                RdoPeriodAll.IsEnabled = RdoPeriod5Y.IsEnabled = RdoPeriod10Y.IsEnabled = RdoPeriodBefore10Y.IsEnabled = false;
                TglRemoveST.IsEnabled = false;
            }
            if (trainTarget == "LimitUp")
            {
                Title = "涨停训练";
                // 涨停票池横跨主板/创业板/科创板且 SQL 已排除 ST：市场锁定"不限"、去除ST 强制开
                RdoMarketAll.IsEnabled = RdoMarketMain.IsEnabled = RdoMarketCyb.IsEnabled = RdoMarketKcb.IsEnabled = false;
                RdoMarketAll.IsChecked = true;
                TglRemoveST.IsEnabled = false;
                TglRemoveST.IsChecked = true;
                // 训练时间段不锁：LoadLimitUpRandomStock 会按所选范围过滤涨停日
            }
            if (trainTarget == "ETF")
            {
                Title = "ETF训练";
                RdoMarketAll.IsEnabled = RdoMarketMain.IsEnabled = RdoMarketCyb.IsEnabled = RdoMarketKcb.IsEnabled = false;
                RdoPeriodAll.IsEnabled = RdoPeriod5Y.IsEnabled = RdoPeriod10Y.IsEnabled = RdoPeriodBefore10Y.IsEnabled = false;
                TglRemoveST.IsEnabled = false;
            }
            // ★ 配置窗头部大标题随入口训练类型变化（原固定"双盲训练"）：各分支已设好 Title，直接复用。
            //   XAML 里标题 TextBlock 需带 x:Name="TxtTitle"；没加也不报错（FindName 找不到就跳过）。
            if (FindName("TxtTitle") is System.Windows.Controls.TextBlock titleTxt)
                titleTxt.Text = Title ?? "双盲训练";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private TrainingConfig CollectConfig()
        {
            var config = new TrainingConfig();

            // 分仓
            config.IsSplitPosition = TglSplitPosition.IsChecked == true;
            config.SplitPositionPercent = (int)SliderSplitPosition.Value;

            // 杠杆：开关关闭或未选时默认 1 倍
            if (TglLeverage.IsChecked == true)
            {
                if (RdoLeverage2.IsChecked == true) config.Leverage = 2;
                else if (RdoLeverage3.IsChecked == true) config.Leverage = 3;
                else if (RdoLeverage4.IsChecked == true) config.Leverage = 4;
                else config.Leverage = 1;
            }
            else
            {
                config.Leverage = 1;
            }

            // 止盈/止损
            config.IsStopProfit = TglStopProfit.IsChecked == true;
            config.StopProfitPercent = ParsePercent(TxtStopProfit.Text, 10);
            config.IsStopLoss = TglStopLoss.IsChecked == true;
            config.StopLossPercent = ParsePercent(TxtStopLoss.Text, 10);

            // 自动卖出天数
            config.AutoSellEnabled = TglAutoSell.IsChecked == true;
            config.AutoSellDays = (int)SliderAutoSell.Value;

            // 开盘买入 / 买卖自动跳 / 周月按日跳
            config.OpenPriceTrading = TglOpenBuy.IsChecked == true;
            config.AutoSkipAfterTrade = TglTradeAutoSkip.IsChecked == true;
            config.WeekMonthDayJump = TglWeekMonthDayJump.IsChecked == true;

            // 去除ST（默认开）
            config.RemoveST = TglRemoveST.IsChecked == true;

            // 市场类型
            config.MarketType = RdoMarketMain.IsChecked == true ? "Main"
                              : RdoMarketCyb.IsChecked == true ? "CYB"
                              : RdoMarketKcb.IsChecked == true ? "KCB" : "All";

            // 训练时间段
            config.TrainPeriod = RdoPeriod5Y.IsChecked == true ? "5Y"
                               : RdoPeriod10Y.IsChecked == true ? "10Y"
                               : RdoPeriodBefore10Y.IsChecked == true ? "Before10Y" : "All";

            // 训练目标（个股/指数）；涨停训练挂 IsLimitUpMode 开关，TrainTarget 保持 "Stock"（走个股 T+1 规则）
            config.IsLimitUpMode = _trainTarget == "LimitUp";
            config.TrainTarget = config.IsLimitUpMode ? "Stock" : _trainTarget;

            return config;
        }

        // 文本框可能为空或非法，解析失败时用默认值
        private static double ParsePercent(string text, double fallback)
        {
            return double.TryParse(text, out double v) && v > 0 ? v : fallback;
        }

        private void StartTraining_Click(object sender, RoutedEventArgs e)
        {
            var config = CollectConfig();
            KLineTrainWindow kLineWin = new KLineTrainWindow(config);
            kLineWin.Show();
            Application.Current.MainWindow = kLineWin;
            this.Close();
            this.Owner?.Close();
        }

        // ========== 止盈数值调节 ==========

        private void TxtStopProfit_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void BtnProfitUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtStopProfit.Text, out int val))
                TxtStopProfit.Text = (val + 1).ToString();
        }

        private void BtnProfitDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtStopProfit.Text, out int val))
                TxtStopProfit.Text = Math.Max(1, val - 1).ToString();
        }

        // ========== 止损数值调节 ==========

        private void TxtStopLoss_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void BtnLossUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtStopLoss.Text, out int val))
                TxtStopLoss.Text = (val + 1).ToString();
        }

        private void BtnLossDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtStopLoss.Text, out int val))
                TxtStopLoss.Text = Math.Max(1, val - 1).ToString();
        }

        // ========== 自动卖出天数调节 ==========

        private void BtnAutoSellMinus_Click(object sender, RoutedEventArgs e)
        {
            if (SliderAutoSell.Value > SliderAutoSell.Minimum)
                SliderAutoSell.Value -= 1;
        }

        private void BtnAutoSellPlus_Click(object sender, RoutedEventArgs e)
        {
            if (SliderAutoSell.Value < SliderAutoSell.Maximum)
                SliderAutoSell.Value += 1;
        }

        // ========== 分仓模式调节 ==========

        private void BtnSplitMinus_Click(object sender, RoutedEventArgs e)
        {
            if (SliderSplitPosition.Value > SliderSplitPosition.Minimum)
                SliderSplitPosition.Value -= 10;
        }

        private void BtnSplitPlus_Click(object sender, RoutedEventArgs e)
        {
            if (SliderSplitPosition.Value < SliderSplitPosition.Maximum)
                SliderSplitPosition.Value += 10;
        }

        // ========== 分仓模式与爆竹比例联动 ==========

        private void TglSplitPosition_Checked(object sender, RoutedEventArgs e)
        {
            TxtFirecrackerRatio.Text = ((int)SliderSplitPosition.Value).ToString();
        }

        private void TglSplitPosition_Unchecked(object sender, RoutedEventArgs e)
        {
            TxtFirecrackerRatio.Text = "100";
        }

        private void SliderSplitPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TglSplitPosition.IsChecked == true)
                TxtFirecrackerRatio.Text = ((int)SliderSplitPosition.Value).ToString();
        }
    }
}