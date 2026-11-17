import argparse
import json
import os
import shlex
import mmonsterpatch_db as db
import sys
import threading
import time

import mmonsterpatch_mmo_core as mmo
import mmonsterpatch_tradingpost_core as trading
import mmonsterpatch_social_core as social
import mmonsterpatch_moderation_core as moderation
import mmonsterpatch_world_config as world_config


def env_int(name, default):
    try:
        return int(os.environ.get(name, str(default)))
    except Exception:
        return default


def env_float(name, default):
    try:
        return float(os.environ.get(name, str(default)))
    except Exception:
        return default


def first_env_value(*names):
    for name in names:
        value = os.environ.get(name, "")
        if value and value.strip():
            return value.strip()
    return ""



def _safe_write_json_file(path, payload):
    """Windows-friendly JSON write used by status exports.

    Avoids failing the server loop when AccountWebsite/RepackManager briefly has
    runtime_status.json open while reading it.
    """
    data = json.dumps(payload, indent=2)
    directory = os.path.dirname(os.path.abspath(path))
    os.makedirs(directory, exist_ok=True)
    tmp_path = path + ".tmp"
    last_error = None
    for _ in range(8):
        try:
            with open(tmp_path, "w", encoding="utf-8") as f:
                f.write(data)
            try:
                os.replace(tmp_path, path)
            except PermissionError:
                # If replace is blocked by a reader, fall back to truncating the existing file.
                with open(path, "w", encoding="utf-8") as f:
                    f.write(data)
            return True
        except Exception as ex:
            last_error = ex
            time.sleep(0.05)
    raise last_error

def export_account_characters(social_db_path):
    try:
        data_dir = os.path.join(os.path.dirname(os.path.dirname(__file__)), "data")
        out_path = os.path.join(data_dir, "account_characters.json")
        con = db.connect_social()
        try:
            rows = con.execute("""
                SELECT u.account_uuid, COALESCE(a.steam_id64,''), u.slot_index, u.character_id, u.display_name,
                       u.public_handle, u.status,
                       COALESCE(r.season_id,'season0'), COALESCE(r.rp,0), COALESCE(r.rank,'E'),
                       COALESCE(r.wins,0), COALESCE(r.losses,0), COALESCE(r.draws,0),
                       COALESCE(r.highest_rp,0), COALESCE(r.highest_rank,'E')
                FROM users u
                LEFT JOIN accounts a ON a.account_uuid = u.account_uuid
                LEFT JOIN ranked_profiles r ON r.character_id = u.character_id AND r.season_id = 'season0'
                WHERE u.status='active'
                ORDER BY u.account_uuid, u.slot_index, u.display_name
            """).fetchall()
        except Exception:
            rows = con.execute("""
                SELECT u.account_uuid, COALESCE(a.steam_id64,''), u.slot_index, u.character_id, u.display_name,
                       u.public_handle, u.status,
                       'season0', 0, 'E', 0, 0, 0, 0, 'E'
                FROM users u
                LEFT JOIN accounts a ON a.account_uuid = u.account_uuid
                WHERE u.status='active'
                ORDER BY u.account_uuid, u.slot_index, u.display_name
            """).fetchall()
        finally:
            try: con.close()
            except Exception: pass
        payload = []
        for r in rows:
            pseudo_or_steam = str(r[1] or "")
            account_id = "acct_" + pseudo_or_steam[len("mmpacct_"):] if pseudo_or_steam.startswith("mmpacct_") else ""
            payload.append({
                "accountId": account_id,
                "accountUuid": str(r[0] or ""),
                "steamId64": pseudo_or_steam,
                "slotIndex": int(r[2] or 0),
                "characterId": str(r[3] or ""),
                "displayName": str(r[4] or ""),
                "publicHandle": str(r[5] or ""),
                "status": str(r[6] or "active"),
                "rankProfile": {
                    "seasonId": str(r[7] or "season0"),
                    "seasonName": "Season 0",
                    "rp": int(r[8] or 0),
                    "rank": str(r[9] or "E"),
                    "wins": int(r[10] or 0),
                    "losses": int(r[11] or 0),
                    "draws": int(r[12] or 0),
                    "highestRp": int(r[13] or 0),
                    "highestRank": str(r[14] or "E")
                }
            })
        _safe_write_json_file(out_path, payload)
    except Exception as ex:
        print(f"[account-characters] export failed: {ex}", flush=True)



