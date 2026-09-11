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

For output capture:

.NET API calls vLLM with stream: true, relays chunks to the client as they arrive (read/write loop, no buffering).
Count completion tokens as chunks pass through (via usage field if requested, or one token per content chunk as fallback).
Write the usage record when the loop ends, regardless of whether the client disconnected first.

Considerations:

Disable response compression and any buffering middleware in ASP.NET Core for this endpoint.

- TODO: what are the downsides?
  Whatever proxy sits in front of the .NET API (Caddy probably) needs buffering disabled and a read timeout longer than generation time — this is the part that silently breaks streaming if missed.
  Test through the actual reverse proxy before considering it done; local dotnet run won't surface the buffering issue.
  Worst-case failure: a disconnect mid-stream undercounts that request's output tokens. Not a big deal.

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

**ASP.NET API** — the whole application. Name will be generic "LLM Gateway", with casing matching standard project and technology conventions.

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

- **Issue:** admin curls `POST /admin/vouchers` with section, count, allowance, expiry. Response returns plaintext keys _once_; DB stores hashes only. That CSV is distributed and then deleted.
- **Use:** student pastes key into harness config. Middleware validates on each request.
- **Revoke:** status flag, not a delete — a deleted row can't explain why something stopped working.
- **Expire:** checked at request time, not only by a sweep.
- **Term rollover:** issue a new batch. No reuse across terms.
- **Lost voucher:** issue a new one. No recovery, because no identity.

Note: admins need the ADMIN_KEY to create vouchers, which is implemented as an environment variable. We have to protect the case where this leaks. Fix is simple - change env key and cycle service. But we need logging and notifications whenever vouchers are created. Log voucher ID and context info (not actual voucher key b/c this is sensitive).

## Metering

Soft budgets — check on entry, record after. A student can overshoot on their final request; that's accepted.

Token counts come from the `usage` object in vLLM's response.

**Trap:** in streaming mode for vLLM `usage` is absent unless the request includes `stream_options: {"include_usage": true}` — and harnesses won't set it. Either inject it server-side, check whether vLLM can force it, or meter by request count instead.

## Hardware

- **Prototype (now):** work machine, tiny model (Qwen2.5-0.5B-Instruct). Proving GPU passthrough and plumbing, not quality.
- **Pilot (<20 users):** ~48GB VRAM (L40S / A6000) with a Qwen coder model. **Check first:** a colleague has ASUS Ascent GX10 units (GB10, 128GB unified memory) — ample capacity, but LPDDR5x bandwidth is lower than discrete VRAM and token generation is bandwidth-bound. Arm64, so verify container images. Borrow one and run Milestone 1 before requesting hardware.

## Students

Harness runs on Windows, Mac, Linux. **Chromebooks are not supported**.

## Deferred

streaming-aware proxying (Caddy supports better than nginx?)
