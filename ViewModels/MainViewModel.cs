using ScottPlot;
using ScottPlot.WPF;
using StockKLineTrainer.Models;
using StockKLineTrainer.Services;
using StockKLineTrainer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace StockKLineTrainer
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _dbService;
        private List<StockData> _currentDataList = new();
        private int _totalBars = 270;
        private int _trainingBars = 120;
        private int _currentVisibleBars = 120;
        private bool _isTrainingMode = false;
        private bool _isAnswerRevealed = false;
        private bool _isInitializing = true;
        private readonly Random _random = new Random();

        // ===== 十字光标（替换旧的 Scatter 光标）=====
        private ScottPlot.Plottables.VerticalLine? _klineVLine;
        private ScottPlot.Plottables.HorizontalLine? _klineHLine;
        private ScottPlot.Plottables.VerticalLine? _volVLine;
        private ScottPlot.Plottables.VerticalLine? _macdVLine;

        // ===== 训练开始标记线 =====
        private double _startMarkerIndex = -1;   // -1 表示无标记

        private string? _selectedStock;
        public string? SelectedStock
        {
            get => _selectedStock;
            set
            {
                if (_selectedStock != value)
                {
                    _selectedStock = value;
                    OnPropertyChanged();
                    if (!_isInitializing) LoadData();
                }
            }
        }

        private DateTime _startDate = DateTime.Now.AddYears(-1);
        public DateTime StartDate
        {
            get => _startDate;
            set { _startDate = value; OnPropertyChanged(); }
        }

        private string _infoText = "";
        public string InfoText
        {
            get => _infoText;
            set { _infoText = value; OnPropertyChanged(); }
        }

        private string _trainingStatus = "浏览模式";
        public string TrainingStatus
        {
            get => _trainingStatus;
            set { _trainingStatus = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> StockList { get; set; } = new();
        public WpfPlot? KlinePlot { get; set; }
        public WpfPlot? VolPlot { get; set; }
        public WpfPlot? MacdPlot { get; set; }

        public ICommand RefreshCommand { get; }
        public ICommand RandomStockCommand { get; }
        public ICommand ToggleTrainingCommand { get; }
        public ICommand RevealAnswerCommand { get; }
        public ICommand NextTrainingCommand { get; }
        public ICommand HoldOrWatchCommand { get; }
        public ICommand NextStockCommand { get; }

        public MainViewModel()
        {
            _dbService = new DatabaseService(@"E:\baozhu\stockdata\cy_stock.db");

            RefreshCommand = new RelayCommand(_ => LoadData());
            RandomStockCommand = new RelayCommand(_ => LoadRandomStock());
            NextStockCommand = new RelayCommand(_ => LoadRandomStock());
            ToggleTrainingCommand = new RelayCommand(_ => ToggleTrainingMode());
            RevealAnswerCommand = new RelayCommand(_ => RevealAnswer(), _ => _isTrainingMode && !_isAnswerRevealed);
            NextTrainingCommand = new RelayCommand(_ => LoadRandomStock(), _ => _isTrainingMode);
            HoldOrWatchCommand = new RelayCommand(_ => OnHoldOrWatch(), _ => !_isAnswerRevealed && _currentVisibleBars < _totalBars);

            LoadStockList();
            _currentVisibleBars = _trainingBars;

            if (StockList.Count > 0)
            {
                _selectedStock = StockList[0];
                OnPropertyChanged(nameof(SelectedStock));
            }

            _isInitializing = false;
        }

        private void LoadStockList()
        {
            var stocks = _dbService.GetAllStockCodes();
            StockList.Clear();
            foreach (var s in stocks) StockList.Add(s);
            Debug.WriteLine($"[DIAG] LoadStockList: {stocks.Count} stocks loaded");
        }

        private void LoadRandomStock()
        {
            _startMarkerIndex = -1;   // 切股票时清除标记线

            if (StockList.Count == 0) return;
            if (StockList.Count == 1)
            {
                LoadData();
                return;
            }

            for (int attempt = 0; attempt < 10; attempt++)
            {
                string newStock = StockList[_random.Next(StockList.Count)];
                if (newStock == _selectedStock && StockList.Count > 1) continue;

                var allData = _dbService.GetStockData(newStock, startDate: "19900101", endDate: "20251231", limit: 10000);
                if (allData.Count == 0) continue;

                if (allData.Count >= _totalBars)
                {
                    int maxStart = allData.Count - _totalBars;
                    int startIndex = _random.Next(maxStart);
                    _startDate = allData[startIndex].Date;
                    _currentDataList = allData.Skip(startIndex).Take(_totalBars).ToList();

                    OnPropertyChanged(nameof(StartDate));
                    SelectedStock = newStock;
                    UpdatePriceChart();
                    UpdateVolChart();
                    UpdateMacdChart();
                    UpdateInfoBar();
                    return;
                }
            }

            MessageBox.Show($"无法找到 2026 年之前有足够历史数据（{_totalBars}根）的股票");
        }

        public void LoadData()
        {
            Debug.WriteLine($"[DIAG] LoadData called, SelectedStock={SelectedStock}");
            if (string.IsNullOrEmpty(SelectedStock)) return;

            string startDateStr = _startDate.ToString("yyyyMMdd");
            Debug.WriteLine($"[DIAG] Query startDate={startDateStr}");

            _currentDataList = _dbService.GetStockData(
                SelectedStock,
                startDate: startDateStr,
                endDate: "20251231",
                limit: _totalBars);

            Debug.WriteLine($"[DIAG] Data loaded: {_currentDataList?.Count ?? 0} bars");

            if (_currentDataList == null || _currentDataList.Count < _totalBars)
            {
                Debug.WriteLine($"[DIAG] Data insufficient, fetching recent {_totalBars} bars before 2026");
                var allData = _dbService.GetStockData(SelectedStock, endDate: "20251231", limit: 2000);

                if (allData.Count >= _totalBars)
                {
                    _currentDataList = allData.Skip(allData.Count - _totalBars).Take(_totalBars).ToList();
                    _startDate = _currentDataList[0].Date;
                    OnPropertyChanged(nameof(StartDate));
                    Debug.WriteLine($"[DIAG] Fetched recent {_currentDataList.Count} bars from {_startDate:yyyy-MM-dd}");
                }
                else if (allData.Count > 0)
                {
                    _currentDataList = allData;
                    _startDate = _currentDataList[0].Date;
                    OnPropertyChanged(nameof(StartDate));
                    Debug.WriteLine($"[DIAG] Warning: Stock only has {_currentDataList.Count} bars before 2026");
                }
            }

            if (_currentDataList == null || _currentDataList.Count == 0)
            {
                MessageBox.Show("未找到 2026 年之前的数据");
                return;
            }

            UpdatePriceChart();
            UpdateVolChart();
            UpdateMacdChart();
            UpdateInfoBar();
        }

        private void UpdatePriceChart()
        {
            Debug.WriteLine($"[DIAG] UpdatePriceChart called, KlinePlot={(KlinePlot == null ? "NULL" : "OK")}");
            if (KlinePlot == null) return;

            var dataList = _currentDataList;
            Debug.WriteLine($"[DIAG] dataList count={dataList?.Count ?? 0}");
            if (dataList == null || dataList.Count == 0) return;

            int windowStart = !_isAnswerRevealed ? Math.Max(0, _currentVisibleBars - _trainingBars) : 0;
            int windowEnd = !_isAnswerRevealed ? Math.Min(_currentVisibleBars, dataList.Count) : dataList.Count;
            int visibleCount = windowEnd - windowStart;

            var spOhlcList = dataList.Select(d => d.ToOHLC()).ToList();
            Debug.WriteLine($"[DIAG] spOhlcList count={spOhlcList.Count}, first={spOhlcList[0].Open}/{spOhlcList[0].High}/{spOhlcList[0].Low}/{spOhlcList[0].Close}");

            KlinePlot.Plot.Clear();
            Debug.WriteLine("[DIAG] Plot cleared");

            var candlestick = KlinePlot.Plot.Add.Candlestick(spOhlcList);
            Debug.WriteLine($"[DIAG] Candlestick added, Sequential={candlestick.Sequential}");

            candlestick.Sequential = true;
            candlestick.SymbolWidth = 0.7;
            candlestick.RisingColor = Color.FromHex("#FF3232");
            candlestick.FallingColor = Color.FromHex("#00A800");

            AddMALines(dataList, windowStart, visibleCount);
            KlinePlot.Plot.Legend.IsVisible = false;
            KlinePlot.Plot.Axes.Left.Label.Text = "价格";
            KlinePlot.Plot.Axes.Bottom.Label.Text = "";
            KlinePlot.Plot.Grid.XAxisStyle.IsVisible = false;

            SetupDateAxis(KlinePlot.Plot, dataList, windowStart, visibleCount);

            Debug.WriteLine($"[DIAG] Before SetLimitsX: XRange={KlinePlot.Plot.Axes.GetLimits().XRange}");
            KlinePlot.Plot.Axes.SetLimitsX(windowStart - 0.5, windowEnd - 0.5);
            KlinePlot.Plot.Axes.AutoScaleY();
            Debug.WriteLine($"[DIAG] After SetLimitsX: XRange={KlinePlot.Plot.Axes.GetLimits().XRange}, YRange={KlinePlot.Plot.Axes.GetLimits().YRange}");

            // ===== 十字光标：竖直 + 水平虚线 =====
            _klineVLine = KlinePlot.Plot.Add.VerticalLine(0);
            _klineVLine.Color = Color.FromHex("#000000");
            _klineVLine.LineWidth = 0.5f;
            _klineVLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _klineVLine.IsVisible = false;

            _klineHLine = KlinePlot.Plot.Add.HorizontalLine(0);
            _klineHLine.Color = Color.FromHex("#000000");
            _klineHLine.LineWidth = 0.5f;
            _klineHLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _klineHLine.IsVisible = false;

            // 优化网格线
            KlinePlot.Plot.Grid.MajorLineColor = Color.FromHex("#F0F0F0");
            KlinePlot.Plot.Grid.MajorLineWidth = 0.5f;

            // 统一左右边距
            KlinePlot.Plot.Axes.Left.MinimumSize = 50;
            KlinePlot.Plot.Axes.Right.MinimumSize = 50;

            // ===== 训练开始标记线（蓝色竖虚线）=====
            if (_isTrainingMode && _startMarkerIndex >= 0)
            {
                var marker = KlinePlot.Plot.Add.VerticalLine(_startMarkerIndex);
                marker.Color = Color.FromHex("#2196F3");      // 蓝色
                marker.LineWidth = 2;
                marker.LinePattern = LinePattern.Dashed;
            }

            KlinePlot.Refresh();
            Debug.WriteLine("[DIAG] Refresh called");
        }

        private void AddMALines(List<StockData> dataList, int windowStart, int count)
        {
            double[] xs = Enumerable.Range(windowStart, count).Select(i => (double)i).ToArray();

            double[] ma5 = dataList.Skip(windowStart).Take(count).Select(d => d.MA5 ?? double.NaN).ToArray();
            double[] ma10 = dataList.Skip(windowStart).Take(count).Select(d => d.MA10 ?? double.NaN).ToArray();
            double[] ma15 = dataList.Skip(windowStart).Take(count).Select(d => d.MA20 ?? double.NaN).ToArray();

            if (ma5.All(double.IsNaN))
                ma5 = CalculateMA(dataList.Skip(windowStart).Take(count).Select(d => d.Close).ToArray(), 5);
            if (ma10.All(double.IsNaN))
                ma10 = CalculateMA(dataList.Skip(windowStart).Take(count).Select(d => d.Close).ToArray(), 10);
            if (ma15.All(double.IsNaN))
                ma15 = CalculateMA(dataList.Skip(windowStart).Take(count).Select(d => d.Close).ToArray(), 15);

            PlotMALine(xs, ma5, "#FFD700", "MA5");
            PlotMALine(xs, ma10, "#00BFFF", "MA10");
            PlotMALine(xs, ma15, "#FF69B4", "MA15");
        }

        private void PlotMALine(double[] xs, double[] ma, string colorHex, string label)
        {
            if (ma.Length == 0) return;
            var validXs = new List<double>();
            var validYs = new List<double>();

            for (int i = 0; i < ma.Length; i++)
            {
                if (!double.IsNaN(ma[i]))
                {
                    validXs.Add(xs[i]);
                    validYs.Add(ma[i]);
                }
            }

            if (validXs.Count > 0)
            {
                var line = KlinePlot!.Plot.Add.Scatter(validXs.ToArray(), validYs.ToArray());
                line.Color = Color.FromHex(colorHex);
                line.LineWidth = 1.5f;
                line.MarkerSize = 0;
                line.LegendText = label;
            }
        }

        private void UpdateVolChart()
        {
            if (VolPlot == null) return;

            var dataList = _currentDataList;
            if (dataList == null || dataList.Count == 0) return;

            int windowStart = !_isAnswerRevealed ? Math.Max(0, _currentVisibleBars - _trainingBars) : 0;
            int windowEnd = !_isAnswerRevealed ? Math.Min(_currentVisibleBars, dataList.Count) : dataList.Count;
            int visibleCount = windowEnd - windowStart;

            VolPlot.Plot.Clear();

            var bars = new List<ScottPlot.Bar>();
            for (int i = 0; i < visibleCount; i++)
            {
                int dataIndex = windowStart + i;
                bool isRising = dataList[dataIndex].Close >= dataList[dataIndex].Open;
                bars.Add(new ScottPlot.Bar
                {
                    Position = dataIndex,
                    Value = dataList[dataIndex].Volume,
                    FillColor = isRising
                        ? Color.FromHex("#FF3232")
                        : Color.FromHex("#00A800")
                });
            }

            VolPlot.Plot.Add.Bars(bars);

            var volMA5 = dataList.Skip(windowStart).Take(visibleCount).Select(d => d.VolMA5 ?? double.NaN).ToArray();
            var volMA10 = dataList.Skip(windowStart).Take(visibleCount).Select(d => d.VolMA10 ?? double.NaN).ToArray();

            double[] xs = Enumerable.Range(windowStart, visibleCount).Select(i => (double)i).ToArray();

            if (!volMA5.All(double.IsNaN))
            {
                var line5 = VolPlot.Plot.Add.Scatter(xs, volMA5);
                line5.Color = Color.FromHex("#FFD700");
                line5.LineWidth = 1;
                line5.MarkerSize = 0;
            }

            if (!volMA10.All(double.IsNaN))
            {
                var line10 = VolPlot.Plot.Add.Scatter(xs, volMA10);
                line10.Color = Color.FromHex("#00BFFF");
                line10.LineWidth = 1;
                line10.MarkerSize = 0;
            }

            VolPlot.Plot.Axes.Left.IsVisible = false;
            VolPlot.Plot.Axes.Right.IsVisible = true;
            VolPlot.Plot.Axes.Right.Label.Text = "";
            VolPlot.Plot.Axes.Bottom.IsVisible = false;

            // ===== 竖直虚线光标 =====
            _volVLine = VolPlot.Plot.Add.VerticalLine(0);
            _volVLine.Color = Color.FromHex("#000000");
            _volVLine.LineWidth = 0.5f;
            _volVLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _volVLine.IsVisible = false;

            VolPlot.Plot.Axes.SetLimitsX(windowStart - 0.5, windowEnd - 0.5);
            VolPlot.Plot.Axes.AutoScaleY();

            VolPlot.Plot.Grid.MajorLineColor = Color.FromHex("#F0F0F0");
            VolPlot.Plot.Grid.MajorLineWidth = 0.5f;

            VolPlot.Plot.Axes.Left.MinimumSize = 50;
            VolPlot.Plot.Axes.Right.MinimumSize = 50;

            VolPlot.Refresh();
        }

        private void UpdateMacdChart()
        {
            if (MacdPlot == null) return;

            var dataList = _currentDataList;
            if (dataList == null || dataList.Count == 0) return;

            int windowStart = !_isAnswerRevealed ? Math.Max(0, _currentVisibleBars - _trainingBars) : 0;
            int windowEnd = !_isAnswerRevealed ? Math.Min(_currentVisibleBars, dataList.Count) : dataList.Count;
            int visibleCount = windowEnd - windowStart;

            var visibleData = dataList.Skip(windowStart).Take(visibleCount).ToList();

            var difList = visibleData.Select(d => d.DIF).ToList();
            var deaList = visibleData.Select(d => d.DEA).ToList();
            var macdHistList = visibleData.Select(d => d.MACDHist).ToList();

            double[] xs = Enumerable.Range(windowStart, visibleCount).Select(i => (double)i).ToArray();
            double[] dif, dea, macd;

            if (difList.All(v => v.HasValue))
            {
                dif = difList.Select(v => v!.Value).ToArray();
                dea = deaList.Select(v => v!.Value).ToArray();
                macd = macdHistList.Select(v => v!.Value).ToArray();
            }
            else
            {
                double[] closes = visibleData.Select(d => d.Close).ToArray();
                (dif, dea, macd) = CalculateMACD(closes);
            }

            MacdPlot.Plot.Clear();

            var macdBars = new List<ScottPlot.Bar>();
            for (int i = 0; i < macd.Length && i < visibleCount; i++)
            {
                macdBars.Add(new ScottPlot.Bar
                {
                    Position = windowStart + i,
                    Value = macd[i],
                    FillColor = macd[i] >= 0
                        ? Color.FromHex("#FF3232")
                        : Color.FromHex("#00A800")
                });
            }
            MacdPlot.Plot.Add.Bars(macdBars);

            PlotMacdLine(xs, dif, "#FFD700", "DIF");
            PlotMacdLine(xs, dea, "#00BFFF", "DEA");

            MacdPlot.Plot.Axes.Left.IsVisible = false;
            MacdPlot.Plot.Axes.Right.IsVisible = true;
            MacdPlot.Plot.Axes.Right.Label.Text = "";

            SetupDateAxis(MacdPlot.Plot, dataList, windowStart, visibleCount);

            // ===== 竖直虚线光标 =====
            _macdVLine = MacdPlot.Plot.Add.VerticalLine(0);
            _macdVLine.Color = Color.FromHex("#000000");
            _macdVLine.LineWidth = 0.5f;
            _macdVLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _macdVLine.IsVisible = false;

            MacdPlot.Plot.Axes.SetLimitsX(windowStart - 0.5, windowEnd - 0.5);

            double macdMax = macd.Length > 0 ? macd.Max() : 0;
            double macdMin = macd.Length > 0 ? macd.Min() : 0;
            double difMax = dif.Length > 0 ? dif.Max() : 0;
            double difMin = dif.Length > 0 ? dif.Min() : 0;

            double yMax = Math.Max(Math.Abs(macdMax), Math.Abs(difMax));
            double yMin = -yMax;

            if (yMax < 0.3) yMax = 0.3;

            MacdPlot.Plot.Axes.SetLimitsY(yMin * 1.2, yMax * 1.2);

            MacdPlot.Plot.Grid.MajorLineColor = Color.FromHex("#F0F0F0");
            MacdPlot.Plot.Grid.MajorLineWidth = 0.5f;

            MacdPlot.Plot.Axes.Left.MinimumSize = 50;
            MacdPlot.Plot.Axes.Right.MinimumSize = 50;

            MacdPlot.Refresh();
        }

        private void PlotMacdLine(double[] xs, double[] data, string colorHex, string label)
        {
            var validXs = new List<double>();
            var validYs = new List<double>();

            for (int i = 0; i < data.Length; i++)
            {
                if (!double.IsNaN(data[i]))
                {
                    validXs.Add(xs[i]);
                    validYs.Add(data[i]);
                }
            }

            if (validXs.Count > 0)
            {
                var line = MacdPlot!.Plot.Add.Scatter(validXs.ToArray(), validYs.ToArray());
                line.Color = Color.FromHex(colorHex);
                line.LineWidth = 1.5f;
                line.MarkerSize = 0;
                line.LegendText = label;
            }
        }

        private void SetupDateAxis(Plot plot, List<StockData> dataList, int windowStart, int visibleCount)
        {
            if (dataList == null || dataList.Count == 0) return;

            int windowEnd = Math.Min(windowStart + visibleCount, dataList.Count);
            int actualVisible = windowEnd - windowStart;
            if (actualVisible <= 0) return;

            var bottomAxis = plot.Axes.Bottom;

            int tickCount = Math.Min(8, actualVisible);
            int interval = Math.Max(1, actualVisible / tickCount);

            var tickPositions = new List<double>();
            var tickLabels = new List<string>();

            for (int i = 0; i < actualVisible; i += interval)
            {
                int dataIndex = windowStart + i;
                tickPositions.Add(dataIndex);
                tickLabels.Add(dataList[dataIndex].Date.ToString("MM-dd"));
            }

            if (tickPositions.Count == 0 || tickPositions.Last() != windowEnd - 1)
            {
                tickPositions.Add(windowEnd - 1);
                tickLabels.Add(dataList[windowEnd - 1].Date.ToString("MM-dd"));
            }

            var tickGen = new ScottPlot.TickGenerators.NumericManual(
                tickPositions.ToArray(),
                tickLabels.ToArray()
            );
            bottomAxis.TickGenerator = tickGen;

            bottomAxis.TickLabelStyle.Rotation = -30;
            bottomAxis.TickLabelStyle.Alignment = Alignment.MiddleRight;
        }

        private void UpdateInfoBar()
        {
            if (_currentDataList == null || _currentDataList.Count == 0) return;

            int displayIndex = !_isAnswerRevealed
                ? Math.Min(_currentVisibleBars, _currentDataList.Count) - 1
                : _currentDataList.Count - 1;
            if (displayIndex < 0) displayIndex = 0;

            var last = _currentDataList[displayIndex];
            var prev = displayIndex > 0 ? _currentDataList[displayIndex - 1] : last;
            double change = last.Close - prev.Close;
            double changePct = prev.Close != 0 ? (change / prev.Close) * 100 : 0;

            InfoText = $"{SelectedStock}  {last.Date:yyyy-MM-dd}  " +
                       $"开:{last.Open:F2} 高:{last.High:F2} 低:{last.Low:F2} 收:{last.Close:F2}  " +
                       $"涨跌:{change:F2} ({changePct:F2}%)  量:{last.Volume:N0}";
        }

        private void ToggleTrainingMode()
        {
            if ((DateTime.Now - _lastToggleTime).TotalMilliseconds < 500) return;
            _lastToggleTime = DateTime.Now;

            _isTrainingMode = !_isTrainingMode;
            _isAnswerRevealed = false;
            _currentVisibleBars = _trainingBars;

            if (_isTrainingMode)
            {
                TrainingStatus = $"训练模式：预测后{_totalBars - _trainingBars}根K线走势";

                if (!string.IsNullOrEmpty(SelectedStock))
                {
                    var allData = _dbService.GetStockData(SelectedStock, limit: 2000);

                    if (allData.Count >= _totalBars)
                    {
                        _currentDataList = allData.Skip(allData.Count - _totalBars).Take(_totalBars).ToList();
                        _startDate = _currentDataList[0].Date;
                        OnPropertyChanged(nameof(StartDate));
                        UpdatePriceChart();
                        UpdateVolChart();
                        UpdateMacdChart();
                        UpdateInfoBar();
                    }
                    else
                    {
                        MessageBox.Show($"当前股票历史数据不足 {_totalBars} 根，无法训练");
                        _isTrainingMode = false;
                        TrainingStatus = "浏览模式";
                    }
                }
            }
            else
            {
                _startMarkerIndex = -1;   // 退出训练清除标记线
                TrainingStatus = "浏览模式";
                LoadData();
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private void RevealAnswer()
        {
            _isAnswerRevealed = true;
            TrainingStatus = "答案已揭示";
            UpdatePriceChart();
            UpdateVolChart();
            UpdateMacdChart();
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnHoldOrWatch()
        {
            if (_isAnswerRevealed) return;

            bool wasTraining = _isTrainingMode;

            if (!_isTrainingMode)
            {
                _isTrainingMode = true;
                _isAnswerRevealed = false;
                _currentVisibleBars = _trainingBars;

                if (_currentDataList == null || _currentDataList.Count == 0)
                {
                    MessageBox.Show("当前没有数据，无法进入训练模式");
                    _isTrainingMode = false;
                    return;
                }

                // 记录开始标记线位置：当前可见最右端（下一根推进的起点）
                _startMarkerIndex = _currentVisibleBars - 0.5;
            }
            else
            {
                if (_currentVisibleBars >= _totalBars) return;
                _currentVisibleBars++;
            }

            UpdatePriceChart();
            UpdateVolChart();
            UpdateMacdChart();
            UpdateInfoBar();

            int remaining = _totalBars - _currentVisibleBars;
            if (remaining > 0)
                TrainingStatus = $"训练模式：已推进 {_currentVisibleBars}/{_totalBars}，剩余 {remaining} 根";
            else
                TrainingStatus = "训练模式：已到最后一根，请揭示答案";

            CommandManager.InvalidateRequerySuggested();
        }

        private DateTime _lastSyncTime = DateTime.MinValue;
        private DateTime _lastToggleTime = DateTime.MinValue;

        public void SetupChartLinkage()
        {
            if (KlinePlot != null)
            {
                KlinePlot.UserInputProcessor.IsEnabled = false;
                KlinePlot.MouseWheel += (s, e) => SyncChartsFrom(KlinePlot);
                KlinePlot.MouseUp += (s, e) => SyncChartsFrom(KlinePlot);
                KlinePlot.MouseMove += (s, e) => UpdateCrosshair(KlinePlot, e);
                KlinePlot.MouseEnter += (s, e) => KlinePlot.Cursor = System.Windows.Input.Cursors.Cross;
                KlinePlot.MouseLeave += (s, e) => { KlinePlot.Cursor = System.Windows.Input.Cursors.Arrow; HideCrosshair(); };
            }
            if (VolPlot != null)
            {
                VolPlot.UserInputProcessor.IsEnabled = false;
                VolPlot.MouseWheel += (s, e) => SyncChartsFrom(VolPlot);
                VolPlot.MouseUp += (s, e) => SyncChartsFrom(VolPlot);
                VolPlot.MouseMove += (s, e) => UpdateCrosshair(VolPlot, e);
                VolPlot.MouseEnter += (s, e) => VolPlot.Cursor = System.Windows.Input.Cursors.Cross;
                VolPlot.MouseLeave += (s, e) => { VolPlot.Cursor = System.Windows.Input.Cursors.Arrow; HideCrosshair(); };
            }
            if (MacdPlot != null)
            {
                MacdPlot.UserInputProcessor.IsEnabled = false;
                MacdPlot.MouseWheel += (s, e) => SyncChartsFrom(MacdPlot);
                MacdPlot.MouseUp += (s, e) => SyncChartsFrom(MacdPlot);
                MacdPlot.MouseMove += (s, e) => UpdateCrosshair(MacdPlot, e);
                MacdPlot.MouseEnter += (s, e) => MacdPlot.Cursor = System.Windows.Input.Cursors.Cross;
                MacdPlot.MouseLeave += (s, e) => { MacdPlot.Cursor = System.Windows.Input.Cursors.Arrow; HideCrosshair(); };
            }
        }

        private void UpdateCrosshair(WpfPlot source, System.Windows.Input.MouseEventArgs e)
        {
            if (_klineVLine == null) return;

            var pos = e.GetPosition(source);
            Pixel mousePixel = new(pos.X, pos.Y);
            Coordinates mouseCoords = source.Plot.GetCoordinates(mousePixel);
            double x = mouseCoords.X;

            var dataList = _currentDataList;
            if (dataList == null || dataList.Count == 0) return;

            int windowStart = !_isAnswerRevealed ? Math.Max(0, _currentVisibleBars - _trainingBars) : 0;
            int windowEnd = !_isAnswerRevealed ? Math.Min(_currentVisibleBars, dataList.Count) : dataList.Count;

            if (x < windowStart - 0.5 || x > windowEnd - 0.5)
            {
                HideCrosshair();
                return;
            }

            // 同步三个图的竖直虚线
            if (_klineVLine != null) { _klineVLine.X = x; _klineVLine.IsVisible = true; }
            if (_volVLine != null) { _volVLine.X = x; _volVLine.IsVisible = true; }
            if (_macdVLine != null) { _macdVLine.X = x; _macdVLine.IsVisible = true; }

            // K线图的水平虚线（仅当鼠标在K线图上时）
            if (source == KlinePlot && _klineHLine != null)
            {
                _klineHLine.Y = mouseCoords.Y;
                _klineHLine.IsVisible = true;
            }
            else if (_klineHLine != null)
            {
                _klineHLine.IsVisible = false;
            }

            KlinePlot?.Refresh();
            VolPlot?.Refresh();
            MacdPlot?.Refresh();
        }

        private void HideCrosshair()
        {
            if (_klineVLine != null) _klineVLine.IsVisible = false;
            if (_klineHLine != null) _klineHLine.IsVisible = false;
            if (_volVLine != null) _volVLine.IsVisible = false;
            if (_macdVLine != null) _macdVLine.IsVisible = false;

            KlinePlot?.Refresh();
            VolPlot?.Refresh();
            MacdPlot?.Refresh();
        }

        private void SyncChartsFrom(WpfPlot source)
        {
            if ((DateTime.Now - _lastSyncTime).TotalMilliseconds < 50) return;
            _lastSyncTime = DateTime.Now;

            var xMin = source.Plot.Axes.GetLimits().XRange.Min;
            var xMax = source.Plot.Axes.GetLimits().XRange.Max;

            if (source != KlinePlot && KlinePlot != null)
            {
                KlinePlot.Plot.Axes.SetLimitsX(xMin, xMax);
                KlinePlot.Refresh();
            }
            if (source != VolPlot && VolPlot != null)
            {
                VolPlot.Plot.Axes.SetLimitsX(xMin, xMax);
                VolPlot.Refresh();
            }
            if (source != MacdPlot && MacdPlot != null)
            {
                MacdPlot.Plot.Axes.SetLimitsX(xMin, xMax);
                MacdPlot.Refresh();
            }
        }

        private double[] CalculateMA(double[] data, int period)
        {
            if (data.Length < period) return Array.Empty<double>();
            var result = new double[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                if (i < period - 1)
                {
                    result[i] = double.NaN;
                    continue;
                }
                double sum = 0;
                for (int j = 0; j < period; j++)
                    sum += data[i - j];
                result[i] = sum / period;
            }
            return result;
        }

        private (double[] dif, double[] dea, double[] macd) CalculateMACD(double[] data,
            int fast = 12, int slow = 26, int signal = 9)
        {
            if (data.Length < slow) return (Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>());

            double[] emaFast = CalculateEMA(data, fast);
            double[] emaSlow = CalculateEMA(data, slow);

            int validLength = Math.Min(emaFast.Length, emaSlow.Length);
            double[] dif = new double[validLength];
            for (int i = 0; i < validLength; i++)
                dif[i] = emaFast[i] - emaSlow[i];

            double[] dea = CalculateEMA(dif, signal);
            int macdLength = Math.Min(dif.Length, dea.Length);
            double[] macd = new double[macdLength];
            for (int i = 0; i < macdLength; i++)
                macd[i] = (dif[i] - dea[i]) * 2;

            return (dif, dea, macd);
        }

        private double[] CalculateEMA(double[] data, int period)
        {
            var result = new double[data.Length];
            double multiplier = 2.0 / (period + 1);

            result[0] = data[0];
            for (int i = 1; i < data.Length; i++)
                result[i] = (data[i] - result[i - 1]) * multiplier + result[i - 1];

            return result;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}