def runtime_status_loop(status_path, cfg, args):
    """Write live server status for Account Website, launcher, and Repack Manager.

    This is intentionally file-based status export; MariaDB is the v0.15.1 source of truth so the current
    Python server can expose live player counts without introducing another
    local API/service dependency yet.
    """
    data_dir = os.path.dirname(os.path.abspath(status_path))
    os.makedirs(data_dir, exist_ok=True)
    last_players_online = -1

    while True:
        try:
            now = time.time()
            players = []
            try:
                with mmo.players_lock:
                    for player in mmo.players_by_conn.values():
                        # Ignore stale entries; the MMO timeout loop also cleans these up,
                        # but status should not show players that have not checked in.
                        if now - getattr(player, "last_seen", 0.0) > 35.0:
                            continue
                        players.append({
                            "playerId": getattr(player, "player_id", ""),
                            "name": getattr(player, "name", ""),
                            "cluster": getattr(player, "cluster", ""),
                            "scene": getattr(player, "scene", ""),
                            "status": getattr(player, "status", "available"),
                            "lastSeenAgeSeconds": round(now - getattr(player, "last_seen", now), 1),
                        })
            except Exception as ex:
                players = []
                print(f"[runtime-status] Failed reading MMO players: {ex}", flush=True)

            social_clients = 0
            try:
                with social.clients_lock:
                    social_clients = len(social.clients)
            except Exception:
                social_clients = 0

            payload = {
                "online": True,
                "serverName": getattr(cfg, "realm_name", "MMOnsterpatch UnOfficial Server"),
                "serverVersion": getattr(social, "VERSION", "0.11.82"),
                "managerBuild": "v0.12.8",
                "authOnline": True,
                "worldOnline": True,
                "tradingPostOnline": True,
                "socialOnline": True,
                "playersOnline": len(players),
                "socialClients": social_clients,
                "players": players,
                "ports": {
                    "tradingPost": int(args.gts_port),
                    "disabledSteamOpenId": int(args.openid_port),
                    "mmo": int(args.mmo_port),
                    "social": int(args.social_port),
                },
                "urls": {
                    "account": "https://account.gamingwithgoose.com",
                    "monAuth": args.openid_public_base_url,
                    "mmo": "mmo.gamingwithgoose.com",
                    "tradingPost": "mon.gamingwithgoose.com",
                },
                "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                "updatedUnix": now,
            }

            _safe_write_json_file(status_path, payload)
            export_account_characters(args.social_db)

            if len(players) != last_players_online:
                print(f"[runtime-status] playersOnline={len(players)} status={status_path}", flush=True)
                last_players_online = len(players)
        except Exception as ex:
            print(f"[runtime-status] Status write failed: {ex}", flush=True)
        time.sleep(2.0)

def serve_tradingpost(host, gts_port, openid_host, openid_port, openid_public_base_url, openid_realm, steam_web_api_key):
    trading.HOST = host
    trading.PORT = int(gts_port)
    trading.OPENID_HTTP_HOST = openid_host
    trading.OPENID_HTTP_PORT = int(openid_port)
    trading.OPENID_PUBLIC_BASE_URL = (openid_public_base_url or f"http://127.0.0.1:{openid_port}").rstrip("/")
    trading.OPENID_REALM = (openid_realm or trading.OPENID_PUBLIC_BASE_URL).rstrip("/")
    if steam_web_api_key:
        trading.STEAM_WEB_API_KEY = steam_web_api_key.strip()

    trading.log("MMOnsterpatch Trading Post starting")
    trading.log(f"Trading Post socket listening on {trading.HOST}:{trading.PORT}")
    trading.log(f"Steam OpenID callback listening on {trading.OPENID_HTTP_HOST}:{trading.OPENID_HTTP_PORT}")
    trading.log(f"Steam OpenID public base URL: {trading.OPENID_PUBLIC_BASE_URL}")
    trading.log(f"Steam Web API key configured for display names: {'yes' if trading.STEAM_WEB_API_KEY else 'no'}")

    trading.start_openid_http_server()
    with trading.ThreadedTCPServer((trading.HOST, trading.PORT), trading.BankHandler) as server:
        server.serve_forever()


