using System.Windows;

namespace BaozhuKLineTrainer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Services.ThemeService.Initialize();   // 必须在 base.OnStartup 之前应用主题（防白闪）
            base.OnStartup(e);
        }
    }
}