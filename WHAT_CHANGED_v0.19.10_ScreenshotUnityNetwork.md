# Screenshot Unity networking v0.19.10

MMOnsterpatch v0.19.10 - Unity-native screenshot website networking

Addresses the local "Object reference not set" failure during screenshot posting.
The game now sends the account check, screenshot upload, and post request through UnityWebRequest (the game's own HTTPS stack). The default Mono HttpClientHandler is not used by in-game posting. The existing account/opaque-session validation and explicit Post action remain.

The previous build did not log the original failing stack, so its exact failing line could not be established from the existing log. This build adds credential-safe exception-type/stack diagnostics and the failing posting stage. It does not log tokens, headers, account-response bodies, or raw HTTP errors.

Close the game and launcher normally, then run this Release folder's MMOnsterpatchLauncher.exe and try screenshot posting again. No server restart or website deployment is required.

Validation
- Client and Windows launcher build passed, with matching embedded client payloads.
- 15 simulated community protocol tests and 6 simulated Unity transport/frame tests passed (21 total).
- Transport checks cover bytes/headers, no redirects, cancellations, HTTP rejection, failed connection, and disposal.
- No game or browser was launched; no live image or post was sent. Native in-game networking still needs your test.

If another failure occurs, the game log now includes [MMOnsterpatch Screenshot] Posting failed at ... followed by exception types and stack traces. The Unity player log is under AppData/LocalLow/SmashGames/Monsterpatch/Player.log; BepInEx logs may also contain these entries while the game is running. These are read-only diagnostic sources; the launcher may restore/remove staged BepInEx files after the game exits.
