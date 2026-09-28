namespace BaozhuKLineTrainer
{
    public class TrainingConfig
    {
        // 分仓
        public bool IsSplitPosition { get; set; }
        public int SplitPositionPercent { get; set; }
        public int Leverage { get; set; } = 1;

        // 止盈止损
        public bool IsStopProfit { get; set; }
        public double StopProfitPercent { get; set; } = 10;
        public bool IsStopLoss { get; set; }
        public double StopLossPercent { get; set; } = 10;
        /// <summary>涨停训练模式：A股数据、T+1、锁定日线，锚点=涨停日。</summary>
        public bool IsLimitUpMode { get; set; }

        // ★ 本次新增：配置窗开关全部落到这里
        public bool AutoSellEnabled { get; set; }      // 自动卖出开关
        public int AutoSellDays { get; set; } = 10;    // 自动卖出天数
        public bool OpenPriceTrading { get; set; }     // 开盘买入（成交价用开盘价）
        public bool AutoSkipAfterTrade { get; set; }   // 买卖自动跳
        public bool WeekMonthDayJump { get; set; }     // 周月按日跳（点击1次=推进1根）
        public bool RemoveST { get; set; } = true;     // 去除ST（默认开）
        public string MarketType { get; set; } = "All";   // All / Main / CYB / KCB
        public string TrainPeriod { get; set; } = "All";  // All / 5Y / 10Y / Before10Y

        // ★ 本次新增：配置窗开关全部落到这里
        public string TrainTarget { get; set; } = "Stock";  // Stock=个股盲盘 / Index=指数训练
    }
}