#!/usr/bin/env python3
import argparse
import os
import socketserver
import threading
import time
import random
import uuid
from dataclasses import dataclass, field
from urllib.parse import quote, unquote

try:
    import mmonsterpatch_social_core as social
except Exception:
    social = None

@dataclass
class PlayerState:
    conn_id: str
    player_id: str
    name: str
    cluster: str
    scene: str
    x: float
    y: float
    z: float
    facing: int
    moving: bool
    design: int
    color1: int
    color2: int
    status: str
    follower_enabled: bool = False
    follower_x: float = 0.0
    follower_y: float = 0.0
    follower_facing: int = 0
    follower_moving: bool = False
    follower_sprite: str = ""
    follower_flip_x: bool = False
    last_seen: float = 0.0
    riding_broom: bool = False
    water_walking: bool = False
    jumping_or_bouncing: bool = False
    bouncing_name: str = ""

@dataclass
class WorldSpawn:
    spawn_id: str
    cluster: str
    scene: str
    x: float
    y: float
    z: float
    mon_id: int
    mon_key: str
    shiny: bool
    level: int
    state: str = "available"
    owner_id: str = ""
    created_at: float = 0.0
    updated_at: float = 0.0
    zone_id: str = ""
    lock_state: str = "public"          # public | owner_only
    personal_owner_id: str = ""        # player who owns a personal encounter reward spawn
    mon_save_b64: str = ""             # optional exact Mon save payload for personal encounter reward spawns
    version_requirement: str = "None"   # None | Skyfarer | Aurora | Eldenwood | unknown
    zone_type: str = "grass"             # grass | water

@dataclass
class MapSpawnState:
    cluster: str
    scene: str
    zones: list = field(default_factory=list)
    signature: str = ""
    last_zone_report: float = 0.0
    last_spawn_pass: float = 0.0
    no_players_since: float = 0.0
    next_spawn_due_at: float = 0.0

@dataclass
class PendingWorldSpawnRequest:
    request_id: str
    cluster: str
    scene: str
    zone_id: str
    x: float
    y: float
    z: float
    rarity: str
    requested_at: float
    requester_conn_id: str = ""
    zone_type: str = "grass"

players_lock = threading.Lock()
players_by_conn = {}
handlers_by_conn = {}

ranked_sessions_lock = threading.RLock()
ranked_battle_sessions = {}

# ---------------------------------------------------------------------------
# Server-owned visible overworld spawns
# ---------------------------------------------------------------------------
# Official Online Mode uses a server-owned visible spawn chance per spawn tile.
# Default is tuned to 40% so online maps feel more active than vanilla 25%. Clients
# only report/dedupe map spawnZone positions and generate a species/level payload when
# the server asks; clients do not control rate, caps, or timing.
world_spawns_lock = threading.RLock()
world_spawns_by_id = {}
world_map_states = {}
pending_world_spawn_requests = {}
world_spawn_seq = 0
world_spawn_request_seq = 0
world_spawn_snapshot_cache_by_conn = {}

VISIBLE_WORLD_SPAWNS_ENABLED = True
VANILLA_VISIBLE_SPAWN_CHANCE_PERCENT = 40
SERVER_VISIBLE_SPAWN_MULTIPLIER = 1
SERVER_VISIBLE_SPAWN_CHANCE_PERCENT = min(100, VANILLA_VISIBLE_SPAWN_CHANCE_PERCENT * SERVER_VISIBLE_SPAWN_MULTIPLIER)
MAP_NO_PLAYER_DESPAWN_SECONDS = 8.0
SPAWN_TOPUP_INTERVAL_SECONDS = 20.0
SPAWN_REFILL_DELAY_SECONDS = 12.0
VISIBLE_SPAWN_EXTRA_PLAYER_DENSITY_PERCENT = 8
VISIBLE_SPAWN_MAX_DENSITY_PERCENT = 70
VISIBLE_SPAWN_MINIMUM_PER_ACTIVE_MAP = 1
MAX_REPORTED_SPAWN_ZONES = 128
PENDING_SPAWN_REQUEST_TIMEOUT_SECONDS = 12.0
NO_PERMISSION_CAPTURE_MESSAGE = "You don't have permission to capture this MoN."

def enc(s): return quote(str(s), safe="")
def dec(s): return unquote(s)
def parse_bool(s): return s == "1" or s.lower() == "true"

def fmt_player(p):
    return "|".join([
        enc(p.player_id), enc(p.name), enc(p.cluster), enc(p.scene),
        f"{p.x:.4f}", f"{p.y:.4f}", f"{p.z:.4f}",
        str(int(p.facing)), "1" if p.moving else "0",
        str(int(p.design)), str(int(p.color1)), str(int(p.color2)), enc(getattr(p, "status", "available")),
        "1" if getattr(p, "follower_enabled", False) else "0",
        f"{getattr(p, 'follower_x', 0.0):.4f}",
        f"{getattr(p, 'follower_y', 0.0):.4f}",
        str(int(getattr(p, "follower_facing", 0))),
        "1" if getattr(p, "follower_moving", False) else "0",
        enc(getattr(p, "follower_sprite", "")),
        "1" if getattr(p, "follower_flip_x", False) else "0",
        "1" if getattr(p, "riding_broom", False) else "0",
        "1" if getattr(p, "water_walking", False) else "0",
        "1" if getattr(p, "jumping_or_bouncing", False) else "0",
        enc(getattr(p, "bouncing_name", "") or ""),
    ])

def make_snapshot(for_conn_id):
    # WoW-like presence: do not remove players from world snapshots just because
    # last_seen is old. Real socket disconnect/finish handles actual removal;
    # future AFK handling can mark status without despawning the player/follower.
    with players_lock:
        me = players_by_conn.get(for_conn_id)
        if not me:
            return "SNAP|"

        remotes = [
            p for cid, p in players_by_conn.items()
            if cid != for_conn_id and p.cluster == me.cluster and p.scene == me.scene and p.scene not in ("", "unknown")
        ]

    return "SNAP|" + ";".join(fmt_player(p) for p in remotes)

def fmt_spawn(s):
    return "|".join([
        enc(s.spawn_id), enc(s.cluster), enc(s.scene),
        f"{s.x:.4f}", f"{s.y:.4f}", f"{s.z:.4f}",
        str(int(s.mon_id)), "1" if s.shiny else "0", str(int(s.level)),
        enc(s.state), enc(s.owner_id or ""), enc(getattr(s, "mon_key", "") or ""),
        enc(getattr(s, "lock_state", "public") or "public"),
        enc(getattr(s, "personal_owner_id", "") or ""),
        enc(getattr(s, "mon_save_b64", "") or ""),
        enc(getattr(s, "version_requirement", "None") or "None"),
        enc(getattr(s, "zone_type", "grass") or "grass")
    ])

def visible_world_spawns_for_conn(conn_id):
    with players_lock:
        me = players_by_conn.get(conn_id)
    if not me or me.scene in ("", "unknown"):
        return []

    with world_spawns_lock:
        return [
            s for s in world_spawns_by_id.values()
            if s.cluster == me.cluster
            and s.scene == me.scene
            and s.state in ("available", "busy")
        ]

def make_world_spawn_snapshot(conn_id):
    spawns = visible_world_spawns_for_conn(conn_id)
    return "WORLD_SPAWNS|" + ";".join(fmt_spawn(s) for s in spawns)

def maybe_make_world_spawn_snapshot(conn_id, now=None, repair_interval=2.0):
    """Send unchanged world-spawn snapshots slowly; send changed snapshots immediately."""
    if now is None:
        now = time.time()

    line = make_world_spawn_snapshot(conn_id)
    last_line, last_sent = world_spawn_snapshot_cache_by_conn.get(conn_id, ("", 0.0))
    if line != last_line or now - last_sent >= repair_interval:
        world_spawn_snapshot_cache_by_conn[conn_id] = (line, now)
        return line
    return None

def broadcast_world_spawn_remove(spawn, exclude_conn_id=None):
    if spawn is None:
        return
    with players_lock:
        targets = [
            cid for cid, p in players_by_conn.items()
            if p.cluster == spawn.cluster and p.scene == spawn.scene
            and (exclude_conn_id is None or cid != exclude_conn_id)
        ]
    line = "WORLD_SPAWN_REMOVE|" + enc(spawn.spawn_id)
    for cid in targets:
        send_to_conn(cid, line)

def broadcast_world_spawn_add(spawn, exclude_conn_id=None):
    if spawn is None:
        return
    with players_lock:
        targets = [
            cid for cid, p in players_by_conn.items()
            if p.cluster == spawn.cluster and p.scene == spawn.scene
            and (exclude_conn_id is None or cid != exclude_conn_id)
        ]
    line = "WORLD_SPAWN_ADD|" + fmt_spawn(spawn)
    for cid in targets:
        send_to_conn(cid, line)

def broadcast_world_spawn_lock_update(spawn, reason="OWNER_LEFT_MAP", exclude_conn_id=None):
    if spawn is None:
        return
    with players_lock:
        targets = [
            cid for cid, p in players_by_conn.items()
            if p.cluster == spawn.cluster and p.scene == spawn.scene
            and (exclude_conn_id is None or cid != exclude_conn_id)
        ]
    line = "WORLD_SPAWN_LOCK_UPDATE|" + "|".join([
        enc(spawn.spawn_id),
        enc(getattr(spawn, "lock_state", "public") or "public"),
        enc(getattr(spawn, "personal_owner_id", "") or ""),
        enc(reason or ""),
    ])
    for cid in targets:
        send_to_conn(cid, line)

def world_spawn_version_requirement_is_shared(spawn):
    try:
        req = (getattr(spawn, "version_requirement", "") or "").strip().lower()
    except Exception:
        req = ""
    # VersionRequirement.None means the encounter entry is shared by all versions.
    # Unknown/blank personal reward metadata is treated as not safely shareable,
    # so it will be deleted instead of unlocked when the owner leaves the map.
    return req in ("none", "shared", "all", "both")

def unlock_personal_spawns_for_owner_left_map(player_id, cluster, scene, reason="OWNER_LEFT_MAP"):
    if not player_id or not cluster or scene in ("", "unknown"):
        return
    removed = []
    with world_spawns_lock:
        for sid, spawn in list(world_spawns_by_id.items()):
            if spawn.cluster != cluster or spawn.scene != scene:
                continue
            if getattr(spawn, "lock_state", "public") != "owner_only":
                continue
            if (getattr(spawn, "personal_owner_id", "") or "") != player_id:
                continue

            # Prize/personal encounter mons are not shared world population.
            # If the owner logs off or leaves the map, delete them instead of
            # unlocking them to public; otherwise another player can inherit a
            # reward spawn that belonged to the owner's crystal.
            spawn.state = "despawned"
            spawn.updated_at = time.time()
            removed.append(spawn)
            world_spawns_by_id.pop(sid, None)

    for spawn in removed:
        print(f"[personal-spawn-delete] spawn={spawn.spawn_id} owner={player_id} scene={scene} versionRequirement={getattr(spawn, 'version_requirement', '')} reason={reason}; owner left map/disconnected, deleting owner-only prize spawn")
        broadcast_world_spawn_remove(spawn)

def world_spawn_is_locked_for_player(spawn, player_id):
    if spawn is None:
        return False
    if getattr(spawn, "lock_state", "public") != "owner_only":
        return False
    owner = getattr(spawn, "personal_owner_id", "") or ""
    return bool(owner and owner != (player_id or ""))