def serve_social(host, social_port, db_path):
    print(f"[Social] Chat/Guild socket listening on {host}:{social_port}")
    print(f"[Social] Database: {db_path}")
    with social.ThreadedTCPServer((host, int(social_port)), social.SocialHandler) as server:
        server.serve_forever()


def serve_mmo(host, mmo_port, snapshot_hz):
    print(f"[MMO] Binding multiplayer socket on {host}:{mmo_port}...")
    try:
        with mmo.ThreadedTCPServer((host, int(mmo_port)), mmo.Handler) as server:
            print(f"[MMO] Multiplayer socket listening on {host}:{mmo_port}")
            threading.Thread(target=mmo.snapshot_loop, args=(float(snapshot_hz),), name="MMOSnapshotLoop", daemon=True).start()
            server.serve_forever()
    except PermissionError:
        print("")
        print("[MMO] FAILED TO BIND MULTIPLAYER PORT")
        print(f"[MMO] Host/port: {host}:{mmo_port}")
        print("[MMO] Keeping your configured port. This usually means Windows/security software denied Python access")
        print("[MMO] or another old server/python process is still holding/reserving the port.")
        print(f"[MMO] Check listeners: netstat -ano | findstr :{mmo_port}")
        print("[MMO] Check excluded ports: netsh interface ipv4 show excludedportrange protocol=tcp")
        print("[MMO] Also try closing old server windows or: taskkill /IM python.exe /F")
        raise



def _admin_print_help():
    print("[Admin] Commands:")
    print("[Admin]   /bansteam <SteamID64> <reason>")
    print("[Admin]   /unbansteam <SteamID64> <reason>")
    print("[Admin]   /bancheck <SteamID64>")
    print("[Admin]   /banlist")
    print("[Admin]   /system <message>")
    print("[Admin]   /givemon <Player#1234|ALL> <mon name or id> [level] [Shiny]")
    print("[Admin]   /giveitem <Player#1234|ALL> <item name or id> [amount]")
    print("[Admin]   /givesats <Player#1234|ALL> <amount>")
    print("[Admin]   /givewin <Player#1234> [rp] [reason]")
    print("[Admin]   /giveloss <Player#1234> [rp] [reason]")
    print("[Admin]   /givedraw <Player#1234> [rp] [reason]")
    print("[Admin]   /giverp <Player#1234> <amount> [reason]")
    print("[Admin]   /pvphistory <Player#1234> [limit]")
    print("[Admin]   /adminhelp")


