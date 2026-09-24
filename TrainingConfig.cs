namespace BaozhuKLineTrainer
{
    public class TrainingConfig
    {
        // 分仓
        public bool IsSplitPosition { get; set; }
        public int SplitPositionPercent { get; set; }
        public int Leverage { get; set; } = 1;
        public bool IsStopProfit { get; set; }
        public double StopProfitPercent { get; set; } = 10;
        public bool IsStopLoss { get; set; }
        public double StopLossPercent { get; set; } = 10;
    }
}