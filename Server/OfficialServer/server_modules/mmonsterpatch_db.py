r"""
MMOnsterpatch native MariaDB database layer.

This is the official v0.15.4 server database access layer. It does not monkey-patch
Python's sqlite3 module and it does not open runtime .db files. All active server
state is read from and written to MariaDB databases configured in:

    Server\Config\database.ini

The existing gameplay/social/trading modules still call a small execute/fetch API
while their high-level logic is preserved. This module owns MariaDB connection,
schema bootstrap, placeholder binding, transaction handling, and the handful of
SQL clause normalizations needed for the old call sites during the native cutover.
"""
from __future__ import annotations

import configparser
import os
import re
import threading
from pathlib import Path
from typing import Any, Iterable, Optional

from mmonsterpatch_mariadb_schema import SCHEMA_DDL

try:
    import pymysql
except Exception as ex:
    pymysql = None
    _IMPORT_ERROR = ex
else:
    _IMPORT_ERROR = None

class Error(Exception): pass
class OperationalError(Error): pass
class IntegrityError(Error): pass
class Row: pass

_LOCK = threading.RLock()
_SCHEMA_DONE: set[str] = set()
_DATABASE_DONE: set[str] = set()
_CONFIG_CACHE: dict[str, str] | None = None

_ENV_TO_INI = {
    "MMONSTERPATCH_DB_HOST": "Host",
    "MMONSTERPATCH_DB_PORT": "Port",
    "MMONSTERPATCH_DB_USER": "User",
    "MMONSTERPATCH_DB_PASSWORD": "Password",
    "MMONSTERPATCH_DB_AUTH": "AuthDatabase",
    "MMONSTERPATCH_DB_TRADINGPOST": "TradingPostDatabase",
    "MMONSTERPATCH_DB_SOCIAL": "SocialDatabase",
    "MMONSTERPATCH_DB_FILES": "FilesDatabase",
}

def _find_database_ini() -> Path | None:
    here = Path(__file__).resolve()
    for parent in [here.parent, *here.parents]:
        candidates = [
            parent / "Config" / "database.ini",
            parent.parent / "Config" / "database.ini" if parent.parent != parent else parent / "Config" / "database.ini",
        ]
        for candidate in candidates:
            try:
                if candidate.is_file(): return candidate
            except Exception: pass
    return None

def _load_config() -> dict[str, str]:
    global _CONFIG_CACHE
    if _CONFIG_CACHE is not None: return _CONFIG_CACHE
    cfg = {
        "Provider": "MariaDB", "Host": "127.0.0.1", "Port": "3307",
        "User": "mmonsterpatch", "Password": "T1r2e3y421!!",
        "AuthDatabase": "mmonsterpatch_auth", "TradingPostDatabase": "mmonsterpatch_tradingpost",
        "SocialDatabase": "mmonsterpatch_social", "FilesDatabase": "mmonsterpatch_files",
    }
    path = _find_database_ini()
    if path is not None:
        parser = configparser.ConfigParser()
        try:
            parser.read(path, encoding="utf-8")
            if parser.has_section("Database"):
                for key in list(cfg.keys()):
                    if parser.has_option("Database", key):
                        cfg[key] = parser.get("Database", key).strip()
        except Exception:
            pass
    _CONFIG_CACHE = cfg
    return cfg

def _env(name: str, default: str) -> str:
    value = os.environ.get(name, "")
    if value and value.strip(): return value.strip()
    key = _ENV_TO_INI.get(name)
    if key:
        value = _load_config().get(key, "")
        if value and value.strip() and not value.strip().startswith("CHANGE_ME"):
            return value.strip()
    return default

def _password() -> str:
    value = os.environ.get("MMONSTERPATCH_DB_PASSWORD", "")
    if value: return value
    value = _load_config().get("Password", "")
    if value and not value.strip().startswith("CHANGE_ME"): return value
    return "T1r2e3y421!!"

def _port() -> int:
    try: return int(_env("MMONSTERPATCH_DB_PORT", "3307"))
    except Exception: return 3307

def _db_name(logical: str) -> str:
    l = (logical or "social").lower()
    if l in ("auth", "mmonsterpatch_auth"):
        return _env("MMONSTERPATCH_DB_AUTH", "mmonsterpatch_auth")
    if l in ("tradingpost", "trading", "bank", "gts", "mmonsterpatch_tradingpost"):
        return _env("MMONSTERPATCH_DB_TRADINGPOST", "mmonsterpatch_tradingpost")
    if l in ("files", "mmonsterpatch_files"):
        return _env("MMONSTERPATCH_DB_FILES", "mmonsterpatch_files")
    return _env("MMONSTERPATCH_DB_SOCIAL", "mmonsterpatch_social")

