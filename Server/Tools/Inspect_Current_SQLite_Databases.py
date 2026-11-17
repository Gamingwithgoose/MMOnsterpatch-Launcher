#!/usr/bin/env python3
import json
import os
import sqlite3

root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
data = os.path.join(root, 'OfficialServer', 'data')
for db_name in ('bank.db', 'social.db'):
    path = os.path.join(data, db_name)
    print(f'=== {db_name} ===')
    if not os.path.exists(path):
        print('missing:', path)
        continue
    con = sqlite3.connect(path)
    con.row_factory = sqlite3.Row
    try:
        tables = con.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").fetchall()
        for t in tables:
            name = t['name']
            count = con.execute(f'SELECT COUNT(*) AS c FROM "{name}"').fetchone()['c']
            print(f'{name}: {count} rows')
            cols = con.execute(f'PRAGMA table_info("{name}")').fetchall()
            print('  columns:', ', '.join(c['name'] for c in cols))
    finally:
        con.close()
