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

        public List<string> GetAllStockCodes(bool excludeSt = false)
        {
            var codes = new List<string>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var sql = "SELECT code FROM cy_stock_info";
            if (excludeSt) sql += " WHERE is_st = 0 AND name NOT LIKE '%ST%'";
            sql += " ORDER BY code";

            var cmd = new SqliteCommand(sql, conn);

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

        // ==================== 指数训练数据源 ====================

        /// <summary>全部指数代码（cy_index_daily 里实际有的）</summary>
        public List<string> GetAllIndexCodes()
        {
            var codes = new List<string>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code FROM cy_index_daily GROUP BY code ORDER BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                codes.Add(reader.GetString(0));
            return codes;
        }

        /// <summary>指数日线根数（筛池用，同 _stockDailyCounts 口径）</summary>
        public Dictionary<string, int> GetIndexDailyCounts()
        {
            var counts = new Dictionary<string, int>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code, COUNT(*) FROM cy_index_daily GROUP BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
            return counts;
        }

        /// <summary>指数日线（表里没有预算指标，OHLCV 返回后由 IndicatorCalculator.Fill 现算）</summary>
        public List<StockData> GetIndexData(string code, string? startDate = null, string? endDate = null, int limit = 10000)
        {
            var data = new List<StockData>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string sql = @"SELECT trade_date, open, high, low, close, volume
                   FROM cy_index_daily WHERE code = @code";
            if (!string.IsNullOrEmpty(startDate)) sql += " AND trade_date >= @startDate";
            if (!string.IsNullOrEmpty(endDate)) sql += " AND trade_date <= @endDate";
            sql += " ORDER BY trade_date ASC LIMIT @limit";

            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@limit", limit);
            if (!string.IsNullOrEmpty(startDate)) cmd.Parameters.AddWithValue("@startDate", startDate);
            if (!string.IsNullOrEmpty(endDate)) cmd.Parameters.AddWithValue("@endDate", endDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                data.Add(new StockData
                {
                    Date = ParseTradeDate(reader.GetString(0)),
                    Open = reader.GetDouble(1),
                    High = reader.GetDouble(2),
                    Low = reader.GetDouble(3),
                    Close = reader.GetDouble(4),
                    Volume = reader.GetInt64(5)
                });
            }
            return data;
        }

        // ==================== 可转债训练数据源 ====================

        /// <summary>全部可转债代码（cy_bond_daily 里实际有的）</summary>
        public List<string> GetAllBondCodes()
        {
            var codes = new List<string>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code FROM cy_bond_daily GROUP BY code ORDER BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                codes.Add(reader.GetString(0));
            return codes;
        }

        /// <summary>可转债日线根数（筛池用）</summary>
        public Dictionary<string, int> GetBondDailyCounts()
        {
            var counts = new Dictionary<string, int>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code, COUNT(*) FROM cy_bond_daily GROUP BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
            return counts;
        }

        /// <summary>可转债日线（表里没有预算指标，OHLCV 返回后由 IndicatorCalculator.Fill 现算）</summary>
        public List<StockData> GetBondData(string code, string? startDate = null, string? endDate = null, int limit = 10000)
        {
            var data = new List<StockData>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string sql = @"SELECT trade_date, open, high, low, close, volume
                   FROM cy_bond_daily WHERE code = @code";
            if (!string.IsNullOrEmpty(startDate)) sql += " AND trade_date >= @startDate";
            if (!string.IsNullOrEmpty(endDate)) sql += " AND trade_date <= @endDate";
            sql += " ORDER BY trade_date ASC LIMIT @limit";

            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@limit", limit);
            if (!string.IsNullOrEmpty(startDate)) cmd.Parameters.AddWithValue("@startDate", startDate);
            if (!string.IsNullOrEmpty(endDate)) cmd.Parameters.AddWithValue("@endDate", endDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                data.Add(new StockData
                {
                    Date = ParseTradeDate(reader.GetString(0)),
                    Open = reader.GetDouble(1),
                    High = reader.GetDouble(2),
                    Low = reader.GetDouble(3),
                    Close = reader.GetDouble(4),
                    Volume = reader.GetInt64(5)
                });
            }
            return data;
        }

        // ==================== 美股训练数据源 ====================

        /// <summary>全部美股代码（cy_us_daily 里实际有的）</summary>
        public List<string> GetAllUSCodes()
        {
            var codes = new List<string>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code FROM cy_us_daily GROUP BY code ORDER BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                codes.Add(reader.GetString(0));
            return codes;
        }

        /// <summary>美股日线根数（筛池用）</summary>
        public Dictionary<string, int> GetUSDailyCounts()
        {
            var counts = new Dictionary<string, int>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code, COUNT(*) FROM cy_us_daily GROUP BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
            return counts;
        }

        /// <summary>美股日线（表里没有预算指标，OHLCV 返回后由 IndicatorCalculator.Fill 现算）</summary>
        public List<StockData> GetUSData(string code, string? startDate = null, string? endDate = null, int limit = 10000)
        {
            var data = new List<StockData>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string sql = @"SELECT trade_date, open, high, low, close, volume
                   FROM cy_us_daily WHERE code = @code";
            if (!string.IsNullOrEmpty(startDate)) sql += " AND trade_date >= @startDate";
            if (!string.IsNullOrEmpty(endDate)) sql += " AND trade_date <= @endDate";
            sql += " ORDER BY trade_date ASC LIMIT @limit";

            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@limit", limit);
            if (!string.IsNullOrEmpty(startDate)) cmd.Parameters.AddWithValue("@startDate", startDate);
            if (!string.IsNullOrEmpty(endDate)) cmd.Parameters.AddWithValue("@endDate", endDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                data.Add(new StockData
                {
                    Date = ParseTradeDate(reader.GetString(0)),
                    Open = reader.GetDouble(1),
                    High = reader.GetDouble(2),
                    Low = reader.GetDouble(3),
                    Close = reader.GetDouble(4),
                    Volume = reader.GetInt64(5)
                });
            }
            return data;
        }

        // ==================== 港股训练数据源 ====================

        /// <summary>全部港股代码（cy_hk_daily 里实际有的）</summary>
        public List<string> GetAllHKCodes()
        {
            var codes = new List<string>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code FROM cy_hk_daily GROUP BY code ORDER BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                codes.Add(reader.GetString(0));
            return codes;
        }

        /// <summary>港股日线根数（筛池用）</summary>
        public Dictionary<string, int> GetHKDailyCounts()
        {
            var counts = new Dictionary<string, int>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code, COUNT(*) FROM cy_hk_daily GROUP BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
            return counts;
        }

        /// <summary>港股日线（表里没有预算指标，OHLCV 返回后由 IndicatorCalculator.Fill 现算）</summary>
        public List<StockData> GetHKData(string code, string? startDate = null, string? endDate = null, int limit = 10000)
        {
            var data = new List<StockData>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string sql = @"SELECT trade_date, open, high, low, close, volume
                   FROM cy_hk_daily WHERE code = @code";
            if (!string.IsNullOrEmpty(startDate)) sql += " AND trade_date >= @startDate";
            if (!string.IsNullOrEmpty(endDate)) sql += " AND trade_date <= @endDate";
            sql += " ORDER BY trade_date ASC LIMIT @limit";

            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@limit", limit);
            if (!string.IsNullOrEmpty(startDate)) cmd.Parameters.AddWithValue("@startDate", startDate);
            if (!string.IsNullOrEmpty(endDate)) cmd.Parameters.AddWithValue("@endDate", endDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                data.Add(new StockData
                {
                    Date = ParseTradeDate(reader.GetString(0)),
                    Open = reader.GetDouble(1),
                    High = reader.GetDouble(2),
                    Low = reader.GetDouble(3),
                    Close = reader.GetDouble(4),
                    Volume = reader.GetInt64(5)
                });
            }
            return data;
        }

        // ==================== 期货训练数据源 ====================

        /// <summary>全部期货代码（cy_future_daily 里实际有的）</summary>
        public List<string> GetAllFutureCodes()
        {
            var codes = new List<string>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code FROM cy_future_daily GROUP BY code ORDER BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                codes.Add(reader.GetString(0));
            return codes;
        }

        /// <summary>期货日线根数（筛池用）</summary>
        public Dictionary<string, int> GetFutureDailyCounts()
        {
            var counts = new Dictionary<string, int>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand("SELECT code, COUNT(*) FROM cy_future_daily GROUP BY code", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
            return counts;
        }

        /// <summary>期货日线（表里没有预算指标，OHLCV 返回后由 IndicatorCalculator.Fill 现算）</summary>
        public List<StockData> GetFutureData(string code, string? startDate = null, string? endDate = null, int limit = 10000)
        {
            var data = new List<StockData>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string sql = @"SELECT trade_date, open, high, low, close, volume
                   FROM cy_future_daily WHERE code = @code";
            if (!string.IsNullOrEmpty(startDate)) sql += " AND trade_date >= @startDate";
            if (!string.IsNullOrEmpty(endDate)) sql += " AND trade_date <= @endDate";
            sql += " ORDER BY trade_date ASC LIMIT @limit";

            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@limit", limit);
            if (!string.IsNullOrEmpty(startDate)) cmd.Parameters.AddWithValue("@startDate", startDate);
            if (!string.IsNullOrEmpty(endDate)) cmd.Parameters.AddWithValue("@endDate", endDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                data.Add(new StockData
                {
                    Date = ParseTradeDate(reader.GetString(0)),
                    Open = reader.GetDouble(1),
                    High = reader.GetDouble(2),
                    Low = reader.GetDouble(3),
                    Close = reader.GetDouble(4),
                    Volume = reader.GetInt64(5)
                });
            }
            return data;
        }


        /// <summary>防脏 trade_date 解析：兼容 "yyyyMMdd" / "yyyy-MM-dd" / "yyyyMMdd HH:mm:ss"（美股等脏数据），
        /// 全部失败降级 DateTime.Today，绝不抛异常把训练炸掉</summary>
        private static DateTime ParseTradeDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return DateTime.Today;
            string s = dateStr.Trim();

            // 先截前 8 位按 yyyyMMdd 解析（覆盖 "20120518 00:00:00" 这类脏格式）
            if (s.Length >= 8 &&
                DateTime.TryParseExact(s.Substring(0, 8), "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d1))
                return d1;

            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d2))
                return d2;

            if (DateTime.TryParse(s, out var d3))
                return d3;

            return DateTime.Today;   // 彻底认不出 → 降级今天，保证能开局
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

        /// <summary>性能索引（幂等，2026-09-28）：一次性创建后永久生效，
        /// 股票池 GROUP BY / 涨停票池 LAG / 各市场抽段查询全部从全表扫描变索引扫描。
        /// 首次执行约十几秒（建索引本身），之后每次启动仅毫秒级校验，可放心每次调用。</summary>
        public void EnsureIndexes()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            string[] ddls =
            {
                "CREATE INDEX IF NOT EXISTS idx_daily_code_date ON cy_daily(code, trade_date)",
                "CREATE INDEX IF NOT EXISTS idx_index_code_date ON cy_index_daily(code, trade_date)",
                "CREATE INDEX IF NOT EXISTS idx_future_code_date ON cy_future_daily(code, trade_date)",
                "CREATE INDEX IF NOT EXISTS idx_hk_code_date ON cy_hk_daily(code, trade_date)",
                "CREATE INDEX IF NOT EXISTS idx_us_code_date ON cy_us_daily(code, trade_date)",
                "CREATE INDEX IF NOT EXISTS idx_bond_code_date ON cy_bond_daily(code, trade_date)",
            };
            foreach (var ddl in ddls)
                new SqliteCommand(ddl, conn).ExecuteNonQuery();
        }

        /// <summary>建表（幂等，老库启动时自动升级）。每局结算写入一条。</summary>
        public void EnsureTrainingRecordTable()
        {
            EnsureIndexes();   // ★ 索引幂等：老库第一次启动自动补建，之后每次仅毫秒级校验

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
        public Dictionary<string, int> GetDailyBarCounts(bool excludeSt = false)
        {
            var counts = new Dictionary<string, int>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var sql = "SELECT d.code, COUNT(*) FROM cy_daily d JOIN cy_stock_info s ON d.code = s.code";
            if (excludeSt) sql += " WHERE s.is_st = 0 AND s.name NOT LIKE '%ST%'";
            sql += " GROUP BY d.code";
            var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
            return counts;
        }

        /// <summary>爆竹数量曲线数据：按结算顺序返回（时间, 累计爆竹）。
        /// 累计口径：10000 起步，每局盈亏额累加（盈利为正则加，亏损为负则减）</summary>
        /// <summary>爆竹数量曲线数据（带诊断输出）</summary>
        public List<(string time, double firecrackers)> GetFirecrackerCurve()
        {
            var list = new List<(string, double)>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            System.Diagnostics.Debug.WriteLine($"[DIAG] 曲线读取的数据库 = {_connectionString}");
            var cmd = new SqliteCommand(
                "SELECT id, train_time, profit_amount, final_firecrackers FROM cy_training_record ORDER BY id ASC", conn);
            double total = 10000;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                total += reader.GetDouble(2);
                list.Add((reader.GetString(1), total));
                System.Diagnostics.Debug.WriteLine(
                    $"[DIAG] 记录 id={reader.GetInt32(0)} 时间={reader.GetString(1)} 盈亏={reader.GetDouble(2):F0} 结算值={reader.GetDouble(3):F0} 累计={total:F0}");
            }
            return list;
        }

        /// <summary>首页顶部统计：训练场次 + 最新累计爆竹（无记录时返回默认值 10000）</summary>
        public (int gameCount, double latestFirecrackers) GetHomeSummary()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(
                "SELECT COUNT(*), COALESCE(10000 + (SELECT SUM(profit_amount) FROM cy_training_record), 10000) FROM cy_training_record", conn);
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
            // ★ 期货兜底：个股表查不到 → 查期货表（RB0 → 螺纹钢）
            if (result == null)
            {
                var cmd2 = new SqliteCommand("SELECT name FROM cy_future_daily WHERE code = @code LIMIT 1", conn);
                cmd2.Parameters.AddWithValue("@code", code);
                result = cmd2.ExecuteScalar();
            }
            // ★ 港股兜底：查港股表（00700 → 腾讯控股）
            if (result == null)
            {
                var cmd3 = new SqliteCommand("SELECT name FROM cy_hk_daily WHERE code = @code LIMIT 1", conn);
                cmd3.Parameters.AddWithValue("@code", code);
                result = cmd3.ExecuteScalar();
            }
            // ★ 美股兜底：查美股表（AAPL → 苹果）
            if (result == null)
            {
                var cmd4 = new SqliteCommand("SELECT name FROM cy_us_daily WHERE code = @code LIMIT 1", conn);
                cmd4.Parameters.AddWithValue("@code", code);
                result = cmd4.ExecuteScalar();
            }
            // ★ 可转债兜底：查转债表（110059 → 浦发转债）
            if (result == null)
            {
                var cmd5 = new SqliteCommand("SELECT name FROM cy_bond_daily WHERE code = @code LIMIT 1", conn);
                cmd5.Parameters.AddWithValue("@code", code);
                result = cmd5.ExecuteScalar();
            }
            return result == null ? "" : result.ToString() ?? "";
        }

        /// <summary>清空全部训练记录（首页"重置本金"按钮用）</summary>
        public void ResetTrainingRecords()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            new SqliteCommand("DELETE FROM cy_training_record", conn).ExecuteNonQuery();
        }

        /// <summary>最近 N 局训练记录（首页"训练记录"列表用，按时间倒序，排除破产重置记录）</summary>
        public List<(string stockName, string stockCode, string period, string time, double holdRate,
                     double heavyRate, double openWinRate, double intervalPct, double profitPct,
                     int elapsedSec, string startDate, string endDate)> GetRecentRecords(int limit = 50)
        {
            var list = new List<(string, string, string, string, double, double, double, double, double, int, string, string)>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(@"
                SELECT stock_name, stock_code, period, train_time,
                       hold_days, watch_days, heavy_hold_days, win_rate, interval_pct, profit_pct,
                       elapsed_sec, start_date, end_date
                FROM cy_training_record
                WHERE stock_code <> 'RESET'
                ORDER BY id DESC LIMIT @limit", conn);
            cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                double hold = reader.GetDouble(4), watch = reader.GetDouble(5);
                double total = hold + watch;
                double holdRate = total > 0 ? hold / total * 100 : 0;
                double heavyRate = total > 0 ? reader.GetDouble(6) / total * 100 : 0;
                string time = reader.IsDBNull(3) ? "" : reader.GetString(3);
                if (time.Length >= 16) time = time.Substring(5, 11);   // "MM-dd HH:mm"
                list.Add((reader.IsDBNull(0) ? "" : reader.GetString(0),
                          reader.IsDBNull(1) ? "" : reader.GetString(1),
                          reader.IsDBNull(2) ? "" : reader.GetString(2),
                          time, holdRate, heavyRate,
                          reader.GetDouble(7), reader.GetDouble(8), reader.GetDouble(9),
                          reader.GetInt32(10),
                          reader.IsDBNull(11) ? "" : reader.GetString(11),
                          reader.IsDBNull(12) ? "" : reader.GetString(12)));
            }
            return list;
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