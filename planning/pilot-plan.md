# Classroom LLM Pilot

## TODO:

_Some notes on budgeting and streaming output_

Need to decide streaming and budgeting.

Students will gripe if time-to-first-token is long.

Budgeting (enforcement, checked before calling vLLM, soft cap so students can go over during request and next request is denied not current):

Budget model may be: on

- Request count; simple but very inaccurate
- Input tokens only; easy implementation, no need to intercept output
- Input + output tokens

**The cost argument for the first two has mostly evaporated.** They were attractive
because capturing output looked expensive. With `--enable-force-include-usage` the
server hands over `total_tokens` (already input + output) without the gateway parsing
a single chunk, so the most accurate model is now also nearly the cheapest. The
weaker models survive only as the fallback if streaming metering proves fragile in
practice.

For output capture:

_Settled 2026-10-28 — see [streaming-response-forced-usage.md](../api_documentation/chat-completions/streaming-response-forced-usage.md)._

The gateway relays the client's request upstream untouched (no `stream_options`
injection, so the opaque passthrough survives) and relays chunks back as they arrive.
vLLM runs with `--enable-force-include-usage`, which puts a `usage` object carrying a
**running count** on every chunk.

So the loop does not count anything and does not parse chunks on the fly:

1. Relay bytes through unchanged, splitting only on `\n` to find line boundaries.
2. Keep a copy of the most recent `data:` line — overwrite a variable, no parsing.
3. When the loop ends, parse that one saved line and read `total_tokens` off it.

`total_tokens` is already `prompt_tokens + completion_tokens` — read it, don't sum
them, or the prompt gets double-billed.

Write the usage record when the loop ends, regardless of whether the client
disconnected first. Stay on bytes rather than reassembling lines into strings:
re-serializing risks mangling SSE framing, including the keep-alive comment lines
mentioned below.

Considerations:

Disable response compression and any buffering middleware in ASP.NET Core for this endpoint.

- TODO: what are the downsides?
  Whatever proxy sits in front of the .NET API (Caddy probably) needs buffering disabled and a read timeout longer than generation time — this is the part that silently breaks streaming if missed.
  Test through the actual reverse proxy before considering it done; local dotnet run won't surface the buffering issue.

  **vLLM 0.29 can help from its end:** `--sse-keep-alive-interval <seconds>` emits an
  SSE comment line while a stream is idle — queued, during prefill, or between tokens
  — specifically to stop proxies and tunnels with read timeouts from closing the
  connection. Defaults to 0 (disabled). Fixing it at the source beats tuning proxy
  timeouts, so consider setting this when Caddy lands.

  Worst-case failure: a disconnect mid-stream used to undercount that request's output
  tokens. With a running count on every chunk, the last chunk that got out still
  carries a valid total, so the undercount is now bounded by whatever was in flight.

## Goal

Concrete goal: Give students a Claude Code–like agentic coding experience without paid API access or capable local hardware. Institution hosts the model; students run only a lightweight harness locally.

General goal: This will serve as a general purpose OpenAI API that gives our institution full control and a lightweight usage allocation model. They can use it with a harness, or anything else that hooks into the spec.

## Shape

```
Student harness  →  ASP.NET API  →  vLLM (Docker, GPU)
   (OpenCode /      - auth
    Aider)          - voucher check
                    - usage recording
                        ↓
                    Vouchers DB
```

**Key decision:** the API sits _in_ the request path. This is a deliberate trade — it enables token metering.

## Components

**vLLM** — official Docker image, GPU passthrough, `ipc: host` required. Serves an OpenAI-compatible endpoint. Has no concept of students; the API calls it with a single service credential. It is not part of the Web API application; it's just what that application talks to.

**ASP.NET API** — the whole application. Name will be generic "LLM Voucher Gateway", with casing matching standard project and technology conventions.

Composition root plus:

