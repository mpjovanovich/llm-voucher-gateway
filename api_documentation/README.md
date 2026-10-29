# API capture set — Milestone 1

Everything captured from a live vLLM instance so Milestones 4–9 can be built and
tested without a GPU. Captured generously per the roadmap's instruction — anything missed here is a return trip.

## Environment at capture time

- **vLLM image (pinned):** `vllm/vllm-openai:v0.29.0`
- **Model served:** `Qwen/Qwen2.5-0.5B-Instruct`
- **`max_model_len`:** 32768
- **Launch command (at capture time):** `--model Qwen/Qwen2.5-0.5B-Instruct --gpu-memory-utilization 0.85`
- **Launch command (current):** adds `--enable-force-include-usage` (see [docker-compose.llm.yml](../docker-compose.llm.yml))
- **Capture date:** 2026-09-11, except the forced-usage capture below (2026-10-28)

The flag changes the streaming response shape, so the two streaming captures from
2026-09-11 no longer describe what this server returns. Each says so at the top.

## Contents

| Concern | Path | Notes |
|---|---|---|
| OpenAPI schema | [openapi.json](openapi.json) | Raw `/openapi.json` from the running instance. Re-pull and diff in Milestone 10 if the vLLM version changes. |
| Non-streaming chat completion | [chat-completions/non-streaming-response.json](chat-completions/non-streaming-response.json) | Baseline response shape, including `usage`. |
| Streaming chat completion | [chat-completions/streaming-response.md](chat-completions/streaming-response.md) | Flag off. Raw SSE chunks. `finish_reason` lands on the final content chunk; no `usage` at all. Also the integration tests' SSE fixture. |
| Streaming with usage | [chat-completions/streaming-response-with-usage.md](chat-completions/streaming-response-with-usage.md) | Flag off, client opts in. `usage` arrives in its own trailing chunk with empty `choices`, after the `finish_reason: "stop"` chunk. |
| **Streaming, server-forced usage** | [chat-completions/streaming-response-forced-usage.md](chat-completions/streaming-response-forced-usage.md) | **What the gateway actually sees.** Feeds Milestone 7. `usage` on every chunk as a running count, with no client cooperation. |
| Error: unknown model | [errors/unknown-model.json](errors/unknown-model.json) | HTTP 404, `type: "NotFoundError"`. |
| Error: context-length overflow | [errors/context-length-overflow.json](errors/context-length-overflow.json) | HTTP 400, `type: "BadRequestError"`. Forced via an out-of-range `max_tokens`. |
| Error: malformed request | [errors/malformed-request.json](errors/malformed-request.json) | HTTP 400, `type: "Bad Request"`. Two variants: invalid JSON syntax, and schema-validation failure. |

## Streaming baseline — Milestone 3

Evidence that the gateway relays SSE unbuffered. **When streaming misbehaves after a
change (Caddy in Milestone 9 is the likely one), rerun this and diff against these
numbers.**

- **Date:** 2026-09-12 — same vLLM image and model as above, GPU host under WSL2
- **Gateway:** `dotnet run`, Development config, Kestrel on `:5131` → vLLM on `:8000`
- **Method:** every `data:` line timestamped on arrival at the client. Direct and
  gateway runs alternated, three each, `temperature: 0`. Equivalent by hand:

  ```bash
  curl -N -s http://localhost:5131/v1/chat/completions \
      -H "Content-Type: application/json" \
      -H "Authorization: Bearer $VOUCHER_KEY" \
      -d '{"model":"Qwen/Qwen2.5-0.5B-Instruct",
           "messages":[{"role":"user","content":"Count slowly from 1 to 50."}],
           "stream":true}' \
    | while IFS= read -r line; do printf '%s %s\n' "$(date +%s.%3N)" "$line"; done
  ```

  **Since Milestone 6 this endpoint requires a voucher.** The baseline above was
  recorded while it was open, so rerunning it means minting one first and exporting it
  as `VOUCHER_KEY`:

  ```bash
  curl -s http://localhost:5131/admin/vouchers \
      -H "Content-Type: application/json" \
      -H "Authorization: Bearer dev-admin-key" \
      -d '{"section":"timing","count":1,"allowance":1000000,
           "expiresAt":"2027-06-30T23:59:59Z"}'
  ```

  The response is a JSON array; `key` from its single object is the bearer token, and
  that response is the only place it exists. Allow enough allowance for the run —
  consumption is not yet recorded (Milestone 7), but a voucher that starts exhausted is
  refused with `402`. Against vLLM directly on `:8000` the header is ignored, since vLLM
  has no concept of vouchers.

**"Count slowly from 1 to 50."** — 207 chunks, `max_tokens: 512`

| Target | Time to first chunk | Spread (first→last) | Median gap | p95 gap | Max gap |
|---|---|---|---|---|---|
| Direct | 25–27 ms | 854–858 ms | 4.1–4.2 ms | 4.8 ms | 5.3–13.8 ms |
| Gateway | 25–43 ms | 844–854 ms | 4.1 ms | 4.7–4.8 ms | 5.1–5.6 ms |

**600-word essay** — ~750 chunks, `max_tokens: 1024`

| Target | Time to first chunk | Spread (first→last) | Median gap | p95 gap | Max gap |
|---|---|---|---|---|---|
| Direct | 25–26 ms | 3.1–3.3 s | 4.2 ms | 4.7–5.4 ms | 5.7–16.1 ms |
| Gateway | 27–32 ms | 3.1–3.2 s | 4.1–4.3 ms | 4.7–5.1 ms | 5.8–14.2 ms |

Direct's cold first request (304 ms to first chunk) is excluded above as warmup.

**How to read it.** Buffering shows up as chunks collapsing onto one timestamp: time to
first chunk jumps to roughly the full generation time, and the median gap drops toward
zero. Neither happens here — both targets deliver a chunk every ~4 ms (≈240 tokens/s)
spread across the whole generation, and the gateway adds at most a few milliseconds to
the first chunk. About 1% of gaps are under 1 ms on *both* targets; that is vLLM
emitting adjacent chunks together, not the gateway.

Note that `temperature: 0` is not fully deterministic on vLLM: one direct essay run
produced 961 chunks against 750 for the other five. Compare timing shapes, not text.

## Regenerating this capture set

With vLLM running (`make up-llm`), see the `curl` examples at the top of each
file. The comments at the top of
[docker-compose.llm.yml](../docker-compose.llm.yml) show the base request shapes
this capture set was built from.

## Feeds forward to

- **Milestone 6** (voucher validation): error payload shapes, so rejections stay
  OpenAI-shaped. A fake `IInferenceClient` replays these responses so the pass
  case can run without a GPU.
- **Milestone 7** (consumption recording): `streaming-response-forced-usage.md` —
  the running-count-per-chunk shape, which is what the metering loop reads.
  `streaming-response-with-usage.md` keeps the trailing-chunk-only shape for contrast.
- **Milestone 10:** re-pull `openapi.json` and diff against this copy if the vLLM
  version changes before load testing.

The pinned image tag above is the drift control; there is no CI schema check, and
no DTOs are generated from `openapi.json`.
