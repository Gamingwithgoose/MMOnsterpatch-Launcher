CREATE DATABASE IF NOT EXISTS mmonsterpatch_social;
USE mmonsterpatch_social;

CREATE TABLE IF NOT EXISTS social_friends (
  account_id CHAR(36) NOT NULL,
  friend_account_id CHAR(36) NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (account_id, friend_account_id)
);
