using Microsoft.Data.Sqlite;
using BaozhuKLineTrainer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BaozhuKLineTrainer.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        public List<string> GetAllStockCodes()
        {
            var codes = new List<string>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var cmd = new SqliteCommand(
                "SELECT code FROM cy_stock_info WHERE is_st = 0 ORDER BY code", conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                codes.Add(reader.GetString(0));
            }
            return codes;
        }

        public List<StockData> GetStockData(string code, string? startDate = null, string? endDate = null, int limit = 200)
        {
            var data = new List<StockData>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // 查询所有需要的字段
            string sql = @"
                SELECT trade_date, open, high, low, close, volume,
                       ma5, ma10, ma20,
                       ema5, ema10, ema20,
                       boll_up, boll_mid, boll_dn,
                       vol_ma5, vol_ma10,
                       dif, dea, macd_hist
                FROM cy_daily 
                WHERE code = @code";

            if (!string.IsNullOrEmpty(startDate))
                sql += " AND trade_date >= @startDate";
            if (!string.IsNullOrEmpty(endDate))
                sql += " AND trade_date <= @endDate";

            sql += " ORDER BY trade_date ASC LIMIT @limit";

            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@limit", limit);

            if (!string.IsNullOrEmpty(startDate))
                cmd.Parameters.AddWithValue("@startDate", startDate);
            if (!string.IsNullOrEmpty(endDate))
                cmd.Parameters.AddWithValue("@endDate", endDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string dateStr = reader.GetString(0);
                DateTime date = ParseTradeDate(dateStr);

                data.Add(new StockData
                {
                    Date = date,
                    Open = reader.GetDouble(1),
                    High = reader.GetDouble(2),
                    Low = reader.GetDouble(3),
                    Close = reader.GetDouble(4),
                    Volume = reader.GetInt64(5),

                    MA5 = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    MA10 = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    MA20 = reader.IsDBNull(8) ? null : reader.GetDouble(8),

                    EMA5 = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    EMA10 = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                    EMA20 = reader.IsDBNull(11) ? null : reader.GetDouble(11),

                    BollUp = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                    BollMid = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                    BollDn = reader.IsDBNull(14) ? null : reader.GetDouble(14),

                    VolMA5 = reader.IsDBNull(15) ? null : reader.GetDouble(15),
                    VolMA10 = reader.IsDBNull(16) ? null : reader.GetDouble(16),

                    DIF = reader.IsDBNull(17) ? null : reader.GetDouble(17),
                    DEA = reader.IsDBNull(18) ? null : reader.GetDouble(18),
                    MACDHist = reader.IsDBNull(19) ? null : reader.GetDouble(19)
                });
            }

            return data;
        }

        private DateTime ParseTradeDate(string dateStr)
        {
            if (dateStr.Length == 8)
            {
                int year = int.Parse(dateStr.Substring(0, 4));
                int month = int.Parse(dateStr.Substring(4, 2));
                int day = int.Parse(dateStr.Substring(6, 2));
                return new DateTime(year, month, day);
            }
            return DateTime.Parse(dateStr);
        }

        /// <summary>
        /// 获取数据库中所有股票代码
        /// </summary>



        public (List<StockData> data, string code, DateTime startDate, DateTime endDate) GetRandomSegment(int segmentDays = 120)
        {
            var codes = GetAllStockCodes();
            if (codes.Count == 0) return (new List<StockData>(), "", DateTime.MinValue, DateTime.MinValue);

            var random = new Random();
            string selectedCode = codes[random.Next(codes.Count)];

            var allData = GetStockData(selectedCode, limit: 2000);
            if (allData.Count < segmentDays * 2)
                return GetRandomSegment(segmentDays);

            int maxStart = allData.Count - segmentDays;
            int startIndex = random.Next(maxStart);
            var segment = allData.Skip(startIndex).Take(segmentDays).ToList();

            return (segment, selectedCode, segment.First().Date, segment.Last().Date);
        }

        // ==================== 训练记录持久化 ====================

        /// <summary>建表（幂等，老库启动时自动升级）。每局结算写入一条。</summary>
        public void EnsureTrainingRecordTable()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(@"
                CREATE TABLE IF NOT EXISTS cy_training_record (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    train_time TEXT NOT NULL,
                    stock_code TEXT,
                    stock_name TEXT,
                    period TEXT,
                    start_date TEXT,
                    end_date TEXT,
                    initial_firecrackers REAL,
                    final_firecrackers REAL,
                    profit_amount REAL,
                    profit_pct REAL,
                    interval_pct REAL,
                    open_count INTEGER,
                    win_rate REAL,
                    hold_days INTEGER,
                    watch_days INTEGER,
                    elapsed_sec INTEGER,
                    leverage INTEGER,
                    is_full_game INTEGER DEFAULT 1,
                    config_json TEXT,
                    heavy_hold_days INTEGER DEFAULT 0
                )", conn);
            cmd.ExecuteNonQuery();

            // 老库升级：缺 heavy_hold_days 列则自动补上（幂等）
            bool hasHeavy = false;
            using (var r = new SqliteCommand("PRAGMA table_info(cy_training_record)", conn).ExecuteReader())
                while (r.Read())
                    if (r.GetString(1) == "heavy_hold_days") hasHeavy = true;
            if (!hasHeavy)
                new SqliteCommand("ALTER TABLE cy_training_record ADD COLUMN heavy_hold_days INTEGER DEFAULT 0", conn).ExecuteNonQuery();
        }

        /// <summary>写入一条训练记录</summary>
        public void SaveTrainingRecord(TrainingRecordEntry e)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(@"
                INSERT INTO cy_training_record
                    (train_time, stock_code, stock_name, period, start_date, end_date,
                     initial_firecrackers, final_firecrackers, profit_amount, profit_pct, interval_pct,
                     open_count, win_rate, hold_days, watch_days, elapsed_sec, leverage, is_full_game, config_json,heavy_hold_days)
                VALUES
                    (@train_time, @stock_code, @stock_name, @period, @start_date, @end_date,
                     @initial_firecrackers, @final_firecrackers, @profit_amount, @profit_pct, @interval_pct,
                     @open_count, @win_rate, @hold_days, @watch_days, @elapsed_sec, @leverage, @is_full_game, @config_json,@heavy_hold_days)", conn);
            cmd.Parameters.AddWithValue("@train_time", e.TrainTime);
            cmd.Parameters.AddWithValue("@stock_code", e.StockCode);
            cmd.Parameters.AddWithValue("@stock_name", e.StockName);
            cmd.Parameters.AddWithValue("@period", e.Period);
            cmd.Parameters.AddWithValue("@start_date", e.StartDate);
            cmd.Parameters.AddWithValue("@end_date", e.EndDate);
            cmd.Parameters.AddWithValue("@initial_firecrackers", e.InitialFirecrackers);
            cmd.Parameters.AddWithValue("@final_firecrackers", e.FinalFirecrackers);
            cmd.Parameters.AddWithValue("@profit_amount", e.ProfitAmount);
            cmd.Parameters.AddWithValue("@profit_pct", e.ProfitPct);
            cmd.Parameters.AddWithValue("@interval_pct", e.IntervalPct);
            cmd.Parameters.AddWithValue("@open_count", e.OpenCount);
            cmd.Parameters.AddWithValue("@win_rate", e.WinRate);
            cmd.Parameters.AddWithValue("@hold_days", e.HoldDays);
            cmd.Parameters.AddWithValue("@watch_days", e.WatchDays);
            cmd.Parameters.AddWithValue("@elapsed_sec", e.ElapsedSec);
            cmd.Parameters.AddWithValue("@leverage", e.Leverage);
            cmd.Parameters.AddWithValue("@is_full_game", e.IsFullGame);
            cmd.Parameters.AddWithValue("@config_json", e.ConfigJson ?? "");
            cmd.Parameters.AddWithValue("@heavy_hold_days", e.HeavyHoldDays);
            cmd.ExecuteNonQuery();
        }

        /// <summary>一次 GROUP BY 拿全部股票的日线根数（启动筛池用，避免逐股拉全量数据只为数个数）</summary>
        public Dictionary<string, int> GetDailyBarCounts()
        {
            var counts = new Dictionary<string, int>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(
                "SELECT code, COUNT(*) FROM cy_daily GROUP BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
            return counts;
        }

        /// <summary>爆竹数量曲线数据：按结算顺序返回（时间, 累计爆竹）。
        /// 累计口径：10000 起步，每局盈亏额累加（盈利为正则加，亏损为负则减）</summary>
        public List<(string time, double firecrackers)> GetFirecrackerCurve()
        {
            var list = new List<(string, double)>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(
                "SELECT train_time, profit_amount FROM cy_training_record ORDER BY id ASC", conn);
            using var reader = cmd.ExecuteReader();
            double total = 10000;   // 起始本金
            while (reader.Read())
            {
                total = Math.Max(0, total + reader.GetDouble(1));   // 累计为负则归零（破产）   // 每局盈亏额：正加负减
                list.Add((reader.GetString(0), total));
            }
            return list;
        }

        /// <summary>首页顶部统计：训练场次 + 最新累计爆竹（无记录时返回默认值 10000）</summary>
        public (int gameCount, double latestFirecrackers) GetHomeSummary()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(
                                                "SELECT COUNT(*), MAX(0, COALESCE(10000 + (SELECT SUM(profit_amount) FROM cy_training_record), 10000)) FROM cy_training_record", conn);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt32(0), reader.GetDouble(1));
            return (0, 10000);
        }

        /// <summary>首页"训练数据"格子聚合：总胜率(开仓加权)/平均持仓/跑赢区间率/盈亏比/平均每局收益</summary>
        /// <summary>训练数据聚合（首页"训练数据"卡片 12 格）</summary>
        public (int gameCount, double gameWinRate, double beatIntervalRate, double profitLossRatio,
                double avgHoldDays, double holdRate, double heavyRate, double avgElapsedSec,
                int totalOpens, double openWinRate, double maxProfitPct, int winGames) GetTrainingStats()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(@"
                SELECT
                    COUNT(*),
                    COALESCE(AVG(CASE WHEN profit_pct > 0 THEN 1.0 ELSE 0 END) * 100, 0),
                    COALESCE(AVG(CASE WHEN profit_pct > interval_pct THEN 1.0 ELSE 0 END) * 100, 0),
                    COALESCE(
                        (SUM(CASE WHEN profit_pct > 0 THEN profit_pct END)
                            / NULLIF(COUNT(CASE WHEN profit_pct > 0 THEN 1 END), 0))
                        /
                        (ABS(SUM(CASE WHEN profit_pct < 0 THEN profit_pct END))
                            / NULLIF(COUNT(CASE WHEN profit_pct < 0 THEN 1 END), 0))
                    , 0),
                    COALESCE(AVG(hold_days), 0),
                    COALESCE(SUM(hold_days) * 100.0 / NULLIF(SUM(hold_days + watch_days), 0), 0),
                    COALESCE(SUM(heavy_hold_days) * 100.0 / NULLIF(SUM(hold_days + watch_days), 0), 0),
                    COALESCE(AVG(elapsed_sec), 0),
                    COALESCE(SUM(open_count), 0),
                    COALESCE(SUM(win_rate * open_count) / NULLIF(SUM(open_count), 0), 0),
                    COALESCE(MAX(profit_pct), 0),
                    COALESCE(SUM(CASE WHEN profit_pct > 0 THEN 1 ELSE 0 END), 0)
                FROM cy_training_record", conn);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt32(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3),
                        reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7),
                        reader.GetInt32(8), reader.GetDouble(9), reader.GetDouble(10), reader.GetInt32(11));
            return (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        /// <summary>按代码查股票名称（训练记录用）</summary>
        public string GetStockName(string code)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT name FROM cy_stock_info WHERE code = @code", conn);
            cmd.Parameters.AddWithValue("@code", code);
            var result = cmd.ExecuteScalar();
            return result == null ? "" : result.ToString() ?? "";
        }
    }

    /// <summary>训练记录行模型（落库用）</summary>
    public class TrainingRecordEntry
    {
        public string TrainTime { get; set; } = "";
        public string StockCode { get; set; } = "";
        public string StockName { get; set; } = "";
        public string Period { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public double InitialFirecrackers { get; set; }
        public double FinalFirecrackers { get; set; }
        public double ProfitAmount { get; set; }
        public double ProfitPct { get; set; }
        public double IntervalPct { get; set; }
        public int OpenCount { get; set; }
        public double WinRate { get; set; }
        public int HoldDays { get; set; }
        public int WatchDays { get; set; }
        public int ElapsedSec { get; set; }
        public int Leverage { get; set; }
        public int IsFullGame { get; set; } = 1;
        public string ConfigJson { get; set; } = "";
        public int HeavyHoldDays { get; set; }
    }
}