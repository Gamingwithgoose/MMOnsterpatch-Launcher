CREATE DATABASE IF NOT EXISTS mmonsterpatch_auth;
USE mmonsterpatch_auth;

CREATE TABLE IF NOT EXISTS auth_accounts (
  account_id CHAR(36) NOT NULL PRIMARY KEY,
  email VARCHAR(255) NOT NULL UNIQUE,
  display_name VARCHAR(64) NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NULL
);

CREATE TABLE IF NOT EXISTS auth_account_credentials (
  account_id CHAR(36) NOT NULL PRIMARY KEY,
  password_hash VARBINARY(255) NULL,
  password_updated_at DATETIME NULL,
  FOREIGN KEY (account_id) REFERENCES auth_accounts(account_id)
);

CREATE TABLE IF NOT EXISTS auth_account_profiles (
  account_id CHAR(36) NOT NULL PRIMARY KEY,
  avatar_id CHAR(36) NULL,
  display_name VARCHAR(64) NOT NULL,
  avatar_updated_at DATETIME NULL,
  FOREIGN KEY (account_id) REFERENCES auth_accounts(account_id)
);

CREATE TABLE IF NOT EXISTS auth_steam_links (
  account_id CHAR(36) NOT NULL,
  steam_id VARCHAR(32) NOT NULL UNIQUE,
  linked_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (account_id, steam_id),
  FOREIGN KEY (account_id) REFERENCES auth_accounts(account_id)
);

CREATE TABLE IF NOT EXISTS auth_launcher_sessions (
  session_id CHAR(36) NOT NULL PRIMARY KEY,
  account_id CHAR(36) NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  expires_at DATETIME NOT NULL,
  revoked_at DATETIME NULL,
  FOREIGN KEY (account_id) REFERENCES auth_accounts(account_id)
);

CREATE TABLE IF NOT EXISTS auth_game_tickets (
  ticket_id CHAR(36) NOT NULL PRIMARY KEY,
  account_id CHAR(36) NOT NULL,
  character_id CHAR(36) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  expires_at DATETIME NOT NULL,
  consumed_at DATETIME NULL,
  FOREIGN KEY (account_id) REFERENCES auth_accounts(account_id)
);

CREATE TABLE IF NOT EXISTS server_schema_updates (
  update_id VARCHAR(128) NOT NULL PRIMARY KEY,
  filename VARCHAR(255) NOT NULL,
  checksum_sha256 CHAR(64) NOT NULL,
  applied_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);
