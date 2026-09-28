using System.Windows;
using BaozhuKLineTrainer.Services;

namespace BaozhuKLineTrainer
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += (_, __) =>
            {
                if (ThemeService.Current == AppTheme.Dark) RbDark.IsChecked = true;
                else RbLight.IsChecked = true;
            };
            RbDark.Checked += (_, __) => ThemeService.Apply(AppTheme.Dark);
            RbLight.Checked += (_, __) => ThemeService.Apply(AppTheme.Light);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}