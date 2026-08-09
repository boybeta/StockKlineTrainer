using BaozhuKLineTrainer;
using System;
using System.IO;
using System.Windows;

namespace BaozhuKLineTrainer
{
    public partial class LoginWindow : Window
    {
        // 固定根目录 E:\baozhu
        public static readonly string RootDir = @"E:\baozhu";
        public static readonly string UserDataDir = Path.Combine(RootDir, "userdata");
        public static readonly string ImageDir = Path.Combine(RootDir, "images");

        public LoginWindow()
        {
            InitializeComponent();
            CreateProjectFolders();
        }

        /// <summary>
        /// 自动创建所有项目文件夹
        /// </summary>
        private void CreateProjectFolders()
        {
            Directory.CreateDirectory(RootDir);
            Directory.CreateDirectory(UserDataDir);
            Directory.CreateDirectory(ImageDir);
            Directory.CreateDirectory(Path.Combine(RootDir, "stockdata"));
            Directory.CreateDirectory(Path.Combine(RootDir, "config"));
            Directory.CreateDirectory(Path.Combine(RootDir, "logs"));
            Directory.CreateDirectory(Path.Combine(RootDir, "output"));
        }

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