def broadcast_world_spawn_catch_start(spawn, owner_player_id, exclude_conn_id=None):
    if spawn is None:
        return
    with players_lock:
        targets = [
            cid for cid, p in players_by_conn.items()
            if p.cluster == spawn.cluster and p.scene == spawn.scene
            and (exclude_conn_id is None or cid != exclude_conn_id)
        ]
    line = "WORLD_SPAWN_CATCH_START|" + enc(owner_player_id or "") + "|" + enc(spawn.spawn_id)
    for cid in targets:
        send_to_conn(cid, line)

def broadcast_world_spawn_catch_end(spawn, owner_player_id, result, exclude_conn_id=None):
    if spawn is None:
        return
    with players_lock:
        targets = [
            cid for cid, p in players_by_conn.items()
            if p.cluster == spawn.cluster and p.scene == spawn.scene
            and (exclude_conn_id is None or cid != exclude_conn_id)
        ]
    line = "WORLD_SPAWN_CATCH_END|" + enc(owner_player_id or "") + "|" + enc(spawn.spawn_id) + "|" + enc(result or "")
    for cid in targets:
        send_to_conn(cid, line)

def map_key(cluster, scene):
    return (cluster or "", scene or "")

def active_player_count_for_map(cluster, scene):
    with players_lock:
        return len([
            p for p in players_by_conn.values()
            if p.cluster == cluster and p.scene == scene and p.scene not in ("", "unknown")
        ])

def choose_world_spawn_generator(cluster, scene, preferred_conn_id=None):
    with players_lock:
        if preferred_conn_id:
            p = players_by_conn.get(preferred_conn_id)
            if p and p.cluster == cluster and p.scene == scene:
                return preferred_conn_id
        for cid, p in players_by_conn.items():
            if p.cluster == cluster and p.scene == scene and p.scene not in ("", "unknown"):
                return cid
    return None

def world_spawn_tile_key(x, y):
    # Tile/position identity used by server-owned visible spawns.
    # Use whole-map-tile identity, not sub-tile float identity, so the server never
    # allows two visible MoNs to occupy the same square at the same time.
    try:
        return f"{int(round(float(x)))}:{int(round(float(y)))}"
    except Exception:
        return ""

def is_personal_world_spawn(spawn):
    try:
        if spawn is None:
            return False
        sid = str(getattr(spawn, "spawn_id", "") or "")
        if sid.lower().startswith("owp-"):
            return True
        return (getattr(spawn, "lock_state", "public") or "public") == "owner_only" or bool(getattr(spawn, "personal_owner_id", "") or "")
    except Exception:
        return False

def normalize_world_spawn_zone_type(value):
    zt = str(value or "grass").strip().lower()
    if zt in ("water", "spawnzonewater", "spawn_zone_water"):
        return "water"
    return "grass"

def parse_world_spawn_zones(zone_body):
    zones = []
    seen_tiles = set()
    if not zone_body:
        return zones
    records = [r for r in zone_body.split(";") if r.strip()]
    for rec in records[:MAX_REPORTED_SPAWN_ZONES]:
        parts = rec.split(",")
        if len(parts) < 4:
            continue
        try:
            client_zone_id = dec(parts[0]).strip()
            x = float(parts[1])
            y = float(parts[2])
            z = float(parts[3])
            zone_type = normalize_world_spawn_zone_type(parts[4] if len(parts) > 4 else "grass")
            tile_key = world_spawn_tile_key(x, y) or client_zone_id or str(len(zones))
            # One visible server-owned spawn per map tile. If grass/water objects
            # overlap the same tile, the first reported zone wins and prevents stacking.
            if tile_key in seen_tiles:
                continue
            seen_tiles.add(tile_key)
            zones.append({"zone_id": tile_key, "client_zone_id": client_zone_id, "x": x, "y": y, "z": z, "zone_type": zone_type})
        except Exception:
            continue
    return zones

def get_occupied_world_spawn_zone_ids(cluster, scene):
    occupied = set()
    for s in world_spawns_by_id.values():
        if is_personal_world_spawn(s):
            # Personal prize spawns are visible world entities, but they do not reserve
            # normal public spawn-zone population or hide/replace regular ow-* spawns.
            continue
        if s.cluster == cluster and s.scene == scene and s.state in ("available", "busy"):
            occupied.add(getattr(s, "zone_id", "") or "")
    for req in pending_world_spawn_requests.values():
        if req.cluster == cluster and req.scene == scene:
            occupied.add(req.zone_id or "")
    occupied.discard("")
    return occupied

def roll_visible_world_spawn_rarity():
    # Official visible overworld table from WildMonSpawnManager:
    # rand < 5 = rare, rand >= 35 = common, otherwise uncommon.
    r = random.randrange(0, 100)
    if r < 5:
        return "rare"
    if r >= 35:
        return "common"
    return "uncommon"

def count_world_spawn_population(cluster, scene):
    active = 0
    pending = 0
    for s in world_spawns_by_id.values():
        if is_personal_world_spawn(s):
            continue
        if s.cluster == cluster and s.scene == scene and s.state in ("available", "busy"):
            active += 1
    for req in pending_world_spawn_requests.values():
        if req.cluster == cluster and req.scene == scene:
            pending += 1
    return active, pending

def target_world_spawn_count(zone_count, player_count):
    try:
        zc = max(0, int(zone_count))
        pc = max(0, int(player_count))
    except Exception:
        return 0
    if zc <= 0 or pc <= 0:
        return 0
    density = float(SERVER_VISIBLE_SPAWN_CHANCE_PERCENT) + max(0, pc - 1) * float(VISIBLE_SPAWN_EXTRA_PLAYER_DENSITY_PERCENT)
    density = max(0.0, min(float(VISIBLE_SPAWN_MAX_DENSITY_PERCENT), density))
    target = int(round((zc * density) / 100.0))
    target = max(int(VISIBLE_SPAWN_MINIMUM_PER_ACTIVE_MAP), target)
    return max(0, min(zc, target))

def schedule_world_spawn_refill(cluster, scene, delay_seconds=None):
    key = map_key(cluster, scene)
    state = world_map_states.get(key)
    if not state:
        return
    delay = SPAWN_REFILL_DELAY_SECONDS if delay_seconds is None else float(delay_seconds)
    state.next_spawn_due_at = min(state.next_spawn_due_at or 0.0, time.time() + delay) if state.next_spawn_due_at else time.time() + delay
    # Let the population controller run soon even if a normal top-up pass just happened.
    if state.last_spawn_pass > 0:
        state.last_spawn_pass = min(state.last_spawn_pass, time.time() - max(0.0, SPAWN_TOPUP_INTERVAL_SECONDS - delay))

def clear_world_spawn_map_if_empty(cluster, scene, reason="MAP_EMPTY"):
    if not cluster and not scene:
        return False
    if scene in ("", "unknown"):
        return False
    if active_player_count_for_map(cluster, scene) > 0:
        return False
    key = map_key(cluster, scene)
    removed_spawns = 0
    removed_requests = 0
    with world_spawns_lock:
        for rid, req in list(pending_world_spawn_requests.items()):
            if req.cluster == cluster and req.scene == scene:
                pending_world_spawn_requests.pop(rid, None)
                removed_requests += 1
        for sid, spawn in list(world_spawns_by_id.items()):
            if spawn.cluster == cluster and spawn.scene == scene:
                world_spawns_by_id.pop(sid, None)
                removed_spawns += 1
        world_map_states.pop(key, None)
    if removed_spawns or removed_requests:
        print(f"[world-spawn-map-clear] scene={scene} reason={reason} spawns={removed_spawns} pending={removed_requests}")
    return bool(removed_spawns or removed_requests)

def request_world_spawn_generation(cluster, scene, zone, preferred_conn_id=None):
    global world_spawn_request_seq
    generator_conn = choose_world_spawn_generator(cluster, scene, preferred_conn_id)
    if not generator_conn:
        return False

    world_spawn_request_seq += 1
    request_id = f"owreq-{int(time.time())}-{world_spawn_request_seq}"
    req = PendingWorldSpawnRequest(
        request_id=request_id,
        cluster=cluster,
        scene=scene,
        zone_id=str(zone.get("zone_id", "")),
        x=float(zone.get("x", 0.0)),
        y=float(zone.get("y", 0.0)),
        z=float(zone.get("z", 0.0)),
        rarity=roll_visible_world_spawn_rarity(),
        requested_at=time.time(),
        requester_conn_id=generator_conn,
        zone_type=normalize_world_spawn_zone_type(zone.get("zone_type", "grass")),
    )
    pending_world_spawn_requests[request_id] = req
    send_to_conn(generator_conn, "WORLD_SPAWN_GEN_REQ|" + "|".join([
        enc(req.request_id), enc(req.scene), enc(req.rarity), enc(req.zone_id),
        f"{req.x:.4f}", f"{req.y:.4f}", f"{req.z:.4f}", enc(getattr(req, "zone_type", "grass") or "grass"),
    ]))
    print(f"[world-spawn-gen-req] scene={scene} zone={req.zone_id} type={getattr(req, 'zone_type', 'grass')} rarity={req.rarity} to={generator_conn}")
    return True

def run_world_spawn_pass_for_map(cluster, scene, preferred_conn_id=None, force=False):
    if not VISIBLE_WORLD_SPAWNS_ENABLED or scene in ("", "unknown"):
        return
    key = map_key(cluster, scene)
    state = world_map_states.get(key)
    if not state or not state.zones:
        return

    player_count = active_player_count_for_map(cluster, scene)
    if player_count <= 0:
        return

    now = time.time()
    if not force:
        next_due = float(getattr(state, "next_spawn_due_at", 0.0) or 0.0)
        if next_due > 0 and now < next_due:
            return
        if now - state.last_spawn_pass < SPAWN_TOPUP_INTERVAL_SECONDS:
            return

    occupied = get_occupied_world_spawn_zone_ids(cluster, scene)
    active_count, pending_count = count_world_spawn_population(cluster, scene)
    target_count = target_world_spawn_count(len(state.zones), player_count)
    needed = max(0, target_count - active_count - pending_count)
    made_requests = 0

    if needed > 0:
        candidate_zones = [z for z in list(state.zones) if str(z.get("zone_id", "")) not in occupied]
        random.shuffle(candidate_zones)
        for zone in candidate_zones:
            if made_requests >= needed:
                break
            zone_id = str(zone.get("zone_id", ""))
            if zone_id and zone_id in occupied:
                continue
            if request_world_spawn_generation(cluster, scene, zone, preferred_conn_id=preferred_conn_id):
                made_requests += 1
                if zone_id:
                    occupied.add(zone_id)

    state.last_spawn_pass = now
    # If the map is still below target because a generator failed or no candidate zones
    # were available, try again soon; otherwise use the normal top-up interval.
    state.next_spawn_due_at = now + (SPAWN_REFILL_DELAY_SECONDS if made_requests < needed else SPAWN_TOPUP_INTERVAL_SECONDS)

    skipped_occupied = 0
    try:
        skipped_occupied = len(get_occupied_world_spawn_zone_ids(cluster, scene))
    except Exception:
        skipped_occupied = 0
    print(f"[world-spawn-pass] scene={scene} players={player_count} zones={len(state.zones)} target={target_count} active={active_count} pending={pending_count} requested={made_requests} occupied_or_pending={skipped_occupied}")

