# Screenshot session fix v0.19.9

MMOnsterpatch v0.19.9 - Screenshot posting session fix

Fixes the false "Your launcher session is unavailable" message for players who are already logged in.
The community backend issues opaque session tokens without the old la_/aio_ prefixes. Screenshot posting now accepts those tokens and verifies them with the website before any upload. Account IDs are still rejected as credentials.

Close the game and launcher normally, then use this version's MMOnsterpatchLauncher.exe and enter Online Play. Take a screenshot and choose Post to website again. No server restart is needed. A fresh login should only be needed if the website reports that the actual session expired.

Verification: 15 simulated HTTP tests passed, including opaque-token acceptance and invalid-token rejection. The actual updated posting client also passed a read-only check against the website using the current saved launcher session. Client/launcher build and embedded-payload checks passed. No game was launched, and no image or post was sent.

The themed, movable/resizable screenshot button/preview, caption, Friends/Everyone audience, local PNG saving, and F8/F9 controls remain available.