def admin_console_loop():
    while True:
        try:
            raw = sys.stdin.readline()
            if not raw:
                time.sleep(1.0)
                continue
            line = raw.strip()
            if not line:
                continue
            try:
                parts = shlex.split(line)
            except Exception as ex:
                print(f"[Admin] Could not parse command: {ex}")
                continue
            if not parts:
                continue
            cmd = parts[0].lower()
            if not cmd.startswith('/'):
                cmd = '/' + cmd
            try:
                if cmd in ('/adminhelp', '/help'):
                    _admin_print_help()
                    continue
                if cmd == '/bansteam':
                    if len(parts) < 2:
                        print('[Admin] Usage: /bansteam <SteamID64> <reason>')
                        continue
                    steam_id64 = moderation.normalize_steam_id64(parts[1])
                    reason = ' '.join(parts[2:]).strip() or 'No reason provided.'
                    ban = moderation.ban_steam(steam_id64, reason=reason, banned_by='server-console')
                    revoked = trading.revoke_aio_sessions_for_steam(steam_id64)
                    social_disc = social.disconnect_active_banned_steam(steam_id64)
                    trade_disc = trading.disconnect_active_steam_sessions(steam_id64)
                    print(f"[Admin] BANNED SteamID64={ban['steam_id64']} ban_id={ban['ban_id']} reason={ban['reason']}")
                    print(f"[Admin] Revoked AIO sessions: {revoked}; disconnected social={social_disc}; trading={trade_disc}")
                    continue
                if cmd == '/unbansteam':
                    if len(parts) < 2:
                        print('[Admin] Usage: /unbansteam <SteamID64> <reason>')
                        continue
                    steam_id64 = moderation.normalize_steam_id64(parts[1])
                    reason = ' '.join(parts[2:]).strip() or 'Unbanned by admin.'
                    count = moderation.unban_steam(steam_id64, unban_reason=reason, unbanned_by='server-console')
                    print(f"[Admin] UNBANNED SteamID64={steam_id64}; active bans cleared={count}; reason={reason}")
                    continue
                if cmd == '/bancheck':
                    if len(parts) < 2:
                        print('[Admin] Usage: /bancheck <SteamID64>')
                        continue
                    steam_id64 = moderation.normalize_steam_id64(parts[1])
                    ban = moderation.get_active_ban(steam_id64=steam_id64)
                    if not ban:
                        print(f"[Admin] SteamID64={steam_id64} is not actively banned.")
                    else:
                        print(f"[Admin] SteamID64={steam_id64} ACTIVE BAN ban_id={ban['ban_id']} reason={ban['reason']} banned_by={ban['banned_by']} banned_at={ban['banned_at']}")
                    continue
                if cmd == '/banlist':
                    bans = moderation.list_active_bans(50)
                    if not bans:
                        print('[Admin] No active SteamID bans.')
                    else:
                        print(f"[Admin] Active SteamID bans ({len(bans)} shown):")
                        for ban in bans:
                            print(f"[Admin]   #{ban['ban_id']} steam={ban['steam_id64']} account={ban['account_uuid']} reason={ban['reason']} by={ban['banned_by']} at={ban['banned_at']}")
                    continue
                if cmd == '/system':
                    msg = line[len('/system'):].strip()
                    if not msg:
                        print('[Admin] Usage: /system message')
                    else:
                        social.send_system_global_message(msg)
                        print('[Admin] System global message sent.')
                    continue
                if cmd == '/givemon':
                    if len(parts) < 3:
                        print('[Admin] Usage: /givemon <Player#1234|ALL> <mon name or id> [level] [Shiny]')
                        continue
                    target = parts[1]
                    level = 1
                    shiny = False
                    mon_parts = list(parts[2:])
                    while mon_parts:
                        tail = str(mon_parts[-1]).strip().lower()
                        if tail in ('shiny', 'shiny=true', 'true', 'sparkle', 'sparkly'):
                            shiny = True
                            mon_parts = mon_parts[:-1]
                            continue
                        try:
                            level = int(mon_parts[-1])
                            mon_parts = mon_parts[:-1]
                            continue
                        except Exception:
                            pass
                        break
                    mon_spec = ' '.join(mon_parts).strip()
                    ok, msg = trading.send_admin_reward_mail(target, 'MON', mon_spec, amount=1, level=level, shiny=shiny)
                    print('[Admin] ' + str(msg))
                    continue
                if cmd == '/giveitem':
                    if len(parts) < 3:
                        print('[Admin] Usage: /giveitem <Player#1234|ALL> <item name or id> [amount]')
                        continue
                    target = parts[1]
                    amount = 1
                    item_parts = list(parts[2:])
                    if len(item_parts) >= 2:
                        try:
                            amount = int(item_parts[-1])
                            item_parts = item_parts[:-1]
                        except Exception:
                            amount = 1
                    item_spec = ' '.join(item_parts).strip()
                    ok, msg = trading.send_admin_reward_mail(target, 'ITEM', item_spec, amount=amount)
                    print('[Admin] ' + str(msg))
                    continue
                if cmd == '/givesats':
                    if len(parts) < 3:
                        print('[Admin] Usage: /givesats <Player#1234|ALL> <amount>')
                        continue
                    target = parts[1]
                    try:
                        amount = int(parts[2])
                    except Exception:
                        amount = 0
                    ok, msg = trading.send_admin_reward_mail(target, 'SATS', 'SATS', amount=amount)
                    print('[Admin] ' + str(msg))
                    continue
                if cmd in ('/givewin', '/giveloss', '/givedraw'):
                    if len(parts) < 2:
                        print(f'[Admin] Usage: {cmd} <Player#1234> [rp] [reason]')
                        continue
                    target = parts[1]
                    delta = None
                    reason_parts = list(parts[2:])
                    if reason_parts:
                        try:
                            delta = int(float(reason_parts[0]))
                            reason_parts = reason_parts[1:]
                        except Exception:
                            delta = None
                    reason = ' '.join(reason_parts).strip() or 'server console test result'
                    result = cmd.replace('/give', '')
                    ok, msg = social.admin_apply_pvp_result(target, result, delta, reason=reason, created_by='server-console')
                    print('[Admin] ' + str(msg))
                    continue
                if cmd == '/giverp':
                    if len(parts) < 3:
                        print('[Admin] Usage: /giverp <Player#1234> <amount> [reason]')
                        continue
                    target = parts[1]
                    try:
                        delta = int(float(parts[2]))
                    except Exception:
                        print('[Admin] Usage: /giverp <Player#1234> <amount> [reason]')
                        continue
                    reason = ' '.join(parts[3:]).strip() or 'server console RP adjustment'
                    ok, msg = social.admin_apply_pvp_result(target, 'rp', delta, reason=reason, created_by='server-console')
                    print('[Admin] ' + str(msg))
                    continue
                if cmd == '/pvphistory':
                    if len(parts) < 2:
                        print('[Admin] Usage: /pvphistory <Player#1234> [limit]')
                        continue
                    limit = 10
                    if len(parts) >= 3:
                        try:
                            limit = int(parts[2])
                        except Exception:
                            limit = 10
                    ok, lines = social.admin_get_pvp_history(parts[1], limit)
                    for history_line in lines:
                        print('[Admin] ' + str(history_line))
                    continue
                print(f"[Admin] Unknown command: {line}")
            except Exception as ex:
                print(f"[Admin] Command failed: {ex}")
        except Exception as ex:
            print(f"[Admin] Console loop error: {ex}")
            time.sleep(1.0)