def receive_world_spawn_zones(conn_id, player_id, cluster, scene, signature, zone_body):
    if not VISIBLE_WORLD_SPAWNS_ENABLED or scene in ("", "unknown"):
        return

    with players_lock:
        p = players_by_conn.get(conn_id)
    if not p:
        return
    if p.cluster != cluster or p.scene != scene:
        print(f"[world-spawn-zones-ignored] conn={conn_id} player map={p.cluster}/{p.scene} packet map={cluster}/{scene}")
        return

    zones = parse_world_spawn_zones(zone_body)
    if not zones:
        return

    now = time.time()
    key = map_key(cluster, scene)
    with world_spawns_lock:
        state = world_map_states.get(key)
        first_report = state is None
        signature_changed = first_report or state.signature != (signature or "")
        if state is None:
            state = MapSpawnState(cluster=cluster, scene=scene)
            world_map_states[key] = state
        state.zones = zones
        state.signature = signature or ""
        state.last_zone_report = now
        if state.no_players_since:
            state.no_players_since = 0.0

    if signature_changed or first_report:
        print(f"[world-spawn-zones] scene={scene} zones={len(zones)} signature={signature}")

    # First report for a map/player should stock the map immediately. Later reports
    # update the zone list but do not blindly reroll/refresh if other players are already
    # on the map; the population controller only tops up when below target.
    run_world_spawn_pass_for_map(cluster, scene, preferred_conn_id=conn_id, force=bool(first_report))

def complete_world_spawn_generation(conn_id, player_id, request_id, mon_id, mon_key, shiny, level, version_requirement="None"):
    global world_spawn_seq
    with world_spawns_lock:
        req = pending_world_spawn_requests.pop(request_id, None)
        if req is None:
            print(f"[world-spawn-gen-result-ignored] missing request {request_id}")
            return

    with players_lock:
        player = players_by_conn.get(conn_id)
    if not player or player.cluster != req.cluster or player.scene != req.scene:
        print(f"[world-spawn-gen-result-ignored] wrong map request={request_id}")
        return

    try:
        mon_id = int(mon_id)
    except Exception:
        mon_id = -1
    try:
        level = max(1, int(level))
    except Exception:
        level = 1

    version_requirement = str(version_requirement or "None").strip() or "None"
    if version_requirement.lower() not in ("none", "shared", "all", "both"):
        print(f"[world-spawn-gen-result-rejected] request={request_id} scene={req.scene} mon={mon_id}/{mon_key} versionRequirement={version_requirement}; server overworld spawns exclude version-exclusive mons")
        return

    with world_spawns_lock:
        # Re-check occupancy when the generator result comes back. A different client
        # or a previous pending request may have filled this square while generation
        # was in flight, and map updates may contain visually duplicated spawn zones.
        req_tile = req.zone_id or world_spawn_tile_key(req.x, req.y)
        for existing in world_spawns_by_id.values():
            if is_personal_world_spawn(existing):
                continue
            if existing.cluster != req.cluster or existing.scene != req.scene:
                continue
            if existing.state not in ("available", "busy"):
                continue
            existing_tile = getattr(existing, "zone_id", "") or world_spawn_tile_key(existing.x, existing.y)
            if existing_tile and req_tile and existing_tile == req_tile:
                print(f"[world-spawn-gen-result-blocked] request={request_id} scene={req.scene} tile={req_tile}; occupied by {existing.spawn_id}")
                return

        world_spawn_seq += 1
        spawn_id = f"ow-{req.cluster}-{req.scene}-{int(time.time())}-{world_spawn_seq}"
        now = time.time()
        spawn = WorldSpawn(
            spawn_id=spawn_id,
            cluster=req.cluster,
            scene=req.scene,
            x=float(req.x),
            y=float(req.y),
            z=float(req.z) if abs(float(req.z)) > 0.0001 else float(req.y) * 0.01,
            mon_id=mon_id,
            mon_key=str(mon_key or ""),
            shiny=bool(shiny),
            level=level,
            state="available",
            owner_id="",
            created_at=now,
            updated_at=now,
            zone_id=req.zone_id,
            lock_state="public",
            personal_owner_id="",
            mon_save_b64="",
            version_requirement="None",
            zone_type=normalize_world_spawn_zone_type(getattr(req, "zone_type", "grass")),
        )
        world_spawns_by_id[spawn_id] = spawn

    print(f"[world-spawn-add] {spawn.spawn_id} scene={spawn.scene} zone={req.zone_id} type={getattr(spawn, 'zone_type', 'grass')} pos=({spawn.x:.2f},{spawn.y:.2f}) mon={spawn.mon_id} mon_key={spawn.mon_key} shiny={spawn.shiny} level={spawn.level}")
    broadcast_world_spawn_add(spawn)

def receive_personal_encounter_spawns(conn_id, player_id, cluster, scene, records_body):
    global world_spawn_seq
    if not VISIBLE_WORLD_SPAWNS_ENABLED:
        return
    with players_lock:
        player = players_by_conn.get(conn_id)
    if not player:
        return
    if player.cluster != cluster or player.scene != scene or scene in ("", "unknown"):
        print(f"[personal-spawn-ignored] wrong map packet={cluster}/{scene} actual={getattr(player, 'cluster', '')}/{getattr(player, 'scene', '')}")
        return

    records = [r for r in (records_body or "").split(";") if r.strip()]
    if not records:
        return

    created = []
    removed_old = []
    now = time.time()
    owner_id = player_id or player.player_id
    with world_spawns_lock:
        # A personal/prize crystal packet replaces this owner/map's previous owner-only
        # reward spawns. Otherwise an unconsumed/resent packet can stack old crystals
        # and make the client look desynced after relog or map changes.
        for sid, old_spawn in list(world_spawns_by_id.items()):
            try:
                if old_spawn.cluster == cluster and old_spawn.scene == scene \
                   and (getattr(old_spawn, "personal_owner_id", "") or "") == owner_id \
                   and is_personal_world_spawn(old_spawn):
                    world_spawns_by_id.pop(sid, None)
                    old_spawn.state = "despawned"
                    old_spawn.updated_at = now
                    removed_old.append(old_spawn)
            except Exception:
                continue

        for rec in records[:4]:
            parts = rec.split(",")
            if len(parts) < 9:
                continue
            try:
                slot = parts[0]
                x = float(parts[1])
                y = float(parts[2])
                z = float(parts[3])
                mon_id = int(parts[4]) if parts[4] else -1
                shiny = parse_bool(parts[5])
                level = int(parts[6]) if parts[6] else 1
                mon_key = dec(parts[7])
                mon_save_b64 = dec(parts[8])
                version_requirement = dec(parts[9]) if len(parts) > 9 else "unknown"
            except Exception:
                continue
            world_spawn_seq += 1
            spawn_id = f"owp-{cluster}-{scene}-{int(now)}-{world_spawn_seq}"
            spawn = WorldSpawn(
                spawn_id=spawn_id,
                cluster=cluster,
                scene=scene,
                x=x,
                y=y,
                z=z if abs(z) > 0.0001 else y * 0.01,
                mon_id=mon_id,
                mon_key=mon_key or "",
                shiny=bool(shiny),
                level=max(1, int(level)),
                state="available",
                owner_id="",
                created_at=now,
                updated_at=now,
                zone_id="personal:" + str(slot),
                lock_state="owner_only",
                personal_owner_id=owner_id,
                mon_save_b64=mon_save_b64 or "",
                version_requirement=version_requirement or "unknown",
                zone_type="grass",
            )
            world_spawns_by_id[spawn_id] = spawn
            created.append(spawn)

    for spawn in removed_old:
        print(f"[personal-spawn-replace-remove] {spawn.spawn_id} owner={owner_id} scene={scene}; replacing old owner-only prize spawn")
        broadcast_world_spawn_remove(spawn)

    for spawn in created:
        print(f"[personal-spawn-add] {spawn.spawn_id} owner={spawn.personal_owner_id} scene={scene} mon={spawn.mon_id} shiny={spawn.shiny} level={spawn.level} versionRequirement={getattr(spawn, 'version_requirement', '')}")
        broadcast_world_spawn_add(spawn)

def maintain_world_spawn_maps():
    if not VISIBLE_WORLD_SPAWNS_ENABLED:
        return
    now = time.time()
    with world_spawns_lock:
        # Drop stale generation requests so a failed generator client cannot block a zone forever.
        stale_req_ids = [
            rid for rid, req in pending_world_spawn_requests.items()
            if now - req.requested_at > PENDING_SPAWN_REQUEST_TIMEOUT_SECONDS
        ]
        stale_maps = set()
        for rid in stale_req_ids:
            req = pending_world_spawn_requests.pop(rid, None)
            if req:
                stale_maps.add((req.cluster, req.scene))
                print(f"[world-spawn-gen-timeout] {rid} scene={req.scene} zone={req.zone_id}")
        for cluster, scene in stale_maps:
            schedule_world_spawn_refill(cluster, scene, delay_seconds=1.0)

        states = list(world_map_states.items())

    for key, state in states:
        cluster, scene = key
        count = active_player_count_for_map(cluster, scene)
        if count <= 0:
            remove_now = False
            with world_spawns_lock:
                current_state = world_map_states.get(key)
                if not current_state:
                    continue
                if current_state.no_players_since <= 0.0:
                    current_state.no_players_since = now
                elif now - current_state.no_players_since >= MAP_NO_PLAYER_DESPAWN_SECONDS:
                    remove_now = True
            if remove_now:
                clear_world_spawn_map_if_empty(cluster, scene, reason="EMPTY_TIMEOUT")
            continue

        with world_spawns_lock:
            current_state = world_map_states.get(key)
            if current_state:
                current_state.no_players_since = 0.0
                next_due = float(getattr(current_state, "next_spawn_due_at", 0.0) or 0.0)
                due = bool(current_state.zones) and (next_due <= 0.0 or now >= next_due or now - current_state.last_spawn_pass >= SPAWN_TOPUP_INTERVAL_SECONDS)
            else:
                due = False
        if due:
            run_world_spawn_pass_for_map(cluster, scene, force=False)

def claim_world_spawn(conn_id, player_id, spawn_id):
    with players_lock:
        player = players_by_conn.get(conn_id)

    if not player:
        send_to_conn(conn_id, "WORLD_SPAWN_CLAIM_FAIL|" + "|".join([
            enc(spawn_id), enc("Player not registered")
        ]))
        return

    with world_spawns_lock:
        spawn = world_spawns_by_id.get(spawn_id)
        if spawn is None:
            send_to_conn(conn_id, "WORLD_SPAWN_CLAIM_FAIL|" + "|".join([
                enc(spawn_id), enc("Spawn is gone")
            ]))
            return

        if spawn.cluster != player.cluster or spawn.scene != player.scene:
            send_to_conn(conn_id, "WORLD_SPAWN_CLAIM_FAIL|" + "|".join([
                enc(spawn_id), enc("Spawn is not in this area")
            ]))
            return

        expected_owner = player_id or player.player_id
        if world_spawn_is_locked_for_player(spawn, expected_owner):
            send_to_conn(conn_id, "WORLD_SPAWN_CLAIM_FAIL|" + "|".join([
                enc(spawn.spawn_id), enc(NO_PERMISSION_CAPTURE_MESSAGE)
            ]))
            return
        if spawn.state != "available":
            if spawn.state == "busy" and (spawn.owner_id or "") == expected_owner:
                send_to_conn(conn_id, "WORLD_SPAWN_CLAIM_OK|" + enc(spawn.spawn_id))
                return
            send_to_conn(conn_id, "WORLD_SPAWN_BUSY|" + "|".join([
                enc(spawn.spawn_id), enc(spawn.owner_id or "")
            ]))
            return

        # First processed claim wins.
        spawn.state = "busy"
        spawn.owner_id = expected_owner
        spawn.updated_at = time.time()

    print(f"[world-spawn-claim] {player.name}({player.player_id}) claimed {spawn.spawn_id}; marking busy for other clients")
    send_to_conn(conn_id, "WORLD_SPAWN_CLAIM_OK|" + enc(spawn.spawn_id))
    # Claim/menu-open locks interaction for everyone else but keeps the spawn visible.
    # Broadcast the busy record immediately so other clients can block local spoof attempts
    # before the next periodic WORLD_SPAWNS repair snapshot.
    broadcast_world_spawn_add(spawn, exclude_conn_id=conn_id)
    # v0.6.3: claim only locks ownership. Remote catch FX now starts when the
    # claimant actually presses Catch, via WORLD_SPAWN_CATCH_START.

