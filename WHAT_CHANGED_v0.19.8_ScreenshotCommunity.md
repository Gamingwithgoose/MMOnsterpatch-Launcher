# Screenshot website support v0.19.8

MMOnsterpatch v0.19.8 - Themed screenshots and website posts

Start Online Play using this Release folder's MMOnsterpatchLauncher.exe and enter your save.
No game-server restart or website deployment is needed: this build uses the existing community API.

What changed
- PHOTO and preview now reuse the chat window's native font, cream panels, dark borders, and button/input styling.
- The preview adds a caption and Friends/Everyone audience choice. Friends is the initial default.
- Post to website verifies the real launcher session, uploads the PNG, and creates a post under that account.
- Save locally is still available. Facebook and X sharing are not included.

Controls
- Click PHOTO or press F8 to capture. The button and preview hide during capture.
- Drag PHOTO to move it; drag its bottom-right // corner to resize.
- Drag the preview title bar to move it, or its bottom-right corner to resize.
- F9 resets layout. Existing positions/sizes are kept, subject to the larger preview's minimum size.
- Retake keeps your caption. Close/Escape discards an unsaved preview while idle.
- Save locally writes a PNG to Pictures/MMOnsterpatch Screenshots (LocalApplicationData fallback).
- Posting does not happen on capture or Save locally. Press Post to website explicitly.
- Cancel stops a website request. If its outcome cannot be confirmed, check your feed before creating another post.

Suggested in-game checks
1. Confirm the PHOTO button and preview font/panels match chat/Trading Post.
2. Move and resize both; capture and inspect the image.
3. Add a caption, choose Friends or Everyone, and press Post to website when ready to publish.
4. Check the community feed at https://mmonsterpatch.gamingwithgoose.com for your post and audience.
5. Confirm Save locally still works and that closing/reopening through the launcher preserves layout.

Limits and failure behavior
- Images must be 8 MB or less; captions may contain up to 5000 characters.
- Expired/rejected sessions require signing into the launcher again.
- The preview/caption remain available after a failed request.
- Ambiguous post results disable reposting that draft to avoid duplicates; inspect your feed first.
- Uploads use HTTPS and a real session token, never an account ID as a credential.

Verification completed without launching the game
- Client and Windows single-file launcher built successfully; embedded payload verified.
- 13 automated tests used simulated HTTP responses; no real images or posts were sent.
- Live read-only /health and /api/account/me checks confirmed the community backend and matching launcher account.
- Native game HTTP/serialization assemblies are used rather than requiring System.Web.Extensions.
- In-game appearance and real screenshot posting still need your test.
- Package vulnerability-data lookup reported a NuGet connectivity warning; compilation/publishing succeeded.
