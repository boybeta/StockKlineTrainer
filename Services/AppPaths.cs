using System;
using System.IO;

namespace BaozhuKLineTrainer.Services
{
    /// <summary>
    /// 应用数据目录统一入口（2026-09-30 去除 E 盘硬编码，开源前改造）：
    /// 老用户优先沿用 E:\baozhu（数据零迁移）；没有该目录的机器自动退回
    /// 程序目录下的 baozhu-data\ —— clone 即用、随程序走、好找好备份。
    /// </summary>
    public static class AppPaths
    {
        private const string LegacyRoot = @"E:\baozhu";

        public static string RootDir { get; } = ResolveRoot();
        public static string UserDataDir => Path.Combine(RootDir, "userdata");
        public static string ImageDir => Path.Combine(RootDir, "images");
        public static string StockDataDir => Path.Combine(RootDir, "stockdata");
        public static string ConfigDir => Path.Combine(RootDir, "config");
        public static string LogDir => Path.Combine(RootDir, "logs");
        public static string OutputDir => Path.Combine(RootDir, "output");

        /// <summary>
        /// 训练数据库路径：E 盘老库优先（兼容老用户）→ 程序目录 stockdata\ 兜底。
        /// 开源用户把自己建的 cy_stock.db 放到 程序目录\stockdata\ 即可直接使用。
        /// </summary>
        public static string GetDbPath()
        {
            string legacy = Path.Combine(LegacyRoot, "stockdata", "cy_stock.db");
            if (File.Exists(legacy)) return legacy;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stockdata", "cy_stock.db");
        }

        private static string ResolveRoot()
        {
            try
            {
                // 老数据目录存在则沿用（2026-09-30 之前的用户数据零迁移）
                if (Directory.Exists(LegacyRoot)) return LegacyRoot;
            }
            catch { /* 无 E 盘等任何异常 → 走便携目录 */ }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baozhu-data");
        }

        /// <summary>启动时确保目录齐全（全程 try-catch：目录创建失败不阻断程序启动）</summary>
        public static void EnsureDirectories()
        {
            foreach (var dir in new[] { RootDir, UserDataDir, ImageDir, StockDataDir, ConfigDir, LogDir, OutputDir })
            {
                try { Directory.CreateDirectory(dir); } catch { /* 权限/磁盘问题由具体使用时再暴露 */ }
            }
        }
    }
}