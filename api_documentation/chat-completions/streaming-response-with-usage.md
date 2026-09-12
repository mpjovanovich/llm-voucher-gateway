# Streaming response — with `stream_options: {"include_usage": true}`

## Request

```
curl -N http://localhost:8000/v1/chat/completions \
    -H "Content-Type: application/json" \
    -d '{
      "model": "Qwen/Qwen2.5-0.5B-Instruct",
      "messages": [{"role": "user", "content": "Say hello in one sentence."}],
      "stream": true,
      "stream_options": {"include_usage": true}
    }'
```

## Key finding — feeds Milestone 7

Setting `include_usage` adds **one extra chunk after the `finish_reason: "stop"` chunk**,
before `[DONE]`. That extra chunk has an **empty `choices` array** and carries the
`usage` object (`prompt_tokens`, `completion_tokens`, `total_tokens`) plus
`system_fingerprint` — both of which sit on the finish_reason chunk itself when
`include_usage` is *not* set (compare
[streaming-response.md](streaming-response.md)).

Implication for Milestone 7 (consumption recording): a streaming consumer must not
stop reading at `finish_reason: "stop"` — it has to keep reading until `[DONE]` to
see the usage chunk. And `stream_options.include_usage` can be set server-side by
vLLM's request handling in this version, so it's an option the API layer can inject
without client cooperation if a client omits it.

## Raw SSE stream

```
data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"role":"assistant","content":""},"logprobs":null,"finish_reason":null}],"prompt_token_ids":null,"prompt_text":null}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"Hello"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"!"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" It"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"'s"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" always"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" nice"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" to"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" say"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" hello"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" when"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" you"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" meet"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" someone"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" new"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" or"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" want"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" to"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" introduce"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" yourself"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"."},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":""},"logprobs":null,"finish_reason":"stop","stop_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-ad6ca7ad89c6f55d","object":"chat.completion.chunk","created":1789154478,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[],"usage":{"prompt_tokens":35,"total_tokens":56,"completion_tokens":21},"system_fingerprint":"vllm-0.29.0-13ff2e7e"}

data: [DONE]
```
