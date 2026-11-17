CREATE DATABASE IF NOT EXISTS mmonsterpatch_tradingpost;
USE mmonsterpatch_tradingpost;

CREATE TABLE IF NOT EXISTS tradingpost_listings (
  listing_id CHAR(36) NOT NULL PRIMARY KEY,
  seller_character_id CHAR(36) NOT NULL,
  offered_payload JSON NOT NULL,
  requested_payload JSON NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  expires_at DATETIME NULL,
  claimed_at DATETIME NULL
);
