import base64
import hashlib
import html
import json
import math
import os
import re
import secrets
import socketserver
import mmonsterpatch_db as db
import threading
import time
import traceback
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from datetime import datetime, timedelta

import mmonsterpatch_moderation_core as moderation

ROOT = os.path.dirname(os.path.abspath(__file__))
DATA_DIR = os.path.join(ROOT, "data")
LOG_DIR = os.path.join(ROOT, "logs")
DB_PATH = "tradingpost"
SOCIAL_DB_PATH = "social"
HOST = os.environ.get("PBO_HOST", "0.0.0.0")
PORT = int(os.environ.get("PBO_PORT", "61526"))
SLOT_COUNT = 240
GTS_PAGE_SIZE = 30
RA_PAGE_SIZE = 30
RA_MAX_PRICE = 9_999_999
GTS_LISTING_DURATION_SECONDS = int(os.environ.get("MMP_GTS_LISTING_DURATION_SECONDS", str(48 * 60 * 60)))
MAIL_UNREAD_RETENTION_DAYS = int(os.environ.get("MMP_MAIL_UNREAD_RETENTION_DAYS", "90"))
MAIL_READ_RETENTION_DAYS = int(os.environ.get("MMP_MAIL_READ_RETENTION_DAYS", "30"))
MAIL_PAGE_SIZE = int(os.environ.get("MMP_MAIL_PAGE_SIZE", "50"))
MAILMON_HANDLE = os.environ.get("MMP_MAILMON_HANDLE", "MMOMailmon").strip() or "MMOMailmon"
MAILMON_GITHUB_URL = os.environ.get("MMP_MAILMON_GITHUB_URL", "https://github.com/maver1gam1ng/MMOnsterpatch").strip()
MAILMON_X_URL = os.environ.get("MMP_MAILMON_X_URL", "https://x.com/ItsJustGooseNow").strip()
ADMIN_REWARD_SUBJECT = os.environ.get("MMP_ADMIN_REWARD_SUBJECT", "System Reward").strip() or "System Reward"
ADMIN_REWARD_BODY = os.environ.get("MMP_ADMIN_REWARD_BODY", "Thank you for playing MMOnsterpatch. Here's a little gift to show our appreciation.").strip() or "Thank you for playing MMOnsterpatch. Here's a little gift to show our appreciation."

# -----------------------------------------------------------------------------
# Steam OpenID test settings
# -----------------------------------------------------------------------------
# For local testing on the same PC as the server, the default localhost URL works.
# For real players, this must be a public HTTPS URL that points to this server,
# for example: https://gts.yourdomain.com
OPENID_HTTP_HOST = os.environ.get("PBO_OPENID_HTTP_HOST", "0.0.0.0")
OPENID_HTTP_PORT = int(os.environ.get("PBO_OPENID_HTTP_PORT", "25891"))
OPENID_PUBLIC_BASE_URL = os.environ.get("PBO_OPENID_PUBLIC_BASE_URL", f"http://127.0.0.1:{OPENID_HTTP_PORT}").rstrip("/")
OPENID_REALM = os.environ.get("PBO_OPENID_REALM", OPENID_PUBLIC_BASE_URL).rstrip("/")
OPENID_STATE_TTL_SECONDS = int(os.environ.get("PBO_OPENID_STATE_TTL_SECONDS", "300"))
STEAM_OPENID_PROVIDER = "https://steamcommunity.com/openid/login"

# Optional: Steam Web API key for public display names.
# Get one from https://steamcommunity.com/dev/apikey and set it on the server only.
# The key is never sent to the Monsterpatch client.
def first_env_value(*names: str) -> str:
    for name in names:
        value = os.environ.get(name, "")
        if value and value.strip():
            return value.strip()
    return ""

# Optional: Steam Web API key for public display names.
# Accept a few aliases because older launchers/configs used different setting names.
STEAM_WEB_API_KEY = first_env_value("STEAM_WEB_API_KEY", "PBO_STEAM_WEB_API_KEY", "PBO_STEAM_API_KEY", "STEAM_API_KEY")
STEAM_DISPLAY_NAME_LOOKUP = os.environ.get("PBO_STEAM_DISPLAY_NAME_LOOKUP", "1").strip().lower() not in ("0", "false", "no", "off")
STEAM_DISPLAY_NAME_CACHE = {}

# Pending browser auth states live only in memory. If the server restarts, the
# user just starts the Steam login again. Nothing sensitive is stored here.
PENDING_OPENID = {}
PENDING_OPENID_LOCK = threading.Lock()
ACTIVE_BANK_HANDLERS_LOCK = threading.RLock()
ACTIVE_BANK_HANDLERS = set()

os.makedirs(DATA_DIR, exist_ok=True)
os.makedirs(LOG_DIR, exist_ok=True)


def log(msg: str):
    stamp = datetime.now().strftime("[%Y-%m-%d %H:%M:%S]")
    line = f"{stamp} {msg}"
    print(line)
    with open(os.path.join(LOG_DIR, "server.log"), "a", encoding="utf-8") as f:
        f.write(line + "\n")


def utcnow_iso() -> str:
    return datetime.now().isoformat()


def get_db():
    conn = db.connect_tradingpost(isolation_level=None)
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA foreign_keys=ON")
    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS accounts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT UNIQUE NOT NULL,
            password_salt TEXT NOT NULL,
            password_hash TEXT NOT NULL,
            created_at TEXT NOT NULL,
            last_login_at TEXT
        )
        """
    )
    ensure_account_openid_columns(conn)
    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS bank_slots (
            account_id INTEGER NOT NULL,
            slot_index INTEGER NOT NULL,
            species TEXT NOT NULL,
            level INTEGER NOT NULL,
            name_b64 TEXT NOT NULL,
            gender INTEGER NOT NULL,
            shiny INTEGER NOT NULL,
            blob_b64 TEXT NOT NULL,
            PRIMARY KEY(account_id, slot_index),
            FOREIGN KEY(account_id) REFERENCES accounts(id) ON DELETE CASCADE
        )
        """
    )
    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS gts_listings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            owner_account_id INTEGER NOT NULL,
            owner_username TEXT NOT NULL,
            request_species TEXT NOT NULL,
            offered_species TEXT NOT NULL,
            level INTEGER NOT NULL,
            name_b64 TEXT NOT NULL,
            gender INTEGER NOT NULL,
            shiny INTEGER NOT NULL,
            blob_b64 TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'open',
            completed_by_account_id INTEGER,
            completed_by_username TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            FOREIGN KEY(owner_account_id) REFERENCES accounts(id) ON DELETE CASCADE,
            FOREIGN KEY(completed_by_account_id) REFERENCES accounts(id) ON DELETE SET NULL
        )
        """
    )
    for ddl in (
        "ALTER TABLE gts_listings ADD COLUMN request_type TEXT NOT NULL DEFAULT 'MON'",
        "ALTER TABLE gts_listings ADD COLUMN request_sats INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE gts_listings ADD COLUMN offer_type TEXT NOT NULL DEFAULT 'MON'",
        "ALTER TABLE gts_listings ADD COLUMN offered_sats INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE gts_listings ADD COLUMN expires_at TEXT",
        "ALTER TABLE gts_listings ADD COLUMN expired_return_mail_id INTEGER",
        "ALTER TABLE gts_listings ADD COLUMN owner_character_id TEXT NOT NULL DEFAULT ''",
        "ALTER TABLE gts_listings ADD COLUMN owner_public_handle TEXT NOT NULL DEFAULT ''",
        "ALTER TABLE gts_listings ADD COLUMN completed_by_character_id TEXT NULL DEFAULT NULL",
        "ALTER TABLE gts_listings ADD COLUMN completed_by_public_handle TEXT NULL DEFAULT NULL",
    ):
        try:
            conn.execute(ddl)
        except db.OperationalError as ex:
            if "duplicate column" not in str(ex).lower():
                raise

    # MariaDB strict mode rejects completed listings when these legacy columns are
    # NOT NULL without a default. Keep the startup repair here so existing servers
    # are corrected without a manual HeidiSQL pass.
    for ddl in (
        "ALTER TABLE gts_listings MODIFY completed_by_character_id TEXT NULL DEFAULT NULL",
        "ALTER TABLE gts_listings MODIFY completed_by_public_handle TEXT NULL DEFAULT NULL",
    ):
        try:
            conn.execute(ddl)
        except Exception as ex:
            msg = str(ex).lower()
            if "syntax" not in msg and "near" not in msg and "unknown column" not in msg:
                log(f"Trading Post schema repair warning: {ex}")

    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS gts_sats_claims (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            owner_account_id INTEGER NOT NULL,
            source_listing_id INTEGER NOT NULL,
            amount INTEGER NOT NULL,
            claimed INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            claimed_at TEXT,
            notified_at TEXT,
            FOREIGN KEY(owner_account_id) REFERENCES accounts(id) ON DELETE CASCADE,
            FOREIGN KEY(source_listing_id) REFERENCES gts_listings(id) ON DELETE CASCADE
        )
        """
    )
    conn.execute("CREATE INDEX IF NOT EXISTS idx_gts_sats_claims_owner_claimed ON gts_sats_claims(owner_account_id, claimed, id)")

    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS mail_messages (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            recipient_account_id INTEGER NOT NULL,
            sender_account_id INTEGER,
            sender_name TEXT NOT NULL,
            subject_b64 TEXT NOT NULL,
            body_b64 TEXT NOT NULL,
            mail_type TEXT NOT NULL DEFAULT 'PLAYER',
            attachment_type TEXT NOT NULL DEFAULT 'NONE',
            attachment_blob_b64 TEXT,
            attachment_sats INTEGER NOT NULL DEFAULT 0,
            source_listing_id INTEGER,
            is_read INTEGER NOT NULL DEFAULT 0,
            is_saved INTEGER NOT NULL DEFAULT 0,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            claimed_at TEXT,
            created_at TEXT NOT NULL,
            read_at TEXT,
            deleted_at TEXT,
            expires_at TEXT NOT NULL,
            FOREIGN KEY(recipient_account_id) REFERENCES accounts(id) ON DELETE CASCADE,
            FOREIGN KEY(sender_account_id) REFERENCES accounts(id) ON DELETE SET NULL
        )
        """
    )
    for ddl in (
        "ALTER TABLE mail_messages ADD COLUMN recipient_character_id TEXT NOT NULL DEFAULT ''",
        "ALTER TABLE mail_messages ADD COLUMN recipient_public_handle TEXT NOT NULL DEFAULT ''",
        "ALTER TABLE mail_messages ADD COLUMN sender_character_id TEXT NOT NULL DEFAULT ''",
        "ALTER TABLE mail_messages ADD COLUMN sender_public_handle TEXT NOT NULL DEFAULT ''",
        "ALTER TABLE mail_messages ADD COLUMN notified_at TEXT",
    ):
        try:
            conn.execute(ddl)
        except db.OperationalError as ex:
            if "duplicate column" not in str(ex).lower():
                raise
    conn.execute("CREATE INDEX IF NOT EXISTS idx_mail_recipient_deleted_created ON mail_messages(recipient_account_id, is_deleted, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_mail_recipient_character_deleted_created ON mail_messages(recipient_account_id, recipient_character_id, is_deleted, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_mail_recipient_claimed ON mail_messages(recipient_account_id, is_deleted, claimed_at, attachment_type, id)")

    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS mailboxes (
            account_id INTEGER PRIMARY KEY,
            public_handle TEXT NOT NULL,
            created_at TEXT NOT NULL,
            last_seen_at TEXT NOT NULL,
            FOREIGN KEY(account_id) REFERENCES accounts(id) ON DELETE CASCADE
        )
        """
    )

    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS character_mailboxes (
            character_id TEXT PRIMARY KEY,
            account_id INTEGER NOT NULL,
            public_handle TEXT NOT NULL COLLATE NOCASE,
            created_at TEXT NOT NULL,
            last_seen_at TEXT NOT NULL,
            FOREIGN KEY(account_id) REFERENCES accounts(id) ON DELETE CASCADE
        )
        """
    )
    conn.execute("CREATE INDEX IF NOT EXISTS idx_character_mailboxes_handle ON character_mailboxes(public_handle COLLATE NOCASE)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_character_mailboxes_account ON character_mailboxes(account_id, character_id)")

    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS gts_claims (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            owner_account_id INTEGER NOT NULL,
            source_listing_id INTEGER NOT NULL,
            blob_b64 TEXT NOT NULL,
            claimed INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            claimed_at TEXT,
            FOREIGN KEY(owner_account_id) REFERENCES accounts(id) ON DELETE CASCADE,
            FOREIGN KEY(source_listing_id) REFERENCES gts_listings(id) ON DELETE CASCADE
        )
        """
    )
    conn.execute("CREATE INDEX IF NOT EXISTS idx_gts_listings_status_created ON gts_listings(status, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_gts_listings_status_request ON gts_listings(status, request_species, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_gts_listings_owner_status ON gts_listings(owner_account_id, status, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_gts_listings_owner_character_status ON gts_listings(owner_character_id, status, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_gts_claims_owner_claimed ON gts_claims(owner_account_id, claimed, id)")
    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS aio_sessions (
            token TEXT PRIMARY KEY,
            account_id INTEGER NOT NULL,
            steam_id64 TEXT,
            display_name TEXT,
            created_at TEXT NOT NULL,
            last_seen_at TEXT NOT NULL,
            expires_at TEXT NOT NULL,
            revoked INTEGER NOT NULL DEFAULT 0,
            FOREIGN KEY(account_id) REFERENCES accounts(id) ON DELETE CASCADE
        )
        """
    )
    conn.execute("CREATE INDEX IF NOT EXISTS idx_aio_sessions_account ON aio_sessions(account_id, revoked, expires_at)")
    try:
        conn.execute("ALTER TABLE aio_sessions ADD COLUMN source_ip TEXT")
    except db.OperationalError as ex:
        if "duplicate column" not in str(ex).lower():
            raise

    # Trading Post Marketplace tables. These are intentionally server-only and are
    # not used by the website code.
    try:
        conn.execute("ALTER TABLE gts_claims ADD COLUMN notified_at TEXT")
    except db.OperationalError as ex:
        if "duplicate column" not in str(ex).lower():
            raise

    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS rocket_pokemon_listings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            seller_account_id INTEGER NOT NULL,
            seller_username TEXT NOT NULL,
            price INTEGER NOT NULL,
            species TEXT NOT NULL,
            level INTEGER NOT NULL,
            name_b64 TEXT NOT NULL,
            gender INTEGER NOT NULL,
            shiny INTEGER NOT NULL,
            blob_b64 TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'open',
            buyer_account_id INTEGER,
            buyer_username TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            FOREIGN KEY(seller_account_id) REFERENCES accounts(id) ON DELETE CASCADE,
            FOREIGN KEY(buyer_account_id) REFERENCES accounts(id) ON DELETE SET NULL
        )
        """
    )
    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS rocket_item_listings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            seller_account_id INTEGER NOT NULL,
            seller_username TEXT NOT NULL,
            item_id TEXT NOT NULL,
            item_name_b64 TEXT NOT NULL,
            quantity INTEGER NOT NULL,
            price INTEGER NOT NULL,
            status TEXT NOT NULL DEFAULT 'open',
            buyer_account_id INTEGER,
            buyer_username TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            FOREIGN KEY(seller_account_id) REFERENCES accounts(id) ON DELETE CASCADE,
            FOREIGN KEY(buyer_account_id) REFERENCES accounts(id) ON DELETE SET NULL
        )
        """
    )
    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS rocket_payouts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            owner_account_id INTEGER NOT NULL,
            amount INTEGER NOT NULL,
            reason TEXT NOT NULL,
            source_kind TEXT NOT NULL,
            source_listing_id INTEGER NOT NULL,
            claimed INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            claimed_at TEXT,
            FOREIGN KEY(owner_account_id) REFERENCES accounts(id) ON DELETE CASCADE
        )
        """
    )
    conn.execute("CREATE INDEX IF NOT EXISTS idx_ra_pokemon_status_created ON rocket_pokemon_listings(status, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_ra_pokemon_seller_status ON rocket_pokemon_listings(seller_account_id, status, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_ra_item_status_created ON rocket_item_listings(status, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_ra_item_seller_status ON rocket_item_listings(seller_account_id, status, created_at, id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_ra_payouts_owner_claimed ON rocket_payouts(owner_account_id, claimed, id)")
    try:
        ensure_all_account_mailboxes(conn)
    except Exception as ex:
        log(f"Mailbox backfill warning: {ex}")
    return conn


