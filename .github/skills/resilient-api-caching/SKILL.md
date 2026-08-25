---
name: resilient-api-caching
description: Domain knowledge for the Masjid App's typed, resilient HTTP client and offline-first caching seam that every feature reads/writes through. USE WHEN building or modifying API access, retry/backoff, request timeouts, ETag/Last-Modified conditional requests, SQLite/secure-preferences caching, offline get-or-fetch, stale-data indicators, or environment (local/staging/prod) base-URL configuration (batches 01, 02, A1, A3, B1, B3, E2). Trigger phrases include HttpClient, Polly, exponential backoff, timeout, 304 Not Modified, ETag, If-None-Match, cache store, offline, last updated, tolerant JSON, base URL config, secure storage.
---

# Resilient API Client & Offline Caching

The single networking + persistence seam all features use. Requirements: `API-07`–`API-11`, `TC-06`,
`DV-03`, `NFR-OF-01`–`NFR-OF-03`, `NFR-RL-01`, `NFR-SC-01`. Conventions: see
[csharp.instructions.md](../../instructions/csharp.instructions.md) (System.Text.Json, tolerant parsing,
async + CancellationToken).

## Non-negotiables
- **HTTPS/TLS 1.2+ only** (`TC-06`). Reject/avoid plaintext endpoints.
- **Never crash on bad data or network failure** - degrade to cached or empty state (`API-09`, `NFR-RL-01`).
- **No hard-coded secrets/keys**; sensitive values go to platform secure storage (`NFR-SC-01`).
- Keep this seam **feature-agnostic** - no prayer/event/announcement models leak into it.

## Client design (`API-11`)
- Define `IMasjidApiClient` in `MasjidApp.Core`; back it with a single configured `HttpClient`
  (use `IHttpClientFactory` in the app head). Flow `CancellationToken` through every call.
- **Timeouts:** per-request timeout (e.g. ~10s) via the token / `HttpClient.Timeout`.
- **Retry:** exponential backoff with jitter for transient failures (timeouts, 5xx, connection resets)
  using Polly or an equivalent policy. Do **not** retry 4xx (except 408/429 with backoff).
- Return a typed result that distinguishes `Fresh`, `NotModified`, `Cached/Stale`, and `Failed` so the
  UI can show a non-blocking error/retry state.

## Tolerant deserialization (`API-07`)
- `System.Text.Json` with source-generated `JsonSerializerContext`.
- Ignore unknown/optional fields; tolerate missing values; never throw on partial/malformed payloads.
- Wrap parsing so a malformed body yields a degraded result, never an unhandled exception.

## Caching (`API-08`, `NFR-OF-03`)
- `ICacheStore` backed by **SQLite** for response bodies + metadata (`ETag`, `Last-Modified`,
  fetched-at timestamp). Persists across restarts.
- **Conditional requests:** send `If-None-Match` / `If-Modified-Since`; on `304 Not Modified`, serve the
  cached body and refresh the timestamp.
- **Get-or-fetch policy:** try network → on success update cache → on failure/timeout serve cache.
  Always carry the cache's fetched-at timestamp to the UI.

## Offline & staleness (`NFR-OF-01`, `NFR-OF-02`)
- Every content surface shows a **"last updated"** time and a **stale badge** when older than a
  configurable threshold. Reuse cache metadata - do not build a second timestamp source.
- After one successful fetch, all features must render from cache with no network.

## Environment configuration (`DV-03`)
- `local` / `staging` / `prod` base URLs are configurable **without code changes** (config file +
  secure storage; no recompilation).
- Android emulator reaches the host at `http://10.0.2.2:<port>` instead of `localhost`; ship this in the
  local defaults.
- Per-masjid endpoint URLs come from the config document (`API-06`) via `IMasjidContext`, not hard-coded.

## Testing (`QA-03`)
- Unit-test: backoff/retry on 5xx+timeout then cache fallback; `304` handling; timeout; tolerant parse
  of malformed JSON; cache survives restart.
- Contract-test against the mock JSON in [mock/](../../../mock) so payload-shape drift fails CI.
