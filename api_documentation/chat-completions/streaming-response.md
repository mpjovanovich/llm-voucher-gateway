# Streaming response — without `stream_options.include_usage`

> **Stale as a description of the current server.** This was captured before
> `--enable-force-include-usage` was added to
> [docker-compose.llm.yml](../../docker-compose.llm.yml). With that flag set, this
> request shape now returns `usage` on every chunk — see
> [streaming-response-forced-usage.md](streaming-response-forced-usage.md) for what
> the gateway actually sees.
>
> Kept for two reasons: it records vLLM's unforced behavior, and the integration
> tests rebuild their fake SSE stream from the `data:` lines below
> (`CapturedResponses.StreamingBody()`). Reproducing it requires removing the flag.

## Request

```
curl -N http://localhost:8000/v1/chat/completions \
    -H "Content-Type: application/json" \
    -d '{
      "model": "Qwen/Qwen2.5-0.5B-Instruct",
      "messages": [{"role": "user", "content": "Say hello in one sentence."}],
      "stream": true
    }'
```

## Key finding

`finish_reason` and `system_fingerprint` land **on the same, final content chunk** —
there is no separate terminal event. `usage` is **absent entirely**, which is the
problem the metering design had to solve: a client that doesn't opt in gets no token
counts at all.

Two ways to get them, both captured separately:

- the client sets `stream_options.include_usage` —
  [streaming-response-with-usage.md](streaming-response-with-usage.md)
- the server sets `--enable-force-include-usage`, which is what the gateway relies on
  since harnesses won't opt in —
  [streaming-response-forced-usage.md](streaming-response-forced-usage.md)

Either way a terminal `choices: []` chunk appears and `system_fingerprint` moves onto
it, so the "no separate terminal event" finding above holds only with both switches
off.

## Raw SSE stream

```
data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"role":"assistant","content":""},"logprobs":null,"finish_reason":null}],"prompt_token_ids":null,"prompt_text":null}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"Hello"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"!"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" It"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"'s"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" a"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" pleasure"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" to"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" meet"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" you"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"."},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":""},"logprobs":null,"finish_reason":"stop","stop_reason":null,"token_ids":null}],"system_fingerprint":"vllm-0.29.0-13ff2e7e"}

data: [DONE]
```
