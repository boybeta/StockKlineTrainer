#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
make_sample_db.py — 生成"火星币K线训练助手"演示数据库 cy_stock.db
纯本地合成数据（随机游走），不联网、可复现（固定种子）。
用法：
    python make_sample_db.py                # 生成 ./stockdata/cy_stock.db
    python make_sample_db.py --out D:\\path\\cy_stock.db --seed 42 --days 800
说明：
    - 数据为程序合成，仅用于演示/测试，不构成任何真实行情；
    - 每只 A 股注入了若干 +9.8% 涨停日（2026-09-28 版涨停训练判定阈值 1.095 留容差），涨停训练模式可玩；
    - 指标列（MA/EMA/BOLL/VOLMA/MACD）按 IndicatorCalculator.cs 同款公式预算，与程序内置兜底口径一致。
"""
import argparse
import sqlite3
import numpy as np
import pandas as pd
from pathlib import Path

# ============ 与 C# IndicatorCalculator.cs 完全一致的指标公式 ============
def sma(arr, period):
    r = np.full(len(arr), np.nan)
    if len(arr) >= period:
        c = np.cumsum(np.insert(arr.astype(float), 0, 0.0))
        r[period - 1:] = (c[period:] - c[:-period]) / period
    return r

def ema(arr, period):
    arr = arr.astype(float)
    r = np.empty(len(arr)); k = 2.0 / (period + 1)
    r[0] = arr[0]
    for i in range(1, len(arr)):
        r[i] = (arr[i] - r[i - 1]) * k + r[i - 1]
    return r

def boll(closes, period=20, mult=2.0):
    n = len(closes)
    mid = sma(closes, period)
    up = np.full(n, np.nan); dn = np.full(n, np.nan)
    for i in range(period - 1, n):
        seg = closes[i - period + 1:i + 1].astype(float)
        std = np.sqrt(((seg - mid[i]) ** 2).mean())   # 总体标准差（同 C# 的 sumSq/20）
        up[i] = mid[i] + mult * std
        dn[i] = mid[i] - mult * std
    return up, mid, dn

def macd(closes, fast=12, slow=26, signal=9):
    dif = ema(closes, fast) - ema(closes, slow)
    dea = ema(dif, signal)
    return dif, dea, (dif - dea) * 2.0

# ============ 合成行情 ============
def synth_ohlcv(dates, seed, start_price, limit_ups=0):
    """随机游走合成 OHLCV；limit_ups>0 时注入若干近似涨停日（供涨停训练演示）"""
    rng = np.random.default_rng(seed)
    n = len(dates)
    # 分段趋势 + 波动率聚类，让形态识别有东西可看
    drift = np.repeat(rng.uniform(-0.0015, 0.0018, n // 60 + 1), 60)[:n]
    vol = np.repeat(rng.uniform(0.008, 0.030, n // 40 + 1), 40)[:n]
    rets = rng.normal(drift, vol)
    closes = start_price * np.exp(np.cumsum(rets))
    opens = closes * (1 + rng.normal(0, 0.004, n))
    highs = np.maximum(opens, closes) * (1 + np.abs(rng.normal(0, 0.005, n)))
    lows = np.minimum(opens, closes) * (1 - np.abs(rng.normal(0, 0.005, n)))
    volumes = rng.integers(1_000_000, 80_000_000, n)

    for idx in rng.choice(np.arange(30, n - 2), size=min(limit_ups, n - 32), replace=False):
        prev = closes[idx - 1]
        opens[idx] = prev
        closes[idx] = round(prev * 1.098, 2)      # 涨停日（10% 板，留 1.095 判定容差内）
        highs[idx] = closes[idx]; lows[idx] = opens[idx]
        volumes[idx] = int(volumes[idx] * 1.8)

    return pd.DataFrame({
        "trade_date": dates.strftime("%Y%m%d"),
        "open": np.round(opens, 2), "high": np.round(highs, 2),
        "low": np.round(lows, 2), "close": np.round(closes, 2),
        "volume": volumes.astype(np.int64),
    })

def add_indicators(df):
    c = df["close"].values; v = df["volume"].values.astype(float)
    up, mid, dn = boll(c)
    dif, dea, hist = macd(c)
    ind = pd.DataFrame({
        "ma5": sma(c, 5), "ma10": sma(c, 10), "ma20": sma(c, 20),
        "ema5": ema(c, 5), "ema10": ema(c, 10), "ema20": ema(c, 20),
        "boll_up": up, "boll_mid": mid, "boll_dn": dn,
        "vol_ma5": sma(v, 5), "vol_ma10": sma(v, 10),
        "dif": dif, "dea": dea, "macd_hist": hist,
    })
    return pd.concat([df, ind], axis=1)

# ============ 表结构（与 DatabaseService.cs 查询口径一致） ============
DDL = """
CREATE TABLE IF NOT EXISTS cy_stock_info (code TEXT PRIMARY KEY, name TEXT, is_st INTEGER DEFAULT 0);
CREATE TABLE IF NOT EXISTS cy_daily (
    code TEXT NOT NULL, trade_date TEXT NOT NULL,
    open REAL, high REAL, low REAL, close REAL, volume INTEGER,
    ma5 REAL, ma10 REAL, ma20 REAL, ema5 REAL, ema10 REAL, ema20 REAL,
    boll_up REAL, boll_mid REAL, boll_dn REAL,
    vol_ma5 REAL, vol_ma10 REAL, dif REAL, dea REAL, macd_hist REAL,
    PRIMARY KEY (code, trade_date));
CREATE TABLE IF NOT EXISTS cy_index_daily (
    code TEXT NOT NULL, trade_date TEXT NOT NULL,
    open REAL, high REAL, low REAL, close REAL, volume INTEGER,
    PRIMARY KEY (code, trade_date));
