@echo off
echo Checking local Account Website on http://127.0.0.1:8080/health
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8080/health | Select-Object -ExpandProperty Content } catch { Write-Host $_.Exception.Message; exit 1 }"
echo.
echo Public Cloudflare test:
echo   https://account.gamingwithgoose.com/health
echo   https://account.gamingwithgoose.com/patch/manifest.json
pause
