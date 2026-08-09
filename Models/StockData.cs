using System;

namespace BaozhuKLineTrainer.Models
{
    public class StockData
    {
        public DateTime Date { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }

        // MA
        public double? MA5 { get; set; }
        public double? MA10 { get; set; }
        public double? MA20 { get; set; }

        // EMA
        public double? EMA5 { get; set; }
        public double? EMA10 { get; set; }
        public double? EMA20 { get; set; }

        // BOLL
        public double? BollUp { get; set; }
        public double? BollMid { get; set; }
        public double? BollDn { get; set; }

        // VOL_MA
        public double? VolMA5 { get; set; }
        public double? VolMA10 { get; set; }

        // MACD
        public double? DIF { get; set; }
        public double? DEA { get; set; }
        public double? MACDHist { get; set; }

        public ScottPlot.OHLC ToOHLC()
        {
            return new ScottPlot.OHLC(Open, High, Low, Close, Date, TimeSpan.FromDays(1));
        }
    }
}