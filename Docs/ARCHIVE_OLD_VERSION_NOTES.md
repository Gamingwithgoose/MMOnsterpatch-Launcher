# Archived old per-version notes

These older README/WHAT_CHANGED/TESTING files were consolidated during v0.15.5 so the workspace root stays clean.


---

## `README_YOLO_v0.12.11.3.txt`

```text
MMOnsterpatch YOLO v0.12.11.3 Full Client Source Recovery

This package fixes the v0.12.11.2 client compile failure caused by an incomplete/truncated GTSRuntimeHost.cs.

The GTSRuntimeHost.cs file has been restored from the full client source baseline that contains the missing Trading Post/mail/helper methods such as SearchListingsCoroutine, Disconnect, RefreshMailCountCoroutine, FindMonSlot, DrawMailPanel, MyListingsCoroutine, etc.

No new auth architecture was added in this package. This is a build recovery package only: no Steam auth in the in-game login path, and no fallback to old client DLLs.

```


---

## `README_YOLO_v0.12.11.txt`

```text
MMOnsterpatch YOLO v0.12.11 - No-Steam Account Session Recovery Build

This build is intentionally narrow:
- Based on the v0.12.9 launcher/server manager layout.
- Keeps the existing Online Mode staging/restoration design.
- Includes the client patcher source under Client\MMOnsterpatchOfficialServerPatcher.
- Builds the client patcher as a required step. No fallback to an old DLL.
- Removes Steam/OpenID as a client/server login requirement for this test path.
- Online Mode uses the MMOnsterpatch launcher account session only.

Expected flow:
1. Register/login through Account Website / Launcher.
2. Launcher requests /api/account/game-ticket.
3. Launcher stages the online payload.
4. Launcher writes BepInEx\config\goose.monsterpatch.gts.client.cfg into the staged game folder.
5. In-game Log In loads that cached account session and sends SESSION_LOGIN.
6. Current Official Server accepts the launcher account session through auth.db/json mirror.

Steam auth is disabled in this build on purpose. Add it back later only after launcher account login and in-game SESSION_LOGIN are proven working.

```


---

## `README_YOLO_v0.12.7.txt`

```text
MMOnsterpatch YOLO v0.12.8 - Account/Status Foundation Workspace

Run CLEAN_AND_BUILD_ALL.bat from this root folder.

Outputs:
- Releases\Launcher\MMOnsterpatchLauncher.exe
- Releases\Server\MMOnsterpatchRepackManager.exe plus all server runtime folders beside it

Current public routing:
- account.gamingwithgoose.com -> Cloudflare Tunnel -> http://127.0.0.1:8080
- mon-auth.gamingwithgoose.com -> Cloudflare Tunnel -> http://127.0.0.1:61527
- mon.gamingwithgoose.com -> DNS-only/raw TCP -> 61526
- mmo.gamingwithgoose.com -> DNS-only/raw TCP -> 61528 and 61529

Current live ports:
- 61526 = Trading Post socket
- 61527 = Steam OpenID / mon-auth HTTP
- 61528 = MMO multiplayer
- 61529 = Social/chat/guild

This build adds the live server status/player-count foundation but does not finish real account credentials yet.

```


---

## `README_YOLO_v0.12.8.txt`

```text
Fresh account credential foundation build. See WHAT_CHANGED_YOLO_v0.12.8.txt and TESTING_STEPS_YOLO_v0.12.8.txt.

```


---

## `README_YOLO_v0.12.9.txt`

```text
MMOnsterpatch YOLO v0.12.9

Focus: move from website JSON-only credentials toward DB-backed account storage and pass launcher account sessions into the game as the official cached session.

Cloudflare routes remain:
account.gamingwithgoose.com -> http://127.0.0.1:8080
mon-auth.gamingwithgoose.com -> http://127.0.0.1:61527

Current ports remain:
61526 Trading Post
61527 Steam OpenID/mon-auth HTTP callback
61528 MMO
61529 Social/chat/guild

```


---

## `README_YOLO_v0.13.1_UIChange.txt`

```text
MMOnsterpatch YOLO v0.13.1 - UI Change

Baseline:
- Built from confirmed v0.13.0 Stable / v0.12.11.4 LauncherAccountGateTest.
- Existing CLEAN_AND_BUILD_ALL.bat -> Clean_And_Build_All.ps1 build flow preserved.

Launcher UI changes:
- Discord and GitHub top buttons now use the same dark button fill as Profile/Info instead of purple fill.
- Profile button no longer opens the login window when already logged in.
- Signed-in Profile dropdown now shows profile picture placeholder, account/display name, username/email line, View Profile, Manage Account, and Log Out.
- Profile dropdown is taller/wider so all account actions fit cleanly.
- Launcher alerts/info/errors now use a custom dark/purple themed dialog instead of stock Windows MessageBox.

Account website support:
- Added /launcher/profile?token=... and /launcher/manage-account?token=... bridge routes so launcher buttons can open browser pages from the saved launcher session.
- Added /profile page showing profile picture placeholder, username, display name, email, creation date, and total characters.
- Added /manage-account page showing profile picture placeholder/edit note, username edit, display name edit, password change, MFA placeholder, and Save Changes.
- Username/display name/password save is wired to auth.db; MFA/profile-picture upload remain placeholder UI for the later security/profile pass.

No intentional changes to:
- Online game-login handoff confirmed in v0.13.0 Stable.
- Client patcher gameplay/auth bridge.
- Current ports/subdomains.
- Root build flow.

```


---

## `README_YOLO_v0.13.2_SocialsFix.txt`

```text
MMOnsterpatch YOLO v0.13.2 - SocialsFix

Built from confirmed v0.13.0 Stable plus v0.13.1 UI Change/TextCleanup.

Purpose:
- Fix MMO/social/chat connection after loading an Official Server online save.
- Preserve the launcher-account-gated flow: website account -> launcher login -> Online Play -> title Log In -> online save selector.
- Let social/chat use the same launcher account handoff instead of requiring the old Trading Post/Steam socket login flag first.

Build:
Run CLEAN_AND_BUILD_ALL.bat from the repack root.

Important:
- No root BAT/PowerShell build flow logic was changed. Only visible version banner text was updated.
- No ports/subdomains were changed.
- Steam auth remains disabled/deferred while account flow is stabilized.

```


---

## `README_YOLO_v0.13.3_TradingPostLayoutFix.txt`

```text
MMOnsterpatch YOLO v0.13.3 - TradingPostLayoutFix

Baseline: v0.13.2 SocialsFix stable.

Purpose:
- Restore the old tuned Trading Post layout defaults when launching through the new WPF launcher.
- Preserve Trading Post window size/position and other layout config values across online launches.
- Stop launcher Online Play from replacing the entire goose.monsterpatch.gts.client.cfg with only auth/session keys.

Build:
Run CLEAN_AND_BUILD_ALL.bat from the repack root.

```


---

## `README_MARIADB_HOTFIX1.md`

```text
# MMOnsterpatch v0.15.0 MariaDBOfficial Hotfix 1

Fixes the first live MariaDB cutover authentication issue:

```text
OperationalError: (2059, "Authentication plugin 'auth_gssapi_client' not configured")
```

Cause:
- The server was trying to connect as MariaDB `root`.
- On this Windows MariaDB install, `root` can use GSSAPI/Windows authentication.
- PyMySQL should not use that account for the game server.

Fix:
- Official Server MariaDB bridge now reads `Server/Config/database.ini` directly.
- Default DB user is now `mmonsterpatch`, not `root`.
- Added `CREATE_MARIADB_USER_SQL3307.bat` to create/grant a dedicated MariaDB user and update `database.ini`.

## How to use

1. Extract/build this workspace.
2. Run `CREATE_MARIADB_USER_SQL3307.bat` from the workspace root, or from the built server root if copied there.
3. Enter a new password for the `mmonsterpatch` DB user.
4. Enter the MariaDB admin/root password when prompted by `mysql.exe`.
5. Start `Server/MMOnsterpatchRepackManager.exe`.

The DB config should end up as:

```ini
[Database]
Provider=MariaDB
Host=127.0.0.1
Port=3307
User=mmonsterpatch
Password=<your password>
AuthDatabase=mmonsterpatch_auth
TradingPostDatabase=mmonsterpatch_tradingpost
SocialDatabase=mmonsterpatch_social
FilesDatabase=mmonsterpatch_files
```

```


---

## `README_MARIADB_NATIVE_v0.15.1.md`

```text
# MMOnsterpatch v0.15.1 MariaDBNative

This build makes MariaDB the official runtime database backend for the Account Website and Python Official Server.

## Machine layout

- Workstation: build only.
- Server: runs MariaDB and `MMOnsterpatchRepackManager.exe`.

## Build on workstation

Run:

```bat
BUILD_MARIADB_NATIVE_OFFICIAL_AND_COPY_SERVER.bat
```

or:

```bat
CLEAN_AND_BUILD_ALL.bat
```

Copy the generated `Releases\Server` folder to the server machine.

## Server requirements

On the server machine:

```bat
py -m pip install PyMySQL
```

MariaDB should be listening on port `3307`.

## Config

Edit the deployed server file:

```text
Server\Config\database.ini
```

Expected values:

```ini
[Database]
Provider=MariaDB
Host=127.0.0.1
Port=3307
User=mmonsterpatch
Password=YOUR_PASSWORD_HERE
AuthDatabase=mmonsterpatch_auth
TradingPostDatabase=mmonsterpatch_tradingpost
SocialDatabase=mmonsterpatch_social
FilesDatabase=mmonsterpatch_files
```

`127.0.0.1` is correct because MariaDB and MMOnsterpatch server run on the same server machine.

## Optional helper

On the server machine, you can run:

```bat
CREATE_OR_UPDATE_MARIADB_USER_SQL3307_SERVER_SIDE.bat
```

This creates/updates the dedicated `mmonsterpatch` MariaDB user and updates `database.ini` if it is beside the BAT.

## Runtime folders

The clean runtime still needs:

```text
Server\MMOnsterpatchRepackManager.exe
Server\AccountWebsiteServer\ConfigServer\OfficialServerServer\PatchPayloadServer\LogsServer\Backups```

`OfficialServer\data\` may still be created for live non-database files such as `runtime_status.json`, chat logs, user reports, and archived character snapshots. That is expected.

The old active database files are no longer runtime requirements:

```text
auth.db
accounts.json
launcher_sessions.json
social.db
bank.db
server_modules\dataank.db
```

Keep them only in your external backup.

```


