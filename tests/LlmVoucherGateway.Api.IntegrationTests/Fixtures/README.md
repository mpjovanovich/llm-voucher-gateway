# Captured vLLM responses

Byte-for-byte captures from a live vLLM instance, replayed by `FakeInferenceClient` so the
gateway can be tested without a GPU. These files are the single source of truth for what
the upstream server returns; `api_documentation/` explains the shapes and cites them rather
than holding its own copies.

Capture environment (image tag, model, launch flags) is recorded in
[api_documentation/README.md](../../../api_documentation/README.md).

## Streaming captures

Two independent switches decide whether a streaming response carries token counts, and the
three captures are one per reachable combination. Streaming itself is caused by neither of
them — that is `"stream": true` in the request body, which all three captures set.

| File | How it was derived | Resulting `usage` shape |
|---|---|---|
| [streaming-response-usage-requested-server.sse](streaming-response-usage-requested-server.sse) | server started with `--enable-force-include-usage`; client sent no `stream_options` | on **every** chunk, as a running count: `completion_tokens` climbs by one per chunk, and the `finish_reason: "stop"` chunk already holds the final total |
| [streaming-response-usage-requested-client.sse](streaming-response-usage-requested-client.sse) | flag off; client sent `stream_options: {"include_usage": true}` | **one** trailing chunk only, after the `finish_reason` chunk, with empty `choices` |
| [streaming-response-usage-not-requested.sse](streaming-response-usage-not-requested.sse) | flag off; client sent no `stream_options` | **absent entirely**; `finish_reason` and `system_fingerprint` land together on the last content chunk, with no trailing chunk at all |

| File | `data:` lines (incl. `[DONE]`) | Final counts (prompt/completion/total) | Captured |
|---|---|---|---|
| `...usage-requested-server.sse` | 28 | 35 / 25 / 60 | 2026-10-28 |
| `...usage-requested-client.sse` | 24 | 35 / 21 / 56 | 2026-09-11 |
| `...usage-not-requested.sse` | 13 | none reported | 2026-09-11 |

The server-forced capture is the one the gateway is built against, because the dev compose
file sets that flag and student harnesses do not send `stream_options`. The client-requested
capture is currently read by no test; it is kept as the record of what an unforced server
does when a client opts in, which is the shape the gateway would face if the flag were ever
dropped.

A fourth combination — both switches on — is not captured. The server flag short-circuits
ahead of the client option, so it would return the running-count shape already captured
above.

## Non-streaming and error captures

| File | What it is |
|---|---|
| [non-streaming-response.json](non-streaming-response.json) | `"stream": false` chat completion, `usage` included unconditionally (35 / 10 / 45). Captured 2026-09-11. |
| [errors/unknown-model.json](errors/unknown-model.json) | HTTP 404, `type: "NotFoundError"`. Wraps the triggering `request` alongside the `response`. |
| [errors/context-length-overflow.json](errors/context-length-overflow.json) | HTTP 400, `type: "BadRequestError"`, forced with an out-of-range `max_tokens`. Same `request`/`response` wrapper. |
| [errors/malformed-request.json](errors/malformed-request.json) | HTTP 400. Holds two `variants` — invalid JSON syntax, and schema-validation failure — in place of the single `request`/`response` pair the other two use. |

Error captures pin the gateway's own rejections to the upstream envelope, so a harness only
ever has to understand one error format.

## Editing these

Tests assert on the exact bytes — the token totals the usage observers extract, and the
bodies the relay must pass through untouched. Refresh one by re-running the capture against
a live vLLM (`make up-llm`, then the `curl` shapes in `api_documentation/`), not by hand
editing. The `created` timestamps and `id` values are part of the capture; leaving them
intact is what makes the file evidence rather than a fabricated example.
