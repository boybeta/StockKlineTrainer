namespace BaozhuKLineTrainer.Models
{
    public enum PatternType
    {
        None,
        HeadAndShoulders,      // 头肩顶
        InverseHeadAndShoulders, // 头肩底
        DoubleTop,            // 双顶
        DoubleBottom,         // 双底
        TriangleAscending,    // 上升三角形
        TriangleDescending,   // 下降三角形
        TriangleSymmetrical,  // 对称三角形
        FlagBull,             // 牛市旗形
        FlagBear,             // 熊市旗形
        WedgeRising,          // 上升楔形
        WedgeFalling,         // 下降楔形
        CupAndHandle,         // 杯柄形态
        Random                // 随机/无特定形态
    }
}