---

## `README_MARIADB_NATIVE_v0.15.4_SAVE_PERF.md`

```text
# MMOnsterpatch v0.15.4 MariaDBNative Save Performance

This build assumes MariaDB is already the official runtime storage on the server and the imported databases exist on port `3307`.

## Build

On the workstation, run:

```bat
BUILD_MARIADB_NATIVE_OFFICIAL_AND_COPY_SERVER.bat
```

Then copy `Releases\Server` to the server machine.

## Runtime database config

On the server machine, confirm:

```ini
[Database]
Provider=MariaDB
Host=127.0.0.1
Port=3307
User=mmonsterpatch
Password=YOUR_PASSWORD
AuthDatabase=mmonsterpatch_auth
TradingPostDatabase=mmonsterpatch_tradingpost
SocialDatabase=mmonsterpatch_social
FilesDatabase=mmonsterpatch_files
```

## New client performance config

The patcher adds these settings to `BepInEx\config\goose.monsterpatch.officialserver.cfg` after launching once:

```ini
[Official Server Network]
AsyncOfficialSaveUploads = true
FinalSaveFlushTimeoutMs = 2500
WorldRatesRefreshMinIntervalSeconds = 30
```

`AsyncOfficialSaveUploads=true` is the main freeze reduction. It keeps blocking local disk writes while queueing normal online save uploads to a background worker.

`FinalSaveFlushTimeoutMs` controls how long disconnect/quit waits for the newest queued save. Increase for maximum safety on slow links; decrease if exit still feels too hitchy.

`WorldRatesRefreshMinIntervalSeconds` avoids duplicate rates fetches during title login -> save-select -> load flow.

```


---

## `README_MARIADB_OFFICIAL_v0.15.0.md`

```text
# MMOnsterpatch v0.15.0 MariaDBOfficial

This package is the source cutover after the MariaDB migration kit was successfully imported.

## Required setup

1. MariaDB must be running on `127.0.0.1:3307`.
2. The migration kit should already have imported:
   - `mmonsterpatch_auth`
   - `mmonsterpatch_tradingpost`
   - `mmonsterpatch_social`
   - `mmonsterpatch_files`
3. Install Python MariaDB client dependency on the server PC:

```bat
py -m pip install PyMySQL
```

4. Edit:

```text
Server\Config\database.ini
```

Set the real MariaDB password:

```ini
Password=YOUR_PASSWORD_HERE
```

## Build

Run from the workspace root:

```bat
CLEAN_AND_BUILD_ALL.bat
```

Then deploy:

```text
Releases\Server\
```

## Expected runtime layout

```text
Server\
  MMOnsterpatchRepackManager.exe
  AccountWebsite\
  Config\
  OfficialServer\
  PatchPayload\
  Logs\
  Backups\
```

## No longer required as live runtime DB storage

```text
Data\Accounts\auth.db
Data\Accounts\accounts.json
Data\Accounts\launcher_sessions.json
OfficialServer\data\social.db
OfficialServer\data\bank.db
OfficialServer\server_modules\data\bank.db
```

Keep your old folder as a backup until this version is tested.

## Notes

`OfficialServer\data\` still exists because the server writes non-database runtime files there, such as `runtime_status.json`, chat logs, reports, and archived character exports. That is not the old DB system.

```


---

## `WHAT_CHANGED_YOLO_v0.12.11.1.txt`

```text
MMOnsterpatch YOLO v0.12.11.1 - No-Steam Account Session Build Fix

This is not a gameplay/auth redesign. It repairs the client build reference flow from v0.12.11.

Fixes:
- Always prepares Client\lib before building the client patcher, matching the old working flow.
- Clears stale partial DLL references from Client\lib first.
- Verifies Assembly-CSharp, BepInEx, Harmony, Cecil, Unity modules, and TextMeshPro exist before dotnet build.
- Removes command-line MONSTERPATCH_LIB_DIR passing from the PowerShell build to avoid bracket/trailing-backslash reference resolution problems.
- Updates the client csproj to resolve references directly from $(MSBuildProjectDirectory)\..\lib by default.

If Monsterpatch is not in the default Steam path, run:
  set "MONSTERPATCH_GAME_DIR=C:\Path\To\Monsterpatch"
  CLEAN_AND_BUILD_ALL.bat

```


---

## `WHAT_CHANGED_YOLO_v0.12.11.2.txt`

```text
v0.12.11.2 fixes the build script invocation of Client\prepare_lib_from_game.bat. The previous script called cmd.exe /c with a bracketed path and Windows split the path at [MMOnsterpatch]. This version invokes the .bat through PowerShell call operator using the path variable directly. No auth redesign changes were made from v0.12.11.1.

```


---

## `WHAT_CHANGED_YOLO_v0.12.11.4.txt`

```text
MMOnsterpatch YOLO v0.12.11.4 - Launcher Account Gate Test

Purpose
- Make the custom title-screen Online Mode Log In button look for the launcher account handoff first.
- Stop requiring the old Steam/OpenID or Trading Post socket SESSION_LOGIN success flag before opening the online save slot selector.
- Keep the WoW-like target flow for this test:
  1. Create account on Account Website.
  2. Log into MMOnsterpatch Launcher.
  3. Launch Monsterpatch with Online selected.
  4. Click Log In on the custom title screen.
  5. Open the online save slot selector.

Changed source files
- Client/MMOnsterpatchOfficialServerPatcher/GTSRuntimeHost.cs
  - Added TryUseLauncherAccountSessionForAio().
  - Added launcher-account-backed session token selection.
  - For this test build, prefers stable AccountId (acct_...) over expiring aio_ session token for online save/account requests.
  - EnsureAioAuthenticatedCoroutine now accepts launcher account handoff locally before trying old socket session login.

- Client/MMOnsterpatchOfficialServerPatcher/OfficialServerSaveSelectNative.cs
  - Title-screen Log In flow now calls TryUseLauncherAccountSessionForAio() before old auth routine.
  - Native save-select Online switch and logout/reopen flow use the same launcher handoff check.
  - If launcher handoff exists, it skips old Steam/socket pre-auth and loads online slots.

- Server/OfficialServer/server_modules/mmonsterpatch_tradingpost_core.py
  - resolve_aio_session now accepts acct_ website account IDs as a temporary account identity token.
  - Launcher-created sessions no longer fail this test flow due to clock/expiry/IP mismatches.
  - This is intentional temporary auth loosening for flow testing.

- Server/OfficialServer/server_modules/mmonsterpatch_social_core.py
  - Updated rejected-session wording away from Steam-specific messages.

Not changed
- Root CLEAN_AND_BUILD_ALL.bat and Clean_And_Build_All.ps1 build flow were not changed.
- Launcher staging/restore flow was not changed.
- Account Website registration/login flow was not changed.
- Steam auth was not re-added.

Security note
- This build intentionally loosens auth/session validation to prove the account -> launcher -> game -> online save selector flow.
- After this flow works, Steam verification and stronger ticket/session validation should be added back deliberately.

```


---

## `WHAT_CHANGED_YOLO_v0.12.11.txt`

```text
What changed in v0.12.11

Recovery target:
- Stop changing broad architecture.
- Restore v0.12.9 launcher/server manager layout baseline.
- Make one direct login-path change: Steam/OpenID is removed from the test login path.

Client:
- Added full client source under Client\MMOnsterpatchOfficialServerPatcher.
- Client DLL version metadata updated to 0.12.11.
- GTSRuntimeHost no longer calls LoginWithSteamCoroutine from EnsureAioAuthenticatedCoroutine.
- LoginWithSteamCoroutine is retained only as a disabled stub so accidental old calls do not open Steam auth.
- In-game login now requires the launcher-written cached AIO/session token and sends SESSION_LOGIN.
- UI/status text no longer tells users to log in with Steam.

Launcher:
- Keeps Online Mode staging/restoration.
- Requests game-ticket/account session from Account Website.
- Writes goose.monsterpatch.gts.client.cfg into the staged game's real BepInEx\config folder after staging.
- Does not attempt Steam auth.

Server:
- Current Official Server keeps SESSION_LOGIN as the login endpoint.
- Steam/OpenID socket commands return disabled errors for this build.
- Same-IP session requirement default changed to 0 for account website/cloudflare + raw game socket testing.
- runtime_status.json writer now retries/falls back to reduce WinError 5 access denied spam.

Build:
- Client patcher build is required.
- No fallback to old MMOnsterpatchOfficialServerPatcher.dll.
- Releases\Client Files mirrors the exact payload the launcher stages, including followers and bottle DLLs.

```


---

## `WHAT_CHANGED_YOLO_v0.12.7.txt`

```text
MMOnsterpatch YOLO v0.12.8 - Account/Status Foundation

What changed:

1. Repack Manager Console / Events panel
- Removed the bottom micro-console behavior from the main workflow area in v0.12.6.
- Fixed the right-side Console / Events log box so the output control fills the right panel instead of staying as a tiny one-line box.
- Console / Events remains scrollable and uses readable green console text.

2. Repack Manager Online Users panel
- The left Online Users panel now reads OfficialServer\data\runtime_status.json.
- It displays the current online player count and a basic user list when the server exposes live players.
- Until players connect, it shows "No players online" instead of placeholder-only text.

3. Current Official Server runtime status
- Current Official Server now writes OfficialServer\data\runtime_status.json every ~2 seconds.
- This includes:
  - online true/false source data
  - playersOnline
  - basic player names/location/status from current MMO sessions
  - social client count
  - current port map: 61526/61527/61528/61529
  - current public DNS/URL map
- This is intentionally file-based for the first account/status foundation pass so the current Python server can feed the Account Website and Repack Manager without needing a new local service.

4. Account Website status API
- /api/launcher/status now reads the live runtime status file.
- If the Current Official Server is not running or the status file is stale, the endpoint reports online=false and playersOnline=0.
- Added /api/server/online-users for manager/future tooling.
- /health still reports the account website itself.

5. Launcher server status card
- The launcher now polls https://account.gamingwithgoose.com/api/launcher/status.
- If the endpoint reports online=true:
  - server dot is green
  - text says ONLINE
  - Players Online shows the live playersOnline count
- If endpoint is offline/unreachable/stale:
  - server dot is red
  - text says OFFLINE
  - Players Online shows 0

6. Online Play gate remains in place
- Offline Play still does not require launcher login.
- Online Play still requires placeholder launcher sign-in before it continues.
- This preserves the future direction: Online Play will later require real MMOnsterpatch account credentials/session.

7. Current ports remain locked
- 61526 = Trading Post socket
- 61527 = Steam OpenID / mon-auth HTTP
- 61528 = MMO multiplayer
- 61529 = Social/chat/guild
- Future Auth placeholder remains disabled/not part of the live path.

8. Start servers minimized
- Start servers minimized remains enabled by default in config.

Not changed yet:
- Real account registration/login is not finished yet.
- Steam auth is not yet moved to account creation only.
- Existing online saves/character slots are not migrated to account-credential ownership yet.
- Trading Post marketplace merge is not implemented yet.

```


