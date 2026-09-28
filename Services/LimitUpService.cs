using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using BaozhuKLineTrainer.Models;

namespace BaozhuKLineTrainer.Services
{
    /// <summary>
    /// 涨停训练数据服务（2026-09-28）：
    /// 从 cy_daily 筛选发生过涨停的非 ST 个股，并把训练锚点定位到"最后一根可见K线 = 涨停日"。
    /// 新文件、零侵入；MainViewModel 仅需 2 行接线。
    /// </summary>
    public class LimitUpService
    {
        private readonly string _connStr;

        /// <summary>dbPath 与 MainViewModel 构造里给 DatabaseService 的是同一个（它内部转成连接串）。</summary>
        public LimitUpService(string dbPath)
        {
            _connStr = $"Data Source={dbPath}";
        }

        /// <summary>涨停判定阈值：30/68 开头 20%，其余 10%（与 UpdatePriceChart 黄蓝逻辑一致，留 0.5% 容差）。</summary>
        public static double LimitRatio(string code)
            => (code.StartsWith("30") || code.StartsWith("68")) ? 1.195 : 1.095;

        public class LimitUpStock
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public int Count { get; set; }   // 涨停次数（已按 >=2 过滤，供加权抽票/显示用）
        }

        private List<LimitUpStock> _poolCache;   // 票池缓存：数据不常变，进程内只查一次库

        /// <summary>
        /// 有涨停记录的个股池：2015 年后 >=2 次涨停、非 ST（is_st=0 且名称不含 ST，口径同 GetDailyBarCounts）。
        /// 注意：cy_daily 没有 name 列，名称/is_st 必须 JOIN cy_stock_info 取。
        /// 全表 LAG 一次算完（约几秒），结果进程内缓存；数据更新后调 Refresh()。
        /// </summary>
        public List<LimitUpStock> GetLimitUpStocks()
        {
            if (_poolCache != null) return _poolCache;

            const string sql = @"
SELECT t.code, MAX(s.name) AS name, COUNT(*) AS cnt
FROM (
    SELECT d.code, d.close,
           LAG(d.close) OVER (PARTITION BY d.code ORDER BY d.trade_date) AS prev_close
    FROM cy_daily d
    WHERE d.trade_date >= '20150101'
) t
JOIN cy_stock_info s ON s.code = t.code
WHERE t.prev_close IS NOT NULL
  AND s.is_st = 0 AND s.name NOT LIKE '%ST%'
  AND (   ((t.code LIKE '30%' OR t.code LIKE '68%')  AND t.close >= t.prev_close * 1.195)
       OR ((t.code NOT LIKE '30%' AND t.code NOT LIKE '68%') AND t.close >= t.prev_close * 1.095))
GROUP BY t.code
HAVING COUNT(*) >= 2
ORDER BY cnt DESC;";

            var list = new List<LimitUpStock>();
            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(sql, conn))
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        list.Add(new LimitUpStock
                        {
                            Code = rd.GetString(0),
                            Name = rd.IsDBNull(1) ? "" : rd.GetString(1),
                            Count = rd.GetInt32(2)
                        });
                    }
                }
            }
            _poolCache = list;
            return list;
        }

        public void Refresh() => _poolCache = null;

        private readonly Dictionary<string, List<string>> _dateCache = new();

        /// <summary>某只票的全部涨停日（yyyyMMdd 字符串列表，进程内缓存）。</summary>
        public List<string> GetLimitUpDates(string code)
        {
            if (_dateCache.TryGetValue(code, out var cached)) return cached;

            double ratio = LimitRatio(code);
            const string sql = @"
WITH t AS (
    SELECT trade_date, close,
           LAG(close) OVER (ORDER BY trade_date) AS prev_close
    FROM cy_daily
    WHERE code = @code
)
SELECT trade_date FROM t
WHERE prev_close IS NOT NULL AND close >= prev_close * @ratio
ORDER BY trade_date;";

            var dates = new List<string>();
            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@code", code);
                    cmd.Parameters.AddWithValue("@ratio", ratio);
                    using (var rd = cmd.ExecuteReader())
                        while (rd.Read()) dates.Add(rd.GetString(0));
                }
            }
            _dateCache[code] = dates;
            return dates;
        }

        /// <summary>
        /// 在单票日线中随机取一个涨停日作为锚点：
        /// 可见训练段 = [anchor-trainBars+1, anchor]（最后一根即涨停日），未来段 = 其后 futureBars 根。
        /// 约束：anchor 前有 trainBars-1 根可见 + 20 根均线/MACD 预热，后有 futureBars 根未来数据。
        /// 返回 null 表示该票无合格锚点（调用方应换票重抽）。
        /// </summary>
        public static List<StockData> PickSegment(List<StockData> daily, string code, int trainBars, int futureBars, Random rng)
        {
            if (daily == null || daily.Count < trainBars + futureBars + 20) return null;

            int firstAnchor = trainBars - 1 + 20;          // 前段：119 根可见 + 20 根指标预热
            int lastAnchor = daily.Count - futureBars - 1; // 后段：必须留足未来
            if (firstAnchor > lastAnchor) return null;

            var anchors = new List<int>();
            for (int i = firstAnchor; i <= lastAnchor; i++)
            {
                var cur = daily[i];
                var prev = daily[i - 1];
                if (cur.Close >= prev.Close * LimitRatio(code))
                    anchors.Add(i);
            }
            if (anchors.Count == 0) return null;

            int a = anchors[rng.Next(anchors.Count)];
            return daily.GetRange(a - trainBars + 1, trainBars + futureBars);
        }
    }
}