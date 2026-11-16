# Gateway API

The gateway sits in front of a vLLM server. It has two sets of routes:

| Routes     | Who calls them                     | Credential  |
| ---------- | ---------------------------------- | ----------- |
| `/v1/*`    | Voucher holders, through a client  | Voucher key |
| `/admin/*` | Operators, with curl or an editor  | Admin key   |

Both credentials are sent the same way, as a bearer token:

```
Authorization: Bearer <key>
```

An admin key does not work on `/v1`, and a voucher does not work on `/admin`.

Runnable examples of every request below are in [LlmVoucherGateway.Api.http](LlmVoucherGateway.Api.http).

## Base URL

| Environment | Base URL                                     |
| ----------- | -------------------------------------------- |
| Local       | `http://localhost:5131` (`make run-api`)     |
| Deployed    | `https://<LLM_HOSTNAME>`, as set in `.env`   |

## Using a voucher

The `/v1` routes are OpenAI-compatible. In any client that accepts a custom OpenAI endpoint, set:

- **Base URL:** `<base URL>/v1`
- **API key:** your voucher key

```sh
curl https://<LLM_HOSTNAME>/v1/chat/completions \
  -H "Authorization: Bearer <voucher-key>" \
  -H "Content-Type: application/json" \
  -d '{"model":"<model>","messages":[{"role":"user","content":"Say hello."}]}'
```

### Routes

| Route                       | What it does                                  |
| --------------------------- | --------------------------------------------- |
| `GET /v1/models`            | Lists the served model. Use its `id` as `model`. |
| `POST /v1/chat/completions` | Chat completion, streaming or not.            |