---

## `WHAT_CHANGED_YOLO_v0.12.8.txt`

```text
MMOnsterpatch YOLO v0.12.8 - Account Credential Foundation

Added real first-pass account registration and login to Account Website.
Added website account pages: /register, /login, /account, /character/{id}.
Added launcher login against https://account.gamingwithgoose.com/api/account/login.
Online Play now requires a real account login and writes an account-backed AIO session config for the current official patcher.
Added JSON account/session store under Data/Accounts for this fresh setup stage.
Modified current Python server session resolution to accept Account Website launcher sessions and synthesize compatible current-server account rows.
Preserved Name#1234 public_handle character identity.
Added account character export so website account page can show characters/rank profile after they are created in-game.

Still future/not final:
- Steam verification/linking during registration.
- Email verification/password reset.
- Full website Trading Post/mailbox actions.

```


---

## `WHAT_CHANGED_YOLO_v0.12.9.txt`

```text
MMOnsterpatch YOLO v0.12.9 - Launcher Ticket + DB Foundation

Changed/added:
- Launcher Discord/GitHub top buttons now use the user-provided icons.
- Launcher now asks Account Website for a game-ticket/session before Online Play.
- Launcher writes exact cached-session config keys expected by the current official patcher:
  CachedOfficialSessionTokenBase64, CachedOfficialSessionExpiresUtc,
  CachedOfficialSteamID64, CachedOfficialAccountUUID.
- Also keeps older alias keys for diagnostics/backward compatibility.
- Account Website account storage now uses Data\Accounts\auth.db as the source of truth.
- Account Website mirrors accounts.json and launcher_sessions.json for compatibility with the current Python server/patcher bridge.
- Current Python server can read launcher sessions from auth.db directly, with JSON fallback.
- In-game/session-login failure text now tells users to log into the MMOnsterpatch Launcher instead of Steam.
- Added /api/account/game-ticket endpoint. First pass returns the current AIO-compatible session token as the game ticket.
- Did not ship live account/session files from the uploaded JSON because those contain active credentials/session tokens.

Still expected:
- If the current binary patcher still hangs on the in-game login button after these exact cached keys are written, the next step is a patcher-source rebuild to bypass the Steam coroutine and call SESSION_LOGIN directly from launcher_session/config.

```


---

## `WHAT_CHANGED_YOLO_v0.13.1_UIChange_ServerValidatedLoginFix.txt`

```text
MMOnsterpatch YOLO v0.13.1 - UI Change Server-Validated Login Fix

Read-only/login-state correction based on user feedback after v0.13.1 UI Change BuildFix.

Changes:
- Launcher no longer trusts cached local signed-in state by itself.
- On startup, a remembered launcher account/session must validate against Account Website /api/account/me before the profile/user icon shows the signed-in menu.
- If validation fails, expires, is revoked, or the Account Website cannot be reached, the launcher clears the signed-in state and shows the user as offline/logged out.
- Clicking Online Play also verifies the launcher account session with the server before staging/launching Online Mode.
- Clicking the profile icon while signed in revalidates before opening the profile menu.
- Manage Account website page now shows username as read-only/permanent.
- Manage Account backend ignores submitted username changes, even if the HTML is manually edited.

Intentionally unchanged:
- Root CLEAN_AND_BUILD_ALL.bat / Clean_And_Build_All.ps1 build flow.
- v0.13.0 Stable game login handoff behavior.
- Client patcher title Log In -> online save slot flow.
- Ports/subdomains/current server routing.
- Account/character ownership model and Name#1234 character handle direction.

```


---

## `WHAT_CHANGED_YOLO_v0.13.2_SocialsFix.txt`

```text
MMOnsterpatch YOLO v0.13.2 - SocialsFix

Changed:
- SocialAIOPatcher ConnectAllServicesCoroutine now accepts the launcher account handoff as valid social/chat authentication.
- The old check required GTSRuntimeHost.IsLoggedInForAio(), which also required the old Trading Post socket client to be connected.
- In the v0.13.0 stable launcher-account flow, the title/save-selector path intentionally does not require that old socket pre-auth. Social/chat now matches that behavior.
- When the launcher account handoff is accepted locally, social now proceeds to connect MMO/social services and sends ACCOUNT_SLOT_HELLO with the launcher account token/account id.

Updated metadata:
- Project version metadata moved to v0.13.2_SocialsFix.
- Patch manifest version moved to 0.13.2.

Unchanged:
- Build BAT/PowerShell logic.
- DNS/subdomains and current ports.
- Website/launcher login flow.
- Online title Log In -> online save selector behavior.

```


---

## `WHAT_CHANGED_YOLO_v0.13.3_TradingPostLayoutFix.txt`

```text
MMOnsterpatch YOLO v0.13.3 - TradingPostLayoutFix

Changed:
- Launcher now seeds goose.monsterpatch.gts.client.cfg with the legacy/full Trading Post layout defaults when missing.
- Launcher now upserts only account/session/auth keys instead of overwriting the full config file.
- Launcher repairs known bad launcher/fallback layout values, including Create Listing dropdown/group alignment.
- Launcher preserves saved Trading Post window X/Y/Width/Height while keeping RememberWindowPosition/RememberWindowSize enabled.
- Client fallback/static defaults now match the old tuned Trading Post layout more closely.
- Version metadata moved to v0.13.3_TradingPostLayoutFix.

Not changed:
- Build BAT/PowerShell flow.
- Account launcher handoff flow.
- v0.13.2 SocialsFix MMO/social/chat auth behavior.
- Server ports/subdomains.

```


---

## `WHAT_CHANGED_YOLO_v0.13.4_FollowerAIOMergeTest.txt`

```text
MMOnsterpatch v0.13.4 - FollowerAIOMergeTest

Purpose:
- Merge the previously separate follower runtime source into the MMOnsterpatch AIO/client patcher.
- Stop staging separate Monsterpatch_Followers_Patcher.dll and Monsterpatch_Followers_Runtime.dll.
- Keep the same follower sync packet shape so the server protocol does not need to change.

Expected payload:
- BepInEx\patchers\MMOnsterpatchOfficialServerPatcher.dll owns online + follower behavior.
- BepInEx\config\goose.monsterpatch.followersruntime.cfg is staged as the follower defaults/config.
- No standalone follower DLLs are staged.

Server impact:
- No server packet/protocol rewrite should be required for this test.
- AIO still sends the same follower fields in the STEP/account movement packets.

```


---

## `WHAT_CHANGED_YOLO_v0.14.1_WorldSpawnLayeringFix.txt`

```text
MMOnsterpatch YOLO v0.14.2 - WorldSpawnLifecycleFix

Baseline: v0.14.0 stable (v0.13.4 FollowerAIOMergeTest confirmed working).

Changes:
- Server-owned overworld spawn visuals now use follower/remote-player style world layering.
- Removed the forced SpriteRenderer sortingOrder=1 behavior that made server spawns draw on top of map/object edges.
- Server spawn roots are normalized to z = y * 0.01f so they layer through the world like follower mons.
- Placeholder world spawns now use sortingOrder=0 instead of priority order.
- Follower item-find test chance reduced from 100% to 10% after the 250-step threshold.

No protocol/server rewrite was required. The server still owns spawn identity/position; this fix changes the AIO client-side visual presentation of those server-owned spawns.

Build flow unchanged: run CLEAN_AND_BUILD_ALL.bat.

```


---

## `WHAT_CHANGED_YOLO_v0.14.2_WorldSpawnLifecycleFix.txt`

```text
MMOnsterpatch YOLO v0.14.2 - WorldSpawnLifecycleFix

Focus:
- Server-owned overworld spawn lifecycle and population control.
- Prevent stacked same-tile overworld spawns after repeated map transitions.
- Support water spawn zones from the latest Monsterpatch update.

Server changes:
- Replaced passive one-roll/wait behavior with a per-map target population controller.
- Target visible spawn count scales from configured base density and active player count.
- Default base density remains 40%, +8% per extra player, capped at 70%.
- Minimum one visible spawn for active maps with spawn zones.
- Top-up interval lowered to 20 seconds.
- Catch/removal schedules a refill after 12 seconds.
- Last-player-leaves-map clears that map's server-owned spawns and pending generation immediately.
- Empty-map stale cleanup default lowered to 8 seconds for disconnect/timeout cleanup.
- Pending generation requests count as occupied tiles.
- Generation result rechecks tile occupancy before adding a spawn.
- Zone type grass/water is stored in pending requests/spawn records.

Client changes:
- Reports both spawnZone and spawnZoneWater.
- Sends zone type in WORLD_SPAWNZONES records.
- Accepts zone type in WORLD_SPAWN_GEN_REQ.
- Generates water-only encounter candidates for water zones instead of using player water-walking state.
- Server-owned spawn visuals dedupe same scene/tile before creating a new visual.

Build flow:
- Root BAT/PowerShell flow unchanged.

```


---

## `WHAT_CHANGED_YOLO_v0.14.3_FollowerAnimationTierFix.txt`

```text
MMOnsterpatch YOLO v0.14.3 - FollowerAnimationTierFix

Built from v0.14.2 WorldSpawnLifecycleFix.

Changes:
- Corrected merged AIO follower animation tier detection.
- Uses verified native Monsterpatch PlayerController static fields: isMoving, isRunning, ridingBroom.
- Removed moveMod from follower animation tier selection. moveMod affects movement/step behavior and caused walk/run animation to appear swapped.
- Keeps config multipliers: idle 0.75, walk 1, run 1.5, broom 2.
- Consolidated README/notes into Docs/README_CONSOLIDATED_v0.14.3.md.
- Added HANDOFF_v0.14.3_FollowerAnimationTierFix.json and Docs/ARTIFACTS_AND_BASELINES_v0.14.3.md.

No server lifecycle/protocol changes were made after v0.14.2.

```


