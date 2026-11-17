# New Mailbox source import — November 17, 2026

Imported Launcher/, Client/, Server/, Docs/, NuGet.Config, and the root build scripts from:
`C:/Users/maver/OneDrive/Desktop/Monsterpatch Stuff/New Mailbox/`

The original Desktop folder remains intact. The existing repository README and launcher ZIP remain intact. No build, launch, game modification, server startup, or connection fix was performed as part of this import. Evaluation and testing are deferred at the user's request.

## Which source corresponds to the existing ZIP?

The repository's MMOnsterpatchLauncher.zip contains one MMOnsterpatchLauncher.exe.
Its SHA-256 is `d86dd3dace2b0b5ca9b0943c4d9ffacd84cb32776549882d8c278fa5ebeee1de`.
It is byte-for-byte identical to `MMOnsterpatch v0.18.7 Latest/Releases/Launcher/MMOnsterpatchLauncher.exe`.
The selected New Mailbox release executable differs from that ZIP. This import intentionally uses New Mailbox as requested.

## Selected source

New Mailbox includes the WPF Windows launcher, Avalonia cross-platform launcher, shared Core, client patcher, account website, server manager, Python official server, SQL schemas, platform payloads, and build scripts. Its root changelog and shared Core identify v0.19.6; the WPF project still identifies v0.18.4. Folder names and version labels alone do not establish a reproducible binary-to-source match.

New Mailbox's launcher source is otherwise identical to Linux Launcher's source; their embedded client patcher DLLs differ. New Mailbox has the mailbox client code and updated payloads.

## Files intentionally left out of source import

Generated bin/obj and Releases output, old reference-only launcher payload, Python caches, debug symbols, and runtime logs/databases are omitted. Required embedded payload DLLs are retained. Client/lib game reference assemblies are copied locally but ignored by Git; the included preparation script can recreate them from the installed game.

Database and official-server config templates are provided as *.example.ini. The corresponding local *.ini files are ignored by Git. Nonempty password/API-key fields in imported configuration are cleared; the originals remain in the Desktop source folder.

## Work to resume later

Reported issue: the title screen's Log In button fails when entering the game. Steam authentication is a suspicion, not a confirmed cause. Review the client/server account-token and Steam-auth path together before changing it.

Do not assume the existing repository ZIP contains these newly imported mailbox changes. It has not been rebuilt or replaced. Build scripts are included for later use and have not been executed.
