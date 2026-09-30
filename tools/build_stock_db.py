#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_stock_db.py — 用 akshare 免费数据源构建全量训练数据库 cy_stock.db
覆盖：沪深 A 股（cy_daily + cy_stock_info）+ 6 个主要指数（cy_index_daily）

用法：
    python build_stock_db.py                  # 全量（断点续跑：已入库的股票自动跳过）
    python build_stock_db.py --update         # 增量：只补每只股票的最后 60 天
    python build_stock_db.py --out D:\\path\\cy_stock.db

说明：
- 数据截止 20251231（与程序查询口径一致）；
- 前复权（qfq）口径，与程序内涨跌停 10%/20% 判定兼容；
- 港股/美股/期货/转债表的生成脚本暂未提供（cy_hk_daily 等留空也能启动程序，
  对应训练入口会提示数据不足），欢迎 PR 补充。
"""
import argparse
import sqlite3
import sys
import time
from pathlib import Path

import akshare as ak
import pandas as pd

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
CREATE INDEX IF NOT EXISTS idx_daily_code_date ON cy_daily(code, trade_date);
CREATE INDEX IF NOT EXISTS idx_index_code_date ON cy_index_daily(code, trade_date);
"""

INDICES = [("sh000001", "上证指数"), ("sz399001", "深证成指"), ("sz399006", "创业板指"),
           ("sh000300", "沪深300"), ("sh000905", "中证500"), ("sh000688", "科创50")]
END = "20251231"

def norm_daily(df):
    """akshare stock_zh_a_hist 输出 → 程序口径（trade_date 转 yyyyMMdd 字符串）"""
    df = df.rename(columns={"日期": "trade_date", "开盘": "open", "最高": "high",
                            "最低": "low", "收盘": "close", "成交量": "volume"})
    df["trade_date"] = df["trade_date"].astype(str).str.replace("-", "")
    df["volume"] = df["volume"].astype("int64")
    return df[["trade_date", "open", "high", "low", "close", "volume"]]

def insert_daily(conn, code, df):
    conn.executemany(
        "INSERT OR REPLACE INTO cy_daily "
        "(code, trade_date, open, high, low, close, volume,"
        " ma5, ma10, ma20, ema5, ema10, ema20, boll_up, boll_mid, boll_dn,"
        " vol_ma5, vol_ma10, dif, dea, macd_hist) VALUES (?,?,?,?,?,?,?,"
        " NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)",
        [(code, r[0], r[1], r[2], r[3], r[4], r[5]) for r in df.itertuples(index=False, name=None)])

def insert_index(conn, code, df):
    conn.executemany(
        "INSERT OR REPLACE INTO cy_index_daily (code, trade_date, open, high, low, close, volume)"
        " VALUES (?,?,?,?,?,?,?)",
        [(code, r[0], r[1], r[2], r[3], r[4], r[5]) for r in df.itertuples(index=False, name=None)])

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="stockdata/cy_stock.db")
    ap.add_argument("--update", action="store_true", help="只补最近 60 个交易日")
    a = ap.parse_args()

    out = Path(a.out); out.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(out)
    conn.executescript(DDL)

    start = "20251101" if a.update else "19900101"

    # ---- 股票清单（东财现货接口，含名称/ST 标记）----
    spot = ak.stock_zh_a_spot_em()
    spot = spot[["代码", "名称"]].rename(columns={"代码": "code", "名称": "name"})
    spot["is_st"] = spot["name"].str.contains("ST").astype(int)
    conn.executemany("INSERT OR REPLACE INTO cy_stock_info VALUES (?,?,?)",
                     spot.itertuples(index=False, name=None))
    conn.commit()
    print(f"股票清单: {len(spot)} 只")

    done = {r[0] for r in conn.execute("SELECT DISTINCT code FROM cy_daily")}
    todo = [c for c in spot["code"] if c not in done] if not a.update else list(spot["code"])
    print(f"待拉取: {len(todo)} 只（已跳过 {len(done)} 只）")

    ok = fail = 0
    for i, code in enumerate(todo):
        for attempt in range(3):
            try:
                df = ak.stock_zh_a_hist(symbol=code, period="daily",
                                        start_date=start, end_date=END, adjust="qfq")
                if df is not None and len(df):
                    insert_daily(conn, code, norm_daily(df))
                ok += 1
                break
            except Exception as e:
                if attempt == 2:
                    print(f"  [跳过] {code}: {e}")
                    fail += 1
                time.sleep(2 * (attempt + 1))
        if (i + 1) % 50 == 0:
            conn.commit()
            print(f"  进度 {i + 1}/{len(todo)}  成功 {ok}  失败 {fail}")
            time.sleep(0.5)   # 温和限流
    conn.commit()

    # ---- 指数 ----
    for code, name in INDICES:
        try:
            symbol = code[2:] if code[:2] in ("sh", "sz") else code
            df = ak.index_zh_a_hist(symbol=symbol, period="daily",
                                    start_date="19900101", end_date=END)
            df = df.rename(columns={"日期": "trade_date", "开盘": "open", "最高": "high",
                                    "最低": "low", "收盘": "close", "成交量": "volume"})
            df["trade_date"] = df["trade_date"].astype(str).str.replace("-", "")
            insert_index(conn, code, df[["trade_date", "open", "high", "low", "close", "volume"]])
            print(f"指数 {name} 完成")
        except Exception as e:
            print(f"指数 {name} 失败: {e}")
    conn.commit()

    n = conn.execute("SELECT COUNT(*) FROM cy_daily").fetchone()[0]
    print(f"? 完成: {out}  日线共 {n} 行，成功 {ok} 失败 {fail}")
    conn.close()

if __name__ == "__main__":
    sys.exit(main())