using ScottPlot;
using ScottPlot.WPF;
using StockKLineTrainer.Models;
using StockKLineTrainer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SPColor = ScottPlot.Color;

namespace StockKLineTrainer
{
    public class TradeRecord
    {
        public string Type { get; set; } = "";
        public string Date { get; set; } = "";
        public double Price { get; set; }
        public string Profit { get; set; } = "";
    }

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

        // ===== 十字光标 =====
        private ScottPlot.Plottables.VerticalLine? _klineVLine;
        private ScottPlot.Plottables.HorizontalLine? _klineHLine;
        private ScottPlot.Plottables.VerticalLine? _volVLine;
        private ScottPlot.Plottables.VerticalLine? _macdVLine;

        private ScottPlot.Plottables.Text? _priceLabel;

        // ===== 训练开始标记线 =====
        private double _startMarkerIndex = -1;

        // ===== 爆竹资金系统 =====
        private const double DefaultInitialFirecrackers = 10000;
        private double _initialFirecrackers = DefaultInitialFirecrackers;
        private const double FeeRate = 0.0003;
        private double _cash;
        private double _holdBuyAmount = 0;
        private double? _avgCostPrice = null;
        private int _buyBarIndex = -1;
        private bool _hasPosition => _holdBuyAmount > 0;

        // ===== 预筛选缓存 =====
        private List<string> _validStockList = new();

        // ===== 交易标记 B/S =====
        private readonly List<TradeMarker> _tradeMarkers = new();

        private class TradeMarker
        {
            public int BarIndex { get; set; }
            public string Type { get; set; } = "";
        }

        // ===== 训练统计 =====
        private int _openCount = 0;
        private int _profitCount = 0;
        private int _watchDays = 0;
        private int _holdDays = 0;
        private int _heavyHoldDays = 0;
        private double _closedProfitAmount = 0;
        private double _closedCostAmount = 0;

        // ===== 计时器 =====
        private DispatcherTimer? _timer;
        private DateTime? _trainingStartTime;
        private TimeSpan _elapsed;

        // ===== 操作流水 =====
        public ObservableCollection<TradeRecord> TradeRecords { get; } = new();

        // ===== 顶部信息栏绑定属性 =====
        private double _currentPrice;
        public double CurrentPrice { get => _currentPrice; set { _currentPrice = value; OnPropertyChanged(); } }

        private double _priceChange;
        public double PriceChange { get => _priceChange; set { _priceChange = value; OnPropertyChanged(); } }

        private double _priceChangePct;
        public double PriceChangePct { get => _priceChangePct; set { _priceChangePct = value; OnPropertyChanged(); } }

