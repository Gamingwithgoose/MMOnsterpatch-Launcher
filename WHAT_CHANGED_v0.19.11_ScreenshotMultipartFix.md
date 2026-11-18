# Multipart disposal fix v0.19.11

MMOnsterpatch v0.19.11 - Confirmed multipart disposal fix

The game log identified System.Net.Http.MultipartContent.Dispose as the failing method. The upload code previously disposed its image, form, and request separately, causing the form to be disposed twice. Inspection of the game's own Mono HTTP assembly confirms that MultipartContent clears its internal list on disposal and crashes if disposed again.

Fixed the upload to use one ownership chain: request owns form, form owns image. Only the request is explicitly disposed. This also retains uploaded-image state and the existing website/account checks.

Close the game and launcher normally, then start Online Play with this version's MMOnsterpatchLauncher.exe and try Post to website again. No server restart is required.

Verification: 16 simulated community tests plus 6 simulated Unity-network tests passed (22 total), including a strict multipart owner that throws on double disposal. Client/launcher builds and embedded-payload checks passed. No game was launched and no live post was created.
