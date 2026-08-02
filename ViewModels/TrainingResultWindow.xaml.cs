using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StockKLineTrainer
{
    public partial class TrainingResultWindow : Window
    {
        // 用户点了哪个按钮，外部通过这属性读取
        public ResultAction ResultAction { get; private set; } = ResultAction.None;

        public TrainingResultWindow(TrainingResult result)
        {
            InitializeComponent();
            DataContext = result;
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 只有点击遮罩才关闭（点内容区不关闭）
            if (e.Source is Border border && border.Background.ToString() == "#80000000")
            {
                ResultAction = ResultAction.None;
                Close();
            }
        }

        private void Review_Click(object sender, RoutedEventArgs e)
        {
            ResultAction = ResultAction.Review;
            Close();
        }

        private void End_Click(object sender, RoutedEventArgs e)
        {
            ResultAction = ResultAction.End;
            Close();
        }

        private void NextGame_Click(object sender, RoutedEventArgs e)
        {
            ResultAction = ResultAction.NextGame;
            Close();
        }
    }
}