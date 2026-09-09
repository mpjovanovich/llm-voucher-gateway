# Pilot Roadmap — Refined

Each milestone ends in something demonstrable. Ordering favors killing unknowns early over building in layers.

---

## 1. vLLM running

Docker Compose, tiny model (Qwen2.5-0.5B-Instruct), GPU passthrough.

- `curl /v1/models` returns the served model
- `curl /v1/chat/completions` returns valid JSON
- Same request with `"stream": true` returns SSE chunks ending in `[DONE]`
- Capture both response shapes — they're your DTO reference

**Done when:** you have a saved non-streaming and streaming response to model against.

**Risk:** NVIDIA Container Toolkit and `ipc: host`. If GPU passthrough is going to fight you, it fights you here.

---

## 2. Harness against vLLM directly

Prove the agent loop works before your code is anywhere near it.

- Aider or OpenCode configured at `localhost:8000`
- Complete a small file-edit task (the CSV demo)

**Done when:** the model reads a file, edits it, and reports back.

**Why before the API:** if the harness misbehaves, you want to know it's not your fault.

---

## 3. Pass-through API

ASP.NET, no auth, no vouchers. Accepts `/v1/chat/completions`, forwards to vLLM, returns the response.

- `IInferenceClient` + `VllmInferenceClient` behind it
- **Streaming must pass through unbuffered**
- Point the harness at your API instead of vLLM and redo the Milestone 2 task

**Done when:** the harness can't tell the difference.

**Risk:** ASP.NET buffering the response. A harness against a buffering proxy looks hung, not broken, and it's a miserable thing to diagnose later with auth and vouchers in the way. Prove it here while there's nothing else to blame.

---

## 4. Voucher issuance

Domain module and admin endpoint. No enforcement yet.

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
- **Errors must be OpenAI-shaped.** A harness receiving your custom error format will render garbage or crash. This is the domain-error modeling you flagged — the constraint is that every failure has to leave through a wire format the client already understands.
- Distinguish 401 (bad credential) from 402/429 (valid but out of allowance), so a student can tell "wrong key" from "used it up"

**Done when:** a valid voucher works, an invalid one fails cleanly _in the harness_, not just in curl.

---

## 6. Consumption recording

- Read `usage` from the response, decrement the voucher
- Streaming: requires `stream_options: {"include_usage": true}` — check whether vLLM can force it server-side, otherwise inject at the API
- Soft limits: check on entry, record after, overshoot accepted
- Recording failure must not fail the request

**Done when:** consumption tracks across a real multi-turn agentic session.

**Note:** this is the first milestone whose correctness isn't obvious from using it. Worth a test that runs a known-token request and asserts the decrement.

---

## 7. Revocation, expiry, admin operations

- `POST /admin/vouchers/{id}/revoke` — status flag with reason
- Expiry checked at request time
- `GET /admin/sections/{id}/vouchers` — status and consumption
- Issuance alert (email or webhook)

**Done when:** the withdrawal use case works end to end, and revocation takes effect within your cache TTL.

## 8. Operational readiness

The cross-cutting work, made concrete:

- Structured logging with a correlation ID per request
- `/health` — checks vLLM reachability, not just that the API is up
- Configuration and secrets outside source control
- Global exception handling that returns OpenAI-shaped errors
- Caddy in front as reverse proxy for easy certificate handling
- Restart policy, and a documented "it's down, what now" procedure

**Done when:** you can restart the box and it comes back without intervention.

---

## 9. Real model and load test

Everything above runs on the 0.5B model. Now find out what it does under actual conditions.

- Swap to a real coder model on real hardware (colleague has a GX10)
- Script N concurrent agentic sessions — not chat requests, full tool loops with long contexts
- Measure: time to first token, throughput, where it degrades
- Settle model, quantization, `--max-model-len`, `--gpu-memory-utilization`

**Done when:** you have a defensible concurrency number for the hardware proposal.

---

## 10. Student rollout

- Small scale test with one class, where it doesn't matter if it fails
- Setup docs per OS, and a `.env` template
- The `openai/` prefix gotcha, and the `OPENAI_API_KEY` collision

**Done when:** a student with no setup is running in under 15 minutes without your help.
