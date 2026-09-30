本目录存放训练数据库 cy_stock.db。

【快速体验】
仓库自带 cy_stock.db 为"演示示例库"（tools/make_sample_db.py 合成的随机数据，
含 12 只演示股票 + 6 个指数 + 少量美股/港股/期货/转债，仅供跑通程序，
不构成真实行情，训练结果无参考意义）。

【正式使用】
用真实行情重建数据库（沪深 A 股 + 主要指数，约 10~30 分钟，需联网）：
    pip install -r tools/requirements.txt
    python tools/build_stock_db.py
生成后把 cy_stock.db 放到本目录（与 A.exe 同级的 stockdata\）即可。

【数据格式约定】（自建库务必遵守）
- trade_date 必须是 yyyyMMdd 字符串（如 20251231），程序全部按字符串比较；
- 日线表最少字段：code, trade_date, open, high, low, close, volume；
- cy_daily 的指标列（ma5/ema20/boll/macd_hist 等）可留空，程序会自动现算；
- 程序查询截止 20251231，拉数据时 end_date 不要超过该日期。