        private Brush _priceChangeBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray);
        public Brush PriceChangeBrush { get => _priceChangeBrush; set { _priceChangeBrush = value; OnPropertyChanged(); } }

        private double _openPrice;
        public double OpenPrice { get => _openPrice; set { _openPrice = value; OnPropertyChanged(); } }

        private double _highPrice;
        public double HighPrice { get => _highPrice; set { _highPrice = value; OnPropertyChanged(); } }

        private double _lowPrice;
        public double LowPrice { get => _lowPrice; set { _lowPrice = value; OnPropertyChanged(); } }

        private string _volumeRatio = "--";
        public string VolumeRatio { get => _volumeRatio; set { _volumeRatio = value; OnPropertyChanged(); } }

        private string _turnoverRate = "--";
        public string TurnoverRate { get => _turnoverRate; set { _turnoverRate = value; OnPropertyChanged(); } }

        private string _currentDate = "xxxx-xx-xx";
        public string CurrentDate { get => _currentDate; set { _currentDate = value; OnPropertyChanged(); } }

        // ===== 指标数值绑定属性 =====
        private string _ma5Value = "--";
        public string MA5Value { get => _ma5Value; set { _ma5Value = value; OnPropertyChanged(); } }

        private string _ma10Value = "--";
        public string MA10Value { get => _ma10Value; set { _ma10Value = value; OnPropertyChanged(); } }

        private string _ma20Value = "--";
        public string MA20Value { get => _ma20Value; set { _ma20Value = value; OnPropertyChanged(); } }

        private string _ema5Value = "--";
        public string EMA5Value { get => _ema5Value; set { _ema5Value = value; OnPropertyChanged(); } }

        private string _ema10Value = "--";
        public string EMA10Value { get => _ema10Value; set { _ema10Value = value; OnPropertyChanged(); } }

        private string _ema20Value = "--";
        public string EMA20Value { get => _ema20Value; set { _ema20Value = value; OnPropertyChanged(); } }

        private string _bollUpValue = "--";
        public string BollUpValue { get => _bollUpValue; set { _bollUpValue = value; OnPropertyChanged(); } }

        private string _bollMidValue = "--";
        public string BollMidValue { get => _bollMidValue; set { _bollMidValue = value; OnPropertyChanged(); } }

        private string _bollDnValue = "--";
        public string BollDnValue { get => _bollDnValue; set { _bollDnValue = value; OnPropertyChanged(); } }

        private string _volMA5Value = "--";
        public string VolMA5Value { get => _volMA5Value; set { _volMA5Value = value; OnPropertyChanged(); } }

        private string _volMA10Value = "--";
        public string VolMA10Value { get => _volMA10Value; set { _volMA10Value = value; OnPropertyChanged(); } }

        private string _volumeValue = "--";
        public string VolumeValue { get => _volumeValue; set { _volumeValue = value; OnPropertyChanged(); } }

        private string _difValue = "--";
        public string DIFValue { get => _difValue; set { _difValue = value; OnPropertyChanged(); } }

        private string _deaValue = "--";
        public string DEAValue { get => _deaValue; set { _deaValue = value; OnPropertyChanged(); } }

        private string _macdValue = "--";
        public string MACDValue { get => _macdValue; set { _macdValue = value; OnPropertyChanged(); } }

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
        public ICommand BuyCommand { get; }
        public ICommand SellCommand { get; }
        public ICommand SettleCommand { get; }

        // ===== 右侧绑定属性 =====
        public double TotalFirecrackers => _cash + CurrentMarketValue;
        public double UsedFirecrackers => CurrentMarketValue;
        public double UnusedFirecrackers => _cash;

        private double CurrentMarketValue
        {
            get
            {
                if (!_hasPosition || !_avgCostPrice.HasValue) return 0;
                double price = GetCurrentPrice();
                if (price <= 0 || _avgCostPrice.Value <= 0) return 0;
                return _holdBuyAmount * (price / _avgCostPrice.Value);
            }
        }

        public double CurrentProfitPct => _hasPosition && _avgCostPrice.HasValue && _avgCostPrice.Value > 0
            ? (GetCurrentPrice() - _avgCostPrice.Value) / _avgCostPrice.Value * 100
            : 0;

        public double ClosedProfitPct => _closedCostAmount > 0
            ? _closedProfitAmount / _closedCostAmount * 100
            : 0;

        public string AvgCostPriceText => _avgCostPrice.HasValue ? _avgCostPrice.Value.ToString("F2") : "--";
        public string PositionText => _hasPosition ? "1/1" : "0/1";
        public string FrozenText => (_hasPosition && _currentVisibleBars <= _buyBarIndex + 1) ? "1" : "0";
        public int RemainingBars => _totalBars - _currentVisibleBars;
        public int OpenCount => _openCount;
        public int ProfitCount => _profitCount;
        public int WatchDays => _watchDays;
        public int HoldDays => _holdDays;
        public int HeavyHoldDays => _heavyHoldDays;
        public string ElapsedText => $"{(int)_elapsed.TotalSeconds}s";

        public string BuyBtnSubText => !_hasPosition ? "可买1/1仓" : "无仓位可买";
        public string SellBtnSubText
        {
            get
            {
                if (!_hasPosition) return "无仓位可卖";
                if (_currentVisibleBars <= _buyBarIndex + 1) return "T+1冻结";
                return "可卖1/1仓";
            }
        }

        // ===== 开仓收益 =====
        public double OpenProfitAmount => _hasPosition ? CurrentMarketValue - _holdBuyAmount : 0;

        public double OpenProfitPct => _hasPosition && _holdBuyAmount > 0
            ? (CurrentMarketValue - _holdBuyAmount) / _holdBuyAmount * 100
            : 0;

        // ===== 本局收益 =====
        public double TotalProfitAmount => TotalFirecrackers - _initialFirecrackers;
        public double TotalProfitPct => _initialFirecrackers > 0
            ? (TotalFirecrackers - _initialFirecrackers) / _initialFirecrackers * 100
            : 0;

        public Brush OpenProfitBrush => GetProfitBrush(OpenProfitAmount);
        public Brush TotalProfitBrush => GetProfitBrush(TotalProfitAmount);

        private Brush GetProfitBrush(double value)
        {
            if (value > 0) return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x32, 0x32));
            if (value < 0) return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xA8, 0x00));
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));
        }

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
            BuyCommand = new RelayCommand(_ => ExecuteBuy(), _ => CanBuy());
            SellCommand = new RelayCommand(_ => ExecuteSell(), _ => CanSell());
            SettleCommand = new RelayCommand(_ => ExecuteSettle(), _ => _isTrainingMode);

            LoadStockList();
            _currentVisibleBars = _trainingBars;

            _cash = _initialFirecrackers;

            _isInitializing = false;

            if (StockList.Count > 0)
            {
                LoadRandomStock();
            }
        }

        private void LoadStockList()
        {
            var stocks = _dbService.GetAllStockCodes();
            StockList.Clear();
            foreach (var s in stocks) StockList.Add(s);

            _validStockList = stocks.Where(code =>
            {
                var data = _dbService.GetStockData(code, startDate: "19900101", endDate: "20251231", limit: 10000);
                return data.Count >= _totalBars;
            }).ToList();

            Debug.WriteLine($"[DIAG] LoadStockList: {stocks.Count} stocks loaded, {_validStockList.Count} valid");
        }

        private void LoadRandomStock()
        {
            _startMarkerIndex = -1;
            ResetTrainingStats();

            if (_validStockList.Count == 0)
            {
                MessageBox.Show($"数据库中没有一只股票拥有足够的历史数据（{_totalBars}根）");
                return;
            }

            string newStock;
            if (_validStockList.Count == 1)
            {
                newStock = _validStockList[0];
            }
            else
            {
                do
                {
                    newStock = _validStockList[_random.Next(_validStockList.Count)];
                } while (newStock == _selectedStock);
            }

            var allData = _dbService.GetStockData(newStock, startDate: "19900101", endDate: "20251231", limit: 10000);

            if (allData.Count < _totalBars)
            {
                MessageBox.Show($"股票 {newStock} 数据不足 {_totalBars} 根");
                return;
            }

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
            NotifyAllStats();
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
            NotifyAllStats();
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

            // ===== 手动绘制 K 线（支持单根颜色 + 右侧内投影）=====
            var mainBars = new List<ScottPlot.Bar>();
            var shadowBars = new List<ScottPlot.Bar>();
            var upperShadows = new List<ScottPlot.Bar>();
            var lowerShadows = new List<ScottPlot.Bar>();

            for (int i = windowStart; i < windowEnd; i++)
            {
                var d = dataList[i];
                double x = i;
                bool isRising = d.Close >= d.Open;
                double prevClose = i > 0 ? dataList[i - 1].Close : d.Open;
                double changePct = prevClose > 0 ? (d.Close - prevClose) / prevClose : 0;

                // 颜色判断（0.195 = 20% 涨跌停，主板 10% 请改成 0.095）
                SPColor mainColor;
                if (changePct >= 0.195)
                    mainColor = SPColor.FromHex("#ffb600");      // 涨停黄
                else if (changePct <= -0.195)
                    mainColor = SPColor.FromHex("#008eff");      // 跌停蓝
                else if (isRising)
                    mainColor = SPColor.FromHex("#e04555");      // 普通红
                else
                    mainColor = SPColor.FromHex("#1f9d72");      // 普通绿

                // 内投影色（主色加深 30%）
                SPColor shadowColor = new SPColor(
                    (byte)(mainColor.R * 0.7),
                    (byte)(mainColor.G * 0.7),
                    (byte)(mainColor.B * 0.7)
                );

                double bodyTop = Math.Max(d.Open, d.Close);
                double bodyBottom = Math.Min(d.Open, d.Close);
                if (bodyTop - bodyBottom < 0.0001) bodyTop += 0.0001;

                // 上影线
                if (d.High > bodyTop)
                {
                    upperShadows.Add(new ScottPlot.Bar
                    {
                        Position = x,
                        Value = d.High,
                        ValueBase = bodyTop,
                        Size = 0.02,
                        FillColor = mainColor,
                        LineWidth = 0
                    });
                }

                // 下影线
                if (d.Low < bodyBottom)
                {
                    lowerShadows.Add(new ScottPlot.Bar
                    {
                        Position = x,
                        Value = bodyBottom,
                        ValueBase = d.Low,
                        Size = 0.2,
                        FillColor = mainColor,
                        LineWidth = 0
                    });
                }

                // 实体主体
                mainBars.Add(new ScottPlot.Bar
                {
                    Position = x,
                    Value = bodyTop,
                    ValueBase = bodyBottom,
                    Size = 0.6,
                    FillColor = mainColor,
                    LineWidth = 0
                });

                // 右侧内投影（窄条，贴紧实体右边缘）
                double shadowSize = 0.12;
                double shadowPos = x + 0.3 - shadowSize / 2;
                shadowBars.Add(new ScottPlot.Bar
                {
                    Position = shadowPos,
                    Value = bodyTop,
                    ValueBase = bodyBottom,
                    Size = shadowSize,
                    FillColor = shadowColor,
                    LineWidth = 0
                });
            }

            KlinePlot.Plot.Add.Bars(upperShadows);
            KlinePlot.Plot.Add.Bars(lowerShadows);
            KlinePlot.Plot.Add.Bars(mainBars);
            KlinePlot.Plot.Add.Bars(shadowBars);


            AddMALines(dataList, windowStart, visibleCount);
            KlinePlot.Plot.Legend.IsVisible = false;

            // ===== 价格轴移到右侧，隐藏左侧 =====
            KlinePlot.Plot.Axes.Top.IsVisible = false;
            KlinePlot.Plot.Axes.Left.IsVisible = false;
            KlinePlot.Plot.Axes.Left.MinimumSize = 0;
            KlinePlot.Plot.Axes.Right.IsVisible = true;
            KlinePlot.Plot.Axes.Right.MinimumSize = 50;
            KlinePlot.Plot.Axes.Right.Label.Text = "";

            // ===== 去掉底部日期轴（由MACD统一显示） =====
            KlinePlot.Plot.Axes.Bottom.IsVisible = false;
            KlinePlot.Plot.Grid.XAxisStyle.IsVisible = false;

            // ===== Y轴边距压缩：只留5%边距 =====
                        // ===== Y轴边距压缩：只留5%边距 =====
            var visibleData = dataList.Skip(windowStart).Take(visibleCount).ToList();
            if (visibleData.Count > 0)
            {
                double yMin = visibleData.Min(d => d.Low);
                double yMax = visibleData.Max(d => d.High);
                double range = yMax - yMin;
                if (range <= 0) range = yMax * 0.01;
                double padding = range * 0.05;
                
                // 设置 Left 轴（Plottables 绑定的默认轴）
                KlinePlot.Plot.Axes.SetLimitsY(yMin - padding, yMax + padding);
                
                // ★ 关键：把数据范围同步给 Right 轴，右侧才会显示价格刻度
                KlinePlot.Plot.Axes.Right.Min = yMin - padding;
                KlinePlot.Plot.Axes.Right.Max = yMax + padding;
            }

            Debug.WriteLine($"[DIAG] Before SetLimitsX: XRange={KlinePlot.Plot.Axes.GetLimits().XRange}");
            KlinePlot.Plot.Axes.SetLimitsX(windowStart - 0.5, windowEnd - 0.5);

            Debug.WriteLine($"[DIAG] Before SetLimitsX: XRange={KlinePlot.Plot.Axes.GetLimits().XRange}");
            KlinePlot.Plot.Axes.SetLimitsX(windowStart - 0.5, windowEnd - 0.5);
            Debug.WriteLine($"[DIAG] After SetLimitsX: XRange={KlinePlot.Plot.Axes.GetLimits().XRange}, YRange={KlinePlot.Plot.Axes.GetLimits().YRange}");

            // ===== 十字光标 =====
            _klineVLine = KlinePlot.Plot.Add.VerticalLine(0);
            _klineVLine.Color = SPColor.FromHex("#000000");
            _klineVLine.LineWidth = 0.5f;
            _klineVLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _klineVLine.IsVisible = false;

            _klineHLine = KlinePlot.Plot.Add.HorizontalLine(0);
            _klineHLine.Color = SPColor.FromHex("#000000");
            _klineHLine.LineWidth = 0.5f;
            _klineHLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _klineHLine.IsVisible = false;

            // ===== 右侧Y轴跟随价格标签（蓝色背景） =====
            _priceLabel = KlinePlot.Plot.Add.Text("", new Coordinates(0, 0));
            _priceLabel.LabelStyle.FontSize = 11;
            _priceLabel.LabelStyle.Bold = true;
            _priceLabel.LabelStyle.ForeColor = SPColor.FromHex("#FFFFFF");
            _priceLabel.LabelStyle.BackgroundColor = SPColor.FromHex("#2196F3");
            _priceLabel.LabelStyle.BorderColor = SPColor.FromHex("#2196F3");
            _priceLabel.LabelStyle.BorderWidth = 1;
            _priceLabel.LabelStyle.Alignment = Alignment.MiddleRight; // 坐标点在标签右侧，文字向左展开
            _priceLabel.IsVisible = false;

            // 优化网格线
            KlinePlot.Plot.Grid.MajorLineColor = SPColor.FromHex("#F0F0F0");
            KlinePlot.Plot.Grid.MajorLineWidth = 0.5f;

            // ===== 训练开始标记线（蓝色竖虚线） =====
            if (_isTrainingMode && _startMarkerIndex >= 0)
            {
                var marker = KlinePlot.Plot.Add.VerticalLine(_startMarkerIndex);
                marker.Color = SPColor.FromHex("#2196F3");
                marker.LineWidth = 2;
                marker.LinePattern = LinePattern.Dashed;
            }

            // ===== 绘制 B/S 标记和"开始"标签 =====
            DrawTradeMarkers(dataList, windowStart, windowEnd);

            KlinePlot.Refresh();
            Debug.WriteLine("[DIAG] Refresh called");
        }

        private void DrawTradeMarkers(List<StockData> dataList, int windowStart, int windowEnd)
        {
            // 绘制 B/S 标记
            foreach (var marker in _tradeMarkers)
            {
                if (marker.BarIndex < windowStart || marker.BarIndex >= windowEnd) continue;

                var d = dataList[marker.BarIndex];
                double x = marker.BarIndex;
                double y = d.High * 1.015;

                var txt = KlinePlot!.Plot.Add.Text(marker.Type, new Coordinates(x, y));
                txt.LabelStyle.FontSize = 10;
                txt.LabelStyle.Bold = true;
                txt.LabelStyle.ForeColor = SPColor.FromHex("#FFFFFF");
                txt.LabelStyle.BackgroundColor = marker.Type == "B"
                    ? SPColor.FromHex("#FF3232")
                    : SPColor.FromHex("#2196F3");
                txt.LabelStyle.BorderColor = txt.LabelStyle.BackgroundColor;
                txt.LabelStyle.BorderWidth = 1;
                txt.LabelStyle.Alignment = Alignment.LowerCenter;
            }

            // 绘制"开始"标签
            if (_isTrainingMode && _startMarkerIndex >= 0)
            {
                int startIdx = (int)Math.Floor(_startMarkerIndex);
                if (startIdx >= windowStart && startIdx < windowEnd && startIdx < dataList.Count)
                {
                    double startY = dataList[startIdx].High * 1.05;
                    var startTxt = KlinePlot!.Plot.Add.Text("开始", new Coordinates(_startMarkerIndex, startY));
                    startTxt.LabelStyle.FontSize = 10;
                    startTxt.LabelStyle.Bold = true;
                    startTxt.LabelStyle.ForeColor = SPColor.FromHex("#FFFFFF");
                    startTxt.LabelStyle.BackgroundColor = SPColor.FromHex("#2196F3");
                    startTxt.LabelStyle.BorderColor = SPColor.FromHex("#2196F3");
                    startTxt.LabelStyle.BorderWidth = 1;
                    startTxt.LabelStyle.Alignment = Alignment.LowerCenter;
                }
            }
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
                line.Color = SPColor.FromHex(colorHex);
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
                    Size = 0.5f,
                    FillColor = isRising
        ? SPColor.FromHex("#e04555")
        : SPColor.FromHex("#1f9d72"),
                    LineWidth = 0  // ← 显式去掉边框
                });
            }

            VolPlot.Plot.Add.Bars(bars);

            var volMA5 = dataList.Skip(windowStart).Take(visibleCount).Select(d => d.VolMA5 ?? double.NaN).ToArray();
            var volMA10 = dataList.Skip(windowStart).Take(visibleCount).Select(d => d.VolMA10 ?? double.NaN).ToArray();

            double[] xs = Enumerable.Range(windowStart, visibleCount).Select(i => (double)i).ToArray();

            if (!volMA5.All(double.IsNaN))
            {
                var line5 = VolPlot.Plot.Add.Scatter(xs, volMA5);
                line5.Color = SPColor.FromHex("#FFD700");
                line5.LineWidth = 1;
                line5.MarkerSize = 0;
            }

            if (!volMA10.All(double.IsNaN))
            {
                var line10 = VolPlot.Plot.Add.Scatter(xs, volMA10);
                line10.Color = SPColor.FromHex("#00BFFF");
                line10.LineWidth = 1;
                line10.MarkerSize = 0;
            }
            VolPlot.Plot.Axes.Top.IsVisible = false;
            VolPlot.Plot.Axes.Left.IsVisible = false;
            VolPlot.Plot.Axes.Right.IsVisible = false;
            VolPlot.Plot.Axes.Right.Label.Text = "";
            VolPlot.Plot.Axes.Bottom.IsVisible = false;

            // ===== 竖直虚线光标 =====
            _volVLine = VolPlot.Plot.Add.VerticalLine(0);
            _volVLine.Color = SPColor.FromHex("#000000");
            _volVLine.LineWidth = 0.5f;
            _volVLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _volVLine.IsVisible = false;

            VolPlot.Plot.Axes.SetLimitsX(windowStart - 0.5, windowEnd - 0.5);
            VolPlot.Plot.Axes.AutoScaleY();

            VolPlot.Plot.Grid.MajorLineColor = SPColor.FromHex("#F0F0F0");
            VolPlot.Plot.Grid.MajorLineWidth = 0.5f;

            VolPlot.Plot.Axes.Left.MinimumSize = 0;
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

            MacdPlot.Plot.Legend.IsVisible = false;

            var transparent = new SPColor(0, 0, 0, 0);
            var macdBars = new List<ScottPlot.Bar>();

            for (int i = 0; i < macd.Length && i < visibleCount; i++)
            {
                bool isRising = i == 0 || Math.Abs(macd[i]) > Math.Abs(macd[i - 1]);

                var bar = new ScottPlot.Bar
                {
                    Position = windowStart + i,
                    Value = macd[i],
                    Size = 0.5f,
                    LineWidth = 1.2f
                };

                if (macd[i] >= 0)
                {
                    if (isRising)
                    {
                        bar.FillColor = transparent;
                        bar.LineColor = SPColor.FromHex("#FF3232");
                    }
                    else
                    {
                        bar.FillColor = SPColor.FromHex("#FF3232");
                        bar.LineColor = SPColor.FromHex("#FF3232");
                    }
                }
                else
                {
                    if (isRising)
                    {
                        bar.FillColor = transparent;
                        bar.LineColor = SPColor.FromHex("#00A800");
                    }
                    else
                    {
                        bar.FillColor = SPColor.FromHex("#00A800");
                        bar.LineColor = SPColor.FromHex("#00A800");
                    }
                }

                macdBars.Add(bar);
            }

            MacdPlot.Plot.Add.Bars(macdBars);

            PlotMacdLine(xs, dif, "#FFD700", "DIF");
            PlotMacdLine(xs, dea, "#00BFFF", "DEA");

            MacdPlot.Plot.Axes.Top.IsVisible = false;
            MacdPlot.Plot.Axes.Left.IsVisible = false;
            MacdPlot.Plot.Axes.Right.IsVisible = false;
            MacdPlot.Plot.Axes.Right.Label.Text = "";
            MacdPlot.Plot.Axes.Bottom.IsVisible = false;

            SetupDateAxis(MacdPlot.Plot, dataList, windowStart, visibleCount);

            // ===== 竖直虚线光标 =====
            _macdVLine = MacdPlot.Plot.Add.VerticalLine(0);
            _macdVLine.Color = SPColor.FromHex("#000000");
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

            MacdPlot.Plot.Grid.MajorLineColor = SPColor.FromHex("#F0F0F0");
            MacdPlot.Plot.Grid.MajorLineWidth = 0.5f;

            MacdPlot.Plot.Axes.Left.MinimumSize = 0;
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
                line.Color = SPColor.FromHex(colorHex);
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

            // 顶部信息栏
            CurrentPrice = last.Close;
            PriceChange = change;
            PriceChangePct = changePct;
            PriceChangeBrush = GetProfitBrush(change);
            OpenPrice = last.Open;
            HighPrice = last.High;
            LowPrice = last.Low;
            TurnoverRate = "--"; // ← 如果数据库有换手率字段，把这里改成对应的属性名
            VolumeRatio = "--";
            CurrentDate = last.Date.ToString("yyyy-MM-dd");

            // 指标数值（取最后一根可见K线的值）
            MA5Value = last.MA5.HasValue ? last.MA5.Value.ToString("F2") : "--";
            MA10Value = last.MA10.HasValue ? last.MA10.Value.ToString("F2") : "--";
            MA20Value = last.MA20.HasValue ? last.MA20.Value.ToString("F2") : "--";

            EMA5Value = last.EMA5.HasValue ? last.EMA5.Value.ToString("F2") : "--";
            EMA10Value = last.EMA10.HasValue ? last.EMA10.Value.ToString("F2") : "--";
            EMA20Value = last.EMA20.HasValue ? last.EMA20.Value.ToString("F2") : "--";

            BollUpValue = last.BollUp.HasValue ? last.BollUp.Value.ToString("F2") : "--";
            BollMidValue = last.BollMid.HasValue ? last.BollMid.Value.ToString("F2") : "--";
            BollDnValue = last.BollDn.HasValue ? last.BollDn.Value.ToString("F2") : "--";

            VolMA5Value = last.VolMA5.HasValue ? (last.VolMA5.Value / 1_000_000).ToString("F3") + "M" : "--";
            VolMA10Value = last.VolMA10.HasValue ? (last.VolMA10.Value / 1_000_000).ToString("F3") + "M" : "--";
            VolumeValue = (last.Volume / 1_000_000).ToString("F3") + "M";

            DIFValue = last.DIF.HasValue ? last.DIF.Value.ToString("F4") : "--";
            DEAValue = last.DEA.HasValue ? last.DEA.Value.ToString("F4") : "--";
            MACDValue = last.MACDHist.HasValue ? last.MACDHist.Value.ToString("F4") : "--";

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
                        NotifyAllStats();
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
                _startMarkerIndex = -1;
                StopTimer();
                ResetTrainingStats();
                TrainingStatus = "浏览模式";
                LoadData();
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private void RevealAnswer()
        {
            _isAnswerRevealed = true;
            TrainingStatus = "答案已揭示";
            StopTimer();
            UpdatePriceChart();
            UpdateVolChart();
            UpdateMacdChart();
            NotifyAllStats();
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnHoldOrWatch()
        {
            if (_isAnswerRevealed) return;

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

                _startMarkerIndex = _currentVisibleBars - 0.5;
                StartTimer();
            }
            else
            {
                if (_currentVisibleBars >= _totalBars) return;
                _currentVisibleBars++;

                if (_hasPosition)
                {
                    _holdDays++;
                    _heavyHoldDays++;
                }
                else
                {
                    _watchDays++;
                }
            }

            UpdatePriceChart();
            UpdateVolChart();
            UpdateMacdChart();
            UpdateInfoBar();
            NotifyAllStats();

            int remaining = _totalBars - _currentVisibleBars;
            if (remaining > 0)
            {
                TrainingStatus = $"训练模式：已推进 {_currentVisibleBars}/{_totalBars}，剩余 {remaining} 根";
                CommandManager.InvalidateRequerySuggested();
            }
            else
            {
                ExecuteSettle();
            }
        }

        private bool CanBuy() => _isTrainingMode && !_isAnswerRevealed && !_hasPosition && _currentVisibleBars < _totalBars;

        private void ExecuteBuy()
        {
            if (!CanBuy()) return;

            double price = GetCurrentPrice();
            if (price <= 0) return;

            double buyAmount = _cash / (1 + FeeRate);
            double fee = buyAmount * FeeRate;

            _holdBuyAmount = buyAmount;
            _avgCostPrice = price;
            _buyBarIndex = _currentVisibleBars;
            _cash = 0;
            _openCount++;
            _tradeMarkers.Add(new TradeMarker { BarIndex = _currentVisibleBars - 1, Type = "B" });

            TradeRecords.Insert(0, new TradeRecord
            {
                Type = "买入",
                Date = GetCurrentDate(),
                Price = price,
                Profit = "100.00%"
            });

            NotifyAllStats();
            CommandManager.InvalidateRequerySuggested();
        }

        private bool CanSell() => _isTrainingMode && !_isAnswerRevealed && _hasPosition && _currentVisibleBars > _buyBarIndex + 1;

        private void ExecuteSell()
        {
            if (!CanSell()) return;

            double price = GetCurrentPrice();
            if (price <= 0 || !_avgCostPrice.HasValue) return;

            double marketValue = _holdBuyAmount * (price / _avgCostPrice.Value);
            double fee = marketValue * FeeRate;
            double netCash = marketValue - fee;
            double profit = marketValue - _holdBuyAmount - (_holdBuyAmount * FeeRate) - fee;
            double profitPct = (price - _avgCostPrice.Value) / _avgCostPrice.Value * 100;

            _cash = netCash;
            _tradeMarkers.Add(new TradeMarker { BarIndex = _currentVisibleBars - 1, Type = "S" });
            _closedProfitAmount += profit;
            _closedCostAmount += _holdBuyAmount;

            if (profit > 0) _profitCount++;

            TradeRecords.Insert(0, new TradeRecord
            {
                Type = "卖出",
                Date = GetCurrentDate(),
                Price = price,
                Profit = profitPct.ToString("F2") + "%"
            });

            _holdBuyAmount = 0;
            _avgCostPrice = null;
            _buyBarIndex = -1;

            NotifyAllStats();
            CommandManager.InvalidateRequerySuggested();
        }

        private void ExecuteSettle()
        {
            if (_hasPosition)
            {
                double price = GetCurrentPrice();
                if (price > 0 && _avgCostPrice.HasValue)
                {
                    double marketValue = _holdBuyAmount * (price / _avgCostPrice.Value);
                    double fee = marketValue * FeeRate;
                    double profit = marketValue - _holdBuyAmount - fee - (_holdBuyAmount * FeeRate);

                    _cash = marketValue - fee;
                    _closedProfitAmount += profit;
                    _closedCostAmount += _holdBuyAmount;
                    if (profit > 0) _profitCount++;

                    TradeRecords.Insert(0, new TradeRecord
                    {
                        Type = "卖出(结算)",
                        Date = GetCurrentDate(),
                        Price = price,
                        Profit = ((price - _avgCostPrice.Value) / _avgCostPrice.Value * 100).ToString("F2") + "%"
                    });
                }
                _holdBuyAmount = 0;
                _avgCostPrice = null;
            }

            StopTimer();
            _isAnswerRevealed = true;
            TrainingStatus = "训练已结算";
            UpdatePriceChart();
            UpdateVolChart();
            UpdateMacdChart();
            NotifyAllStats();
            CommandManager.InvalidateRequerySuggested();

            _initialFirecrackers = TotalFirecrackers;
            ShowTrainingResult();
        }

        private double GetCurrentPrice()
        {
            if (_currentDataList == null || _currentDataList.Count == 0) return 0;
            int idx = Math.Min(_currentVisibleBars, _currentDataList.Count) - 1;
            if (idx < 0) idx = 0;
            return _currentDataList[idx].Close;
        }

        private string GetCurrentDate()
        {
            if (_currentDataList == null || _currentDataList.Count == 0) return "";
            int idx = Math.Min(_currentVisibleBars, _currentDataList.Count) - 1;
            if (idx < 0) idx = 0;
            return _currentDataList[idx].Date.ToString("yyyy-MM-dd");
        }

        private void StartTimer()
        {
            _trainingStartTime = DateTime.Now;
            _elapsed = TimeSpan.Zero;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                if (_trainingStartTime.HasValue)
                {
                    _elapsed = DateTime.Now - _trainingStartTime.Value;
                    OnPropertyChanged(nameof(ElapsedText));
                }
            };
            _timer.Start();
        }

        private void StopTimer()
        {
            _timer?.Stop();
            _timer = null;
        }

        private void ResetTrainingStats()
        {
            _cash = _initialFirecrackers;
            _holdBuyAmount = 0;
            _avgCostPrice = null;
            _buyBarIndex = -1;
            _openCount = 0;
            _profitCount = 0;
            _watchDays = 0;
            _holdDays = 0;
            _heavyHoldDays = 0;
            _closedProfitAmount = 0;
            _closedCostAmount = 0;
            _elapsed = TimeSpan.Zero;
            TradeRecords.Clear();
            _tradeMarkers.Clear();
            NotifyAllStats();
        }

        private void NotifyAllStats()
        {
            OnPropertyChanged(nameof(TotalFirecrackers));
            OnPropertyChanged(nameof(UsedFirecrackers));
            OnPropertyChanged(nameof(UnusedFirecrackers));
            OnPropertyChanged(nameof(OpenProfitAmount));
            OnPropertyChanged(nameof(OpenProfitPct));
            OnPropertyChanged(nameof(TotalProfitAmount));
            OnPropertyChanged(nameof(TotalProfitPct));
            OnPropertyChanged(nameof(OpenProfitBrush));
            OnPropertyChanged(nameof(TotalProfitBrush));
            OnPropertyChanged(nameof(AvgCostPriceText));
            OnPropertyChanged(nameof(PositionText));
            OnPropertyChanged(nameof(FrozenText));
            OnPropertyChanged(nameof(RemainingBars));
            OnPropertyChanged(nameof(OpenCount));
            OnPropertyChanged(nameof(ProfitCount));
            OnPropertyChanged(nameof(WatchDays));
            OnPropertyChanged(nameof(HoldDays));
            OnPropertyChanged(nameof(HeavyHoldDays));
            OnPropertyChanged(nameof(ElapsedText));
            OnPropertyChanged(nameof(BuyBtnSubText));
            OnPropertyChanged(nameof(SellBtnSubText));
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

            if (_klineVLine != null) { _klineVLine.X = x; _klineVLine.IsVisible = true; }
            if (_volVLine != null) { _volVLine.X = x; _volVLine.IsVisible = true; }
            if (_macdVLine != null) { _macdVLine.X = x; _macdVLine.IsVisible = true; }

            if (source == KlinePlot && _klineHLine != null)
            {
                _klineHLine.Y = mouseCoords.Y;
                _klineHLine.IsVisible = true;

                // 更新右侧Y轴价格标签
                if (_priceLabel != null)
                {
                    var xRange = KlinePlot.Plot.Axes.GetLimits().XRange;
                    _priceLabel.Location = new Coordinates(xRange.Max, mouseCoords.Y);
                    _priceLabel.LabelText = mouseCoords.Y.ToString("F2");
                    _priceLabel.IsVisible = true;
                }
            }
            else if (_klineHLine != null)
            {
                _klineHLine.IsVisible = false;
                if (_priceLabel != null) _priceLabel.IsVisible = false;
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
            if (_priceLabel != null) _priceLabel.IsVisible = false;
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

        private void ShowTrainingResult()
        {
            if (_currentDataList == null || _currentDataList.Count < _trainingBars) return;

            int startIdx = _trainingBars - 1;
            int endIdx = Math.Min(_currentVisibleBars, _currentDataList.Count) - 1;
            if (startIdx < 0 || endIdx < 0 || startIdx >= _currentDataList.Count) return;

            double startPrice = _currentDataList[startIdx].Close;
            double endPrice = _currentDataList[endIdx].Close;
            double intervalPct = startPrice > 0 ? (endPrice - startPrice) / startPrice * 100 : 0;

            double profitAmount = TotalFirecrackers - _initialFirecrackers;
            double profitPct = _initialFirecrackers > 0 ? profitAmount / _initialFirecrackers * 100 : 0;

            var result = new TrainingResult
            {
                StockName = $"{SelectedStock}",
                DateRange = $"{_currentDataList[startIdx].Date:yyyy年M月d日}-{_currentDataList[endIdx].Date:yyyy年M月d日}",
                ProfitAmount = profitAmount,
                ProfitPct = profitPct,
                IntervalChangePct = intervalPct,
                OpenCount = _openCount,
                WinRate = _openCount > 0 ? (double)_profitCount / _openCount * 100 : 0,
                ElapsedTime = ElapsedText
            };

            var dialog = new TrainingResultWindow(result)
            {
                Owner = Application.Current.MainWindow
            };

            dialog.ShowDialog();

            switch (dialog.ResultAction)
            {
                case ResultAction.NextGame:
                    NextGame();
                    break;
                case ResultAction.End:
                    ExitTraining();
                    break;
                case ResultAction.Review:
                    break;
            }
        }

        private void NextGame()
        {
            StopTimer();
            ResetTrainingStats();
            _isTrainingMode = false;
            _isAnswerRevealed = false;
            _startMarkerIndex = -1;
            _currentVisibleBars = _trainingBars;

            LoadRandomStock();

            if (_currentDataList != null && _currentDataList.Count >= _totalBars)
            {
                _isTrainingMode = true;
                _isAnswerRevealed = false;
                _currentVisibleBars = _trainingBars;
                _startMarkerIndex = _currentVisibleBars - 0.5;
                StartTimer();

                UpdatePriceChart();
                UpdateVolChart();
                UpdateMacdChart();
                UpdateInfoBar();
                TrainingStatus = $"训练模式：预测后{_totalBars - _trainingBars}根K线走势";
                NotifyAllStats();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ExitTraining()
        {
            _isTrainingMode = false;
            _isAnswerRevealed = false;
            _startMarkerIndex = -1;
            _currentVisibleBars = _trainingBars;
            StopTimer();
            ResetTrainingStats();
            TrainingStatus = "浏览模式";
            LoadData();
        }
    }
}