Requests and responses pass through to vLLM unchanged, so the body format is the [OpenAI chat completions format](https://platform.openai.com/docs/api-reference/chat). No other `/v1` route is available.

### Differences from OpenAI

- **Tool calling is not enabled.** Requests that send `tools` are not supported.
- **Streamed responses carry `usage` on every chunk**, whether or not the request asks for it. The last chunk holds the total.
- **`error.code` is a number** (the HTTP status), not a string.
- **Every response has an `X-Request-Id` header.** Quote it when reporting a problem.

### Allowance

Each voucher has a token allowance and an expiry date, both set when it is issued.

- Every request costs its `usage.total_tokens`: prompt plus response.
- A chat resends the whole conversation each turn, so long conversations cost more per message.
- The allowance is checked before a request and charged after it. The request that crosses the limit completes; the next one is refused.
- `GET /v1/models` is free.

There is no route for a voucher holder to check their balance. Ask whoever issued the voucher.

### Errors

Errors use the OpenAI envelope:

```json
{
  "error": {
    "message": "This voucher's token allowance is used up.",
    "type": "insufficient_quota",
    "param": null,
    "code": 403
  }
}
```

| Status | `type`                  | `message`                                    | What to do                                  |
| ------ | ----------------------- | -------------------------------------------- | ------------------------------------------- |
| `401`  | `invalid_request_error` | `A voucher is required.`                     | Key is missing or mistyped. Check it.       |
| `401`  | `invalid_request_error` | `This voucher has expired.`                  | Ask for a new voucher.                      |
| `401`  | `invalid_request_error` | `This voucher has been revoked.`             | Ask whoever issued it.                      |
| `403`  | `insufficient_quota`    | `This voucher's token allowance is used up.` | Ask for a new voucher.                      |
| `502`  | `server_error`          | `The inference server could not be reached.` | The model is down. Try again later.         |

Any other error comes from vLLM and is relayed as is: an unknown model is `404`, and a malformed request or one that exceeds the context length is `400`. Captured examples are in [docs/api_documentation/errors/](../../docs/api_documentation/errors/).

## Admin

The admin key is `ADMIN_KEY` in `.env`. A missing or wrong key returns `401` with an empty body.

Admin errors other than that `401` are [problem details](https://www.rfc-editor.org/rfc/rfc9457) JSON, not the OpenAI envelope.

### Issue vouchers

`POST /admin/vouchers`

```json
{
  "section": "CSCI-101",
  "count": 3,
  "allowance": 100000,
  "expiresAt": "2027-06-30T23:59:59Z"
}
```

| Field       | Required | Meaning                                                              |
| ----------- | -------- | -------------------------------------------------------------------- |
| `section`   | No       | A label for the batch, such as a course section. Used to list it later. |
| `count`     | Yes      | How many vouchers to mint. 1 to 100.                                 |
| `allowance` | Yes      | Tokens each voucher may spend. Must be positive.                     |
| `expiresAt` | Yes      | When each voucher stops working. Must be in the future.              |

Returns `200` with one entry per voucher:

```json
[
  {
    "id": "0b6f1c1e-5d1b-4b0e-9a57-3f1f6f0a2c11",
    "key": "3fa94c1d...",
    "prefix": "3fa94c1d",
    "section": "CSCI-101",
    "allowance": 100000,
    "consumed": 0,
    "status": "Active",
    "createdAt": "2026-11-15T14:02:11Z",
    "expiresAt": "2027-06-30T23:59:59Z"
  }
]
```

**Save the `key` values from this response.** The gateway stores only a hash, so a key that is lost cannot be recovered or shown again.

The `prefix` is the first eight characters of the key. It is safe to share, appears in the gateway's request log, and is how a voucher is looked up afterwards.

Invalid input returns `400`.

From the repository root, `make api-mint-voucher count=1 allowance=10000 days=30` sends this request.

### List a section's vouchers

`GET /admin/sections/{section}/vouchers`

Returns `200` with every voucher issued to that section, oldest first. A section with no vouchers returns an empty list.

```json
[
  {
    "id": "0b6f1c1e-5d1b-4b0e-9a57-3f1f6f0a2c11",
    "prefix": "3fa94c1d",
    "section": "CSCI-101",
    "allowance": 100000,
    "consumed": 18422,
    "status": "Active",
    "createdAt": "2026-11-15T14:02:11Z",
    "expiresAt": "2027-06-30T23:59:59Z",
    "lastRequestedAt": "2026-11-15T15:40:03Z"
  }
]
```

| Field             | Meaning                                                                          |
| ----------------- | -------------------------------------------------------------------------------- |
| `consumed`        | Tokens spent so far. Can exceed `allowance` by the size of the last request.     |
| `status`          | `Active` or `Revoked`. An expired or used-up voucher still shows `Active`; compare `expiresAt` and `consumed`. |
| `lastRequestedAt` | Last time the key was presented, including refused requests. `null` if never.    |

### Look up one voucher

`GET /admin/vouchers/{prefix}`

Returns `200` with a single voucher in the same shape as above.

| Status | Meaning                                                                        |
| ------ | ------------------------------------------------------------------------------ |
| `404`  | No voucher has that prefix.                                                    |
| `409`  | More than one voucher has that prefix. List the section to tell them apart.    |

### Revoke a voucher

`POST /admin/vouchers/{id}/revoke`

```json
{
  "reason": "Student withdrew from the course."
}
```

| Field    | Required | Meaning                                  |
| -------- | -------- | ---------------------------------------- |
| `reason` | Yes      | Why the voucher was withdrawn, for the record. |

Takes the voucher's `id`, not its prefix. To get from a prefix in the request log to an id, look the voucher up first.

Returns `200` with the voucher in the same shape as the lookup, now with `status` `Revoked`. The voucher is refused from its next request on, with `401` and `This voucher has been revoked.` A revoked voucher cannot be reactivated; issue a new one instead.

| Status | Meaning                        |
| ------ | ------------------------------ |
| `400`  | `reason` is missing or empty.  |
| `404`  | No voucher has that id.        |

## Health

`GET /health` returns `200` when vLLM is reachable and `503` when it is not. It needs no credential. In a deployment it is not exposed publicly; call it on the server at `localhost:8080/health`.
