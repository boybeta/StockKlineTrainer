using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace BaozhuKLineTrainer.Services
{
    public enum AppTheme { Dark, Light }

    /// <summary>
    /// 主题切换：换自定义颜色字典 + MaterialDesign BaseTheme + 通知图表重刷。
    /// 持久化到 E:\baozhu\config\theme.txt（不进数据库、不进 git）。
    /// </summary>
    public static class ThemeService
    {
        // 2026-09-30：去 E 盘硬编码，改走 AppPaths（老用户 E:\baozhu，新用户 程序目录\baozhu-data）
        private static string ConfigDir => AppPaths.ConfigDir;
        private static string ConfigPath => Path.Combine(AppPaths.ConfigDir, "theme.txt");

        public static AppTheme Current { get; private set; } = AppTheme.Dark;

        /// <summary>主题变化通知：VM 订阅后重刷 ScottPlot 配色</summary>
        public static event Action<AppTheme> ThemeChanged;

        /// <summary>App.OnStartup 里、显示主窗口之前调用</summary>
        public static void Initialize()
        {
            Apply(Load(), save: false);
        }

        public static void Apply(AppTheme theme, bool save = true)
        {
            Current = theme;
            var dicts = Application.Current.Resources.MergedDictionaries;

            // 1) 换自定义颜色字典（插在首位，优先级最高）
            string colorsUri = theme == AppTheme.Dark
                ? "pack://application:,,,/Themes/Colors.Dark.xaml"
                : "pack://application:,,,/Themes/Colors.Light.xaml";

            for (int i = dicts.Count - 1; i >= 0; i--)
            {
                var src = dicts[i].Source;
                if (src != null && src.OriginalString.Contains("Themes/Colors."))
                    dicts.RemoveAt(i);
            }
            dicts.Insert(0, new ResourceDictionary { Source = new Uri(colorsUri, UriKind.Absolute) });

            // 2) MaterialDesign 官方控件跟 BaseTheme 走
            foreach (var d in dicts)
            {
                if (d is MaterialDesignThemes.Wpf.BundledTheme bt)
                {
                    bt.BaseTheme = theme == AppTheme.Dark
                        ? MaterialDesignThemes.Wpf.BaseTheme.Dark
                        : MaterialDesignThemes.Wpf.BaseTheme.Light;
                }
            }

            if (save) Save(theme);

            // 3) 通知图表重刷（切到 UI 线程）
            Application.Current.Dispatcher.BeginInvoke(
                new Action(() => ThemeChanged?.Invoke(theme)));
        }

        private static AppTheme Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var s = File.ReadAllText(ConfigPath).Trim();
                    if (Enum.TryParse<AppTheme>(s, out var t)) return t;
                }
            }
            catch { /* 配置损坏就退回深色 */ }
            return AppTheme.Dark;
        }

        private static void Save(AppTheme theme)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigPath, theme.ToString());
            }
            catch { /* 写失败不影响使用 */ }
        }
    }
}