using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace BaozhuKLineTrainer
{
    public partial class DoubleBlindTrainWindow : Window
    {
        public DoubleBlindTrainWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private TrainingConfig CollectConfig()
        {
            var config = new TrainingConfig();

            // 当前只收集分仓配置，后续每加一个功能就在这里加一行
            config.IsSplitPosition = TglSplitPosition.IsChecked == true;
            config.SplitPositionPercent = (int)SliderSplitPosition.Value;

            return config;
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

        // ========== 止损数值调节（新增） ==========

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