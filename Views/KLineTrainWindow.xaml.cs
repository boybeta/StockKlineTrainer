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

            Loaded += (s, e) =>
            {
                vm.SetupChartLinkage();
                vm.LoadData();
            };
        }
    }
}