# VPS deployment

The current version of the application is meant for: one server, low reliability expectations. Choices favor fewer moving parts over hardening.

---

## Topology

Everything runs on one server, which has the GPU. One Compose project holds four services on one Docker network:

| Service      | Image                                  | Listens on | Published to the host |
| ------------ | -------------------------------------- | ---------- | --------------------- |
| `caddy`      | `caddy:2`                              | 80, 443    | `80:80`, `443:443`    |
| `gateway`    | built from `src/LlmVoucherGateway.Api` | 8080       | `127.0.0.1:8080:8080` |
| `web`        | built from `src/LlmVoucherGateway.Web` | 8501       | none                  |
| `llm-server` | `vllm/vllm-openai` (pinned tag)        | 8000       | none                  |

Caddy is the only public entry point. vLLM is reachable only from inside the Docker network.

---

## Packaging

Two images are ours; vLLM is the official image, unchanged.

### Gateway API

- Multi-stage Dockerfile: build on `mcr.microsoft.com/dotnet/sdk:10.0`, run on `mcr.microsoft.com/dotnet/aspnet:10.0`
- Listens on **8080**, the .NET image default (`ASPNETCORE_HTTP_PORTS`). `launchSettings.json` and port 5131 are dev-only
- Runs as `Production`, so only [appsettings.json](../src/LlmVoucherGateway.Api/appsettings.json) loads. Its empty values plus `ValidateOnStart` mean the container will not boot until the environment supplies:
  - `Inference__BaseUrl=http://llm-server:8000`
  - `Admin__Keys__0` — from `ADMIN_KEY` in the server's `.env`. Compose refuses to start without it
  - `Database__Path=/data/vouchers.db`
- Runs as the non-root `app` user, **UID 1654**. The host directory mounted at `/data` must be writable by it: `chown 1654 /srv/llm-gateway/data`
- Mount the **directory**, not the database file. WAL mode keeps `vouchers.db-wal` and `vouchers.db-shm` beside it

### Streamlit web UI

- `python:3.12-slim`, `pip install -r requirements.txt`
- `streamlit run app.py --server.address 0.0.0.0 --server.port 8501 --server.headless true`
- `GATEWAY_BASE_URL=http://gateway:8080/v1`
- Pin versions in [requirements.txt](../src/LlmVoucherGateway.Web/requirements.txt) before the first image build

### vLLM

- Same GPU reservation and launch flags as [docker-compose.llm.yml](../docker-compose.llm.yml), including `--enable-force-include-usage`
- The image tag is written out in both compose files rather than taken from `.env`. It is the pin the captured [api_documentation/](../docs/api_documentation/) describes, so it changes in a commit, not per machine
- **No `ports:`**
- No `--api-key`. Nothing outside the Docker network can reach it, and the gateway sends no upstream key. If vLLM ever gets a published port or moves to another host, add `--api-key` and have the gateway send it in the same change
- `VLLM_WSL2_ENABLE_PIN_MEMORY=0` on the native Linux host, which is what both compose files default to. A WSL2 dev box is the one that sets it to 1, in its own `.env`

### Compose and build

- One production file, `docker-compose.prod.yml`, with all four services. `docker-compose.llm.yml` stays as the dev file
- Every service: `restart: unless-stopped`, and log rotation:

  ```yaml
  logging:
    driver: json-file
    options:
      max-size: 10m
      max-file: "5"
  ```

- Images are built on the server from a git checkout; no registry. Every change ships as `make deploy`, which pulls the git repo and build the image. Then `make run-prod` to bring it up.
- Lifecycle runs through the [Makefile](../Makefile), so one file shows how everything starts in both environments: `make deploy`, `run-prod`, `down-prod`, `ps-prod`, `logs-prod service=<name>`. They add the `-f docker-compose.prod.yml` flag and nothing else — `.env` stays the only place values are set
- Per-machine values live in the server's `.env`, not in git. [.env.example](../.env.example) is the list. `ADMIN_KEY`, `VLLM_MODEL` and `VLLM_GPU_MEMORY_UTILIZATION` are **required**: compose stops with a message naming the variable rather than falling back to a dev value. `VLLM_PORT` is unused here and `GATEWAY_DATA_DIR` defaults to `/srv/llm-gateway/data`
- `chmod 600 .env`. It holds the admin key
- Compose project name is `llm-voucher-gateway-prod`, so it never collides with the dev project in the same checkout. On a dev box that means a separate Hugging Face cache volume, and the model downloads again
- Caddy's config is bind-mounted from `./Caddyfile` beside the compose file. The mount uses long syntax, so a missing Caddyfile fails the start rather than being created as an empty directory
- `depends_on` only orders startup. vLLM takes minutes to load a model; until it is ready the gateway is up but inference requests fail

### When images get built

The dev workflow stays as it is: vLLM in a container, gateway and web UI on the host (`make run-llm`, `make run-api`, `make run-web`). The Dockerfiles arrive with Milestone 8, because Caddy, forwarded headers and the file log sink can only be exercised in the deployed shape.

---

## Exposing the web app and the API

The gateway is a product in its own right, not only Streamlit's backend. It is a pass-through to vLLM and the endpoint for managing vouchers.

