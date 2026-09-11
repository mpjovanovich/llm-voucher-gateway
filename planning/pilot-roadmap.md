# Pilot Roadmap — Refined

Each milestone ends in something demonstrable. Ordering favors killing unknowns early over building in layers.

---

## Environment split

Development may happens across two machine hardware profiles, and the roadmap depends on knowing which is which.

- **GPU box** Runs vLLM. Can be a dev machine as long as it has an NVIDIA GPU. The only place real responses come from.
- **Non GPU machine** Noted here because some dev machines do not have an NVIDIA GPU; AVX2 only — no AVX512 — so it cannot run vLLM's CPU backend. We can't run local inference on machines of this profile.

**Requires live running vLLM:** Milestones 1, 2, 3, 9
**Can proceed on fixtures:** Milestones 4, 5, 6, 7, 8

---

## 1. vLLM running on a host machine with an NVIDIA GPU

_COMPLETE_

Docker Compose, tiny model (Qwen2.5-0.5B-Instruct), GPU passthrough. Requires a host with an NVIDIA GPU.

- `curl /v1/models` returns the served model
- `curl /v1/chat/completions` returns valid JSON
- Same request with `"stream": true` returns SSE chunks ending in `[DONE]`

### Capture set

_COMPLETE_

Everything later milestones will be built against. Capture generously — anything missed here is a return trip.

- Non-streaming response
- Streaming response, **including the final chunk**: where `finish_reason` lands, and whether `usage` appears there
- Streaming response with `stream_options: {"include_usage": true}` — feeds Milestone 6
- Error payloads: context-length overflow, malformed request, unknown model name — feed Milestone 5
- `/openapi.json` from the running instance, committed to the repo
- The pinned vLLM image tag, recorded

**Done when:** the full capture set is committed.

**Risk:** NVIDIA Container Toolkit and `ipc: host`. If GPU passthrough is going to cause problems, it will happen here.

**Risk:** Note that dev machines that do not have an NVIDIA GPU will not be able to run vLLM. That is why we are capturing the responses here (and so that we don't need to actually run vLLM when developing locally).

---

## 1b. Development environment

_IN PROGRESS_

What makes Milestones 4 through 8 possible without a live GPU.

- Fixture stub server replaying the captured responses, streaming included
- DTOs generated from — or validated against — the committed `openapi.json`
- CI check that fails when a fixture drifts from the schema
- Base URL driven by configuration from the start: localhost, production

**Done when:** you can develop and run tests without a GPU (or without using it).

**Note:** fixtures cover stretches without a live GPU, not the default workflow. Anything not captured in Milestone 1 means running vLLM again.

---

## 2. Harness against vLLM directly

Prove the agent loop works before we start coding against the API. Requires live vLLM.

- Aider or OpenCode configured at the vLLM endpoint (`localhost:8000`)
- Complete a small file-edit task (the CSV demo)

**Done when:** the model reads a file, edits it, and reports back.

---

## 3. Pass-through API

.NET Core API, no auth, no vouchers. Accepts `/v1/chat/completions`, forwards to vLLM, returns the response.

- `IInferenceClient` + `VllmInferenceClient` behind it, endpoint supplied by configuration — this is the seam that lets the same code target local vLLM in development and real hardware in production
- **Streaming must pass through unbuffered**
- Point the harness at your API instead of vLLM and redo the Milestone 2 task

**Done when:** the harness can't tell the difference.

**Requires live vLLM.** Streaming passthrough has to be proven against the real server. The fixture stub won't reproduce the buffering behavior this milestone exists to catch.

**Risk:** ASP.NET buffering the response. A harness against a buffering proxy looks hung, not broken, and it's difficult to diagnose later with auth and vouchers in the way. Prove it here while there's nothing else added on top of it.

---

## 4. Voucher issuance

Domain module and admin endpoint. No enforcement yet. Fixtures are sufficient from here through Milestone 8.

- Voucher aggregate: key hash, prefix, section, allowance, consumed, expiry, status
- `POST /admin/vouchers` — issue a batch, return plaintext once
- Admin key check (list of valid keys, not one)
- Issuance logging: voucher IDs, prefixes, count, source IP — never plaintext

**Done when:** `curl` mints 20 vouchers and you get a distributable CSV.

---

## 5. Voucher validation

Middleware resolves credential → `{ VoucherId?, IsAdmin }`. Requests without a valid voucher are rejected.

- Hash + lookup, cached with a short TTL (decide the TTL deliberately — it's your revocation delay)
- Reject: unknown, revoked, expired, allowance exhausted
- **Errors must be OpenAI-shaped**, matching the error payloads captured in Milestone 1. A harness receiving your custom error format will render garbage or crash. This is the domain-error modeling you flagged — the constraint is that every failure has to leave through a wire format the client already understands.
- Distinguish 401 (bad credential) from 402/429 (valid but out of allowance), so a student can tell "wrong key" from "used it up"

**Depends on:** captured error payloads from Milestone 1. Without them the error shape is guesswork.

**Done when:** a valid voucher works, an invalid one fails cleanly _in the harness_, not just in curl.

---

## 6. Consumption recording

- Read `usage` from the response, decrement the voucher
- Streaming: requires `stream_options: {"include_usage": true}`. Whether vLLM can force this server-side is answerable from the Milestone 1 capture; otherwise inject at the API
- Soft limits: check on entry, record after, overshoot accepted
- Recording failure must not fail the request

**Depends on:** the streaming-with-usage capture from Milestone 1.

**Done when:** consumption tracks across a real multi-turn agentic session.

**Note:** this is the first milestone whose correctness isn't obvious from using it. Worth a test that runs a known-token request and asserts the decrement.

---

## 7. Revocation, expiry, admin operations

- `POST /admin/vouchers/{id}/revoke` — status flag with reason
- Expiry checked at request time
- `GET /admin/sections/{id}/vouchers` — status and consumption
- Issuance alert (email or webhook)

**Done when:** the withdrawal use case works end to end, and revocation takes effect within your cache TTL.

---

## 8. Operational readiness

The cross-cutting work, made concrete:

- Structured logging with a correlation ID per request
- `/health` — checks vLLM reachability, not just that the API is up. Endpoint from configuration, not hardcoded.
- Configuration and secrets outside source control
- Global exception handling that returns OpenAI-shaped errors
- Caddy in front as reverse proxy for easy certificate handling
- Restart policy, and a documented "it's down, what now" procedure

**Done when:** you can restart the box and it comes back without intervention.

---

## 9. Real model and load test

Everything above runs on the 0.5B model. Now find out what it does under actual conditions. Requires live vLLM on real hardware.

- Swap to a real coder model on real hardware (colleague has a GX10)
- Script N concurrent agentic sessions — not chat requests, full tool loops with long contexts
- Measure: time to first token, throughput, where it degrades
- Settle model, quantization, `--max-model-len`, `--gpu-memory-utilization`
- Re-pull `/openapi.json` if the vLLM version changed, and diff it against the committed copy

**Done when:** you have a defensible concurrency number for the hardware proposal.

---

## 10. Student rollout

- Small scale test with one class, where it doesn't matter if it fails
- Setup docs per OS, and a `.env` template
- The `openai/` prefix gotcha, and the `OPENAI_API_KEY` collision

**Done when:** a student with no setup is running in under 15 minutes without your help.
