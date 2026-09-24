using System;
using System.Collections.Generic;
using System.Linq;
using BaozhuKLineTrainer.Models;

namespace BaozhuKLineTrainer.Services
{
    /// <summary>
    /// K线周期聚合（日线 → 周线、月线，未来可扩展季线等）
    /// </summary>
    public static class KLineAggregator
    {
        /// <summary>
        /// 按目标周期聚合日线数据（日线直接原样返回，无拷贝）
        /// </summary>
        public static List<StockData> ToPeriod(List<StockData> daily, KLinePeriod period)
        {
            if (daily == null || daily.Count == 0)
                return daily ?? new List<StockData>();

            return period switch
            {
                KLinePeriod.Week => ToWeek(daily),
                KLinePeriod.Month => ToMonth(daily),
                _ => daily,   // Day 或未知周期，原样返回
            };
        }

        /// <summary>
        /// 周线聚合为周线。
        /// 规则：按自然周（周一起算）分组；
        ///       Open=周内首个交易日开盘价，High/Low=周内最高/最低；
        ///       Close=周内最后一个交易日收盘价，Volume=成交量求和；
        ///       Date=周内最后一个交易日（即该周线在序列中的位置）。
        /// </summary>
        private static List<StockData> ToWeek(List<StockData> daily)
        {
            var result = new List<StockData>();

            StockData? current = null;
            DateTime currentMonday = DateTime.MinValue;

            foreach (var d in daily.OrderBy(x => x.Date))
            {
                // 用该交易日所在周的周一作分组键（周一=0，避免跨年/跨月边界错乱）
                int diff = ((int)d.Date.DayOfWeek + 6) % 7;
                var monday = d.Date.AddDays(-diff).Date;

                if (current == null || monday != currentMonday)
                {
                    // 新的一周，先把上一周加进结果
                    if (current != null)
                        result.Add(current);

                    currentMonday = monday;
                    current = new StockData
                    {
                        Date = d.Date,
                        Open = d.Open,
                        High = d.High,
                        Low = d.Low,
                        Close = d.Close,
                        Volume = d.Volume
                    };
                }
                else
                {
                    // 同一周，向后累计
                    current.High = Math.Max(current.High, d.High);
                    current.Low = Math.Min(current.Low, d.Low);
                    current.Close = d.Close;          // 收盘价 = 周内最后一天
                    current.Date = d.Date;            // 日期   = 周内最后一个交易日
                    current.Volume += d.Volume;
                }
            }

            if (current != null)
                result.Add(current);

            return result;
        }

        /// <summary>
        /// 日线聚合为月线。
        /// 规则：按自然月分组；
        ///       Open=月内首个交易日开盘价，High/Low=月内最高/最低；
        ///       Close=月内最后一个交易日收盘价，Volume=成交量求和；
        ///       Date=月内最后一个交易日（即该月线在序列中的位置）。
        /// </summary>
        private static List<StockData> ToMonth(List<StockData> daily)
        {
            var result = new List<StockData>();

            StockData? current = null;
            int currentKey = 0;   // Year*12+Month 作分组键

            foreach (var d in daily.OrderBy(x => x.Date))
            {
                int key = d.Date.Year * 12 + d.Date.Month;

                if (current == null || key != currentKey)
                {
                    // 新的一月，先把上一月加进结果
                    if (current != null)
                        result.Add(current);

                    currentKey = key;
                    current = new StockData
                    {
                        Date = d.Date,
                        Open = d.Open,
                        High = d.High,
                        Low = d.Low,
                        Close = d.Close,
                        Volume = d.Volume
                    };
                }
                else
                {
                    // 同一月，向后累计
                    current.High = Math.Max(current.High, d.High);
                    current.Low = Math.Min(current.Low, d.Low);
                    current.Close = d.Close;          // 收盘价 = 月末交易日
                    current.Date = d.Date;            // 日期   = 月内最后一个交易日
                    current.Volume += d.Volume;
                }
            }

            if (current != null)
                result.Add(current);

            return result;
        }
    }
}