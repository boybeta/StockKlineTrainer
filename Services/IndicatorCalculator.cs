using System;
using System.Collections.Generic;
using BaozhuKLineTrainer.Models;

namespace BaozhuKLineTrainer.Services
{
    /// <summary>
    /// 技术指标计算：MA / EMA / BOLL / VOL MA / MACD
    /// 注意：必须在完整序列上一次性计算（聚合后的周线要重新 Fill 一遍）。
    /// </summary>
    public static class IndicatorCalculator
    {
        public static void Fill(List<StockData> bars)
        {
            if (bars == null || bars.Count == 0) return;

            int n = bars.Count;
            var closes = new double[n];
            var vols = new double[n];
            for (int i = 0; i < n; i++)
            {
                closes[i] = bars[i].Close;
                vols[i] = bars[i].Volume;
            }

            // ===== 均线与布林 =====
            double[] ma5 = SMA(closes, 5);
            double[] ma10 = SMA(closes, 10);
            double[] ma20 = SMA(closes, 20);

            double[] ema5 = EMA(closes, 5);
            double[] ema10 = EMA(closes, 10);
            double[] ema20 = EMA(closes, 20);

            // 布林线：中轨=MA20，上下轨=±2倍标准差
            double[] bollMid = SMA(closes, 20);
            double[] bollUp = new double[n];
            double[] bollDn = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (i < 19)
                {
                    bollUp[i] = double.NaN;
                    bollDn[i] = double.NaN;
                    continue;
                }
                double mean = bollMid[i];
                double sumSq = 0;
                for (int j = 0; j < 20; j++)
                {
                    double diff = closes[i - j] - mean;
                    sumSq += diff * diff;
                }
                double std = Math.Sqrt(sumSq / 20);   // 总体标准差（同花顺/通达信用法）
                bollUp[i] = mean + 2 * std;
                bollDn[i] = mean - 2 * std;
            }

            // ===== 成交量均线 =====
            double[] volMa5 = SMA(vols, 5);
            double[] volMa10 = SMA(vols, 10);

            // ===== MACD（12,26,9），柱值 = 2×(DIF-DEA)，与主窗口计算口径一致 =====
            double[] emaFast = EMA(closes, 12);
            double[] emaSlow = EMA(closes, 26);
            double[] dif = new double[n];
            for (int i = 0; i < n; i++)
                dif[i] = emaFast[i] - emaSlow[i];
            double[] dea = EMA(dif, 9);

            // ===== 写回 =====
            for (int i = 0; i < n; i++)
            {
                bars[i].MA5 = ma5[i];
                bars[i].MA10 = ma10[i];
                bars[i].MA20 = ma20[i];

                bars[i].EMA5 = ema5[i];
                bars[i].EMA10 = ema10[i];
                bars[i].EMA20 = ema20[i];

                bars[i].BollUp = bollUp[i];
                bars[i].BollMid = bollMid[i];
                bars[i].BollDn = bollDn[i];

                bars[i].VolMA5 = volMa5[i];
                bars[i].VolMA10 = volMa10[i];

                bars[i].DIF = dif[i];
                bars[i].DEA = dea[i];
                bars[i].MACDHist = (dif[i] - dea[i]) * 2;
            }
        }

        /// <summary>简单移动平均，前 period-1 个为 NaN</summary>
        private static double[] SMA(double[] data, int period)
        {
            var result = new double[data.Length];
            double sum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                sum += data[i];
                if (i >= period) sum -= data[i - period];

                result[i] = i >= period - 1 ? sum / period : double.NaN;
            }
            return result;
        }

        /// <summary>指数移动平均，以首元素为种子（与 MainViewModel.CalculateEMA 口径一致）</summary>
        private static double[] EMA(double[] data, int period)
        {
            var result = new double[data.Length];
            double k = 2.0 / (period + 1);

            result[0] = data[0];
            for (int i = 1; i < data.Length; i++)
                result[i] = (data[i] - result[i - 1]) * k + result[i - 1];

            return result;
        }
    }
}