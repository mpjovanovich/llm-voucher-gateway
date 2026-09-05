# Preliminary Design — API Key Management Service

**Status:** Draft / pilot phase
**Owner:** Instructor-developer
**Stack:** .NET (C#), containerized alongside vLLM and the reverse proxy

---

## 1. Purpose

Issue, validate, and revoke per-student API keys that gate access to the institution-hosted vLLM endpoint. Keys are scoped to a course and expire at the end of the term.

This service does **not** sit in the request path for inference. The reverse proxy performs the actual per-request key check against a shared store; this service owns the lifecycle of what's in that store. Keeping it out of the hot path means a service outage does not take down student access.

---

## 2. Scope

**In scope (pilot):**
- Generate keys for a course roster
- Revoke individual keys
- Expire keys automatically at course end
- Record per-key usage attributed from proxy logs
- Admin-only access (instructor)

**Out of scope (deferred):**
- Student self-service portal
- SSO / institutional identity integration
- Per-student quota enforcement beyond flat rate limits
- Multi-instructor tenancy

---

## 3. Architecture Position

```
Student Harness → Reverse Proxy → vLLM
                       ↓ (reads)
                  Key Store (Redis or Postgres)
                       ↑ (writes)
              Key Management Service (.NET)
```

The store is the contract between the two. The proxy only ever reads; the service only ever writes.

**Store choice matters more than language choice.** Nginx validating keys against a file requires regenerating and reloading a map file on every change — workable but clumsy. Caddy with a Redis or Postgres lookup is cleaner and makes revocation take effect immediately. Recommend settling the proxy decision before building this service, since it defines the write target.

---

## 4. Data Model

### Course
| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| Code | string | e.g. `CSCI-2400-FA26` |
| Name | string | Display name |
| StartsOn | date | |
| EndsOn | date | Drives automatic key expiry |
| CreatedAt | timestamp | |

### ApiKey
| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| CourseId | Guid | FK → Course |
| StudentLabel | string | Name or student ID; see §8 on PII |
| KeyHash | string | Hash of the key, not the key itself |
| KeyPrefix | string | First ~8 chars, plaintext, for identification in UI and logs |
| Status | enum | `Active`, `Revoked`, `Expired` |
| IssuedAt | timestamp | |
| ExpiresAt | timestamp | Defaults to course `EndsOn` |
| RevokedAt | timestamp? | |
| RevokedReason | string? | |
| LastSeenAt | timestamp? | Updated from log ingestion |

### UsageRecord
| Field | Type | Notes |
|---|---|---|
| Id | long | PK |
| ApiKeyId | Guid | FK |
| Date | date | Daily rollup, not per-request |
| RequestCount | int | |
| TokensIn | long? | If the proxy or vLLM can surface it |
| TokensOut | long? | |

Daily rollups rather than per-request rows — a class generating thousands of requests a day doesn't need row-level retention for the questions you'll actually ask ("who's using this, how much").

---

## 5. Key Format and Handling

- Generated from a cryptographic RNG, not `Guid.NewGuid()`. 32 bytes, base64url-encoded.
- Prefixed for recognizability: `sk-clsrm-<random>`. The prefix makes leaked keys greppable and identifiable in logs.
- **Shown to the instructor exactly once at generation.** Stored only as a hash.
- If the proxy needs plaintext comparison, hash on the proxy side too — the store holds hashes either way.

Rationale for hashing even in a low-stakes classroom setting: it's the same amount of work, and it means a store dump doesn't hand over live credentials.

---

## 6. Interface

Pilot can be CLI-first; the API surface below is what a future portal would build on.

| Operation | Description |
|---|---|
| `POST /courses` | Create a course with term dates |
| `POST /courses/{id}/keys` | Bulk-generate keys from a roster (CSV upload or list of labels) |
| `GET /courses/{id}/keys` | List keys with status and last-seen |
| `POST /keys/{id}/revoke` | Immediate revocation with reason |
| `POST /courses/{id}/expire` | Force-expire all keys in a course |
| `GET /courses/{id}/usage` | Usage rollup for the term |

**Bulk generation output:** a CSV of `StudentLabel, ApiKey` for distribution. This is the one moment plaintext keys exist outside the store, so the file should be treated accordingly — deleted after distribution.

---

## 7. Background Work

- **Expiry sweep:** scheduled job flipping `Active` → `Expired` where `ExpiresAt` has passed. Run daily. The proxy should also check expiry at request time so a missed sweep doesn't silently extend access.
- **Log ingestion:** parse proxy access logs, attribute requests to keys by prefix, write daily rollups. Can be a separate Python script rather than part of the .NET service — it's throwaway parsing work and easier to iterate on there.

---

## 8. Open Questions

1. **PII in `StudentLabel`.** Storing student names or IDs in a service that isn't covered by your institution's normal student-data controls may pull FERPA into scope. An opaque per-course pseudonym, with the mapping held outside this system, sidesteps the question entirely. Worth raising with whoever owns data governance before the roster import gets built. This is a genuine legal question, not just a design preference — I'd get an answer rather than assume.
2. **Proxy choice** — blocks the store decision, which blocks this service's write path.
3. **Key distribution mechanism.** Emailing keys is convenient and is also the most likely place one leaks. LMS private message or in-person handoff are alternatives.
4. **Reuse across terms** — recommend no. Fresh keys each term, no exceptions, keeps revocation semantics simple.
5. **Shared vs. individual keys for group work.** If a course has team projects, decide early whether teams get one key or members use their own.

---

## 9. Rough Build Order

1. Data model + migrations
2. Key generation and hashing
3. Store writes matching whatever the proxy reads
4. Revocation
5. Expiry sweep
6. Usage ingestion (last — it's reporting, not access control)