def start_world_spawn_catch_animation(conn_id, player_id, spawn_id):
    with players_lock:
        player = players_by_conn.get(conn_id)

    if not player:
        return

    with world_spawns_lock:
        spawn = world_spawns_by_id.get(spawn_id)
        if spawn is None:
            return

        owner = spawn.owner_id or ""
        expected_owner = player_id or player.player_id
        if world_spawn_is_locked_for_player(spawn, expected_owner):
            send_to_conn(conn_id, "WORLD_SPAWN_CLAIM_FAIL|" + "|".join([
                enc(spawn.spawn_id), enc(NO_PERMISSION_CAPTURE_MESSAGE)
            ]))
            return

        # v0.6.4: catch-start is allowed to be the first packet that reaches the
        # server. If the spawn is still available, claim it for this player and
        # relay the catch FX immediately. TCP order normally sends CLAIM first,
        # but this makes the animation path robust instead of silently ignoring.
        if spawn.state == "available":
            spawn.state = "busy"
            spawn.owner_id = expected_owner
            spawn.updated_at = time.time()
            owner = expected_owner
            send_to_conn(conn_id, "WORLD_SPAWN_CLAIM_OK|" + enc(spawn.spawn_id))
        elif spawn.state != "busy" or (owner and expected_owner and owner != expected_owner):
            print(f"[world-spawn-catch-start-ignored] spawn={spawn_id} state={getattr(spawn, 'state', '')} owner={owner} from={expected_owner}")
            return

    print(f"[world-spawn-catch-start] {player.name}({player.player_id}) started catch animation for {spawn.spawn_id}")
    broadcast_world_spawn_catch_start(spawn, player.player_id, exclude_conn_id=conn_id)

def world_spawn_result_removes_spawn(result):
    value = (result or "").strip().lower().replace("-", "_").replace(" ", "_")
    return value in (
        "caught", "catch", "success", "1", "true", "yes",
        "removed", "remove", "despawn", "despawned",
        "shoo", "shooed", "sent_to_town", "senttotown", "town"
    )

def finish_world_spawn_claim(conn_id, player_id, spawn_id, result):
    result = (result or "").strip().lower()
    removes_spawn = world_spawn_result_removes_spawn(result)
    result_for_clients = "removed" if result in ("removed", "remove", "shoo", "shooed", "sent_to_town", "senttotown", "town", "despawn", "despawned") else ("caught" if removes_spawn else "failed")

    with players_lock:
        player = players_by_conn.get(conn_id)

    if not player:
        return

    with world_spawns_lock:
        spawn = world_spawns_by_id.get(spawn_id)
        if spawn is None:
            return

        owner = spawn.owner_id or ""
        expected_owner = player_id or player.player_id
        if spawn.state != "busy" or (owner and expected_owner and owner != expected_owner):
            print(f"[world-spawn-result-ignored] spawn={spawn_id} result={result} state={getattr(spawn, 'state', '')} owner={owner} from={expected_owner}")
            return

        personal_spawn = is_personal_world_spawn(spawn)
        if removes_spawn:
            spawn.state = "despawned"
            spawn.updated_at = time.time()
            # Once a server-owned spawn is caught/shooed/removed, remove it from the
            # authoritative table instead of leaving a despawned record around. This
            # is especially important for owner-only prize spawns: the owner's client
            # must not be able to rehydrate the same reward locally after relog/map
            # refresh, and other clients should only ever see an explicit remove.
            world_spawns_by_id.pop(spawn.spawn_id, None)
        else:
            spawn.state = "available"
            spawn.owner_id = ""
            spawn.updated_at = time.time()

    if removes_spawn:
        action = "removed" if result_for_clients == "removed" else "caught"
        print(f"[world-spawn-{action}] {player.name}({player.player_id}) result={result} spawn={spawn.spawn_id}; despawning globally personal={personal_spawn}")
        broadcast_world_spawn_catch_end(spawn, player.player_id, result_for_clients)
        broadcast_world_spawn_remove(spawn)
        if not personal_spawn:
            with world_spawns_lock:
                schedule_world_spawn_refill(spawn.cluster, spawn.scene, delay_seconds=SPAWN_REFILL_DELAY_SECONDS)
    else:
        print(f"[world-spawn-released] {player.name}({player.player_id}) result={result} for {spawn.spawn_id}; making available again")
        broadcast_world_spawn_catch_end(spawn, player.player_id, "failed")
        broadcast_world_spawn_add(spawn)

def send_to_conn(conn_id, text):
    with players_lock:
        handler = handlers_by_conn.get(conn_id)
    if not handler:
        return
    try:
        if text.startswith("WORLD_SPAWN_CATCH") or text.startswith("WORLD_SPAWN_CLAIM") or text.startswith("WORLD_SPAWN_BUSY") or text.startswith("WORLD_SPAWN_ADD") or text.startswith("WORLD_SPAWN_REMOVE") or text.startswith("WORLD_SPAWN_LOCK_UPDATE"):
            print(f"[world-spawn-send] to={conn_id} {text}")
        handler.send_line(text)
    except Exception as e:
        print(f"[send-error] {conn_id}: {e}")

def broadcast_step(from_conn_id, step_line, cluster, scene):
    if scene in ("", "unknown"):
        return

    with players_lock:
        targets = [
            cid for cid, p in players_by_conn.items()
            if cid != from_conn_id and p.cluster == cluster and p.scene == scene
        ]

    for cid in targets:
        send_to_conn(cid, step_line)


def broadcast_visual_event(from_conn_id, visual_line, cluster, scene):
    if scene in ("", "unknown"):
        return

    with players_lock:
        targets = [
            cid for cid, p in players_by_conn.items()
            if cid != from_conn_id and p.cluster == cluster and p.scene == scene
        ]

    for cid in targets:
        send_to_conn(cid, visual_line)

def find_conn_by_player_id(player_id):
    with players_lock:
        for cid, p in players_by_conn.items():
            if p.player_id == player_id:
                return cid
    return None

def get_player_by_conn(conn_id):
    with players_lock:
        return players_by_conn.get(conn_id)


def _handle_player_left_cleanup(p, reason="DISCONNECTED"):
    if not p:
        return
    try:
        print(f"[player-left] reason={reason} name={getattr(p, 'name', '')} id={getattr(p, 'player_id', '')}")
    except Exception:
        pass
    try:
        _ranked_remove_sessions_for_player(p.player_id)
    except Exception as ex:
        print(f"[ranked-left-cleanup-error] player={getattr(p, 'player_id', '')} err={ex}")
    try:
        _remove_battle_sessions_for_player(p.player_id)
    except Exception as ex:
        print(f"[battle-left-cleanup-error] player={getattr(p, 'player_id', '')} err={ex}")
    try:
        unlock_personal_spawns_for_owner_left_map(p.player_id, p.cluster, p.scene, reason="OWNER_" + str(reason or "LEFT"))
        clear_world_spawn_map_if_empty(p.cluster, p.scene, reason="PLAYER_LEFT_" + str(reason or "LEFT"))
    except Exception as ex:
        print(f"[spawn-left-cleanup-error] player={getattr(p, 'player_id', '')} err={ex}")


# Server-owned PvP action scheduler. The server owns action order and actionSeq;
# clients still render battles and report resolved hit packets for this test build.
battle_sessions_lock = threading.RLock()
battle_sessions = {}

class ServerOwnedBattleSession:
    def __init__(self, battle_id, a_id, b_id):
        self.battle_id = str(battle_id or "")
        self.players = sorted([str(a_id or ""), str(b_id or "")])
        self.sides = {}
        self.initialized = False
        self.seq = 0
        self.current = None
        self.rng = random.Random((self.battle_id + "|server-owned-pvp").encode('utf-8').hex())

    def side_ready(self, player_id, mons):
        side = []
        for m in mons:
            slot = int(m.get('slot', len(side)))
            hp = max(0, int(m.get('hp', 0)))
            maxhp = max(1, int(m.get('maxhp', hp if hp > 0 else 1)))
            level = max(1, int(m.get('level', 1)))
            spd_stat = max(1, int(m.get('spd', 1)))
            eff = max(1.0, float(spd_stat) ** 0.65)
            start_meter = min(99.0, eff + (float(level) * 0.25) + self.rng.uniform(0.0, 15.0))
            side.append({
                'owner': str(player_id or ''),
                'slot': slot,
                'hp': hp,
                'maxhp': maxhp,
                'level': level,
                'spd': spd_stat,
                'speed': eff,
                'meter': start_meter,
            })
        while len(side) < 4:
            side.append({'owner': str(player_id or ''), 'slot': len(side), 'hp': 0, 'maxhp': 1, 'level': 1, 'spd': 1, 'speed': 1.0, 'meter': 0.0})
        self.sides[str(player_id or '')] = side[:4]

    def all_battlers(self):
        out = []
        for pid in self.players:
            out.extend(self.sides.get(pid, []))
        return out

    def has_both_sides(self):
        return all(pid in self.sides for pid in self.players)

    def _living(self):
        return [b for b in self.all_battlers() if int(b.get('hp', 0)) > 0]

    def choose_next(self):
        living = self._living()
        if not living:
            self.current = None
            return None

        ready = [b for b in living if float(b.get('meter', 0.0)) >= 99.999]
        if not ready:
            min_time = None
            for b in living:
                speed = max(1.0, float(b.get('speed', 1.0)))
                needed = max(0.0, 100.0 - float(b.get('meter', 0.0))) / speed
                if min_time is None or needed < min_time:
                    min_time = needed
            if min_time is None:
                min_time = 0.0
            for b in living:
                b['meter'] = float(b.get('meter', 0.0)) + max(1.0, float(b.get('speed', 1.0))) * min_time
            ready = [b for b in living if float(b.get('meter', 0.0)) >= 99.999]

        if not ready:
            # Defensive fallback: highest meter if float rounding produced no ready battler.
            ready = [max(living, key=lambda x: float(x.get('meter', 0.0)))]

        chosen = self.rng.choice(ready)
        self.seq += 1
        self.current = {'seq': self.seq, 'owner': chosen['owner'], 'slot': int(chosen['slot'])}
        return self.current

    def apply_done(self, seq, owner, slot):
        if not self.current:
            return False
        if int(seq) != int(self.current.get('seq', -1)):
            return False
        if str(owner or '') != str(self.current.get('owner', '')):
            return False
        if int(slot) != int(self.current.get('slot', -1)):
            return False
        for b in self.sides.get(str(owner or ''), []):
            if int(b.get('slot', -1)) == int(slot):
                b['meter'] = max(0.0, float(b.get('meter', 0.0)) - 100.0)
                break
        self.current = None
        return True

    def apply_hit(self, from_id, to_id, target_side, target_slot, hp_after):
        # targetSide is sender-relative: E = opponent, P = sender/self side.
        owner = str(to_id if str(target_side or 'E').upper() == 'E' else from_id)
        for b in self.sides.get(owner, []):
            if int(b.get('slot', -1)) == int(target_slot):
                b['hp'] = max(0, int(hp_after))
                return