| Caller                                                  | Address                                                  | Path through the stack         |
| ------------------------------------------------------- | -------------------------------------------------------- | ------------------------------ |
| Harnesses and IDE extensions (Aider, OpenCode, VS Code) | `https://llm.example.edu/v1`, voucher as bearer          | Caddy → `gateway:8080`         |
| Admin, managing vouchers                                | `https://llm.example.edu/admin/...`, admin key as bearer | Caddy → `gateway:8080`         |
| Admin, on the server                                    | `http://localhost:8080/...` over SSH                     | Host loopback → `gateway:8080` |
| Students in a browser                                   | `https://chat.example.edu`                               | Caddy → `web:8501`             |
| Streamlit's server process                              | `http://gateway:8080/v1`                                 | Docker network only            |

- `/admin` is public over HTTPS and protected by the admin key alone
- The loopback port is invisible from outside. It lets the admin skip Caddy and TLS, and it tells a proxy problem apart from a gateway problem
- The Makefile's `api-*` targets still hardcode `localhost:5131` and `dev-admin-key`. A `GATEWAY_URL` variable defaulting to the dev address, and `ADMIN_KEY` from `.env`, let the same targets run against `localhost:8080` on the server — the first step of [Milestone 8c](pilot-roadmap.md#8c-gateway-on-host-loopback)

**Docker-published ports bypass `ufw`.** Docker writes its own iptables rules. The only protection for `gateway` and `llm-server` is not publishing them beyond loopback.

---

## Reverse proxy

Caddy terminates TLS and routes by hostname. Without it, voucher and admin keys would cross the internet in cleartext.

Written out in [Caddyfile](../Caddyfile), which uses `{$LLM_HOSTNAME}`, `{$CHAT_HOSTNAME}` and `{$ACME_EMAIL}` rather than literal hostnames — Caddy substitutes them at parse time, so the institution's real hostnames stay in `.env`. Compose requires all three.

- **Certificates:** automatic from Let's Encrypt. Needs DNS A records for both hostnames and ports 80 and 443 open. Persist Caddy's `/data` in a named volume, or every rebuild re-requests certificates and runs into rate limits
- **Cloudflare records are DNS only (grey cloud), not proxied.** Cloudflare's proxy cuts responses off at 100 seconds, which kills long generations, and it puts its own addresses in front of every client
- **Hostnames over paths:** Streamlit under a subpath needs `baseUrlPath` and is fiddly. A hostname per app keeps the client base URL a plain `https://llm.example.edu/v1`
- **Streaming:** Caddy flushes `text/event-stream` immediately; `flush_interval -1` makes that explicit. **No `encode` on the gateway site**, since compression is the likeliest thing to buffer a stream. Re-run Milestone 3's per-chunk timing through Caddy
- **Timeouts:** Caddy and Kestrel have no default response timeout, and the gateway's `HttpClient.Timeout` is infinite. Long generations survive end to end. Do not add a timeout without considering streams. The Streamlit client is the one hop with a timeout, `GATEWAY_TIMEOUT_SECONDS` (default 300), and it is a read timeout reset by every chunk — so it bounds silence from the server, mostly time to first token, not generation length
- **Client addresses:** set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the gateway. It trusts `X-Forwarded-For` from any sender, which is safe here because the only non-loopback way in is Caddy. Without it every logged address is Caddy's
- **WebSockets:** Streamlit needs them; Caddy proxies them with no configuration
- **Rate limiting:** none. Voucher allowances cap usage

---

## Logs

Access is SSH to the server.

### Live

```
make logs-prod service=gateway
```

Swap `gateway` for `caddy`, `web` or `llm-server`; omit `service` for all of them. Console logs rotate per the Compose `logging` block and disappear whenever a container is recreated, including on every rebuild. They are for watching, not for record.

### Durable

Gateway logs also go to a Serilog file sink on the mounted volume (Milestone 8):

- `Serilog.Sinks.File`, path `/data/logs/gateway-.log`, `rollingInterval: Day`, `retainedFileCountLimit: 30`
- Compact JSON (`Serilog.Formatting.Compact`), read with `jq`:

  ```
  jq 'select(.["@l"] == "Warning")' /srv/llm-gateway/data/logs/gateway-20261107.log
  ```

- The files sit beside the database, so they outlive containers and travel with backups

### What must not appear

The gateway already never logs keys, `Authorization` headers, or bodies. Two other services need a one-time eyes-on check against real output:

- Caddy redacts `Authorization` in access logs by default
- vLLM's request logging, which can include prompt text, is off in the pinned version

---

## Database

- Lives at `/srv/llm-gateway/data/vouchers.db` on the host, `/data/vouchers.db` in the container
- Migrations apply at gateway startup. Right for one instance
- **Backups** from host cron, never `cp`:

  ```
  sqlite3 /srv/llm-gateway/data/vouchers.db ".backup /mnt/backup/vouchers-$(date +%F).db"
  ```

- **Ad-hoc queries:** `ghcr.io/coleifer/sqlite-web:0.8.2` as a Compose service under `profiles: [tools]`, published on `127.0.0.1:8081` only, reached with `ssh -L 8081:localhost:8081 server`. Started only when needed, and stopped after: `docker compose --profile tools up -d sqlite-web`, then `docker compose --profile tools down`. It has no authentication of its own - just uses SSH.
