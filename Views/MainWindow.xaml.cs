using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BaozhuKLineTrainer;  // 如果 TrainingResultWindow 在同一命名空间下，这行其实不需要，但加上保险

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
        }

        /// <summary>
        /// 点击【盲盘训练】文字跳转双盲训练窗口
        /// </summary>
        private void OpenDoubleBlindTrain_Click(object sender, MouseButtonEventArgs e)
        {
            DoubleBlindTrainWindow trainWindow = new DoubleBlindTrainWindow();
            // 将当前主窗口设为弹窗所有者，弹窗居中依附主窗口
            trainWindow.Owner = this;
            // 模态弹窗，关闭训练窗口后才能操作主窗口
            trainWindow.ShowDialog();
        }
    }
}