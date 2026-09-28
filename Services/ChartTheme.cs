using BaozhuKLineTrainer.Services;   // AppTheme 所在命名空间（本文件同命名空间可省略）

namespace BaozhuKLineTrainer.Services
{
    /// <summary>ScottPlot 图表配色按主题取值；K线红涨绿跌等业务色不在这里</summary>
    public static class ChartTheme
    {
        public static ScottPlot.Color FigureBg(AppTheme t) => ScottPlot.Color.FromHex(t == AppTheme.Dark ? "#111318" : "#FFFFFF");
        public static ScottPlot.Color DataBg(AppTheme t) => ScottPlot.Color.FromHex(t == AppTheme.Dark ? "#151922" : "#FAFBFD");
        public static ScottPlot.Color Grid(AppTheme t) => ScottPlot.Color.FromHex(t == AppTheme.Dark ? "#262D3B" : "#E3E6EB");
        public static ScottPlot.Color Axis(AppTheme t) => ScottPlot.Color.FromHex(t == AppTheme.Dark ? "#AAB4C5" : "#555B66");
        public static ScottPlot.Color Crosshair(AppTheme t) => ScottPlot.Color.FromHex(t == AppTheme.Dark ? "#6EA8FF" : "#2196F3");
    }
}