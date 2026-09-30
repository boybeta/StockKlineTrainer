using ScottPlot;
using ScottPlot.WPF;
using BaozhuKLineTrainer.Models;
using BaozhuKLineTrainer.Services;
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
using System.IO;
using System.Windows.Controls;
namespace BaozhuKLineTrainer
{
    public class TradeRecord
    {
        public string Type { get; set; } = "";
        public string Date { get; set; } = "";
        public double Price { get; set; }
        public string Profit { get; set; } = "";
    }

    public class SplitBuyItem
    {
        public int Index { get; set; }
        public string Text { get; set; } = "";
        public int Percent { get; set; }
    }


    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _dbService;
        // ===== 涨停训练（2026-09-28 接入）=====
        private readonly LimitUpService _limitUpService;   // 涨停票池/涨停日查询（进程内缓存）
        private DateTime? _limitUpAnchorDate;              // 本局锚定的涨停日（开局提示用）
        private List<StockData> _currentDataList = new();
        private int _totalBars = 270;
        // ===== 按周期取窗口总根数 =====
        // 日线：270 = 120 训练 + 150 未来；周线：170 = 120 训练 + 50 未来（约1年）
        // 月线：名义 72 = 60 训练 + 12 未来；实际未来段按数据量动态取 6~12 根（见 MonthMinFutureBars）
        private const int WeekTotalBars = 170;
        private const int MonthTotalBars = 72;
        private const int MonthMinFutureBars = 6;   // 月线未来段下限：不足12根时，≥6根也开局，练完提前结算
        private int _monthTotalBars = MonthTotalBars;   // 当前这局月线的实际总根数（60 + 实际未来段 6~12）
        private int _currentTotalBars = 270;   // 当前窗口实际总根数（短历史窗口时 < 名义值，推进/剩余/结算上限都走它）
        // 当前窗口"实际"总根数：短历史窗口时小于名义值（如短周线局 143 < 170）。
        // 推进上限、剩余根数、结算判定、进度映射都必须走它，避免推进进死区/进度算错
        private int TotalBarsForPeriod => _currentTotalBars;

        // 任意周期对应的"名义"窗口总根数（切周期算新周期的未来段上限用）
        private int TotalBarsForPeriodFor(KLinePeriod period) => period switch
        {
            KLinePeriod.Week => WeekTotalBars,
            KLinePeriod.Month => MonthTotalBars,
            _ => _totalBars,
        };

        // ===== 训练窗口根数：日/周 120 根，月线 60 根（5年；120根月K=10年，太笨重） =====
        private int TrainingBarsForPeriod => TrainingBarsFor(SelectedPeriod);
        private static int TrainingBarsFor(KLinePeriod period) => period == KLinePeriod.Month ? 60 : 120;

        // ===== 短历史窗口：次新股历史不满训练窗口时，用满全部真实历史（图表从左侧开始画） =====
        // 低于该下限才回滚——上市时间太短，确实没东西可练
        private static int MinPastBarsFor(KLinePeriod period) => period switch
        {
            KLinePeriod.Month => 24,   // 月线至少 2 年真实历史
            KLinePeriod.Week => 52,    // 周线至少 1 年
            _ => 120,                  // 日线至少半年
        };
        private int _trainStartBarIdx = 119;   // 本局训练起点K线索引（结算区间涨跌幅用，切换时随短窗口调整）
        private int _currentVisibleBars = 120;
        private bool _isTrainingMode = false;
        private bool _isAnswerRevealed = false;
        private bool _isInitializing = true;

        private readonly Random _random = new Random();
        private readonly TrainingConfig _config;

        // ===== 指数训练 =====
        private bool IsIndexMode => _config.TrainTarget == "Index";
        // ===== 期货训练（2026-09-28 接入）=====
        private bool IsFutureMode => _config.TrainTarget == "Future";
        // ===== 港股训练（2026-09-28 接入）=====
        private bool IsHKMode => _config.TrainTarget == "HK";
        // ===== 美股训练（2026-09-28 接入）=====
        private bool IsUSMode => _config.TrainTarget == "US";
        // ===== 可转债训练（2026-09-28 接入）=====
        private bool IsBondMode => _config.TrainTarget == "Bond";
        /// <summary>T+0 模式：期货 + 港股 + 美股 + 可转债（无 T+1 冻结）</summary>
        private bool IsT0Mode => IsFutureMode || IsHKMode || IsUSMode || IsBondMode;
        /// <summary>无涨跌停模式：指数 + 期货 + 港股 + 美股 + 可转债（K线不做涨停黄/跌停蓝判断）</summary>
        private bool NoLimitMode => IsIndexMode || IsFutureMode || IsHKMode || IsUSMode || IsBondMode;
        /// <summary>指数代码 → 名称（训练记录/显示用）</summary>
        private static readonly Dictionary<string, string> IndexNames = new()
        {
            ["sh000001"] = "上证指数",
            ["sz399001"] = "深证成指",
            ["sz399006"] = "创业板指",
            ["sh000300"] = "沪深300",
            ["sh000905"] = "中证500",
            ["sh000688"] = "科创50",
        };

        // ===== 十字光标 =====
        private ScottPlot.Plottables.VerticalLine? _klineVLine;
        private ScottPlot.Plottables.HorizontalLine? _klineHLine;
        private ScottPlot.Plottables.VerticalLine? _volVLine;
        private ScottPlot.Plottables.VerticalLine? _macdVLine;

        // ===== 训练开始标记线 =====
        private double _startMarkerIndex = -1;

        // ===== 火星币资金系统 =====
        private const double DefaultInitialFirecrackers = 10000;
        private const double BankruptcyFirecrackers = 0;   // ★ 2026-09-30 破产线改为 0：火星币 ≤ 0（归零或透支）才触发重置
        private double _initialFirecrackers = DefaultInitialFirecrackers;
        private const double FeeRate = 0.0003;
        private double _cash;
        private double _holdBuyAmount = 0;
        private double? _avgCostPrice = null;
        private int _buyBarIndex = -1;
        private bool _hasPosition => _holdBuyAmount > 0;

        // 最大允许持仓金额 = 本金 × 使用比例 × 杠杆
        private double MaxPositionAmount =>
            _initialFirecrackers
            * (_config.IsSplitPosition ? _config.SplitPositionPercent / 100.0 : 1.0)
            * Math.Max(1, _config.Leverage);

        // 1 成仓 = 初始本金的 10%（档位按钮的单位，语义保持不变）
        private double PositionUnit => _initialFirecrackers / 10.0;

        // ===== 预筛选缓存：code → 日线根数（启动查一次）；各周期合格池惰性缓存 =====
        private readonly Dictionary<string, int> _stockDailyCounts = new();
        private readonly Dictionary<KLinePeriod, List<string>> _eligibleCache = new();

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

        public ObservableCollection<SplitBuyItem> SplitBuyItems { get; } = new();
        public bool IsSplitMode => _config?.IsSplitPosition == true;
        public bool IsNormalMode => !IsSplitMode;
        private bool _isBuyOptionsVisible;
        public bool IsBuyOptionsVisible
        {
            get => _isBuyOptionsVisible;
            set { _isBuyOptionsVisible = value; OnPropertyChanged(); }
        }

        public ObservableCollection<SplitBuyItem> SplitSellItems { get; } = new();

        private bool _isSellOptionsVisible;
        public bool IsSellOptionsVisible
        {
            get => _isSellOptionsVisible;
            set { _isSellOptionsVisible = value; OnPropertyChanged(); }
        }
        public ICommand SplitBuyCommand { get; }
        public ICommand SplitSellCommand { get; }

        public ICommand TogglePlayCommand { get; }
        public ICommand PlaySpeedCommand { get; }
        // ===== 周期切换（日/周） =====
        public KLinePeriod SelectedPeriod { get; private set; } = KLinePeriod.Day;
        public bool IsPeriodDay => SelectedPeriod == KLinePeriod.Day;
        public bool IsPeriodWeek => SelectedPeriod == KLinePeriod.Week;
        public bool IsPeriodMonth => SelectedPeriod == KLinePeriod.Month;
        public ICommand SwitchPeriodCommand { get; }
        // ===== 涨停训练 =====
        public bool IsLimitUpMode => _config.IsLimitUpMode;
        /// <summary>涨停模式仅日线：false 时隐藏/禁用周期切换按钮（XAML 绑它）</summary>
        public bool ShowPeriodSwitch => !_config.IsLimitUpMode;
        // ===== 持有/观望连击计数（周线模式：一根周K = 5次点击） =====
        private int _holdWatchClicks = 0;

        /// <summary>推进一根K线所需的"持有/观望"点击次数：日线1次，周线5次（一个交易周）</summary>
        private int ClicksPerStep => SelectedPeriod == KLinePeriod.Day || _config.WeekMonthDayJump ? 1 : 5;

        // ===== 自动播放 =====
        private DispatcherTimer? _playTimer;
        private bool _isPlaying = false;
        private double _playSpeed = 1.0;

        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayBtnText)); }
        }

        public string PlayBtnText => _isPlaying ? "暂停" : "自动播放";

        public double PlaySpeed
        {
            get => _playSpeed;
            set
            {
                _playSpeed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSpeed05x));
                OnPropertyChanged(nameof(IsSpeed1x));
                OnPropertyChanged(nameof(IsSpeed2x));
                OnPropertyChanged(nameof(IsSpeed4x));
                if (_playTimer != null)
                    _playTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / value / ClicksPerStep);
            }
        }

        public bool IsSpeed05x => Math.Abs(_playSpeed - 0.5) < 0.001;
        public bool IsSpeed1x => Math.Abs(_playSpeed - 1.0) < 0.001;
        public bool IsSpeed2x => Math.Abs(_playSpeed - 2.0) < 0.001;
        public bool IsSpeed4x => Math.Abs(_playSpeed - 4.0) < 0.001;

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
        // ===== K线形态（PatternDetector 识别结果，训练提示用）=====
        private string _patternText = "--";
        public string PatternText
        {
            get => _patternText;
            set { _patternText = value; OnPropertyChanged(); }
        }

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
        // ===== 批量刷新：多图联动更新时挂起，全部画完再一次刷新，消除闪烁 =====
        private bool _refreshSuspended = false;

        private void BeginBatchUpdate() => _refreshSuspended = true;

        private void EndBatchUpdate()
        {
            _refreshSuspended = false;
            KlinePlot?.Refresh();
            VolPlot?.Refresh();
            MacdPlot?.Refresh();
        }

        /// <summary>三图一次画完再一次刷新（替代逐个 Update + 逐个 Refresh）</summary>
        private void UpdateAllCharts()
        {
            BeginBatchUpdate();
            try
            {
                UpdatePriceChart();
                UpdateVolChart();
                UpdateMacdChart();
            }
            finally
            {
                EndBatchUpdate();
            }
        }
        // ===== 十字光标价格标签（WPF 覆盖层，由 KLineTrainWindow 注入）=====
        public Border? CursorPriceLabel { get; set; }
        public TextBlock? CursorPriceText { get; set; }

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
        public string FrozenText => !IsT0Mode && _hasPosition && _currentVisibleBars <= _buyBarIndex + 1 ? "1" : "0";
        /// <summary>冻结标签文字：期货 T+0</summary>
        public string FrozenLabel => IsT0Mode ? "T+0" : "T+1冻结";
        public int RemainingBars => TotalBarsForPeriod - _currentVisibleBars;
        public int OpenCount => _openCount;
        public int ProfitCount => _profitCount;
        public int WatchDays => _watchDays;
        public int HoldDays => _holdDays;
        public int HeavyHoldDays => _heavyHoldDays;
        public string ElapsedText => $"{(int)_elapsed.TotalSeconds}s";
        // 杠杆与借款显示
        public int LeverageText => Math.Max(1, _config?.Leverage ?? 1);

        public string BorrowText
        {
            get
            {
                double debt = Math.Max(0, -_cash);
                return debt > 0.01 ? debt.ToString("F2") : "0.00";
            }
        }

        // 当前股票代码（信息栏显示，避免用户对"看到的是哪只股"产生困惑）
        public string CurrentStockCode => SelectedStock ?? "--";

        // 有借款时红色显示，无借款灰色
        public Brush BorrowBrush => Math.Max(0, -_cash) > 0.01
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x32, 0x32))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));

        public string BuyBtnSubText
        {
            get
            {
                // 分仓模式：根据剩余额度显示
                if (_config != null && _config.IsSplitPosition)
                {
                    double maxRatio = _config.SplitPositionPercent / 100.0;
                    double usedRatio = _holdBuyAmount / _initialFirecrackers;
                    int remainingTenths = (int)Math.Floor((maxRatio - usedRatio) * 10);
                    remainingTenths = Math.Max(0, remainingTenths);

                    if (remainingTenths <= 0) return "无仓位可买";
                    return $"可买{remainingTenths}/10仓";
                }

                // 非分仓模式
                if (_hasPosition) return "无仓位可买";
                return "可买10/10仓";
            }
        }

        public string SellBtnSubText
        {
            get
            {
                if (!_hasPosition) return "无仓位可卖";
                if (_currentVisibleBars <= _buyBarIndex + 1) return IsT0Mode ? "T+0 可卖" : "T+1冻结";
                return "可卖1/1仓";
            }
        }

        // ===== 开仓收益 =====
        public string OpenProfitAmount => _hasPosition && _holdBuyAmount > 0
    ? ((CurrentMarketValue - _holdBuyAmount) / _holdBuyAmount * 100).ToString("F2") + "%"
    : "0.00%";

        public double OpenProfitPct => _hasPosition && _holdBuyAmount > 0
            ? (CurrentMarketValue - _holdBuyAmount) / _holdBuyAmount * 100
            : 0;

        // ===== 本局收益 =====
        public string TotalProfitAmount => _initialFirecrackers > 0
