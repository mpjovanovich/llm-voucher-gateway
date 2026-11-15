# Pilot Roadmap

_This file holds work in progress developer documentation, not finished product documentation._

Each milestone ends in something demonstrable. Ordering favors killing unknowns early over building in layers.

This is a temporary working document. Settled detail lives in the code and tests; this keeps status, decisions whose reasoning the code can't show, and the open work.

---

## Environment split

- **GPU box** — runs vLLM. Any dev machine with an NVIDIA GPU. The only place real responses come from.
- **Non-GPU machine** — AVX2 only, so it cannot run vLLM's CPU backend. No local inference.

**Requires live vLLM:** Milestones 1, 2, 3, 6g, 10. Everything else can proceed without a GPU.

The dev GPU box is lower spec than production. These milestones prove shape and plumbing, not performance; performance is Milestone 10.

---

## Completed: Milestones 1–5, 6a–6f

| Milestone                                                                                                  | Where to look                                                                                                                                                                                                                                                                                                                 |
| ---------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1. vLLM on a GPU host, capture set                                                                         | [api_documentation/](../api_documentation/)                                                                                                                                                                                                                                                                                   |
| 2. Aider against vLLM directly                                                                             | Baseline for Milestone 3                                                                                                                                                                                                                                                                                                      |
| 3. Pass-through API                                                                                        | [api_documentation/README.md](../api_documentation/README.md#streaming-baseline--milestone-3), [LlmVoucherGateway.Api.http](../src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.http)                                                                                                                                          |
| 4. Persistence (SQLite, EF Core)                                                                           | [Persistence/](../src/LlmVoucherGateway.Api/Persistence/)                                                                                                                                                                                                                                                                     |
| 5. Voucher issuance, admin auth, issuance logging                                                          | [Endpoints/](../src/LlmVoucherGateway.Api/Endpoints/), [Credentials/](../src/LlmVoucherGateway.Api/Credentials/)                                                                                                                                                                                                              |
| 6a–6f. Error contract, fake inference client, spendability, voucher lookup, enforcement, rejection logging | [Errors/](../src/LlmVoucherGateway.Api/Errors/), [CredentialMiddleware.cs](../src/LlmVoucherGateway.Api/Middleware/CredentialMiddleware.cs), [VoucherRequiredEndpointFilter.cs](../src/LlmVoucherGateway.Api/Credentials/VoucherRequiredEndpointFilter.cs), [Voucher.cs](../src/LlmVoucherGateway.Domain/Vouchers/Voucher.cs) |

Run with `make run-llm && make run-api`.

### Findings worth carrying forward

- **Aider requests only streamed `POST /v1/chat/completions`** — no `GET /v1/models`, no tool calling. Each task issues two requests: the edit, then Aider's automatic lint-fix follow-up. A different client may request other paths.
- **Tool calling is not enabled.** No tool-call shape was captured, and the compose command lacks `--enable-auto-tool-choice --tool-call-parser hermes`. If a harness sends `tools`, that is a return trip to Milestone 1.
- **Streaming passthrough is proven unbuffered only against vLLM direct** (Milestone 3, per-chunk timing). The fake inference client uses a `MemoryStream`, so the test suite cannot catch buffering. It must be re-proven through Caddy in Milestone 8.
- **The pinned vLLM image tag is the drift control.** There is no CI schema check; Milestone 10 re-pulls and diffs `openapi.json` if the version changes.

### Decisions and their reasons

- **SQLite over Postgres.** One table, one indexed lookup per request, under 20 users. SQLite buys a single container, no credentials, and a shorter recovery procedure. Postgres is the upgrade path, and cheap: a connection string and regenerated migrations. Take it if the deployment grows past one API instance or a second writer appears.
- **Migrations apply at startup** (`Database.Migrate()`). Right for one instance, wrong for two.
- **Backups cannot be `cp`.** In WAL mode a live copy can be torn. Use `sqlite3 vouchers.db ".backup /mnt/backup/vouchers.db"` or `VACUUM INTO`.
- **No lookup cache.** A lookup is free at this scale, and a cache would delay revocation. Revisit only if Milestone 10 shows a reason.
- **Admin auth is an endpoint filter on the `/admin` group, not ASP.NET's auth stack.** The stack would duplicate the credential middleware to carry one boolean, and its framework challenges would have to be overridden for OpenAI-shaped errors anyway. New admin routes inherit the filter by joining the group.
- **Credential resolution and rejection are separate.** The middleware only resolves (a revoked or exhausted voucher still resolves); endpoint filters decide. The resolved credential carries the loaded, tracked `Voucher` so Milestone 7 can record consumption without a second query.
- **Status map:** unknown, revoked and expired are `401 invalid_request_error`; exhausted is `403 insufficient_quota`. `403` was chosen over `402` and `429`. `code` is numeric, matching vLLM rather than OpenAI. Admin `401`s keep empty bodies because only operators with curl see them.
- **An admin key does not pass `/v1/chat/completions` or `/v1/models`.** Admitting it would leave an unmetered path to the GPU. To exercise the endpoint, mint a voucher.
- **Exhausted means consumed is at or over the allowance.** Consumption is recorded after a request, so `Consumed` can exceed `Allowance`. That overshoot is accepted under soft limits.
- **Never logged:** voucher keys, admin keys, attempted bearer tokens, voucher ids or prefixes. Time plus source address is the handle for querying the database.

---

## 6. Voucher validation

Fill in the voucher branch of the middleware from Milestone 5a. Requests without a valid voucher are rejected.

- Hash + lookup, straight to the database — **no cache.** Under 20 users on requests that occupy a GPU for seconds, a lookup is free, and caching would buy a revocation delay for no measurable gain. Revisit only if Milestone 10 shows a reason
- Reject: unknown, revoked, expired, allowance exhausted
- **Errors must be OpenAI-shaped**, matching the error payloads captured in Milestone 1. A harness receiving your custom error format will render garbage or crash. Every failure has to leave through a wire format the client already understands. Errors from vLLM relay verbatim and are already correctly shaped; this is about the rejections we generate
- Distinguish 401 (bad credential) from 402 (valid but out of allowance), so a student can tell "wrong key" from "used it up"

**Depends on:** captured error payloads from Milestone 1. Without them the error shape is guesswork. The shape they fix is `{"error":{"message","type","param","code"}}` — see [errors/](../api_documentation/errors/).

**Substep ordering:** 6a, 6b and 6c are independent of each other and can be taken in any order. Everything after is forced: 6a and 6b and 6d all feed 6e, 6c feeds 6d and 6e, and 6f then 6g follow enforcement. `/v1/chat/completions` stays open until 6e — the same deliberate gap as 5b before 5c, and for the same reason: a requirement needs something to attach to, and resolution is testable before rejection exists.

### 6a. The OpenAI error contract

_COMPLETE_ — see [Errors/](../src/LlmVoucherGateway.Api/Errors/) and [OpenAiErrorResultsTests.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Errors/OpenAiErrorResultsTests.cs)

A writer that produces `{"error":{"message","type","param","code"}}` with a matching status code. Nothing uses it yet; this is the wire format every later rejection leaves through, and getting it wrong is the failure the milestone warns about.

- `OpenAiError` is the envelope; `OpenAiErrorResults` exposes `InvalidCredential` and `AllowanceExhausted` as results a filter or endpoint returns
- Status map: unknown credential, revoked and expired are all `401` with type `invalid_request_error`, and the message says which. Allowance exhausted is `403` with type `insufficient_quota`
- **`code` is the numeric HTTP status, not a string.** OpenAI's own service sends a string there, but the captures show vLLM sending a number, and a client that copes with the relayed vLLM errors is the client we have to satisfy
- The tests compare the generated field set against the captured payloads rather than against a shape written down in a test, so a field added or renamed here fails immediately. Status code and content type are asserted by executing the result against a bare `HttpContext`
- The admin rejections from 5c keep their empty bodies. They are reached with curl by an operator, not parsed by a harness

**Done when:** the serialized output matches the captured payloads field for field, asserted in tests.

### 6b. Fake `IInferenceClient`

_COMPLETE_ — see [FakeInferenceClient.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Inference/FakeInferenceClient.cs) and [ChatCompletionsEndpointTests.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Inference/ChatCompletionsEndpointTests.cs)

A stand-in replaying the Milestone 1 captures, registered by the test factory in place of `VllmInferenceClient`. A class, not a stub server — nothing here points a live harness at a GPU-less box.

- Returns [non-streaming-response.json](../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/non-streaming-response.json), or the captured SSE stream [streaming-response-usage-requested-server.sse](../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/streaming-response-usage-requested-server.sse), chosen by whether the forwarded body sets `"stream": true`
- **The captures are linked into the test project as content**, not copied into it, so a re-capture cannot leave the tests asserting a stale response shape
- The fake records the bodies it was asked to forward. That is what lets 6e assert a rejected request never reached the inference server at all, rather than only checking the status a caller saw
- First tests of `/v1/chat/completions`: the response relays byte for byte, the request body is forwarded unchanged, and a streamed response ends in `data: [DONE]` — the terminator whose absence makes a harness hang rather than fail

**Does not cover:** that streaming stays unbuffered. The fake's body is a `MemoryStream`, so timing proves nothing here; that remains Milestone 3's proof against a real server, and Milestone 8's through the proxy.

**Done when:** an integration test posts to `/v1/chat/completions` and gets the captured response back, with no GPU involved.

### 6c. Domain: when is a voucher spendable?

_COMPLETE_ — see [Voucher.cs](../src/LlmVoucherGateway.Domain/Vouchers/Voucher.cs) and [VoucherSpendabilityTests.cs](../tests/LlmVoucherGateway.Domain.UnitTests/Vouchers/VoucherSpendabilityTests.cs)

`Status`, `Lifetime` and `Consumed` are currently just data — nothing in the aggregate can answer whether a voucher may be spent.

An end-to-end checkpoint for what the integration tests can't see. The proof of concept rests on it.

The client in use is a [Streamlit web UI](../src/LlmVoucherGateway.Web/app.py) rather than Aider at first. It calls the gateway from its Python server process, so CORS is not a factor. It reads `GATEWAY_BASE_URL`, which defaults to the `make run-api` address. `GET /v1/models` is now passed through behind the voucher filter, so a bad voucher shows up in the sidebar before a prompt is typed.

Mint vouchers with `POST /admin/vouchers` (see [LlmVoucherGateway.Api.http](../src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.http)). There is no revoke endpoint until Milestone 9 and no consumption recording until Milestone 7, so set those states in the database directly: `src/LlmVoucherGateway.Api/.data/vouchers.db`, table `Vouchers`, columns `Status` (`'Active'` / `'Revoked'`), `Consumed`, `Allowance`, `Lifetime_ExpiresAt`, `Key_Prefix`.

- **6g.1** _COMPLETE_ — Web client base URL from config
- **6g.2** _COMPLETE_ — `GET /v1/models` pass-through
- **6g.3** _COMPLETE_ — Streamlit against the gateway with a valid voucher

**Done when (whole milestone):** a valid voucher works, and an invalid one fails cleanly _in the client_, not just in curl.

---

## 7. Consumption recording

_Complete_

- Read `usage.total_tokens` from the response and add it to the voucher's consumption. `total_tokens` already includes the prompt; adding `prompt_tokens` again would double-bill it
- Streaming: vLLM forces usage server-side with `--enable-force-include-usage` (set in `docker-compose.llm.yml`). Every chunk carries a running count, so the gateway relays bytes untouched and parses only the last `data:` line after the stream ends. See [streaming-response-forced-usage.md](../api_documentation/chat-completions/streaming-response-forced-usage.md)
- **The flag must be set wherever the pilot's vLLM runs.** Without it, streaming requests meter as nothing
- Soft limits: check on entry, record after, overshoot accepted
- Use `Voucher.RecordConsumption` against the aggregate already on the resolved credential

### Known limitations, accepted

- **Concurrent requests on one voucher can lose a charge.** Each request loads the voucher on entry and saves an absolute total, so overlapping requests (two client windows, a harness side request, a retry while the original runs) can overwrite each other. It under-meters and never blocks anyone, which soft budgets tolerate. The fix is a concurrency token on `Consumed` with reload-and-retry. Revisit with Milestone 10's concurrency numbers
- **A stream that ends in an error event is unmetered.** If vLLM fails mid-generation, the last `data:` line is the error rather than a chunk with usage. Rare enough not to justify parsing every chunk

### Developer Tests

**Done when:** consumption tracks across a real multi-turn agentic session.

---

### 7.5 Misc cleanup and live developer checks

_Complete_

- Streamlit against the gateway with rejected vouchers:
  - _COMPLETE_ — unknown
  - _COMPLETE_ — exhausted
  - _COMPLETE_ — expired (mint with an `expiresAt` a minute or two out, then wait)
  - TODO — revoked (can't test yet, no endpoint; need a way to update sqlite database easily)

- _COMPLETE_ — Check that the admin key for vouchers is working (case where we didn't mint one)

- _COMPLETE_ — Do an eyes-on check of voucher rows in `vouchers.db`, checking consumption updates.

- _COMPLETE_ - Cleanup Program.cs - break DI into files

- _COMPLETE_ - The Streamlit frontend was vibe coded and needs review and cleanup.

## 7.55 - Add LastRequestedAt field to Vouchers

_Complete_

Field has now been added, and is set whenever anyone tries to use the API with the voucher.

## 7.5 Remove hardcoded options - port to configuration

_COMPLETE_

See planning doc for this.

## 7.6 VPS deployment plan

_COMPLETE_ — see [vps-deployment.md](vps-deployment.md)

- _COMPLETE_ — Topology, packaging, gateway exposure, reverse proxy
- _COMPLETE_ — Database location, backup and ad-hoc access
- _COMPLETE_ — Log access
- _COMPLETE_ — When container images get built: Milestone 8

## 7.7 Simplify launch configuration

_Complete_ — see [vllm-configuration.md](vllm-configuration.md)

- [.env.example](../.env.example) is the contract: the list of what a machine decides, and the only place it gets set. `VLLM_MODEL`, `VLLM_GPU_MEMORY_UTILIZATION` and (in production) `ADMIN_KEY` are required, so compose stops with a message naming the variable instead of falling back to a dev value
- Defaults only where the value is not an opinion: `VLLM_PORT=8000` is the port vLLM listens on inside its container, and `VLLM_WSL2_ENABLE_PIN_MEMORY=0` is what a native Linux host wants. The duplicate default layers in the Makefile and the compose files are gone
- Literals, because none is a per-machine choice: the pinned image tag, `--enable-force-include-usage`, `count: 1` for GPUs, and the in-container addresses
- Both environments start from the [Makefile](../Makefile), so one file answers "how does everything start": dev is `run-llm`, `run-api`, `run-web`; production is `deploy`, `run-prod`, `down-prod`, `ps-prod`, `logs-prod service=<name>`. The production targets add the `-f` flag and nothing else
- `make run-api` derives `Inference__BaseUrl` from `VLLM_PORT`, so changing the port cannot leave the gateway dialing a port vLLM was not published on

## 7.7 Remaining config items so that they can be removed from below sections

### A. Config only — no code change, doable now off-GPU

| #   | Item                                                | Where it lands                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| --- | --------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A1  | Forwarded headers + explicit environment (8f, part) | Two lines in the gateway's `environment:` in `docker-compose.prod.yml`: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` and `ASPNETCORE_ENVIRONMENT=Production` (currently relying on the image default). This is the host-level switch, so no `Program.cs` change — and it retires the floating comment at `VoucherIssuanceEndpoint.cs:44`                                                                                                                                           |
| A2  | Durable file logs (8g)                              | A new `appsettings.Production.json` with a `WriteTo` File sink (`/data/logs/gateway-.log`, daily, 30 kept, `CompactJsonFormatter`). I verified the packages are already in the graph transitively via `Serilog.AspNetCore`: `Serilog.Sinks.File/7.0.0` and `Serilog.Formatting.Compact/3.0.0`. Zero code, zero new packages. A separate Production file rather than `appsettings.json` because a `WriteTo` array can't be un-set by an override, and the dev host has no `/data` |
| A3  | Makefile `GATEWAY_URL` + `ADMIN_KEY` (8c)           | The `api-*` targets hardcode `localhost:5131` and `dev-admin-key`. Add `GATEWAY_URL ?= http://localhost:5131` and take `ADMIN_KEY` from `.env`. To avoid a second copy of the dev key, `run-api` should inject `Admin__Keys__0=$(ADMIN_KEY)` the same way it now injects `Inference__BaseUrl` — `appsettings.Development.json` keeps `dev-admin-key` for the IDE path                                                                                                            |
| A4  | Caddyfile (8d)                                      | Doesn't exist yet; content is already written out in `vps-deployment.md`. Worth adding with `{$LLM_HOSTNAME}` / `{$CHAT_HOSTNAME}` / `{$ACME_EMAIL}` rather than literal hostnames — Caddy does parse-time `{$VAR}` substitution — plus an `environment:` block on the `caddy` service to pass them in. That keeps the institution's real hostnames in `.env` instead of git, which is the same test everything else passed                                                      |
| A5  | sqlite-web (8h)                                     | A service under `profiles: [tools]`, pinned tag, published on `127.0.0.1:8081` only, mounting the data volume. Pure compose                                                                                                                                                                                                                                                                                                                                                      |
| A6  | Web UI timeouts                                     | `app.py:44` hardcodes `timeout=60, max_retries=1`. `MAX_FILE_CHARS` and `MAX_HISTORY_CHARS` are already env-driven but absent from `.env.example`. Note the conflict: `vps-deployment.md:118` says long generations survive end to end, but the browser client gives up at 60s — on a slow box with a real model that will bite                                                                                                                                                  |

### B. Config, but needs a live GPU to verify

- **`VLLM_MAX_MODEL_LEN` (Milestone 10)** — a required `.env` variable feeding `--max-model-len` in both compose files. Mechanically trivial, but it changes whether KV cache fits in 8GB, so it belongs to a session where you can watch vLLM start. This is the last genuinely per-machine value that isn't yet in `.env`.
- **Tool calling (Milestone 11)** — `--enable-auto-tool-choice --tool-call-parser <parser>`. I'd leave these out. The parser name is a function of the model, so the two move with `VLLM_MODEL`, and the roadmap already says a harness sending tools is a return trip to Milestone 1 for a re-capture. Also flagging a contradiction: roadmap Milestone 11 lists `--api-key <shared-or-per-student-key>`, while `vps-deployment.md:50` deliberately decides against an upstream key. One of those should change.

### C. Needs real code — noting only, per your instruction

- **Request logging with a correlation ID (8f, rest)** — `UseSerilogRequestLogging()` is one line, but the correlation ID needs a middleware or enricher plus an `appsettings` template. Small-to-moderate.
- **`/health` and OpenAI-shaped unhandled errors (8i)** — a health check that probes the upstream, and a branch in `ExceptionHandler.cs` so `/v1` paths leave through `OpenAiErrorResults` instead of `ProblemDetails`. It currently always writes `ProblemDetails`. Moderate, and the error-shape half is the one with harness consequences.
- **Issuance alert (Milestone 9)** — would introduce a new config surface (SMTP or webhook URL). Don't invent it before the feature.

### Housekeeping found along the way

- Seven stale `api_documentation/` references from the `docs/` move. Three are broken markdown links in `pilot-roadmap.md` (lines 26, 28, 66, 124) and one in `tests/.../Fixtures/README.md:9`; the rest are prose mentions in `OpenAiError.cs:11` and the `.http` file.
- `GATEWAY_BASE_URL` is set twice — `ENV` in the Web Dockerfile and again in the prod compose `environment:`. Pick one; compose is the better home.
- The `.http` file's `@baseUrl`/`@adminKey` can't read `.env`, so those stay hardcoded. Fine, just don't count it as drift.

### Recommended sequencing

1. **A3** (Makefile `GATEWAY_URL`/`ADMIN_KEY`) first — every later verification step in Milestone 8 runs through those targets, and it's the only item you can fully test today.
2. **A1** (two compose env lines) — one minute, and it makes the issuance log line truthful the moment Caddy is in front.
3. **A2** (`appsettings.Production.json`) — write it before the first deploy so logs exist from the first request rather than being retrofitted after you've lost the interesting ones.
4. **A4** (Caddyfile + hostname variables) — the hard blocker for any HTTPS step; nothing in 8d can start without it.
5. **A5** (sqlite-web) and **A6** (web timeouts) — independent, cheap, batch them in with 4.
6. **Housekeeping links** — fold into whichever pass touches those files.
7. **B** (`VLLM_MAX_MODEL_LEN`) — at the GPU box, in the same session as the first real model load.

After 1–6, every per-machine value lives in `.env` and no literal in a compose file or `appsettings` is a machine choice. That's the point where you can call configuration done: what's left in Milestone 8 is behind the two code items in group C, and the remaining milestone references in comments are about features, not configuration.

---

## 8. Deployment and operational readiness

See [vps-deployment.md](vps-deployment.md).

Note - Small scale hardware confirmed:

```
GeForce RTX 5060 8GB vRAM
64GB RAM on host machine
```

Bring the stack up one service at a time, inside out, so each failure has one suspect. Components for production server are listed below. These are the sub-milestones.

Every `docker compose` command below means `docker compose -f docker-compose.prod.yml ...`, run from the checkout on the server. The Makefile wraps the lifecycle ones — `make deploy`, `run-prod`, `down-prod`, `ps-prod`, `logs-prod service=<name>` — and the bare form is spelled out here where a step acts on one service.

### 8a. Server ready

- Install Docker and the NVIDIA Container Toolkit. `sudo systemctl enable docker`, so Docker starts on boot
- Firewall: allow SSH **before** turning `ufw` on, then 80 and 443
- Clone the repo. Create the data directory: `sudo mkdir -p /srv/llm-gateway/data && sudo chown 1654 /srv/llm-gateway/data`
- Server `.env`: `cp .env.example .env`, then set `ADMIN_KEY` (`openssl rand -base64 32`), `VLLM_MODEL` and `VLLM_GPU_MEMORY_UTILIZATION` for this hardware. Compose refuses to start until all three are set. The pinned-memory switch defaults to 0, which is what this host wants. `chmod 600 .env`

**Verify:** `docker run --rm --gpus all ubuntu nvidia-smi` shows the GPU, and `docker compose config --quiet` prints nothing.

### 8b. vLLM, inside the box only

- `docker compose up -d llm-server`, then `docker compose logs -f llm-server` until it reports the server is listening. The first start downloads the model and takes a while

**Verify:**

- Reachable on the Docker network: the throwaway curl against `http://llm-server:8000/v1/models` lists the model
- Not reachable from the host: `curl localhost:8000` is refused

### 8c. Gateway, on host loopback

- Makefile first: replace the hardcoded `localhost:5131` and `dev-admin-key` in the `api-*` targets with `GATEWAY_URL` and `ADMIN_KEY`, defaulting to the dev values. The Makefile already reads `.env`, so adding `GATEWAY_URL=http://localhost:8080` to the server's `.env` points the same targets at production
- `docker compose up -d --build gateway`

**Verify:**

- `make api-mint-voucher` returns a voucher, and `make api-check-completion-streaming key=...` streams a reply
- `/srv/llm-gateway/data/vouchers.db` exists on the host
- `docker compose restart gateway`, then the same voucher still works. That proves the database survives the container
- From another machine, `curl http://<server-ip>:8080` fails

### 8d. Caddy, DNS and HTTPS

- DNS: A records for both hostnames pointing at the server, **grey cloud (DNS only)** in Cloudflare
- Write `Caddyfile` from [vps-deployment.md](vps-deployment.md#reverse-proxy), then `docker compose up -d --build`. This also starts `web`, which has had nothing to test it through until now

**Verify:**

- From your laptop, `curl https://llm.example.edu/v1/models` returns the OpenAI-shaped `401`, with no certificate warning
- `https://chat.example.edu` in a browser: a voucher pasted into the sidebar lists the model, and a chat streams

### 8e. Streaming through Caddy

- Re-run Milestone 3's per-chunk timing against `https://llm.example.edu/v1`
- Point Aider at the same URL and run a small task

**Verify:** chunks arrive spread out over the generation, not all at once at the end. Aider completes a task. This and 8j are the milestone's "Done when".

### 8f. Real client addresses and request logs

Code change, tested locally, then deployed.

- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` in the gateway's `environment`
- One log line per request with method, path, status, duration and client address (Serilog request logging)
- A correlation ID on every log line from the same request

**Verify:** `docker compose logs gateway` after a request from your laptop shows your public IP, not a `172.x` Docker address, and its lines share one correlation ID.

### 8g. Logs that outlive containers

- File sink to `/data/logs`, compact JSON, rolled daily, 30 days kept
- Read Caddy's and vLLM's logs after a few real requests and confirm neither contains a voucher key, an `Authorization` header or prompt text

**Verify:** a file appears in `/srv/llm-gateway/data/logs/`, still has its lines after `docker compose up -d --build` recreates the gateway, and `jq` can read it.

### 8h. Database backups and ad-hoc access

- Daily host cron running `sqlite3 ... ".backup ..."`, per [vps-deployment.md](vps-deployment.md#database)
- sqlite-web as a Compose service under `profiles: [tools]`, published on `127.0.0.1:8081` only

**Verify:**

- Run the backup by hand once, then `sqlite3 <backup> "select count(*) from Vouchers"` returns the live count. A backup isn't proven until it has been read back
- Through `ssh -L 8081:localhost:8081 server`, `http://localhost:8081` on your laptop shows the tables

### 8i. Health and error shape

Code change, tested locally, then deployed.

- `/health`: `200` when the gateway can reach vLLM, `503` when it can't
- Unhandled exceptions on `/v1` return the OpenAI error shape instead of ProblemDetails

**Verify:** `curl localhost:8080/health` returns `200`. After `docker compose stop llm-server` it returns `503`, and after `start` it returns `200` again.

### 8j. Reboot, and the "it's down" page

- `sudo reboot`
- Write a short runbook in [vps-deployment.md](vps-deployment.md) for when it's down: which of `docker compose ps`, `/health`, and the logs for each service to check, in what order, and how to restart one service

**Verify:** after the reboot, with no manual steps, `docker compose ps` shows all four services running, and 8e's streaming check passes again.

### Post-milestone considerations

We will not have done any load testing when this first goes out; this is fine for a pilot with a small number of users. Appropriate settings are to be determined in milestone 10.

---

## 9. Revocation, expiry, admin operations

- `POST /admin/vouchers/{id}/revoke` — status flag with reason (`Voucher.Revoke()` exists; the reason does not yet)
- Expiry checked at request time (already enforced by 6e)
- `GET /admin/sections/{id}/vouchers` — status and consumption
- Issuance alert (email or webhook)
- Friendly API documentation (README in API project - link from root README)

**Done when:** the withdrawal use case works end to end, and revocation takes effect on the next request.

## 10. Real model and load test

(Optional)

Requires live vLLM on real hardware.

- Script N concurrent agentic sessions: full tool loops with long contexts, not chat requests
- Measure time to first token, throughput, and where it degrades
- Settle model, quantization, `--max-model-len`, `--gpu-memory-utilization`
- Re-pull `/openapi.json` if the vLLM version changed, and diff it against the committed copy
- Revisit the no-cache decision and any concurrency caps against real numbers

**Done when:** load tests on current hardware have been documented.

_Proposed model_: Qwen3.5-9B

After loading the model into VRAM, the remaining space is allocated to KV cache. This is what's left to handle user context from requests and the model's responses. Because there are only 8GB VRAM, we may see some blocking if many users hit the system at the same time or when users have a large context.

A better card for this at the same price point would be an RTX 3060, w/ 12GB VRAM.

---

## 11. Updates for VS Code integration

Agent features depend on tool calling, and vLLM does not enable it by default. Need to start the server with flags such as:

vllm serve <model> \
 --enable-auto-tool-choice \
 --tool-call-parser <parser-for-your-model> \
 --api-key <shared-or-per-student-key>

Agentic capabilities may be limited by the model that we're running.

Looks like this needs a VS Code extension to be able to use the API:

- Search extensions for tag:"language-models"
- Foundry Toolkit for VS Code
  - ms-windows-ai-studio.windows-ai-studio
  - https://peterfalkingham.com/2025/10/27/using-local-ai-llm-in-vs-code-without-third-party-software/
  - May have licencing restrictions
- OAI Compatible Provider for Copilot
  - johnny-zhao.oai-compatible-copilot
  - MIT licensed