def _parse_battle_snapshot_payload(payload):
    mons = []
    for part in str(payload or '').split(';'):
        if not part:
            continue
        bits = part.split(',')
        if len(bits) < 5:
            continue
        try:
            mons.append({
                'slot': int(bits[0]),
                'hp': int(bits[1]),
                'maxhp': int(bits[2]),
                'level': int(bits[3]),
                'spd': int(bits[4]),
            })
        except Exception:
            continue
    return mons


def _battle_session_key(battle_id):
    return str(battle_id or '')


def _get_or_create_battle_session(battle_id, from_id, to_id):
    key = _battle_session_key(battle_id)
    with battle_sessions_lock:
        sess = battle_sessions.get(key)
        if not sess:
            sess = ServerOwnedBattleSession(key, from_id, to_id)
            battle_sessions[key] = sess
        return sess


def _send_battle_grant(sess, reason='SERVER_TURN_METER'):
    if sess is None:
        return
    grant = sess.current or sess.choose_next()
    if not grant:
        return
    line = "BATTLE_GRANT|" + "|".join([
        enc(sess.battle_id), str(int(grant['seq'])), enc(grant['owner']), str(int(grant['slot'])), enc(reason)
    ])
    for pid in list(sess.players):
        conn = find_conn_by_player_id(pid)
        if conn:
            send_to_conn(conn, line)
    print(f"[battle-grant] battle={sess.battle_id} seq={grant['seq']} owner={grant['owner']} slot={grant['slot']} reason={reason}")


def register_battle_snapshot(from_conn_id, from_id, to_id, battle_id, payload):
    mons = _parse_battle_snapshot_payload(payload)
    key = _battle_session_key(battle_id)
    with battle_sessions_lock:
        sess = battle_sessions.get(key)
        # battleId is deterministic for the same two players. A fresh snapshot from a
        # side that already snapshotted this battleId means these players are starting
        # another PvP battle, so discard any stale current grant/meters from the prior
        # finished battle before accepting the new snapshots.
        if sess is not None and str(from_id or '') in sess.sides:
            print(f"[battle-session-reset] battle={battle_id} reason=repeat-snapshot player={from_id}")
            sess = ServerOwnedBattleSession(key, from_id, to_id)
            battle_sessions[key] = sess
        elif sess is None:
            sess = ServerOwnedBattleSession(key, from_id, to_id)
            battle_sessions[key] = sess

        sess.side_ready(from_id, mons)
        print(f"[battle-snapshot] battle={battle_id} player={from_id} mons={len(mons)}")
        if sess.has_both_sides() and not sess.current:
            _send_battle_grant(sess, 'SERVER_TURN_METER_INIT')


def _validate_current_action(from_id, battle_id, action_seq, actor_slot):
    with battle_sessions_lock:
        sess = battle_sessions.get(_battle_session_key(battle_id))
        if not sess or not sess.current:
            return False
        cur = sess.current
        return str(cur.get('owner', '')) == str(from_id or '') and int(cur.get('seq', -1)) == int(action_seq) and int(cur.get('slot', -1)) == int(actor_slot)


def _battle_action_done(from_id, battle_id, action_seq, actor_slot):
    with battle_sessions_lock:
        sess = battle_sessions.get(_battle_session_key(battle_id))
        if not sess:
            return
        if not sess.apply_done(action_seq, from_id, actor_slot):
            print(f"[battle-done-stale] battle={battle_id} from={from_id} seq={action_seq} actor={actor_slot}")
            return
        _send_battle_grant(sess, 'SERVER_TURN_METER_NEXT')


def _battle_apply_hit(from_id, to_id, battle_id, action_seq, actor_slot, target_side, target_slot, hp_after):
    with battle_sessions_lock:
        sess = battle_sessions.get(_battle_session_key(battle_id))
        if not sess:
            return
        if not _validate_current_action(from_id, battle_id, action_seq, actor_slot):
            print(f"[battle-hit-stale] battle={battle_id} from={from_id} seq={action_seq} actor={actor_slot}")
            return
        sess.apply_hit(from_id, to_id, target_side, target_slot, hp_after)


def _remove_battle_sessions_for_player(player_id):
    if not player_id:
        return
    with battle_sessions_lock:
        doomed = [bid for bid, sess in battle_sessions.items() if str(player_id) in sess.players]
        for bid in doomed:
            try:
                for other_id in list(getattr(sess, 'players', []) or []):
                    if str(other_id) == str(player_id):
                        continue
                    other_conn = find_conn_by_player_id(other_id)
                    if other_conn:
                        send_to_conn(other_conn, "BATTLE_OPPONENT_LEFT|" + "|".join([
                            enc(str(bid)), enc(str(player_id)), enc("opponent-disconnected")
                        ]))
                        print(f"[battle-opponent-left] battle={bid} left={player_id} notify={other_id}")
            except Exception as ex:
                print(f"[battle-opponent-left-error] battle={bid} left={player_id} err={ex}")
            battle_sessions.pop(bid, None)
            print(f"[battle-session-remove] battle={bid} player={player_id}")


def _is_no_result_battle_end_reason(reason):
    r = str(reason or '').lower()
    return r.startswith('no-result') or 'disconnect' in r or 'timeout' in r or 'abort' in r or 'opponent-left' in r


def _notify_battle_no_result_other(from_id, to_id, battle_id, reason):
    # BATTLE_END normally just removes a completed battle session. For disconnect/timeout
    # no-result endings, explicitly tell the other player to leave battle too. This
    # covers manual MMO Disconnect/BYE paths where the socket can close before the
    # normal Handler.finish cleanup notification reaches the opponent.
    if not to_id:
        return
    other_conn = find_conn_by_player_id(to_id)
    if other_conn:
        send_to_conn(other_conn, "BATTLE_OPPONENT_LEFT|" + "|".join([
            enc(str(battle_id)), enc(str(from_id or '')), enc(str(reason or 'no-result'))
        ]))
        print(f"[battle-no-result-notify] battle={battle_id} from={from_id} notify={to_id} reason={reason}")


def end_battle_session(from_id, to_id, battle_id, reason='completed'):
    key = _battle_session_key(battle_id)
    notify_other = _is_no_result_battle_end_reason(reason)
    with battle_sessions_lock:
        sess = battle_sessions.get(key)
        if sess is not None:
            battle_sessions.pop(key, None)
            print(f"[battle-session-end] battle={battle_id} from={from_id} to={to_id} reason={reason}")
        else:
            print(f"[battle-session-end-miss] battle={battle_id} from={from_id} to={to_id} reason={reason}")
    if notify_other:
        _notify_battle_no_result_other(from_id, to_id, battle_id, reason)


def send_battle_request(from_conn_id, from_id, from_name, cluster, scene, to_id, team_payload):
    target_conn = find_conn_by_player_id(to_id)
    if not target_conn:
        send_to_conn(from_conn_id, f"BATTLE_DECLINE|{enc(to_id)}|{enc('Player offline')}")
        return

    with players_lock:
        target = players_by_conn.get(target_conn)
        sender = players_by_conn.get(from_conn_id)

    if not target or not sender:
        return

    if target.cluster != cluster or target.scene != scene or scene in ("", "unknown"):
        send_to_conn(from_conn_id, f"BATTLE_DECLINE|{enc(to_id)}|{enc('Player unavailable')}")
        return

    target_status = getattr(target, "status", "available")
    if target_status != "available":
        print(f"[battle-busy] {from_name}({from_id}) tried {target.name}({to_id}) status={target_status}")
        send_to_conn(from_conn_id, "BATTLE_BUSY|" + "|".join([
            enc(to_id), enc(target.name), enc(target_status)
        ]))
        return

    print(f"[battle-req] {from_name}({from_id}) -> {target.name}({to_id})")
    send_to_conn(target_conn, "BATTLE_REQ|" + "|".join([
        enc(from_id), enc(from_name), team_payload
    ]))

def send_battle_accept(from_conn_id, requester_id, acceptor_id, acceptor_name, team_payload):
    requester_conn = find_conn_by_player_id(requester_id)
    if not requester_conn:
        return
    print(f"[battle-accept] {acceptor_name}({acceptor_id}) -> {requester_id}")
    send_to_conn(requester_conn, "BATTLE_ACCEPT|" + "|".join([
        enc(acceptor_id), enc(acceptor_name), team_payload
    ]))

def send_battle_decline(from_conn_id, requester_id, decliner_id, decliner_name):
    requester_conn = find_conn_by_player_id(requester_id)
    if not requester_conn:
        return
    print(f"[battle-decline] {decliner_name}({decliner_id}) -> {requester_id}")
    send_to_conn(requester_conn, "BATTLE_DECLINE|" + "|".join([
        enc(decliner_id), enc(decliner_name)
    ]))

def send_battle_busy_reply(from_conn_id, requester_id, busy_id, busy_name, status):
    requester_conn = find_conn_by_player_id(requester_id)
    if not requester_conn:
        return
    print(f"[battle-busy-reply] {busy_name}({busy_id}) -> {requester_id} status={status}")
    send_to_conn(requester_conn, "BATTLE_BUSY|" + "|".join([
        enc(busy_id), enc(busy_name), enc(status)
    ]))

def send_battle_cmd(from_conn_id, from_id, to_id, battle_id, action_seq, actor_slot, move_slot, target_slot, target_ally):
    target_conn = find_conn_by_player_id(to_id)
    if not target_conn:
        print(f"[battle-cmd-miss] target offline: {to_id}")
        return

    if not _validate_current_action(from_id, battle_id, action_seq, actor_slot):
        print(f"[battle-cmd-reject] battle={battle_id} from={from_id} seq={action_seq} actor={actor_slot}")
        return

    print(f"[battle-cmd] {from_id} -> {to_id} battle={battle_id} seq={action_seq} actor={actor_slot} move={move_slot} target={target_slot} ally={target_ally}")
    send_to_conn(target_conn, "BATTLE_CMD|" + "|".join([
        enc(from_id), enc(battle_id), str(int(action_seq)),
        str(int(actor_slot)), str(int(move_slot)), str(int(target_slot)),
        "1" if target_ally else "0"
    ]))

def send_battle_hit(from_conn_id, from_id, to_id, battle_id, action_seq, actor_slot, move_slot, target_side, target_slot, amount, hp_after, shield_after, crit):
    target_conn = find_conn_by_player_id(to_id)
    if not target_conn:
        print(f"[battle-hit-miss] target offline: {to_id}")
        return

    if not _validate_current_action(from_id, battle_id, action_seq, actor_slot):
        print(f"[battle-hit-reject] battle={battle_id} from={from_id} seq={action_seq} actor={actor_slot}")
        return

    _battle_apply_hit(from_id, to_id, battle_id, action_seq, actor_slot, target_side, target_slot, hp_after)
    print(f"[battle-hit] {from_id} -> {to_id} battle={battle_id} seq={action_seq} actor={actor_slot} move={move_slot} target={target_side}{target_slot} amount={amount} hpAfter={hp_after} crit={crit}")
    send_to_conn(target_conn, "BATTLE_HIT|" + "|".join([
        enc(from_id), enc(battle_id), str(int(action_seq)),
        str(int(actor_slot)), str(int(move_slot)), enc(target_side or "E"),
        str(int(target_slot)), str(int(amount)), str(int(hp_after)), str(int(shield_after)),
        "1" if str(crit).lower() in ("1", "true", "yes") else "0"
    ]))

