using BaozhuKLineTrainer;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BaozhuKLineTrainer
{
    public partial class TrainingResultWindow : Window
    {
        public ResultAction ResultAction { get; private set; } = ResultAction.None;

        public TrainingResultWindow(TrainingResult result)
        {
            InitializeComponent();
            DataContext = result;
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 点击窗口任意空白处关闭（因为没有标题栏关闭按钮）
            if (e.Source is Border)
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