---

## `WHAT_CHANGED_v0.13.3_FollowerSourceIncluded.txt`

```text
MMOnsterpatch v0.13.3 - Follower Source Included

This package is based on v0.13.3 TradingPostLayoutFix stable source.

Change:
- Added the actual Monsterpatch Followers source under:
  Client\Mods\Monsterpatch_Followers

Included:
- Monsterpatch_Followers_Patcher source
- Monsterpatch_Followers_Runtime source
- follower runtime config template
- standalone follower build/install scripts
- README/LICENSE from the follower source package

Not changed:
- Root CLEAN_AND_BUILD_ALL.bat / Clean_And_Build_All.ps1 flow
- Launcher payload DLLs
- Server files
- Client patcher/social/trading post code
- Ports/subdomains/account flow

Note:
The included follower projects are not yet wired into the root build. They are present so all launcher/mod source can live in one workspace. A later confirmed build-system update can make the root build compile and stage the follower DLLs automatically.

```


---

## `WHAT_CHANGED_v0.15.0_MariaDBOfficial.txt`

```text
MMOnsterpatch v0.15.0 - MariaDBOfficial

Purpose:
- Make MariaDB the official persistence backend after the successful migration/import.
- Stop depending on scattered SQLite/JSON runtime DB folders for account/session/social/trading storage.
- Keep the current working gameplay/server logic instead of rewriting broad systems.

Required MariaDB:
- Host: 127.0.0.1
- Port: 3307
- Databases already imported by the migration kit:
  - mmonsterpatch_auth
  - mmonsterpatch_tradingpost
  - mmonsterpatch_social
  - mmonsterpatch_files

Changed:
- Server/AccountWebsite/Program.cs now uses MySqlConnector/MariaDB instead of Microsoft.Data.Sqlite.
- AccountWebsite reads Server/Config/database.ini and uses mmonsterpatch_auth for accounts/sessions.
- AccountWebsite character/profile display now reads mmonsterpatch_social directly instead of account_characters.json.
- Server/AccountWebsite/MMOnsterpatch.AccountWebsite.csproj now references MySqlConnector and version metadata is v0.15.0.
- Python OfficialServer modules now import mmonsterpatch_mariadb_compat instead of sqlite3.
- Added server_modules/mmonsterpatch_mariadb_compat.py as a targeted sqlite3 API compatibility bridge backed by PyMySQL.
- Trading Post launcher session resolution now tries MariaDB auth first and no longer falls back to accounts.json/launcher_sessions.json.
- START_CURRENT_OFFICIAL_SERVER.bat now reads ../Config/database.ini and exports MMONSTERPATCH_DB_* environment variables before starting Python.
- Server/Config/database.ini defaults to MariaDB on port 3307.
- Root build script no longer publishes placeholder AuthServer/WorldServer or copies old SQL/Tools/Data persistence folders into Releases/Server.

Runtime dependency:
- Python server needs PyMySQL installed on the server machine:
  py -m pip install PyMySQL

Before running:
- Edit Server/Config/database.ini and set Password to the MariaDB password.
- Keep your old server folder backup. Do not delete it until v0.15.0 is tested.

Build:
- Run CLEAN_AND_BUILD_ALL.bat from the workspace root.
- Deploy the generated Releases/Server folder.

Expected clean runtime folders:
- MMOnsterpatchRepackManager.exe
- AccountWebsite/
- Config/
- OfficialServer/
- PatchPayload/
- Logs/
- Backups/

No old runtime DB requirements:
- Data/Accounts/auth.db is not required.
- Data/Accounts/accounts.json is not required.
- Data/Accounts/launcher_sessions.json is not required.
- OfficialServer/data/social.db is not required.
- OfficialServer/data/bank.db is not required.
- OfficialServer/server_modules/data/bank.db is not required.

Notes:
- OfficialServer/data/ is still created/used for non-DB runtime output like runtime_status.json, chat logs, reports, and archived character files.
- This build deliberately preserves current server logic and redirects persistence through MariaDB instead of inventing a new server architecture.

```


---

## `WHAT_CHANGED_v0.15.1_MariaDBNative.txt`

```text
MMOnsterpatch v0.15.1 - MariaDBNative Official Cutover

Purpose:
- Make MariaDB the official runtime database backend.
- Remove the sqlite compatibility bridge file and stop monkey-patching sqlite3.
- Stop requiring runtime auth.db/accounts.json/launcher_sessions.json/social.db/bank.db files.
- Preserve launcher/client protocol behavior: launcher and game client still talk to Account Website/Official Server, not SQL.

Major server changes:
- Added Server/OfficialServer/server_modules/mmonsterpatch_db.py as the native MariaDB database layer.
- Added Server/OfficialServer/server_modules/mmonsterpatch_mariadb_schema.py with non-destructive MariaDB schema bootstrap DDL.
- OfficialServer Python modules now import mmonsterpatch_db instead of mmonsterpatch_mariadb_compat/sqlite3.
- OfficialServer uses logical MariaDB stores: auth, tradingpost, social, files.
- worldserver.ini no longer points at data/social.db; it documents MariaDB social storage.
- runtime_status.json still writes to OfficialServer/data/ because it is a live status file, not a database.
- SQLite .db and JSON account mirror files are removed from the source/runtime package.

Account Website:
- Remains MySqlConnector/MariaDB backed from v0.15.0.
- Reads Server/Config/database.ini.

Server Manager:
- Version banner updated to v0.15.1 MariaDBNative.
- SQLite .db backup message changed to MariaDB backup note behavior.

Build/deploy:
- Build on workstation with BUILD_MARIADB_NATIVE_OFFICIAL_AND_COPY_SERVER.bat or CLEAN_AND_BUILD_ALL.bat.
- Copy Releases/Server to the server machine.
- On the server machine, set Server/Config/database.ini Password for the dedicated mmonsterpatch MariaDB user.
- MariaDB port remains 3307.
- Install PyMySQL on the server machine: py -m pip install PyMySQL.

Important:
- Existing imported MariaDB data is preserved. Schema bootstrap uses CREATE IF NOT EXISTS and indexes only.
- Keep your old pre-MariaDB server folder as backup until live login/save/social/trading tests pass.

```


---

## `WHAT_CHANGED_v0.15.2_MariaDBNativeStartupFix.txt`

```text
MMOnsterpatch v0.15.2 - MariaDBNativeStartupFix

Purpose:
- Fix first MariaDBNative startup crash caused by remaining SQLite-era social DB path initialization.

Fixes:
- Social init now treats social as a logical MariaDB database name instead of a .db path.
- Chat logs, user reports, and archived character JSON files are placed under OfficialServer/data.
- Moderation init no longer tries to create dirname("social").
- MariaDB DB layer treats duplicate CREATE INDEX / ALTER ADD COLUMN startup checks as no-ops.

Still required on the server machine:
- MariaDB running on port 3307.
- PyMySQL installed.
- Server/Config/database.ini populated with the mmonsterpatch MariaDB user/password.

```


---

## `WHAT_CHANGED_v0.15.4_MariaDBNativeSavePerf.txt`

```text
MMOnsterpatch v0.15.4 - MariaDBNative Save Performance

Baseline:
- Built from v0.15.3 MariaDBNative startup hotfix state after server successfully ran on MariaDB and loaded online saves.

Purpose:
- Reduce online-mode freezes caused by blocking TCP/MariaDB save uploads and duplicate world-rate fetches on Unity main thread.

Client patcher changes:
- Official online SaveSystem.SaveGame now queues normal server save uploads on a background ThreadPool worker.
- Local disk writes remain blocked while Official Online Mode is active.
- SaveData is still serialized immediately before preview cleanup, preserving the existing raw-save upload behavior.
- Only the latest queued save payload is retained if multiple saves happen quickly, reducing redundant write spam.
- Return-to-title, chat disconnect, and application quit paths wait briefly for the latest queued save before disconnecting.
- Added config defaults in BepInEx/config/goose.monsterpatch.officialserver.cfg:
  - AsyncOfficialSaveUploads = true
  - FinalSaveFlushTimeoutMs = 2500
  - WorldRatesRefreshMinIntervalSeconds = 30
- Duplicate synchronous Official Server world-rate refreshes are skipped for the configured interval, reducing the save-slot selection hitch after title login already refreshed rates.

Server changes:
- Carries forward v0.15.3 MariaDBNative startup/server-module fixes.
- No server protocol rewrite required for this performance pass.

Expected behavior:
- Title-screen Log In can still pause briefly because it must fetch online save-slot data from the server.
- Selecting a save should no longer do a duplicate world-rates socket request if title login just fetched rates.
- Normal in-game autosaves should no longer freeze while waiting for MariaDB/network ACK.
- Exit/disconnect may still wait briefly up to FinalSaveFlushTimeoutMs to protect the last save.

```


---

## `WHAT_CHANGED_YOLO_v0.12.11.1.txt`

```text
MMOnsterpatch YOLO v0.12.11.1 - No-Steam Account Session Build Fix

This is not a gameplay/auth redesign. It repairs the client build reference flow from v0.12.11.

Fixes:
- Always prepares Client\lib before building the client patcher, matching the old working flow.
- Clears stale partial DLL references from Client\lib first.
- Verifies Assembly-CSharp, BepInEx, Harmony, Cecil, Unity modules, and TextMeshPro exist before dotnet build.
- Removes command-line MONSTERPATCH_LIB_DIR passing from the PowerShell build to avoid bracket/trailing-backslash reference resolution problems.
- Updates the client csproj to resolve references directly from $(MSBuildProjectDirectory)\..\lib by default.

If Monsterpatch is not in the default Steam path, run:
  set "MONSTERPATCH_GAME_DIR=C:\Path\To\Monsterpatch"
  CLEAN_AND_BUILD_ALL.bat

```


---

## `WHAT_CHANGED_YOLO_v0.12.11.2.txt`

