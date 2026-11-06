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

| Milestone                                                                                                  | Where to look                                                                                                                                                                                                                                                                                                                 |
| ---------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1. vLLM on a GPU host, capture set                                                                         | [api_documentation/](../api_documentation/)                                                                                                                                                                                                                                                                                   |
| 2. Aider against vLLM directly                                                                             | Baseline for Milestone 3                                                                                                                                                                                                                                                                                                      |
| 3. Pass-through API                                                                                        | [api_documentation/README.md](../api_documentation/README.md#streaming-baseline--milestone-3), [LlmVoucherGateway.Api.http](../src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.http)                                                                                                                                          |
| 4. Persistence (SQLite, EF Core)                                                                           | [Persistence/](../src/LlmVoucherGateway.Api/Persistence/)                                                                                                                                                                                                                                                                     |
| 5. Voucher issuance, admin auth, issuance logging                                                          | [Endpoints/](../src/LlmVoucherGateway.Api/Endpoints/), [Credentials/](../src/LlmVoucherGateway.Api/Credentials/)                                                                                                                                                                                                              |
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

**Does not cover:** that streaming stays unbuffered. The fake's body is a `MemoryStream`, so timing proves nothing here; that remains Milestone 3's proof against a real server, and Milestone 9's through the proxy.

**Done when:** an integration test posts to `/v1/chat/completions` and gets the captured response back, with no GPU involved.

### 6c. Domain: when is a voucher spendable?

_COMPLETE_ — see [Voucher.cs](../src/LlmVoucherGateway.Domain/Vouchers/Voucher.cs) and [VoucherSpendabilityTests.cs](../tests/LlmVoucherGateway.Domain.UnitTests/Vouchers/VoucherSpendabilityTests.cs)

`Status`, `Lifetime` and `Consumed` are currently just data — nothing in the aggregate can answer whether a voucher may be spent.

An end-to-end checkpoint for what the integration tests can't see. The proof of concept rests on it.

The client in use is a [Streamlit web UI](../src/LlmVoucherGateway.Web/app.py) rather than Aider at first. It calls the gateway from its Python server process, so CORS is not a factor. It reads `GATEWAY_BASE_URL`, which defaults to the `make run-api` address. `GET /v1/models` is now passed through behind the voucher filter, so a bad voucher shows up in the sidebar before a prompt is typed.

Mint vouchers with `POST /admin/vouchers` (see [LlmVoucherGateway.Api.http](../src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.http)). There is no revoke endpoint until Milestone 8 and no consumption recording until Milestone 7, so set those states in the database directly: `src/LlmVoucherGateway.Api/.data/vouchers.db`, table `Vouchers`, columns `Status` (`'Active'` / `'Revoked'`), `Consumed`, `Allowance`, `Lifetime_ExpiresAt`, `Key_Prefix`.

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

See planning doc for this.

### 7.6 Sanity check how it's going to work on a VPS

- Determine and document a way to access and backup the database.
  - Database should be bind mounted so that it exists outside of the container filesystem.
  - sqlite-web with ssh to connect in case we need to do ad-hoc queries
- At what point do we start building container images?
  - Dev workflow only needs container for vLLM

---

## 8. Operational readiness

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

## 9. Revocation, expiry, admin operations

- `POST /admin/vouchers/{id}/revoke` — status flag with reason (`Voucher.Revoke()` exists; the reason does not yet)
- Expiry checked at request time (already enforced by 6e)
- `GET /admin/sections/{id}/vouchers` — status and consumption
- Issuance alert (email or webhook)

**Done when:** the withdrawal use case works end to end, and revocation takes effect on the next request.

## 10. Real model and load test

Requires live vLLM on real hardware.

- Swap to a real coder model (a colleague has a GX10, which is arm64, so verify the container images)
- Script N concurrent agentic sessions: full tool loops with long contexts, not chat requests
- Measure time to first token, throughput, and where it degrades
- Settle model, quantization, `--max-model-len`, `--gpu-memory-utilization`
- Re-pull `/openapi.json` if the vLLM version changed, and diff it against the committed copy
- Revisit the no-cache decision and any concurrency caps against real numbers

**Done when:** you have a defensible concurrency number for the hardware proposal.

Small scale hardware confirmed:

```
GeForce RTX 5060 8GB vRAM
64GB RAM on host machine
```

_Proposed model_: Qwen3.5-9B

After loading the model into VRAM, the remaining space is allocated to KV cache. This is what's left to handle user context from requests and the model's responses. Because there are only 8GB VRAM, we may see some blocking if many users hit the system at the same time or when users have a large context.

A better card for this at the same price point would be an RTX 3060, w/ 12GB VRAM.

---

## 11. Student rollout

- Small-scale test with one class, where it doesn't matter if it fails
- Setup docs per OS, and a `.env` template
- The `openai/` model prefix gotcha, and the `OPENAI_API_KEY` collision

**Done when:** a student with no setup is running in under 15 minutes without your help.

---

## 12. Updates for VS Code integration

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