def hash_password(password: str, salt: str) -> str:
    return hashlib.sha256((salt + password).encode("utf-8")).hexdigest()


def encode_message(msg: str) -> str:
    return base64.b64encode(msg.encode("utf-8")).decode("ascii")


def decode_message(msg: str) -> str:
    return base64.b64decode(msg.encode("ascii")).decode("utf-8")


def add_seconds_to_iso(base_iso: str, seconds: int) -> str:
    try:
        dt = datetime.fromisoformat(base_iso)
    except Exception:
        dt = datetime.now()
    return (dt + timedelta(seconds=max(1, int(seconds)))).isoformat()


def mail_expiry_for(is_read: bool, created_iso: str = None) -> str:
    days = MAIL_READ_RETENTION_DAYS if is_read else MAIL_UNREAD_RETENTION_DAYS
    try:
        base_dt = datetime.fromisoformat(created_iso) if created_iso else datetime.now()
    except Exception:
        base_dt = datetime.now()
    return (base_dt + timedelta(days=max(1, int(days)))).isoformat()


def add_mail(conn, recipient_account_id: int, sender_account_id, sender_name: str, subject: str, body: str,
             mail_type: str = "SYSTEM", attachment_type: str = "NONE", attachment_blob_b64: str = "",
             attachment_sats: int = 0, source_listing_id=None, now: str = None,
             recipient_character_id: str = "", recipient_public_handle: str = "",
             sender_character_id: str = "", sender_public_handle: str = "") -> int:
    now = now or utcnow_iso()
    attachment_type = (attachment_type or "NONE").upper()
    cur = conn.execute(
        """
        INSERT INTO mail_messages(
            recipient_account_id, sender_account_id, sender_name, subject_b64, body_b64, mail_type,
            attachment_type, attachment_blob_b64, attachment_sats, source_listing_id,
            is_read, is_saved, is_deleted, created_at, expires_at,
            recipient_character_id, recipient_public_handle, sender_character_id, sender_public_handle
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, 0, 0, ?, ?, ?, ?, ?, ?)
        """,
        (recipient_account_id, sender_account_id, sender_name or "System", encode_message(subject or "Mail"),
         encode_message(body or ""), mail_type or "SYSTEM", attachment_type, attachment_blob_b64 or "",
         int(attachment_sats or 0), source_listing_id, now, mail_expiry_for(False, now),
         (recipient_character_id or "").strip(), (recipient_public_handle or "").strip(),
         (sender_character_id or "").strip(), (sender_public_handle or "").strip())
    )
    return int(cur.lastrowid)

def cleanup_mail(conn):
    now = utcnow_iso()
    try:
        conn.execute(
            """
            UPDATE mail_messages
               SET is_deleted = 1, deleted_at = ?
             WHERE is_deleted = 0
               AND is_saved = 0
               AND expires_at <= ?
            """,
            (now, now)
        )
    except Exception as ex:
        log(f"mail cleanup failed: {ex}")


def expire_gts_listings(conn):
    now = utcnow_iso()
    rows = conn.execute(
        """
        SELECT id, owner_account_id, owner_username, offered_species, level, name_b64, blob_b64, expires_at,
               COALESCE(offer_type, 'MON') AS offer_type, COALESCE(offered_sats, 0) AS offered_sats,
               COALESCE(owner_character_id, '') AS owner_character_id, COALESCE(owner_public_handle, '') AS owner_public_handle
          FROM gts_listings
         WHERE status = 'open'
           AND expires_at IS NOT NULL
           AND expires_at != ''
           AND expires_at <= ?
         ORDER BY id ASC
        """,
        (now,)
    ).fetchall()
    for listing_id, owner_account_id, owner_username, offered_species, level, name_b64, blob_b64, expires_at, offer_type, offered_sats, owner_character_id, owner_public_handle in rows:
        offer_type = (offer_type or 'MON').upper()
        if offer_type == 'SATS':
            amount = int(offered_sats or 0)
            subject = f"Expired SATS offer returned: {amount} SATS"
            body = (
                f"Your Trading Post listing expired after 48 hours.\n\n"
                f"Returned: {amount} SATS.\n"
                f"Open this mail and claim the attachment to return the SATS to your balance."
            )
            mail_id = add_mail(conn, owner_account_id, None, "Trading Post", subject, body,
                               "TRADE_EXPIRED", "SATS", "", amount, listing_id, now,
                               owner_character_id or "", owner_public_handle or "")
        else:
            subject = f"Expired listing returned: {offered_species}"
            try:
                display_name = decode_message(name_b64) if name_b64 else offered_species
            except Exception:
                display_name = offered_species
            body = (
                f"Your Trading Post listing expired after 48 hours.\n\n"
                f"Returned: {display_name} ({offered_species}) Lv.{level}.\n"
                f"Open this mail and claim the attachment to return the MoN to your box."
            )
            mail_id = add_mail(conn, owner_account_id, None, "Trading Post", subject, body,
                               "TRADE_EXPIRED", "MON", blob_b64, 0, listing_id, now,
                               owner_character_id or "", owner_public_handle or "")
        conn.execute(
            "UPDATE gts_listings SET status = 'expired', updated_at = ?, expired_return_mail_id = ? WHERE id = ? AND status = 'open'",
            (now, mail_id, listing_id)
        )
        log(f"GTS listing expired: listing_id={listing_id} owner={owner_public_handle or owner_username} offer_type={offer_type} returned_mail_id={mail_id}")



def account_display_for_mail(conn, account_id: int) -> str:
    try:
        handle = account_mail_handle(conn, account_id)
        if handle:
            return handle
        row = conn.execute("SELECT username, steam_id64, display_name FROM accounts WHERE id = ?", (account_id,)).fetchone()
        if not row:
            return "Unknown"
        username, steam_id64, display_name = row
        return resolve_account_display_name(conn, account_id, steam_id64, display_name, username)
    except Exception:
        return "Unknown"


def ensure_tradingpost_maintenance(conn):
    cleanup_mail(conn)
    expire_gts_listings(conn)


def ensure_account_openid_columns(conn):
    """Ensure Steam/OpenID account columns exist in MariaDB tradingpost.accounts."""
    cols = {row[1] for row in conn.execute("PRAGMA table_info(accounts)").fetchall()}
    if "auth_provider" not in cols:
        conn.execute("ALTER TABLE accounts ADD COLUMN auth_provider TEXT NOT NULL DEFAULT 'password'")
    if "steam_id64" not in cols:
        conn.execute("ALTER TABLE accounts ADD COLUMN steam_id64 TEXT")
    if "display_name" not in cols:
        conn.execute("ALTER TABLE accounts ADD COLUMN display_name TEXT")
    if "mail_tag" not in cols:
        conn.execute("ALTER TABLE accounts ADD COLUMN mail_tag TEXT")
    if "mail_handle" not in cols:
        conn.execute("ALTER TABLE accounts ADD COLUMN mail_handle TEXT")
    # Partial unique index allows many password accounts with NULL steam_id64.
    conn.execute("CREATE UNIQUE INDEX IF NOT EXISTS idx_accounts_steam_id64 ON accounts(steam_id64) WHERE steam_id64 IS NOT NULL")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_accounts_mail_handle ON accounts(mail_handle)")


def cleanup_pending_openid():
    now = time.time()
    with PENDING_OPENID_LOCK:
        stale = [state for state, data in PENDING_OPENID.items() if now - data.get("created", 0) > OPENID_STATE_TTL_SECONDS]
        for state in stale:
            PENDING_OPENID.pop(state, None)


def openid_return_url(state: str) -> str:
    return f"{OPENID_PUBLIC_BASE_URL}/steam/return?state={urllib.parse.quote(state)}"


def build_steam_openid_login_url(state: str) -> str:
    return_to = openid_return_url(state)
    params = {
        "openid.ns": "http://specs.openid.net/auth/2.0",
        "openid.mode": "checkid_setup",
        "openid.return_to": return_to,
        "openid.realm": OPENID_REALM,
        "openid.identity": "http://specs.openid.net/auth/2.0/identifier_select",
        "openid.claimed_id": "http://specs.openid.net/auth/2.0/identifier_select",
    }
    return STEAM_OPENID_PROVIDER + "?" + urllib.parse.urlencode(params)