- _Vouchers module_ — the only domain module. Holds voucher state (key hash, section, allowance, consumed, expiry, status) and the rules around it.
- _Inference client adapter_ — thin `IInferenceClient` implementation for talking with vLLM server. HTTP, JSON, DTO mapping, no domain logic. Exists so tests don't need a GPU.
- _Auth middleware_ — resolves any credential into `{ VoucherId?, IsAdmin }`.

**Vouchers DB** — one table. Policy values (allowance, expiry) are _stamped onto each voucher at issuance_ via API parameters, so changing policy affects future batches only.

If we later want more control over voucher policy we can template these with a table and have the parameter be a "voucher type".

## Auth model

No user entities, no identity.

```
Bearer token →
  == configured ADMIN_KEY?  → { IsAdmin: true }
  else hash + lookup voucher → { VoucherId, IsAdmin: false }
  else 401
```

Vouchers are bearer tokens: whoever holds one can spend it. Sharing is undetectable. Fine for a classroom with soft budgets.

**Why no user entities:** authorization needs a _claim_, not a person. One admin means the secret _is_ the credential. Students are pseudonymous by design, which also keeps FERPA out of scope entirely.

## Voucher lifecycle

- **Issue:** admin curls `POST /admin/vouchers` with section, count, allowance, expiry. The response is a JSON array of voucher objects. Each object includes the bearer secret once; that response is the only place the secret exists. The API does not write a file. Storing the hash is persistence (roadmap Milestone 4), not part of issuance.
- **Use:** student pastes key into harness config. Middleware validates on each request.
- **Revoke:** status flag, not a delete — a deleted row can't explain why something stopped working.
- **Expire:** checked at request time, not only by a sweep.
- **Term rollover:** issue a new batch. No reuse across terms.
- **Lost voucher:** issue a new one. No recovery, because no identity.

Note: admins need the ADMIN_KEY to create vouchers, which is implemented as an environment variable. We have to protect the case where this leaks. Fix is simple - change env key and cycle service. But we need logging and notifications whenever vouchers are created. Log voucher ID and context info (not actual voucher key b/c this is sensitive).

## Metering

Soft budgets — check on entry, record after. A student can overshoot on their final request; that's accepted.

Token counts come from the `usage` object in vLLM's response — specifically
`total_tokens`, which already includes both input and output.

**Trap (resolved 2026-10-28):** in streaming mode vLLM omits `usage` unless the
request sets `stream_options: {"include_usage": true}`, and harnesses won't set it.
Of the three outs listed here — inject it server-side, check whether vLLM can force
it, meter by request count — the second one landed. vLLM 0.29 has
`--enable-force-include-usage`, now set in
[docker-compose.llm.yml](../docker-compose.llm.yml); it forces usage on regardless of
the client, and additionally reports a running count on every chunk. Injection was
the worse option anyway: it would have meant parsing and rewriting every request
body, costing the opaque passthrough.

**This flag is not optional in production.** It lives in a compose file that is
explicitly dev-only, so wherever the pilot's vLLM gets launched needs it too —
without it, metering silently records nothing on every streaming request.

**Untested:** the flag sends `usage` to clients that never asked for it. Should be
ignorable, but verify against OpenCode/Aider during the harness run.

## Hardware

- **Prototype (now):** work machine, tiny model (Qwen2.5-0.5B-Instruct). Proving GPU passthrough and plumbing, not quality.
- **Pilot (<20 users):** ~48GB VRAM (L40S / A6000) with a Qwen coder model. **Check first:** a colleague has ASUS Ascent GX10 units (GB10, 128GB unified memory) — ample capacity, but LPDDR5x bandwidth is lower than discrete VRAM and token generation is bandwidth-bound. Arm64, so verify container images. Borrow one and run Milestone 1 before requesting hardware.

## Students

Harness runs on Windows, Mac, Linux. **Chromebooks are not supported**.

## Deferred

streaming-aware proxying (Caddy supports better than nginx?) — pair with vLLM's
`--sse-keep-alive-interval`, noted under the TODO above.