```text
v0.12.11.2 fixes the build script invocation of Client\prepare_lib_from_game.bat. The previous script called cmd.exe /c with a bracketed path and Windows split the path at [MMOnsterpatch]. This version invokes the .bat through PowerShell call operator using the path variable directly. No auth redesign changes were made from v0.12.11.1.

```


---

## `WHAT_CHANGED_YOLO_v0.12.11.4.txt`

```text
MMOnsterpatch YOLO v0.12.11.4 - Launcher Account Gate Test

Purpose
- Make the custom title-screen Online Mode Log In button look for the launcher account handoff first.
- Stop requiring the old Steam/OpenID or Trading Post socket SESSION_LOGIN success flag before opening the online save slot selector.
- Keep the WoW-like target flow for this test:
  1. Create account on Account Website.
  2. Log into MMOnsterpatch Launcher.
  3. Launch Monsterpatch with Online selected.
  4. Click Log In on the custom title screen.
  5. Open the online save slot selector.

Changed source files
- Client/MMOnsterpatchOfficialServerPatcher/GTSRuntimeHost.cs
  - Added TryUseLauncherAccountSessionForAio().
  - Added launcher-account-backed session token selection.
  - For this test build, prefers stable AccountId (acct_...) over expiring aio_ session token for online save/account requests.
  - EnsureAioAuthenticatedCoroutine now accepts launcher account handoff locally before trying old socket session login.

- Client/MMOnsterpatchOfficialServerPatcher/OfficialServerSaveSelectNative.cs
  - Title-screen Log In flow now calls TryUseLauncherAccountSessionForAio() before old auth routine.
  - Native save-select Online switch and logout/reopen flow use the same launcher handoff check.
  - If launcher handoff exists, it skips old Steam/socket pre-auth and loads online slots.

- Server/OfficialServer/server_modules/mmonsterpatch_tradingpost_core.py
  - resolve_aio_session now accepts acct_ website account IDs as a temporary account identity token.
  - Launcher-created sessions no longer fail this test flow due to clock/expiry/IP mismatches.
  - This is intentional temporary auth loosening for flow testing.

- Server/OfficialServer/server_modules/mmonsterpatch_social_core.py
  - Updated rejected-session wording away from Steam-specific messages.

Not changed
- Root CLEAN_AND_BUILD_ALL.bat and Clean_And_Build_All.ps1 build flow were not changed.
- Launcher staging/restore flow was not changed.
- Account Website registration/login flow was not changed.
- Steam auth was not re-added.

Security note
- This build intentionally loosens auth/session validation to prove the account -> launcher -> game -> online save selector flow.
- After this flow works, Steam verification and stronger ticket/session validation should be added back deliberately.

```


---

## `WHAT_CHANGED_YOLO_v0.12.11.txt`

```text
What changed in v0.12.11

Recovery target:
- Stop changing broad architecture.
- Restore v0.12.9 launcher/server manager layout baseline.
- Make one direct login-path change: Steam/OpenID is removed from the test login path.

Client:
- Added full client source under Client\MMOnsterpatchOfficialServerPatcher.
- Client DLL version metadata updated to 0.12.11.
- GTSRuntimeHost no longer calls LoginWithSteamCoroutine from EnsureAioAuthenticatedCoroutine.
- LoginWithSteamCoroutine is retained only as a disabled stub so accidental old calls do not open Steam auth.
- In-game login now requires the launcher-written cached AIO/session token and sends SESSION_LOGIN.
- UI/status text no longer tells users to log in with Steam.

Launcher:
- Keeps Online Mode staging/restoration.
- Requests game-ticket/account session from Account Website.
- Writes goose.monsterpatch.gts.client.cfg into the staged game's real BepInEx\config folder after staging.
- Does not attempt Steam auth.

Server:
- Current Official Server keeps SESSION_LOGIN as the login endpoint.
- Steam/OpenID socket commands return disabled errors for this build.
- Same-IP session requirement default changed to 0 for account website/cloudflare + raw game socket testing.
- runtime_status.json writer now retries/falls back to reduce WinError 5 access denied spam.

Build:
- Client patcher build is required.
- No fallback to old MMOnsterpatchOfficialServerPatcher.dll.
- Releases\Client Files mirrors the exact payload the launcher stages, including followers and bottle DLLs.

```


---

## `WHAT_CHANGED_YOLO_v0.12.7.txt`

```text
MMOnsterpatch YOLO v0.12.8 - Account/Status Foundation

What changed:

1. Repack Manager Console / Events panel
- Removed the bottom micro-console behavior from the main workflow area in v0.12.6.
- Fixed the right-side Console / Events log box so the output control fills the right panel instead of staying as a tiny one-line box.
- Console / Events remains scrollable and uses readable green console text.

2. Repack Manager Online Users panel
- The left Online Users panel now reads OfficialServer\data\runtime_status.json.
- It displays the current online player count and a basic user list when the server exposes live players.
- Until players connect, it shows "No players online" instead of placeholder-only text.

3. Current Official Server runtime status
- Current Official Server now writes OfficialServer\data\runtime_status.json every ~2 seconds.
- This includes:
  - online true/false source data
  - playersOnline
  - basic player names/location/status from current MMO sessions
  - social client count
  - current port map: 61526/61527/61528/61529
  - current public DNS/URL map
- This is intentionally file-based for the first account/status foundation pass so the current Python server can feed the Account Website and Repack Manager without needing a new local service.

4. Account Website status API
- /api/launcher/status now reads the live runtime status file.
- If the Current Official Server is not running or the status file is stale, the endpoint reports online=false and playersOnline=0.
- Added /api/server/online-users for manager/future tooling.
- /health still reports the account website itself.

5. Launcher server status card
- The launcher now polls https://account.gamingwithgoose.com/api/launcher/status.
- If the endpoint reports online=true:
  - server dot is green
  - text says ONLINE
  - Players Online shows the live playersOnline count
- If endpoint is offline/unreachable/stale:
  - server dot is red
  - text says OFFLINE
  - Players Online shows 0

6. Online Play gate remains in place
- Offline Play still does not require launcher login.
- Online Play still requires placeholder launcher sign-in before it continues.
- This preserves the future direction: Online Play will later require real MMOnsterpatch account credentials/session.

7. Current ports remain locked
- 61526 = Trading Post socket
- 61527 = Steam OpenID / mon-auth HTTP
- 61528 = MMO multiplayer
- 61529 = Social/chat/guild
- Future Auth placeholder remains disabled/not part of the live path.

8. Start servers minimized
- Start servers minimized remains enabled by default in config.

Not changed yet:
- Real account registration/login is not finished yet.
- Steam auth is not yet moved to account creation only.
- Existing online saves/character slots are not migrated to account-credential ownership yet.
- Trading Post marketplace merge is not implemented yet.

```


---

## `WHAT_CHANGED_YOLO_v0.12.8.txt`

```text
MMOnsterpatch YOLO v0.12.8 - Account Credential Foundation

Added real first-pass account registration and login to Account Website.
Added website account pages: /register, /login, /account, /character/{id}.
Added launcher login against https://account.gamingwithgoose.com/api/account/login.
Online Play now requires a real account login and writes an account-backed AIO session config for the current official patcher.
Added JSON account/session store under Data/Accounts for this fresh setup stage.
Modified current Python server session resolution to accept Account Website launcher sessions and synthesize compatible current-server account rows.
Preserved Name#1234 public_handle character identity.
Added account character export so website account page can show characters/rank profile after they are created in-game.

Still future/not final:
- Steam verification/linking during registration.
- Email verification/password reset.
- Full website Trading Post/mailbox actions.

```


---

## `WHAT_CHANGED_YOLO_v0.12.9.txt`

```text
MMOnsterpatch YOLO v0.12.9 - Launcher Ticket + DB Foundation

Changed/added:
- Launcher Discord/GitHub top buttons now use the user-provided icons.
- Launcher now asks Account Website for a game-ticket/session before Online Play.
- Launcher writes exact cached-session config keys expected by the current official patcher:
  CachedOfficialSessionTokenBase64, CachedOfficialSessionExpiresUtc,
  CachedOfficialSteamID64, CachedOfficialAccountUUID.
- Also keeps older alias keys for diagnostics/backward compatibility.
- Account Website account storage now uses Data\Accounts\auth.db as the source of truth.
- Account Website mirrors accounts.json and launcher_sessions.json for compatibility with the current Python server/patcher bridge.
- Current Python server can read launcher sessions from auth.db directly, with JSON fallback.
- In-game/session-login failure text now tells users to log into the MMOnsterpatch Launcher instead of Steam.
- Added /api/account/game-ticket endpoint. First pass returns the current AIO-compatible session token as the game ticket.
- Did not ship live account/session files from the uploaded JSON because those contain active credentials/session tokens.

Still expected:
- If the current binary patcher still hangs on the in-game login button after these exact cached keys are written, the next step is a patcher-source rebuild to bypass the Steam coroutine and call SESSION_LOGIN directly from launcher_session/config.

```


---

## `WHAT_CHANGED_YOLO_v0.13.1_UIChange_ServerValidatedLoginFix.txt`

```text
MMOnsterpatch YOLO v0.13.1 - UI Change Server-Validated Login Fix

Read-only/login-state correction based on user feedback after v0.13.1 UI Change BuildFix.

Changes:
- Launcher no longer trusts cached local signed-in state by itself.
- On startup, a remembered launcher account/session must validate against Account Website /api/account/me before the profile/user icon shows the signed-in menu.
- If validation fails, expires, is revoked, or the Account Website cannot be reached, the launcher clears the signed-in state and shows the user as offline/logged out.
- Clicking Online Play also verifies the launcher account session with the server before staging/launching Online Mode.
- Clicking the profile icon while signed in revalidates before opening the profile menu.
- Manage Account website page now shows username as read-only/permanent.
- Manage Account backend ignores submitted username changes, even if the HTML is manually edited.

Intentionally unchanged:
- Root CLEAN_AND_BUILD_ALL.bat / Clean_And_Build_All.ps1 build flow.
- v0.13.0 Stable game login handoff behavior.
- Client patcher title Log In -> online save slot flow.
- Ports/subdomains/current server routing.
- Account/character ownership model and Name#1234 character handle direction.

```


---

## `WHAT_CHANGED_YOLO_v0.13.2_SocialsFix.txt`

