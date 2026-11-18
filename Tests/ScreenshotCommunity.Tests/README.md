These tests simulate HTTP responses and do not start the game, contact a live website, upload images, or create real posts.

Build: dotnet build Tests/ScreenshotCommunity.Tests/ScreenshotCommunity.Tests.csproj -c Release
Run on Windows: Tests/ScreenshotCommunity.Tests/bin/Release/net472/ScreenshotCommunity.Tests.exe

The suite checks account/session matching, multipart PNG upload, audiences, size limits, foreign media rejection, expired sessions, duplicate prevention, retry behavior, invalid responses, and HTTPS requirements.
