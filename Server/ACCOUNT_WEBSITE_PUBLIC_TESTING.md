# Account Website Public Testing - v0.12.4

This build changes the Account Website from a local-only style placeholder into a public HTTP test endpoint.

## What changed

- Account Website now reads `Config/website.ini`.
- Default bindings are `http://0.0.0.0:80` and `http://0.0.0.0:8080`.
- Public test URL is `http://account.gamingwithgoose.com`.
- HTTPS is intentionally not forced yet because SSL/cert/reverse-proxy work is still separate.
- The Server Manager View button opens the local test URL first: `http://127.0.0.1:8080/`.

## Setup on the server

1. Copy the whole `Releases\Server` folder to the server.
2. In `Tools`, right-click `SETUP_ACCOUNT_WEBSITE_HTTP80_FIREWALL_ADMIN.bat` and choose **Run as administrator**.
3. Open `MMOnsterpatchRepackManager.exe`.
4. Click **Start Website**.
5. On the server, test `http://127.0.0.1:8080/health`.
6. From outside, test `http://account.gamingwithgoose.com/`.

## If public URL still does not load

Check these before changing code:

- DNS for `account.gamingwithgoose.com` points to the server/public IP.
- Windows Firewall allows TCP 80.
- Router/NAT/provider firewall forwards/allows TCP 80 to the server.
- No IIS/Apache/nginx/other service is already using port 80.
- If you require HTTPS, add a reverse proxy/certificate later. This build serves plain HTTP for first public testing.

## Launcher auth note

The launcher login window is still placeholder/local UI. It is not real account credential auth yet. Register/Forgot Password links now open the HTTP account website test endpoint. Real account credentials and Steam-on-account-creation flow are future work.
