using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace BaozhuKLineTrainer.Services
{
    /// <summary>
    /// 段位系统（2026-09-30 从 MainWindow 抽出共享）：
    /// 段位表 / 下标计算 / 徽章图片加载，首页与训练结算弹窗共用一份，改阈值只改这里。
    /// 判定口径：火星币达到阈值即升段；首页按曲线"历史峰值"定段（只升不降），
    /// 结算弹窗按"本局结算前后本金"判定当次升级。
    /// </summary>
    public static class RankService
    {
        public static readonly (string Name, double Threshold)[] Ranks =
        {
            ("白银小账户",              0),   // 起步 1 万
            ("黄金账户",          100_000),   // 10 万
            ("铂金账户",        1_000_000),   // 100 万
            ("钻石账户",       10_000_000),   // 1000 万
            ("星耀账户",      100_000_000),   // 1 亿
            ("传奇账户",    1_000_000_000),   // 10 亿（达到即暴富+1，重置回 1 万）
        };

        /// <summary>资金值 → 段位下标：>= 阈值的最高一档</summary>
        public static int CalcRankIndex(double value)
        {
            int idx = 0;
            for (int i = 0; i < Ranks.Length; i++)
                if (value >= Ranks[i].Threshold) idx = i;
            return idx;
        }

        /// <summary>结算升级判定：跨档则返回新段位下标，未升级返回 -1。
        /// 连跳多级只报最高到达档（一次结算不连弹多个窗）。</summary>
        public static int GetPromotionRankIndex(double oldValue, double newValue)
        {
            int newIdx = CalcRankIndex(newValue);
            return newIdx > CalcRankIndex(oldValue) ? newIdx : -1;
        }

        /// <summary>段位徽章路径：程序目录 images\{段位名}.png（白银小账户.png / 黄金账户.png …）</summary>
        public static string GetRankIconPath(int rankIdx)
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", Ranks[rankIdx].Name + ".png");

        /// <summary>加载徽章图；文件不存在或加载失败返回 null，由调用方兜底</summary>
        public static BitmapImage? LoadRankIcon(int rankIdx)
        {
            string path = GetRankIconPath(rankIdx);
            if (!File.Exists(path)) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // 立即加载，避免文件被锁
            bmp.EndInit();
            return bmp;
        }
    }
}