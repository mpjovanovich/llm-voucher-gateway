# Pilot Roadmap

Each milestone ends in something demonstrable. Ordering favors killing unknowns early over building in layers.

---

## Environment split

Development may happens across two machine hardware profiles, and the roadmap depends on knowing which is which.

- **GPU box** Runs vLLM. Can be a dev machine as long as it has an NVIDIA GPU. The only place real responses come from.
- **Non GPU machine** Noted here because some dev machines do not have an NVIDIA GPU; AVX2 only — no AVX512 — so it cannot run vLLM's CPU backend. We can't run local inference on machines of this profile.

**Requires live running vLLM:** Milestones 1, 2, 3, 10
**Can proceed without a GPU:** Milestones 4, 5, 6, 7, 8, 9

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

Postgres and EF Core. No new behavior — this exists because Milestone 5 can't mint a voucher without somewhere to put it.

- `DbContext`, connection string from configuration, container already in [docker-compose.yml](../docker-compose.yml)
- Map the existing voucher aggregate: `VoucherKey` and `VoucherLifetime` as owned types
- Migration workflow settled and documented — one command, written down

**Done when:** a migration creates the voucher table and a round-trip test saves and reloads a voucher with its value objects intact.

---

## 5. Voucher issuance

_IN PROGRESS: voucher issuance domain complete_ — see [Vouchers/](../src/LlmVoucherGateway.Domain/Vouchers/)

Admin endpoint over the existing domain. No enforcement yet.

- Voucher aggregate: key hash, prefix, section, allowance, consumed, expiry, status — _done_
- `POST /admin/vouchers` — issue a batch, return plaintext once
- Credential resolution middleware, written once here: bearer token → `{ VoucherId?, IsAdmin }`, with the admin branch live and the voucher branch landing in Milestone 6. Admin check takes a list of valid keys, not one
- Issuance logging: voucher IDs, prefixes, count, source IP — never plaintext

**Done when:** `curl` mints 20 vouchers and you get a distributable CSV.

---

## 6. Voucher validation

Fill in the voucher branch of the middleware from Milestone 5. Requests without a valid voucher are rejected.

- Hash + lookup, straight to the database — **no cache.** Under 20 users on requests that occupy a GPU for seconds, a lookup is free, and caching would buy a revocation delay for no measurable gain. Revisit only if Milestone 10 shows a reason
- Reject: unknown, revoked, expired, allowance exhausted
- **Errors must be OpenAI-shaped**, matching the error payloads captured in Milestone 1. A harness receiving your custom error format will render garbage or crash. Every failure has to leave through a wire format the client already understands. Errors from vLLM relay verbatim and are already correctly shaped; this is about the rejections we generate
- Distinguish 401 (bad credential) from 402/429 (valid but out of allowance), so a student can tell "wrong key" from "used it up"
- First milestone that needs a stand-in for vLLM to exercise the pass case: a fake `IInferenceClient` replaying captured fixtures. A class, not a stub server — nothing here points a live harness at a GPU-less box

**Depends on:** captured error payloads from Milestone 1. Without them the error shape is guesswork.

**Done when:** a valid voucher works, an invalid one fails cleanly _in the harness_, not just in curl.

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
- **Cache removed from voucher validation.** Premature at this scale, and it bought a revocation delay.
- **Credential middleware written once, in Milestone 5.** Previously split between an admin check in issuance and a general resolver in validation.
- **Milestone 2's bar lowered to protocol, not edit quality.** A 0.5B model failing an agentic edit says nothing about the plumbing.
- **No feasibility spike.** Hardware access is available and the shape is understood; pulling Milestone 10 forward would answer a question that isn't open.