def _logical_from_value(value: Any) -> str:
    s = str(value or "social").replace("\\", "/").lower()
    if s in ("auth", "tradingpost", "trading", "bank", "social", "files"): return s
    if "auth" in s or "account" in s: return "auth"
    if "bank" in s or "trading" in s or "gts" in s: return "tradingpost"
    if "files" in s: return "files"
    return "social"

def _server_conn(database: str | None = None, autocommit: bool = True):
    if pymysql is None:
        raise OperationalError("PyMySQL is required for MariaDB mode. Run on the server: py -m pip install PyMySQL") from _IMPORT_ERROR
    try:
        return pymysql.connect(host=_env("MMONSTERPATCH_DB_HOST", "127.0.0.1"), port=_port(),
                               user=_env("MMONSTERPATCH_DB_USER", "mmonsterpatch"), password=_password(),
                               database=database, charset="utf8mb4", autocommit=autocommit,
                               cursorclass=pymysql.cursors.Cursor)
    except Exception as ex:
        raise OperationalError(str(ex)) from ex

def _ensure_database(logical: str) -> str:
    database = _db_name(logical)
    # MariaDB-native v0.15.9: CREATE DATABASE is a startup/bootstrap concern, not a
    # per-request cost.  The old SQLite-shaped call sites open many short-lived
    # logical connections; without this process cache each request paid an extra
    # server-level connection plus CREATE DATABASE IF NOT EXISTS round-trip before
    # doing useful work.
    key = f"{logical}:{database}"
    if key in _DATABASE_DONE:
        return database
    with _LOCK:
        if key in _DATABASE_DONE:
            return database
        try:
            raw = _server_conn(None, autocommit=True)
            try:
                with raw.cursor() as c:
                    c.execute(f"CREATE DATABASE IF NOT EXISTS `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci")
            finally:
                raw.close()
        except Exception as ex:
            raise OperationalError(str(ex)) from ex
        _DATABASE_DONE.add(key)
    return database

def _ensure_schema(logical: str, database: str):
    key = f"{logical}:{database}"
    if key in _SCHEMA_DONE: return
    with _LOCK:
        if key in _SCHEMA_DONE: return
        ddls = SCHEMA_DDL.get(logical, [])
        if not ddls:
            _SCHEMA_DONE.add(key); return
        raw = _server_conn(database, autocommit=True)
        try:
            with raw.cursor() as c:
                for ddl in ddls:
                    ddl2 = _normalize_ddl(ddl)
                    try:
                        c.execute(ddl2)
                    except Exception as ex:
                        msg = str(ex).lower()
                        if "duplicate" in msg or "already exists" in msg:
                            continue
                        raise
        finally:
            raw.close()
        _SCHEMA_DONE.add(key)

def connect(logical: Any = "social", timeout: Optional[float] = None, isolation_level: Any = None, **kwargs: Any):
    l = _logical_from_value(logical)
    database = _ensure_database(l)
    _ensure_schema(l, database)
    # sqlite isolation_level=None meant autocommit. The Trading Post relies on that.
    autocommit = (isolation_level is None)
    return Connection(_server_conn(database, autocommit=autocommit), l, database, autocommit)

def connect_auth(**kwargs): return connect("auth", **kwargs)
def connect_tradingpost(**kwargs): return connect("tradingpost", **kwargs)
def connect_social(**kwargs): return connect("social", **kwargs)
def connect_files(**kwargs): return connect("files", **kwargs)

class _Row:
    __slots__ = ("_values", "_names", "_map")
    def __init__(self, values: Iterable[Any], names: Iterable[str]):
        self._values = tuple(values); self._names = tuple(names); self._map = {n:i for i,n in enumerate(self._names)}
    def __getitem__(self, key: Any) -> Any:
        if isinstance(key, str): return self._values[self._map[key]]
        return self._values[key]
    def __iter__(self): return iter(self._values)
    def __len__(self): return len(self._values)
    def keys(self): return self._names
    def __repr__(self): return repr(tuple(self._values))