```text
MMOnsterpatch YOLO v0.13.2 - SocialsFix

Changed:
- SocialAIOPatcher ConnectAllServicesCoroutine now accepts the launcher account handoff as valid social/chat authentication.
- The old check required GTSRuntimeHost.IsLoggedInForAio(), which also required the old Trading Post socket client to be connected.
- In the v0.13.0 stable launcher-account flow, the title/save-selector path intentionally does not require that old socket pre-auth. Social/chat now matches that behavior.
- When the launcher account handoff is accepted locally, social now proceeds to connect MMO/social services and sends ACCOUNT_SLOT_HELLO with the launcher account token/account id.

Updated metadata:
- Project version metadata moved to v0.13.2_SocialsFix.
- Patch manifest version moved to 0.13.2.

Unchanged:
- Build BAT/PowerShell logic.
- DNS/subdomains and current ports.
- Website/launcher login flow.
- Online title Log In -> online save selector behavior.

```


---

## `WHAT_CHANGED_YOLO_v0.13.3_TradingPostLayoutFix.txt`

```text
MMOnsterpatch YOLO v0.13.3 - TradingPostLayoutFix

Changed:
- Launcher now seeds goose.monsterpatch.gts.client.cfg with the legacy/full Trading Post layout defaults when missing.
- Launcher now upserts only account/session/auth keys instead of overwriting the full config file.
- Launcher repairs known bad launcher/fallback layout values, including Create Listing dropdown/group alignment.
- Launcher preserves saved Trading Post window X/Y/Width/Height while keeping RememberWindowPosition/RememberWindowSize enabled.
- Client fallback/static defaults now match the old tuned Trading Post layout more closely.
- Version metadata moved to v0.13.3_TradingPostLayoutFix.

Not changed:
- Build BAT/PowerShell flow.
- Account launcher handoff flow.
- v0.13.2 SocialsFix MMO/social/chat auth behavior.
- Server ports/subdomains.

```


---

## `WHAT_CHANGED_YOLO_v0.13.4_FollowerAIOMergeTest.txt`

```text
MMOnsterpatch v0.13.4 - FollowerAIOMergeTest

Purpose:
- Merge the previously separate follower runtime source into the MMOnsterpatch AIO/client patcher.
- Stop staging separate Monsterpatch_Followers_Patcher.dll and Monsterpatch_Followers_Runtime.dll.
- Keep the same follower sync packet shape so the server protocol does not need to change.

Expected payload:
- BepInEx\patchers\MMOnsterpatchOfficialServerPatcher.dll owns online + follower behavior.
- BepInEx\config\goose.monsterpatch.followersruntime.cfg is staged as the follower defaults/config.
- No standalone follower DLLs are staged.

Server impact:
- No server packet/protocol rewrite should be required for this test.
- AIO still sends the same follower fields in the STEP/account movement packets.

```


---

## `WHAT_CHANGED_YOLO_v0.14.1_WorldSpawnLayeringFix.txt`

```text
MMOnsterpatch YOLO v0.14.2 - WorldSpawnLifecycleFix

Baseline: v0.14.0 stable (v0.13.4 FollowerAIOMergeTest confirmed working).

Changes:
- Server-owned overworld spawn visuals now use follower/remote-player style world layering.
- Removed the forced SpriteRenderer sortingOrder=1 behavior that made server spawns draw on top of map/object edges.
- Server spawn roots are normalized to z = y * 0.01f so they layer through the world like follower mons.
- Placeholder world spawns now use sortingOrder=0 instead of priority order.
- Follower item-find test chance reduced from 100% to 10% after the 250-step threshold.

No protocol/server rewrite was required. The server still owns spawn identity/position; this fix changes the AIO client-side visual presentation of those server-owned spawns.

Build flow unchanged: run CLEAN_AND_BUILD_ALL.bat.

```


---

## `WHAT_CHANGED_YOLO_v0.14.2_WorldSpawnLifecycleFix.txt`

```text
MMOnsterpatch YOLO v0.14.2 - WorldSpawnLifecycleFix

Focus:
- Server-owned overworld spawn lifecycle and population control.
- Prevent stacked same-tile overworld spawns after repeated map transitions.
- Support water spawn zones from the latest Monsterpatch update.

Server changes:
- Replaced passive one-roll/wait behavior with a per-map target population controller.
- Target visible spawn count scales from configured base density and active player count.
- Default base density remains 40%, +8% per extra player, capped at 70%.
- Minimum one visible spawn for active maps with spawn zones.
- Top-up interval lowered to 20 seconds.
- Catch/removal schedules a refill after 12 seconds.
- Last-player-leaves-map clears that map's server-owned spawns and pending generation immediately.
- Empty-map stale cleanup default lowered to 8 seconds for disconnect/timeout cleanup.
- Pending generation requests count as occupied tiles.
- Generation result rechecks tile occupancy before adding a spawn.
- Zone type grass/water is stored in pending requests/spawn records.

Client changes:
- Reports both spawnZone and spawnZoneWater.
- Sends zone type in WORLD_SPAWNZONES records.
- Accepts zone type in WORLD_SPAWN_GEN_REQ.
- Generates water-only encounter candidates for water zones instead of using player water-walking state.
- Server-owned spawn visuals dedupe same scene/tile before creating a new visual.

Build flow:
- Root BAT/PowerShell flow unchanged.

```


---

## `WHAT_CHANGED_YOLO_v0.14.3_FollowerAnimationTierFix.txt`

```text
MMOnsterpatch YOLO v0.14.3 - FollowerAnimationTierFix

Built from v0.14.2 WorldSpawnLifecycleFix.

Changes:
- Corrected merged AIO follower animation tier detection.
- Uses verified native Monsterpatch PlayerController static fields: isMoving, isRunning, ridingBroom.
- Removed moveMod from follower animation tier selection. moveMod affects movement/step behavior and caused walk/run animation to appear swapped.
- Keeps config multipliers: idle 0.75, walk 1, run 1.5, broom 2.
- Consolidated README/notes into Docs/README_CONSOLIDATED_v0.14.3.md.
- Added HANDOFF_v0.14.3_FollowerAnimationTierFix.json and Docs/ARTIFACTS_AND_BASELINES_v0.14.3.md.

No server lifecycle/protocol changes were made after v0.14.2.

```


---

## `TESTING_STEPS_YOLO_v0.12.11.4.txt`

```text
MMOnsterpatch YOLO v0.12.11.4 Testing Steps

1. Build using the existing root BAT:
   CLEAN_AND_BUILD_ALL.bat

2. Start the server stack from the rebuilt Releases/Server folder.

3. Confirm Account Website loads:
   https://account.gamingwithgoose.com

4. Create or use a test website account.

5. Open the rebuilt launcher from Releases/Launcher.

6. Log into the launcher with the website account.

7. Select Online mode.

8. Click Play.

9. On the custom Monsterpatch title screen, click Log In.

Expected result
- Button may briefly show Logging In.
- It should NOT bounce back just because SESSION_LOGIN/socket auth did not validate.
- It should load the online save slot selector.

Good log lines to look for in BepInEx/LogOutput.log
- Launcher account handoff accepted locally; title Log In will load online save slots without Steam/socket pre-auth.
- Launcher account handoff found. Skipping old Steam/socket pre-auth and loading online save slots.
- Loaded Official Server online save slots via v2.

If it still returns to Log In
- Look for:
  - Cannot load Official Server save slots: launcher session token is empty.
  - FetchOnlineSaveSlotsFromServerV2 failed:
  - Official Server v2 save slot request failed:
- That would mean the title auth gate is fixed, but the save-slot service/port/protocol is the next failure.

```


---

## `TESTING_STEPS_YOLO_v0.12.11.txt`

```text
Testing steps for v0.12.11

1. Extract into a fresh folder.
2. Run CLEAN_AND_BUILD_ALL.bat.
   - If it cannot find Monsterpatch_Data\Managed, run from the same Command Prompt:
     set "MONSTERPATCH_GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\Monsterpatch"
     CLEAN_AND_BUILD_ALL.bat
3. Confirm build completes. The client patcher build is required and must not silently fall back.
4. Confirm fresh DLL exists:
   Releases\Client Files\BepInEx\patchers\MMOnsterpatchOfficialServerPatcher.dll
5. Copy the entire Releases\Server folder to the server desktop/test folder.
6. Start Repack Manager from Releases\Server\MMOnsterpatchRepackManager.exe.
7. Start Account Website and Current Official Server.
8. Use Releases\Launcher\MMOnsterpatchLauncher.exe.
9. Log into the launcher with MMOnsterpatch account credentials.
10. Select Online Mode and click Play.
11. In game, click Log In.

Expected result:
- No Steam browser prompt.
- In-game Log In should use the launcher-written cached session.
- Character select should load if the account/session is valid.

If it fails:
- Send Monsterpatch\BepInEx\LogOutput.log.
- Send the Repack Manager Current Official Server lines around SESSION_LOGIN.
- Confirm Monsterpatch\BepInEx\config\goose.monsterpatch.gts.client.cfg exists after Online Mode launch and contains non-empty CachedSessionToken.

```


---

## `TESTING_STEPS_YOLO_v0.12.7.txt`

```text
MMOnsterpatch YOLO v0.12.8 Testing Steps

1. Build
- Extract the workspace zip.
- Run CLEAN_AND_BUILD_ALL.bat.
- Copy the entire Releases\Server folder to the server desktop.

2. Server Manager
- Run MMOnsterpatchRepackManager.exe from inside Releases\Server.
- Confirm Start servers minimized is checked by default.
- Start Current Official Server.
- Start Account Website.
- Confirm the right-side Console / Events box is tall and scrollable.
- Confirm the left Online Users panel says 0 online / No players online until a client connects.

3. Runtime status file
- Confirm this file is created after Current Official Server starts:
  OfficialServer\data\runtime_status.json
- It should update every couple seconds.
- It should show playersOnline: 0 until a player connects.

4. Public status endpoints
Test in a browser:
- https://account.gamingwithgoose.com/health
- https://account.gamingwithgoose.com/api/launcher/status
- https://account.gamingwithgoose.com/api/server/online-users
- https://account.gamingwithgoose.com/register
- https://account.gamingwithgoose.com/forgot-password
- https://account.gamingwithgoose.com/patch/manifest.json
- https://mon-auth.gamingwithgoose.com/

Expected:
- /health shows Account Website online.
- /api/launcher/status shows online=true only when Current Official Server is running and runtime_status.json is fresh.
- /api/launcher/status shows playersOnline matching the runtime_status file.

5. Launcher
- Launch the v0.12.8 launcher after building.
- Confirm the server status card turns green ONLINE when the server is running.
- Confirm it turns red OFFLINE if Account Website/status endpoint is unreachable or Current Official Server is stopped long enough for the status file to become stale.
- Confirm Players Online reflects playersOnline from /api/launcher/status.
- Confirm Offline Play still does not require login.
- Confirm Online Play still asks for launcher login first.

6. Close cleanup
- Close Server Manager.
- Confirm python.exe exits and no old process keeps the Server folder locked.

```


