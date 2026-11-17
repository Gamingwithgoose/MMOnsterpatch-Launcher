# Future Trading Post Marketplace Merge

Do not treat this as live behavior yet.

Long-term goal: one player exchange system named **Trading Post**.

## Listing types

- Mon for Mon
- SATS for Mon
- Mon for SATS
- Item for SATS
- SATS for Item

## Compatibility rule

For the first migration pass, keep old client protocol commands and table names working:

- `GTS_*` commands remain accepted.
- `RA_*` commands remain accepted.
- New aliases may be accepted by the server, but the current client/patcher can continue using old commands until rebuilt.

## Future table direction

Eventually replace/normalize these old tables:

- `gts_listings`
- `gts_claims`
- `gts_sats_claims`
- `rocket_pokemon_listings`
- `rocket_item_listings`
- `rocket_payouts`

Into a unified Trading Post schema, with offered/requested type columns for `MON`, `ITEM`, and `SATS`.