class Cursor:
    def __init__(self, conn: "Connection", raw_cursor: Any):
        self._conn = conn; self._cur = raw_cursor; self._rows = None; self._pos = 0
        self.description = None; self.lastrowid = 0; self.rowcount = -1
    def execute(self, sql: str, params: Any = None):
        sql2, params2, special = _prepare_sql(sql, params, self._conn.logical, self._conn.database)
        try:
            if special is not None:
                self._rows = special; self._pos = 0
                self.description = [(n,) for n in (special[0].keys() if special else [])]
                self.rowcount = len(special); self.lastrowid = 0; return self
            if sql2 is None:
                self._rows = []; self._pos = 0; self.description = []; self.rowcount = 0; self.lastrowid = 0; return self
            self._cur.execute(sql2, params2)
            self.description = self._cur.description; self.rowcount = self._cur.rowcount
            self.lastrowid = getattr(self._cur, "lastrowid", 0) or 0
            if self.description:
                names=[d[0] for d in self.description]
                self._rows=[_Row(r,names) for r in self._cur.fetchall()]; self._pos=0
            else:
                self._rows=None; self._pos=0
            return self
        except Exception as ex:
            msg=str(ex).lower(); name=ex.__class__.__name__.lower(); sql_low=(sql2 or sql or "").strip().lower()
            # Native MariaDB schema is installed up front. Some preserved call sites still
            # issue SQLite-style CREATE INDEX IF NOT EXISTS / ALTER ADD COLUMN checks.
            # Treat duplicate index/column creation as the intended IF NOT EXISTS no-op.
            if (sql_low.startswith("create index") or sql_low.startswith("create unique index")) and ("duplicate" in msg or "already exists" in msg):
                self._rows=[]; self._pos=0; self.description=[]; self.rowcount=0; self.lastrowid=0; return self
            if sql_low.startswith("alter table") and ("duplicate column" in msg or "already exists" in msg):
                self._rows=[]; self._pos=0; self.description=[]; self.rowcount=0; self.lastrowid=0; return self
            if "duplicate" in msg or "integrity" in name:
                raise IntegrityError(str(ex)) from ex
            raise OperationalError(str(ex)) from ex
    def fetchone(self):
        if self._rows is None or self._pos >= len(self._rows): return None
        row=self._rows[self._pos]; self._pos += 1; return row
    def fetchall(self):
        if self._rows is None: return []
        rows=self._rows[self._pos:]; self._pos=len(self._rows); return rows
    def close(self):
        try: self._cur.close()
        except Exception: pass
    def __iter__(self): return iter(self.fetchall())
    def __enter__(self): return self
    def __exit__(self, exc_type, exc, tb): self.close(); return False

class Connection:
    def __init__(self, raw: Any, logical: str, database: str, autocommit: bool):
        self._raw=raw; self.logical=logical; self.database=database; self.autocommit=autocommit; self.row_factory=None
    def cursor(self): return Cursor(self, self._raw.cursor())
    def execute(self, sql: str, params: Any = None):
        cur=self.cursor(); return cur.execute(sql, params)
    def commit(self):
        try: self._raw.commit()
        except Exception as ex: raise OperationalError(str(ex)) from ex
    def rollback(self):
        try: self._raw.rollback()
        except Exception as ex: raise OperationalError(str(ex)) from ex
    def close(self):
        try: self._raw.close()
        except Exception: pass
    def __enter__(self): return self
    def __exit__(self, exc_type, exc, tb):
        if not self.autocommit:
            if exc_type is None: self.commit()
            else: self.rollback()
        self.close(); return False

def _prepare_sql(sql: str, params: Any, logical: str, database: str):
    s=(sql or "").strip()
    if not s: return None, None, None
    low=re.sub(r"\s+", " ", s.lower())
    if low.startswith("pragma journal_mode") or low.startswith("pragma foreign_keys"):
        return None, None, None
    m=re.match(r"pragma\s+table_info\s*\(\s*`?([A-Za-z0-9_]+)`?\s*\)", s, re.I)
    if m: return None, None, _table_info_rows(database, m.group(1))
    if "sqlite_master" in low:
        m=re.search(r"name\s*=\s*'([^']+)'", s, re.I)
        return None, None, _table_exists_rows(database, m.group(1) if m else "")
    if low.startswith("create table"):
        # Native schema is installed up front. Avoid parsing stale SQLite DDL at runtime.
        return None, None, None
    if low.startswith("create index") or low.startswith("create unique index"):
        s = _normalize_ddl(s)
    elif low.startswith("alter table"):
        s = _normalize_ddl(s)
    elif low.startswith("begin immediate") or low == "begin":
        return "START TRANSACTION", None, None
    elif low == "commit":
        return "COMMIT", None, None
    elif low == "rollback":
        return "ROLLBACK", None, None
    elif "last_insert_rowid()" in low:
        return "SELECT LAST_INSERT_ID()", None, None
    else:
        s = _normalize_dml(s)
    s, params2 = _convert_placeholders(s, params)
    return s, params2, None

