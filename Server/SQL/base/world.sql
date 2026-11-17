CREATE DATABASE IF NOT EXISTS mmonsterpatch_world;
USE mmonsterpatch_world;

CREATE TABLE IF NOT EXISTS world_manifest_versions (
  manifest_id CHAR(36) NOT NULL PRIMARY KEY,
  patch_version VARCHAR(32) NOT NULL,
  required_game_build VARCHAR(32) NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  notes TEXT NULL
);

CREATE TABLE IF NOT EXISTS world_spawn_instances (
  spawn_id CHAR(36) NOT NULL PRIMARY KEY,
  map_id VARCHAR(128) NOT NULL,
  room_id VARCHAR(128) NULL,
  tile_x INT NOT NULL,
  tile_y INT NOT NULL,
  species_id VARCHAR(128) NOT NULL,
  level INT NOT NULL,
  shiny BOOLEAN NOT NULL DEFAULT FALSE,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  expires_at DATETIME NULL,
  owner_state VARCHAR(32) NULL,
  UNIQUE KEY uq_spawn_tile (map_id, tile_x, tile_y)
);
