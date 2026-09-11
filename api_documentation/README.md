# API capture set — Milestone 1

Everything captured from a live vLLM instance so Milestones 4–8 can be built and
tested against fixtures without a GPU (per Milestone 1b). Captured generously per
the roadmap's instruction — anything missed here is a return trip.

## Environment at capture time

- **vLLM image (pinned):** `vllm/vllm-openai:v0.29.0`
- **Model served:** `Qwen/Qwen2.5-0.5B-Instruct`
- **`max_model_len`:** 32768
- **Launch command:** `--model Qwen/Qwen2.5-0.5B-Instruct --gpu-memory-utilization 0.85` (see [docker-compose.yml](../docker-compose.yml))
- **Capture date:** 2026-09-11

## Contents

| Concern | Path | Notes |
|---|---|---|
| OpenAPI schema | [openapi.json](openapi.json) | Raw `/openapi.json` from the running instance. Re-pull and diff in Milestone 9 if the vLLM version changes. |
| Non-streaming chat completion | [chat-completions/non-streaming-response.json](chat-completions/non-streaming-response.json) | Baseline response shape, including `usage`. |
| Streaming chat completion | [chat-completions/streaming-response.md](chat-completions/streaming-response.md) | Raw SSE chunks. `finish_reason` lands on the final content chunk; no `usage` without `include_usage`. |
| Streaming with usage | [chat-completions/streaming-response-with-usage.md](chat-completions/streaming-response-with-usage.md) | Feeds Milestone 6. `usage` arrives in its own trailing chunk with empty `choices`, after the `finish_reason: "stop"` chunk. |
| Error: unknown model | [errors/unknown-model.json](errors/unknown-model.json) | HTTP 404, `type: "NotFoundError"`. |
| Error: context-length overflow | [errors/context-length-overflow.json](errors/context-length-overflow.json) | HTTP 400, `type: "BadRequestError"`. Forced via an out-of-range `max_tokens`. |
| Error: malformed request | [errors/malformed-request.json](errors/malformed-request.json) | HTTP 400, `type: "Bad Request"`. Two variants: invalid JSON syntax, and schema-validation failure. |

## Regenerating this capture set

With the compose stack running (`docker compose up`), see the `curl` examples at
the top of each file. The comments at the top of
[docker-compose.yml](../docker-compose.yml) show the base request shapes this
capture set was built from.

## Feeds forward to

- **Milestone 1b:** fixture stub server replays these responses; DTOs are
  generated from / validated against `openapi.json`.
- **Milestone 5** (voucher validation): error payload shapes, so rejections stay
  OpenAI-shaped.
- **Milestone 6** (consumption recording): the usage-chunk placement/timing in
  `streaming-response-with-usage.md`.
- **Milestone 9:** re-pull `openapi.json` and diff against this copy if the vLLM
  version changes before load testing.
