# Pilot Roadmap

_This file holds work in progress developer documentation, not finished product documentation._

Each milestone ends in something demonstrable. Ordering favors killing unknowns early over building in layers.

This is a temporary working document. Settled detail lives in the code and tests; this keeps status, decisions whose reasoning the code can't show, and the open work.

---

## Environment split

- **GPU box** — runs vLLM. Any dev machine with an NVIDIA GPU. The only place real responses come from.
- **Non-GPU machine** — AVX2 only, so it cannot run vLLM's CPU backend. No local inference.

**Requires the GPU box:** Milestones 1, 2, 3, 6g, 8A, 10. **Requires the VPS:** 8B, 8C. Everything else can proceed without a GPU.

The dev GPU box is lower spec than production. These milestones prove shape and plumbing, not performance; performance is Milestone 10.

---

## Completed: Milestones 1–5, 6a–6f

| Milestone                                                                                                  | Where to look                                                                                                                                                                                                                                                                                                                 |
| ---------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1. vLLM on a GPU host, capture set                                                                         | [api_documentation/](../docs/api_documentation/)                                                                                                                                                                                                                                                                                   |
| 2. Aider against vLLM directly                                                                             | Baseline for Milestone 3                                                                                                                                                                                                                                                                                                      |
| 3. Pass-through API                                                                                        | [api_documentation/README.md](../docs/api_documentation/README.md#streaming-baseline--milestone-3), [LlmVoucherGateway.Api.http](../src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.http)                                                                                                                                          |
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
- **Never logged:** voucher keys, admin keys, attempted bearer tokens, voucher ids. **The voucher prefix is logged** on every request line, as `as <prefix>` (`as admin` for an admin key, `as unknown` when no credential resolved) — see [LoggingRegistration.cs](../src/LlmVoucherGateway.Api/Observability/LoggingRegistration.cs). Source address alone was the original handle, and it does not survive the Streamlit UI: that traffic reaches the gateway from the web container, so every browser user shares one address, while only a harness through Caddy carries its own. The prefix is the handle that works on both paths and the thing revocation acts on. It is the first eight characters of the key, so it discloses part of a secret; accepted because the rest leaves the key unguessable and the log sink writes to an internal file.

---

## 6. Voucher validation

Fill in the voucher branch of the middleware from Milestone 5a. Requests without a valid voucher are rejected.

- Hash + lookup, straight to the database — **no cache.** Under 20 users on requests that occupy a GPU for seconds, a lookup is free, and caching would buy a revocation delay for no measurable gain. Revisit only if Milestone 10 shows a reason
- Reject: unknown, revoked, expired, allowance exhausted
- **Errors must be OpenAI-shaped**, matching the error payloads captured in Milestone 1. A harness receiving your custom error format will render garbage or crash. Every failure has to leave through a wire format the client already understands. Errors from vLLM relay verbatim and are already correctly shaped; this is about the rejections we generate
- Distinguish 401 (bad credential) from 402 (valid but out of allowance), so a student can tell "wrong key" from "used it up"

**Depends on:** captured error payloads from Milestone 1. Without them the error shape is guesswork. The shape they fix is `{"error":{"message","type","param","code"}}` — see [errors/](../docs/api_documentation/errors/).

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
- Streaming: vLLM forces usage server-side with `--enable-force-include-usage` (set in `docker-compose.llm.yml`). Every chunk carries a running count, so the gateway relays bytes untouched and parses only the last `data:` line after the stream ends. See [streaming-response-forced-usage.md](../docs/api_documentation/chat-completions/chat-completions.md#why-the-flag-and-what-it-costs)
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

_Complete_ — every per-machine value lives in `.env`, and [.env.example](../.env.example) is the list. See [vllm-configuration.md](../docs/configuration/vllm-configuration.md).

---

## 8. Deployment and operational readiness

See [vps-deployment.md](vps-deployment.md).

| Machine        | GPU            | RAM  |
| -------------- | -------------- | ---- |
| Dev box (WSL2) | RTX 3060, 12GB | 32GB |
| Pilot server   | RTX 5060, 8GB  | 64GB |

Three parts, split by what each check needs rather than by service. **8A** needs only the dev GPU box. **8B** needs the VPS and a domain. **8C** is operations, once it works.

Sequence: 8A first. Then Milestone 9 while the VPS is procured — it does not depend on deployment, and the rehearsal has caught the failures it was meant to catch early. 8B as soon as the VPS exists, then 8C. Milestone 11 is last.

### Already in code

Written and tested locally, each with its deployment check in 8A and 8B.

- One log line per request with client address, and a correlation id on every line from the same request, also returned as `X-Request-Id` — [LoggingRegistration.cs](../src/LlmVoucherGateway.Api/Observability/LoggingRegistration.cs), [CorrelationIdMiddleware.cs](../src/LlmVoucherGateway.Api/Middleware/CorrelationIdMiddleware.cs)
- JSON file sink to `/data/logs`, daily, 30 kept — [appsettings.Production.json](../src/LlmVoucherGateway.Api/appsettings.Production.json). Written, never run under Docker
- `/health`: `200` when vLLM is reachable, `503` when not; unauthenticated, and hidden from the internet by the [Caddyfile](../Caddyfile) — [InferenceHealthCheck.cs](../src/LlmVoucherGateway.Api/Inference/InferenceHealthCheck.cs)
- Unhandled exceptions on `/v1` leave in the OpenAI envelope, and vLLM unreachable is `502 server_error` — [ExceptionHandler.cs](../src/LlmVoucherGateway.Api/ExceptionHandlers/ExceptionHandler.cs)
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the gateway; the Caddyfile passes `caddy validate` with example hostnames; sqlite-web under `profiles: [tools]`, never run

### 8A. Local rehearsal — dev GPU box, no domain, no second machine

Proves the production compose contract before a VPS exists. The gateway and web images have never been built; that is the step most likely to fail, and a Dockerfile is a ten-minute fix here and a miserable one over SSH.

Every command below is spelled out in full, including the `-f docker-compose.prod.yml` that makes it the production compose file. Not `make deploy`, which runs `git pull` first. One service at a time, inside out, so each failure has one suspect.

What the box cannot stand in for, being WSL2: firewall, boot, systemd, and reachability from another machine. Every "not reachable from outside" check reduces to "bound to `127.0.0.1`", which `docker compose -f docker-compose.prod.yml ps` shows. 

1. _COMPLETE_ **Prepare.** `make down` — the GPU holds one vLLM. In `.env`: `LLM_HOSTNAME=llm.localhost`, `CHAT_HOSTNAME=chat.localhost`, any `ACME_EMAIL` (Caddy issues locally-trusted certificates for `*.localhost` and never contacts Let's Encrypt), `GATEWAY_DATA_DIR` at a local directory you `chown 1654`, `GATEWAY_URL=http://localhost:8080`. Add `127.0.0.1 llm.localhost chat.localhost` to `/etc/hosts`; they do not resolve in WSL otherwise. `docker compose -f docker-compose.prod.yml config --quiet` prints nothing
2. _COMPLETE_ **Build.** `docker compose -f docker-compose.prod.yml build`. Both Dockerfiles build
3. _COMPLETE_ **vLLM.** `docker compose -f docker-compose.prod.yml up -d llm-server`. A throwaway container on the compose network lists the model: `docker run --rm --network llm-voucher-gateway-prod_default curlimages/curl:8.11.1 -sS http://llm-server:8000/v1/models`. From the host, `curl localhost:8000/v1/models` is refused. The network is `<compose project name>_default` — no network is declared, so this is the default one. `llm-server` is a Compose service name that Docker's internal DNS resolves only on that network; it is **not** an `/etc/hosts` entry, and adding one would make the check resolve and then fail to connect, since `llm-server` publishes no port. The `/etc/hosts` line from step 1 is for the Caddy hostnames in steps 5 and 6
4. _COMPLETE_ **Gateway.** `docker compose -f docker-compose.prod.yml up -d gateway`. `make api-mint-voucher` returns a voucher and `make api-check-completion-streaming key=...` streams. `vouchers.db` appears in `GATEWAY_DATA_DIR` owned by 1654. After `docker compose -f docker-compose.prod.yml restart gateway` the same voucher still works. `docker compose -f docker-compose.prod.yml ps` shows `127.0.0.1:8080`
5. _COMPLETE_ **Caddy and web.** `docker compose -f docker-compose.prod.yml up -d`. `curl -k https://llm.localhost/v1/models` → OpenAI-shaped `401`; `curl -k https://llm.localhost/health` → `404`; `curl localhost:8080/health` → `200`. `https://chat.localhost` in a Windows browser, past the certificate warning: a voucher lists the model and a chat streams
6. _COMPLETE_ **Streaming through Caddy.** Milestone 3's per-chunk timing with `curl -k -N` against `https://llm.localhost/v1`. Chunks arrive spread over the generation, not all at the end. Caddy's behaviour does not depend on who signed the certificate, so this is the proof, not a rehearsal of it
7. _COMPLETE_ **Forwarded headers.** The request lines from step 5 carry the address Caddy saw the connection from — the Docker bridge gateway, ending in `.1`, for requests from this box — rather than the caddy container's own address (`docker inspect` shows which is which). Weak proof; the real one is your public IP in 8B
8. _COMPLETE_ **Logs.** A file appears in `$GATEWAY_DATA_DIR/logs`, keeps its lines after `docker compose -f docker-compose.prod.yml up -d --build` recreates the gateway, and `jq` reads it. `docker compose -f docker-compose.prod.yml logs caddy` and `docker compose -f docker-compose.prod.yml logs llm-server` contain no voucher key, `Authorization` header or prompt text
9. _COMPLETE_ **Health.** `docker compose -f docker-compose.prod.yml stop llm-server` → `localhost:8080/health` is `503` and `https://llm.localhost/v1/models` is `502` in the OpenAI envelope; `docker compose -f docker-compose.prod.yml start llm-server` → `200` again
10. _COMPLETE_ **sqlite-web.** `docker compose -f docker-compose.prod.yml --profile tools up -d sqlite-web`; `http://localhost:8081` shows the tables; `docker compose -f docker-compose.prod.yml --profile tools stop sqlite-web`. Afterwards the gateway can still write — the `-wal` and `-shm` files must not have become root's
11. _COMPLETE_ **Persistence.** `docker compose -f docker-compose.prod.yml down`, then `docker compose -f docker-compose.prod.yml up -d`: the model is not downloaded again and the voucher still works

**Done when:** all eleven pass, and whatever broke is fixed and committed.

### 8B. VPS bring-up — needs the server and a domain

Only what 8A could not prove.

1. **Server.** Docker and the NVIDIA Container Toolkit; `sudo systemctl enable docker`. `ufw`: allow SSH **before** enabling it, then 80 and 443. `docker run --rm --gpus all ubuntu nvidia-smi` shows the GPU
2. **Checkout.** Clone. `sudo mkdir -p /srv/llm-gateway/data && sudo chown 1654 /srv/llm-gateway/data`. `.env` from `.env.example` with the real hostnames, `ACME_EMAIL`, `ADMIN_KEY` (`openssl rand -base64 32`), `GATEWAY_URL=http://localhost:8080`, and the vLLM values for the 8GB card — this is the first real model load, and `VLLM_MAX_MODEL_LEN` decides whether the KV cache fits (Milestone 10's question, answered here first). `chmod 600 .env`. `docker compose run --rm caddy caddy validate --config /etc/caddy/Caddyfile`
3. **DNS.** A records for both hostnames, **grey cloud (DNS only)** in Cloudflare. The proxy is not in the path, so there is no streaming cut-off to test; just confirm the cloud is grey
4. **Up.** `make deploy && make run-prod`, then `make logs-prod service=llm-server` until it is listening. The first start downloads the model
5. **From your laptop.** `curl https://llm.<domain>/v1/models` → OpenAI-shaped `401`, no certificate warning; `/health` → `404`. `https://chat.<domain>` streams a chat. `make logs-prod service=gateway` shows your public IP in the request line, not a `172.x` address
6. **Aider.** Pointed at `https://llm.<domain>/v1`, completes a small task. Each task sends two requests, and both meter
7. **Reboot.** `sudo reboot`. With no manual steps, `make ps-prod` shows all four services and step 5 passes again

**Done when:** steps 5–7 pass.

### 8C. Operations — once it works

1. **Backups.** Daily host cron running `sqlite3 ... ".backup ..."` per [vps-deployment.md](vps-deployment.md#database). Run it by hand once, then `sqlite3 <backup> "select count(*) from Vouchers"` returns the live count. A backup is not proven until it has been read back
2. **Remote sqlite-web.** `ssh -L 8081:localhost:8081 <server>`, then `http://localhost:8081` on the laptop
3. **Runbook.** In [vps-deployment.md](vps-deployment.md), for "it's down": which of `make ps-prod`, `/health` and each service's logs to check, in what order; how to restart one service; and `df` plus `ls -l /srv/llm-gateway/data` for the database, since `/health` deliberately does not check SQLite — it is a file, and its failures are disk and ownership

No load testing before the pilot starts; a handful of users does not need it. Settings are Milestone 10's.

---

## 9. Revocation, expiry, admin operations

### 9.1 Add remaining voucher management endpoints

- _COMPLETE_ — `GET /admin/sections/{section}/vouchers` — status and consumption, never the key. See [SectionVouchersEndpoint.cs](../src/LlmVoucherGateway.Api/Endpoints/SectionVouchersEndpoint.cs)
- _COMPLETE_ — `GET /admin/vouchers/{prefix}` — one voucher's status and consumption by the prefix the request line logs. `404` when unknown, `409` if two vouchers ever share a prefix (nothing enforces uniqueness). See [VoucherLookupEndpoint.cs](../src/LlmVoucherGateway.Api/Endpoints/VoucherLookupEndpoint.cs)
- `POST /admin/vouchers/{id}/revoke` — status flag with reason (`Voucher.Revoke()` exists; the reason does not yet)

**Done when:** the withdrawal use case works end to end, and revocation takes effect on the next request.

### 9.2 API documentation

- Friendly API documentation (README in API project - link from root README)


## 10. Real model and load test

(Optional)

Requires live vLLM on real hardware.

- Script N concurrent agentic sessions: full tool loops with long contexts, not chat requests
- Measure time to first token, throughput, and where it degrades
- Settle model, quantization, `VLLM_MAX_MODEL_LEN`, `VLLM_GPU_MEMORY_UTILIZATION`. Both are already `.env` variables; only the values for the 8GB card are open, since `--max-model-len` decides whether the KV cache fits
- Re-pull `/openapi.json` if the vLLM version changed, and diff it against the committed copy
- Revisit the no-cache decision and any concurrency caps against real numbers

**Done when:** load tests on current hardware have been documented.

_Proposed model_: Qwen3.5-9B

After loading the model into VRAM, the remaining space is allocated to KV cache. This is what's left to handle user context from requests and the model's responses. Because there are only 8GB VRAM, we may see some blocking if many users hit the system at the same time or when users have a large context.

A better card for this at the same price point would be an RTX 3060, w/ 12GB VRAM.

### 10.1 Note on HuggingFace usage

We get this warning from the vLLM image on boot - does it matter?

We can't really tie this to any particular HuggingFace account, so we're probably doing the best we can with this limitation.

```
(APIServer pid=1) Warning: You are sending unauthenticated requests to the HF Hub. Please set a HF_TOKEN to enable higher rate limits and faster downloads.
```

---

## 11. Updates for VS Code integration

Agent features depend on tool calling, and vLLM does not enable it by default. Need to start the server with flags such as:

vllm serve <model> \
 --enable-auto-tool-choice \
 --tool-call-parser <parser-for-your-model>

Update note in API readme about tool usage when this changes.

Agentic capabilities may be limited by the model that we're running.

Config notes:

- The parser name is a function of the model, so the two tool-calling flags move with `VLLM_MODEL`. They are deliberately not in the compose files yet; a harness sending `tools` is a return trip to Milestone 1 for a re-capture first
- **Open contradiction:** `--api-key` above conflicts with [vps-deployment.md](vps-deployment.md), which deliberately decides against an upstream key. One of the two should change

Looks like this needs a VS Code extension to be able to use the API:

- Search extensions for tag:"language-models"
- Foundry Toolkit for VS Code
  - ms-windows-ai-studio.windows-ai-studio
  - https://peterfalkingham.com/2025/10/27/using-local-ai-llm-in-vs-code-without-third-party-software/
  - May have licencing restrictions
- OAI Compatible Provider for Copilot
  - johnny-zhao.oai-compatible-copilot
  - MIT licensed

---

## 12. Scaling past the pilot

The pilot is under 20 users; the eventual target is a few thousand concurrent users. Several decisions above were made for the pilot and do not survive the target. None blocks the pilot. Each is listed so it is taken deliberately rather than discovered.

### 12.0

Issuance alert (email or webhook). Brings a new config surface (SMTP settings or a webhook URL) into `.env`; don't add it before the feature

### 12.1 Voucher prefix collisions

The prefix is the first eight hex characters of the key: 32 bits, and nothing enforces uniqueness. It is already the operator's handle — every request line logs it, `GET /admin/vouchers/{prefix}` looks up by it, and revoke-by-prefix is the natural next use. Vouchers are never deleted, so the table grows every term.

| Vouchers in the table | Chance any two share a prefix |
| --------------------- | ----------------------------- |
| 100                   | ~1 in 1,000,000               |
| 2,000                 | ~1 in 2,000                   |
| 5,000                 | ~1 in 350                     |
| 20,000                | ~1 in 20                      |

Today a collision on lookup is a `409`, which is correct but means the prefix stops working as a handle for those two vouchers. Any endpoint that *acts* by prefix (revoke) must refuse on a collision rather than pick one.

**Fix:** lengthen the prefix to twelve characters (48 bits), which makes collisions negligible at any plausible size. Touches `VoucherKeyGenerator`, the `Key_Prefix` column length, a migration, and the `as <prefix>` log line. **Do it before vouchers are issued at scale:** the prefix is persisted at issuance, so lengthening later leaves old vouchers at eight characters and new at twelve, and every lookup and log reader has to cope with both.

### 12.2 Concurrency

The system was not designed with concurrency in mind. Known consequences:

- **Lost charges.** Milestone 7 accepts that overlapping requests on one voucher overwrite each other's `Consumed` (load on entry, save an absolute total). Tolerable for one student with two windows; with thousands of users and agentic harnesses firing parallel requests it under-meters systematically. Fix: a concurrency token on `Consumed` with reload-and-retry, or an atomic `UPDATE ... SET Consumed = Consumed + @tokens`.
- **Single writer.** SQLite serializes writes and the deployment is one gateway instance with migrations applied at startup. Thousands of concurrent streams, or a second instance for availability, is the Postgres trigger named in the Milestone 4 decisions. The upgrade path is a connection string and regenerated migrations.
- **Lookup per request, no cache.** Chosen so revocation takes effect immediately. One indexed read per request is still cheap at this scale on Postgres; revisit only with Milestone 10's numbers.
- **GPU contention.** Independent of the gateway: a few thousand concurrent users on one 8GB card is a vLLM capacity question (KV cache, queueing, time to first token), answered by Milestone 10's load test on the real hardware.

**Done when:** the prefix is twelve characters, consumption recording is atomic under concurrent requests on one voucher (with a test that proves it), and the Postgres move has been made or explicitly deferred with a load number behind it.