def main():
    parser = argparse.ArgumentParser(description="MMOnsterpatch Official World Server")
    parser.add_argument("--config", default=os.environ.get("MMONSTERPATCH_WORLD_CONFIG", world_config.default_config_path()))
    parser.add_argument("--host", default=None)
    parser.add_argument("--mmo-port", type=int, default=None)
    parser.add_argument("--gts-port", type=int, default=None)
    parser.add_argument("--openid-host", default=None)
    parser.add_argument("--openid-port", type=int, default=None)
    parser.add_argument("--openid-public-base-url", default=None)
    parser.add_argument("--openid-realm", default=None)
    parser.add_argument("--steam-web-api-key", default=None)
    parser.add_argument("--social-port", type=int, default=None)
    parser.add_argument("--social-db", default=None)
    parser.add_argument("--snapshot-hz", type=float, default=None)
    args = parser.parse_args()

    requested_config_path = os.path.abspath(os.path.expandvars(os.path.expanduser(args.config or world_config.default_config_path())))
    print(f"[WorldConfig] Requested config: {requested_config_path}", flush=True)
    if not os.path.exists(requested_config_path):
        print(f"[WorldConfig] ERROR: world config file was not found: {requested_config_path}", flush=True)
        print("[WorldConfig] The server will not start with silent default rates. Check Start-WorldServer.bat and edit Server\\configs\\worldserver.ini.", flush=True)
        sys.exit(2)

    cfg = world_config.load_world_config(requested_config_path)
    try:
        mtime = time.strftime('%Y-%m-%d %H:%M:%S', time.localtime(os.path.getmtime(requested_config_path)))
        print(f"[WorldConfig] Loaded config: {cfg.config_path} ; modified={mtime}", flush=True)
        print("[WorldConfig] Parsed rates: "
              f"EXP={cfg.exp_rate}, SATS={cfg.sats_rate}, ShinyOddsDenom={cfg.shiny_odds_denominator}, "
              f"Catch={cfg.catch_rate}, ItemDrop={cfg.item_drop_rate}, "
              f"RandomEncounter={cfg.random_encounter_rate}, VisibleSpawn={cfg.visible_spawn_rate}, "
              f"RewardSpawn={cfg.reward_spawn_rate}, RPGain={cfg.rp_gain_rate}, RPLoss={cfg.rp_loss_rate}", flush=True)
    except Exception as ex:
        print(f"[WorldConfig] Loaded config, but startup summary failed: {ex}", flush=True)

    # Command-line args remain available for emergency overrides, but the
    # intended server-owner workflow is configs/worldserver.ini.
    args.host = args.host or os.environ.get("MMONSTERPATCH_HOST", os.environ.get("PBO_HOST", cfg.host))
    args.mmo_port = args.mmo_port or env_int("MMO_PORT", cfg.mmo_port)
    args.gts_port = args.gts_port or env_int("PBO_PORT", cfg.trading_post_port)
    args.openid_host = args.openid_host or os.environ.get("PBO_OPENID_HTTP_HOST", cfg.openid_http_host)
    args.openid_port = args.openid_port or env_int("PBO_OPENID_HTTP_PORT", cfg.openid_http_port)
    args.openid_public_base_url = args.openid_public_base_url or os.environ.get("PBO_OPENID_PUBLIC_BASE_URL", cfg.openid_public_base_url)
    args.openid_realm = args.openid_realm or os.environ.get("PBO_OPENID_REALM", cfg.openid_realm)
    args.steam_web_api_key = args.steam_web_api_key or first_env_value("STEAM_WEB_API_KEY", "PBO_STEAM_WEB_API_KEY", "PBO_STEAM_API_KEY", "STEAM_API_KEY") or cfg.steam_web_api_key
    args.social_port = args.social_port or env_int("SOCIAL_PORT", cfg.social_port)
    args.social_db = "social"
    args.snapshot_hz = args.snapshot_hz or env_float("MMO_SNAPSHOT_HZ", cfg.snapshot_hz)

    world_config.apply_world_config(cfg, mmo_module=mmo, social_module=social, trading_module=trading)
    social.init_db(args.social_db)
    trading.SOCIAL_DB_PATH = "social"

    print("MMOnsterpatch Official Server v0.11.82")
    print(f"Realm: {cfg.realm_name} ({cfg.realm_type})")
    print(f"World config: {cfg.config_path}")
    print("One process serving:")
    print(f"  MMO multiplayer TCP:       {args.host}:{args.mmo_port}")
    print(f"  Social chat/guild TCP:     {args.host}:{args.social_port}")
    print(f"  Trading Post/GTS TCP:      {args.host}:{args.gts_port}")
    print(f"  Steam OpenID HTTP:         {args.openid_host}:{args.openid_port}")
    print(f"  Steam OpenID public URL:   {args.openid_public_base_url}")
    print(f"  Steam display names:       {'enabled' if args.steam_web_api_key else 'fallback only - no Steam Web API key'}")
    print("  Social database:           MariaDB:mmonsterpatch_social")
    print("  Moderation bans database:  MariaDB:mmonsterpatch_social")
    print(f"  Social server version:     {social.VERSION}")
    print("Active world rates:")
    print(f"  EXP Rate:                  {cfg.exp_rate}")
    print(f"  SATS Rate:                 {cfg.sats_rate}")
    print(f"  Shiny Odds Denominator:    {cfg.shiny_odds_denominator} (1 in {cfg.shiny_odds_denominator})")
    print(f"  Catch Rate:                {cfg.catch_rate}")
    print(f"  Item Drop Chance Rate:     {cfg.item_drop_rate}")
    print(f"  Random Encounter Rate:     {cfg.random_encounter_rate}")
    print(f"  Visible Spawn Rate:        {cfg.visible_spawn_rate} -> {cfg.visible_spawn_chance_percent}% per empty deduped spawn tile")
    print(f"  RP Gain/Loss Rate:         {cfg.rp_gain_rate} / {cfg.rp_loss_rate}")
    print(f"  AIO Session Hours:         {cfg.aio_session_hours}; same IP required={cfg.require_same_ip_for_session}")
    print("Ctrl+C to stop.")
    print("[Admin] Console ready. Type /adminhelp for commands.")

    runtime_status_path = os.path.join(os.path.dirname(os.path.dirname(__file__)), "data", "runtime_status.json")
    print(f"  Runtime status file:       {runtime_status_path}")
    threading.Thread(
        target=runtime_status_loop,
        args=(runtime_status_path, cfg, args),
        name="RuntimeStatusWriter",
        daemon=True,
    ).start()

    admin_thread = threading.Thread(target=admin_console_loop, name="AdminConsole", daemon=True)
    admin_thread.start()

    t = threading.Thread(
        target=serve_tradingpost,
        args=(args.host, args.gts_port, args.openid_host, args.openid_port, args.openid_public_base_url, args.openid_realm, args.steam_web_api_key),
        name="TradingPostServer",
        daemon=True,
    )
    t.start()

    sthread = threading.Thread(
        target=serve_social,
        args=(args.host, args.social_port, args.social_db),
        name="SocialServer",
        daemon=True,
    )
    sthread.start()

    try:
        serve_mmo(args.host, args.mmo_port, args.snapshot_hz)
    except KeyboardInterrupt:
        print("\nMMOnsterpatch server stopping.")


if __name__ == "__main__":
    main()
