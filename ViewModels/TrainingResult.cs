using System.Windows.Media;

namespace BaozhuKLineTrainer
{
    public class TrainingResult
    {
        public string StockName { get; set; } = "";
        public string DateRange { get; set; } = "";
        public double ProfitAmount { get; set; }
        public double ProfitPct { get; set; }
        public double IntervalChangePct { get; set; }
        public int OpenCount { get; set; }
        public double WinRate { get; set; }
        public string ElapsedTime { get; set; } = "";

        // 颜色画刷
        public Brush ProfitBrush => ProfitAmount >= 0 ? Brushes.Crimson : Brushes.Green;
        public Brush IntervalBrush => IntervalChangePct >= 0 ? Brushes.Crimson : Brushes.Green;
    }
}