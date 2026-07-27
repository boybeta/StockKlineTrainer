using Microsoft.Data.Sqlite;
using StockKLineTrainer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockKLineTrainer.Services
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
    }
}