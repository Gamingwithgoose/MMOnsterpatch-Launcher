@echo off
setlocal

echo MMOnsterpatch managed-process cleanup
echo This stops common leftover server-manager child processes that can lock the Server folder or ports.
echo.

taskkill /F /IM MMOnsterpatchRepackManager.exe 2>nul
taskkill /F /IM MMOnsterpatch.AccountWebsite.exe 2>nul
taskkill /F /IM MMOnsterpatch.AuthServer.exe 2>nul
taskkill /F /IM MMOnsterpatch.WorldServer.exe 2>nul
taskkill /F /IM MMOnsterpatchServer.exe 2>nul
taskkill /F /IM python.exe 2>nul
taskkill /F /IM pythonw.exe 2>nul

echo.
echo Current port listeners after cleanup attempt:
netstat -ano | findstr ":61526 :61527 :61528 :61529 :8080"
echo.
pause
