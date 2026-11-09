# JellyShare

Jellyfin plugin that adds a **Share** button to item detail pages and a temporary-link API. Share links stream a single media file to anyone with the link, until they expire or are revoked.

Install it through the [registry](../../README.md), or build it yourself (see below).

## How it works

- **Server** (C#, net8.0, Jellyfin.Controller 10.10.0): `ShareController` exposes the endpoints, `ShareTokenService` stores tokens (256-bit, URL-safe base64) in `JellyShare.tokens.json` in the plugin configuration directory.
- **Client** (`Resources/jellyshare.js`): injected into the Jellyfin web UI. It adds a Share button to movie and episode pages and a Share entry to item action sheets.
- **Admin page** (`Resources/admin.html`): Dashboard → Plugins → JellyShare lists active links with Copy, Extend, and Revoke.

On startup the plugin adds `<script src="/JellyShare/ClientScript" data-jellyshare-plugin defer></script>` to `index.html`. If the web root is read-only (as in the Docker image), the injection fails silently. Use the manual fallback below.

### Manual fallback

Dashboard → General → **Custom JavaScript**:

```js
var s=document.createElement('script');s.src='/JellyShare/ClientScript';s.defer=true;document.head.appendChild(s);
```

## API

Replace `YOUR_SERVER` with your Jellyfin address and `YOUR_API_KEY` with an API key.

**Create a share link** (requires auth):

```bash
curl -X POST https://YOUR_SERVER/share/create \
  -H "Authorization: MediaBrowser Token=\"YOUR_API_KEY\"" \
  -H "Content-Type: application/json" \
  -d '{"itemId":"<32-hex-guid>","expirationMinutes":120}'
```

Response:

```json
{ "token": "abc123...", "expiresAt": "2026-04-20T15:00:00Z", "shareUrl": "https://YOUR_SERVER/share/abc123..." }
```

**Stream** (no auth):

```bash
curl -L "https://YOUR_SERVER/share/abc123..." -o movie.mp4
curl -L "https://YOUR_SERVER/share/abc123..." -H "Range: bytes=0-5242880" -o chunk.mp4
```

**Other endpoints** (require auth): `GET /share/list`, `DELETE /share/{token}` (revoke), `POST /share/{token}/extend` with body `{"additionalMinutes": 60}`.

## Build

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet publish plugins/JellyShare/JellyShare.csproj -c Release -o /src/publish
```

Tests:

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test plugins/JellyShare/tests/JellyShare.Tests/JellyShare.Tests.csproj
```

## Limits and known gaps

- Any authenticated user can list, revoke, and extend every share link.
- The admin page is available to any authenticated user, not only administrators.
- Tokens are not bound to the user who created them.
- Expiry is limited to 1–10080 minutes per request; there is no cap on total lifetime with repeated extensions.
- Behind a reverse proxy, share URLs use `X-Forwarded-Proto` and `X-Forwarded-Host`. Access logs at the proxy record the token in the path; filter them if that matters to you.
