@echo off
echo MMOnsterpatch current port check
echo.
echo Expected current live ports:
echo   61526 Trading Post
echo   61527 Steam OpenID / mon-auth tunnel target
echo   61528 MMO multiplayer
echo   61529 Social chat/guild
echo   8080  Account Website local tunnel target
echo.
netstat -ano | findstr ":61526 :61527 :61528 :61529 :8080"
echo.
echo If a port is already in use before starting the server, check the PID above:
echo   tasklist /FI "PID eq PIDNUMBER"
pause
