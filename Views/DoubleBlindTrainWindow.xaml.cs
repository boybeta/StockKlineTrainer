using System.Windows;

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
            // 1. 创建K线训练窗口（非模态打开）
            KLineTrainWindow kLineWin = new KLineTrainWindow();
            kLineWin.Show();

            // 2. 把K线窗口设为主窗口，后续弹窗（如TrainingResultWindow）才能正确找到Owner
            Application.Current.MainWindow = kLineWin;

            // 3. 关闭当前双盲配置窗口
            this.Close();

            // 4. 关闭首页MainWindow（this.Owner就是打开当前窗口的MainWindow）
            this.Owner?.Close();
        }
    }
}