CREATE TABLE IF NOT EXISTS cy_us_daily (
    code TEXT NOT NULL, trade_date TEXT NOT NULL,
    open REAL, high REAL, low REAL, close REAL, volume INTEGER,
    PRIMARY KEY (code, trade_date));
CREATE TABLE IF NOT EXISTS cy_hk_daily (
    code TEXT NOT NULL, trade_date TEXT NOT NULL,
    open REAL, high REAL, low REAL, close REAL, volume INTEGER,
    PRIMARY KEY (code, trade_date));
CREATE TABLE IF NOT EXISTS cy_future_daily (
    code TEXT NOT NULL, trade_date TEXT NOT NULL,
    open REAL, high REAL, low REAL, close REAL, volume INTEGER,
    PRIMARY KEY (code, trade_date));
CREATE TABLE IF NOT EXISTS cy_bond_daily (
    code TEXT NOT NULL, trade_date TEXT NOT NULL,
    open REAL, high REAL, low REAL, close REAL, volume INTEGER,
    PRIMARY KEY (code, trade_date));
"""

COLS = ("code, trade_date, open, high, low, close, volume, "
        "ma5, ma10, ma20, ema5, ema10, ema20, boll_up, boll_mid, boll_dn, "
        "vol_ma5, vol_ma10, dif, dea, macd_hist")

def insert_daily(conn, table, code, df):
    rows = [(code, *r) for r in df.itertuples(index=False, name=None)]
    conn.executemany(
        f"INSERT OR REPLACE INTO {table} ({COLS}) VALUES "
        f"({','.join('?' * 21)})", rows)

def insert_plain(conn, table, code, df):
    rows = [(code, r[0], r[1], r[2], r[3], r[4], r[5])
            for r in df.itertuples(index=False, name=None)]
    conn.executemany(
        f"INSERT OR REPLACE INTO {table} (code, trade_date, open, high, low, close, volume) "
        f"VALUES ({','.join('?' * 7)})", rows)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="stockdata/cy_stock.db", help="输出路径")
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--days", type=int, default=800, help="每标的日线根数")
    a = ap.parse_args()

    out = Path(a.out); out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists(): out.unlink()
    conn = sqlite3.connect(out)
    conn.executescript(DDL)

    dates = pd.bdate_range(end="2025-12-31", periods=a.days)   # 工作日近似交易日

    # ---- A 股演示票（900xxx 为虚构代码，避免与真实股票混淆；每只注入 3 个涨停日）----
    stocks = [(f"9000{i:02d}", f"演示股票{chr(65 + i)}") for i in range(12)]
    conn.executemany("INSERT OR REPLACE INTO cy_stock_info VALUES (?,?,0)",
                     [(c, n) for c, n in stocks])
    for i, (code, _) in enumerate(stocks):
        df = synth_ohlcv(dates, a.seed + i, start_price=rng_start(i), limit_ups=3)
        insert_daily(conn, "cy_daily", code, add_indicators(df))

    # ---- 指数（代码与 MainViewModel.IndexNames 对齐）----
    indices = [("sh000001", "上证指数"), ("sz399001", "深证成指"), ("sz399006", "创业板指"),
               ("sh000300", "沪深300"), ("sh000905", "中证500"), ("sh000688", "科创50")]
    for i, (code, _) in enumerate(indices):
        df = synth_ohlcv(dates, a.seed + 100 + i, start_price=1000 + i * 500)
        insert_plain(conn, "cy_index_daily", code, df)

    # ---- 美股 / 港股 / 期货 / 转债（短一点，够抽局即可；表结构无指标列）----
    us = ["AAPL", "TSLA", "MSFT", "NVDA", "AMZN"]
    for i, code in enumerate(us):
        insert_plain(conn, "cy_us_daily", code, synth_ohlcv(dates[-400:], a.seed + 200 + i, 150 + i * 80))
    hk = [("00700", "腾讯控股"), ("09988", "阿里巴巴"), ("01810", "小米集团")]
    for i, (code, _) in enumerate(hk):
        insert_plain(conn, "cy_hk_daily", code, synth_ohlcv(dates[-400:], a.seed + 300 + i, 300 + i * 60))
    fut = [("RB0", "螺纹钢"), ("I0", "铁矿石"), ("CU0", "沪铜"), ("AU0", "沪金"), ("AG0", "沪银")]
    for i, (code, _) in enumerate(fut):
        insert_plain(conn, "cy_future_daily", code, synth_ohlcv(dates[-400:], a.seed + 400 + i, 3000 + i * 1200))
    bond = [("110059", "浦发转债"), ("113044", "大秦转债"), ("127005", "长证转债")]
    for i, (code, _) in enumerate(bond):
        insert_plain(conn, "cy_bond_daily", code, synth_ohlcv(dates[-400:], a.seed + 500 + i, 100 + i * 10))

    conn.commit()
    for t in ["cy_stock_info", "cy_daily", "cy_index_daily", "cy_us_daily",
              "cy_hk_daily", "cy_future_daily", "cy_bond_daily"]:
        n = conn.execute(f"SELECT COUNT(*) FROM {t}").fetchone()[0]
        print(f"  {t}: {n} 行")
    conn.close()
    kb = out.stat().st_size / 1024
    print(f"✓ 示例库已生成: {out} ({kb:.0f} KB)")
    print("  注意：数据为合成演示数据，仅用于跑通程序，不构成真实行情！")

def rng_start(i):   # 让各票起始价位有区分度
    return [18.5, 6.2, 85.0, 12.8, 230.0, 3.5, 45.6, 9.9, 150.0, 28.3, 5.1, 66.6][i % 12]

if __name__ == "__main__":
    main()