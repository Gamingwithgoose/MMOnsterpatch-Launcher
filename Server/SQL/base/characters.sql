CREATE DATABASE IF NOT EXISTS mmonsterpatch_characters;
USE mmonsterpatch_characters;

CREATE TABLE IF NOT EXISTS characters (
  character_id CHAR(36) NOT NULL PRIMARY KEY,
  account_id CHAR(36) NOT NULL,
  slot_index INT NOT NULL,
  character_name VARCHAR(64) NOT NULL,
  game_version VARCHAR(32) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  deleted_at DATETIME NULL,
  UNIQUE KEY uq_account_slot (account_id, slot_index)
);

CREATE TABLE IF NOT EXISTS character_save_blobs (
  character_id CHAR(36) NOT NULL PRIMARY KEY,
  save_blob LONGBLOB NULL,
  save_json LONGTEXT NULL,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (character_id) REFERENCES characters(character_id)
);
