# MMOnsterpatch Artifacts and Baselines - v0.14.3

## Current source package
- `MMOnsterpatch_YOLO_v0.14.3_FollowerAnimationTierFix_SourceWorkspace.zip`

## Confirmed stable/current baselines from this session
- `v0.13.0 Stable` - launcher account handoff worked: website account -> launcher login -> Online Play -> title Log In -> online save selector.
- `v0.13.2 SocialsFix` - MMO/social/chat authentication restored using launcher account handoff.
- `v0.14.0 Baseline` - follower source/runtime merged into AIO; no separate follower runtime DLL required; fixed the broken follower standalone experiments.
- `v0.14.2 WorldSpawnLifecycleFix` - world spawn lifecycle/population/water-zone logic worked pretty well and is carried forward.

## Major project folders in this workspace
- `Launcher/MMOnsterpatchLauncher.Wpf/` - WPF launcher source and embedded payload folder.
- `Client/MMOnsterpatchOfficialServerPatcher/` - AIO/client patcher source, including merged follower runtime.
- `Client/Mods/Monsterpatch_Followers/` - old/included follower source reference only; current live follower behavior is merged into AIO.
- `Server/OfficialServer/` - current Python official server.
- `Server/PatchPayload/` - server-side patch payload/manifest source.
- `Server/MMOnsterpatchRepackManager.Wpf/` - server manager source.
- `Server/AccountWebsite/` - account website source.
- `Releases/` - build output staging folders populated by the root build.

## Runtime data to preserve when replacing server files
Do not overwrite/delete these when updating a live server:
- `Releases/Server/Data/Accounts/`
- `Releases/Server/OfficialServer/data/`

These contain account/session DBs and character/social/trading data.

## Important public routes/ports
- `account.gamingwithgoose.com` -> Cloudflare Tunnel -> `127.0.0.1:8080` Account Website.
- `mon-auth.gamingwithgoose.com` -> Cloudflare Tunnel -> `127.0.0.1:61527` Steam/OpenID/current auth HTTP callback route.
- `mon.gamingwithgoose.com` -> DNS-only raw TCP -> `61526` Trading Post.
- `mmo.gamingwithgoose.com` -> DNS-only raw TCP -> `61528` MMO and `61529` social/chat/guild.

## Build flow
- Run `CLEAN_AND_BUILD_ALL.bat` at the workspace root.
- BAT calls `Clean_And_Build_All.ps1`.
- Script prepares `Client/lib` from local Monsterpatch install, builds the AIO client patcher, stages the patcher into launcher/server payloads, publishes launcher/server tools, and syncs release folders.
- Do not change the root build flow without explicit approval.
