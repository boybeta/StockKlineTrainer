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
        private int _totalBars = 120;
        private int _trainingBars = 80;
        private int _currentVisibleBars = 80;
        private bool _isTrainingMode = false;
        private bool _isAnswerRevealed = false;
        private bool _isInitializing = true;
        private readonly Random _random = new Random();

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
        private ScottPlot.Plottables.Scatter? _klineCursor;
        private ScottPlot.Plottables.Scatter? _volCursor;
        private ScottPlot.Plottables.Scatter? _macdCursor;

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
            HoldOrWatchCommand = new RelayCommand(_ => OnHoldOrWatch(), _ => _isTrainingMode && !_isAnswerRevealed && _currentVisibleBars < _totalBars);

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
            if (StockList.Count == 0) return;
            if (StockList.Count == 1)
            {
                LoadData();
                return;
            }

            string newStock;
            do
            {
                newStock = StockList[_random.Next(StockList.Count)];
            } while (newStock == _selectedStock);

            var allData = _dbService.GetStockData(newStock, startDate: "19900101", limit: 10000);
            if (allData.Count == 0) return;

            if (allData.Count > _totalBars)
            {
                int maxStart = allData.Count - _totalBars;
                int startIndex = _random.Next(maxStart);
                _startDate = allData[startIndex].Date;
            }
            else
            {
                _startDate = allData[0].Date;
            }

            OnPropertyChanged(nameof(StartDate));
            SelectedStock = newStock;
        }

        public void LoadData()
        {
            Debug.WriteLine($"[DIAG] LoadData called, SelectedStock={SelectedStock}");
            if (string.IsNullOrEmpty(SelectedStock)) return;

            string startDateStr = _startDate.ToString("yyyyMMdd");
            Debug.WriteLine($"[DIAG] Query startDate={startDateStr}");
            _currentDataList = _dbService.GetStockData(SelectedStock, startDate: startDateStr, limit: _totalBars);
            Debug.WriteLine($"[DIAG] Data loaded: {_currentDataList?.Count ?? 0} bars");

            if (_currentDataList == null || _currentDataList.Count == 0)
            {
                MessageBox.Show("未找到数据");
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

            int visibleCount = _isTrainingMode && !_isAnswerRevealed
                ? Math.Min(_currentVisibleBars, dataList.Count)
                : dataList.Count;
            Debug.WriteLine($"[DIAG] visibleCount={visibleCount}, isTrainingMode={_isTrainingMode}");

            AddMALines(dataList, visibleCount);

            KlinePlot.Plot.Axes.Left.Label.Text = "价格";
            KlinePlot.Plot.Axes.Bottom.Label.Text = "";
            KlinePlot.Plot.Grid.XAxisStyle.IsVisible = false;

            SetupDateAxis(KlinePlot.Plot, dataList, visibleCount);

            Debug.WriteLine($"[DIAG] Before SetLimitsX: XRange={KlinePlot.Plot.Axes.GetLimits().XRange}");
            KlinePlot.Plot.Axes.SetLimitsX(-0.5, visibleCount - 0.5);
            KlinePlot.Plot.Axes.AutoScaleY();
            Debug.WriteLine($"[DIAG] After SetLimitsX: XRange={KlinePlot.Plot.Axes.GetLimits().XRange}, YRange={KlinePlot.Plot.Axes.GetLimits().YRange}");

            // 添加十字光标
            double[] cursorX = new double[] { 0, 0 };
            double[] cursorY = new double[] { -99999, 99999 };
            _klineCursor = KlinePlot.Plot.Add.Scatter(cursorX, cursorY);
            _klineCursor.Color = Color.FromHex("#999999");
            _klineCursor.LineWidth = 1;
            _klineCursor.MarkerSize = 0;
            _klineCursor.IsVisible = false;

            // 优化网格线
            KlinePlot.Plot.Grid.MajorLineColor = Color.FromHex("#F0F0F0");
            KlinePlot.Plot.Grid.MajorLineWidth = 0.5f;

            // ===== 统一左右边距 =====
            KlinePlot.Plot.Axes.Left.MinimumSize = 50;
            KlinePlot.Plot.Axes.Right.MinimumSize = 50;

            KlinePlot.Refresh();
            Debug.WriteLine("[DIAG] Refresh called");
        }

        private void AddMALines(List<StockData> dataList, int count)
        {
            double[] xs = Enumerable.Range(0, count).Select(i => (double)i).ToArray();

            double[] ma5 = dataList.Take(count).Select(d => d.MA5 ?? double.NaN).ToArray();
            double[] ma10 = dataList.Take(count).Select(d => d.MA10 ?? double.NaN).ToArray();
            double[] ma15 = dataList.Take(count).Select(d => d.MA20 ?? double.NaN).ToArray();

            if (ma5.All(double.IsNaN))
                ma5 = CalculateMA(dataList.Take(count).Select(d => d.Close).ToArray(), 5);
            if (ma10.All(double.IsNaN))
                ma10 = CalculateMA(dataList.Take(count).Select(d => d.Close).ToArray(), 10);
            if (ma15.All(double.IsNaN))
                ma15 = CalculateMA(dataList.Take(count).Select(d => d.Close).ToArray(), 15);

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

            int visibleCount = _isTrainingMode && !_isAnswerRevealed
                ? Math.Min(_currentVisibleBars, dataList.Count)
                : dataList.Count;

            VolPlot.Plot.Clear();

            // 添加柱状图
            var bars = new List<ScottPlot.Bar>();
            for (int i = 0; i < visibleCount; i++)
            {
                bool isRising = dataList[i].Close >= dataList[i].Open;
                bars.Add(new ScottPlot.Bar
                {
                    Position = i,
                    Value = dataList[i].Volume,
                    FillColor = isRising
                        ? Color.FromHex("#FF3232")
                        : Color.FromHex("#00A800")
                });
            }

            VolPlot.Plot.Add.Bars(bars);

            // 添加VOL均线
            var volMA5 = dataList.Take(visibleCount).Select(d => d.VolMA5 ?? double.NaN).ToArray();
            var volMA10 = dataList.Take(visibleCount).Select(d => d.VolMA10 ?? double.NaN).ToArray();

            double[] xs = Enumerable.Range(0, visibleCount).Select(i => (double)i).ToArray();

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

            // Y轴放右侧，左侧隐藏但占位
            VolPlot.Plot.Axes.Left.IsVisible = false;
            VolPlot.Plot.Axes.Right.IsVisible = true;
            VolPlot.Plot.Axes.Right.Label.Text = "";

            // 隐藏底部X轴标签
            VolPlot.Plot.Axes.Bottom.IsVisible = false;

            // 添加十字光标
            double[] volCursorX = new double[] { 0, 0 };
            double[] volCursorY = new double[] { -99999, 99999 };
            _volCursor = VolPlot.Plot.Add.Scatter(volCursorX, volCursorY);
            _volCursor.Color = Color.FromHex("#999999");
            _volCursor.LineWidth = 1;
            _volCursor.MarkerSize = 0;
            _volCursor.IsVisible = false;

            // 设置X轴范围
            VolPlot.Plot.Axes.SetLimitsX(-0.5, visibleCount - 0.5);
            VolPlot.Plot.Axes.AutoScaleY();

            // 优化网格线
            VolPlot.Plot.Grid.MajorLineColor = Color.FromHex("#F0F0F0");
            VolPlot.Plot.Grid.MajorLineWidth = 0.5f;

            // ===== 统一左右边距 =====
            VolPlot.Plot.Axes.Left.MinimumSize = 50;
            VolPlot.Plot.Axes.Right.MinimumSize = 50;

            VolPlot.Refresh();
        }

        private void UpdateMacdChart()
        {
            if (MacdPlot == null) return;

            var dataList = _currentDataList;
            if (dataList == null || dataList.Count == 0) return;

            int visibleCount = _isTrainingMode && !_isAnswerRevealed
                ? Math.Min(_currentVisibleBars, dataList.Count)
                : dataList.Count;

            var visibleData = dataList.Take(visibleCount).ToList();

            var difList = visibleData.Select(d => d.DIF).ToList();
            var deaList = visibleData.Select(d => d.DEA).ToList();
            var macdHistList = visibleData.Select(d => d.MACDHist).ToList();

            double[] xs = Enumerable.Range(0, visibleCount).Select(i => (double)i).ToArray();
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

            // MACD柱状图
            var macdBars = new List<ScottPlot.Bar>();
            for (int i = 0; i < macd.Length && i < visibleCount; i++)
            {
                macdBars.Add(new ScottPlot.Bar
                {
                    Position = i,
                    Value = macd[i],
                    FillColor = macd[i] >= 0
                        ? Color.FromHex("#FF3232")
                        : Color.FromHex("#00A800")
                });
            }
            MacdPlot.Plot.Add.Bars(macdBars);

            // DIF/DEA线
            PlotMacdLine(xs, dif, "#FFD700", "DIF");
            PlotMacdLine(xs, dea, "#00BFFF", "DEA");

            // Y轴放右侧，左侧隐藏但占位
            MacdPlot.Plot.Axes.Left.IsVisible = false;
            MacdPlot.Plot.Axes.Right.IsVisible = true;
            MacdPlot.Plot.Axes.Right.Label.Text = "";

            // 设置X轴日期
            SetupDateAxis(MacdPlot.Plot, dataList, visibleCount);

            // 添加十字光标
            double[] macdCursorX = new double[] { 0, 0 };
            double[] macdCursorY = new double[] { -99999, 99999 };
            _macdCursor = MacdPlot.Plot.Add.Scatter(macdCursorX, macdCursorY);
            _macdCursor.Color = Color.FromHex("#999999");
            _macdCursor.LineWidth = 1;
            _macdCursor.MarkerSize = 0;
            _macdCursor.IsVisible = false;

            // 设置X轴范围
            MacdPlot.Plot.Axes.SetLimitsX(-0.5, visibleCount - 0.5);

            // 计算Y轴范围
            double macdMax = macd.Length > 0 ? macd.Max() : 0;
            double macdMin = macd.Length > 0 ? macd.Min() : 0;
            double difMax = dif.Length > 0 ? dif.Max() : 0;
            double difMin = dif.Length > 0 ? dif.Min() : 0;

            double yMax = Math.Max(Math.Abs(macdMax), Math.Abs(difMax));
            double yMin = -yMax;

            if (yMax < 0.3) yMax = 0.3;

            MacdPlot.Plot.Axes.SetLimitsY(yMin * 1.2, yMax * 1.2);

            // 优化网格线
            MacdPlot.Plot.Grid.MajorLineColor = Color.FromHex("#F0F0F0");
            MacdPlot.Plot.Grid.MajorLineWidth = 0.5f;

            // ===== 统一左右边距 =====
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

        private void SetupDateAxis(Plot plot, List<StockData> dataList, int visibleCount)
        {
            if (dataList == null || dataList.Count == 0) return;

            var bottomAxis = plot.Axes.Bottom;

            int tickCount = Math.Min(8, visibleCount);
            int interval = Math.Max(1, visibleCount / tickCount);

            var tickPositions = new List<double>();
            var tickLabels = new List<string>();

            for (int i = 0; i < visibleCount; i += interval)
            {
                tickPositions.Add(i);
                tickLabels.Add(dataList[i].Date.ToString("MM-dd"));
            }

            if (tickPositions.Count == 0 || tickPositions.Last() != visibleCount - 1)
            {
                tickPositions.Add(visibleCount - 1);
                tickLabels.Add(dataList[visibleCount - 1].Date.ToString("MM-dd"));
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

            int displayIndex = _isTrainingMode && !_isAnswerRevealed
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
            // 防止 500ms 内重复点击导致状态错乱
            if ((DateTime.Now - _lastToggleTime).TotalMilliseconds < 500) return;
            _lastToggleTime = DateTime.Now;

            _isTrainingMode = !_isTrainingMode;
            _isAnswerRevealed = false;
            _currentVisibleBars = _trainingBars;

            if (_isTrainingMode)
            {
                TrainingStatus = $"训练模式：预测后{_totalBars - _trainingBars}根K线走势";

                // 基于当前股票取最近 _totalBars 根数据（不切换股票）
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
            if (!_isTrainingMode || _isAnswerRevealed) return;
            if (_currentVisibleBars >= _totalBars) return;

            _currentVisibleBars++;
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
                KlinePlot.MouseWheel += (s, e) => SyncChartsFrom(KlinePlot);
                KlinePlot.MouseUp += (s, e) => SyncChartsFrom(KlinePlot);
                KlinePlot.MouseMove += (s, e) => UpdateCrosshair(KlinePlot, e);
            }
            if (VolPlot != null)
            {
                VolPlot.MouseWheel += (s, e) => SyncChartsFrom(VolPlot);
                VolPlot.MouseUp += (s, e) => SyncChartsFrom(VolPlot);
                VolPlot.MouseMove += (s, e) => UpdateCrosshair(VolPlot, e);
            }
            if (MacdPlot != null)
            {
                MacdPlot.MouseWheel += (s, e) => SyncChartsFrom(MacdPlot);
                MacdPlot.MouseUp += (s, e) => SyncChartsFrom(MacdPlot);
                MacdPlot.MouseMove += (s, e) => UpdateCrosshair(MacdPlot, e);
            }
        }

        private void UpdateCrosshair(WpfPlot source, System.Windows.Input.MouseEventArgs e)
        {
            if (_klineCursor == null || _volCursor == null || _macdCursor == null) return;

            var pos = e.GetPosition(source);
            Pixel mousePixel = new(pos.X, pos.Y);
            Coordinates mouseCoords = source.Plot.GetCoordinates(mousePixel);
            double x = mouseCoords.X;

            var dataList = _currentDataList;
            if (dataList == null || dataList.Count == 0) return;

            int visibleCount = _isTrainingMode && !_isAnswerRevealed
                ? Math.Min(_currentVisibleBars, dataList.Count)
                : dataList.Count;

            if (x < -0.5 || x > visibleCount - 0.5) return;

            UpdateCursorLine(KlinePlot, ref _klineCursor, x);
            UpdateCursorLine(VolPlot, ref _volCursor, x);
            UpdateCursorLine(MacdPlot, ref _macdCursor, x);

            KlinePlot?.Refresh();
            VolPlot?.Refresh();
            MacdPlot?.Refresh();
        }

        private void UpdateCursorLine(WpfPlot? plot, ref ScottPlot.Plottables.Scatter? cursor, double x)
        {
            if (plot == null) return;

            if (cursor != null)
            {
                plot.Plot.Remove(cursor);
            }

            double[] xs = new double[] { x, x };
            double[] ys = new double[] { -99999, 99999 };
            cursor = plot.Plot.Add.Scatter(xs, ys);
            cursor.Color = Color.FromHex("#999999");
            cursor.LineWidth = 1;
            cursor.MarkerSize = 0;
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