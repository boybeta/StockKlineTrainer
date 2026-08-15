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

        private void StartTraining_Click(object sender, RoutedEventArgs e)
        {
            KLineTrainWindow kLineWin = new KLineTrainWindow();
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
    }
}