# Pilot Roadmap

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

| Milestone | Where to look |
| --- | --- |
| 1. vLLM on a GPU host, capture set | [api_documentation/](../api_documentation/) |
| 2. Aider against vLLM directly | Baseline for Milestone 3 |
| 3. Pass-through API | [api_documentation/README.md](../api_documentation/README.md#streaming-baseline--milestone-3), [LlmVoucherGateway.Api.http](../src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.http) |
| 4. Persistence (SQLite, EF Core) | [Persistence/](../src/LlmVoucherGateway.Api/Persistence/) |
| 5. Voucher issuance, admin auth, issuance logging | [Endpoints/](../src/LlmVoucherGateway.Api/Endpoints/), [Credentials/](../src/LlmVoucherGateway.Api/Credentials/) |
| 6a–6f. Error contract, fake inference client, spendability, voucher lookup, enforcement, rejection logging | [Errors/](../src/LlmVoucherGateway.Api/Errors/), [CredentialMiddleware.cs](../src/LlmVoucherGateway.Api/Middleware/CredentialMiddleware.cs), [VoucherRequiredEndpointFilter.cs](../src/LlmVoucherGateway.Api/Credentials/VoucherRequiredEndpointFilter.cs), [Voucher.cs](../src/LlmVoucherGateway.Domain/Vouchers/Voucher.cs) |

Run with `make up-llm && make run-api`.

### Findings worth carrying forward

- **Aider requests only streamed `POST /v1/chat/completions`** — no `GET /v1/models`, no tool calling. Each task issues two requests: the edit, then Aider's automatic lint-fix follow-up. A different client may request other paths.
- **Tool calling is not enabled.** No tool-call shape was captured, and the compose command lacks `--enable-auto-tool-choice --tool-call-parser hermes`. If a harness sends `tools`, that is a return trip to Milestone 1.
- **Streaming passthrough is proven unbuffered only against vLLM direct** (Milestone 3, per-chunk timing). The fake inference client uses a `MemoryStream`, so the test suite cannot catch buffering. It must be re-proven through Caddy in Milestone 9.
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

## 6g. Harness verification — IN PROGRESS (manual tests on the GPU box)

An end-to-end checkpoint for what the integration tests can't see. The proof of concept rests on it.

The client in use is a [Streamlit web UI](../src/LlmVoucherGateway.Web/app.py) rather than Aider at first. It calls the gateway from its Python server process, so CORS is not a factor. It reads `GATEWAY_BASE_URL`, which defaults to the `make run-api` address. `GET /v1/models` is now passed through behind the voucher filter, so a bad voucher shows up in the sidebar before a prompt is typed.

Mint vouchers with `POST /admin/vouchers` (see [LlmVoucherGateway.Api.http](../src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.http)). There is no revoke endpoint until Milestone 8 and no consumption recording until Milestone 7, so set those states in the database directly: `src/LlmVoucherGateway.Api/.data/vouchers.db`, table `Vouchers`, columns `Status` (`'Active'` / `'Revoked'`), `Consumed`, `Allowance`, `Lifetime_ExpiresAt`, `Key_Prefix`.

- **6g.1** _COMPLETE_ — Web client base URL from config
- **6g.2** _COMPLETE_ — `GET /v1/models` pass-through
- **6g.3** _COMPLETE_ — Streamlit against the gateway with a valid voucher
- **6g.4** Streamlit against the gateway with rejected vouchers:
  - _COMPLETE_ — unknown
  - TODO — expired (mint with an `expiresAt` a minute or two out, then wait)
  - TODO — revoked
  - TODO — exhausted. Watch this one: `403 insufficient_quota` is less common than `401`, and a client may render it poorly. If it does, revisit the status map rather than hiding the distinction
- **6g.5** TODO — Preliminary Aider run: redo the Milestone 2 CSV task through the gateway, with the voucher in `OPENAI_API_KEY`. Repeat with a revoked voucher and confirm the failure is legible in the harness, not a crash or silence

Other open items:

- TODO: Capture the real `GET /v1/models` response into `api_documentation/`. The fake currently replays a labelled stand-in body.
- TODO: Do an eyes-on check of voucher rows in `vouchers.db`.
- TODO: The Streamlit frontend was vibe coded and needs review and cleanup.

**Done when (whole milestone):** a valid voucher works, and an invalid one fails cleanly _in the client_, not just in curl.

---

## 7. Consumption recording

- Read `usage.total_tokens` from the response and add it to the voucher's consumption. `total_tokens` already includes the prompt; adding `prompt_tokens` again would double-bill it
- Streaming: vLLM forces usage server-side with `--enable-force-include-usage` (set in `docker-compose.llm.yml`). Every chunk carries a running count, so the gateway relays bytes untouched and parses only the last `data:` line after the stream ends. See [streaming-response-forced-usage.md](../api_documentation/chat-completions/streaming-response-forced-usage.md)
- The flag must be set wherever the pilot's vLLM runs. Without it, streaming requests meter as nothing
- Soft limits: check on entry, record after, overshoot accepted
- A recording failure must not fail the request
- Use `Voucher.RecordConsumption` against the aggregate already on the resolved credential

**Done when:** consumption tracks across a real multi-turn agentic session. Add a test that runs a known-token request and asserts the recorded amount, since this is the first milestone whose correctness isn't obvious from using it.

**Fallback:** if metering streamed output proves fragile, meter on input tokens or request count. Don't get stuck here.

---

## 8. Revocation, expiry, admin operations

- `POST /admin/vouchers/{id}/revoke` — status flag with reason (`Voucher.Revoke()` exists; the reason does not yet)
- Expiry checked at request time (already enforced by 6e)
- `GET /admin/sections/{id}/vouchers` — status and consumption
- Issuance alert (email or webhook)

**Done when:** the withdrawal use case works end to end, and revocation takes effect on the next request.

---

## 9. Operational readiness

- Structured logging with a correlation ID per request
- `app.UseSerilogRequestLogging()` — one line per HTTP request, the only record of a request rejected before an endpoint ran. The client address needs a small enricher
- Rolling file sink with 14–30 days retention, on the same mounted volume as the SQLite database. Console logs die with the container
- `UseForwardedHeaders` once Caddy is in front, or every logged address becomes the proxy's
- **Never logged:** keys, `Authorization` headers, request or response bodies. Logging prompt bodies would end the project's claim to hold no student content
- `/health` — checks vLLM reachability, endpoint from configuration
- Configuration and secrets outside source control
- Global exception handling that returns OpenAI-shaped errors
- Caddy as reverse proxy for certificates
- Restart policy, and a documented "it's down, what now" procedure

**Done when:** you can restart the box and it comes back without intervention.

**Risk, accepted:** Caddy is the most likely thing to break streaming. Re-run Milestone 3's timing comparison through the proxy. If it buffers and the fix isn't quick, fall back to Milestone 7's simpler metering.

---

## 10. Real model and load test

Requires live vLLM on real hardware.

- Swap to a real coder model (a colleague has a GX10, which is arm64, so verify the container images)
- Script N concurrent agentic sessions: full tool loops with long contexts, not chat requests
- Measure time to first token, throughput, and where it degrades
- Settle model, quantization, `--max-model-len`, `--gpu-memory-utilization`
- Re-pull `/openapi.json` if the vLLM version changed, and diff it against the committed copy
- Revisit the no-cache decision and any concurrency caps against real numbers

**Done when:** you have a defensible concurrency number for the hardware proposal.

---

## 11. Student rollout

- Small-scale test with one class, where it doesn't matter if it fails
- Setup docs per OS, and a `.env` template
- The `openai/` model prefix gotcha, and the `OPENAI_API_KEY` collision

**Done when:** a student with no setup is running in under 15 minutes without your help.