def validate_steam_openid_response(query_params):
    """Validate Steam OpenID browser callback with Steam's check_authentication step."""
    flat = {}
    for key, values in query_params.items():
        if key.startswith("openid.") and values:
            flat[key] = values[0]

    if flat.get("openid.mode") != "id_res":
        return False, None, "Steam did not return a successful login response."

    claimed_id = flat.get("openid.claimed_id", "")
    identity = flat.get("openid.identity", "")
    if identity and identity != claimed_id:
        return False, None, "Steam OpenID identity mismatch."

    match = re.match(r"^https?://steamcommunity\.com/openid/id/(\d{17})$", claimed_id)
    if not match:
        return False, None, "SteamID was missing from the OpenID response."
    steam_id64 = match.group(1)

    check = dict(flat)
    check["openid.mode"] = "check_authentication"
    data = urllib.parse.urlencode(check).encode("utf-8")
    req = urllib.request.Request(
        STEAM_OPENID_PROVIDER,
        data=data,
        headers={"Content-Type": "application/x-www-form-urlencoded", "User-Agent": "MMOnsterpatch-TradingPost-OpenID-Test/0.1"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            body = resp.read().decode("utf-8", errors="replace")
    except Exception as ex:
        return False, None, f"Could not validate Steam login with Steam: {ex}"

    if "is_valid:true" not in body:
        return False, None, "Steam rejected the OpenID response."
    return True, steam_id64, None


def sanitize_display_name(name: str, steam_id64: str) -> str:
    """Keep Steam public persona names safe for the tab-delimited socket protocol."""
    name = (name or "").replace("\t", " ").replace("\r", " ").replace("\n", " ").strip()
    name = " ".join(name.split())
    if not name:
        name = f"SteamUser_{steam_id64[-6:]}"
    return name[:64]


def steam_display_fallback(steam_id64: str) -> str:
    return f"SteamUser_{steam_id64[-6:]}"


def is_steam_display_fallback(name: str, steam_id64: str = "") -> bool:
    name = (name or "").strip()
    if not name:
        return True
    if name.startswith("steam_"):
        return True
    if name.startswith("SteamUser_"):
        return True
    if steam_id64 and name == steam_display_fallback(steam_id64):
        return True
    return False


def fetch_steam_display_name(steam_id64: str) -> str:
    """Fetch the user's public Steam persona name. Fails closed to a non-secret fallback."""
    fallback = steam_display_fallback(steam_id64)
    if not steam_id64:
        return fallback
    cached = STEAM_DISPLAY_NAME_CACHE.get(steam_id64)
    if cached:
        return cached
    if not STEAM_DISPLAY_NAME_LOOKUP:
        log("Steam display name lookup is disabled by PBO_STEAM_DISPLAY_NAME_LOOKUP.")
        return fallback
    if not STEAM_WEB_API_KEY:
        log("Steam display name lookup skipped: no Steam Web API key configured. Set STEAM_WEB_API_KEY or PBO_STEAM_WEB_API_KEY.")
        return fallback
    try:
        params = urllib.parse.urlencode({
            "key": STEAM_WEB_API_KEY,
            "steamids": steam_id64,
        })
        url = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?" + params
        req = urllib.request.Request(url, headers={"User-Agent": "MMOnsterpatch-TradingPost-Server/0.3"})
        with urllib.request.urlopen(req, timeout=8) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
        data = json.loads(raw)
        players = data.get("response", {}).get("players", [])
        if not players:
            log(f"Steam display name lookup returned no player for {steam_id64}.")
            return fallback
        resolved = sanitize_display_name(players[0].get("personaname", ""), steam_id64)
        if resolved and not is_steam_display_fallback(resolved, steam_id64):
            STEAM_DISPLAY_NAME_CACHE[steam_id64] = resolved
            log(f"Steam display name resolved for {steam_id64}: {resolved}")
        return resolved
    except Exception as ex:
        log(f"Steam display name lookup failed for {steam_id64}: {ex}")
        return fallback


def get_or_create_steam_account(conn, steam_id64: str, display_name: str = None):
    ensure_account_openid_columns(conn)
    resolved_name = sanitize_display_name(display_name or steam_display_fallback(steam_id64), steam_id64)
    row = conn.execute(
        "SELECT id, username, display_name FROM accounts WHERE steam_id64 = ?",
        (steam_id64,),
    ).fetchone()
    if row:
        account_id, username, existing_display_name = row
        # Do not overwrite a real display name with SteamUser_#### when the Web API lookup fails.
        final_display_name = resolved_name
        if is_steam_display_fallback(resolved_name, steam_id64) and existing_display_name and not is_steam_display_fallback(existing_display_name, steam_id64):
            final_display_name = existing_display_name
        conn.execute(
            "UPDATE accounts SET last_login_at = ?, display_name = ? WHERE id = ?",
            (utcnow_iso(), final_display_name, account_id),
        )
        ensure_mailbox(conn, account_id, final_display_name, username)
        return account_id, username, final_display_name

    # Keep username unique and stable for internal account logic. Display name is separate and can change.
    username = f"steam_{steam_id64}"
    conn.execute(
        """
        INSERT INTO accounts(username, password_salt, password_hash, created_at, last_login_at, auth_provider, steam_id64, display_name)
        VALUES (?, '', 'STEAM_OPENID', ?, ?, 'steam_openid', ?, ?)
        """,
        (username, utcnow_iso(), utcnow_iso(), steam_id64, resolved_name),
    )
    account_id = conn.execute("SELECT id FROM accounts WHERE steam_id64 = ?", (steam_id64,)).fetchone()[0]
    ensure_mailbox(conn, account_id, resolved_name, username)
    return account_id, username, resolved_name


def resolve_account_display_name(conn, account_id, steam_id64, current_display_name, fallback_username):
    """Return the best display name and lazily backfill SteamUser_#### placeholders."""
    current_display_name = (current_display_name or "").strip()
    if steam_id64 and is_steam_display_fallback(current_display_name, steam_id64):
        resolved = fetch_steam_display_name(steam_id64)
        if resolved and not is_steam_display_fallback(resolved, steam_id64):
            conn.execute("UPDATE accounts SET display_name = ? WHERE id = ?", (resolved, account_id))
            return resolved
    if current_display_name:
        return current_display_name
    return fallback_username or (steam_display_fallback(steam_id64) if steam_id64 else "Unknown")


def sanitize_mail_handle_base(name: str) -> str:
    base = (name or "").strip()
    if "#" in base:
        base = base.split("#", 1)[0].strip()
    base = re.sub(r"[^A-Za-z0-9 _.-]", "", base).strip()
    if not base:
        base = "Player"
    return base[:24]


def ensure_account_mail_handle(conn, account_id: int, display_name: str = None, username: str = None) -> str:
    ensure_account_openid_columns(conn)
    row = conn.execute("SELECT username, display_name, mail_tag, mail_handle FROM accounts WHERE id = ?", (account_id,)).fetchone()
    if not row:
        return ""
    account_username, account_display, tag, existing_handle = row
    base = sanitize_mail_handle_base(display_name or account_display or username or account_username or "Player")
    if not tag:
        # Stable Discord-style player handle. Tags only need to make the full handle unique.
        for _ in range(100):
            candidate = f"{secrets.randbelow(10000):04d}"
            handle = f"{base}#{candidate}"
            collision = conn.execute(
                "SELECT id FROM accounts WHERE lower(mail_handle) = lower(?) AND id <> ? LIMIT 1",
                (handle, account_id),
            ).fetchone()
            if not collision:
                tag = candidate
                break
        if not tag:
            tag = f"{int(account_id) % 10000:04d}"
    handle = f"{base}#{tag}"
    if handle != existing_handle or tag != (tag or ""):
        conn.execute("UPDATE accounts SET mail_tag = ?, mail_handle = ? WHERE id = ?", (tag, handle, account_id))
    return handle


def ensure_mailbox(conn, account_id: int, display_name: str = None, username: str = None) -> str:
    handle = ensure_account_mail_handle(conn, account_id, display_name, username)
    now = utcnow_iso()
    conn.execute(
        """
        INSERT INTO mailboxes(account_id, public_handle, created_at, last_seen_at)
        VALUES (?, ?, ?, ?)
        ON CONFLICT(account_id) DO UPDATE SET public_handle = excluded.public_handle, last_seen_at = excluded.last_seen_at
        """,
        (account_id, handle, now, now),
    )
    return handle


def ensure_all_account_mailboxes(conn):
    """Backfill mailbox handles for existing accounts from older builds."""
    try:
        ensure_account_openid_columns(conn)
        rows = conn.execute("SELECT id, username, display_name, mail_handle FROM accounts").fetchall()
        for account_id, username, display_name, handle in rows:
            if not handle:
                ensure_mailbox(conn, int(account_id), display_name, username)
    except Exception:
        raise


def ensure_character_mailbox(conn, account_id: int, character_id: str = "", public_handle: str = "") -> str:
    character_id = (character_id or "").strip()
    public_handle = (public_handle or "").strip()
    if not character_id or not public_handle:
        return ""
    now = utcnow_iso()
    conn.execute(
        """
        INSERT INTO character_mailboxes(character_id, account_id, public_handle, created_at, last_seen_at)
        VALUES (?, ?, ?, ?, ?)
        ON CONFLICT(character_id) DO UPDATE SET account_id = excluded.account_id, public_handle = excluded.public_handle, last_seen_at = excluded.last_seen_at
        """,
        (character_id, int(account_id), public_handle, now, now),
    )
    return public_handle


def _account_steam_id64(conn, account_id: int) -> str:
    try:
        row = conn.execute("SELECT steam_id64 FROM accounts WHERE id = ?", (account_id,)).fetchone()
        return str(row[0] or "").strip() if row else ""
    except Exception:
        return ""


def _account_row_by_steam(conn, steam_id64: str):
    steam_id64 = (steam_id64 or "").strip()
    if not steam_id64:
        return None
    return conn.execute(
        "SELECT id, username, steam_id64, display_name, mail_handle FROM accounts WHERE steam_id64 = ? LIMIT 1",
        (steam_id64,),
    ).fetchone()


def _social_query_user(where_sql: str, params):
    social_db = _resolved_social_db_path()
    # v0.15.1 MariaDBNative: social_db is logical storage, not a file requirement.
    try:
        scon = db.connect_social()
        try:
            return scon.execute(
                f"""
                SELECT u.character_id, u.public_handle, u.display_name, u.account_uuid, COALESCE(a.steam_id64, '') AS steam_id64
                  FROM users u
                  LEFT JOIN accounts a ON a.account_uuid = u.account_uuid
                 WHERE {where_sql}
                 ORDER BY CASE WHEN COALESCE(u.status, 'active') = 'active' THEN 0 ELSE 1 END, u.last_seen_at DESC
                 LIMIT 1
                """,
                params,
            ).fetchone()
        finally:
            scon.close()
    except Exception as ex:
        log(f"Social character lookup warning: {ex}")
        return None


def resolve_character_context_for_account(conn, account_id: int, character_id: str = "", public_handle: str = ""):
    """Resolve/verify the current active character for a Trading Post account.

    Returns (character_id, public_handle, display_name). If the supplied character
    does not belong to the logged-in Steam account, it is ignored.
    """
    steam_id64 = _account_steam_id64(conn, account_id)
    if not steam_id64:
        return "", "", ""
    character_id = (character_id or "").strip()
    public_handle = (public_handle or "").strip()
    row = None
    if character_id:
        row = _social_query_user("u.character_id = ? AND COALESCE(u.status, 'active') = 'active'", (character_id,))
    if not row and public_handle:
        row = _social_query_user("lower(u.public_handle) = lower(?) AND COALESCE(u.status, 'active') = 'active'", (public_handle,))
    if not row:
        row = _social_query_user("COALESCE(a.steam_id64, '') = ? AND COALESCE(u.status, 'active') = 'active'", (steam_id64,))
    if not row:
        return "", "", ""
    cid, handle, display, _acct_uuid, row_steam = row
    if str(row_steam or "").strip() != steam_id64:
        return "", "", ""
    ensure_character_mailbox(conn, account_id, str(cid or ""), str(handle or ""))
    return str(cid or ""), str(handle or ""), str(display or "")


def character_display_for_mail(conn, account_id: int, character_id: str = "", public_handle: str = "") -> str:
    cid, handle, _display = resolve_character_context_for_account(conn, account_id, character_id, public_handle)
    if handle:
        return handle
    return account_display_for_mail(conn, account_id)


def _normalise_mail_lookup(value: str) -> str:
    return (value or "").strip()


def _resolved_social_db_path() -> str:
    path = (SOCIAL_DB_PATH or os.environ.get("SOCIAL_DB") or "social").strip()
    if not os.path.isabs(path):
        path = os.path.join(ROOT, path)
    return path


def find_recipient_account_by_mail_name(conn, recipient_name: str):
    """
    Resolve player mail recipients.

    Character mail is now delivered to the character public handle, such as
    GOOSE#7722, not the Steam account mailbox. The returned tuple is:
    (account_id, username, steam_id64, display_name, mail_handle, character_id, public_handle)
    """
    recipient_name = _normalise_mail_lookup(recipient_name)
    if not recipient_name:
        return None
    ensure_account_openid_columns(conn)

    # Preferred: Social/Official character public handle.
    srow = _social_query_user("lower(u.public_handle) = lower(?) AND COALESCE(u.status, 'active') = 'active'", (recipient_name,))
    if srow:
        character_id, public_handle, character_display_name, _account_uuid, steam_id64 = srow
        account_row = _account_row_by_steam(conn, steam_id64)
        if account_row:
            account_id, username, row_steam, display_name, mail_handle = account_row
            ensure_mailbox(conn, int(account_id), display_name or username, username)
            ensure_character_mailbox(conn, int(account_id), str(character_id or ""), str(public_handle or ""))
            return (int(account_id), username, row_steam, character_display_name or display_name, mail_handle, str(character_id or ""), str(public_handle or ""))

    # Fallback: account mailbox/username/display name for legacy/manual testing.
    row = conn.execute(
        """
        SELECT id, username, steam_id64, display_name, mail_handle
          FROM accounts
         WHERE lower(mail_handle) = lower(?) OR lower(username) = lower(?) OR lower(display_name) = lower(?)
         ORDER BY CASE WHEN lower(mail_handle) = lower(?) THEN 0 ELSE 1 END, id ASC LIMIT 1
        """,
        (recipient_name, recipient_name, recipient_name, recipient_name)
    ).fetchone()
    if row:
        ensure_mailbox(conn, int(row[0]), row[3], row[1])
        return (int(row[0]), row[1], row[2], row[3], row[4], "", row[4] or row[3] or row[1])
    return None


def account_mail_handle(conn, account_id: int) -> str:
    row = conn.execute("SELECT mail_handle, username, display_name FROM accounts WHERE id = ?", (account_id,)).fetchone()
    if not row:
        return ""
    handle, username, display_name = row
    if handle:
        return handle
    return ensure_mailbox(conn, account_id, display_name, username)


def _active_social_character_recipients(conn):
    """Return bank-backed active character recipients for ALL reward mail."""
    rows = []
    social_db = _resolved_social_db_path()
    # v0.15.1 MariaDBNative: social_db is logical storage, not a file requirement.
    try:
        with db.connect_social() as scon:
            srows = scon.execute(
                """
                SELECT u.character_id, u.public_handle, u.display_name, COALESCE(a.steam_id64, '')
                  FROM users u
                  LEFT JOIN accounts a ON a.account_uuid = u.account_uuid
                 WHERE COALESCE(u.status, 'active') = 'active'
                   AND COALESCE(u.public_handle, '') <> ''
                 ORDER BY lower(u.public_handle) ASC
                """
            ).fetchall()
    except Exception as ex:
        log(f"ALL reward recipient scan failed: {ex}")
        return rows
    seen = set()
    for character_id, public_handle, display_name, steam_id64 in srows:
        public_handle = str(public_handle or '').strip()
        if not public_handle or public_handle.lower() == MAILMON_HANDLE.lower():
            continue
        if not steam_id64:
            continue
        account_row = _account_row_by_steam(conn, str(steam_id64 or '').strip())
        if not account_row:
            continue
        account_id, username, row_steam, account_display, mail_handle = account_row
        key = (str(character_id or '').lower(), public_handle.lower())
        if key in seen:
            continue
        seen.add(key)
        ensure_mailbox(conn, int(account_id), account_display or username, username)
        ensure_character_mailbox(conn, int(account_id), str(character_id or ''), public_handle)
        rows.append((int(account_id), username, row_steam, display_name or account_display, mail_handle, str(character_id or ''), public_handle))
    return rows


def _admin_reward_targets(conn, target_identifier: str):
    target = (target_identifier or '').strip()
    if not target:
        return [], 'Missing target.'
    if target.upper() == 'ALL':
        rows = _active_social_character_recipients(conn)
        if not rows:
            return [], 'No active character mailboxes found for ALL.'
        return rows, ''
    row = find_recipient_account_by_mail_name(conn, target)
    if not row:
        return [], 'Recipient not found. Use a full character mailbox handle like Player#1234, or ALL.'
    recip_id, username, _steam_id64, display_name, _mail_handle, recip_character_id, recip_public_handle = row
    if not recip_character_id or not recip_public_handle:
        return [], 'Target must resolve to a character mailbox, not just a Steam/account mailbox.'
    ensure_mailbox(conn, int(recip_id), display_name, username)
    ensure_character_mailbox(conn, int(recip_id), recip_character_id, recip_public_handle)
    return [row], ''


def _admin_reward_payload(reward_type: str, reward_value: str, amount: int = 1, level: int = 1, shiny: bool = False):
    kind = (reward_type or '').strip().upper()
    value = str(reward_value or '').strip()
    try:
        amount = max(1, int(amount))
    except Exception:
        amount = 1
    try:
        level = max(1, min(100, int(level)))
    except Exception:
        level = 1
    if kind == 'SATS':
        if amount <= 0:
            raise ValueError('SATS amount must be greater than 0.')
        return 'SATS', '', amount, f'{amount} SATS'
    if kind == 'ITEM':
        if not value:
            raise ValueError('Missing item name or id.')
        return 'ITEM', encode_message(value), amount, f'{value} x{amount}'
    if kind == 'MON':
        if not value:
            raise ValueError('Missing MoN name or id.')
        payload = json.dumps({'mon_spec': value, 'level': int(level), 'shiny': bool(shiny)}, separators=(',', ':'))
        label = f"{value} Lv.{int(level)}" + (" Shiny" if shiny else "")
        return 'MON_REWARD', encode_message(payload), 0, label
    raise ValueError('Unknown reward type: ' + str(reward_type or ''))


def send_admin_reward_mail(target_identifier: str, reward_type: str, reward_value: str = '', amount: int = 1, level: int = 1, shiny: bool = False):
    """Send MMOMailmon reward mail to one character mailbox or every character mailbox."""
    try:
        attachment_type, blob_b64, attachment_sats, reward_label = _admin_reward_payload(reward_type, reward_value, amount, level, shiny)
    except Exception as ex:
        return False, str(ex)
    with get_db() as conn:
        targets, err = _admin_reward_targets(conn, target_identifier)
        if err:
            return False, err
        now = utcnow_iso()
        sent = 0
        for row in targets:
            recip_id, username, _steam_id64, display_name, _mail_handle, recip_character_id, recip_public_handle = row
            if str(recip_public_handle or '').strip().lower() == MAILMON_HANDLE.lower():
                continue
            add_mail(
                conn,
                int(recip_id),
                None,
                MAILMON_HANDLE,
                ADMIN_REWARD_SUBJECT,
                ADMIN_REWARD_BODY,
                'SYSTEM_REWARD',
                attachment_type,
                blob_b64,
                int(attachment_sats or 0),
                None,
                now,
                recipient_character_id=recip_character_id or '',
                recipient_public_handle=recip_public_handle or '',
                sender_character_id='',
                sender_public_handle=MAILMON_HANDLE,
            )
            sent += 1
        conn.commit()
    if str(target_identifier or '').strip().upper() == 'ALL':
        return True, f"MMOMailmon sent {reward_label} reward mail to {sent} character mailbox(es)."
    return True, f"MMOMailmon sent {reward_label} reward mail to {targets[0][6] if targets else target_identifier}."



def revoke_aio_sessions_for_steam(steam_id64: str) -> int:
    steam_id64 = moderation.normalize_steam_id64(steam_id64)
    if not steam_id64:
        return 0
    try:
        conn = get_db()
        try:
            cur = conn.execute(
                """
                UPDATE aio_sessions
                SET revoked = 1
                WHERE steam_id64 = ? OR account_id IN (SELECT id FROM accounts WHERE steam_id64 = ?)
                """,
                (steam_id64, steam_id64),
            )
            return int(cur.rowcount or 0)
        finally:
            conn.close()
    except Exception as ex:
        log(f"Could not revoke AIO sessions for banned SteamID {steam_id64}: {ex}")
        return 0


def disconnect_active_steam_sessions(steam_id64: str, reason: str = "This Steam account is banned from MMOnsterpatch online services.") -> int:
    steam_id64 = moderation.normalize_steam_id64(steam_id64)
    if not steam_id64:
        return 0
    with ACTIVE_BANK_HANDLERS_LOCK:
        targets = [h for h in list(ACTIVE_BANK_HANDLERS) if getattr(h, "steam_id64", "") == steam_id64]
    disconnected = 0
    for h in targets:
        try:
            h.send_error(reason)
        except Exception:
            pass
        try:
            h.request.shutdown(2)
        except Exception:
            pass
        try:
            h.request.close()
        except Exception:
            pass
        disconnected += 1
    return disconnected

def create_aio_session(conn, account_id, steam_id64="", display_name="", source_ip=""):
    if moderation.get_active_ban(steam_id64=steam_id64):
        raise RuntimeError("This Steam account is banned from MMOnsterpatch online services.")
    token = secrets.token_urlsafe(36)
    now = datetime.now()
    created = now.isoformat()
    # Official Server cached sessions are reusable across game restarts for up to 12 hours,
    # but only from the same public IP that created the session.  The client may store a
    # base64-obfuscated copy of the token; this server-side row is the authority.
    expires = (now + timedelta(hours=float(AIO_SESSION_HOURS))).isoformat()
    conn.execute(
        """
        INSERT INTO aio_sessions(token, account_id, steam_id64, display_name, created_at, last_seen_at, expires_at, revoked, source_ip)
        VALUES (?, ?, ?, ?, ?, ?, ?, 0, ?)
        """,
        (token, int(account_id), steam_id64 or "", display_name or "", created, created, expires, source_ip or ""),
    )
    return token, expires


def _launcher_accounts_dir():
    # v0.15.1: launcher/account sessions live in MariaDB auth, not Account Website JSON/auth.db files.
    try:
        official_data = os.path.dirname(os.path.abspath(DB_PATH))
        official_server = os.path.dirname(official_data)
        repack_root = os.path.dirname(official_server)
        return os.path.join(repack_root, "Data", "Accounts")
    except Exception:
        return os.path.join(os.getcwd(), "Data", "Accounts")


def _load_launcher_accounts():
    accounts_dir = _launcher_accounts_dir()
    auth_db_path = "auth"
    # v0.15.0 MariaDB cutover: always try the MariaDB auth database first.
    # Native MariaDB maps this logical auth store to mmonsterpatch_auth.
    if True:
        try:
            out_accounts = []
            out_sessions = []
            with db.connect_auth(timeout=5.0) as con:
                # MariaDB native rows support index/name lookup; row_factory ignored
                for a in con.execute("""
                    SELECT account_id,email,username,display_name,password_salt,password_hash,pseudo_steam_id,
                           steam_verified,linked_steam_id64,created_utc,updated_utc,last_login_utc
                    FROM accounts
                """):
                    out_accounts.append({
                        "AccountId": a["account_id"],
                        "Email": a["email"],
                        "Username": a["username"],
                        "DisplayName": a["display_name"],
                        "PasswordSalt": a["password_salt"],
                        "PasswordHash": a["password_hash"],
                        "PseudoSteamId": a["pseudo_steam_id"],
                        "SteamVerified": bool(a["steam_verified"]),
                        "LinkedSteamId64": a["linked_steam_id64"],
                        "CreatedUtc": a["created_utc"],
                        "UpdatedUtc": a["updated_utc"],
                        "LastLoginUtc": a["last_login_utc"],
                    })
                for s in con.execute("""
                    SELECT session_token,aio_session_token,account_id,username,email,display_name,pseudo_steam_id,
                           source_ip,created_utc,last_seen_utc,expires_utc,revoked_utc
                    FROM launcher_sessions
                    WHERE revoked_utc IS NULL
                """):
                    out_sessions.append({
                        "SessionToken": s["session_token"],
                        "AioSessionToken": s["aio_session_token"],
                        "AccountId": s["account_id"],
                        "Username": s["username"],
                        "Email": s["email"],
                        "DisplayName": s["display_name"],
                        "PseudoSteamId": s["pseudo_steam_id"],
                        "SourceIp": s["source_ip"],
                        "CreatedUtc": s["created_utc"],
                        "LastSeenUtc": s["last_seen_utc"],
                        "ExpiresUtc": s["expires_utc"],
                        "RevokedUtc": s["revoked_utc"],
                    })
            return out_accounts, out_sessions
        except Exception as ex:
            log(f"Launcher MariaDB auth session load failed; JSON fallback is disabled in official MariaDB mode: {ex}")

    # Official MariaDB mode does not read the old JSON mirrors.
    return [], []


def _parse_iso_datetime(value):
    try:
        v = str(value or "").replace("Z", "+00:00")
        return datetime.fromisoformat(v).replace(tzinfo=None)
    except Exception:
        return None


def _ensure_launcher_bank_account_row(conn, account):
    username = str(account.get("username") or account.get("Username") or "").strip() or "launcher_account"
    display = str(account.get("displayName") or account.get("DisplayName") or username).strip() or username
    pseudo = str(account.get("pseudoSteamId") or account.get("PseudoSteamId") or account.get("accountId") or account.get("AccountId") or "").strip()
    if not pseudo:
        pseudo = "mmpacct_" + secrets.token_hex(8)
    safe_username = "mmp_" + re.sub(r"[^A-Za-z0-9_.-]", "", username).lower()
    if not safe_username or safe_username == "mmp_":
        safe_username = "mmp_" + pseudo.lower()[-12:]
    now = utcnow_iso()

    row = conn.execute("SELECT id FROM accounts WHERE steam_id64=?", (pseudo,)).fetchone()
    if row:
        account_id = int(row[0])
        try:
            conn.execute("UPDATE accounts SET display_name=?, last_login_at=? WHERE id=?", (display, now, account_id))
        except Exception:
            pass
        return account_id, safe_username, pseudo, display

    # Try username; if it is already taken, suffix with part of the pseudo ID.
    final_username = safe_username
    if conn.execute("SELECT 1 FROM accounts WHERE username=?", (final_username,)).fetchone():
        final_username = f"{safe_username}_{pseudo[-6:]}"

    cols = {row[1] for row in conn.execute("PRAGMA table_info(accounts)").fetchall()}
    base_cols = ["username", "password_salt", "password_hash", "created_at", "last_login_at"]
    vals = [final_username, "launcher", "launcher", now, now]
    optional = []
    if "auth_provider" in cols: optional.append(("auth_provider", "launcher"))
    if "steam_id64" in cols: optional.append(("steam_id64", pseudo))
    if "display_name" in cols: optional.append(("display_name", display))
    if "mail_tag" in cols: optional.append(("mail_tag", ""))
    if "mail_handle" in cols: optional.append(("mail_handle", ""))
    all_cols = base_cols + [c for c, _ in optional]
    all_vals = vals + [v for _, v in optional]
    q = ",".join(["?"] * len(all_cols))
    conn.execute(f"INSERT INTO accounts({','.join(all_cols)}) VALUES({q})", all_vals)
    account_id = int(conn.execute("SELECT last_insert_rowid()").fetchone()[0])
    return account_id, final_username, pseudo, display


def _resolve_launcher_json_session(conn, token, source_ip=None):
    accounts, sessions = _load_launcher_accounts()

    # v0.12.11.4 test-flow account bridge:
    # If the client sends the stable website account id directly, accept it as the
    # account identity for now.  This intentionally removes old AIO/Steam session
    # token validation from the critical title-screen -> online save selector path.
    # Steam re-verification can be layered back in after the flow is proven.
    if str(token or "").startswith("acct_"):
        account = None
        for a in accounts:
            if str(a.get("accountId") or a.get("AccountId") or "").strip() == str(token).strip():
                account = a
                break
        if account is None:
            account = {
                "AccountId": str(token).strip(),
                "Username": str(token).strip(),
                "DisplayName": str(token).strip(),
                "PseudoSteamId": "mmpacct_" + re.sub(r"[^A-Za-z0-9]", "", str(token).replace("acct_", ""))[:32],
            }
        return _ensure_launcher_bank_account_row(conn, account)

    if not sessions:
        return None
    now = datetime.now()
    for s in sessions:
        stoken = str(s.get("aioSessionToken") or s.get("AioSessionToken") or "").strip()
        ltoken = str(s.get("sessionToken") or s.get("SessionToken") or "").strip()
        if token not in (stoken, ltoken):
            continue
        if s.get("revokedUtc") or s.get("RevokedUtc"):
            return None
        # Test-flow auth loosening: do not reject launcher-created sessions due to
        # clock/expiry/IP mismatches while we are proving the account -> launcher ->
        # title Log In -> online save selector flow.
        account_id_str = str(s.get("accountId") or s.get("AccountId") or "").strip()
        account = None
        for a in accounts:
            if str(a.get("accountId") or a.get("AccountId") or "").strip() == account_id_str:
                account = a
                break
        if account is None:
            return None
        bank_id, username, pseudo, display = _ensure_launcher_bank_account_row(conn, account)
        return (int(bank_id), username, pseudo, display)
    return None


def get_aio_session_expires_at(conn, token):
    token = (token or "").strip()
    if not token:
        return ""
    row = conn.execute("SELECT expires_at FROM aio_sessions WHERE token=?", (token,)).fetchone()
    return str(row[0] or "") if row else ""


def resolve_aio_session(conn, token, source_ip=None):
    token = (token or "").strip()
    if not token:
        return None
    if token.startswith("acct_"):
        return _resolve_launcher_json_session(conn, token, source_ip)
    row = conn.execute(
        """
        SELECT s.account_id, COALESCE(a.username, ''), COALESCE(a.steam_id64, s.steam_id64, ''),
               COALESCE(a.display_name, s.display_name, a.username, ''), COALESCE(s.source_ip, '')
        FROM aio_sessions s
        JOIN accounts a ON a.id = s.account_id
        WHERE s.token = ? AND s.revoked = 0 AND s.expires_at > ?
        """,
        (token, datetime.now().isoformat()),
    ).fetchone()
    if not row:
        return _resolve_launcher_json_session(conn, token, source_ip)
    if AIO_SESSION_REQUIRE_SAME_IP and source_ip is not None:
        expected_ip = str(row[4] or "")
        current_ip = str(source_ip or "")
        # Old rows without a source_ip were created before this policy existed. Force re-auth instead of trusting them.
        if not expected_ip or expected_ip != current_ip:
            conn.execute("UPDATE aio_sessions SET revoked=1, last_seen_at=? WHERE token=?", (utcnow_iso(), token))
            return None
    # Do not slide the expiry. Re-auth is required after the original 12-hour window.
    conn.execute("UPDATE aio_sessions SET last_seen_at = ? WHERE token = ?", (utcnow_iso(), token))
    return row[:4]


def revoke_aio_session(conn, token):
    token = (token or "").strip()
    if token:
        conn.execute("UPDATE aio_sessions SET revoked = 1, last_seen_at = ? WHERE token = ?", (utcnow_iso(), token))


class SteamOpenIDHttpHandler(BaseHTTPRequestHandler):
    server_version = "MonsterpatchSteamOpenIDTest/0.1"

    def log_message(self, fmt, *args):
        log("OpenID HTTP: " + fmt % args)

    def _html(self, status, title, body):
        page = f"""<!doctype html>
<html><head><meta charset=\"utf-8\"><title>{html.escape(title)}</title>
<style>body{{font-family:system-ui,Segoe UI,Arial,sans-serif;max-width:720px;margin:40px auto;padding:0 18px;line-height:1.45}}code{{background:#eee;padding:2px 4px;border-radius:4px}}</style></head>
<body><h1>{html.escape(title)}</h1>{body}</body></html>"""
        raw = page.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        qs = urllib.parse.parse_qs(parsed.query)
        if parsed.path == "/":
            self._html(200, "MMOnsterpatch Steam OpenID Callback Server", f"<p>HTTP callback server is running.</p><p>Steam/OpenID HTTP: <code>{OPENID_HTTP_HOST}:{OPENID_HTTP_PORT}</code></p><p>Trading Post socket: <code>{HOST}:{PORT}</code></p><p>Public base URL: <code>{html.escape(OPENID_PUBLIC_BASE_URL)}</code></p>")
            return
        if parsed.path != "/steam/return":
            self._html(404, "Not found", "<p>Unknown path.</p>")
            return

        state = qs.get("state", [""])[0]
        cleanup_pending_openid()
        with PENDING_OPENID_LOCK:
            pending = PENDING_OPENID.get(state)
        if not state or not pending:
            self._html(400, "Steam authentication failed", "<p>This login request expired or does not exist. Please return to the game and try again.</p>")
            return

        ok, steam_id64, error = validate_steam_openid_response(qs)
        if not ok:
            with PENDING_OPENID_LOCK:
                if state in PENDING_OPENID:
                    PENDING_OPENID[state]["status"] = "error"
                    PENDING_OPENID[state]["error"] = error or "Steam authentication failed."
            self._html(400, "Steam authentication failed", f"<p>{html.escape(error or 'Steam authentication failed.')}</p><p>Please return to the game and try again.</p>")
            return

        active_ban = moderation.get_active_ban(steam_id64=steam_id64)
        if active_ban:
            reason = active_ban.get("reason") or "This Steam account is banned from MMOnsterpatch online services."
            with PENDING_OPENID_LOCK:
                if state in PENDING_OPENID:
                    PENDING_OPENID[state]["status"] = "error"
                    PENDING_OPENID[state]["error"] = "This Steam account is banned from MMOnsterpatch online services. Reason: " + str(reason)
            log(f"Steam OpenID blocked banned SteamID: {steam_id64} reason={reason}")
            self._html(403, "Steam authentication blocked", "<p>This Steam account is banned from MMOnsterpatch online services.</p>")
            return

        try:
            conn = get_db()
            display_name = fetch_steam_display_name(steam_id64)
            account_id, username, display_name = get_or_create_steam_account(conn, steam_id64, display_name)
            conn.close()
        except Exception as ex:
            with PENDING_OPENID_LOCK:
                if state in PENDING_OPENID:
                    PENDING_OPENID[state]["status"] = "error"
                    PENDING_OPENID[state]["error"] = f"Server account error: {ex}"
            self._html(500, "Steam authentication failed", "<p>The server could not create/load your Trading Post account. Please try again later.</p>")
            return

        with PENDING_OPENID_LOCK:
            if state in PENDING_OPENID:
                PENDING_OPENID[state].update({
                    "status": "ok",
                    "steam_id64": steam_id64,
                    "account_id": account_id,
                    "username": username,
                    "display_name": display_name,
                    "completed": time.time(),
                })
        log(f"Steam OpenID success: steam_id64={steam_id64} username={username} display_name={display_name}")
        self._html(200, "Steam authentication successful", "<p>You can return to Monsterpatch now.</p><p>No Steam password was sent to or stored by this server.</p>")


def start_openid_http_server():
    server = ThreadingHTTPServer((OPENID_HTTP_HOST, OPENID_HTTP_PORT), SteamOpenIDHttpHandler)
    t = threading.Thread(target=server.serve_forever, name="SteamOpenIDHttpServer", daemon=True)
    t.start()
    log(f"Steam OpenID HTTP callback listening on {OPENID_HTTP_HOST}:{OPENID_HTTP_PORT}; public base URL: {OPENID_PUBLIC_BASE_URL}")
    return server


class BankHandler(socketserver.StreamRequestHandler):
    def setup(self):
        super().setup()
        self.conn = get_db()
        self.account_id = None
        self.username = None
        self.steam_id64 = ""
        self.character_id = ""
        self.public_handle = ""
        with ACTIVE_BANK_HANDLERS_LOCK:
            ACTIVE_BANK_HANDLERS.add(self)

    def finish(self):
        with ACTIVE_BANK_HANDLERS_LOCK:
            ACTIVE_BANK_HANDLERS.discard(self)
        try:
            self.conn.close()
        except Exception:
            pass
        super().finish()

    def send_line(self, *parts):
        safe = [str(p).replace("\t", " ").replace("\r", " ").replace("\n", " ") for p in parts]
        self.wfile.write(("\t".join(safe) + "\n").encode("utf-8"))
        self.wfile.flush()

    def send_error(self, message: str):
        self.send_line("ERR", encode_message(message))

    def handle(self):
        log(f"Connection from {self.client_address[0]}:{self.client_address[1]}")
        try:
            while True:
                raw = self.rfile.readline()
                if not raw:
                    break
                line = raw.decode("utf-8", errors="ignore").strip("\r\n")
                if not line:
                    continue
                parts = line.split("\t")
                cmd = parts[0].upper()
                if cmd == "REGISTER":
                    self.cmd_register(parts)
                elif cmd == "LOGIN":
                    self.cmd_login(parts)
                elif cmd == "STEAM_OPENID_BEGIN":
                    self.cmd_steam_openid_begin(parts)
                elif cmd == "STEAM_OPENID_POLL":
                    self.cmd_steam_openid_poll(parts)
                elif cmd == "SESSION_LOGIN":
                    self.cmd_session_login(parts)
                elif cmd == "SESSION_LOGOUT":
                    self.cmd_session_logout(parts)
                elif cmd == "CHARACTER_CONTEXT":
                    self.cmd_character_context(parts)
                elif cmd == "PING":
                    self.send_line("OK", "PING")
                elif cmd == "FETCH":
                    self.cmd_fetch()
                elif cmd == "SAVE":
                    self.cmd_save(parts)
                elif cmd in ("GTS_CREATE", "TRADING_POST_CREATE"):
                    self.cmd_gts_create(parts)
                elif cmd in ("GTS_CREATE2", "TRADING_POST_CREATE2"):
                    self.cmd_gts_create2(parts)
                elif cmd in ("GTS_CREATE3", "TRADING_POST_CREATE3"):
                    self.cmd_gts_create3(parts)
                elif cmd in ("GTS_SEARCH_PAGE", "TRADING_POST_SEARCH_PAGE"):
                    self.cmd_gts_search_page(parts)
                elif cmd in ("GTS_SEARCH_FILTER_PAGE", "TRADING_POST_SEARCH_FILTER_PAGE"):
                    self.cmd_gts_search_filter_page(parts, mine=False)
                elif cmd in ("GTS_MY_LISTINGS_PAGE", "TRADING_POST_MY_LISTINGS_PAGE"):
                    self.cmd_gts_my_listings_page(parts)
                elif cmd in ("GTS_MY_LISTINGS_FILTER_PAGE", "TRADING_POST_MY_LISTINGS_FILTER_PAGE"):
                    self.cmd_gts_search_filter_page(parts, mine=True)
                elif cmd in ("GTS_OFFER", "TRADING_POST_OFFER"):
                    self.cmd_gts_offer(parts)
                elif cmd in ("GTS_BUY_SATS", "TRADING_POST_BUY_SATS"):
                    self.cmd_gts_buy_sats(parts)
                elif cmd in ("GTS_CANCEL", "TRADING_POST_CANCEL"):
                    self.cmd_gts_cancel(parts)
                elif cmd in ("GTS_CLAIM", "TRADING_POST_CLAIM"):
                    self.cmd_gts_claim()
                elif cmd in ("GTS_NOTIFY_ACCEPTED", "TRADING_POST_NOTIFY_ACCEPTED"):
                    self.cmd_gts_notify_accepted()
                elif cmd == "MAIL_COUNT":
                    self.cmd_mail_count()
                elif cmd == "MAIL_LIST":
                    self.cmd_mail_list(parts)
                elif cmd == "MAIL_VIEW":
                    self.cmd_mail_view(parts)
                elif cmd == "MAIL_DELETE":
                    self.cmd_mail_delete(parts)
                elif cmd == "MAIL_SAVE":
                    self.cmd_mail_save(parts)
                elif cmd == "MAIL_SEND":
                    self.cmd_mail_send(parts)
                elif cmd == "MAIL_CLAIM":
                    self.cmd_mail_claim(parts)
                elif cmd in ("RA_POKEMON_CREATE", "MARKET_MON_CREATE", "TRADING_POST_MON_MARKET_CREATE"):
                    self.cmd_ra_pokemon_create(parts)
                elif cmd in ("RA_POKEMON_SEARCH_PAGE", "MARKET_MON_SEARCH_PAGE", "TRADING_POST_MON_MARKET_SEARCH_PAGE"):
                    self.cmd_ra_pokemon_search_page(parts)
                elif cmd in ("RA_POKEMON_MY_PAGE", "MARKET_MON_MY_PAGE", "TRADING_POST_MON_MARKET_MY_PAGE"):
                    self.cmd_ra_pokemon_my_page(parts)
                elif cmd in ("RA_POKEMON_BUY", "MARKET_MON_BUY", "TRADING_POST_MON_MARKET_BUY"):
                    self.cmd_ra_pokemon_buy(parts)
                elif cmd in ("RA_ITEM_CREATE", "MARKET_ITEM_CREATE", "TRADING_POST_ITEM_MARKET_CREATE"):
                    self.cmd_ra_item_create(parts)
                elif cmd in ("RA_ITEM_SEARCH_PAGE", "MARKET_ITEM_SEARCH_PAGE", "TRADING_POST_ITEM_MARKET_SEARCH_PAGE"):
                    self.cmd_ra_item_search_page(parts)
                elif cmd in ("RA_ITEM_MY_PAGE", "MARKET_ITEM_MY_PAGE", "TRADING_POST_ITEM_MARKET_MY_PAGE"):
                    self.cmd_ra_item_my_page(parts)
                elif cmd in ("RA_ITEM_BUY", "MARKET_ITEM_BUY", "TRADING_POST_ITEM_MARKET_BUY"):
                    self.cmd_ra_item_buy(parts)
                elif cmd in ("RA_CLAIM_PAYOUTS", "MARKET_CLAIM_PAYOUTS", "TRADING_POST_MARKET_CLAIM_PAYOUTS"):
                    self.cmd_ra_claim_payouts()
                elif cmd in ("RA_CANCEL", "MARKET_CANCEL", "TRADING_POST_MARKET_CANCEL"):
                    self.cmd_ra_cancel(parts)
                else:
                    self.send_error("Unknown command.")
        except (ConnectionResetError, BrokenPipeError, OSError):
            pass
        except Exception as ex:
            try:
                user = self.username or "anonymous"
                cmd_name = ""
                try:
                    cmd_name = cmd
                except Exception:
                    cmd_name = "UNKNOWN"
                log(f"Trading Post command failed: cmd={cmd_name} user={user} account_id={self.account_id} character_id={self.character_id or ''} error={ex}")
                log(traceback.format_exc().rstrip())
                self.send_error("Trading Post server error. Please try again; details were written to the server log.")
            except Exception:
                pass
        finally:
            if self.username:
                log(f"{self.username} disconnected")
            else:
                log(f"Disconnected: {self.client_address[0]}:{self.client_address[1]}")

    def require_login(self):
        if self.account_id is None:
            self.send_error("You are not logged in.")
            return False
        return True

    def parse_int(self, raw, default=None):
        try:
            return int(raw)
        except (TypeError, ValueError):
            return default


    def cmd_steam_openid_begin(self, parts):
        # Steam/OpenID is intentionally disabled in v0.12.11 while launcher account login is stabilized.
        # The game client should use SESSION_LOGIN with the launcher-issued AIO session token.
        self.send_error("Steam authentication is disabled. Log into the MMOnsterpatch Launcher and start Online Mode.")

    def cmd_steam_openid_poll(self, parts):
        # Steam/OpenID is intentionally disabled in v0.12.11 while launcher account login is stabilized.
        self.send_error("Steam authentication is disabled. Log into the MMOnsterpatch Launcher and start Online Mode.")

    def cmd_session_login(self, parts):
        if len(parts) < 2:
            self.send_error("Invalid session login request.")
            return
        token = parts[1].strip()
        row = resolve_aio_session(self.conn, token, self.client_address[0])
        if not row:
            self.send_error("Stored launcher account session expired or invalid. Please log into the MMOnsterpatch Launcher again.")
            return
        account_id, username, steam_id64, display_name = row
        active_ban = moderation.get_active_ban(steam_id64=steam_id64)
        if active_ban:
            reason = active_ban.get("reason") or "This Steam account is banned from MMOnsterpatch online services."
            revoke_aio_session(self.conn, token)
            self.send_error("This Steam account is banned from MMOnsterpatch online services. Reason: " + str(reason))
            log(f"AIO session login blocked banned SteamID: username={username} steam_id64={steam_id64} reason={reason}")
            return
        self.account_id = int(account_id)
        self.username = username or display_name or "MMOnsterpatch user"
        self.steam_id64 = steam_id64 or ""
        self.conn.execute("UPDATE accounts SET last_login_at = ? WHERE id = ?", (utcnow_iso(), self.account_id))
        ensure_mailbox(self.conn, self.account_id, display_name or self.username, self.username)
        log(f"AIO session login success: username={self.username} account_key={steam_id64}")
        session_expires = get_aio_session_expires_at(self.conn, token)
        self.send_line("OK", "LOGIN", encode_message(self.username), steam_id64 or "", encode_message(display_name or self.username), token, session_expires)

    def cmd_session_logout(self, parts):
        # Trading Post uses the launcher/AIO session token as a shared account token.
        # Revoking it from the Trading Post socket also invalidates Official Save uploads
        # and can make later manual/quit/logout saves fail after the market window is used.
        # Treat SESSION_LOGOUT here as a Trading Post socket/account-context detach only;
        # full launcher/account logout belongs to the launcher/account service path.
        self.account_id = None
        self.username = None
        self.steam_id64 = ""
        self.character_id = ""
        self.public_handle = ""
        self.send_line("OK", "SESSION_LOGOUT")

    def cmd_character_context(self, parts):
        if not self.require_login():
            return
        character_id = decode_message(parts[1]).strip() if len(parts) > 1 else ""
        public_handle = decode_message(parts[2]).strip() if len(parts) > 2 else ""
        cid, handle, _display = resolve_character_context_for_account(self.conn, self.account_id, character_id, public_handle)
        if cid and handle:
            self.character_id = cid
            self.public_handle = handle
            self.send_line("OK", "CHARACTER_CONTEXT", encode_message(handle), encode_message(cid))
            return
        self.send_error("Character mailbox could not be verified yet.")

    def ensure_character_context(self):
        if self.account_id is None:
            return "", ""
        if self.character_id and self.public_handle:
            return self.character_id, self.public_handle
        cid, handle, _display = resolve_character_context_for_account(self.conn, self.account_id, self.character_id, self.public_handle)
        if cid and handle:
            self.character_id = cid
            self.public_handle = handle
        return self.character_id or "", self.public_handle or ""

    def sender_mail_handle(self):
        _cid, handle = self.ensure_character_context()
        if handle:
            return handle
        return account_display_for_mail(self.conn, self.account_id)

    def mail_recipient_where(self):
        cid, _handle = self.ensure_character_context()
        if cid:
            return "recipient_account_id = ? AND COALESCE(recipient_character_id, '') = ?", [self.account_id, cid]
        return "recipient_account_id = ? AND COALESCE(recipient_character_id, '') = ''", [self.account_id]

    def is_own_listing_row(self, owner_account_id, owner_character_id):
        cid, _handle = self.ensure_character_context()
        owner_character_id = (owner_character_id or "").strip()
        if cid and owner_character_id:
            return owner_character_id.lower() == cid.lower()
        return int(owner_account_id) == int(self.account_id)

    def cmd_register(self, parts):
        if len(parts) < 3:
            self.send_error("Invalid registration request.")
            return
        username = decode_message(parts[1]).strip()
        password = decode_message(parts[2])
        if not username or not password:
            self.send_error("Username and password are required.")
            return
        salt = secrets.token_hex(16)
        pw_hash = hash_password(password, salt)
        try:
            self.conn.execute(
                "INSERT INTO accounts(username, password_salt, password_hash, created_at) VALUES (?, ?, ?, ?)",
                (username, salt, pw_hash, utcnow_iso())
            )
            log(f"Registered account: {username}")
            self.send_line("OK", "REGISTER")
        except db.IntegrityError:
            self.send_error("That username is already in use.")

    def cmd_login(self, parts):
        if len(parts) < 3:
            self.send_error("Invalid login request.")
            return
        username = decode_message(parts[1]).strip()
        password = decode_message(parts[2])
        row = self.conn.execute(
            "SELECT id, password_salt, password_hash FROM accounts WHERE username = ?",
            (username,)
        ).fetchone()
        if not row:
            self.send_error("Invalid username or password.")
            return
        account_id, salt, pw_hash = row
        if hash_password(password, salt) != pw_hash:
            self.send_error("Invalid username or password.")
            return
        self.account_id = account_id
        self.username = username
        self.conn.execute("UPDATE accounts SET last_login_at = ? WHERE id = ?", (utcnow_iso(), account_id))
        ensure_mailbox(self.conn, self.account_id, username, username)
        log(f"Login success: {username}")
        self.send_line("OK", "LOGIN")

    def cmd_fetch(self):
        if not self.require_login():
            return
        rows = self.conn.execute(
            "SELECT slot_index, species, level, name_b64, gender, shiny, blob_b64 FROM bank_slots WHERE account_id = ? ORDER BY slot_index ASC",
            (self.account_id,)
        ).fetchall()
        self.send_line("OK", "FETCH", SLOT_COUNT)
        for row in rows:
            self.send_line("SLOT", row[0], row[1], row[2], row[3], row[4], row[5], row[6])
        self.send_line("END")

    def cmd_save(self, parts):
        if not self.require_login():
            return
        expected_count = SLOT_COUNT
        if len(parts) >= 2:
            try:
                expected_count = int(parts[1])
            except ValueError:
                expected_count = SLOT_COUNT
        if expected_count != SLOT_COUNT:
            self.send_error("Unexpected slot count.")
            return
        records = []
        while True:
            raw = self.rfile.readline()
            if not raw:
                self.send_error("Connection closed during save.")
                return
            line = raw.decode("utf-8", errors="ignore").strip("\r\n")
            if line == "END":
                break
            subparts = line.split("\t")
            if subparts[0] != "SLOT" or len(subparts) < 8:
                self.send_error("Malformed save data.")
                return
            slot_index = self.parse_int(subparts[1], -1)
            if slot_index < 0 or slot_index >= SLOT_COUNT:
                self.send_error("Invalid slot index.")
                return
            records.append((slot_index, subparts[2], int(subparts[3]), subparts[4], int(subparts[5]), int(subparts[6]), subparts[7]))
        # MariaDB persistent handler connection: do not use `with self.conn` here.
        # The wrapper context manager closes the socket-session DB connection on exit;
        # follow-up commands like My Listings then fail with PyMySQL InterfaceError(0, "").
        self.conn.execute("DELETE FROM bank_slots WHERE account_id = ?", (self.account_id,))
        for rec in records:
            self.conn.execute(
                "INSERT INTO bank_slots(account_id, slot_index, species, level, name_b64, gender, shiny, blob_b64) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                (self.account_id, rec[0], rec[1], rec[2], rec[3], rec[4], rec[5], rec[6])
            )
        log(f"Saved bank for {self.username} ({len(records)} occupied slots)")
        self.send_line("OK", "SAVE")

    def cmd_gts_create(self, parts):
        # Legacy MoN-for-MoN create command retained for older clients.
        if len(parts) < 8:
            self.send_error("Invalid GTS listing request.")
            return
        legacy = ["GTS_CREATE2", "MON", parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], parts[7]]
        self.cmd_gts_create2(legacy)

    def cmd_gts_create2(self, parts):
        # v0.9.8-v0.10.1 clients: MoN offer with requested MoN/SATS.
        if len(parts) < 9:
            self.send_error("Invalid Trading Post listing request.")
            return
        legacy = ["GTS_CREATE3", "MON", "0", parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], parts[7], parts[8]]
        self.cmd_gts_create3(legacy)

    def cmd_gts_create3(self, parts):
        if not self.require_login():
            return
        if len(parts) < 11:
            self.send_error("Invalid Trading Post listing request.")
            return
        offer_type = (parts[1] or "MON").strip().upper()
        offered_sats = self.parse_int(parts[2], 0)
        request_type = (parts[3] or "MON").strip().upper()
        request_value = parts[4].strip()
        offered_species = parts[5].strip()
        level = self.parse_int(parts[6], -1)
        name_b64 = parts[7]
        gender = self.parse_int(parts[8], 0)
        shiny = self.parse_int(parts[9], 0)
        blob_b64 = parts[10]
        request_species = request_value
        request_sats = 0

        if offer_type not in ("MON", "SATS"):
            self.send_error("Offered type must be MoN or SATS.")
            return
        if request_type not in ("MON", "SATS"):
            self.send_error("Requested type must be MoN or SATS.")
            return
        if offer_type == "SATS" and request_type == "SATS":
            self.send_error("SATS-for-SATS posts are not allowed.")
            return

        if request_type == "SATS":
            request_sats = self.parse_int(request_value, 0)
            request_species = "SATS"
            if request_sats <= 0 or request_sats > RA_MAX_PRICE:
                self.send_error("Invalid SATS price.")
                return
        elif not request_species:
            self.send_error("Choose a requested MoN first.")
            return

        if offer_type == "SATS":
            if offered_sats <= 0 or offered_sats > RA_MAX_PRICE:
                self.send_error("Invalid offered SATS amount.")
                return
            offered_species = "SATS"
            level = 0
            name_b64 = encode_message("SATS")
            gender = 0
            shiny = 0
            blob_b64 = ""
        else:
            offered_sats = 0
            if not offered_species or level < 1 or not blob_b64:
                self.send_error("Invalid Trading Post listing data.")
                return

        self.ensure_character_context()
        owner_character_id = self.character_id or ""
        owner_public_handle = self.public_handle or self.sender_mail_handle()
        now = utcnow_iso()
        expires_at = add_seconds_to_iso(now, GTS_LISTING_DURATION_SECONDS)
        # MariaDB persistent handler connection: do not use `with self.conn` here.
        # The wrapper context manager closes the socket-session DB connection on exit;
        # follow-up commands like My Listings then fail with PyMySQL InterfaceError(0, "").
        cur = self.conn.execute(
            """
            INSERT INTO gts_listings(
                owner_account_id, owner_username, owner_character_id, owner_public_handle,
                request_species, request_type, request_sats,
                offer_type, offered_sats, offered_species, level, name_b64, gender, shiny, blob_b64,
                status, created_at, updated_at, expires_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'open', ?, ?, ?)
            """,
            (self.account_id, self.username, owner_character_id, owner_public_handle,
             request_species, request_type, request_sats,
             offer_type, offered_sats, offered_species, level, name_b64, gender, shiny, blob_b64,
             now, now, expires_at)
        )
        listing_id = cur.lastrowid
        request_desc = request_species if request_type == 'MON' else str(request_sats) + ' SATS'
        offer_desc = str(offered_sats) + ' SATS' if offer_type == 'SATS' else f"{offered_species} Lv.{level}"
        log(f"GTS create: user={owner_public_handle or self.username} listing_id={listing_id} offer_type={offer_type} offer={offer_desc} request_type={request_type} request={request_desc}")
        self.send_line("OK", "GTS_CREATE", listing_id)

    def _send_listing_page(self, response_tag: str, page_index: int, total_count: int, rows):
        page_count = max(1, math.ceil(total_count / GTS_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        self.send_line("OK", response_tag, page_index, page_count, total_count, GTS_PAGE_SIZE)
        for row in rows:
            # Newer SELECTs include created_at, owner_account_id, steam_id64,
            # display_name, owner_username after the client-facing listing fields.
            owner_name = row[1]
            created_at = ""
            if len(row) >= 10:
                created_at = row[9] or ""
            if len(row) >= 21 and row[19]:
                owner_name = row[19]
            elif len(row) >= 14:
                owner_name = resolve_account_display_name(self.conn, row[10], row[11], row[12], row[13])
            request_type = row[14] if len(row) >= 16 and row[14] else "MON"
            request_sats = row[15] if len(row) >= 16 else 0
            expires_at = row[16] if len(row) >= 17 and row[16] else ""
            offer_type = row[17] if len(row) >= 19 and row[17] else "MON"
            offered_sats = row[18] if len(row) >= 19 else 0
            try:
                time_left = max(0, int((datetime.fromisoformat(expires_at) - datetime.now()).total_seconds())) if expires_at else 0
            except Exception:
                time_left = 0
            self.send_line(
                "LISTING",
                row[0], owner_name, row[2], row[3], row[4], row[5], row[6], row[7], row[8],
                created_at, request_type, request_sats, expires_at, time_left, offer_type, offered_sats
            )
        self.send_line("END")

    def _decode_optional_b64(self, value: str) -> str:
        try:
            if not value:
                return ""
            return decode_message(value).strip()
        except Exception:
            return ""

    def _build_gts_filter_where(self, parts, mine: bool):
        page_index = self.parse_int(parts[1] if len(parts) > 1 else 0, 0)
        search_text = self._decode_optional_b64(parts[2] if len(parts) > 2 else "")
        offered_filter = (parts[3] if len(parts) > 3 else "All").strip().upper()
        requested_filter = (parts[4] if len(parts) > 4 else "All").strip().upper()
        type_filter = (parts[5] if len(parts) > 5 else "All").strip().upper().replace("_", "-")
        time_filter = (parts[6] if len(parts) > 6 else "All").strip().upper()
        seller_filter = self._decode_optional_b64(parts[7] if len(parts) > 7 else "")

        params = []
        where = ["l.status = 'open'"]
        current_character_id, _current_handle = self.ensure_character_context()
        if mine:
            if current_character_id:
                where.append("COALESCE(l.owner_character_id, '') = ?")
                params.append(current_character_id)
            else:
                where.append("l.owner_account_id = ? AND COALESCE(l.owner_character_id, '') = ''")
                params.append(self.account_id)
        else:
            if current_character_id:
                where.append("COALESCE(l.owner_character_id, '') != ?")
                params.append(current_character_id)
            else:
                where.append("l.owner_account_id != ?")
                params.append(self.account_id)

        if offered_filter in ("MON", "SATS"):
            where.append("UPPER(COALESCE(l.offer_type, 'MON')) = ?")
            params.append(offered_filter)
        if requested_filter in ("MON", "SATS"):
            where.append("UPPER(COALESCE(l.request_type, 'MON')) = ?")
            params.append(requested_filter)

        if type_filter in ("MON-FOR-MON", "MON/MON"):
            where.append("UPPER(COALESCE(l.offer_type, 'MON')) = 'MON' AND UPPER(COALESCE(l.request_type, 'MON')) = 'MON'")
        elif type_filter in ("MON-FOR-SATS", "MON/SATS"):
            where.append("UPPER(COALESCE(l.offer_type, 'MON')) = 'MON' AND UPPER(COALESCE(l.request_type, 'MON')) = 'SATS'")
        elif type_filter in ("SATS-FOR-MON", "SATS/MON"):
            where.append("UPPER(COALESCE(l.offer_type, 'MON')) = 'SATS' AND UPPER(COALESCE(l.request_type, 'MON')) = 'MON'")

        now = datetime.now()
        if time_filter == "SHORT":
            where.append("l.expires_at <= ?")
            params.append((now + timedelta(hours=12)).isoformat())
        elif time_filter == "MEDIUM":
            where.append("l.expires_at > ? AND l.expires_at <= ?")
            params.append((now + timedelta(hours=12)).isoformat())
            params.append((now + timedelta(hours=36)).isoformat())
        elif time_filter == "LONG":
            where.append("l.expires_at > ?")
            params.append((now + timedelta(hours=36)).isoformat())

        if search_text:
            like = f"%{search_text.lower()}%"
            where.append("(LOWER(l.offered_species) LIKE ? OR LOWER(l.request_species) LIKE ? OR LOWER(l.owner_username) LIKE ? OR LOWER(COALESCE(a.display_name, '')) LIKE ? OR LOWER(COALESCE(l.owner_public_handle, '')) LIKE ?)")
            params.extend([like, like, like, like, like])
        if seller_filter:
            like = f"%{seller_filter.lower()}%"
            where.append("(LOWER(l.owner_username) LIKE ? OR LOWER(COALESCE(a.display_name, '')) LIKE ? OR LOWER(COALESCE(l.owner_public_handle, '')) LIKE ?)")
            params.extend([like, like, like])

        return max(0, page_index), " AND ".join(where), params

    def cmd_gts_search_filter_page(self, parts, mine: bool = False):
        if not self.require_login():
            return
        ensure_tradingpost_maintenance(self.conn)
        page_index, where_sql, params = self._build_gts_filter_where(parts, mine)
        total_count = self.conn.execute(
            f"SELECT COUNT(*) FROM gts_listings l LEFT JOIN accounts a ON a.id = l.owner_account_id WHERE {where_sql}",
            params
        ).fetchone()[0]
        page_count = max(1, math.ceil(total_count / GTS_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        offset = page_index * GTS_PAGE_SIZE
        rows = self.conn.execute(
            f"""
            SELECT l.id, COALESCE(NULLIF(l.owner_public_handle, ''), COALESCE(NULLIF(a.display_name, ''), l.owner_username)) AS owner_display_name,
                   l.request_species, l.offered_species, l.level, l.name_b64, l.gender, l.shiny, l.blob_b64,
                   l.created_at, l.owner_account_id, a.steam_id64, a.display_name, l.owner_username,
                   COALESCE(l.request_type, 'MON') AS request_type, COALESCE(l.request_sats, 0) AS request_sats,
                   COALESCE(l.expires_at, '') AS expires_at,
                   COALESCE(l.offer_type, 'MON') AS offer_type, COALESCE(l.offered_sats, 0) AS offered_sats,
                   COALESCE(l.owner_public_handle, '') AS owner_public_handle, COALESCE(l.owner_character_id, '') AS owner_character_id
            FROM gts_listings l
            LEFT JOIN accounts a ON a.id = l.owner_account_id
            WHERE {where_sql}
            ORDER BY l.created_at ASC, l.id ASC
            LIMIT ? OFFSET ?
            """,
            (*params, GTS_PAGE_SIZE, offset)
        ).fetchall()
        self._send_listing_page("GTS_MY_LISTINGS_PAGE" if mine else "GTS_SEARCH_PAGE", page_index, total_count, rows)

    def cmd_gts_search_page(self, parts):
        if not self.require_login():
            return
        page_index = self.parse_int(parts[1] if len(parts) > 1 else 0, 0)
        species_filter = parts[2].strip() if len(parts) > 2 else "*"
        page_index = max(0, page_index)
        ensure_tradingpost_maintenance(self.conn)
        # Public browse intentionally hides the current character's own listings. Use My Listings to cancel/inspect yours.
        current_character_id, _current_handle = self.ensure_character_context()
        if current_character_id:
            params = [current_character_id]
            where = ["l.status = 'open'", "COALESCE(l.owner_character_id, '') != ?"]
        else:
            params = [self.account_id]
            where = ["l.status = 'open'", "l.owner_account_id != ?"]
        if species_filter and species_filter != "*":
            where.append("l.request_species = ?")
            params.append(species_filter)
        where_sql = " AND ".join(where)
        total_count = self.conn.execute(f"SELECT COUNT(*) FROM gts_listings l WHERE {where_sql}", params).fetchone()[0]
        page_count = max(1, math.ceil(total_count / GTS_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        offset = page_index * GTS_PAGE_SIZE
        rows = self.conn.execute(
            f"""
            SELECT l.id, COALESCE(NULLIF(l.owner_public_handle, ''), COALESCE(NULLIF(a.display_name, ''), l.owner_username)) AS owner_display_name,
                   l.request_species, l.offered_species, l.level, l.name_b64, l.gender, l.shiny, l.blob_b64,
                   l.created_at, l.owner_account_id, a.steam_id64, a.display_name, l.owner_username,
                   COALESCE(l.request_type, 'MON') AS request_type, COALESCE(l.request_sats, 0) AS request_sats,
                   COALESCE(l.expires_at, '') AS expires_at,
                   COALESCE(l.offer_type, 'MON') AS offer_type, COALESCE(l.offered_sats, 0) AS offered_sats,
                   COALESCE(l.owner_public_handle, '') AS owner_public_handle, COALESCE(l.owner_character_id, '') AS owner_character_id
            FROM gts_listings l
            LEFT JOIN accounts a ON a.id = l.owner_account_id
            WHERE {where_sql}
            ORDER BY l.created_at ASC, l.id ASC
            LIMIT ? OFFSET ?
            """,
            (*params, GTS_PAGE_SIZE, offset)
        ).fetchall()
        self._send_listing_page("GTS_SEARCH_PAGE", page_index, total_count, rows)

    def cmd_gts_my_listings_page(self, parts):
        if not self.require_login():
            return
        page_index = self.parse_int(parts[1] if len(parts) > 1 else 0, 0)
        page_index = max(0, page_index)
        ensure_tradingpost_maintenance(self.conn)
        current_character_id, _current_handle = self.ensure_character_context()
        if current_character_id:
            mine_where = "COALESCE(l.owner_character_id, '') = ? AND l.status = 'open'"
            mine_params = [current_character_id]
        else:
            mine_where = "l.owner_account_id = ? AND COALESCE(l.owner_character_id, '') = '' AND l.status = 'open'"
            mine_params = [self.account_id]
        total_count = self.conn.execute(
            f"SELECT COUNT(*) FROM gts_listings l WHERE {mine_where}",
            mine_params
        ).fetchone()[0]
        page_count = max(1, math.ceil(total_count / GTS_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        offset = page_index * GTS_PAGE_SIZE
        rows = self.conn.execute(
            f"""
            SELECT l.id, COALESCE(NULLIF(l.owner_public_handle, ''), COALESCE(NULLIF(a.display_name, ''), l.owner_username)) AS owner_display_name,
                   l.request_species, l.offered_species, l.level, l.name_b64, l.gender, l.shiny, l.blob_b64,
                   l.created_at, l.owner_account_id, a.steam_id64, a.display_name, l.owner_username,
                   COALESCE(l.request_type, 'MON') AS request_type, COALESCE(l.request_sats, 0) AS request_sats,
                   COALESCE(l.expires_at, '') AS expires_at,
                   COALESCE(l.offer_type, 'MON') AS offer_type, COALESCE(l.offered_sats, 0) AS offered_sats,
                   COALESCE(l.owner_public_handle, '') AS owner_public_handle, COALESCE(l.owner_character_id, '') AS owner_character_id
            FROM gts_listings l
            LEFT JOIN accounts a ON a.id = l.owner_account_id
            WHERE {mine_where}
            ORDER BY l.created_at ASC, l.id ASC
            LIMIT ? OFFSET ?
            """,
            (*mine_params, GTS_PAGE_SIZE, offset)
        ).fetchall()
        self._send_listing_page("GTS_MY_LISTINGS_PAGE", page_index, total_count, rows)

    def cmd_gts_cancel(self, parts):
        if not self.require_login():
            return
        listing_id = self.parse_int(parts[1] if len(parts) > 1 else None, None)
        if listing_id is None:
            self.send_error("Invalid listing id.")
            return
        # MariaDB persistent handler connection: do not use `with self.conn` here.
        # The wrapper context manager closes the socket-session DB connection on exit;
        # follow-up commands like My Listings then fail with PyMySQL InterfaceError(0, "").
        row = self.conn.execute(
            """
            SELECT owner_account_id, status, blob_b64, COALESCE(offer_type, 'MON'), COALESCE(offered_sats, 0), COALESCE(owner_character_id, '')
              FROM gts_listings WHERE id = ?
            """,
            (listing_id,)
        ).fetchone()
        if not row:
            self.send_error("That listing does not exist.")
            return
        owner_account_id, status, blob_b64, offer_type, offered_sats, owner_character_id = row
        offer_type = (offer_type or "MON").upper()
        offered_sats = int(offered_sats or 0)
        if not self.is_own_listing_row(owner_account_id, owner_character_id):
            self.send_error("That listing does not belong to this character.")
            return
        if status != "open":
            self.send_error("That listing is no longer open.")
            return
        self.conn.execute(
            "UPDATE gts_listings SET status = 'cancelled', updated_at = ? WHERE id = ?",
            (utcnow_iso(), listing_id)
        )
        log(f"GTS cancel: user={self.username} listing_id={listing_id} offer_type={offer_type}")
        if offer_type == "SATS":
            self.send_line("OK", "GTS_CANCEL", "SATS", offered_sats)
        else:
            self.send_line("OK", "GTS_CANCEL", blob_b64)

    def cmd_gts_offer(self, parts):
        if not self.require_login():
            return
        if len(parts) < 8:
            self.send_error("Invalid Trading Post offer request.")
            return
        listing_id = self.parse_int(parts[1], None)
        offered_species = parts[2].strip()
        level = self.parse_int(parts[3], -1)
        _name_b64 = parts[4]
        _gender = self.parse_int(parts[5], 0)
        _shiny = self.parse_int(parts[6], 0)
        blob_b64 = parts[7]
        if listing_id is None or not offered_species or level < 1 or not blob_b64:
            self.send_error("Invalid Trading Post offer data.")
            return
        now = utcnow_iso()
        try:
            self.conn.execute("BEGIN IMMEDIATE")
            row = self.conn.execute(
                """
                SELECT id, owner_account_id, owner_username, request_species, blob_b64, status,
                       COALESCE(request_type, 'MON'), COALESCE(offer_type, 'MON'), COALESCE(offered_sats, 0),
                       COALESCE(owner_character_id, ''), COALESCE(owner_public_handle, '')
                FROM gts_listings WHERE id = ?
                """,
                (listing_id,)
            ).fetchone()
            if not row:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing does not exist.")
                return
            _, owner_account_id, owner_username, request_species, listed_blob_b64, status, request_type, offer_type, offered_sats, owner_character_id, owner_public_handle = row
            request_type = (request_type or "MON").upper()
            offer_type = (offer_type or "MON").upper()
            offered_sats = int(offered_sats or 0)
            if self.is_own_listing_row(owner_account_id, owner_character_id):
                self.conn.execute("ROLLBACK")
                self.send_error("You cannot trade with your own character's listing.")
                return
            if status != "open":
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer available.")
                return
            if request_type != "MON":
                self.conn.execute("ROLLBACK")
                self.send_error("That listing wants SATS, not a MoN offer.")
                return
            if offered_species != request_species:
                self.conn.execute("ROLLBACK")
                self.send_error("That MoN does not match the requested species.")
                return
            if offer_type == "SATS" and offered_sats <= 0:
                self.conn.execute("ROLLBACK")
                self.send_error("That SATS offer is invalid.")
                return
            updated = self.conn.execute(
                """
                UPDATE gts_listings
                SET status = 'completed', completed_by_account_id = ?, completed_by_username = ?,
                    completed_by_character_id = ?, completed_by_public_handle = ?, updated_at = ?
                WHERE id = ? AND status = 'open'
                """,
                (self.account_id, self.username, self.character_id or "", self.public_handle or "", now, listing_id)
            )
            if updated.rowcount != 1:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer available.")
                return
            buyer_name = self.sender_mail_handle()
            subject = f"Trade complete: {request_species} received"
            body = (
                f"{buyer_name} completed your Trading Post listing.\n\n"
                f"They offered the requested MoN: {request_species}.\n"
                f"Open this mail and claim the attachment to receive the traded MoN."
            )
            add_mail(self.conn, owner_account_id, self.account_id, "Trading Post", subject, body,
                     "TRADE_COMPLETE", "MON", blob_b64, 0, listing_id, now,
                     owner_character_id or "", owner_public_handle or "", self.character_id or "", self.public_handle or "")
            self.conn.execute("COMMIT")
        except Exception:
            try:
                self.conn.execute("ROLLBACK")
            except Exception:
                pass
            raise
        log(f"GTS offer: buyer={self.username} owner={owner_username} listing_id={listing_id} species={offered_species} lvl={level} offer_type={offer_type}")
        if offer_type == "SATS":
            self.send_line("OK", "GTS_OFFER", "SATS", offered_sats)
        else:
            self.send_line("OK", "GTS_OFFER", listed_blob_b64)

    def cmd_gts_buy_sats(self, parts):
        if not self.require_login():
            return
        listing_id = self.parse_int(parts[1] if len(parts) > 1 else None, None)
        client_price = self.parse_int(parts[2] if len(parts) > 2 else None, None)
        if listing_id is None:
            self.send_error("Invalid listing id.")
            return
        now = utcnow_iso()
        try:
            self.conn.execute("BEGIN IMMEDIATE")
            row = self.conn.execute(
                """
                SELECT id, owner_account_id, owner_username, request_type, request_sats, blob_b64, status, COALESCE(offer_type, 'MON'),
                       COALESCE(owner_character_id, ''), COALESCE(owner_public_handle, '')
                FROM gts_listings WHERE id = ?
                """,
                (listing_id,)
            ).fetchone()
            if not row:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing does not exist.")
                return
            _, owner_account_id, owner_username, request_type, request_sats, listed_blob_b64, status, offer_type, owner_character_id, owner_public_handle = row
            request_type = (request_type or "MON").upper()
            offer_type = (offer_type or "MON").upper()
            request_sats = int(request_sats or 0)
            if self.is_own_listing_row(owner_account_id, owner_character_id):
                self.conn.execute("ROLLBACK")
                self.send_error("You cannot buy your own character's listing.")
                return
            if status != "open":
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer available.")
                return
            if offer_type != "MON":
                self.conn.execute("ROLLBACK")
                self.send_error("That listing offers SATS and must be completed by offering the requested MoN.")
                return
            if request_type != "SATS" or request_sats <= 0:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing does not request SATS.")
                return
            if client_price is not None and client_price != request_sats:
                self.conn.execute("ROLLBACK")
                self.send_error("Listing price changed. Refresh the Trading Post.")
                return
            updated = self.conn.execute(
                """
                UPDATE gts_listings
                SET status = 'completed', completed_by_account_id = ?, completed_by_username = ?,
                    completed_by_character_id = ?, completed_by_public_handle = ?, updated_at = ?
                WHERE id = ? AND status = 'open'
                """,
                (self.account_id, self.username, self.character_id or "", self.public_handle or "", now, listing_id)
            )
            if updated.rowcount != 1:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer available.")
                return
            buyer_name = self.sender_mail_handle()
            subject = f"Sale complete: {request_sats} SATS"
            body = (
                f"{buyer_name} bought your Trading Post listing for {request_sats} SATS.\n\n"
                f"Open this mail and claim the attachment to receive the SATS payout."
            )
            add_mail(self.conn, owner_account_id, self.account_id, "Trading Post", subject, body,
                     "SALE_COMPLETE", "SATS", "", request_sats, listing_id, now,
                     owner_character_id or "", owner_public_handle or "", self.character_id or "", self.public_handle or "")
            self.conn.execute("COMMIT")
        except Exception:
            try:
                self.conn.execute("ROLLBACK")
            except Exception:
                pass
            raise
        log(f"GTS buy SATS: buyer={self.username} owner={owner_username} listing_id={listing_id} amount={request_sats}")
        self.send_line("OK", "GTS_BUY_SATS", listed_blob_b64)

    def cmd_gts_notify_accepted(self):
        if not self.require_login():
            return
        now = utcnow_iso()
        ensure_tradingpost_maintenance(self.conn)
        rows = self.conn.execute(
            """
            SELECT id, source_listing_id
            FROM gts_claims
            WHERE owner_account_id = ?
              AND claimed = 0
              AND (notified_at IS NULL OR notified_at = '')
            ORDER BY id ASC
            """,
            (self.account_id,)
        ).fetchall()
        sats_rows = self.conn.execute(
            """
            SELECT id, source_listing_id
            FROM gts_sats_claims
            WHERE owner_account_id = ?
              AND claimed = 0
              AND (notified_at IS NULL OR notified_at = '')
            ORDER BY id ASC
            """,
            (self.account_id,)
        ).fetchall()
        mail_where, mail_params = self.mail_recipient_where()
        mail_rows = self.conn.execute(
            f"""
            SELECT id
              FROM mail_messages
             WHERE {mail_where}
               AND is_deleted = 0
               AND claimed_at IS NULL
               AND attachment_type IN ('MON', 'SATS', 'ITEM', 'MON_REWARD')
               AND (notified_at IS NULL OR notified_at = '')
             ORDER BY id ASC
            """,
            mail_params
        ).fetchall()
        count = len(rows) + len(sats_rows) + len(mail_rows)
        if rows:
            claim_ids = [row[0] for row in rows]
            placeholders = ",".join("?" for _ in claim_ids)
            self.conn.execute(
                f"UPDATE gts_claims SET notified_at = ? WHERE id IN ({placeholders})",
                (now, *claim_ids)
            )
        if sats_rows:
            claim_ids = [row[0] for row in sats_rows]
            placeholders = ",".join("?" for _ in claim_ids)
            self.conn.execute(
                f"UPDATE gts_sats_claims SET notified_at = ? WHERE id IN ({placeholders})",
                (now, *claim_ids)
            )
        if mail_rows:
            mail_ids = [row[0] for row in mail_rows]
            placeholders = ",".join("?" for _ in mail_ids)
            self.conn.execute(
                f"UPDATE mail_messages SET notified_at = ? WHERE id IN ({placeholders})",
                (now, *mail_ids)
            )
        if count:
            log(f"GTS notify accepted: user={self.username} count={count}")
        self.send_line("OK", "GTS_NOTIFY_ACCEPTED", count)

    def cmd_gts_claim(self):
        if not self.require_login():
            return
        ensure_tradingpost_maintenance(self.conn)
        rows = self.conn.execute(
            "SELECT id, source_listing_id, blob_b64 FROM gts_claims WHERE owner_account_id = ? AND claimed = 0 ORDER BY id ASC",
            (self.account_id,)
        ).fetchall()
        sats_rows = self.conn.execute(
            "SELECT id, source_listing_id, amount FROM gts_sats_claims WHERE owner_account_id = ? AND claimed = 0 ORDER BY id ASC",
            (self.account_id,)
        ).fetchall()
        mail_where, mail_params = self.mail_recipient_where()
        mail_rows = self.conn.execute(
            f"""
            SELECT id, source_listing_id, attachment_type, COALESCE(attachment_blob_b64, ''), COALESCE(attachment_sats, 0)
              FROM mail_messages
             WHERE {mail_where}
               AND is_deleted = 0
               AND claimed_at IS NULL
               AND attachment_type IN ('MON', 'SATS', 'ITEM', 'MON_REWARD')
             ORDER BY id ASC
            """,
            mail_params
        ).fetchall()
        mail_mon_rows = [r for r in mail_rows if (r[2] or '').upper() == 'MON']
        mail_sats_rows = [r for r in mail_rows if (r[2] or '').upper() == 'SATS']
        total_mon = len(rows) + len(mail_mon_rows)
        total_sats = sum(int(r[2] or 0) for r in sats_rows) + sum(int(r[4] or 0) for r in mail_sats_rows)
        self.send_line("OK", "GTS_CLAIM", total_mon, total_sats, len(sats_rows) + len(mail_sats_rows))
        for _, source_listing_id, blob_b64 in rows:
            self.send_line("CLAIM", source_listing_id, blob_b64)
        for mail_id, source_listing_id, _attachment_type, blob_b64, _amount in mail_mon_rows:
            self.send_line("CLAIM", source_listing_id or mail_id, blob_b64)
        for _, source_listing_id, amount in sats_rows:
            self.send_line("SATS", source_listing_id, int(amount or 0))
        for mail_id, source_listing_id, _attachment_type, _blob_b64, amount in mail_sats_rows:
            self.send_line("SATS", source_listing_id or mail_id, int(amount or 0))
        self.send_line("END")
        now = utcnow_iso()
        if rows:
            claim_ids = [row[0] for row in rows]
            placeholders = ",".join("?" for _ in claim_ids)
            self.conn.execute(
                f"UPDATE gts_claims SET claimed = 1, claimed_at = ? WHERE id IN ({placeholders})",
                (now, *claim_ids)
            )
        if sats_rows:
            claim_ids = [row[0] for row in sats_rows]
            placeholders = ",".join("?" for _ in claim_ids)
            self.conn.execute(
                f"UPDATE gts_sats_claims SET claimed = 1, claimed_at = ? WHERE id IN ({placeholders})",
                (now, *claim_ids)
            )
        if mail_rows:
            mail_ids = [row[0] for row in mail_rows]
            placeholders = ",".join("?" for _ in mail_ids)
            self.conn.execute(
                f"UPDATE mail_messages SET claimed_at = ?, is_read = 1, read_at = COALESCE(read_at, ?), expires_at = ? WHERE id IN ({placeholders})",
                (now, now, mail_expiry_for(True, now), *mail_ids)
            )
        if rows or sats_rows or mail_rows:
            log(f"GTS claim: user={self.username} mon_count={total_mon} sats_total={total_sats}")


    # -------------------------------------------------------------------------
    # Trading Post / Mailbox commands
    # -------------------------------------------------------------------------

    def _mail_counts(self):
        ensure_tradingpost_maintenance(self.conn)
        mail_where, mail_params = self.mail_recipient_where()
        row = self.conn.execute(
            f"""
            SELECT
                SUM(CASE WHEN is_read = 0 THEN 1 ELSE 0 END) AS unread_count,
                COUNT(*) AS total_count,
                SUM(CASE WHEN claimed_at IS NULL AND attachment_type IN ('MON', 'SATS', 'ITEM', 'MON_REWARD') THEN 1 ELSE 0 END) AS claimable_count
              FROM mail_messages
             WHERE {mail_where}
               AND is_deleted = 0
            """,
            mail_params
        ).fetchone()
        if not row:
            return 0, 0, 0
        return int(row[0] or 0), int(row[1] or 0), int(row[2] or 0)

    def cmd_mail_count(self):
        if not self.require_login():
            return
        handle = self.sender_mail_handle()
        unread, total, claimable = self._mail_counts()
        self.send_line("OK", "MAIL_COUNT", unread, total, claimable, encode_message(handle))

    def cmd_mail_list(self, parts):
        if not self.require_login():
            return
        page_index = self.parse_int(parts[1] if len(parts) > 1 else 0, 0)
        page_index = max(0, page_index)
        ensure_tradingpost_maintenance(self.conn)
        mail_where, mail_params = self.mail_recipient_where()
        total_count = self.conn.execute(
            f"SELECT COUNT(*) FROM mail_messages WHERE {mail_where} AND is_deleted = 0",
            mail_params
        ).fetchone()[0]
        page_count = max(1, math.ceil(total_count / MAIL_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        offset = page_index * MAIL_PAGE_SIZE
        rows = self.conn.execute(
            f"""
            SELECT id, sender_name, subject_b64, created_at, is_read, is_saved, attachment_type,
                   COALESCE(attachment_sats, 0), CASE WHEN claimed_at IS NULL THEN 0 ELSE 1 END AS claimed,
                   mail_type, expires_at
              FROM mail_messages
             WHERE {mail_where}
               AND is_deleted = 0
             ORDER BY is_read ASC, created_at DESC, id DESC
             LIMIT ? OFFSET ?
            """,
            (*mail_params, MAIL_PAGE_SIZE, offset)
        ).fetchall()
        unread, _total, claimable = self._mail_counts()
        handle = self.sender_mail_handle()
        self.send_line("OK", "MAIL_LIST", page_index, page_count, total_count, unread, claimable, encode_message(handle))
        for row in rows:
            self.send_line(
                "MAIL",
                row[0], encode_message(row[1] or "System"), row[2], row[3], row[4], row[5], row[6], int(row[7] or 0), row[8], row[9], row[10]
            )
        self.send_line("END")

    def cmd_mail_view(self, parts):
        if not self.require_login():
            return
        mail_id = self.parse_int(parts[1] if len(parts) > 1 else None, None)
        if mail_id is None:
            self.send_error("Invalid mail id.")
            return
        now = utcnow_iso()
        mail_where, mail_params = self.mail_recipient_where()
        row = self.conn.execute(
            f"""
            SELECT id, sender_name, subject_b64, body_b64, created_at, is_read, is_saved, attachment_type,
                   COALESCE(attachment_blob_b64, ''), COALESCE(attachment_sats, 0), source_listing_id,
                   CASE WHEN claimed_at IS NULL THEN 0 ELSE 1 END AS claimed, mail_type, expires_at
              FROM mail_messages
             WHERE id = ? AND {mail_where} AND is_deleted = 0
            """,
            (mail_id, *mail_params)
        ).fetchone()
        if not row:
            self.send_error("Mail not found.")
            return
        if int(row[5] or 0) == 0:
            self.conn.execute(
                f"UPDATE mail_messages SET is_read = 1, read_at = ?, expires_at = ? WHERE id = ? AND {mail_where}",
                (now, mail_expiry_for(True, now), mail_id, *mail_params)
            )
        self.send_line(
            "OK", "MAIL_VIEW",
            row[0], encode_message(row[1] or "System"), row[2], row[3], row[4], 1, row[6], row[7], row[9], row[10] or 0, row[11], row[12], row[13]
        )

    def cmd_mail_delete(self, parts):
        if not self.require_login():
            return
        mail_id = self.parse_int(parts[1] if len(parts) > 1 else None, None)
        if mail_id is None:
            self.send_error("Invalid mail id.")
            return
        mail_where, mail_params = self.mail_recipient_where()
        self.conn.execute(
            f"UPDATE mail_messages SET is_deleted = 1, deleted_at = ? WHERE id = ? AND {mail_where}",
            (utcnow_iso(), mail_id, *mail_params)
        )
        self.send_line("OK", "MAIL_DELETE", mail_id)

    def cmd_mail_save(self, parts):
        if not self.require_login():
            return
        mail_id = self.parse_int(parts[1] if len(parts) > 1 else None, None)
        save_flag = self.parse_int(parts[2] if len(parts) > 2 else 1, 1)
        if mail_id is None:
            self.send_error("Invalid mail id.")
            return
        mail_where, mail_params = self.mail_recipient_where()
        self.conn.execute(
            f"UPDATE mail_messages SET is_saved = ? WHERE id = ? AND {mail_where} AND is_deleted = 0",
            (1 if save_flag else 0, mail_id, *mail_params)
        )
        self.send_line("OK", "MAIL_SAVE", mail_id, 1 if save_flag else 0)

    def cmd_mail_send(self, parts):
        if not self.require_login():
            return
        if len(parts) < 4:
            self.send_error("MAIL_SEND needs recipient, subject, and body.")
            return
        recipient_name = decode_message(parts[1]).strip()
        subject = decode_message(parts[2]).strip()
        body = decode_message(parts[3])
        attachment_type = (parts[4].strip().upper() if len(parts) >= 5 else "NONE") or "NONE"
        attachment_sats = self.parse_int(parts[5] if len(parts) >= 6 else None, 0) or 0
        attachment_blob = parts[6].strip() if len(parts) >= 7 else ""
        if attachment_type not in ("NONE", "MON", "SATS"):
            self.send_error("Invalid mail attachment type.")
            return
        if attachment_type == "SATS" and attachment_sats <= 0:
            self.send_error("Invalid SATS attachment amount.")
            return
        if attachment_type == "MON" and not attachment_blob:
            self.send_error("Missing MoN attachment data.")
            return
        if not recipient_name or not subject:
            self.send_error("Recipient and subject are required.")
            return
        sender_name = self.sender_mail_handle()
        if recipient_name.lower() == MAILMON_HANDLE.lower():
            if attachment_type != "NONE":
                self.send_error("MMOMailmon does not accept attachments.")
                return
            reply_body = (
                "Thank you for choosing to play MMOnsterpatch. Keep things civil and fair. "
                "For any issues encountered, please go to:\n"
                f"{MAILMON_GITHUB_URL}\n\n"
                f"{MAILMON_X_URL}"
            )
            mail_id = add_mail(
                self.conn,
                self.account_id,
                None,
                MAILMON_HANDLE,
                "Welcome to MMOnsterpatch",
                reply_body,
                "SYSTEM",
                "NONE",
                "",
                0,
                None,
                None,
                recipient_character_id=self.character_id or "",
                recipient_public_handle=self.public_handle or "",
                sender_character_id="",
                sender_public_handle=MAILMON_HANDLE,
            )
            log(f"MMOMailmon auto-reply sent: from={sender_name} account={self.account_id} mail_id={mail_id}")
            self.send_line("OK", "MAIL_SEND", mail_id)
            return
        row = find_recipient_account_by_mail_name(self.conn, recipient_name)
        if not row:
            self.send_error("Recipient not found. Use the full player mailbox name, for example Player#1234.")
            return
        recip_id, _username, _steam_id64, _display_name, _mail_handle, recip_character_id, recip_public_handle = row
        ensure_mailbox(self.conn, recip_id, _display_name, _username)
        if recip_character_id and recip_public_handle:
            ensure_character_mailbox(self.conn, recip_id, recip_character_id, recip_public_handle)
        mail_id = add_mail(
            self.conn,
            recip_id,
            self.account_id,
            sender_name,
            subject,
            body,
            "PLAYER",
            attachment_type,
            attachment_blob,
            attachment_sats,
            None,
            None,
            recipient_character_id=recip_character_id or "",
            recipient_public_handle=recip_public_handle or "",
            sender_character_id=self.character_id or "",
            sender_public_handle=self.public_handle or "",
        )
        log(f"Mail sent: from={sender_name} to={recip_public_handle or _mail_handle or _display_name or _username} mail_id={mail_id}")
        self.send_line("OK", "MAIL_SEND", mail_id)

    def cmd_mail_claim(self, parts):
        if not self.require_login():
            return
        mail_id = self.parse_int(parts[1] if len(parts) > 1 else None, None)
        if mail_id is None:
            self.send_error("Invalid mail id.")
            return
        now = utcnow_iso()
        mail_where, mail_params = self.mail_recipient_where()
        try:
            self.conn.execute("BEGIN IMMEDIATE")
            row = self.conn.execute(
                f"""
                SELECT id, attachment_type, COALESCE(attachment_blob_b64, ''), COALESCE(attachment_sats, 0), COALESCE(source_listing_id, id)
                  FROM mail_messages
                 WHERE id = ? AND {mail_where} AND is_deleted = 0 AND claimed_at IS NULL
                """,
                (mail_id, *mail_params)
            ).fetchone()
            if not row:
                self.conn.execute("ROLLBACK")
                self.send_error("No claimable attachment found.")
                return
            _mid, attachment_type, blob_b64, amount, source_listing_id = row
            attachment_type = (attachment_type or "NONE").upper()
            if attachment_type not in ("MON", "SATS", "ITEM", "MON_REWARD"):
                self.conn.execute("ROLLBACK")
                self.send_error("This mail has no claimable attachment.")
                return
            self.conn.execute(
                f"UPDATE mail_messages SET claimed_at = ?, is_read = 1, read_at = COALESCE(read_at, ?), expires_at = ? WHERE id = ? AND {mail_where}",
                (now, now, mail_expiry_for(True, now), mail_id, *mail_params)
            )
            self.conn.execute("COMMIT")
        except Exception:
            try:
                self.conn.execute("ROLLBACK")
            except Exception:
                pass
            raise
        if attachment_type == "MON":
            self.send_line("OK", "MAIL_CLAIM", "MON", source_listing_id, blob_b64)
        elif attachment_type == "SATS":
            self.send_line("OK", "MAIL_CLAIM", "SATS", source_listing_id, int(amount or 0))
        elif attachment_type == "ITEM":
            self.send_line("OK", "MAIL_CLAIM", "ITEM", source_listing_id, blob_b64, int(amount or 1))
        else:
            mon_spec = ""
            level = 1
            shiny = 0
            try:
                data = json.loads(decode_message(blob_b64 or "e30="))
                mon_spec = str(data.get("mon_spec", ""))
                level = int(data.get("level", 1))
                shiny = 1 if bool(data.get("shiny", False)) else 0
            except Exception:
                mon_spec = ""
            self.send_line("OK", "MAIL_CLAIM", "MON_REWARD", source_listing_id, encode_message(mon_spec), int(level), int(shiny))



    # -------------------------------------------------------------------------
    # Trading Post Marketplace commands
    # -------------------------------------------------------------------------

    def _ra_send_pokemon_page(self, tag, page_index, total_count, rows):
        page_count = max(1, math.ceil(total_count / RA_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        self.send_line("OK", tag, page_index, page_count, total_count, RA_PAGE_SIZE)
        for row in rows:
            self.send_line("RA_POKEMON", row[0], row[1], row[2], row[3], row[4], row[5], row[6], row[7], row[8])
        self.send_line("END")

    def _ra_send_item_page(self, tag, page_index, total_count, rows):
        page_count = max(1, math.ceil(total_count / RA_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        self.send_line("OK", tag, page_index, page_count, total_count, RA_PAGE_SIZE)
        for row in rows:
            self.send_line("RA_ITEM", row[0], row[1], row[2], row[3], row[4], row[5], row[6])
        self.send_line("END")

    def cmd_ra_pokemon_create(self, parts):
        if not self.require_login():
            return
        if len(parts) < 8:
            self.send_error("Invalid auction listing request.")
            return
        price = self.parse_int(parts[1], -1)
        species = parts[2].strip()
        level = self.parse_int(parts[3], -1)
        name_b64 = parts[4]
        gender = self.parse_int(parts[5], 0)
        shiny = self.parse_int(parts[6], 0)
        blob_b64 = parts[7]
        if price < 1 or price > RA_MAX_PRICE or not species or level < 1 or not blob_b64:
            self.send_error("Invalid auction listing data.")
            return
        now = utcnow_iso()
        cur = self.conn.execute(
            """
            INSERT INTO rocket_pokemon_listings(
                seller_account_id, seller_username, price, species, level, name_b64,
                gender, shiny, blob_b64, status, created_at, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'open', ?, ?)
            """,
            (self.account_id, self.username, price, species, level, name_b64, gender, shiny, blob_b64, now, now),
        )
        log(f"RA Pokemon create: seller={self.username} listing_id={cur.lastrowid} species={species} price={price}")
        self.send_line("OK", "RA_POKEMON_CREATE", cur.lastrowid)

    def cmd_ra_pokemon_search_page(self, parts):
        if not self.require_login():
            return
        page_index = max(0, self.parse_int(parts[1] if len(parts) > 1 else 0, 0))
        total_count = self.conn.execute("SELECT COUNT(*) FROM rocket_pokemon_listings WHERE status = 'open' AND seller_account_id != ?", (self.account_id,)).fetchone()[0]
        page_count = max(1, math.ceil(total_count / RA_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        rows = self.conn.execute(
            """
            SELECT id, seller_username, price, species, level, name_b64, gender, shiny, blob_b64
            FROM rocket_pokemon_listings
            WHERE status = 'open' AND seller_account_id != ?
            ORDER BY created_at ASC, id ASC
            LIMIT ? OFFSET ?
            """,
            (self.account_id, RA_PAGE_SIZE, page_index * RA_PAGE_SIZE),
        ).fetchall()
        self._ra_send_pokemon_page("RA_POKEMON_SEARCH_PAGE", page_index, total_count, rows)

    def cmd_ra_pokemon_my_page(self, parts):
        if not self.require_login():
            return
        page_index = max(0, self.parse_int(parts[1] if len(parts) > 1 else 0, 0))
        total_count = self.conn.execute(
            "SELECT COUNT(*) FROM rocket_pokemon_listings WHERE seller_account_id = ? AND status = 'open'",
            (self.account_id,),
        ).fetchone()[0]
        page_count = max(1, math.ceil(total_count / RA_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        rows = self.conn.execute(
            """
            SELECT id, seller_username, price, species, level, name_b64, gender, shiny, blob_b64
            FROM rocket_pokemon_listings
            WHERE seller_account_id = ? AND status = 'open'
            ORDER BY created_at ASC, id ASC
            LIMIT ? OFFSET ?
            """,
            (self.account_id, RA_PAGE_SIZE, page_index * RA_PAGE_SIZE),
        ).fetchall()
        self._ra_send_pokemon_page("RA_POKEMON_MY_PAGE", page_index, total_count, rows)

    def cmd_ra_pokemon_buy(self, parts):
        if not self.require_login():
            return
        listing_id = self.parse_int(parts[1] if len(parts) > 1 else None, None)
        if listing_id is None:
            self.send_error("Invalid listing id.")
            return
        now = utcnow_iso()
        try:
            self.conn.execute("BEGIN IMMEDIATE")
            row = self.conn.execute(
                """
                SELECT seller_account_id, seller_username, price, blob_b64, status
                FROM rocket_pokemon_listings WHERE id = ?
                """,
                (listing_id,),
            ).fetchone()
            if not row:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing does not exist.")
                return
            seller_account_id, seller_username, price, blob_b64, status = row
            if seller_account_id == self.account_id:
                self.conn.execute("ROLLBACK")
                self.send_error("You cannot buy your own listing.")
                return
            if status != "open":
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer available.")
                return
            updated = self.conn.execute(
                """
                UPDATE rocket_pokemon_listings
                SET status = 'sold', buyer_account_id = ?, buyer_username = ?, updated_at = ?
                WHERE id = ? AND status = 'open'
                """,
                (self.account_id, self.username, now, listing_id),
            )
            if updated.rowcount != 1:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer available.")
                return
            self.conn.execute(
                """
                INSERT INTO rocket_payouts(owner_account_id, amount, reason, source_kind, source_listing_id, claimed, created_at)
                VALUES (?, ?, ?, 'pokemon', ?, 0, ?)
                """,
                (seller_account_id, price, f"Pokemon auction sold to {self.username}", listing_id, now),
            )
            self.conn.execute("COMMIT")
        except Exception:
            try:
                self.conn.execute("ROLLBACK")
            except Exception:
                pass
            raise
        log(f"RA Pokemon buy: buyer={self.username} seller={seller_username} listing_id={listing_id} price={price}")
        self.send_line("OK", "RA_POKEMON_BUY", blob_b64)

    def cmd_ra_item_create(self, parts):
        if not self.require_login():
            return
        if len(parts) < 5:
            self.send_error("Invalid item listing request.")
            return
        item_id = parts[1].strip()
        quantity = self.parse_int(parts[2], -1)
        price = self.parse_int(parts[3], -1)
        item_name_b64 = parts[4]
        if not item_id or quantity < 1 or price < 1 or price > RA_MAX_PRICE:
            self.send_error("Invalid item listing data.")
            return
        now = utcnow_iso()
        cur = self.conn.execute(
            """
            INSERT INTO rocket_item_listings(
                seller_account_id, seller_username, item_id, item_name_b64, quantity,
                price, status, created_at, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, 'open', ?, ?)
            """,
            (self.account_id, self.username, item_id, item_name_b64, quantity, price, now, now),
        )
        log(f"RA Item create: seller={self.username} listing_id={cur.lastrowid} item={item_id} qty={quantity} price={price}")
        self.send_line("OK", "RA_ITEM_CREATE", cur.lastrowid)

    def cmd_ra_item_search_page(self, parts):
        if not self.require_login():
            return
        page_index = max(0, self.parse_int(parts[1] if len(parts) > 1 else 0, 0))
        total_count = self.conn.execute("SELECT COUNT(*) FROM rocket_item_listings WHERE status = 'open' AND seller_account_id != ?", (self.account_id,)).fetchone()[0]
        page_count = max(1, math.ceil(total_count / RA_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        rows = self.conn.execute(
            """
            SELECT id, seller_username, item_id, item_name_b64, quantity, price, status
            FROM rocket_item_listings
            WHERE status = 'open' AND seller_account_id != ?
            ORDER BY created_at ASC, id ASC
            LIMIT ? OFFSET ?
            """,
            (self.account_id, RA_PAGE_SIZE, page_index * RA_PAGE_SIZE),
        ).fetchall()
        self._ra_send_item_page("RA_ITEM_SEARCH_PAGE", page_index, total_count, rows)

    def cmd_ra_item_my_page(self, parts):
        if not self.require_login():
            return
        page_index = max(0, self.parse_int(parts[1] if len(parts) > 1 else 0, 0))
        total_count = self.conn.execute(
            "SELECT COUNT(*) FROM rocket_item_listings WHERE seller_account_id = ? AND status = 'open'",
            (self.account_id,),
        ).fetchone()[0]
        page_count = max(1, math.ceil(total_count / RA_PAGE_SIZE)) if total_count > 0 else 1
        page_index = max(0, min(page_index, page_count - 1))
        rows = self.conn.execute(
            """
            SELECT id, seller_username, item_id, item_name_b64, quantity, price, status
            FROM rocket_item_listings
            WHERE seller_account_id = ? AND status = 'open'
            ORDER BY created_at ASC, id ASC
            LIMIT ? OFFSET ?
            """,
            (self.account_id, RA_PAGE_SIZE, page_index * RA_PAGE_SIZE),
        ).fetchall()
        self._ra_send_item_page("RA_ITEM_MY_PAGE", page_index, total_count, rows)

    def cmd_ra_item_buy(self, parts):
        if not self.require_login():
            return
        listing_id = self.parse_int(parts[1] if len(parts) > 1 else None, None)
        if listing_id is None:
            self.send_error("Invalid listing id.")
            return
        now = utcnow_iso()
        try:
            self.conn.execute("BEGIN IMMEDIATE")
            row = self.conn.execute(
                """
                SELECT seller_account_id, seller_username, item_id, item_name_b64, quantity, price, status
                FROM rocket_item_listings WHERE id = ?
                """,
                (listing_id,),
            ).fetchone()
            if not row:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing does not exist.")
                return
            seller_account_id, seller_username, item_id, item_name_b64, quantity, price, status = row
            if seller_account_id == self.account_id:
                self.conn.execute("ROLLBACK")
                self.send_error("You cannot buy your own listing.")
                return
            if status != "open":
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer available.")
                return
            updated = self.conn.execute(
                """
                UPDATE rocket_item_listings
                SET status = 'sold', buyer_account_id = ?, buyer_username = ?, updated_at = ?
                WHERE id = ? AND status = 'open'
                """,
                (self.account_id, self.username, now, listing_id),
            )
            if updated.rowcount != 1:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer available.")
                return
            self.conn.execute(
                """
                INSERT INTO rocket_payouts(owner_account_id, amount, reason, source_kind, source_listing_id, claimed, created_at)
                VALUES (?, ?, ?, 'item', ?, 0, ?)
                """,
                (seller_account_id, price, f"Item auction sold to {self.username}", listing_id, now),
            )
            self.conn.execute("COMMIT")
        except Exception:
            try:
                self.conn.execute("ROLLBACK")
            except Exception:
                pass
            raise
        log(f"RA Item buy: buyer={self.username} seller={seller_username} listing_id={listing_id} item={item_id} qty={quantity} price={price}")
        self.send_line("OK", "RA_ITEM_BUY", item_id, item_name_b64, quantity, price)

    def cmd_ra_claim_payouts(self):
        if not self.require_login():
            return
        rows = self.conn.execute(
            "SELECT id, amount FROM rocket_payouts WHERE owner_account_id = ? AND claimed = 0 ORDER BY id ASC",
            (self.account_id,),
        ).fetchall()
        total = sum(row[1] for row in rows)
        if rows:
            ids = [row[0] for row in rows]
            placeholders = ",".join("?" for _ in ids)
            self.conn.execute(
                f"UPDATE rocket_payouts SET claimed = 1, claimed_at = ? WHERE id IN ({placeholders})",
                (utcnow_iso(), *ids),
            )
        log(f"RA Claim payouts: user={self.username} total={total} count={len(rows)}")
        self.send_line("OK", "RA_CLAIM_PAYOUTS", total)

    def cmd_ra_cancel(self, parts):
        if not self.require_login():
            return
        if len(parts) < 3:
            self.send_error("Invalid cancel request.")
            return
        kind = parts[1].strip().lower()
        listing_id = self.parse_int(parts[2], None)
        if kind not in ("pokemon", "item") or listing_id is None:
            self.send_error("Invalid cancel request.")
            return

        table = "rocket_pokemon_listings" if kind == "pokemon" else "rocket_item_listings"
        return_column = "blob_b64" if kind == "pokemon" else "item_id || '\t' || item_name_b64 || '\t' || quantity || '\t' || price"
        try:
            self.conn.execute("BEGIN IMMEDIATE")
            row = self.conn.execute(
                f"SELECT seller_account_id, status, {return_column} FROM {table} WHERE id = ?",
                (listing_id,),
            ).fetchone()
            if not row:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing does not exist.")
                return
            seller_account_id, status, return_data = row
            if seller_account_id != self.account_id:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing does not belong to you.")
                return
            if status != "open":
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer open.")
                return
            updated = self.conn.execute(
                f"UPDATE {table} SET status = 'cancelled', updated_at = ? WHERE id = ? AND status = 'open'",
                (utcnow_iso(), listing_id),
            )
            if updated.rowcount != 1:
                self.conn.execute("ROLLBACK")
                self.send_error("That listing is no longer open.")
                return
            self.conn.execute("COMMIT")
        except Exception:
            try:
                self.conn.execute("ROLLBACK")
            except Exception:
                pass
            raise
        log(f"RA Cancel: user={self.username} kind={kind} listing_id={listing_id}")
        if kind == "pokemon":
            self.send_line("OK", "RA_CANCEL", "pokemon", return_data)
        else:
            item_id, item_name_b64, quantity, price = return_data.split("\t", 3)
            self.send_line("OK", "RA_CANCEL", "item", item_id, item_name_b64, quantity, price)


class ThreadedTCPServer(socketserver.ThreadingMixIn, socketserver.TCPServer):
    allow_reuse_address = True
    daemon_threads = True


if __name__ == "__main__":
    openid_http_server = start_openid_http_server()
    log(f"Monsterpatch GTS Server listening on {HOST}:{PORT}")
    with ThreadedTCPServer((HOST, PORT), BankHandler) as server:
        server.serve_forever()