---

## `TESTING_STEPS_YOLO_v0.12.8.txt`

```text
Testing v0.12.8

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Copy Releases\Server to the server desktop.
3. Start Current Official Server and Account Website.
4. Open https://account.gamingwithgoose.com/register and create a test account.
5. Open the launcher and log in with that account.
6. Online Play should no longer accept fake/placeholder sign-in.
7. Use a COPY of Monsterpatch first. Select Online and Play.
8. The launcher writes %LocalAppData%\MMOnsterpatch\Config\goose.monsterpatch.gts.client.cfg with the account session token.
9. Create/load an online character.
10. Visit https://account.gamingwithgoose.com/account and verify the character appears after the server status exporter ticks.

Expected character identity:
- Display name may duplicate.
- Public handle remains Name#1234.
- Character is attached to the MMOnsterpatch account's pseudo account identity for now.

```


---

## `TESTING_STEPS_YOLO_v0.12.9.txt`

```text
Testing v0.12.9

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Copy Releases\Server to the server desktop.
3. Start Current Official Server and Account Website.
4. Register/login at https://account.gamingwithgoose.com/register or /login.
5. Confirm Data\Accounts\auth.db exists after account creation/login.
6. Confirm Data\Accounts\accounts.json and launcher_sessions.json are also mirrored.
7. Build/run the launcher.
8. Log into the launcher with the website account.
9. Online Play on a COPY of Monsterpatch.
10. Check staged BepInEx\config\goose.monsterpatch.gts.client.cfg contains CachedOfficialSessionTokenBase64.
11. In game, try online login/save-slot flow.

If the game still hangs on Logging In:
- Send the staged goose.monsterpatch.gts.client.cfg and BepInEx logs.
- This means the existing patcher binary is not consuming the cached-session config and needs a source rebuild to route the button to SESSION_LOGIN instead of Steam OpenID.

```


---

## `TESTING_STEPS_YOLO_v0.13.2_SocialsFix.txt`

```text
MMOnsterpatch YOLO v0.13.2 - SocialsFix Testing

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Start the server manager and current Official Server services.
3. Create/log into an account on the website.
4. Log into the launcher and choose Online Play.
5. On the custom title screen, click Log In.
6. Load/create an online save.
7. After the world loads, the chat/social panel should no longer say:
   Launcher account authentication did not complete. MMO/social services were not connected.
8. Expected new client-side message if the launcher handoff is being used:
   Launcher account handoff accepted. Connecting MMO/social services with this launcher account...
9. Expected server-side social log line:
   [account-slot] ... account=<account uuid> slot=<slot> display=<name> handle=<Name#1234> id=<char_...>

```


---

## `TESTING_STEPS_YOLO_v0.13.3_TradingPostLayoutFix.txt`

```text
MMOnsterpatch YOLO v0.13.3 - TradingPostLayoutFix Testing

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Launch the new launcher build.
3. Log into the launcher and start Online Play.
4. Open Trading Post and Create Listing.
5. Confirm Offered/Requested buttons, fields, arrows, dropdowns, and List Post alignment match the old organized layout.
6. Move/resize the Trading Post window, close the game, relaunch online, and confirm the window size/position is remembered.
7. Confirm online save selector and MMO/social/chat still work from v0.13.2 stable.

```


---

## `TESTING_STEPS_YOLO_v0.14.3_FollowerAnimationTierFix.txt`

```text
Testing steps for v0.14.3 FollowerAnimationTierFix

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Launch the game via MMOnsterpatch Launcher Online Play.
3. Verify follower animation tiers:
   - idle should be slower than movement,
   - walking should be normal,
   - running should animate faster than walking,
   - broom should be fastest.
4. Verify v0.14.2 world spawns still behave:
   - no same-tile spawn stacking after map transitions,
   - spawns refill after catch/shoo,
   - water spawns can appear in water spawn zones.
5. Verify online save selector/social/chat/trading post still work.

```


---

## `TESTING_STEPS_YOLO_v0.12.11.4.txt`

```text
MMOnsterpatch YOLO v0.12.11.4 Testing Steps

1. Build using the existing root BAT:
   CLEAN_AND_BUILD_ALL.bat

2. Start the server stack from the rebuilt Releases/Server folder.

3. Confirm Account Website loads:
   https://account.gamingwithgoose.com

4. Create or use a test website account.

5. Open the rebuilt launcher from Releases/Launcher.

6. Log into the launcher with the website account.

7. Select Online mode.

8. Click Play.

9. On the custom Monsterpatch title screen, click Log In.

Expected result
- Button may briefly show Logging In.
- It should NOT bounce back just because SESSION_LOGIN/socket auth did not validate.
- It should load the online save slot selector.

Good log lines to look for in BepInEx/LogOutput.log
- Launcher account handoff accepted locally; title Log In will load online save slots without Steam/socket pre-auth.
- Launcher account handoff found. Skipping old Steam/socket pre-auth and loading online save slots.
- Loaded Official Server online save slots via v2.

If it still returns to Log In
- Look for:
  - Cannot load Official Server save slots: launcher session token is empty.
  - FetchOnlineSaveSlotsFromServerV2 failed:
  - Official Server v2 save slot request failed:
- That would mean the title auth gate is fixed, but the save-slot service/port/protocol is the next failure.

```


---

## `TESTING_STEPS_YOLO_v0.12.11.txt`

```text
Testing steps for v0.12.11

1. Extract into a fresh folder.
2. Run CLEAN_AND_BUILD_ALL.bat.
   - If it cannot find Monsterpatch_Data\Managed, run from the same Command Prompt:
     set "MONSTERPATCH_GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\Monsterpatch"
     CLEAN_AND_BUILD_ALL.bat
3. Confirm build completes. The client patcher build is required and must not silently fall back.
4. Confirm fresh DLL exists:
   Releases\Client Files\BepInEx\patchers\MMOnsterpatchOfficialServerPatcher.dll
5. Copy the entire Releases\Server folder to the server desktop/test folder.
6. Start Repack Manager from Releases\Server\MMOnsterpatchRepackManager.exe.
7. Start Account Website and Current Official Server.
8. Use Releases\Launcher\MMOnsterpatchLauncher.exe.
9. Log into the launcher with MMOnsterpatch account credentials.
10. Select Online Mode and click Play.
11. In game, click Log In.

Expected result:
- No Steam browser prompt.
- In-game Log In should use the launcher-written cached session.
- Character select should load if the account/session is valid.

If it fails:
- Send Monsterpatch\BepInEx\LogOutput.log.
- Send the Repack Manager Current Official Server lines around SESSION_LOGIN.
- Confirm Monsterpatch\BepInEx\config\goose.monsterpatch.gts.client.cfg exists after Online Mode launch and contains non-empty CachedSessionToken.

```


---

## `TESTING_STEPS_YOLO_v0.12.7.txt`

```text
MMOnsterpatch YOLO v0.12.8 Testing Steps

1. Build
- Extract the workspace zip.
- Run CLEAN_AND_BUILD_ALL.bat.
- Copy the entire Releases\Server folder to the server desktop.

2. Server Manager
- Run MMOnsterpatchRepackManager.exe from inside Releases\Server.
- Confirm Start servers minimized is checked by default.
- Start Current Official Server.
- Start Account Website.
- Confirm the right-side Console / Events box is tall and scrollable.
- Confirm the left Online Users panel says 0 online / No players online until a client connects.

3. Runtime status file
- Confirm this file is created after Current Official Server starts:
  OfficialServer\data\runtime_status.json
- It should update every couple seconds.
- It should show playersOnline: 0 until a player connects.

4. Public status endpoints
Test in a browser:
- https://account.gamingwithgoose.com/health
- https://account.gamingwithgoose.com/api/launcher/status
- https://account.gamingwithgoose.com/api/server/online-users
- https://account.gamingwithgoose.com/register
- https://account.gamingwithgoose.com/forgot-password
- https://account.gamingwithgoose.com/patch/manifest.json
- https://mon-auth.gamingwithgoose.com/

Expected:
- /health shows Account Website online.
- /api/launcher/status shows online=true only when Current Official Server is running and runtime_status.json is fresh.
- /api/launcher/status shows playersOnline matching the runtime_status file.

5. Launcher
- Launch the v0.12.8 launcher after building.
- Confirm the server status card turns green ONLINE when the server is running.
- Confirm it turns red OFFLINE if Account Website/status endpoint is unreachable or Current Official Server is stopped long enough for the status file to become stale.
- Confirm Players Online reflects playersOnline from /api/launcher/status.
- Confirm Offline Play still does not require login.
- Confirm Online Play still asks for launcher login first.

6. Close cleanup
- Close Server Manager.
- Confirm python.exe exits and no old process keeps the Server folder locked.

```


---

## `TESTING_STEPS_YOLO_v0.12.8.txt`

```text
Testing v0.12.8

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Copy Releases\Server to the server desktop.
3. Start Current Official Server and Account Website.
4. Open https://account.gamingwithgoose.com/register and create a test account.
5. Open the launcher and log in with that account.
6. Online Play should no longer accept fake/placeholder sign-in.
7. Use a COPY of Monsterpatch first. Select Online and Play.
8. The launcher writes %LocalAppData%\MMOnsterpatch\Config\goose.monsterpatch.gts.client.cfg with the account session token.
9. Create/load an online character.
10. Visit https://account.gamingwithgoose.com/account and verify the character appears after the server status exporter ticks.

Expected character identity:
- Display name may duplicate.
- Public handle remains Name#1234.
- Character is attached to the MMOnsterpatch account's pseudo account identity for now.

```


---

## `TESTING_STEPS_YOLO_v0.12.9.txt`

