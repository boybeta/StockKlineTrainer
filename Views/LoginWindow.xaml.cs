using BaozhuKLineTrainer;
using System;
using System.IO;
using System.Windows;
using BaozhuKLineTrainer.Services;

namespace BaozhuKLineTrainer
{
    public partial class LoginWindow : Window
    {
        // 2026-09-30：根目录改走 AppPaths（老用户 E:\baozhu 零迁移；无 E 盘机器自动用程序目录\baozhu-data）
        // 保留这三个静态字段名 → MainWindow 等处的 LoginWindow.UserDataDir 引用无需改动
        public static readonly string RootDir = AppPaths.RootDir;
        public static readonly string UserDataDir = AppPaths.UserDataDir;
        public static readonly string ImageDir = AppPaths.ImageDir;

        public LoginWindow()
        {
            InitializeComponent();
            CreateProjectFolders();
        }

        /// <summary>
        /// 自动创建所有项目文件夹（AppPaths 内部全程 try-catch：无 E 盘/无权限也不会崩启动）
        /// </summary>
        private void CreateProjectFolders() => AppPaths.EnsureDirectories();

        /// <summary>
        /// 登录按钮点击事件，跳转首页
        /// </summary>
        private void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }
    }
}