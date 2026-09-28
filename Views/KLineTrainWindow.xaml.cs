using ScottPlot.Plottables;
using BaozhuKLineTrainer;
using System.Windows;

namespace BaozhuKLineTrainer
{
    public partial class KLineTrainWindow : Window
    {
        public KLineTrainWindow(TrainingConfig config)
        {
            InitializeComponent();
            var vm = new MainViewModel(config);   // ← 传 config
            DataContext = vm;

            // 注入 ScottPlot 控件（名称不变）
            vm.KlinePlot = KlinePlot;
            vm.VolPlot = VolPlot;
            vm.MacdPlot = MacdPlot;
            vm.CursorPriceLabel = CursorPriceLabel;
            vm.CursorPriceText = CursorPriceText;

            Loaded += (s, e) =>
            {
                vm.SetupChartLinkage();
                vm.LoadData();
            };

            // 窗口关闭时退订主题事件，防止旧 VM 对已销毁控件 Refresh
            Closed += (s, e) => vm.DetachTheme();
        }

        /// <summary>按住顶部标题栏空白处拖动窗口（无系统标题栏后需要自己实现）</summary>
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        /// <summary>返回首页：二次确认，放弃本局（不结算、不计入曲线）</summary>
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要返回首页吗？\n\n本局训练进度将被放弃，盈亏不计入爆竹。",
                "返回首页",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            if (DataContext is MainViewModel vm)
                vm.ExitToHome();
        }

    }
}