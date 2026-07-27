using StockKLineTrainer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockKLineTrainer.Services
{
    public class PatternDetector
    {
        /// <summary>
        /// 自动检测K线序列中的主要形态
        /// </summary>
        public PatternType DetectPattern(List<StockData> data)
        {
            if (data == null || data.Count < 30) return PatternType.Random;

            var closes = data.Select(d => d.Close).ToList();
            var highs = data.Select(d => d.High).ToList();
            var lows = data.Select(d => d.Low).ToList();

            // 找局部极值点
            var peaks = FindPeaks(highs, 3);
            var troughs = FindTroughs(lows, 3);

            // 检测各种形态
            if (DetectHeadAndShoulders(highs, peaks)) return PatternType.HeadAndShoulders;
            if (DetectInverseHeadAndShoulders(lows, troughs)) return PatternType.InverseHeadAndShoulders;
            if (DetectDoubleTop(highs, peaks)) return PatternType.DoubleTop;
            if (DetectDoubleBottom(lows, troughs)) return PatternType.DoubleBottom;
            if (DetectAscendingTriangle(data)) return PatternType.TriangleAscending;
            if (DetectDescendingTriangle(data)) return PatternType.TriangleDescending;
            if (DetectSymmetricalTriangle(data)) return PatternType.TriangleSymmetrical;
            if (DetectCupAndHandle(closes)) return PatternType.CupAndHandle;

            return PatternType.Random;
        }

        /// <summary>
        /// 找局部峰值
        /// </summary>
        private List<(int index, double value)> FindPeaks(List<double> data, int window)
        {
            var peaks = new List<(int, double)>();
            for (int i = window; i < data.Count - window; i++)
            {
                bool isPeak = true;
                for (int j = 1; j <= window; j++)
                {
                    if (data[i] <= data[i - j] || data[i] <= data[i + j])
                    {
                        isPeak = false;
                        break;
                    }
                }
                if (isPeak) peaks.Add((i, data[i]));
            }
            return peaks;
        }

        /// <summary>
        /// 找局部谷值
        /// </summary>
        private List<(int index, double value)> FindTroughs(List<double> data, int window)
        {
            var troughs = new List<(int, double)>();
            for (int i = window; i < data.Count - window; i++)
            {
                bool isTrough = true;
                for (int j = 1; j <= window; j++)
                {
                    if (data[i] >= data[i - j] || data[i] >= data[i + j])
                    {
                        isTrough = false;
                        break;
                    }
                }
                if (isTrough) troughs.Add((i, data[i]));
            }
            return troughs;
        }

        /// <summary>
        /// 检测头肩顶：三个峰值，中间最高
        /// </summary>
        private bool DetectHeadAndShoulders(List<double> highs, List<(int index, double value)> peaks)
        {
            if (peaks.Count < 3) return false;

            // 找连续三个峰值，中间最高，两边相近
            for (int i = 0; i < peaks.Count - 2; i++)
            {
                var left = peaks[i];
                var head = peaks[i + 1];
                var right = peaks[i + 2];

                double tolerance = (head.value - Math.Min(left.value, right.value)) * 0.15;

                if (head.value > left.value && head.value > right.value &&
                    Math.Abs(left.value - right.value) < tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检测头肩底
        /// </summary>
        private bool DetectInverseHeadAndShoulders(List<double> lows, List<(int index, double value)> troughs)
        {
            if (troughs.Count < 3) return false;

            for (int i = 0; i < troughs.Count - 2; i++)
            {
                var left = troughs[i];
                var head = troughs[i + 1];
                var right = troughs[i + 2];

                double tolerance = (Math.Max(left.value, right.value) - head.value) * 0.15;

                if (head.value < left.value && head.value < right.value &&
                    Math.Abs(left.value - right.value) < tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检测双顶
        /// </summary>
        private bool DetectDoubleTop(List<double> highs, List<(int index, double value)> peaks)
        {
            if (peaks.Count < 2) return false;

            for (int i = 0; i < peaks.Count - 1; i++)
            {
                double diff = Math.Abs(peaks[i].value - peaks[i + 1].value);
                double avg = (peaks[i].value + peaks[i + 1].value) / 2;

                if (diff / avg < 0.03) // 3%容差
                {
                    // 检查中间是否有明显下跌
                    int midStart = peaks[i].index;
                    int midEnd = peaks[i + 1].index;
                    var midLow = highs.Skip(midStart).Take(midEnd - midStart).Min();

                    if (midLow < peaks[i].value * 0.95)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检测双底
        /// </summary>
        private bool DetectDoubleBottom(List<double> lows, List<(int index, double value)> troughs)
        {
            if (troughs.Count < 2) return false;

            for (int i = 0; i < troughs.Count - 1; i++)
            {
                double diff = Math.Abs(troughs[i].value - troughs[i + 1].value);
                double avg = (troughs[i].value + troughs[i + 1].value) / 2;

                if (diff / avg < 0.03)
                {
                    int midStart = troughs[i].index;
                    int midEnd = troughs[i + 1].index;
                    var midHigh = lows.Skip(midStart).Take(midEnd - midStart).Max();

                    if (midHigh > troughs[i].value * 1.05)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检测上升三角形：高点水平，低点上升
        /// </summary>
        private bool DetectAscendingTriangle(List<StockData> data)
        {
            int n = Math.Min(data.Count, 30);
            var recent = data.Skip(data.Count - n).ToList();

            var highs = recent.Select(d => d.High).ToList();
            var lows = recent.Select(d => d.Low).ToList();

            // 高点趋于水平
            double highVariance = Variance(highs);
            // 低点上升趋势
            double lowSlope = LinearRegressionSlope(lows);

            return highVariance < 0.001 && lowSlope > 0;
        }

        /// <summary>
        /// 检测下降三角形
        /// </summary>
        private bool DetectDescendingTriangle(List<StockData> data)
        {
            int n = Math.Min(data.Count, 30);
            var recent = data.Skip(data.Count - n).ToList();

            var highs = recent.Select(d => d.High).ToList();
            var lows = recent.Select(d => d.Low).ToList();

            double lowVariance = Variance(lows);
            double highSlope = LinearRegressionSlope(highs);

            return lowVariance < 0.001 && highSlope < 0;
        }

        /// <summary>
        /// 检测对称三角形
        /// </summary>
        private bool DetectSymmetricalTriangle(List<StockData> data)
        {
            int n = Math.Min(data.Count, 30);
            var recent = data.Skip(data.Count - n).ToList();

            var highs = recent.Select(d => d.High).ToList();
            var lows = recent.Select(d => d.Low).ToList();

            double highSlope = LinearRegressionSlope(highs);
            double lowSlope = LinearRegressionSlope(lows);

            // 高点下降，低点上升，汇聚
            return highSlope < -0.001 && lowSlope > 0.001 &&
                   Math.Abs(highSlope + lowSlope) < 0.01;
        }

        /// <summary>
        /// 检测杯柄形态
        /// </summary>
        private bool DetectCupAndHandle(List<double> closes)
        {
            if (closes.Count < 40) return false;

            int cupSize = closes.Count / 2;
            var firstHalf = closes.Take(cupSize).ToList();
            var secondHalf = closes.Skip(cupSize).Take(cupSize / 2).ToList();

            // 杯形：U型底部
            double firstMin = firstHalf.Min();
            double secondMin = secondHalf.Min();
            double midPoint = closes[cupSize / 2];

            // 简单的U型判断
            return firstMin < midPoint && secondMin < midPoint &&
                   Math.Abs(firstMin - secondMin) / firstMin < 0.05;
        }

        // 辅助方法
        private double Variance(List<double> data)
        {
            double avg = data.Average();
            return data.Select(x => (x - avg) * (x - avg)).Average();
        }

        private double LinearRegressionSlope(List<double> data)
        {
            int n = data.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

            for (int i = 0; i < n; i++)
            {
                sumX += i;
                sumY += data[i];
                sumXY += i * data[i];
                sumX2 += i * i;
            }

            return (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        }
    }
}