? ((TotalFirecrackers - _initialFirecrackers) / _initialFirecrackers * 100).ToString("F2") + "%"
    : "0.00%";
        public double TotalProfitPct => _initialFirecrackers > 0
            ? (TotalFirecrackers - _initialFirecrackers) / _initialFirecrackers * 100
            : 0;

        public Brush OpenProfitBrush => GetProfitBrush(OpenProfitPct);
        public Brush TotalProfitBrush => GetProfitBrush(TotalProfitPct);

        private Brush GetProfitBrush(double value)
        {
            if (value > 0) return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x32, 0x32));
            if (value < 0) return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xA8, 0x00));
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));
        }

        public MainViewModel(TrainingConfig config)
        {
            _config = config;
            // 2026-09-30：数据库路径改走 AppPaths（老用户 E 盘优先，开源用户放程序目录 stockdata\）
            string dbPath = AppPaths.GetDbPath();
            _dbService = new DatabaseService(dbPath);
            _limitUpService = new LimitUpService(dbPath);

            RefreshCommand = new RelayCommand(_ => LoadData());
            RandomStockCommand = new RelayCommand(async _ => await LoadRandomStock());
            NextStockCommand = new RelayCommand(async _ => await LoadRandomStock());
            ToggleTrainingCommand = new RelayCommand(_ => ToggleTrainingMode());
            RevealAnswerCommand = new RelayCommand(_ => RevealAnswer(), _ => _isTrainingMode && !_isAnswerRevealed);
            NextTrainingCommand = new RelayCommand(async _ => await LoadRandomStock(), _ => _isTrainingMode);
            HoldOrWatchCommand = new RelayCommand(_ => OnHoldOrWatch(), _ => !_isAnswerRevealed && _currentVisibleBars < TotalBarsForPeriod);
            BuyCommand = new RelayCommand(_ => ExecuteBuy(), _ => CanBuy());
            SellCommand = new RelayCommand(_ => ExecuteSell(), _ => CanSell());
            SettleCommand = new RelayCommand(_ => ExecuteSettle(), _ => _isTrainingMode);
            SplitBuyCommand = new RelayCommand(p =>
            {
                if (p is int percent) ExecuteSplitBuy(percent);
            }, _ => CanBuy());

            SplitSellCommand = new RelayCommand(p =>
            {
                if (p is int percent) ExecuteSplitSell(percent);
            }, _ => CanSell());

            TogglePlayCommand = new RelayCommand(_ => TogglePlay(), _ => CanTogglePlay());
            PlaySpeedCommand = new RelayCommand(p =>
            {
                if (p is string s && double.TryParse(s, out double v)) PlaySpeed = v;
            });
            SwitchPeriodCommand = new RelayCommand(p =>
            {
                // ★ Bug1 修复②：limit-up 模式在改 SelectedPeriod 之前直接拦截，杜绝状态错乱
                if (_config.IsLimitUpMode) return;
                if (p is string s && Enum.TryParse<KLinePeriod>(s, out var period) && period != SelectedPeriod)
                {
                    var oldPeriod = SelectedPeriod;   // 记住旧周期：进度映射和失败回滚都要用
                    SelectedPeriod = period;
                    OnPropertyChanged(nameof(IsPeriodDay));
                    OnPropertyChanged(nameof(IsPeriodWeek));
                    OnPropertyChanged(nameof(IsPeriodMonth));
                    SwitchPeriodKeepAnchor(oldPeriod);
                }
            });

            RebuildSellItems();

            var swInit = Stopwatch.StartNew();   // ★ 诊断：启动加载耗时
            LoadStockList();
            Debug.WriteLine($"[DIAG] LoadStockList: {swInit.ElapsedMilliseconds}ms");
            _currentVisibleBars = TrainingBarsForPeriod;

            // 本金滚动：新一局从"累计总火星币"开局（10000 + 历史所有局盈亏之和），
            // 与首页曲线最后一点严格一致；读取失败则回退 10000
            try
            {
                _dbService.EnsureTrainingRecordTable();
                var (_, latestTotal) = _dbService.GetHomeSummary();

                // ★ 破产保护（兜底）：正常路径已在结算点重置；这里接住历史遗留的 ≤0 老数据/异常中断场景
                if (latestTotal <= BankruptcyFirecrackers)
                {
                    _initialFirecrackers = DefaultInitialFirecrackers;
                    _dbService.SaveTrainingRecord(new TrainingRecordEntry
                    {
                        TrainTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        StockCode = "RESET",
                        StockName = "破产重置",
                        Period = "Day",
                        StartDate = "",
                        EndDate = "",
                        InitialFirecrackers = latestTotal,
                        FinalFirecrackers = DefaultInitialFirecrackers,
                        ProfitAmount = DefaultInitialFirecrackers - latestTotal,
                        // ★ 训练记录标签：把本局训练类型存进 config_json（该列原为空置，不用改表）
                        //   首页"训练记录"列表读取此值显示对应标签；老记录为空 → 兜底显示"双盲训练"
                        ConfigJson = _config.IsLimitUpMode ? "LimitUp" : (_config.TrainTarget ?? "Stock"),
                    });
                    MessageBox.Show(
                        $"💥 破产！\n\n火星币已归零或透支（当前 {latestTotal:F2}），本金已重置为 {DefaultInitialFirecrackers:F0}，重新开始！",
                        "破产重置", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    _initialFirecrackers = latestTotal;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DIAG] 读取累计火星币失败，按 10000 开局: {ex.Message}");
            }

            _cash = _initialFirecrackers;
            // 初始化分仓档位按钮（如50% → 1成~5成共5个按钮）
            IsBuyOptionsVisible = false;
            RebuildBuyItems();

            _isInitializing = false;

            if (StockList.Count > 0)
                _ = LoadRandomStock();   // ★ 首抽异步：窗口先显示，数据到了再画

            // 主题切换：图表配色跟随（DetachTheme 由训练窗 Closed 时调用退订）
            ThemeService.ThemeChanged += OnThemeChanged;
        }

        private bool CanSell() =>
    _isTrainingMode &&
    !_isAnswerRevealed &&
    _hasPosition &&
    _buyBarIndex >= 0 &&
    (IsT0Mode || _currentVisibleBars > _buyBarIndex + 1);   // ★ 期货/港股 T+0；股票/指数 T+1：买入当天不可卖

        private void ExecuteSell()
        {
            if (!CanSell()) return;

            // 分仓模式：S = 展开/收起卖出档位面板
            if (_config.IsSplitPosition)
            {
                IsBuyOptionsVisible = false;
                IsSellOptionsVisible = !IsSellOptionsVisible;
                return;
            }

            // ===== 非分仓模式：一次性全仓卖出 =====
            if (SellAll("卖出"))
            {
                CommandManager.InvalidateRequerySuggested();
                AutoSkipAfterTradeIfNeeded();   // 买卖自动跳
            }
        }

        /// <summary>
        /// 切换日/周/月线时保持"当前股票 + 当前价位 + 整局时间跨度"不变。
        /// 当前位置 = 最后一根可见K线；本局结束日 = 旧窗口末根日期（固定）。
        /// 新窗口 = [训练段 + 不晚于结束日的未来段]——切周期只改K线粒度，
        /// 当前价、持仓盈亏、剩余天数在三周期间完全一致，不再按比例重新起算未来段。
        /// </summary>
        private void SwitchPeriodKeepAnchor(KLinePeriod oldPeriod)
        {
            // ★ Bug1：涨停锁定已在 SwitchPeriodCommand 入口（改 SelectedPeriod 之前）拦截，这里不再重复判断

            if (string.IsNullOrEmpty(SelectedStock) || _currentDataList == null || _currentDataList.Count == 0)
            {
                _ = LoadRandomStock();
                return;
            }

            bool wasRevealed = _isAnswerRevealed;
            int anchorIdx = wasRevealed
                ? _currentDataList.Count - 1
                : Math.Min(_currentVisibleBars, _currentDataList.Count) - 1;
            if (anchorIdx < 0) return;

            DateTime curDate = _currentDataList[anchorIdx].Date;                       // ← 当前位置：最后一根可见K线
            DateTime gameEndDate = _currentDataList[_currentTotalBars - 1].Date;       // ← 本局结束日期（固定不变）
                                                                                       // ★ 切换前快照：旧窗口里 B/S 标记与"开始"线都是按索引存的，索引跨周期无法映射，
                                                                                       //   但日期可以——先记住它们各自的日期，新窗口建成后再映射回去
            var markerDates = _tradeMarkers
                .Where(m => m.BarIndex >= 0 && m.BarIndex < _currentDataList.Count)
                .Select(m => (m.Type, Date: _currentDataList[m.BarIndex].Date))
                .ToList();
            int smIdx = (int)Math.Floor(_startMarkerIndex);
            DateTime? startMarkerDate = (_isTrainingMode && _startMarkerIndex >= 0 && smIdx < _currentDataList.Count)
                ? _currentDataList[smIdx].Date
                : (DateTime?)null;
            Debug.WriteLine($"[DIAG] Switch {oldPeriod}->{SelectedPeriod}: cur={curDate:yyyy-MM-dd} end={gameEndDate:yyyy-MM-dd} revealed={_isAnswerRevealed} visible={_currentVisibleBars}/{_currentTotalBars}");

            var daily = IsIndexMode
                      ? _dbService.GetIndexData(SelectedStock, startDate: "19900101", endDate: "20251231", limit: 10000)
                      : IsFutureMode
                      ? _dbService.GetFutureData(SelectedStock, startDate: "19900101", endDate: "20261231", limit: 10000)
                      : IsHKMode
                      ? _dbService.GetHKData(SelectedStock, startDate: "19900101", endDate: "20261231", limit: 10000)
                      : IsUSMode
                      ? _dbService.GetUSData(SelectedStock, startDate: "19900101", endDate: "20261231", limit: 10000)
                      : IsBondMode
                      ? _dbService.GetBondData(SelectedStock, startDate: "19900101", endDate: "20261231", limit: 10000)
                      : _dbService.GetStockData(SelectedStock, startDate: "19900101", endDate: "20251231", limit: 10000);

            List<StockData> pastBars, futureBars;

            if (SelectedPeriod == KLinePeriod.Day)
            {
                // ★ 未来段按日期截断：只含不超过本局结束日的部分（切周期不改变游戏的时间跨度）
                pastBars = daily.Where(d => d.Date <= curDate).ToList();
                futureBars = daily.Where(d => d.Date > curDate && d.Date <= gameEndDate).ToList();
            }
            else
            {
                // 完整周期K线（指标在全量历史上有充分预热）
                var periodBars = KLineAggregator.ToPeriod(daily, SelectedPeriod);
                IndicatorCalculator.Fill(periodBars);

                // 决策周期 = 第一个 Date >= curDate 的K线（周期K线 Date 取期内最后交易日）
                int decisionIdx = periodBars.FindIndex(w => w.Date >= curDate);
                if (decisionIdx < 0)
                {
                    RollbackPeriod(oldPeriod,
                        $"在{PeriodName(SelectedPeriod)}线序列中找不到 {curDate:yyyy-MM} 附近的K线" +
                        $"（该股票{PeriodName(SelectedPeriod)}线数据范围为 {periodBars.First().Date:yyyy-MM} ~ {periodBars.Last().Date:yyyy-MM}）");
                    return;
                }

                // ★ 决策周期截断到 curDate：Close 精确等于当前价，且不泄露未来
                TruncatePeriodBar(periodBars[decisionIdx], daily, PeriodStartOf(curDate, SelectedPeriod), curDate);

                // ★ 未来段 = curDate 之后、且不晚于本局结束日"开始"的周期；
                //   最后一根若跨越结束日，截断到结束日（Close=本局最后收盘价）
                futureBars = new List<StockData>();
                for (int i = decisionIdx + 1; i < periodBars.Count; i++)
                {
                    var p = periodBars[i];
                    DateTime pStart = PeriodStartOf(p.Date, SelectedPeriod);
                    if (pStart > gameEndDate) break;
                    if (p.Date > gameEndDate)
                        TruncatePeriodBar(p, daily, pStart, gameEndDate);
                    futureBars.Add(p);
                }

                pastBars = periodBars.Take(decisionIdx + 1).ToList();
            }

            // ★ 保留训练进度：按"未来段完成比例"映射新旧周期的可见根数，
            //   切换后剩余根数 = 新周期未来段 × (1 − 进度)，不再重置为满额
            // ★ 新窗口 = [训练段 + 同日期范围内的未来段]。
            //   已推进的未来段在新周期里自然落入"训练段"（日期≤curDate），
            //   当前价 = 决策周期收盘价（截断保证 = 旧周期现价），盈亏三周期一致。
            int newTrainBars = TrainingBarsForPeriod;

            // ★ 未来段按目标周期的名义值封顶（日150/周50/月12）：
            //    月/周线出生的局切到日线时，未来段只给150天而非整个12个月跨度
            int nominalFuture = TotalBarsForPeriodFor(SelectedPeriod) - newTrainBars;
            if (futureBars.Count > nominalFuture)
                futureBars = futureBars.Take(nominalFuture).ToList();

            int pastTake = Math.Min(newTrainBars, pastBars.Count);

            // 真实历史低于下限才回滚（次新股上市时间太短，练无可练）
            if (pastBars.Count < MinPastBarsFor(SelectedPeriod))
            {
                RollbackPeriod(oldPeriod,
                    $"决策点（{curDate:yyyy-MM}）之前只有 {pastBars.Count} 根{PeriodName(SelectedPeriod)}K，" +
                    $"低于训练所需下限 {MinPastBarsFor(SelectedPeriod)} 根");
                return;
            }

            int newTotal = pastTake + futureBars.Count;
            // 未揭示：可见=pastTake（当前价=决策周期收盘）；已揭示或未来段耗尽：显示全部
            int needVisible = (wasRevealed || futureBars.Count == 0) ? newTotal : pastTake;
            Debug.WriteLine($"[DIAG] Switch built: past={pastBars.Count} future={futureBars.Count} pastTake={pastTake} newTotal={newTotal} visible={needVisible}");

            // 记录本局实际总根数（推进上限/剩余根数/结算都走它）
            _currentTotalBars = newTotal;
            if (SelectedPeriod == KLinePeriod.Month)
                _monthTotalBars = newTotal;
            OnPropertyChanged(nameof(TotalBarsForPeriod));
            _trainStartBarIdx = pastTake - 1;   // 训练起点（短历史窗口时随实际上限前移）

            _currentDataList = pastBars.Skip(pastBars.Count - pastTake)
                                       .Concat(futureBars.Take(newTotal - pastTake))
                                       .ToList();

            _startDate = _currentDataList[0].Date;
            OnPropertyChanged(nameof(StartDate));

            // ★ 周期切换 = 同一局训练换个视角：保留持仓/资金/统计/流水
            _holdWatchClicks = 0;
            StopPlay();

            // B/S 标记：按日期映射到新窗口（首个 Date >= 标记日期的周期K线 = 包含该日期的K线）
            _tradeMarkers.Clear();
            foreach (var (type, date) in markerDates)
            {
                int idx = _currentDataList.FindIndex(b => b.Date >= date);
                if (idx < 0) idx = _currentDataList.Count - 1;
                if (idx >= 0)
                    _tradeMarkers.Add(new TradeMarker { BarIndex = idx, Type = type });
            }

            // "开始"线：同样按日期重新定位（边界线 = 该日期所在K线的左边缘）
            if (startMarkerDate.HasValue)
            {
                int si = _currentDataList.FindIndex(b => b.Date >= startMarkerDate.Value);
                _startMarkerIndex = (si >= 0 ? si : _currentDataList.Count) - 0.5;
            }
            else
            {
                _startMarkerIndex = -1;
            }
            IsBuyOptionsVisible = false;
            IsSellOptionsVisible = false;
            // T+1 视为已满足（实际已过至少一天）：重置买入K线标记，解冻卖出
            _buyBarIndex = _hasPosition ? 0 : -1;
            RebuildBuyItems();
            RebuildSellItems();
            _isAnswerRevealed = wasRevealed;
            _currentVisibleBars = Math.Min(needVisible, _currentDataList.Count);   // ← 进度保留在这

            // 状态栏同步显示剩余
            TrainingStatus = $"训练模式：已推进 {_currentVisibleBars}/{newTotal}，剩余 {newTotal - _currentVisibleBars} 根";

            UpdatePattern();            // ★ 切周期后按新粒度重新识别
            UpdateAllCharts();          // ★ 批量刷新（原三连 UpdateXxx）
            UpdateInfoBar();
            NotifyAllStats();
            CommandManager.InvalidateRequerySuggested();
        }


        /// <summary>周期切换失败时回滚按钮状态，并弹出带诊断信息的提示（reason = 具体原因）</summary>
        private void RollbackPeriod(KLinePeriod oldPeriod, string reason)
        {
            string targetName = PeriodName(SelectedPeriod);
            SelectedPeriod = oldPeriod;
            OnPropertyChanged(nameof(IsPeriodDay));
            OnPropertyChanged(nameof(IsPeriodWeek));
            OnPropertyChanged(nameof(IsPeriodMonth));
            MessageBox.Show($"{SelectedStock} 无法切换到{targetName}线：\n\n{reason}",
                "周期切换失败", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>周期K线所属周期的起始日（月线=当月1号，周线=自然周一）</summary>
        private static DateTime PeriodStartOf(DateTime date, KLinePeriod period) => period == KLinePeriod.Month
            ? new DateTime(date.Year, date.Month, 1)
            : date.AddDays(-(((int)date.DayOfWeek + 6) % 7)).Date;

        /// <summary>
        /// 把周期K线截断到指定日期：只聚合 [periodStart, cutDate] 的交易日，
        /// Close = cutDate 当天收盘（= 当前价），Date = cutDate 当天，不泄露未来。
        /// </summary>
        private static void TruncatePeriodBar(StockData bar, List<StockData> daily, DateTime periodStart, DateTime cutDate)
        {
            var seg = daily.Where(d => d.Date >= periodStart && d.Date <= cutDate).ToList();
            if (seg.Count == 0) return;
            bar.Open = seg[0].Open;
            bar.High = seg.Max(x => x.High);
            bar.Low = seg.Min(x => x.Low);
            bar.Close = seg[seg.Count - 1].Close;
            bar.Volume = seg.Sum(x => x.Volume);
            bar.Date = seg[seg.Count - 1].Date;
        }

        private List<StockData> LoadPeriodSeries(string code)
        {
            // ★ 可转债模式：同指数，拿到日线后统一 Fill 现算
            if (IsBondMode)
            {
                var bDaily = _dbService.GetBondData(code, startDate: "19900101", endDate: "20261231", limit: 10000);
                IndicatorCalculator.Fill(bDaily);
                if (SelectedPeriod == KLinePeriod.Day) return bDaily;

                var bPeriod = KLineAggregator.ToPeriod(bDaily, SelectedPeriod);
                IndicatorCalculator.Fill(bPeriod);
                return bPeriod;
            }

            // ★ 美股模式：同指数，拿到日线后统一 Fill 现算
            if (IsUSMode)
            {
                var uDaily = _dbService.GetUSData(code, startDate: "19900101", endDate: "20261231", limit: 10000);
                IndicatorCalculator.Fill(uDaily);
                if (SelectedPeriod == KLinePeriod.Day) return uDaily;

                var uPeriod = KLineAggregator.ToPeriod(uDaily, SelectedPeriod);
                IndicatorCalculator.Fill(uPeriod);
                return uPeriod;
            }

            // ★ 港股模式：同指数，拿到日线后统一 Fill 现算
            if (IsHKMode)
            {
                var hDaily = _dbService.GetHKData(code, startDate: "19900101", endDate: "20261231", limit: 10000);
                IndicatorCalculator.Fill(hDaily);
                if (SelectedPeriod == KLinePeriod.Day) return hDaily;

                var hPeriod = KLineAggregator.ToPeriod(hDaily, SelectedPeriod);
                IndicatorCalculator.Fill(hPeriod);
                return hPeriod;
            }

            // ★ 期货模式：同指数，拿到日线后统一 Fill 现算
            if (IsFutureMode)
            {
                var fDaily = _dbService.GetFutureData(code, startDate: "19900101", endDate: "20261231", limit: 10000);
                IndicatorCalculator.Fill(fDaily);
                if (SelectedPeriod == KLinePeriod.Day) return fDaily;

                var fPeriod = KLineAggregator.ToPeriod(fDaily, SelectedPeriod);
                IndicatorCalculator.Fill(fPeriod);
                return fPeriod;
            }

            // ★ 指数模式：表里没有预算指标，拿到日线后统一 Fill 现算
            if (IsIndexMode)
            {
                var idxDaily = _dbService.GetIndexData(code, startDate: "19900101", endDate: "20251231", limit: 10000);
                IndicatorCalculator.Fill(idxDaily);          // 日线指标现算（毫秒级）
                if (SelectedPeriod == KLinePeriod.Day) return idxDaily;

                var idxPeriod = KLineAggregator.ToPeriod(idxDaily, SelectedPeriod);
                IndicatorCalculator.Fill(idxPeriod);
                return idxPeriod;
            }

            // ===== 以下个股逻辑原样保留 =====
            // 先取全量日线再聚合，保证切出的窗口前面有充足的指标预热数据
            var daily = _dbService.GetStockData(code, startDate: "19900101",
                                                endDate: "20251231", limit: 10000);
            if (SelectedPeriod == KLinePeriod.Day) return daily;

            var periodBars = KLineAggregator.ToPeriod(daily, SelectedPeriod);
            IndicatorCalculator.Fill(periodBars);
            return periodBars;
        }

        private void LoadStockList()
        {
            // ★ 涨停训练：换数据源（涨停票池 = 2015 年后 >=2 次涨停的非 ST 个股，LimitUpService 内缓存）
            //   StockList 只填代码，与个股模式一致；抽段走 LoadLimitUpRandomStock，不用 _stockDailyCounts
            if (_config.IsLimitUpMode)
            {
                var limitUpPool = _limitUpService.GetLimitUpStocks();
                StockList.Clear();
                foreach (var s in limitUpPool) StockList.Add(s.Code);

                Debug.WriteLine($"[DIAG] LoadStockList(涨停): {limitUpPool.Count} 只");
                return;
            }

            // ★ 期货模式：换数据源（期货无 ST/市场概念，不走个股过滤）
            if (IsFutureMode)
            {
                var futureCodes = _dbService.GetAllFutureCodes();
                StockList.Clear();
                foreach (var s in futureCodes) StockList.Add(s);

                _stockDailyCounts.Clear();
                foreach (var kv in _dbService.GetFutureDailyCounts())
                    _stockDailyCounts[kv.Key] = kv.Value;
                _eligibleCache.Clear();

                Debug.WriteLine($"[DIAG] LoadStockList(期货): {futureCodes.Count} 个品种");
                return;
            }

            // ★ 港股模式：换数据源（港股无 ST/市场概念）
            if (IsHKMode)
            {
                var hkCodes = _dbService.GetAllHKCodes();
                StockList.Clear();
                foreach (var s in hkCodes) StockList.Add(s);

                _stockDailyCounts.Clear();
                foreach (var kv in _dbService.GetHKDailyCounts())
                    _stockDailyCounts[kv.Key] = kv.Value;
                _eligibleCache.Clear();

                Debug.WriteLine($"[DIAG] LoadStockList(港股): {hkCodes.Count} 只");
                return;
            }

            // ★ 美股模式：换数据源（美股无 ST/市场概念）
            if (IsUSMode)
            {
                var usCodes = _dbService.GetAllUSCodes();
                StockList.Clear();
                foreach (var s in usCodes) StockList.Add(s);

                _stockDailyCounts.Clear();
                foreach (var kv in _dbService.GetUSDailyCounts())
                    _stockDailyCounts[kv.Key] = kv.Value;
                _eligibleCache.Clear();

                Debug.WriteLine($"[DIAG] LoadStockList(美股): {usCodes.Count} 只");
                return;
            }

            // ★ 可转债模式：换数据源（转债无 ST/市场概念）
            if (IsBondMode)
            {
                var bondCodes = _dbService.GetAllBondCodes();
                StockList.Clear();
                foreach (var s in bondCodes) StockList.Add(s);

                _stockDailyCounts.Clear();
                foreach (var kv in _dbService.GetBondDailyCounts())
                    _stockDailyCounts[kv.Key] = kv.Value;
                _eligibleCache.Clear();

                Debug.WriteLine($"[DIAG] LoadStockList(可转债): {bondCodes.Count} 只");
                return;
            }

            // ★ 指数模式：换数据源（指数无 ST/市场概念，不走个股过滤）
            if (IsIndexMode)
            {
                var indexCodes = _dbService.GetAllIndexCodes();
                StockList.Clear();
                foreach (var s in indexCodes) StockList.Add(s);

                _stockDailyCounts.Clear();
                foreach (var kv in _dbService.GetIndexDailyCounts())
                    _stockDailyCounts[kv.Key] = kv.Value;
                _eligibleCache.Clear();

                Debug.WriteLine($"[DIAG] LoadStockList(指数): {indexCodes.Count} 个指数");
                return;
            }
            var stocks = _dbService.GetAllStockCodes(_config.RemoveST);
            StockList.Clear();
            foreach (var s in stocks) StockList.Add(s);

            // 一条 GROUP BY 拿全部日线根数（原实现：1356 只 × 全量拉数据只为数个数，启动极慢）
            _stockDailyCounts.Clear();
            foreach (var kv in _dbService.GetDailyBarCounts(_config.RemoveST))
                _stockDailyCounts[kv.Key] = kv.Value;
            _eligibleCache.Clear();

            Debug.WriteLine($"[DIAG] LoadStockList: {stocks.Count} stocks loaded");
        }

        /// <summary>周期中文名（提示文案用）</summary>
        private static string PeriodName(KLinePeriod p) => p == KLinePeriod.Day ? "日" : p == KLinePeriod.Week ? "周" : "月";

        /// <summary>各周期对应的最低日线根数门槛：日 270 / 周 850（约3.4年）/ 月 1584（约6.4年）</summary>
        private int MinDailyBarsForPeriod(KLinePeriod period) => period switch
        {
            KLinePeriod.Week => WeekTotalBars * 5,      // 170周 × 5个交易日
            KLinePeriod.Month => (TrainingBarsFor(KLinePeriod.Month) + MonthMinFutureBars) * 22,  // 66个月 ≈ 5.5年
            _ => _totalBars,
        };

        /// <summary>市场类型匹配（注意：当前数据库为纯创业板，选主板/科创板将无结果）</summary>
        private bool IsMarketMatch(string code) => _config.MarketType switch
        {
            "CYB" => code.StartsWith("30"),
            "KCB" => code.StartsWith("68"),
            "Main" => code.StartsWith("60") || code.StartsWith("00"),
            _ => true,
        };

        /// <summary>训练时间段过滤：决策点（训练窗口末根）日期必须落在所选范围</summary>
        private bool IsDateInTrainPeriod(DateTime decisionDate)
        {
            if (_config.TrainPeriod == "5Y") return decisionDate >= DateTime.Today.AddYears(-5);
            if (_config.TrainPeriod == "10Y") return decisionDate >= DateTime.Today.AddYears(-10);
            if (_config.TrainPeriod == "Before10Y") return decisionDate <= DateTime.Today.AddYears(-10);
            return true;
        }

        /// <summary>按当前周期取合格股票池（惰性缓存，每个周期只筛一次）</summary>
        private List<string> GetEligibleStocks()
        {
            if (_eligibleCache.TryGetValue(SelectedPeriod, out var cached))
                return cached;

            int need = MinDailyBarsForPeriod(SelectedPeriod);
            var list = _stockDailyCounts.Where(kv => kv.Value >= need && (IsIndexMode || IsFutureMode || IsHKMode || IsUSMode || IsBondMode || IsMarketMatch(kv.Key)))
                .Select(kv => kv.Key)
                .ToList();
            _eligibleCache[SelectedPeriod] = list;
            return list;
        }

        private bool _isLoading;   // ★ Bug7 修复：抽局并发保护——上一次还没抽完时再次调用直接忽略

        private async Task LoadRandomStock()
        {
            if (_isLoading) return;   // 抽局进行中，忽略本次调用
            _isLoading = true;
            try
            {
                await LoadRandomStockCore();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async Task LoadRandomStockCore()
        {
            var swTotal = Stopwatch.StartNew();   // ★ 诊断：抽段总耗时
            _startMarkerIndex = -1;
            ResetTrainingStats();

            // ★ 涨停训练：从涨停票池抽票，锚定"末根可见K线 = 涨停日"的 270 根窗口
            if (_config.IsLimitUpMode)
            {
                await LoadLimitUpRandomStockCore();
                return;
            }

            var pool = GetEligibleStocks();
            if (pool.Count == 0)
            {
                MessageBox.Show($"数据库中没有一只股票拥有足够的{PeriodName(SelectedPeriod)}线历史数据（约需 {MinDailyBarsForPeriod(SelectedPeriod)} 根日线）");
                return;
            }

            int minBars = SelectedPeriod == KLinePeriod.Month
                ? TrainingBarsForPeriod + MonthMinFutureBars
                : TotalBarsForPeriodFor(SelectedPeriod);

            // 随机选段挪进重试循环：根数不足 / 决策点不在所选训练时间段 → 重选
            List<StockData> series = new List<StockData>();
            string newStock = "";
            bool found = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                if (pool.Count == 1) newStock = pool[0];
                else { do { newStock = pool[_random.Next(pool.Count)]; } while (newStock == _selectedStock); }

                var allData = await Task.Run(() => LoadPeriodSeries(newStock));   // ★ 重查询后台跑，不卡 UI
                if (allData.Count < minBars) continue;

                if (SelectedPeriod == KLinePeriod.Month)
                {
                    int startIndex = _random.Next(allData.Count - minBars + 1);
                    int availFuture = allData.Count - startIndex - TrainingBarsForPeriod;
                    int future = Math.Clamp(availFuture, MonthMinFutureBars, MonthTotalBars - TrainingBarsForPeriod);
                    var candidate = allData.Skip(startIndex).Take(TrainingBarsForPeriod + future).ToList();
                    if (!IsDateInTrainPeriod(candidate[TrainingBarsForPeriod - 1].Date)) continue;
                    _monthTotalBars = candidate.Count;
                    series = candidate;
                }
                else
                {
                    int startIndex = _random.Next(allData.Count - TotalBarsForPeriodFor(SelectedPeriod) + 1);
                    var candidate = allData.Skip(startIndex).Take(TotalBarsForPeriodFor(SelectedPeriod)).ToList();
                    if (!IsDateInTrainPeriod(candidate[TrainingBarsForPeriod - 1].Date)) continue;
                    series = candidate;
                }
                found = true;
                break;
            }

            if (!found)
            {
                string extra = _config.TrainPeriod != "All" ? "，或决策点不在所选训练时间段内" : "";
                MessageBox.Show($"连续 40 只股票的{PeriodName(SelectedPeriod)}线数据不足 {minBars} 根{extra}，无法开始训练");
                return;
            }

            _currentDataList = series;
            _trainStartBarIdx = TrainingBarsForPeriod - 1;
            _currentTotalBars = _currentDataList.Count;
            OnPropertyChanged(nameof(TotalBarsForPeriod));

            // 新局窗口重建后必须重置可见根数
            _currentVisibleBars = TrainingBarsForPeriod;

            _startDate = _currentDataList[0].Date;
            OnPropertyChanged(nameof(StartDate));
            _selectedStock = newStock;
            OnPropertyChanged(nameof(SelectedStock));
            OnPropertyChanged(nameof(CurrentStockCode));

            UpdatePattern();            // ★ 开局识别形态
            UpdateAllCharts();          // ★ 批量刷新（原三连 UpdateXxx）
            UpdateInfoBar();
            NotifyAllStats();
            Debug.WriteLine($"[DIAG] LoadRandomStock: {swTotal.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// 涨停训练抽局（2026-09-28）：从"2015 年后 >=2 次涨停的非 ST 票池"随机抽票，
        /// 再随机锚定一个合格涨停日——最后一根可见K线 = 涨停日，其后 150 根 = 真实未来，
        /// 窗口结构 270 = 120 训练 + 150 未来，与日线完全一致（T+1/费用/结算/形态识别全部复用）。
        /// 合格锚点 = 前面够 120+20 根（可见 + 指标预热）、后面够 150 根未来、且落在所选训练时间段。
        /// </summary>
        private async Task LoadLimitUpRandomStock()
        {
            if (_isLoading) return;   // 抽局进行中，忽略本次调用
            _isLoading = true;
            try
            {
                await LoadLimitUpRandomStockCore();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async Task LoadLimitUpRandomStockCore()
        {
            var swTotal = Stopwatch.StartNew();   // ★ 诊断：涨停抽段总耗时
            const int trainBars = 120, futureBars = 150;

            var pool = _limitUpService.GetLimitUpStocks();
            if (pool.Count == 0)
            {
                MessageBox.Show("涨停票池为空：cy_daily 中没有 2015 年后 >=2 次涨停的非 ST 个股");
                return;
            }

            List<StockData> series = new List<StockData>();
            string newStock = "";
            DateTime anchorDate = default;
            bool found = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var pick = pool[_random.Next(pool.Count)];
                if (pool.Count > 1 && pick.Code == _selectedStock) continue;

                var allData = await Task.Run(() => LoadPeriodSeries(pick.Code));   // ★ 重查询后台跑；日线全量（涨停锁定日线，无聚合开销）
                if (allData.Count < trainBars + futureBars + 20) continue;

                var seg = LimitUpService.PickSegment(allData, pick.Code, trainBars, futureBars, _random);
                if (seg == null) continue;
                if (!IsDateInTrainPeriod(seg[trainBars - 1].Date)) continue;   // 训练时间段过滤（5Y/10Y/Before10Y）

                series = seg;
                newStock = pick.Code;
                anchorDate = seg[trainBars - 1].Date;
                found = true;
                break;
            }

            if (!found)
            {
                string extra = _config.TrainPeriod != "All" ? "，或涨停日不在所选训练时间段内" : "";
                MessageBox.Show($"连续 40 次未找到合格的涨停锚点（数据不足 290 根{extra}），无法开始涨停训练");
                return;
            }

            _limitUpAnchorDate = anchorDate;
            _currentDataList = series;
            _trainStartBarIdx = trainBars - 1;
            _currentTotalBars = series.Count;
            OnPropertyChanged(nameof(TotalBarsForPeriod));

            // 新局窗口重建后必须重置可见根数
            _currentVisibleBars = trainBars;

            _startDate = _currentDataList[0].Date;
            OnPropertyChanged(nameof(StartDate));
            _selectedStock = newStock;
            OnPropertyChanged(nameof(SelectedStock));
            OnPropertyChanged(nameof(CurrentStockCode));

            UpdatePattern();            // ★ 开局识别形态
            UpdateAllCharts();          // ★ 批量刷新（原三连 UpdateXxx）
            UpdateInfoBar();
            InfoText = $"【涨停训练】锚定涨停日 {anchorDate:yyyy-MM-dd} ｜ {InfoText}";
            NotifyAllStats();
            Debug.WriteLine($"[DIAG] LoadLimitUpRandomStock: {swTotal.ElapsedMilliseconds}ms");
        }

        public void LoadData()
        {
            // ★ 指数/期货/港股/美股/可转债：窗口数据已由 LoadRandomStock 建好；这里只补首屏重绘（ctor 时 KlinePlot 为 NULL 的那次空画）
            if (IsIndexMode || IsFutureMode || IsHKMode || IsUSMode || IsBondMode)
            {
                UpdateAllCharts();
                UpdateInfoBar();
                return;
            }

            // ★ 涨停训练：换一局 = 重新抽涨停锚点段（与 RandomStockCommand 同路径，避免 LoadData 普通分支的日期查询逻辑）
            if (_config.IsLimitUpMode)
            {
                _ = LoadRandomStock();
                return;
            }

            // 周线模式不支持按起止日期精确定位，直接随机换一局
            if (SelectedPeriod != KLinePeriod.Day)
            {
                _ = LoadRandomStock();
                return;
            }
            Debug.WriteLine($"[DIAG] LoadData called, SelectedStock={SelectedStock}");
            if (string.IsNullOrEmpty(SelectedStock)) return;

            // ★ Bug2 修复：刷新 = 取 20251231 之前最近 TotalBarsForPeriod 根（不再从 _startDate 起查，杜绝假刷新）
            var allData = _dbService.GetStockData(SelectedStock, endDate: "20251231", limit: 2000);

            if (allData.Count >= TotalBarsForPeriod)
            {
                _currentDataList = allData.Skip(allData.Count - TotalBarsForPeriod).Take(TotalBarsForPeriod).ToList();
                Debug.WriteLine($"[DIAG] Fetched recent {TotalBarsForPeriod} bars before 2026");
            }
            else if (allData.Count > 0)
            {
                _currentDataList = allData;
                Debug.WriteLine($"[DIAG] Warning: Stock only has {_currentDataList.Count} bars before 2026");
            }
            else
            {
                MessageBox.Show("未找到 2026 年之前的数据");
                return;
            }

            _startDate = _currentDataList[0].Date;
            OnPropertyChanged(nameof(StartDate));
            Debug.WriteLine($"[DIAG] Data loaded: {_currentDataList.Count} bars from {_startDate:yyyy-MM-dd}");

            _currentTotalBars = _currentDataList.Count;
            OnPropertyChanged(nameof(TotalBarsForPeriod));
            UpdateAllCharts();          // ★ 批量刷新（原三连 UpdateXxx）
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

            int windowEnd = !_isAnswerRevealed ? Math.Min(_currentVisibleBars, dataList.Count) : dataList.Count;
            // ★ 保险：windowStart 不得超过 windowEnd（状态错位时 visibleCount 为负会导致 Enumerable.Range 抛异常）
            int windowStart = !_isAnswerRevealed ? Math.Min(Math.Max(0, _currentVisibleBars - TrainingBarsForPeriod), windowEnd) : 0;
            int visibleCount = windowEnd - windowStart;

            KlinePlot.Plot.Clear();
            ApplyPlotTheme(KlinePlot.Plot);   // ★ 主题：背景/坐标轴色随当前主题
            Debug.WriteLine("[DIAG] Plot cleared");

            // ===== 手动绘制 K 线（支持单根颜色 + 右侧内投影）=====
            var mainBars = new List<ScottPlot.Bar>();
            var shadowBars = new List<ScottPlot.Bar>();

            // ★ 一字板最小可视高度：按当前窗口价格波动区间的 0.8% 定，
            //   保证"一字涨停/跌停"（开=高=低=收）也能画出可见的实体
            double priceRange = 0;
            if (visibleCount > 0)
            {
                var vis = dataList.Skip(windowStart).Take(visibleCount);
                priceRange = vis.Max(d => d.High) - vis.Min(d => d.Low);
            }
            double minBodyH = visibleCount > 0
                ? Math.Max(priceRange * 0.008, dataList[windowStart].Close * 0.000001)
                : 0;

            for (int i = windowStart; i < windowEnd; i++)
            {
                var d = dataList[i];
                double x = i;
                bool isRising = d.Close >= d.Open;
                double prevClose = i > 0 ? dataList[i - 1].Close : d.Open;
                double changePct = prevClose > 0 ? (d.Close - prevClose) / prevClose : 0;

                // 颜色判断（涨跌停阈值按代码前缀：30/68 开头 20%，其余 10%；指数/期货/港股等无涨跌停，跳过黄/蓝）
                double limitThreshold = (SelectedStock?.StartsWith("30") == true || SelectedStock?.StartsWith("68") == true) ? 0.195 : 0.095;
                SPColor mainColor;
                if (!NoLimitMode && changePct >= limitThreshold)
                    mainColor = SPColor.FromHex("#ffb600");      // 涨停黄
                else if (!NoLimitMode && changePct <= -limitThreshold)
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
                if (bodyTop - bodyBottom < minBodyH)
                {
                    // 一字板：以实体中心（=涨停价）上下各扩一半最小高度，画出可见方块
                    double mid = (bodyTop + bodyBottom) / 2;
                    bodyTop = mid + minBodyH / 2;
                    bodyBottom = mid - minBodyH / 2;
                }

                // ===== 上影线 =====
                if (d.High > bodyTop)
                {
                    var upperLine = KlinePlot.Plot.Add.Line(
                        new Coordinates(x, bodyTop),
                        new Coordinates(x, d.High)
                    );
                    upperLine.Color = mainColor;
                    upperLine.LineWidth = 1.0f;
                }

                // ===== 下影线 =====
                if (d.Low < bodyBottom)
                {
                    var lowerLine = KlinePlot.Plot.Add.Line(
                        new Coordinates(x, d.Low),
                        new Coordinates(x, bodyBottom)
                    );
                    lowerLine.Color = mainColor;
                    lowerLine.LineWidth = 1.0f;
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

                // 右侧内投影
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
            Debug.WriteLine($"[DIAG] After SetLimitsX: XRange={KlinePlot.Plot.Axes.GetLimits().XRange}, YRange={KlinePlot.Plot.Axes.GetLimits().YRange}");

            // ===== 十字光标 =====
            _klineVLine = KlinePlot.Plot.Add.VerticalLine(0);
            _klineVLine.Color = ChartTheme.Crosshair(ThemeService.Current);
            _klineVLine.LineWidth = 0.5f;
            _klineVLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _klineVLine.IsVisible = false;

            _klineHLine = KlinePlot.Plot.Add.HorizontalLine(0);
            _klineHLine.Color = ChartTheme.Crosshair(ThemeService.Current);
            _klineHLine.LineWidth = 0.5f;
            _klineHLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _klineHLine.IsVisible = false;

            // 优化网格线
            KlinePlot.Plot.Grid.MajorLineColor = ChartTheme.Grid(ThemeService.Current);
            KlinePlot.Plot.Grid.MajorLineWidth = 0.5f;


            // ===== 训练开始标记线（蓝色竖虚线）=====
            if (_isTrainingMode && _startMarkerIndex >= 0)
            {
                var marker = KlinePlot.Plot.Add.VerticalLine(_startMarkerIndex);
                marker.Color = SPColor.FromHex("#2196F3");
                marker.LineWidth = 2;
                marker.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Dense");
            }

            // ===== 绘制 B/S 标记和"开始"标签 =====
            DrawTradeMarkers(dataList, windowStart, windowEnd);

            if (!_refreshSuspended) KlinePlot.Refresh();
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

            // 绘制"开始"标签（修复：放在可见区域顶部，向下展开避免被裁）
            if (_isTrainingMode && _startMarkerIndex >= 0)
            {
                int startIdx = (int)Math.Floor(_startMarkerIndex);
                if (startIdx >= windowStart && startIdx < windowEnd && startIdx < dataList.Count)
                {
                    var limits = KlinePlot!.Plot.Axes.GetLimits();
                    double yMax = limits.YRange.Max;
                    double yMin = limits.YRange.Min;
                    double startY = yMax - (yMax - yMin) * 0.02;

                    var startTxt = KlinePlot!.Plot.Add.Text("开始", new Coordinates(_startMarkerIndex, startY));
                    startTxt.LabelStyle.FontName = "微软雅黑";
                    startTxt.LabelStyle.FontSize = 10;
                    startTxt.LabelStyle.Bold = true;
                    startTxt.LabelStyle.ForeColor = SPColor.FromHex("#FFFFFF");
                    startTxt.LabelStyle.BackgroundColor = SPColor.FromHex("#2196F3");
                    startTxt.LabelStyle.BorderColor = SPColor.FromHex("#2196F3");
                    startTxt.LabelStyle.BorderWidth = 1;
                    startTxt.LabelStyle.Alignment = Alignment.UpperCenter; // ← 向下展开

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

            int windowEnd = !_isAnswerRevealed ? Math.Min(_currentVisibleBars, dataList.Count) : dataList.Count;
            // ★ 保险：windowStart 不得超过 windowEnd（状态错位时 visibleCount 为负会导致 Enumerable.Range 抛异常）
            int windowStart = !_isAnswerRevealed ? Math.Min(Math.Max(0, _currentVisibleBars - TrainingBarsForPeriod), windowEnd) : 0;
            int visibleCount = windowEnd - windowStart;

            VolPlot.Plot.Clear();
            ApplyPlotTheme(VolPlot.Plot);   // ★ 主题：背景/坐标轴色随当前主题

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
            VolPlot.Plot.Axes.Right.IsVisible = true;
            VolPlot.Plot.Axes.Right.Label.Text = "";
            VolPlot.Plot.Axes.Bottom.IsVisible = false;

            // ===== 竖直虚线光标 =====
            _volVLine = VolPlot.Plot.Add.VerticalLine(0);
            _volVLine.Color = ChartTheme.Crosshair(ThemeService.Current);
            _volVLine.LineWidth = 0.5f;
            _volVLine.LinePattern = new LinePattern(new float[] { 2, 1 }, 0, "Custom");
            _volVLine.IsVisible = false;

            VolPlot.Plot.Axes.SetLimitsX(windowStart - 0.5, windowEnd - 0.5);
            VolPlot.Plot.Axes.AutoScaleY();

            VolPlot.Plot.Grid.MajorLineColor = ChartTheme.Grid(ThemeService.Current);
            VolPlot.Plot.Grid.MajorLineWidth = 0.5f;

            VolPlot.Plot.Axes.Left.MinimumSize = 0;
            VolPlot.Plot.Axes.Right.MinimumSize = 50;

            if (!_refreshSuspended) VolPlot.Refresh();
        }

        private void UpdateMacdChart()
        {
            if (MacdPlot == null) return;

            var dataList = _currentDataList;
            if (dataList == null || dataList.Count == 0) return;

            int windowEnd = !_isAnswerRevealed ? Math.Min(_currentVisibleBars, dataList.Count) : dataList.Count;
            // ★ 保险：windowStart 不得超过 windowEnd（状态错位时 visibleCount 为负会导致 Enumerable.Range 抛异常）
            int windowStart = !_isAnswerRevealed ? Math.Min(Math.Max(0, _currentVisibleBars - TrainingBarsForPeriod), windowEnd) : 0;
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
            ApplyPlotTheme(MacdPlot.Plot);   // ★ 主题：背景/坐标轴色随当前主题

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
            MacdPlot.Plot.Axes.Right.IsVisible = true;
            MacdPlot.Plot.Axes.Right.Label.Text = "";
            MacdPlot.Plot.Axes.Bottom.IsVisible = false;

            SetupDateAxis(MacdPlot.Plot, dataList, windowStart, visibleCount);

            // ===== 竖直虚线光标 =====
            _macdVLine = MacdPlot.Plot.Add.VerticalLine(0);
            _macdVLine.Color = ChartTheme.Crosshair(ThemeService.Current);
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

            MacdPlot.Plot.Grid.MajorLineColor = ChartTheme.Grid(ThemeService.Current);
            MacdPlot.Plot.Grid.MajorLineWidth = 0.5f;

            MacdPlot.Plot.Axes.Left.MinimumSize = 0;
            MacdPlot.Plot.Axes.Right.MinimumSize = 50;

            if (!_refreshSuspended) MacdPlot.Refresh();
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

            // 月线跨度大，日期轴显示到月份即可
            string dateFmt = SelectedPeriod == KLinePeriod.Month ? "yyyy-MM" : "MM-dd";

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
                tickLabels.Add(dataList[dataIndex].Date.ToString(dateFmt));
            }

            if (tickPositions.Count == 0 || tickPositions.Last() != windowEnd - 1)
            {
                tickPositions.Add(windowEnd - 1);
                tickLabels.Add(dataList[windowEnd - 1].Date.ToString(dateFmt));
            }

            var tickGen = new ScottPlot.TickGenerators.NumericManual(
                tickPositions.ToArray(),
                tickLabels.ToArray()
            );
            bottomAxis.TickGenerator = tickGen;

            bottomAxis.TickLabelStyle.Rotation = -30;
            bottomAxis.TickLabelStyle.Alignment = Alignment.MiddleRight;
        }

        /// <summary>识别当前可见训练窗口的K线形态（只取玩家可见段，不含未来），用于开局/切周期提示</summary>
        private void UpdatePattern()
        {
            try
            {
                int end = Math.Min(_currentVisibleBars, _currentDataList.Count);
                if (end < 30) { PatternText = "--"; return; }

                var visible = _currentDataList.Take(end).ToList();
                var pattern = new PatternDetector().DetectPattern(visible);
                PatternText = PatternName(pattern);
                Debug.WriteLine($"[DIAG] 形态识别: {SelectedStock} -> {PatternText}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DIAG] 形态识别失败: {ex.Message}");
                PatternText = "--";
            }
        }

        /// <summary>PatternType → 中文名</summary>
        private static string PatternName(PatternType p) => p switch
        {
            PatternType.HeadAndShoulders => "头肩顶",
            PatternType.InverseHeadAndShoulders => "头肩底",
            PatternType.DoubleTop => "双顶",
            PatternType.DoubleBottom => "双底",
            PatternType.TriangleAscending => "上升三角",
            PatternType.TriangleDescending => "下降三角",
            PatternType.TriangleSymmetrical => "对称三角",
            PatternType.CupAndHandle => "杯柄形态",
            _ => "无明显形态",
        };

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
            // 月线显示到月份
            CurrentDate = SelectedPeriod == KLinePeriod.Month
                ? last.Date.ToString("yyyy-MM")
                : last.Date.ToString("yyyy-MM-dd");

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
            StopPlay();

            _isTrainingMode = !_isTrainingMode;
            _isAnswerRevealed = false;
            _currentVisibleBars = TrainingBarsForPeriod;

            if (_isTrainingMode)
            {
                TrainingStatus = $"训练模式：预测后{TotalBarsForPeriod - TrainingBarsForPeriod}根K线走势";

                if (!string.IsNullOrEmpty(SelectedStock))
                {
                    if (_config.IsLimitUpMode)
                    {
                        // ★ 涨停训练：抽局时窗口已锚定（末根可见K线=涨停日，其后 150 根为未来），沿用现成窗口，不再重开
                        if (_currentDataList != null && _currentDataList.Count > TrainingBarsForPeriod)
                        {
                            _trainStartBarIdx = TrainingBarsForPeriod - 1;   // 训练起点 = 本周期训练窗口末根
                            _currentTotalBars = _currentDataList.Count;
                            UpdateAllCharts();      // ★ 批量刷新（原三连 UpdateXxx）
                            UpdateInfoBar();
                            NotifyAllStats();
                        }
                        else
                        {
                            MessageBox.Show("当前股票历史数据不足，无法训练");
                            _isTrainingMode = false;
                            TrainingStatus = "浏览模式";
                        }
                    }
                    else
                    {
                        var allData = LoadPeriodSeries(SelectedStock);

                        if (allData.Count >= TotalBarsForPeriod)
                        {
                            _currentDataList = allData.Skip(allData.Count - TotalBarsForPeriod).Take(TotalBarsForPeriod).ToList();
                            _startDate = _currentDataList[0].Date;
                            OnPropertyChanged(nameof(StartDate));
                            _trainStartBarIdx = TrainingBarsForPeriod - 1;   // 训练起点 = 本周期训练窗口末根
                            _currentTotalBars = TotalBarsForPeriodFor(SelectedPeriod);
                            UpdateAllCharts();      // ★ 批量刷新（原三连 UpdateXxx）
                            UpdateInfoBar();
                            NotifyAllStats();
                        }
                        else
                        {
                            MessageBox.Show($"当前股票历史数据不足 {TotalBarsForPeriodFor(SelectedPeriod)} 根，无法训练");
                            _isTrainingMode = false;
                            TrainingStatus = "浏览模式";
                        }
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
            StopPlay();
            UpdateAllCharts();              // ★ 批量刷新（原三连 UpdateXxx）
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
                _currentVisibleBars = TrainingBarsForPeriod;

                if (_currentDataList == null || _currentDataList.Count == 0)
                {
                    MessageBox.Show("当前没有数据，无法进入训练模式");
                    _isTrainingMode = false;
                    return;
                }

                _startMarkerIndex = _currentVisibleBars - 0.5;
                _trainStartBarIdx = TrainingBarsForPeriod - 1;   // 训练起点 = 本周期训练窗口末根
                _currentTotalBars = _currentDataList.Count;      // 浏览窗口即本局窗口
                StartTimer();
            }
            else
            {
                if (_currentVisibleBars >= TotalBarsForPeriod) return;

                // ★ 周线模式：点满一个交易周（5次）才推进一根周K
                _holdWatchClicks++;
                if (_holdWatchClicks < ClicksPerStep)
                {
                    TrainingStatus = $"训练模式：本周第 {_holdWatchClicks}/{ClicksPerStep} 天，K线未推进";
                    return;   // 本周还没走完，不推进、不计统计
                }
                _holdWatchClicks = 0;
                _currentVisibleBars++;

                if (_hasPosition)
                {
                    _holdDays++;
                    // ★ 重仓 = 当前持仓 ≥ 最大允许持仓（本金×比例×杠杆）的 7 成
                    if (_holdBuyAmount >= MaxPositionAmount * 0.7)
                        _heavyHoldDays++;
                }
                else
                {
                    _watchDays++;
                }
            }
            if (CheckLiquidation()) return;
            BeginBatchUpdate();              // ★ 批量刷新：触发链+三图一次画完再一次刷
            try
            {
                bool sold = CheckStopTriggers();
                if (!sold) sold = CheckAutoSellDays();
                if (!sold) UpdatePriceChart();
                UpdateVolChart();
                UpdateMacdChart();
            }
            finally { EndBatchUpdate(); }
            UpdateInfoBar();
            NotifyAllStats();

            int remaining = TotalBarsForPeriod - _currentVisibleBars;
            if (remaining > 0)
            {
                TrainingStatus = $"训练模式：已推进 {_currentVisibleBars}/{TotalBarsForPeriod}，剩余 {remaining} 根";
                CommandManager.InvalidateRequerySuggested();
            }
            else
            {
                ExecuteSettle();
            }
        }

        private bool CanBuy()
        {
            if (!_isTrainingMode || _isAnswerRevealed || _currentVisibleBars >= TotalBarsForPeriod)
                return false;

            // 非分仓模式：无持仓才能买（买入金额 = 满仓上限，含杠杆）
            if (!_config.IsSplitPosition)
                return !_hasPosition;

            // 分仓模式：只要没买到上限（本金×比例×杠杆），就可以继续买
            return _holdBuyAmount < MaxPositionAmount - 0.01;
        }

        private void ExecuteBuy()
        {
            IsSellOptionsVisible = false;

            if (_config.IsSplitPosition)
            {
                IsBuyOptionsVisible = !IsBuyOptionsVisible;
                return;
            }

            // 非分仓模式：B = 一次性满仓买入（上限 = 本金 × 杠杆）
            ExecuteSplitBuy(100);
        }

        private void ExecuteSplitBuy(int percent)
        {
            if (!CanBuy()) return;

            double price = GetTradePrice();
            if (price <= 0) return;

            // percent = 目标仓位占"最大允许持仓"的百分比（100 = 满仓）
            double wantAmount = MaxPositionAmount * percent / 100.0;
            double allocatedCash = wantAmount - _holdBuyAmount;   // 本次投入的现金（含手续费）
            allocatedCash = Math.Min(allocatedCash, MaxPositionAmount - _holdBuyAmount);
            if (allocatedCash <= 0.01) return;

            // ★ 杠杆下允许现金为负（负数 = 融资借款），不再用 _cash 截断
            double buyAmount = allocatedCash / (1 + FeeRate);
            double fee = buyAmount * FeeRate;

            bool wasEmpty = !_hasPosition;

            if (_hasPosition && _avgCostPrice.HasValue)
            {
                double totalAmount = _holdBuyAmount + buyAmount;
                _avgCostPrice = (_holdBuyAmount * _avgCostPrice.Value + buyAmount * price) / totalAmount;
                _holdBuyAmount = totalAmount;
            }
            else
            {
                _holdBuyAmount = buyAmount;
                _avgCostPrice = price;
            }

            _buyBarIndex = _currentVisibleBars;
            _cash -= allocatedCash;

            if (wasEmpty) _openCount++;

            _tradeMarkers.Add(new TradeMarker { BarIndex = _currentVisibleBars - 1, Type = "B" });

            TradeRecords.Insert(0, new TradeRecord
            {
                Type = "买入",
                Date = GetOpTime(),
                Price = price,
                Profit = "--"   // ★ Bug9 修复：买入时无盈亏，"--" 避免 "100.00%" 误导
            });
            UpdatePriceChart();
            RebuildBuyItems();
            RebuildSellItems();
            NotifyAllStats();
            CommandManager.InvalidateRequerySuggested();
            IsBuyOptionsVisible = false;
            AutoSkipAfterTradeIfNeeded();   // 买卖自动跳
        }

        // ===== 根据当前持仓动态生成买入档位（只显示还没买到的档）=====
        private void RebuildBuyItems()
        {
            SplitBuyItems.Clear();
            if (!_config.IsSplitPosition) return;

            // 固定 10 档，每档 = 最大持仓的 1/10（与杠杆、分仓比例无关）
            int currentTenths = (int)Math.Round(_holdBuyAmount / (MaxPositionAmount / 10));
            currentTenths = Math.Max(0, Math.Min(currentTenths, 10));

            for (int i = currentTenths + 1; i <= 10; i++)
            {
                SplitBuyItems.Add(new SplitBuyItem
                {
                    Index = i - currentTenths,
                    Text = i == 10 ? "加至满仓" : $"加至{i}/10仓",
                    Percent = i * 10
                });
            }
        }

        // ===== 根据当前持仓动态生成卖出档位 =====
        private void RebuildSellItems()
        {
            SplitSellItems.Clear();
            if (!_config.IsSplitPosition) return;

            int currentTenths = (int)Math.Round(_holdBuyAmount / (MaxPositionAmount / 10));
            currentTenths = Math.Max(0, Math.Min(currentTenths, 10));

            for (int i = currentTenths - 1; i >= 1; i--)
            {
                SplitSellItems.Add(new SplitBuyItem
                {
                    Index = currentTenths - i,
                    Text = $"减至{i}/10仓",
                    Percent = i * 10
                });
            }

            if (currentTenths > 0)
            {
                SplitSellItems.Add(new SplitBuyItem
                {
                    Index = currentTenths,
                    Text = "清仓",
                    Percent = 0
                });
            }
        }

        // ===== 分档卖出：percent = 目标持仓档位（0 = 清仓）=====
        private void ExecuteSplitSell(int percent)
        {
            if (!CanSell()) return;

            double price = GetTradePrice();
            if (price <= 0 || !_avgCostPrice.HasValue || !_hasPosition) return;

            double profitPct = (price - _avgCostPrice.Value) / _avgCostPrice.Value * 100;

            double targetAmount = Math.Max(0, Math.Min(
    MaxPositionAmount * (percent / 100.0), _holdBuyAmount));
            double sellAmount = _holdBuyAmount - targetAmount;
            if (sellAmount <= 0.01) { IsSellOptionsVisible = false; return; }

            double totalMarketValue = _holdBuyAmount * (price / _avgCostPrice.Value);
            double sellMarketValue = totalMarketValue * (sellAmount / _holdBuyAmount);
            double fee = sellMarketValue * FeeRate;
            double profit = sellMarketValue - sellAmount - fee - (sellAmount * FeeRate);

            _cash += sellMarketValue - fee;
            _closedProfitAmount += profit;
            _closedCostAmount += sellAmount;
            if (profit > 0) _profitCount++;

            if (targetAmount <= 0.01)
            {
                _holdBuyAmount = 0;
                _avgCostPrice = null;
                _buyBarIndex = -1;
                IsSellOptionsVisible = false;
            }
            else
            {
                _holdBuyAmount = targetAmount;
            }

            _tradeMarkers.Add(new TradeMarker { BarIndex = _currentVisibleBars - 1, Type = "S" });

            TradeRecords.Insert(0, new TradeRecord
            {
                Type = targetAmount <= 0.01 ? "卖出(清仓)" : "卖出",
                Date = GetOpTime(),
                Price = price,
                Profit = profitPct.ToString("F2") + "%"
            });

            RebuildBuyItems();
            RebuildSellItems();
            NotifyAllStats();
            UpdatePriceChart();
            CommandManager.InvalidateRequerySuggested();
            IsSellOptionsVisible = false;
            AutoSkipAfterTradeIfNeeded();   // 买卖自动跳
        }

        // ===== 爆仓：总火星币跌破初始本金的 10% 时强制清仓并结算 =====
        private const double LiquidationRatio = 0.10;

        private bool CheckLiquidation()
        {
            if (!_isTrainingMode || _isAnswerRevealed || !_hasPosition) return false;
            if (TotalFirecrackers > _initialFirecrackers * LiquidationRatio) return false;

            double price = GetCurrentPrice();
            if (price > 0 && _avgCostPrice.HasValue)
            {
                double marketValue = _holdBuyAmount * (price / _avgCostPrice.Value);
                double fee = marketValue * FeeRate;
                double profit = marketValue - _holdBuyAmount - fee - (_holdBuyAmount * FeeRate);

                _cash += marketValue - fee;
                _closedProfitAmount += profit;
                _closedCostAmount += _holdBuyAmount;
                if (profit > 0) _profitCount++;

                _tradeMarkers.Add(new TradeMarker { BarIndex = _currentVisibleBars - 1, Type = "S" });

                TradeRecords.Insert(0, new TradeRecord
                {
                    Type = "爆仓强平",
                    Date = GetOpTime(),
                    Price = price,
                    Profit = ((price - _avgCostPrice.Value) / _avgCostPrice.Value * 100).ToString("F2") + "%"
                });
            }

            _holdBuyAmount = 0;
            _avgCostPrice = null;
            _buyBarIndex = -1;
            RebuildBuyItems();
            RebuildSellItems();
            NotifyAllStats();

            MessageBox.Show(
                $"💥 爆仓！\n\n总火星币已跌破初始本金 {_initialFirecrackers:F2} 的 10%，持仓被强制平仓。",
                "爆仓", MessageBoxButton.OK, MessageBoxImage.Warning);

            ExecuteSettle();   // 走正常结算流程（出结算弹窗）
            return true;
        }

        /// <summary>买卖自动跳：手动成交后自动推进1根（等价多点一次"持有/观望"；自动触发的卖出不调它，避免连锁）</summary>
        private void AutoSkipAfterTradeIfNeeded()
        {
            if (!_config.AutoSkipAfterTrade || _isAnswerRevealed) return;
            if (!_isTrainingMode || _currentVisibleBars >= TotalBarsForPeriod) return;
            OnHoldOrWatch();
        }

        /// <summary>全仓卖出共用：费用/统计/流水/S标记 一次做完。recordType = 流水类型（"卖出"/"止盈卖出"/"自动卖出(10天)"…）</summary>
        private bool SellAll(string recordType)
        {
            if (!_hasPosition) return false;
            double price = GetTradePrice();
            if (price <= 0 || !_avgCostPrice.HasValue) return false;

            double profitPct = (price - _avgCostPrice.Value) / _avgCostPrice.Value * 100;
            double marketValue = _holdBuyAmount * (price / _avgCostPrice.Value);
            double fee = marketValue * FeeRate;
            double profit = marketValue - _holdBuyAmount - fee - (_holdBuyAmount * FeeRate);

            _cash += marketValue - fee;
            _closedProfitAmount += profit;
            _closedCostAmount += _holdBuyAmount;
            if (profit > 0) _profitCount++;

            _tradeMarkers.Add(new TradeMarker { BarIndex = _currentVisibleBars - 1, Type = "S" });
            TradeRecords.Insert(0, new TradeRecord
            {
                Type = recordType,
                Date = GetOpTime(),
                Price = price,
                Profit = profitPct.ToString("F2") + "%"
            });

            _holdBuyAmount = 0;
            _avgCostPrice = null;
            _buyBarIndex = -1;
            IsSellOptionsVisible = false;
            RebuildBuyItems();
            RebuildSellItems();
            NotifyAllStats();
            UpdatePriceChart();
            return true;
        }

        /// <summary>自动卖出天数：持仓满 N 根K线自动清仓（买入后每推进一根计一天，加仓重置计数）</summary>
        private bool CheckAutoSellDays()
        {
            if (!_config.AutoSellEnabled || _config.AutoSellDays <= 0) return false;
            if (!_isTrainingMode || _isAnswerRevealed || !_hasPosition || _buyBarIndex < 0) return false;
            if (_currentVisibleBars - _buyBarIndex < _config.AutoSellDays) return false;
            return SellAll($"自动卖出({_config.AutoSellDays}天)");
        }

        // ===== 止盈/止损：持仓盈亏达到设定阈值时自动全仓卖出 =====
        private bool CheckStopTriggers()
        {
            if (!_isTrainingMode || _isAnswerRevealed || !_hasPosition) return false;
            if (!_avgCostPrice.HasValue || _avgCostPrice.Value <= 0) return false;

            double price = GetCurrentPrice();
            if (price <= 0) return false;

            double profitPct = (price - _avgCostPrice.Value) / _avgCostPrice.Value * 100;

            string? trigger = null;
            if (_config.IsStopProfit && _config.StopProfitPercent > 0
                && profitPct >= _config.StopProfitPercent)
                trigger = "止盈卖出";
            else if (_config.IsStopLoss && _config.StopLossPercent > 0
                && profitPct <= -_config.StopLossPercent)
                trigger = "止损卖出";

            if (trigger == null) return false;

            return SellAll(trigger);
        }

        private void ExecuteSettle()
        {
            // ★ Bug8 修复：浏览模式不可结算（命令 CanExecute 之外再兜一层，防快捷键/代码路径直调）
            if (!_isTrainingMode) return;

            if (_hasPosition)
            {
                double price = GetCurrentPrice();
                if (price > 0 && _avgCostPrice.HasValue)
                {
                    double marketValue = _holdBuyAmount * (price / _avgCostPrice.Value);
                    double fee = marketValue * FeeRate;
                    double profit = marketValue - _holdBuyAmount - fee - (_holdBuyAmount * FeeRate);

                    _cash += marketValue - fee;
                    _closedProfitAmount += profit;
                    _closedCostAmount += _holdBuyAmount;
                    if (profit > 0) _profitCount++;

                    TradeRecords.Insert(0, new TradeRecord
                    {
                        Type = "卖出(结算)",
                        Date = GetOpTime(),
                        Price = price,
                        Profit = ((price - _avgCostPrice.Value) / _avgCostPrice.Value * 100).ToString("F2") + "%"
                    });
                }
                _holdBuyAmount = 0;
                _avgCostPrice = null;
            }

            // ★ 单局保护：杠杆穿仓时结算最低为 0（亏损不带成负数，否则永远翻不了身）
            if (TotalFirecrackers < 0)
                _cash = 0;

            StopTimer();
            StopPlay();
            _isAnswerRevealed = true;
            // ★ 审查修复②：结算后退出训练模式——"结束训练并结算"按钮 CanExecute 立即失效，
            //   防止结算弹窗关闭后误点再次执行，往 cy_training_record 重复插一条记录
            _isTrainingMode = false;
            TrainingStatus = "训练已结算";
            UpdateAllCharts();              // ★ 批量刷新（原三连 UpdateXxx）
            NotifyAllStats();
            CommandManager.InvalidateRequerySuggested();

            // ===== 训练记录落库（首页统计/曲线的数据源）=====
            bool isEmptyGame = _openCount == 0 && _currentVisibleBars <= TrainingBarsForPeriod;
            if (!isEmptyGame)
            {
                try
                {
                    int sIdx = Math.Max(0, _trainStartBarIdx);
                    int eIdx = Math.Min(_currentVisibleBars, _currentDataList.Count) - 1;
                    double startPrice = _currentDataList[sIdx].Close;
                    double endPrice = _currentDataList[eIdx].Close;

                    _dbService.SaveTrainingRecord(new TrainingRecordEntry
                    {
                        TrainTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        StockCode = SelectedStock ?? "",
                        StockName = IsIndexMode
               ? (IndexNames.TryGetValue(SelectedStock ?? "", out var idxName) ? idxName : SelectedStock ?? "")
               : (SelectedStock != null ? _dbService.GetStockName(SelectedStock) : ""),
                        Period = SelectedPeriod.ToString(),
                        StartDate = _currentDataList[sIdx].Date.ToString("yyyy-MM-dd"),
                        EndDate = _currentDataList[eIdx].Date.ToString("yyyy-MM-dd"),
                        InitialFirecrackers = _initialFirecrackers,
                        FinalFirecrackers = TotalFirecrackers,
                        ProfitAmount = TotalFirecrackers - _initialFirecrackers,
                        ProfitPct = _initialFirecrackers > 0 ? (TotalFirecrackers - _initialFirecrackers) / _initialFirecrackers * 100 : 0,
                        IntervalPct = startPrice > 0 ? (endPrice - startPrice) / startPrice * 100 : 0,
                        OpenCount = _openCount,
                        WinRate = _openCount > 0 ? (double)_profitCount / _openCount * 100 : 0,
                        HoldDays = _holdDays,
                        HeavyHoldDays = _heavyHoldDays,
                        WatchDays = _watchDays,
                        ElapsedSec = (int)_elapsed.TotalSeconds,
                        Leverage = Math.Max(1, _config.Leverage),
                        IsFullGame = _currentVisibleBars >= TotalBarsForPeriod ? 1 : 0,   // 打满整局=1，中途手动结算=0
                        // ★ 训练记录标签：把本局训练类型存进 config_json（首页"训练记录"列表读取此值显示对应标签）
                        //   注意必须先判断 IsLimitUpMode（涨停模式 TrainTarget 被强制写成 "Stock"）
                        ConfigJson = _config.IsLimitUpMode ? "LimitUp" : (_config.TrainTarget ?? "Stock")
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DIAG] 保存训练记录失败: {ex.Message}");
                }
            }

            ShowTrainingResult();
            _initialFirecrackers = TotalFirecrackers;

        }


        /// <summary>成交价：开盘买入开关开 → 当前可见K线开盘价；关 → 收盘价（持仓市值仍按收盘价浮动）</summary>
        private double GetTradePrice()
        {
            if (_currentDataList == null || _currentDataList.Count == 0) return 0;
            int idx = Math.Min(_currentVisibleBars, _currentDataList.Count) - 1;
            if (idx < 0) idx = 0;
            return _config.OpenPriceTrading ? _currentDataList[idx].Open : _currentDataList[idx].Close;
        }

        private double GetCurrentPrice()
        {
            if (_currentDataList == null || _currentDataList.Count == 0) return 0;
            int idx = Math.Min(_currentVisibleBars, _currentDataList.Count) - 1;
            if (idx < 0) idx = 0;
            return _currentDataList[idx].Close;
        }

        /// <summary>流水用的实际操作时刻（双盲：绝不显示历史K线日期）</summary>
        private static string GetOpTime() => DateTime.Now.ToString("HH:mm:ss");

        private string GetCurrentDate()
        {
            if (_currentDataList == null || _currentDataList.Count == 0) return "";
            int idx = Math.Min(_currentVisibleBars, _currentDataList.Count) - 1;
            if (idx < 0) idx = 0;
            return _currentDataList[idx].Date.ToString("yyyy-MM-dd");
        }

        private bool CanTogglePlay()
        {
            if (_isPlaying) return true;   // 播放中随时可以暂停
            return _isTrainingMode && !_isAnswerRevealed && _currentVisibleBars < TotalBarsForPeriod;
        }

        private void TogglePlay()
        {
            if (_isPlaying) { StopPlay(); return; }
            StartPlay();
        }

        private void StartPlay()
        {
            if (!_isTrainingMode || _isAnswerRevealed || _currentVisibleBars >= TotalBarsForPeriod) return;

            _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / PlaySpeed / ClicksPerStep) };
            _playTimer.Tick += PlayTimer_Tick;
            _playTimer.Start();
            IsPlaying = true;
            CommandManager.InvalidateRequerySuggested();
        }

        private void StopPlay()
        {
            _playTimer?.Stop();
            _playTimer = null;
            IsPlaying = false;
            CommandManager.InvalidateRequerySuggested();
        }

        // 每次 Tick = 自动点一次"持有/观望"，交易仍可手动操作
        private void PlayTimer_Tick(object? sender, EventArgs e)
        {
            if (_isAnswerRevealed || _currentVisibleBars >= TotalBarsForPeriod)
            {
                StopPlay();
                return;
            }
            OnHoldOrWatch();
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
            StopPlay();
            _holdWatchClicks = 0;
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
            IsBuyOptionsVisible = false;
            IsSellOptionsVisible = false;
            RebuildBuyItems();
            RebuildSellItems();
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
            OnPropertyChanged(nameof(LeverageText));
            OnPropertyChanged(nameof(BorrowText));
            OnPropertyChanged(nameof(BorrowBrush));
        }

        private DateTime _lastSyncTime = DateTime.MinValue;
        private DateTime _lastToggleTime = DateTime.MinValue;

        // ===== 主题切换：图表配色跟随 =====
        private void OnThemeChanged(AppTheme theme)
        {
            ApplyChartTheme();
            UpdateAllCharts();   // 三个 UpdateXxx 里网格/十字线色已改读 ChartTheme，重绘即生效
        }

        /// <summary>只设图表外框/绘图区背景/坐标轴色；网格与十字线色在各自 UpdateXxx 里读 ChartTheme</summary>
        private void ApplyChartTheme()
        {
            foreach (var plot in new[] { KlinePlot?.Plot, VolPlot?.Plot, MacdPlot?.Plot })
            {
                if (plot == null) continue;
                var t = ThemeService.Current;
                plot.FigureBackground.Color = ChartTheme.FigureBg(t);
                plot.DataBackground.Color = ChartTheme.DataBg(t);
                plot.Axes.Color(ChartTheme.Axis(t));   // 若 5.1.59 无此 API，删除本行即可
            }
        }

        /// <summary>单图版：首次画图和每次重绘都调用，保证背景/轴色始终跟随当前主题</summary>
        private void ApplyPlotTheme(Plot plot)
        {
            var t = ThemeService.Current;
            plot.FigureBackground.Color = ChartTheme.FigureBg(t);
            plot.DataBackground.Color = ChartTheme.DataBg(t);
            plot.Axes.Color(ChartTheme.Axis(t));   // 若 5.1.59 无此 API，删除本行即可
        }

        /// <summary>训练窗关闭时退订，避免旧 VM 被通知后对已销毁控件 Refresh</summary>
        public void DetachTheme() => ThemeService.ThemeChanged -= OnThemeChanged;

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

            int windowEnd = !_isAnswerRevealed ? Math.Min(_currentVisibleBars, dataList.Count) : dataList.Count;
            int windowStart = !_isAnswerRevealed ? Math.Min(Math.Max(0, _currentVisibleBars - TrainingBarsForPeriod), windowEnd) : 0;

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

                // 价格标签：定位到绘图区右侧外（价格刻度区），不遮挡K线
                if (CursorPriceLabel != null && CursorPriceText != null && KlinePlot != null)
                {
                    double pxY = KlinePlot.Plot.GetPixel(new Coordinates(0, mouseCoords.Y)).Y;
                    CursorPriceText.Text = mouseCoords.Y.ToString("F2");
                    CursorPriceLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double w = CursorPriceLabel.DesiredSize.Width;
                    double h = CursorPriceLabel.DesiredSize.Height;
                    // 贴到控件右边缘（盖住刻度数字区域），垂直方向跟随鼠标
                    CursorPriceLabel.RenderTransform = new TranslateTransform(KlinePlot.ActualWidth - w - 2, pxY - h / 2);
                    CursorPriceLabel.Visibility = Visibility.Visible;
                }
            }
            else if (_klineHLine != null)
            {
                _klineHLine.IsVisible = false;
                if (CursorPriceLabel != null) CursorPriceLabel.Visibility = Visibility.Hidden;
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
            if (CursorPriceLabel != null) CursorPriceLabel.Visibility = Visibility.Hidden;
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
            if (_currentDataList == null || _currentDataList.Count == 0) return;

            int startIdx = _trainStartBarIdx;
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

            // ★ 2026-09-30 破产即重置（结算点完成，不等下次进训练窗）：
            //   上面 result 已捕获真实本局盈亏（含破产亏损，结算弹窗显示 -100% 等正确数字）；
            //   累计火星币 ≤ 0 → 当场写 RESET 记录并回本 10000，
            //   之后无论"结束"回首页还是"下一局"，读到的都是 10000。
            //   注意必须在弹窗弹出前完成：弹窗之后"结束"分支会立即刷新首页（读数据库）。
            if (TotalFirecrackers <= BankruptcyFirecrackers)
            {
                try
                {
                    _dbService.SaveTrainingRecord(new TrainingRecordEntry
                    {
                        TrainTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        StockCode = "RESET",
                        StockName = "破产重置",
                        Period = "Day",
                        StartDate = "",
                        EndDate = "",
                        InitialFirecrackers = TotalFirecrackers,
                        FinalFirecrackers = DefaultInitialFirecrackers,
                        ProfitAmount = DefaultInitialFirecrackers - TotalFirecrackers,
                        ConfigJson = _config.IsLimitUpMode ? "LimitUp" : (_config.TrainTarget ?? "Stock"),
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DIAG] 破产重置记录写入失败: {ex.Message}");
                }

                MessageBox.Show(
                    $"💥 破产！\n\n火星币已归零或透支（当前 {TotalFirecrackers:F2}），本金已重置为 {DefaultInitialFirecrackers:F0}，重新开始！",
                    "破产重置", MessageBoxButton.OK, MessageBoxImage.Warning);

                _cash = DefaultInitialFirecrackers;   // VM 资金同步回本："下一局"分支立即以 10000 开局
            }

            // ★ 段位升级判定（2026-09-30）：结算前后本金跨档（如 1万→10万 跨黄金账户）
            //   注意必须在 ShowDialog 之前取新旧值——弹窗关闭后才会执行 ResultAction（下一局会重置本金）
            int promoRankIdx = RankService.GetPromotionRankIndex(_initialFirecrackers, TotalFirecrackers);

            var dialog = new TrainingResultWindow(result)
            {
                Owner = Application.Current.MainWindow
            };

            dialog.ShowDialog();

            // ★ 升级弹窗：结算弹窗关闭后弹出，避免挡住本局盈亏信息；只报最高到达档（连跳多级不连弹）
            if (promoRankIdx > 0)
            {
                var rankUp = new RankUpWindow(promoRankIdx, TotalFirecrackers)
                {
                    Owner = Application.Current.MainWindow
                };
                rankUp.ShowDialog();
            }

            switch (dialog.ResultAction)
            {
                case ResultAction.NextGame:
                    _ = NextGame();
                    break;
                case ResultAction.End:
                    ExitTraining();
                    break;
                case ResultAction.Review:
                    break;
            }
        }

        private async Task NextGame()
        {
            StopTimer();
            _initialFirecrackers = TotalFirecrackers;
            ResetTrainingStats();
            _isTrainingMode = false;
            _isAnswerRevealed = false;
            _startMarkerIndex = -1;
            _currentVisibleBars = TrainingBarsForPeriod;

            await LoadRandomStock();   // ★ 等首抽完成再续训练状态，不能用 _ =（后面立即读 _currentDataList）

            if (_currentDataList != null && _currentDataList.Count >= TotalBarsForPeriod)
            {
                _isTrainingMode = true;
                _isAnswerRevealed = false;
                _currentVisibleBars = TrainingBarsForPeriod;
                _startMarkerIndex = _currentVisibleBars - 0.5;
                StartTimer();

                UpdateAllCharts();          // ★ 批量刷新（原三连 UpdateXxx）
                UpdateInfoBar();
                TrainingStatus = $"训练模式：预测后{TotalBarsForPeriod - TrainingBarsForPeriod}根K线走势";
                NotifyAllStats();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>不结算直接返回首页（训练窗左上角返回按钮用，丢弃本局）</summary>
        public void ExitToHome()
        {
            ExitTraining();
        }


        private void ExitTraining()
        {
            StopTimer();
            StopPlay();

            // ★ 顺序很关键：必须先把首页显示出来，再关训练窗。
            //   否则训练窗关闭瞬间"最后一个窗口"消失，WPF 立即启动退出流程，
            //   之后再 new MainWindow 也拦不住（程序照样退出）。
            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (mainWindow == null)
            {
                mainWindow = new MainWindow();   // 首页已不在 → 重建（Loaded 里会自动 RefreshHomeData）
                mainWindow.Show();
            }
            else
            {
                mainWindow.RefreshHomeData();    // 首页一直在 → 手动刷新刚结算的这局
            }
            mainWindow.Activate();

            // 最后关闭训练主窗口（KLineTrainWindow）
            var trainWindow = Application.Current.Windows.OfType<KLineTrainWindow>().FirstOrDefault();
            trainWindow?.Close();

            Debug.WriteLine($"[DIAG] ExitTraining done, open windows: {string.Join(",", Application.Current.Windows.OfType<Window>().Select(w => w.GetType().Name))}");
        }
    }
}