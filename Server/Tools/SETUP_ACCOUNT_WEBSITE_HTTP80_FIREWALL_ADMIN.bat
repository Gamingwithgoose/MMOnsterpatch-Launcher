@echo off
echo v0.12.8 does not use direct public port 80 for Account Website.
echo Cloudflare Tunnel should publish account.gamingwithgoose.com to http://127.0.0.1:8080.
echo.
echo Opening local/private TCP 8080 firewall rule only, just in case local testing needs it.
netsh advfirewall firewall add rule name="MMOnsterpatch Account Website Local 8080" dir=in action=allow protocol=TCP localport=8080 2>nul
echo.
echo Done. Test local:
echo   http://127.0.0.1:8080/health
echo.
pause