def send_battle_done(from_conn_id, from_id, to_id, battle_id, action_seq, actor_slot, move_slot):
    target_conn = find_conn_by_player_id(to_id)
    if not target_conn:
        print(f"[battle-done-miss] target offline: {to_id}")
        return

    if not _validate_current_action(from_id, battle_id, action_seq, actor_slot):
        print(f"[battle-done-reject] battle={battle_id} from={from_id} seq={action_seq} actor={actor_slot}")
        return

    print(f"[battle-done] {from_id} -> {to_id} battle={battle_id} seq={action_seq} actor={actor_slot} move={move_slot}")
    send_to_conn(target_conn, "BATTLE_DONE|" + "|".join([
        enc(from_id), enc(battle_id), str(int(action_seq)), str(int(actor_slot)), str(int(move_slot))
    ]))
    _battle_action_done(from_id, battle_id, action_seq, actor_slot)

def send_battle_state(from_conn_id, from_id, to_id, battle_id, state_payload):
    target_conn = find_conn_by_player_id(to_id)
    if not target_conn:
        print(f"[battle-state-miss] target offline: {to_id}")
        return

    send_to_conn(target_conn, "BATTLE_STATE|" + "|".join([
        enc(from_id), enc(battle_id), state_payload
    ]))

def _ranked_team_count(team_payload):
    try:
        return len([p for p in str(team_payload or "").split("~") if p and p != "_"])
    except Exception:
        return 0

def _ranked_error(conn_id, message):
    send_to_conn(conn_id, "RANKED_BATTLE_ERROR|" + enc(message or "Ranked battle request failed."))

def _ranked_find_session(battle_id):
    with ranked_sessions_lock:
        return ranked_battle_sessions.get(battle_id)

def _ranked_remove_sessions_for_player(player_id):
    if not player_id:
        return
    removed = []
    with ranked_sessions_lock:
        for bid, sess in list(ranked_battle_sessions.items()):
            if sess.get("requester_id") == player_id or sess.get("target_id") == player_id:
                removed.append((bid, sess))
                ranked_battle_sessions.pop(bid, None)
    for bid, sess in removed:
        other_id = sess.get("target_id") if sess.get("requester_id") == player_id else sess.get("requester_id")
        other_conn = find_conn_by_player_id(other_id)
        if other_conn:
            _ranked_error(other_conn, "Ranked battle cancelled because the other player disconnected.")

def send_ranked_battle_request(from_conn_id, from_id, from_name, cluster, scene, to_id, to_name, team_payload):
    target_conn = find_conn_by_player_id(to_id)
    if not target_conn:
        _ranked_error(from_conn_id, "Player is offline.")
        return

    with players_lock:
        target = players_by_conn.get(target_conn)
        sender = players_by_conn.get(from_conn_id)

    if not target or not sender:
        _ranked_error(from_conn_id, "Player is unavailable.")
        return

    if target.cluster != cluster or target.scene != scene or scene in ("", "unknown"):
        _ranked_error(from_conn_id, "Player is unavailable for ranked battle.")
        return

    target_status = getattr(target, "status", "available")
    if target_status != "available":
        send_to_conn(from_conn_id, "BATTLE_BUSY|" + "|".join([enc(to_id), enc(target.name), enc(target_status)]))
        return

    if social is None:
        _ranked_error(from_conn_id, "Ranked social database is unavailable on this server.")
        return

    requester_team_count = _ranked_team_count(team_payload)
    target_identifier = to_name or getattr(target, "name", "") or to_id
    ok, msg, meta = social.validate_ranked_battle_request(from_name or getattr(sender, "name", ""), target_identifier, requester_team_count)
    if not ok:
        _ranked_error(from_conn_id, msg)
        return

    ranked_id = "rb-" + uuid.uuid4().hex
    with ranked_sessions_lock:
        ranked_battle_sessions[ranked_id] = {
            "battle_id": ranked_id,
            "requester_conn": from_conn_id,
            "target_conn": target_conn,
            "requester_id": from_id,
            "target_id": to_id,
            "requester_name": from_name,
            "target_name": getattr(target, "name", target_identifier),
            "requester_handle": (meta or {}).get("requester_handle", from_name),
            "target_handle": (meta or {}).get("target_handle", target_identifier),
            "required_team_size": int((meta or {}).get("required_team_size", 4) or 4),
            "status": "requested",
            "created_at": time.time(),
            "reports": {},
            "resolved": False,
        }

    title_msg = f"{from_name} wants a Ranked Battle. Rank gap OK. Base RP only; performance modifiers are locked."
    print(f"[ranked-req] {from_name}({from_id}) -> {getattr(target, 'name', target_identifier)}({to_id}) battle={ranked_id}")
    send_to_conn(target_conn, "RANKED_BATTLE_REQ|" + "|".join([
        enc(from_id), enc(from_name), team_payload, enc(ranked_id), enc(title_msg)
    ]))

def send_ranked_battle_accept(from_conn_id, requester_id, acceptor_id, acceptor_name, team_payload, ranked_id):
    sess = _ranked_find_session(ranked_id)
    if not sess:
        _ranked_error(from_conn_id, "Ranked battle session expired.")
        return
    if sess.get("target_id") != acceptor_id or sess.get("requester_id") != requester_id:
        _ranked_error(from_conn_id, "Ranked battle session did not match the request.")
        return
    required = int((sess.get("required_team_size") or 4)) if sess.get("required_team_size") else 4
    if _ranked_team_count(team_payload) < required:
        _ranked_error(from_conn_id, f"Ranked requires a full team of {required} battle-ready MoN.")
        return
    requester_conn = find_conn_by_player_id(requester_id)
    if not requester_conn:
        _ranked_error(from_conn_id, "Requester is offline.")
        return
    with ranked_sessions_lock:
        sess = ranked_battle_sessions.get(ranked_id)
        if sess:
            sess["status"] = "accepted"
            sess["accepted_at"] = time.time()
    print(f"[ranked-accept] {acceptor_name}({acceptor_id}) -> {requester_id} battle={ranked_id}")
    send_to_conn(requester_conn, "RANKED_BATTLE_ACCEPT|" + "|".join([
        enc(acceptor_id), enc(acceptor_name), team_payload, enc(ranked_id)
    ]))

def send_ranked_battle_decline(from_conn_id, requester_id, decliner_id, decliner_name, ranked_id):
    requester_conn = find_conn_by_player_id(requester_id)
    with ranked_sessions_lock:
        ranked_battle_sessions.pop(ranked_id, None)
    if requester_conn:
        send_to_conn(requester_conn, "BATTLE_DECLINE|" + "|".join([enc(decliner_id), enc(decliner_name)]))
    print(f"[ranked-decline] {decliner_name}({decliner_id}) -> {requester_id} battle={ranked_id}")

def receive_ranked_battle_result(from_conn_id, from_id, to_id, ranked_id, result):
    result = (result or "").strip().upper()
    if result not in ("WIN", "LOSS"):
        _ranked_error(from_conn_id, "Invalid ranked battle result.")
        return
    sess = _ranked_find_session(ranked_id)
    if not sess:
        _ranked_error(from_conn_id, "Ranked battle result ignored because the session expired.")
        return
    if from_id not in (sess.get("requester_id"), sess.get("target_id")):
        _ranked_error(from_conn_id, "Ranked battle result ignored because the player was not part of this session.")
        return
    with ranked_sessions_lock:
        sess = ranked_battle_sessions.get(ranked_id)
        if not sess or sess.get("resolved"):
            return
        reports = sess.setdefault("reports", {})
        if from_id in reports:
            return
        reports[from_id] = result
        print(f"[ranked-result] {from_id} reported {result} battle={ranked_id} reports={reports}")
        requester_id = sess.get("requester_id")
        target_id = sess.get("target_id")
        if requester_id not in reports or target_id not in reports:
            send_to_conn(from_conn_id, "RANKED_BATTLE_RESULT_ACK|" + enc("Ranked result reported. Waiting for opponent result."))
            return
        req_result = reports.get(requester_id)
        tar_result = reports.get(target_id)
        if req_result == tar_result:
            sess["resolved"] = True
            ranked_battle_sessions.pop(ranked_id, None)
            msg = "Ranked result conflict. No RP was applied; report this match to an admin."
            for pid in (requester_id, target_id):
                conn = find_conn_by_player_id(pid)
                if conn:
                    _ranked_error(conn, msg)
            print(f"[ranked-result-conflict] battle={ranked_id} reports={reports}")
            return
        winner_name = sess.get("requester_name") if req_result == "WIN" else sess.get("target_name")
        loser_name = sess.get("target_name") if req_result == "WIN" else sess.get("requester_name")
        sess["resolved"] = True
        ranked_battle_sessions.pop(ranked_id, None)

    if social is None:
        msg = "Ranked social database unavailable; no RP applied."
        for pid in (sess.get("requester_id"), sess.get("target_id")):
            conn = find_conn_by_player_id(pid)
            if conn:
                _ranked_error(conn, msg)
        return
    ok, msg, meta = social.apply_ranked_match_result(winner_name, loser_name, battle_id=ranked_id, reason="live ranked battle", created_by="ranked-battle")
    out_msg = msg if ok else (msg or "Ranked result failed; no RP was applied.")
    for pid in (sess.get("requester_id"), sess.get("target_id")):
        conn = find_conn_by_player_id(pid)
        if conn:
            send_to_conn(conn, "RANKED_BATTLE_RESULT_ACK|" + enc(out_msg))
    print(f"[ranked-resolve] battle={ranked_id} ok={ok} {out_msg}")

