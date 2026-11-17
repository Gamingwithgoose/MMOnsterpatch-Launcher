-- Non-destructive future compatibility idea for SQLite only.
-- DO NOT RUN LIVE YET unless we intentionally switch code to these names.
-- These views preserve old tables while giving future code new names for reads.

CREATE VIEW IF NOT EXISTS trading_post_listings AS SELECT * FROM gts_listings;
CREATE VIEW IF NOT EXISTS trading_post_claims AS SELECT * FROM gts_claims;
CREATE VIEW IF NOT EXISTS trading_post_sats_claims AS SELECT * FROM gts_sats_claims;
CREATE VIEW IF NOT EXISTS trading_post_mon_market_listings AS SELECT * FROM rocket_pokemon_listings;
CREATE VIEW IF NOT EXISTS trading_post_item_market_listings AS SELECT * FROM rocket_item_listings;
CREATE VIEW IF NOT EXISTS trading_post_market_payouts AS SELECT * FROM rocket_payouts;
