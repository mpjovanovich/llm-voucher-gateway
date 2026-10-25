# Pilot Roadmap

Each milestone ends in something demonstrable. Ordering favors killing unknowns early over building in layers.

---

## Environment split

Development may happens across two machine hardware profiles, and the roadmap depends on knowing which is which.

- **GPU box** Runs vLLM. Can be a dev machine as long as it has an NVIDIA GPU. The only place real responses come from.
- **Non GPU machine** Noted here because some dev machines do not have an NVIDIA GPU; AVX2 only — no AVX512 — so it cannot run vLLM's CPU backend. We can't run local inference on machines of this profile.

**Requires live running vLLM:** Milestones 1, 2, 3, 6g, 10
**Can proceed without a GPU:** Milestones 4, 5, 6 (except 6g), 7, 8, 9

The dev GPU box is lower spec'd than production hardware. That is accepted: these milestones prove shape and plumbing, not performance. Performance is Milestone 10's question.

---

## 1. vLLM running on a host machine with an NVIDIA GPU

_COMPLETE_

Docker Compose, tiny model (Qwen2.5-0.5B-Instruct), GPU passthrough. Requires a host with an NVIDIA GPU.

- `curl /v1/models` returns the served model
- `curl /v1/chat/completions` returns valid JSON
- Same request with `"stream": true` returns SSE chunks ending in `[DONE]`

### Capture set

_COMPLETE_ — see [api_documentation/](../api_documentation/)

Everything later milestones will be built against. Capture generously — anything missed here is a return trip.

- Non-streaming response
- Streaming response, **including the final chunk**: where `finish_reason` lands, and whether `usage` appears there
- Streaming response with `stream_options: {"include_usage": true}` — feeds Milestone 7
- Error payloads: context-length overflow, malformed request, unknown model name — feed Milestone 6
- `/openapi.json` from the running instance, committed to the repo
- The pinned vLLM image tag, recorded

**Done when:** the full capture set is committed.

**Note:** the pinned image tag is the drift control. There is no CI schema check — Milestone 10 re-pulls and diffs `openapi.json` if the vLLM version changes, and that is the only point at which drift is possible.

**Gap to fill if Milestone 2 needs it:** no tool-call request or response shape was captured, and the compose command does not enable vLLM's tool-call parser (`--enable-auto-tool-choice --tool-call-parser hermes`). If the harness sends `tools`, this is the return trip.

---

## 2. Harness against vLLM directly

_COMPLETE_ — run 2026-09-12 as the baseline for Milestone 3's harness check

**Observed:** Aider 0.86.2 (`--model openai/Qwen/Qwen2.5-0.5B-Instruct`, whole edit format) requests only `POST /v1/chat/completions`, streamed — no `GET /v1/models`, no other paths. A task issues two requests: the edit, then Aider's automatic lint-fix follow-up. No tool calling, so vLLM's tool-call parser was not needed. As predicted, the model mangled the CSV (rewrote it as a Markdown table, misgraded a row). This covers Aider only; a chat client chosen later may request other paths.

Prove the harness plumbing works before we start coding against the API. Requires live vLLM.

- Aider or OpenCode configured at the vLLM endpoint (`localhost:8000`)
- Record which paths the harness actually requests — `GET /v1/models` is the likely extra one, and Milestone 3 has to serve whatever shows up here
- Attempt the small file-edit task (the CSV demo)

**Done when:** the harness connects, issues requests, and renders a streamed response without protocol errors.

**Note:** edit quality is not the bar. Qwen2.5-0.5B will likely fail the file-edit task on capability alone, and enabling tool calling may be needed before the harness will even attempt it. Neither is a blocker — this milestone is about the request/response loop. Model capability is Milestone 10's question.

---

## 3. Pass-through API