def _table_info_rows(database: str, table: str):
    raw=_server_conn(database, autocommit=True)
    try:
        with raw.cursor() as c:
            c.execute("SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, COLUMN_KEY FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=%s AND TABLE_NAME=%s ORDER BY ORDINAL_POSITION", (database, table))
            rows=[]
            for i,(name,coltype,nullable,default,key) in enumerate(c.fetchall()):
                rows.append(_Row((i,name,coltype,0 if nullable=='YES' else 1,default,1 if key=='PRI' else 0), ("cid","name","type","notnull","dflt_value","pk")))
            return rows
    finally:
        raw.close()

def _table_exists_rows(database: str, table: str):
    raw=_server_conn(database, autocommit=True)
    try:
        with raw.cursor() as c:
            c.execute("SELECT TABLE_NAME AS name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=%s AND TABLE_NAME=%s LIMIT 1", (database, table))
            return [_Row((r[0],), ("name",)) for r in c.fetchall()]
    finally:
        raw.close()

def _convert_placeholders(sql: str, params: Any):
    if params is None: return sql, None
    if isinstance(params, dict):
        names=re.findall(r"[$:@]([A-Za-z_][A-Za-z0-9_]*)", sql)
        for n in names: sql=re.sub(r"[$:@]"+re.escape(n)+r"\b", "%s", sql)
        return sql, tuple(params.get(n) for n in names)
    return sql.replace("?", "%s"), params

def _normalize_dml(sql: str) -> str:
    s=sql
    s=re.sub(r"\s+COLLATE\s+NOCASE\b", "", s, flags=re.I)
    s=re.sub(r"\bINSERT\s+OR\s+REPLACE\s+INTO\b", "REPLACE INTO", s, flags=re.I)
    s=re.sub(r"\bINSERT\s+OR\s+IGNORE\s+INTO\b", "INSERT IGNORE INTO", s, flags=re.I)
    s=_convert_on_conflict(s)
    return s

def _normalize_ddl(sql: str) -> str:
    s=sql.strip().rstrip(';')
    s=re.sub(r"\s+COLLATE\s+NOCASE\b", "", s, flags=re.I)
    s=re.sub(r"\bAUTOINCREMENT\b", "AUTO_INCREMENT", s, flags=re.I)
    s=re.sub(r"\bINTEGER\s+PRIMARY\s+KEY\s+AUTO_INCREMENT\b", "BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY", s, flags=re.I)
    s=re.sub(r"\bINTEGER\s+PRIMARY\s+KEY\b", "BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY", s, flags=re.I)
    if re.match(r"CREATE\s+INDEX", s, re.I) or re.match(r"CREATE\s+UNIQUE\s+INDEX", s, re.I):
        # MariaDB does not support IF NOT EXISTS for CREATE INDEX in older builds.
        m=re.match(r"CREATE\s+(UNIQUE\s+)?INDEX\s+(IF\s+NOT\s+EXISTS\s+)?`?([A-Za-z0-9_]+)`?\s+ON\s+`?([A-Za-z0-9_]+)`?\s*\((.+)\)", s, re.I | re.S)
        if m:
            unique='UNIQUE ' if m.group(1) else ''
            idx=m.group(3); table=m.group(4); cols=m.group(5)
            cols=re.sub(r"\s+COLLATE\s+NOCASE\b", "", cols, flags=re.I)
            return f"CREATE {unique}INDEX `{idx}` ON `{table}` ({cols})"
    s=re.sub(r"\bINTEGER\b", "BIGINT", s, flags=re.I)
    s=re.sub(r"\bREAL\b", "DOUBLE", s, flags=re.I)
    s=re.sub(r"\bTEXT\s+PRIMARY\s+KEY\b", "VARCHAR(255) NOT NULL PRIMARY KEY", s, flags=re.I)
    s=re.sub(r"\bTEXT\s+NOT\s+NULL\s+UNIQUE\b", "VARCHAR(255) NOT NULL UNIQUE", s, flags=re.I)
    s=re.sub(r"\bTEXT\s+UNIQUE\b", "VARCHAR(255) UNIQUE", s, flags=re.I)
    s=re.sub(r"\bTEXT\b", "LONGTEXT", s, flags=re.I)
    return s

def _convert_on_conflict(sql: str) -> str:
    m=re.search(r"\s+ON\s+CONFLICT\s*\([^)]*\)\s+DO\s+UPDATE\s+SET\s+(.+)$", sql, flags=re.I|re.S)
    if not m: return sql
    prefix=sql[:m.start()]
    assignments=m.group(1).strip().rstrip(';')
    assignments=re.sub(r"excluded\.([A-Za-z_][A-Za-z0-9_]*)", r"VALUES(\1)", assignments, flags=re.I)
    return prefix + " ON DUPLICATE KEY UPDATE " + assignments
