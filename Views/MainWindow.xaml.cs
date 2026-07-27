using ScottPlot.Plottables;
using StockKLineTrainer.ViewModels;
using System.Windows;

namespace StockKLineTrainer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var vm = new MainViewModel();
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