class Handler(socketserver.StreamRequestHandler):
    def setup(self):
        super().setup()
        self.conn_id = f"{self.client_address[0]}:{self.client_address[1]}:{time.time_ns()}"
        self.send_lock = threading.Lock()
        with players_lock:
            handlers_by_conn[self.conn_id] = self
        print(f"[connect] {self.client_address}")

    def finish(self):
        with players_lock:
            handlers_by_conn.pop(self.conn_id, None)
            p = players_by_conn.pop(self.conn_id, None)
        world_spawn_snapshot_cache_by_conn.pop(self.conn_id, None)
        if p:
            print(f"[disconnect] {p.name} / {p.player_id}")
            _handle_player_left_cleanup(p, "DISCONNECTED")
        else:
            print(f"[disconnect] {self.client_address}")
        super().finish()

    def send_line(self, text):
        with self.send_lock:
            self.wfile.write((text + "\n").encode("utf-8"))
            self.wfile.flush()

    def handle(self):
        self.send_line("WELCOME|server-ready")

        while True:
            try:
                raw = self.rfile.readline()
            except (ConnectionResetError, ConnectionAbortedError, OSError):
                break
            if not raw:
                break

            try:
                line = raw.decode("utf-8", errors="replace").strip()
                if not line:
                    continue

                parts = line.split("|")
                kind = parts[0].upper()
                if kind.startswith("WORLD_SPAWN"):
                    print(f"[world-spawn-recv] from={self.conn_id} {line}")

                if kind == "HELLO":
                    player_id = dec(parts[1]) if len(parts) > 1 else self.conn_id
                    name = dec(parts[2]) if len(parts) > 2 else "Player"
                    cluster = dec(parts[3]) if len(parts) > 3 else "1"
                    design = int(parts[4]) if len(parts) > 4 and parts[4] else 0
                    color1 = int(parts[5]) if len(parts) > 5 and parts[5] else 0
                    color2 = int(parts[6]) if len(parts) > 6 and parts[6] else 0

                    with players_lock:
                        players_by_conn[self.conn_id] = PlayerState(
                            self.conn_id, player_id, name, cluster, "",
                            0.0, 0.0, 0.0, 0, False,
                            design, color1, color2, "available",
                            False, 0.0, 0.0, 0, False, "", False, time.time()
                        )

                    print(f"[hello] {name} id={player_id} cluster={cluster} design={design} colors={color1},{color2}")
                    self.send_line(f"WELCOME|{enc(player_id)}")

                elif kind == "POS":
                    player_id = dec(parts[1])
                    name = dec(parts[2])
                    cluster = dec(parts[3])
                    scene = dec(parts[4])
                    x = float(parts[5])
                    y = float(parts[6])
                    z = float(parts[7])
                    facing = int(parts[8])
                    moving = parse_bool(parts[9])
                    design = int(parts[10]) if len(parts) > 10 and parts[10] else 0
                    color1 = int(parts[11]) if len(parts) > 11 and parts[11] else 0
                    color2 = int(parts[12]) if len(parts) > 12 and parts[12] else 0
                    status = dec(parts[13]) if len(parts) > 13 and parts[13] else "available"
                    follower_enabled = parse_bool(parts[14]) if len(parts) > 14 and parts[14] else False
                    follower_x = float(parts[15]) if len(parts) > 15 and parts[15] else 0.0
                    follower_y = float(parts[16]) if len(parts) > 16 and parts[16] else 0.0
                    follower_facing = int(parts[17]) if len(parts) > 17 and parts[17] else facing
                    follower_moving = parse_bool(parts[18]) if len(parts) > 18 and parts[18] else False
                    follower_sprite = dec(parts[19]) if len(parts) > 19 and parts[19] else ""
                    follower_flip_x = parse_bool(parts[20]) if len(parts) > 20 and parts[20] else False
                    riding_broom = parse_bool(parts[21]) if len(parts) > 21 and parts[21] else False
                    water_walking = parse_bool(parts[22]) if len(parts) > 22 and parts[22] else False
                    jumping_or_bouncing = parse_bool(parts[23]) if len(parts) > 23 and parts[23] else False
                    bouncing_name = dec(parts[24]) if len(parts) > 24 and parts[24] else ""

                    previous_cluster = ""
                    previous_scene = ""
                    with players_lock:
                        previous_state = players_by_conn.get(self.conn_id)
                        if previous_state:
                            previous_cluster = previous_state.cluster
                            previous_scene = previous_state.scene
                        players_by_conn[self.conn_id] = PlayerState(
                            self.conn_id, player_id, name, cluster, scene,
                            x, y, z, facing, moving,
                            design, color1, color2, status,
                            follower_enabled, follower_x, follower_y, follower_facing, follower_moving, follower_sprite, follower_flip_x, time.time(),
                            riding_broom=riding_broom,
                            water_walking=water_walking,
                            jumping_or_bouncing=jumping_or_bouncing,
                            bouncing_name=bouncing_name
                        )
                    if previous_scene and (previous_cluster != cluster or previous_scene != scene):
                        unlock_personal_spawns_for_owner_left_map(player_id, previous_cluster, previous_scene, reason="OWNER_LEFT_MAP")
                        clear_world_spawn_map_if_empty(previous_cluster, previous_scene, reason="LAST_PLAYER_LEFT_MAP")
                        world_spawn_snapshot_cache_by_conn.pop(self.conn_id, None)
                        try:
                            self.send_line(make_world_spawn_snapshot(self.conn_id))
                        except Exception as e:
                            print(f"[world-spawn-map-change-snapshot-error] {self.conn_id}: {e}")

                    # Server-owned visible overworld spawns are driven by WORLD_SPAWNZONES reports.

                    # VMS-style: normal POS snapshots are sent by the fixed-rate broadcaster.
                    # Map-change POS gets an immediate WORLD_SPAWNS repair above so clients clear old map clutter.

                elif kind == "VIS_EVENT":
                    # VIS_EVENT|id|name|cluster|scene|eventType|x|y|z|facing|dir|riding|water|jump|bouncing
                    if len(parts) < 11:
                        raise ValueError("VIS_EVENT packet too short")

                    player_id = dec(parts[1])
                    name = dec(parts[2])
                    cluster = dec(parts[3])
                    scene = dec(parts[4])
                    event_type = dec(parts[5])
                    x = float(parts[6])
                    y = float(parts[7])
                    z = float(parts[8])
                    facing = int(parts[9])
                    event_dir = int(parts[10])
                    riding_broom = parse_bool(parts[11]) if len(parts) > 11 and parts[11] else False
                    water_walking = parse_bool(parts[12]) if len(parts) > 12 and parts[12] else False
                    jumping_or_bouncing = parse_bool(parts[13]) if len(parts) > 13 and parts[13] else False
                    bouncing_name = dec(parts[14]) if len(parts) > 14 and parts[14] else ""

                    with players_lock:
                        previous = players_by_conn.get(self.conn_id)
                        if previous:
                            previous.riding_broom = riding_broom
                            previous.water_walking = water_walking
                            previous.jumping_or_bouncing = jumping_or_bouncing
                            previous.bouncing_name = bouncing_name
                            previous.last_seen = time.time()

                    out = "VIS_EVENT|" + "|".join([
                        enc(player_id), enc(name), enc(cluster), enc(scene), enc(event_type),
                        f"{x:.4f}", f"{y:.4f}", f"{z:.4f}",
                        str(int(facing)), str(int(event_dir)),
                        "1" if riding_broom else "0",
                        "1" if water_walking else "0",
                        "1" if jumping_or_bouncing else "0",
                        enc(bouncing_name),
                    ])
                    broadcast_visual_event(self.conn_id, out, cluster, scene)

                elif kind == "STEP":
                    # STEP|id|name|cluster|scene|originX|originY|targetX|targetY|facing|duration|design|color1|color2|seq
                    if len(parts) < 15:
                        raise ValueError("STEP packet too short")

                    player_id = dec(parts[1])
                    name = dec(parts[2])
                    cluster = dec(parts[3])
                    scene = dec(parts[4])
                    ox = float(parts[5])
                    oy = float(parts[6])
                    tx = float(parts[7])
                    ty = float(parts[8])
                    facing = int(parts[9])
                    duration = float(parts[10])
                    design = int(parts[11]) if parts[11] else 0
                    color1 = int(parts[12]) if parts[12] else 0
                    color2 = int(parts[13]) if parts[13] else 0
                    seq = parts[14]
                    riding_broom = parse_bool(parts[15]) if len(parts) > 15 and parts[15] else False

                    previous_cluster = ""
                    previous_scene = ""
                    with players_lock:
                        previous = players_by_conn.get(self.conn_id)
                        status = getattr(previous, "status", "available") if previous else "available"
                        if previous:
                            previous_cluster = previous.cluster
                            previous_scene = previous.scene
                        players_by_conn[self.conn_id] = PlayerState(
                            self.conn_id, player_id, name, cluster, scene,
                            tx, ty, ty * 0.01, facing, True,
                            design, color1, color2, status,
                            getattr(previous, "follower_enabled", False) if previous else False,
                            getattr(previous, "follower_x", 0.0) if previous else 0.0,
                            getattr(previous, "follower_y", 0.0) if previous else 0.0,
                            getattr(previous, "follower_facing", 0) if previous else 0,
                            getattr(previous, "follower_moving", False) if previous else False,
                            getattr(previous, "follower_sprite", "") if previous else "",
                            getattr(previous, "follower_flip_x", False) if previous else False,
                            time.time(),
                            riding_broom=riding_broom,
                            water_walking=getattr(previous, "water_walking", False) if previous else False,
                            jumping_or_bouncing=getattr(previous, "jumping_or_bouncing", False) if previous else False,
                            bouncing_name=getattr(previous, "bouncing_name", "") if previous else ""
                        )
                    if previous_scene and (previous_cluster != cluster or previous_scene != scene):
                        unlock_personal_spawns_for_owner_left_map(player_id, previous_cluster, previous_scene, reason="OWNER_LEFT_MAP")
                        clear_world_spawn_map_if_empty(previous_cluster, previous_scene, reason="LAST_PLAYER_LEFT_MAP")
                        world_spawn_snapshot_cache_by_conn.pop(self.conn_id, None)
                        try:
                            self.send_line(make_world_spawn_snapshot(self.conn_id))
                        except Exception as e:
                            print(f"[world-spawn-step-map-change-snapshot-error] {self.conn_id}: {e}")

                    out = "STEP|" + "|".join([
                        enc(player_id), enc(name), enc(cluster), enc(scene),
                        f"{ox:.4f}", f"{oy:.4f}", f"{tx:.4f}", f"{ty:.4f}",
                        str(int(facing)), f"{duration:.4f}",
                        str(int(design)), str(int(color1)), str(int(color2)), str(seq),
                        "1" if riding_broom else "0"
                    ])
                    broadcast_step(self.conn_id, out, cluster, scene)

                elif kind == "RANKED_BATTLE_REQ":
                    # RANKED_BATTLE_REQ|fromId|fromName|cluster|scene|toId|toName|teamPayload
                    if len(parts) < 8:
                        raise ValueError("RANKED_BATTLE_REQ packet too short")

                    from_id = dec(parts[1])
                    from_name = dec(parts[2])
                    cluster = dec(parts[3])
                    scene = dec(parts[4])
                    to_id = dec(parts[5])
                    to_name = dec(parts[6])
                    team_payload = parts[7]

                    send_ranked_battle_request(self.conn_id, from_id, from_name, cluster, scene, to_id, to_name, team_payload)

                elif kind == "RANKED_BATTLE_ACCEPT":
                    # RANKED_BATTLE_ACCEPT|requesterId|acceptorId|acceptorName|teamPayload|rankedBattleId
                    if len(parts) < 6:
                        raise ValueError("RANKED_BATTLE_ACCEPT packet too short")

                    requester_id = dec(parts[1])
                    acceptor_id = dec(parts[2])
                    acceptor_name = dec(parts[3])
                    team_payload = parts[4]
                    ranked_id = dec(parts[5])

                    send_ranked_battle_accept(self.conn_id, requester_id, acceptor_id, acceptor_name, team_payload, ranked_id)

                elif kind == "RANKED_BATTLE_DECLINE":
                    # RANKED_BATTLE_DECLINE|requesterId|declinerId|declinerName|rankedBattleId
                    if len(parts) < 5:
                        raise ValueError("RANKED_BATTLE_DECLINE packet too short")

                    requester_id = dec(parts[1])
                    decliner_id = dec(parts[2])
                    decliner_name = dec(parts[3])
                    ranked_id = dec(parts[4])

                    send_ranked_battle_decline(self.conn_id, requester_id, decliner_id, decliner_name, ranked_id)

                elif kind == "RANKED_BATTLE_RESULT":
                    # RANKED_BATTLE_RESULT|fromId|toId|rankedBattleId|WIN|LOSS
                    if len(parts) < 5:
                        raise ValueError("RANKED_BATTLE_RESULT packet too short")

                    from_id = dec(parts[1])
                    to_id = dec(parts[2])
                    ranked_id = dec(parts[3])
                    result = dec(parts[4])

                    receive_ranked_battle_result(self.conn_id, from_id, to_id, ranked_id, result)

                elif kind == "BATTLE_REQ":
                    # BATTLE_REQ|fromId|fromName|cluster|scene|toId|teamPayload
                    if len(parts) < 7:
                        raise ValueError("BATTLE_REQ packet too short")

                    from_id = dec(parts[1])
                    from_name = dec(parts[2])
                    cluster = dec(parts[3])
                    scene = dec(parts[4])
                    to_id = dec(parts[5])
                    team_payload = parts[6]

                    send_battle_request(self.conn_id, from_id, from_name, cluster, scene, to_id, team_payload)

                elif kind == "BATTLE_ACCEPT":
                    # BATTLE_ACCEPT|requesterId|acceptorId|acceptorName|teamPayload
                    if len(parts) < 5:
                        raise ValueError("BATTLE_ACCEPT packet too short")

                    requester_id = dec(parts[1])
                    acceptor_id = dec(parts[2])
                    acceptor_name = dec(parts[3])
                    team_payload = parts[4]

                    send_battle_accept(self.conn_id, requester_id, acceptor_id, acceptor_name, team_payload)

                elif kind == "BATTLE_DECLINE":
                    # BATTLE_DECLINE|requesterId|declinerId|declinerName
                    if len(parts) < 4:
                        raise ValueError("BATTLE_DECLINE packet too short")

                    requester_id = dec(parts[1])
                    decliner_id = dec(parts[2])
                    decliner_name = dec(parts[3])

                    send_battle_decline(self.conn_id, requester_id, decliner_id, decliner_name)

                elif kind == "BATTLE_BUSY_REPLY":
                    # BATTLE_BUSY_REPLY|requesterId|busyId|busyName|status
                    if len(parts) < 5:
                        raise ValueError("BATTLE_BUSY_REPLY packet too short")

                    requester_id = dec(parts[1])
                    busy_id = dec(parts[2])
                    busy_name = dec(parts[3])
                    status = dec(parts[4])

                    send_battle_busy_reply(self.conn_id, requester_id, busy_id, busy_name, status)

                elif kind == "BATTLE_SNAPSHOT":
                    # BATTLE_SNAPSHOT|fromId|toId|battleId|slot,hp,maxhp,level,spd;...
                    if len(parts) < 5:
                        raise ValueError("BATTLE_SNAPSHOT packet too short")

                    from_id = dec(parts[1])
                    to_id = dec(parts[2])
                    battle_id = dec(parts[3])
                    payload = dec(parts[4])

                    register_battle_snapshot(self.conn_id, from_id, to_id, battle_id, payload)

                elif kind == "BATTLE_CMD":
                    # BATTLE_CMD|fromId|toId|battleId|actionSeq|actorSlot|moveSlot|targetSlot|targetAlly
                    if len(parts) < 9:
                        raise ValueError("BATTLE_CMD packet too short")

                    from_id = dec(parts[1])
                    to_id = dec(parts[2])
                    battle_id = dec(parts[3])
                    action_seq = int(parts[4])
                    actor_slot = int(parts[5])
                    move_slot = int(parts[6])
                    target_slot = int(parts[7])
                    target_ally = parse_bool(parts[8])

                    send_battle_cmd(self.conn_id, from_id, to_id, battle_id, action_seq, actor_slot, move_slot, target_slot, target_ally)

                elif kind == "BATTLE_HIT":
                    # BATTLE_HIT|fromId|toId|battleId|actionSeq|actorSlot|moveSlot|targetSide|targetSlot|amount|hpAfter|shieldAfter|crit
                    if len(parts) < 13:
                        raise ValueError("BATTLE_HIT packet too short")

                    from_id = dec(parts[1])
                    to_id = dec(parts[2])
                    battle_id = dec(parts[3])
                    action_seq = int(parts[4])
                    actor_slot = int(parts[5])
                    move_slot = int(parts[6])
                    target_side = dec(parts[7])
                    target_slot = int(parts[8])
                    amount = int(parts[9])
                    hp_after = int(parts[10])
                    shield_after = int(parts[11])
                    crit = dec(parts[12])

                    send_battle_hit(self.conn_id, from_id, to_id, battle_id, action_seq, actor_slot, move_slot, target_side, target_slot, amount, hp_after, shield_after, crit)

                elif kind == "BATTLE_DONE":
                    # BATTLE_DONE|fromId|toId|battleId|actionSeq|actorSlot|moveSlot
                    if len(parts) < 7:
                        raise ValueError("BATTLE_DONE packet too short")

                    from_id = dec(parts[1])
                    to_id = dec(parts[2])
                    battle_id = dec(parts[3])
                    action_seq = int(parts[4])
                    actor_slot = int(parts[5])
                    move_slot = int(parts[6])

                    send_battle_done(self.conn_id, from_id, to_id, battle_id, action_seq, actor_slot, move_slot)

                elif kind == "BATTLE_END":
                    # BATTLE_END|fromId|toId|battleId|reason
                    if len(parts) < 4:
                        raise ValueError("BATTLE_END packet too short")

                    from_id = dec(parts[1])
                    to_id = dec(parts[2])
                    battle_id = dec(parts[3])
                    reason = dec(parts[4]) if len(parts) > 4 else "completed"

                    end_battle_session(from_id, to_id, battle_id, reason)

                elif kind == "BATTLE_STATE":
                    # BATTLE_STATE|fromId|toId|battleId|payload
                    if len(parts) < 5:
                        raise ValueError("BATTLE_STATE packet too short")

                    from_id = dec(parts[1])
                    to_id = dec(parts[2])
                    battle_id = dec(parts[3])
                    payload = parts[4]

                    send_battle_state(self.conn_id, from_id, to_id, battle_id, payload)

                elif kind == "WORLD_SPAWNZONES":
                    # WORLD_SPAWNZONES|playerId|cluster|scene|signature|zone0,x,y,z;zone1,x,y,z
                    if len(parts) < 6:
                        raise ValueError("WORLD_SPAWNZONES packet too short")
                    player_id = dec(parts[1])
                    cluster = dec(parts[2])
                    scene = dec(parts[3])
                    signature = dec(parts[4])
                    zone_body = parts[5]
                    receive_world_spawn_zones(self.conn_id, player_id, cluster, scene, signature, zone_body)

                elif kind == "WORLD_SPAWN_GEN_RESULT":
                    # WORLD_SPAWN_GEN_RESULT|playerId|requestId|monId|monKey|shiny|level|versionRequirement
                    if len(parts) < 7:
                        raise ValueError("WORLD_SPAWN_GEN_RESULT packet too short")
                    player_id = dec(parts[1])
                    request_id = dec(parts[2])
                    mon_id = int(parts[3]) if parts[3] else -1
                    mon_key = dec(parts[4])
                    shiny = parse_bool(parts[5])
                    level = int(parts[6]) if parts[6] else 1
                    version_requirement = dec(parts[7]) if len(parts) > 7 else "None"
                    complete_world_spawn_generation(self.conn_id, player_id, request_id, mon_id, mon_key, shiny, level, version_requirement)

                elif kind == "WORLD_SPAWN_PERSONAL_ADD":
                    # WORLD_SPAWN_PERSONAL_ADD|playerId|cluster|scene|slot,x,y,z,monId,shiny,level,monKey,monSaveB64,versionRequirement;...
                    if len(parts) < 5:
                        raise ValueError("WORLD_SPAWN_PERSONAL_ADD packet too short")
                    player_id = dec(parts[1])
                    cluster = dec(parts[2])
                    scene = dec(parts[3])
                    records_body = parts[4]
                    receive_personal_encounter_spawns(self.conn_id, player_id, cluster, scene, records_body)

                elif kind == "WORLD_SPAWN_CLAIM":
                    # WORLD_SPAWN_CLAIM|playerId|spawnId
                    if len(parts) < 3:
                        raise ValueError("WORLD_SPAWN_CLAIM packet too short")
                    player_id = dec(parts[1])
                    spawn_id = dec(parts[2])
                    claim_world_spawn(self.conn_id, player_id, spawn_id)

                elif kind == "WORLD_SPAWN_CATCH_START":
                    # WORLD_SPAWN_CATCH_START|playerId|spawnId
                    if len(parts) < 3:
                        raise ValueError("WORLD_SPAWN_CATCH_START packet too short")
                    player_id = dec(parts[1])
                    spawn_id = dec(parts[2])
                    start_world_spawn_catch_animation(self.conn_id, player_id, spawn_id)

                elif kind == "WORLD_SPAWN_RESULT":
                    # WORLD_SPAWN_RESULT|playerId|spawnId|caught|failed
                    if len(parts) < 4:
                        raise ValueError("WORLD_SPAWN_RESULT packet too short")
                    player_id = dec(parts[1])
                    spawn_id = dec(parts[2])
                    result = dec(parts[3])
                    finish_world_spawn_claim(self.conn_id, player_id, spawn_id, result)

                elif kind == "PING":
                    with players_lock:
                        p = players_by_conn.get(self.conn_id)
                        if p is not None:
                            p.last_seen = time.time()
                    self.send_line("PONG")

                elif kind == "BYE":
                    break

            except Exception as e:
                print(f"[packet-error] {e} :: {line!r}")
                self.send_line(f"ERR|{enc(str(e))}")

