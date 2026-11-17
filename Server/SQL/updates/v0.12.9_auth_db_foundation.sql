-- MMOnsterpatch v0.12.9 account/auth DB foundation reference.
-- Runtime AccountWebsite uses SQLite at Data/Accounts/auth.db for this test build.

CREATE TABLE IF NOT EXISTS accounts (
  account_id TEXT PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  username TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  password_salt TEXT NOT NULL,
  password_hash TEXT NOT NULL,
  pseudo_steam_id TEXT NOT NULL UNIQUE,
  steam_verified INTEGER NOT NULL DEFAULT 0,
  linked_steam_id64 TEXT NULL,
  created_utc TEXT NOT NULL,
  updated_utc TEXT NOT NULL,
  last_login_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS launcher_sessions (
  session_token TEXT PRIMARY KEY,
  aio_session_token TEXT NOT NULL UNIQUE,
  account_id TEXT NOT NULL,
  username TEXT NOT NULL,
  email TEXT NOT NULL,
  display_name TEXT NOT NULL,
  pseudo_steam_id TEXT NOT NULL,
  source_ip TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  last_seen_utc TEXT NOT NULL,
  expires_utc TEXT NOT NULL,
  revoked_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS game_tickets (
  ticket_token TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  launcher_session_token TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  expires_utc TEXT NOT NULL,
  consumed_utc TEXT NULL
);