```text
Testing v0.12.9

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Copy Releases\Server to the server desktop.
3. Start Current Official Server and Account Website.
4. Register/login at https://account.gamingwithgoose.com/register or /login.
5. Confirm Data\Accounts\auth.db exists after account creation/login.
6. Confirm Data\Accounts\accounts.json and launcher_sessions.json are also mirrored.
7. Build/run the launcher.
8. Log into the launcher with the website account.
9. Online Play on a COPY of Monsterpatch.
10. Check staged BepInEx\config\goose.monsterpatch.gts.client.cfg contains CachedOfficialSessionTokenBase64.
11. In game, try online login/save-slot flow.

If the game still hangs on Logging In:
- Send the staged goose.monsterpatch.gts.client.cfg and BepInEx logs.
- This means the existing patcher binary is not consuming the cached-session config and needs a source rebuild to route the button to SESSION_LOGIN instead of Steam OpenID.

```


---

## `TESTING_STEPS_YOLO_v0.13.2_SocialsFix.txt`

```text
MMOnsterpatch YOLO v0.13.2 - SocialsFix Testing

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Start the server manager and current Official Server services.
3. Create/log into an account on the website.
4. Log into the launcher and choose Online Play.
5. On the custom title screen, click Log In.
6. Load/create an online save.
7. After the world loads, the chat/social panel should no longer say:
   Launcher account authentication did not complete. MMO/social services were not connected.
8. Expected new client-side message if the launcher handoff is being used:
   Launcher account handoff accepted. Connecting MMO/social services with this launcher account...
9. Expected server-side social log line:
   [account-slot] ... account=<account uuid> slot=<slot> display=<name> handle=<Name#1234> id=<char_...>

```


---

## `TESTING_STEPS_YOLO_v0.13.3_TradingPostLayoutFix.txt`

```text
MMOnsterpatch YOLO v0.13.3 - TradingPostLayoutFix Testing

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Launch the new launcher build.
3. Log into the launcher and start Online Play.
4. Open Trading Post and Create Listing.
5. Confirm Offered/Requested buttons, fields, arrows, dropdowns, and List Post alignment match the old organized layout.
6. Move/resize the Trading Post window, close the game, relaunch online, and confirm the window size/position is remembered.
7. Confirm online save selector and MMO/social/chat still work from v0.13.2 stable.

```


---

## `TESTING_STEPS_YOLO_v0.14.3_FollowerAnimationTierFix.txt`

```text
Testing steps for v0.14.3 FollowerAnimationTierFix

1. Run CLEAN_AND_BUILD_ALL.bat.
2. Launch the game via MMOnsterpatch Launcher Online Play.
3. Verify follower animation tiers:
   - idle should be slower than movement,
   - walking should be normal,
   - running should animate faster than walking,
   - broom should be fastest.
4. Verify v0.14.2 world spawns still behave:
   - no same-tile spawn stacking after map transitions,
   - spawns refill after catch/shoo,
   - water spawns can appear in water spawn zones.
5. Verify online save selector/social/chat/trading post still work.

```


---

## `IF_WORKED_NEXT_STEPS.txt`

```text
If v0.12.8 works:

1. Add Steam verification/linking to the register/account page.
2. Add password reset/email verification.
3. Replace JSON account store with MariaDB/SQLite install/update flow when stable.
4. Add website global/guild chat viewer/send endpoints.
5. Add website Trading Post listing/claiming backed by mailbox delivery.
6. Add website avatar/profile management.
7. Rebuild the patcher/plugin so it reads launcher account sessions directly instead of relying on cached Trading Post config compatibility.

```


# Archived MariaDB transition notes merged during v0.15.6


---

## README_MARIADB_OFFICIAL_v0.15.0.md

# MMOnsterpatch v0.15.0 MariaDBOfficial

This package is the source cutover after the MariaDB migration kit was successfully imported.

## Required setup

1. MariaDB must be running on `127.0.0.1:3307`.
2. The migration kit should already have imported:
   - `mmonsterpatch_auth`
   - `mmonsterpatch_tradingpost`
   - `mmonsterpatch_social`
   - `mmonsterpatch_files`
3. Install Python MariaDB client dependency on the server PC:

```bat
py -m pip install PyMySQL
```

4. Edit:

```text
Server\Config\database.ini
```

Set the real MariaDB password:

```ini
Password=YOUR_PASSWORD_HERE
```

## Build

Run from the workspace root:

```bat
CLEAN_AND_BUILD_ALL.bat
```

Then deploy:

```text
Releases\Server\
```

## Expected runtime layout

```text
Server\
  MMOnsterpatchRepackManager.exe
  AccountWebsite\
  Config\
  OfficialServer\
  PatchPayload\
  Logs\
  Backups\
```

## No longer required as live runtime DB storage

```text
Data\Accounts\auth.db
Data\Accounts\accounts.json
Data\Accounts\launcher_sessions.json
OfficialServer\data\social.db
OfficialServer\data\bank.db
OfficialServer\server_modules\data\bank.db
```

Keep your old folder as a backup until this version is tested.

## Notes

`OfficialServer\data\` still exists because the server writes non-database runtime files there, such as `runtime_status.json`, chat logs, reports, and archived character exports. That is not the old DB system.


---

## README_MARIADB_HOTFIX1.md

# MMOnsterpatch v0.15.0 MariaDBOfficial Hotfix 1

Fixes the first live MariaDB cutover authentication issue:

```text
OperationalError: (2059, "Authentication plugin 'auth_gssapi_client' not configured")
```

Cause:
- The server was trying to connect as MariaDB `root`.
- On this Windows MariaDB install, `root` can use GSSAPI/Windows authentication.
- PyMySQL should not use that account for the game server.

Fix:
- Official Server MariaDB bridge now reads `Server/Config/database.ini` directly.
- Default DB user is now `mmonsterpatch`, not `root`.
- Added `CREATE_MARIADB_USER_SQL3307.bat` to create/grant a dedicated MariaDB user and update `database.ini`.

## How to use

1. Extract/build this workspace.
2. Run `CREATE_MARIADB_USER_SQL3307.bat` from the workspace root, or from the built server root if copied there.
3. Enter a new password for the `mmonsterpatch` DB user.
4. Enter the MariaDB admin/root password when prompted by `mysql.exe`.
5. Start `Server/MMOnsterpatchRepackManager.exe`.

The DB config should end up as:

```ini
[Database]
Provider=MariaDB
Host=127.0.0.1
Port=3307
User=mmonsterpatch
Password=<your password>
AuthDatabase=mmonsterpatch_auth
TradingPostDatabase=mmonsterpatch_tradingpost
SocialDatabase=mmonsterpatch_social
FilesDatabase=mmonsterpatch_files
```


---

## README_MARIADB_NATIVE_v0.15.1.md

# MMOnsterpatch v0.15.1 MariaDBNative

This build makes MariaDB the official runtime database backend for the Account Website and Python Official Server.

## Machine layout

- Workstation: build only.
- Server: runs MariaDB and `MMOnsterpatchRepackManager.exe`.

## Build on workstation

Run:

```bat
BUILD_MARIADB_NATIVE_OFFICIAL_AND_COPY_SERVER.bat
```

or:

```bat
CLEAN_AND_BUILD_ALL.bat
```

Copy the generated `Releases\Server` folder to the server machine.

## Server requirements

On the server machine:

```bat
py -m pip install PyMySQL
```

MariaDB should be listening on port `3307`.

## Config

Edit the deployed server file:

```text
Server\Config\database.ini
```

Expected values:

```ini
[Database]
Provider=MariaDB
Host=127.0.0.1
Port=3307
User=mmonsterpatch
Password=YOUR_PASSWORD_HERE
AuthDatabase=mmonsterpatch_auth
TradingPostDatabase=mmonsterpatch_tradingpost
SocialDatabase=mmonsterpatch_social
FilesDatabase=mmonsterpatch_files
```

`127.0.0.1` is correct because MariaDB and MMOnsterpatch server run on the same server machine.

## Optional helper

On the server machine, you can run:

```bat
CREATE_OR_UPDATE_MARIADB_USER_SQL3307_SERVER_SIDE.bat
```

This creates/updates the dedicated `mmonsterpatch` MariaDB user and updates `database.ini` if it is beside the BAT.

## Runtime folders

The clean runtime still needs:

```text
Server\MMOnsterpatchRepackManager.exe
Server\AccountWebsiteServer\ConfigServer\OfficialServerServer\PatchPayloadServer\LogsServer\Backups```

`OfficialServer\data\` may still be created for live non-database files such as `runtime_status.json`, chat logs, user reports, and archived character snapshots. That is expected.

The old active database files are no longer runtime requirements:

```text
auth.db
accounts.json
launcher_sessions.json
social.db
bank.db
server_modules\dataank.db
```

Keep them only in your external backup.


---

## README_MARIADB_NATIVE_v0.15.4_SAVE_PERF.md

# MMOnsterpatch v0.15.4 MariaDBNative Save Performance

This build assumes MariaDB is already the official runtime storage on the server and the imported databases exist on port `3307`.

## Build

On the workstation, run:

```bat
BUILD_MARIADB_NATIVE_OFFICIAL_AND_COPY_SERVER.bat
```

Then copy `Releases\Server` to the server machine.

## Runtime database config

On the server machine, confirm:

```ini
[Database]
Provider=MariaDB
Host=127.0.0.1
Port=3307
User=mmonsterpatch
Password=YOUR_PASSWORD
AuthDatabase=mmonsterpatch_auth
TradingPostDatabase=mmonsterpatch_tradingpost
SocialDatabase=mmonsterpatch_social
FilesDatabase=mmonsterpatch_files
```

## New client performance config

The patcher adds these settings to `BepInEx\config\goose.monsterpatch.officialserver.cfg` after launching once:

```ini
[Official Server Network]
AsyncOfficialSaveUploads = true
FinalSaveFlushTimeoutMs = 2500
WorldRatesRefreshMinIntervalSeconds = 30
```

`AsyncOfficialSaveUploads=true` is the main freeze reduction. It keeps blocking local disk writes while queueing normal online save uploads to a background worker.

`FinalSaveFlushTimeoutMs` controls how long disconnect/quit waits for the newest queued save. Increase for maximum safety on slow links; decrease if exit still feels too hitchy.

`WorldRatesRefreshMinIntervalSeconds` avoids duplicate rates fetches during title login -> save-select -> load flow.