class ThreadedTCPServer(socketserver.ThreadingMixIn, socketserver.TCPServer):
    allow_reuse_address = True
    request_queue_size = 128
    daemon_threads = True


def maintain_battle_session_disconnects(now=None):
    # Independent active-battle watchdog. Snapshot-based stale cleanup is not enough
    # for PvP because a remaining player can be stuck in battle while the opponent
    # connection is gone or stale, and may not receive a normal disconnect callback.
    if now is None:
        now = time.time()
    to_remove = []
    to_notify = []
    with battle_sessions_lock:
        for bid, sess in list(battle_sessions.items()):
            players = list(getattr(sess, 'players', []) or [])
            for pid in players:
                conn = find_conn_by_player_id(pid)
                state = get_player_by_conn(conn) if conn else None
                if state is None:
                    to_remove.append(bid)
                    for other_id in players:
                        if str(other_id) != str(pid):
                            to_notify.append((bid, pid, other_id, 'opponent-disconnected-watchdog'))
                    break
        for bid in set(to_remove):
            battle_sessions.pop(bid, None)
    for bid, left_id, other_id, reason in to_notify:
        other_conn = find_conn_by_player_id(other_id)
        if other_conn:
            send_to_conn(other_conn, "BATTLE_OPPONENT_LEFT|" + "|".join([
                enc(str(bid)), enc(str(left_id)), enc(reason)
            ]))
            print(f"[battle-watchdog-opponent-left] battle={bid} left={left_id} notify={other_id} reason={reason}")


def snapshot_loop(snapshot_hz):
    interval = 1.0 / max(1.0, float(snapshot_hz))
    print(f"[snapshots] broadcasting at {snapshot_hz:g} Hz")
    while True:
        start = time.time()
        maintain_world_spawn_maps()
        maintain_battle_session_disconnects(start)

        with players_lock:
            conn_ids = list(handlers_by_conn.keys())

        for cid in conn_ids:
            try:
                send_to_conn(cid, make_snapshot(cid))
                spawn_snapshot = maybe_make_world_spawn_snapshot(cid, start)
                if spawn_snapshot is not None:
                    send_to_conn(cid, spawn_snapshot)
            except Exception as e:
                print(f"[snapshot-error] {cid}: {e}")

        elapsed = time.time() - start
        time.sleep(max(0.001, interval - elapsed))

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=61528)
    parser.add_argument("--snapshot-hz", type=float, default=30.0)
    args = parser.parse_args()

    print("MMOnsterpatch Server v0.18.7-native-shoo-away-postfix-fix")
    print(f"Listening on {args.host}:{args.port}")
    print("Ctrl+C to stop.")

    with ThreadedTCPServer((args.host, args.port), Handler) as server:
        threading.Thread(target=snapshot_loop, args=(args.snapshot_hz,), daemon=True).start()
        server.serve_forever()

if __name__ == "__main__":
    main()