_COMPLETE_ — streaming baseline recorded in [api_documentation/README.md](../api_documentation/README.md#streaming-baseline--milestone-3)

**Result:** Aider through the gateway produced byte-identical output to Aider against vLLM directly, including the unknown-model error (vLLM's 404 relayed verbatim). Per-chunk timing through the gateway matched vLLM direct (~4 ms median gap, a few ms added to first chunk). Run with `make up-llm && make run-api`; sample requests in [LlmVoucherGateway.Api.http](../src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.http).

_Detailed build plan: [tmp-milestone-3-passthrough-api.md](tmp-milestone-3-passthrough-api.md)_

.NET Core API, minimal APIs, no auth, no vouchers. Accepts `/v1/chat/completions`, forwards to vLLM, returns the response.

- `IInferenceClient` + `VllmInferenceClient` behind it, endpoint supplied by configuration — this is the seam that lets the same code target local vLLM in development and real hardware in production
- Base URL driven by configuration from the start, validated at startup: localhost, production
- Request and response bodies forward opaquely — no DTOs. Parsing arrives in Milestone 7, and only for `usage`
- **Streaming must pass through unbuffered**
- Point the harness at your API instead of vLLM and redo the Milestone 2 task

**Done when:** the harness can't tell the difference.

**Requires live vLLM.** Streaming passthrough has to be proven against the real server, with per-chunk arrival timestamps compared against vLLM direct. Identical payloads prove nothing; only timing distinguishes a buffering proxy.

**Risk:** ASP.NET buffering the response. A harness against a buffering proxy looks hung, not broken, and it's difficult to diagnose later with auth and vouchers in the way. Prove it here while there's nothing else added on top of it.

---

## 4. Persistence

_COMPLETE_ — see [Persistence/](../src/LlmVoucherGateway.Api/Persistence/)

**SQLite and EF Core.** No new behavior of its own — it exists so issuance has somewhere to write (5b.1) and Milestone 6 has a row to look up.

**Executed out of numeric order:** 5a and 5b were built before this, because neither needed a database. 5b.1 does, so this milestone lands between 5b and 5c. The numbering is left alone rather than churning cross-references.

- `DbContext`, database file path from configuration and validated at startup like the other options
- Lives in `src/LlmVoucherGateway.Api/Persistence/`. One table does not justify a third project, and the constraint that matters — the domain stays persistence-ignorant — holds either way
- Map the existing voucher aggregate: `VoucherKey` and `VoucherLifetime` as owned types, with a unique index on the key hash, which is what Milestone 6 looks up by
- `Voucher` needs a private parameterless constructor. EF cannot bind owned types through a constructor, and its only constructor takes both value objects
- Migration workflow settled and documented: `make migration name=AddSomething`, which writes to `Persistence/Migrations`. There is no separate update command, because startup applies whatever migrations exist
- `Database.Migrate()` at startup, so the container is self-sufficient. Accepted tradeoff: schema changes apply on boot, which is right for one instance and wrong for two. Revisit only if the deployment stops being a single container

**Why SQLite over Postgres:** the workload is one table, one indexed lookup per request, and one decrement after, for under 20 users whose requests occupy a GPU for seconds — the database is never the constraint. What SQLite buys is deployment: one container instead of two, no second service to sequence at startup, no database credentials to hold, and a materially shorter "it's down, what now" procedure in Milestone 9. Postgres remains the upgrade path and stays cheap, because with one aggregate and no provider-specific SQL it is a connection string and regenerated migrations. Take it if the deployment ever grows past a single API instance, or if a second tool needs to write concurrently.

**Backups cannot be `cp`.** In WAL mode committed data is spread across the main file and the WAL, so copying a live database can capture a torn state. The periodic job runs `sqlite3 vouchers.db ".backup /mnt/backup/vouchers.db"` or `VACUUM INTO` — same cron shape, consistent snapshot, no need to stop the service.

**Single writer, accepted.** SQLite serializes writes, and WAL mode keeps readers unaffected while a write is in flight. Nothing to configure: EF Core's SQLite provider issues `PRAGMA journal_mode = 'wal'` itself, and Microsoft.Data.Sqlite retries a busy database for the duration of the command timeout, 30 seconds by default — both confirmed against the database the first migration created. Our writes are rare and short anyway, one per issuance call and one per completed request. This is the limit that would eventually force Postgres, not read volume.

**Done when:** a migration creates the voucher table and a round-trip test saves and reloads a voucher with its value objects intact.

---

## 5. Voucher issuance

_COMPLETE_ — see [Vouchers/](../src/LlmVoucherGateway.Domain/Vouchers/)

Admin endpoint over `Voucher.Issue`. It mints a batch and the response body is that batch. No file, and no enforcement on `/v1/chat/completions`. Lookup is Milestone 6 and is not a prerequisite. Persistence (Milestone 4) is not needed for 5a or 5b, but 5b.1 is where issuance starts writing, so Milestone 4 runs before it.

`Voucher.Issue`, `VoucherKey` and `VoucherKeyGenerator` already exist, so all four substeps below are API-side.

**Substep ordering:** credential resolution (5a), then minting (5b), then the requirement that guards it (5c), then the audit trail (5d). Minting comes before its guard because an endpoint is testable on its own — correct JSON array, `400`s on bad input — while a requirement is not testable until there is a route to attach it to. The route is open for one substep, inside the dev loop only; 5c is a prerequisite for running this anywhere a student can reach, and nothing is deployed between the two.

### 5a. Credential middleware

_COMPLETE_ — see [CredentialMiddleware.cs](../src/LlmVoucherGateway.Api/Credentials/CredentialMiddleware.cs) and [CredentialMiddlewareTests.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Credentials/CredentialMiddlewareTests.cs)

Resolution only. Written once, here; Milestone 6 fills in a branch rather than rewriting it.

- Bearer token resolves to `{ VoucherId?, IsAdmin }`, attached to the request as a feature
- Admin branch is live. Configuration is a list of valid keys, not a single key. A match sets `IsAdmin`
- Voucher branch is the same middleware with no lookup yet. It lands in Milestone 6
- An unrecognized token attaches no credential and the request continues. Rejecting it is the route's decision, which is why enforcement sits in 5c
- `/v1/chat/completions` stays open

**Done when:** a configured admin key resolves to an admin credential; a missing bearer, an unknown token, and the right secret under the wrong scheme all resolve to no credential; and the Milestone 3 harness run still passes through untouched.

### 5b. Issuance endpoint

_COMPLETE_ — see [VoucherIssuanceEndpoint.cs](../src/LlmVoucherGateway.Api/Endpoints/VoucherIssuanceEndpoint.cs)

Unprotected: the route is open and 5c closes it. Build the thing, then guard it.

`POST /admin/vouchers`

Request JSON:

- `section` — optional string, stamped on every voucher in the batch
- `count` — how many to issue; positive integer
- `allowance` — positive token budget, the same for every voucher in the batch
- `expiresAt` — instant strictly after issuance time

Response: `200 application/json`, a JSON array with one object per issued voucher. The endpoint loops `Voucher.Issue`; it does not invent a second model.

- `id`
- `key` — the bearer secret. This response is the only place it exists. The aggregate keeps the hash and an 8-character `prefix`
- `prefix`
- `section`, `allowance`, `consumed` (`0`), `status` (`Active`), `createdAt`, `expiresAt`

Whoever called it distributes that array. The API does not write a CSV or any other file.

`400` when `count` is not a positive integer or exceeds the configured cap, or when the domain rejects the call: allowance not positive, or `expiresAt` not in the future.

- `count` is capped by `VoucherIssuance:MaxPerRequest`, default `100`. An uncapped mint loop is one typo away from a runaway batch, and every key in the response is unrecoverable once that response is gone

**Done when:** `curl` with no bearer token at all gets a JSON array of 20 voucher objects, and each bad request above gets a `400` instead of a `500`.

### 5b.1 Issuance writes, verified against a real database

_COMPLETE_ — see [VoucherIssuanceEndpoint.cs](../src/LlmVoucherGateway.Api/Endpoints/VoucherIssuanceEndpoint.cs) and [VoucherIssuanceEndpointTests.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Endpoints/VoucherIssuanceEndpointTests.cs)

Required Milestone 4. Until here, a minted batch existed only in the response body.

This substep was written before Milestone 4 existed and shrank once it did: the per-test database, the choice of the SQLite provider over EF's non-relational `InMemory` one, the fresh-`DbContext` discipline, and the `InternalsVisibleTo` for `VoucherKeyGenerator.Hash` were all settled there. What was left is below.

- The endpoint adds each voucher as it mints it and calls `SaveChangesAsync` once after the loop, so either every voucher in the response exists or none does — a partial write would hand out keys the gateway cannot honour, and the response is the only copy of them. The response itself is unchanged: plaintext keys still appear once and only there
- The test posts a batch, then opens a fresh scope and asserts against the stored rows: one row per object returned, each returned key hashing to its stored `Key_Hash`, matching prefix, the plaintext never stored, and the batch policy — section, allowance, expiry — on every row. The hash assertion is the one that matters: Milestone 6 resolves a bearer token by hashing it and matching that column, so a mismatch would mean vouchers that can never be used
- Field-by-field assertions on the aggregate stay in [VoucherRoundTripTests.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Persistence/VoucherRoundTripTests.cs) rather than being repeated here. That test is kept deliberately: it is the only one that exercises persistence without HTTP, so it localizes a failure to the mapping, and Milestones 7 and 8 write to the same aggregate

**Done when:** a single issuance call is followed by a query that finds the expected rows, with hashes matching the keys from the response.

### 5c. Admin authorization

_COMPLETE_ — see [AdminEndpoints.cs](../src/LlmVoucherGateway.Api/Endpoints/AdminEndpoints.cs) and [AdminOnlyEndpointFilter.cs](../src/LlmVoucherGateway.Api/Credentials/AdminOnlyEndpointFilter.cs)

Closes the route 5b opened. The credential is already resolved by 5a's middleware; this is only the decision to reject.

- An `/admin` route group carries the requirement, so every later admin route inherits it. Milestone 8's revocation and listing routes get it for free
- `IsAdmin` passes. A missing bearer, an unknown token, and a resolved voucher credential are all `401`
- **Mechanism: an endpoint filter on the group, not ASP.NET's authentication/authorization stack.** The stack wants an authentication handler producing a `ClaimsPrincipal`, then policies evaluated against claims — apparatus for carrying one boolean, given the plan has no user entities. It would also duplicate 5a's middleware, since the handler _is_ the resolver in that design. Its one real perk, framework-generated `401` challenges, is something Milestone 6 has to override anyway because rejections must be OpenAI-shaped JSON. An endpoint filter on a `MapGroup` is the minimal-API-native mechanism and keeps the response body ours
- Rejection body stays empty here. The OpenAI-shaped payload is Milestone 6's bar, and that work should shape both the voucher rejections and this one at once

**Done when:** `POST /admin/vouchers` with a configured admin key still returns the 20-voucher array, and the same call with no bearer and with an unknown token both return `401`.

### 5d. Issuance logging

_COMPLETE_ — Serilog, see [Program.cs](../src/LlmVoucherGateway.Api/Program.cs) and [VoucherIssuanceLoggingTests.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Endpoints/VoucherIssuanceLoggingTests.cs)

The audit trail for keys that exist in exactly one response body and nowhere else.

- **Serilog replaces the default providers** (`ClearProviders` then `AddSerilog`), configured from the `Serilog` section of appsettings: console sink, `Information` default, with `Microsoft.AspNetCore` and `Microsoft.EntityFrameworkCore` at `Warning` — the second because EF logs every statement at `Information` and buries everything else
- **One line per issuance call, logged on entry rather than after the save.** The signal is that somebody asked, which matters whether or not the call succeeded, and a request rejected for a bad count leaves no row to find later. It carries source IP, section and requested count; Serilog stamps the time, and time plus address is enough to query the database for what was actually minted. Nothing identifying a voucher is logged — not ids, not prefixes
- **Rejections are logged at `Warning`** in `AdminOnlyEndpointFilter`, with method, path, address and a reason. A rejected admin call is either an operator with the wrong key or somebody probing for one, and the request never reaches an endpoint, so this is its only record
- **The `key` field is not logged, at any level**, and neither is the attempted bearer token on a rejection — the first time somebody pastes the right key into the wrong tool, logging it would put the secret in the log
- **Serilog gets `preserveStaticLogger: true`.** Otherwise the first host created in a process owns `Log.Logger` for every host after it, which inside a test run sends one host's output into another host's sinks. Nothing here uses Serilog's static API
- **`ReadFrom.Services`** lets a sink registered in the container contribute beside the configured ones. That is the seam the logging tests use, since `AddSerilog` replaces `ILoggerFactory` with Serilog's own and an `ILoggerProvider` registered through DI records nothing

**Done when:** an issuance call produces a line naming the section and requested count with no voucher data in it; a call with an unknown bearer produces a rejection line; and a search of the captured log finds neither a plaintext key from the response nor the attempted token. Covered by `VoucherIssuanceLoggingTests`, where each absence assertion is paired with a positive one — a search for an absent secret also passes when nothing was logged at all.

**Done when (whole milestone):** all four substeps, exercised in one call — `curl` with an admin bearer token gets a JSON array of 20 voucher objects, and the log line for that call has ids and prefixes but not keys.

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
- Status map: unknown credential, revoked and expired are all `401` with type `invalid_request_error`, and the message says which. Allowance exhausted is `402` with type `insufficient_quota`
- **`402` rather than `429`:** "Payment Required" says the credential is fine and the budget is not, which is exactly the distinction a student needs. `429` means rate limiting, and Milestone 10 may want it for that actual purpose — spending it here would leave nothing to say "slow down" with
- **`code` is the numeric HTTP status, not a string.** OpenAI's own service sends a string there, but the captures show vLLM sending a number, and a client that copes with the relayed vLLM errors is the client we have to satisfy
- The tests compare the generated field set against the captured payloads rather than against a shape written down in a test, so a field added or renamed here fails immediately. Status code and content type are asserted by executing the result against a bare `HttpContext`
- The admin rejections from 5c keep their empty bodies. They are reached with curl by an operator, not parsed by a harness

**Done when:** the serialized output matches the captured payloads field for field, asserted in tests.

### 6b. Fake `IInferenceClient`

_COMPLETE_ — see [FakeInferenceClient.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Inference/FakeInferenceClient.cs) and [ChatCompletionsEndpointTests.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Inference/ChatCompletionsEndpointTests.cs)

A stand-in replaying the Milestone 1 captures, registered by the test factory in place of `VllmInferenceClient`. A class, not a stub server — nothing here points a live harness at a GPU-less box.

- Returns [non-streaming-response.json](../api_documentation/chat-completions/non-streaming-response.json), or the SSE stream rebuilt from the `data:` lines of [streaming-response.md](../api_documentation/chat-completions/streaming-response.md), chosen by whether the forwarded body sets `"stream": true`
- **The captures are linked into the test project as content**, not copied into it, so a re-capture cannot leave the tests asserting a stale response shape
- The fake records the bodies it was asked to forward. That is what lets 6e assert a rejected request never reached the inference server at all, rather than only checking the status a caller saw
- First tests of `/v1/chat/completions`: the response relays byte for byte, the request body is forwarded unchanged, and a streamed response ends in `data: [DONE]` — the terminator whose absence makes a harness hang rather than fail

**Does not cover:** that streaming stays unbuffered. The fake's body is a `MemoryStream`, so timing proves nothing here; that remains Milestone 3's proof against a real server, and Milestone 9's through the proxy.

**Done when:** an integration test posts to `/v1/chat/completions` and gets the captured response back, with no GPU involved.

### 6c. Domain: when is a voucher spendable?

_IN PROGRESS: expiry done; revoked and exhausted remain_ — see [Voucher.cs](../src/LlmVoucherGateway.Domain/Vouchers/Voucher.cs) and [VoucherSpendabilityTests.cs](../tests/LlmVoucherGateway.Domain.UnitTests/Vouchers/VoucherSpendabilityTests.cs)

`Status`, `Lifetime` and `Consumed` are currently just data — nothing in the aggregate can answer whether a voucher may be spent.

- `Voucher.CheckSpendability(IDateTimeProvider)` returns a `VoucherSpendability`: spendable, or the reason it is not. The clock is passed in rather than read, so the rule can be exercised at the instant either side of a boundary
- Pure domain: unit tests, no database and no HTTP
- **Expiry first, because it is the only rule reachable from the public surface.** `Voucher.Issue` always produces `Active` with `Consumed = 0`, and there is no `Revoke` until Milestone 8, so a unit test cannot yet build a revoked or exhausted voucher. Expiry needs nothing but a different clock. Expired *at* the expiry instant, not after it
- **Still to add:** `Revoked`, which needs a way to revoke — either pulling a small `Revoke` method forward from Milestone 8 or testing through EF materialization — and `Exhausted`, which needs `Consumed` to be settable, the same question Milestone 7 answers when it starts recording consumption. Exhaustion is a read here either way; writing `Consumed` stays in Milestone 7

**Done when:** each reason is covered by a unit test at the boundary — expiring one tick either side, consumed equal to and over allowance.

### 6d. Middleware voucher branch: hash and look up

Fills in the branch 5a left open.

- Hash the bearer token with the domain's hashing, query by `Key_Hash`, attach the credential when a row matches
- **Resolution only, as with the admin branch.** A revoked or exhausted voucher still resolves: "who are you" and "may you" stay separate, which is what lets 6e own every rejection and its status code
- `CredentialMiddleware` needs a scoped `VoucherDbContext`, which conventional middleware takes as an `InvokeAsync` parameter rather than through its constructor
- 6e needs the voucher's state, so the credential carries what the lookup already loaded rather than making 6e query a second time

**Done when:** a request bearing an issued key resolves to a voucher credential, an unknown key resolves to none, and the probe test from 5a reports `voucher` for the first time.

### 6e. Enforcement on `/v1/chat/completions`

Mirrors `AdminOnlyEndpointFilter`: an endpoint filter that requires a voucher credential, asks 6c whether it is spendable, and returns 6a's body with the mapped status. The endpoint stops being open here.

- Pass case uses 6b's fake, so the suite stays GPU-free
- Each rejection reason gets a test asserting both the status and the error body

**Done when:** a valid voucher reaches the inference client; unknown, revoked, expired and exhausted vouchers each get the right status and an OpenAI-shaped body.

### 6f. Rejection logging

Extends what 5d established for admin rejections.

- `Warning` per rejection with source address and reason. The reason is what distinguishes "wrong key" from "used it up" in a post-mortem
- Same paired assertions as 5d: the rejection line is present, and neither the attempted token nor any issued key appears in the log

**Done when:** each rejection reason produces a line naming it, and a search of captured output finds no secret.

### 6g. Harness verification

**Requires live vLLM**, so this runs on the GPU box and is the only substep that cannot be done here.

- Point Aider at the gateway with a valid voucher and redo the Milestone 2 task
- Repeat with a revoked voucher and confirm the failure is legible in the harness rather than a crash or silence

**Done when (whole milestone):** a valid voucher works, an invalid one fails cleanly _in the harness_, not just in curl.

---

## 7. Consumption recording

- Read `usage` from the response, decrement the voucher
- Streaming: requires `stream_options: {"include_usage": true}`. Whether vLLM can force this server-side is answerable from the Milestone 1 capture; otherwise inject at the API
- Soft limits: check on entry, record after, overshoot accepted
- Recording failure must not fail the request

**Depends on:** the streaming-with-usage capture from Milestone 1.

**Done when:** consumption tracks across a real multi-turn agentic session.

**Fallback:** if metering on streamed output proves fragile, meter on input tokens only or on request count. Less accurate, no response parsing, and enough for a classroom with soft budgets. Don't get stuck here.

**Note:** this is the first milestone whose correctness isn't obvious from using it. Worth a test that runs a known-token request and asserts the decrement.

---

## 8. Revocation, expiry, admin operations

- `POST /admin/vouchers/{id}/revoke` — status flag with reason
- Expiry checked at request time
- `GET /admin/sections/{id}/vouchers` — status and consumption
- Issuance alert (email or webhook)

**Done when:** the withdrawal use case works end to end, and revocation takes effect on the next request.

---

## 9. Operational readiness

The cross-cutting work, made concrete. Deliberately last of the build milestones — none of it was a concern while proving the request path.

- Structured logging with a correlation ID per request
- **`app.UseSerilogRequestLogging()`** — one line per HTTP request with method, path, status and duration. This is the backbone of any post-mortem, because it covers every route uniformly rather than only what was logged by hand, and it is the only thing that records a request the pipeline rejected before any endpoint ran. The client address is not included by default and needs a small enricher
- **A rolling file sink with retention**, on the same mounted volume as the SQLite database: console alone dies with the container, so a leak discovered on Tuesday has nothing to look back at. Daily roll with 14–30 days retained covers a term. Configuration only, no code
- **Forwarded headers belong here too.** Once Caddy is in front, every logged address is the proxy's unless `UseForwardedHeaders` is configured, which silently empties the one field abuse analysis depends on
- **Never logged, at any level:** voucher keys, admin keys, `Authorization` headers, and request or response bodies. Prompt bodies are the easy mistake — logging them would be both a volume problem and the end of this project's claim to hold no student content
- `/health` — checks vLLM reachability, not just that the API is up. Endpoint from configuration, not hardcoded.
- Configuration and secrets outside source control
- Global exception handling that returns OpenAI-shaped errors
- Caddy in front as reverse proxy for easy certificate handling
- Restart policy, and a documented "it's down, what now" procedure

**Done when:** you can restart the box and it comes back without intervention.

**Risk, accepted:** Caddy is the most likely thing to break streaming, and it arrives here rather than beside Milestone 3 — so the streaming proof from Milestone 3 doesn't cover the production path. Re-run Milestone 3's timestamp comparison through the proxy. If it buffers and the fix isn't quick, fall back to Milestone 7's simpler metering; this isn't a project where that tradeoff is expensive.

---

## 10. Real model and load test

Everything above runs on the 0.5B model on a dev-spec box. Now find out what it does under actual conditions. Requires live vLLM on real hardware.

- Swap to a real coder model on real hardware (colleague has a GX10 — arm64, so verify container images)
- Script N concurrent agentic sessions — not chat requests, full tool loops with long contexts
- Measure: time to first token, throughput, where it degrades
- Settle model, quantization, `--max-model-len`, `--gpu-memory-utilization`
- Re-pull `/openapi.json` if the vLLM version changed, and diff it against the committed copy
- Revisit the Milestone 6 no-cache decision and any concurrency caps against real numbers

**Done when:** you have a defensible concurrency number for the hardware proposal.

---

## 11. Student rollout

- Small scale test with one class, where it doesn't matter if it fails
- Setup docs per OS, and a `.env` template
- The `openai/` prefix gotcha, and the `OPENAI_API_KEY` collision

**Done when:** a student with no setup is running in under 15 minutes without your help.

---

## Revision notes

Changes from the first draft of this roadmap, kept for context:

- **Old Milestone 1b (development environment) dissolved.** Config-driven base URL moved into Milestone 3; the vLLM stand-in became a fake `IInferenceClient` in Milestone 6. Dropped entirely: DTOs generated from `openapi.json` (the passthrough needs none, and Milestone 7 needs a handful of `usage` fields) and the CI schema-drift check (the pinned image tag is the control). vLLM is a dependency we consume, not a schema we own.
- **Persistence promoted to its own milestone (4).** It was invisible work hiding inside voucher issuance.
- **Milestone 5 returns a JSON array and does not require persistence.** The earlier bar — return plaintext, done when you have a distributable CSV — mixed the one-time secret with a file format and tied minting to the database. The secret is the `key` field on each object. Storing the hash waits for Milestone 4; looking it up waits for Milestone 6.
- **Cache removed from voucher validation.** Premature at this scale, and it bought a revocation delay.
- **Credential middleware written once, in Milestone 5a.** Previously split between an admin check in issuance and a general resolver in validation.
- **Persistence switched from Postgres to SQLite, and stopped being optional.** The original choice was recorded as "Postgres and EF Core" with a container in `docker-compose.yml`, but nothing was built against it. One table for under 20 users does not need a server, and a single container is the real win; the reasoning, the backup caveat, and the rejected Redis alternative are written into Milestone 4. The compose file's `postgres` service was removed.
- **Admin authorization moved out of 5a, and now follows issuance as 5c.** 5a's bar named "an admin-only route," which is the thing 5b creates; a requirement has nothing to attach to before then. Pairing it with the endpoint in one step was the first fix, and splitting it to sit _after_ the endpoint was the second: an unprotected endpoint can be driven with `curl` and asserted on its own, so it makes a smaller increment than endpoint-plus-guard together. Logging became 5d. The original note that the middleware must precede the endpoint still holds — resolution is 5a — but "no open minting route at any point" was relaxed to "no open minting route outside the dev loop."
- **Milestone 6 split into 6a–6g.** It was five bullets spanning a wire format, a test double, a domain rule, a lookup and an enforcement point, under a single bar that could only be checked with a harness on the GPU box. The substeps are ordered so each one is verifiable when it lands, with the three independent ones first; the harness check stays last because it is the only part that needs live vLLM. `402` was settled over `429` for an exhausted allowance there.
- **Milestone 5 split into 5a/5b/5c.** It was three deliverables under one bar: the middleware, the endpoint, and the logging. The middleware is a prerequisite for the endpoint and is the thing Milestone 6 extends, so it gets its own done-when rather than being proven incidentally by a successful mint.
- **Milestone 2's bar lowered to protocol, not edit quality.** A 0.5B model failing an agentic edit says nothing about the plumbing.
- **No feasibility spike.** Hardware access is available and the shape is understood; pulling Milestone 10 forward would answer a question that isn't open.
