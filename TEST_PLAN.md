# ProConnect — End-to-End Test Case Strategy (A→Z)

Status: **living document**. Written before execution; results of each run get appended to
`TEST_RUN_LOG.md`, not edited into this file, so the plan and the evidence stay separate.

## 1. Scope

Every feature currently shipped (Phases 0–7): registration/sign-up, authentication, profile,
match criteria, matching/swiping, messaging (touched only incidentally — not a named target but
sits behind matches so a couple of cases exercise it), notifications (in-app + push), and
subscriptions/payments. Two passes:

1. **Single-user functional pass** — walk one primary test identity through every endpoint,
   happy and unhappy path, verifying each step against the running API's own logs
   (`ProConnect.WebApi/Logs/proconnect-*.txt` + console) and a direct Postgres read — not just the
   HTTP status code.
2. **Bulk/scale pass** — repeat the funnel across many concurrent users to surface
   concurrency bugs, cache-stampede issues, N+1s, and anything that only shows up under load.

## 2. Environment

- API: `dotnet run` against `ProConnect.WebApi`, `ASPNETCORE_ENVIRONMENT=Development`, HTTP on
  `localhost:5109` (no HTTPS/launchBrowser — scripted via curl, not Swagger).
- Postgres (`proconnect-postgres`, PostGIS) and Redis (`proconnect-redis`) — already running
  Docker containers, reused as-is.
- Verification channels per test case: (a) HTTP response body/status, (b) `docker exec
  proconnect-postgres psql` read of the relevant table(s), (c) `grep`/tail of the Serilog output
  for the matching log line (request-in, any `LogError`/`LogWarning`, the specific
  `_logger.LogInformation` lines each service emits).

## 3. Test-data strategy — what's real vs. faked

| Dependency | Status this environment | Strategy |
|---|---|---|
| **Apify/HarvestApi** (LinkedIn profile scrape) | **Real, configured** (`appsettings.json` ships a live token; ~$0.004/call, 7-day cache per `public_identifier`) | Use **real public LinkedIn profiles** for sign-up — this is the app's actual intended function, not a mock. Reuse the same small set of identifiers across runs so the 7-day cache keeps repeat runs free. Keep the list short (≈6–8 distinct identifiers total across *both* passes) to keep real spend negligible (<$0.05 total) and avoid hammering a third party's API. |
| **Stripe** (payments) | **Unconfigured** (`StripeConfig.secretkey`/`webhooksecret` blank — no Stripe account) | Per your instruction: **fake it**. `POST /subscription/v1/checkout` itself is expected to fail cleanly (dark dependency — see §5.6 unhappy path). The actual subscription-state changes are driven the same way Phase 6/7 were originally verified: hand-crafted, HMAC-signed `checkout.session.completed` / `invoice.payment_succeeded` / `invoice.payment_failed` / `customer.subscription.deleted` JSON payloads POSTed straight to `/subscription/v1/webhook`, using a **throwaway, test-only webhook secret** set via `dotnet user-secrets` for the duration of the run and removed afterward (identical pattern to the prior Phase 6 live-test, nothing committed, nothing real). This exercises the exact same code path (`SubscriptionService.HandleWebhook` → `IFeatureGateService` → `INotificationService`) a real Stripe account would. |
| **FCM push** (`PushConfig.serviceaccountjson`) | **Unconfigured** (dark by design — no Firebase project yet) | Expected to fail-and-swallow inside `FcmPushNotificationGateway`/`Notify`. Test asserts the **in-app** `notification` row is still created and the failure is logged, never thrown to the caller. |

## 4. Entry criteria

- Both containers healthy (`docker ps`).
- `dotnet user-secrets list` shows only `JwtConfig:*` before we start (clean baseline) and only
  `JwtConfig:*` again after cleanup.
- App builds and starts without exceptions; `GET /api/user/v1/ping` returns 200.

## 5. Test case catalog

ID scheme: `<AREA>-<NN>`. **H** = happy path, **U** = unhappy path. Every case states
Preconditions → Steps → Expected result → Log/DB evidence to check.

### 5.1 Registration (SIGNUP)

| ID | Type | Case |
|---|---|---|
| SIGNUP-01 | H | `POST /api/user/v1/signup` with a real `public_identifier`, valid email, `birth_date` ≥18y ago → 200, body has `token`, `userid`; profile fields populated from the real scrape. DB: `users` row exists, `is_active=true`. Log: no errors; `WarmRecommendations` fire-and-forget log line. |
| SIGNUP-02 | H | Re-signup same `public_identifier` (second distinct user record, LinkedIn allows re-scrape) within 7 days → served from cache (no second Apify spend); log shows cache hit path (no `HarvestApiProfileProvider` debug line on 2nd call), still 200. |
| SIGNUP-03 | U | Missing `public_identifier` → FluentValidation error, `VALIDATION` errorcode, 4xx — not a 500/crash. |
| SIGNUP-04 | U | `birth_date` <18 years ago → `NOTADULT` errorcode, 4xx. |
| SIGNUP-05 | U | Malformed `birth_date` (e.g. month=13) → `INVALIDDATEFORMAT` errorcode. |
| SIGNUP-06 | U | Invalid `email` format → `INVALIDEMAILADDRESS` errorcode. |
| SIGNUP-07 | U | Literal empty/`null` JSON body → clean `"Request body is required."` response, not an unhandled exception (regression guard — this was a real historical bug per `PROJECT_NOTES.md`). |
| SIGNUP-08 | U | Nonsense `public_identifier` (no such LinkedIn profile) → `InvalidOperationException` path surfaces as a clean error response (not a raw 500 stack trace to the client), logged via `_logger.LogError`. |

### 5.2 Authentication (AUTH)

| ID | Type | Case |
|---|---|---|
| AUTH-01 | H | `GET /api/user/v1/systemauthentication` with the JWT from SIGNUP-01 → 200, returns current profile, confirms "/me" works. |
| AUTH-02 | U | Same endpoint with **no** `Authorization` header → 401. |
| AUTH-03 | U | Same endpoint with a garbage/expired/tampered JWT → 401. |
| AUTH-04 | H | `GET /api/account/v1/profile` (different controller, same global auth policy) with valid token → 200, matches signup data. |
| AUTH-05 | U | `GET /api/account/v1/profile` with no token → 401 — confirms the **global fallback auth policy** (no per-endpoint `[Authorize]` needed) actually covers a controller that never references it explicitly. |
| AUTH-06 | H | `POST /api/user/v1/logout` → 200 (stateless no-op, documented as such — confirm it doesn't itself require special handling). |
| AUTH-07 | H | `GET /api/user/v1/ping` and `GET /api/user/v1/generateconsentlinkedinloginpage` with **no** token → 200 (confirms `[AllowAnonymous]` allow-list is intact and the auth-default flip documented in `PROJECT_NOTES.md` 2026-08-13 hasn't regressed). |
| AUTH-08 | U | `GET /api/user/v1/linkedinauthentication?code=bad&state=bad` (real LinkedIn OAuth call, will fail against LinkedIn) → clean `issuccessfull=false` error body, not a crash — exercises the "LinkedIn rejected it" branch without needing a real OAuth consent round-trip. |

### 5.3 Profile & defining criteria (PROFILE / CRITERIA)

| ID | Type | Case |
|---|---|---|
| PROFILE-01 | H | `PUT /api/account/v1/profile` updates `headline`/`city`/`summary` → 200, echo reflects new values. DB: `users` row updated; geocode re-run (`latitude`/`longitude` populated/changed) if city changed. |
| PROFILE-02 | U | `PUT` with null body → clean handled response, not a crash (mirrors the historical `UpdateProfile` null-body bug fixed for `SignUp`). |
| PROFILE-03 | H | `DELETE /api/account/v1/profile` (DeactivateAccount — check actual verb/route) → `is_active=false`; subsequent `GetProfile`/matching treats the user as gone. |
| PROFILE-04 | H | `GET /api/match/v1/criteria` before ever setting one → 200 with defaults derived from own profile (no row yet), per `MatchCriteriaRequest` doc comment "all fields optional". |
| CRITERIA-01 | H | `PUT /api/match/v1/criteria` with `city`/`industry`/`min_age`/`max_age`/`interests` → 200; DB upsert (insert first time) confirmed by `GET` round-trip. |
| CRITERIA-02 | H | `PUT` again with different values → **update**, not a duplicate row (regression guard per `PROJECT_NOTES.md` "insert then update, not duplicate" decision). |
| CRITERIA-03 | H | `PUT` with `radius_km` set, as a **Free**-plan user → criteria still saves (storage isn't gated), but `radius_search_enabled` is a Plus/Premium feature — verify `GetRecommendations`/`Search` actually ignores/falls back when the plan doesn't allow it (cross-check against §5.6 feature gating). |
| CRITERIA-04 | U | `PUT` with `min_age` > `max_age` or an absurd value (e.g. negative) → confirm current behavior (no validator exists for this request type per code read — this case documents whatever the real behavior is, flags it as a gap if it silently accepts nonsense). |

### 5.4 Finding matches (MATCH)

Needs **two** real signed-up users (matching is inherently two-sided) — call them **User A**
(primary single-user-pass subject) and **User B** (counterpart, signed up once, reused as the
other side of every pairwise case).

| ID | Type | Case |
|---|---|---|
| MATCH-01 | H | `GET /api/match/v1/recommendations` as A → 200, paged list, excludes self, excludes already-swiped. |
| MATCH-02 | H | `POST /api/match/v1/like?targetUserId=B` as A (one-sided) → 200, no match yet. DB: `matches` row with pending/one-sided state (per schema) — check actual column semantics. Notification: **NewLike** fires for B (generic content, no identity — privacy-by-design per `PROJECT_NOTES.md`). |
| MATCH-03 | H | `POST /api/match/v1/like?targetUserId=A` as B (reciprocal) → mutual match created. Notification: **NewMatch** fires for **both** A and B, with real name/`matchid` this time (identity already revealed by matching). |
| MATCH-04 | H | `GET /api/match/v1/matches` as A and as B → both see the new match. |
| MATCH-05 | H | `POST /api/match/v1/pass?targetUserId=<someone else>` → recorded, that candidate never resurfaces in `GetRecommendations`. |
| MATCH-06 | H | `GET /api/match/v1/incomingLikes` as B *before* reciprocating (i.e. redo with a third user C who likes B but B hasn't liked back) → gated by `who_liked_me_visible` (Plus/Premium only) — on Free, expect empty/blocked; confirms the paywall. |
| MATCH-07 | H | `DELETE /api/match/v1/unmatch?matchId=...` → match removed; `GetMatches` no longer shows it for either side. |
| MATCH-08 | H | `POST /api/match/v1/block?targetUserId=B` → B no longer appears in A's recommendations; attempt by B to message/like A post-block is rejected (check actual enforcement). |
| MATCH-09 | U | `POST /api/match/v1/like?targetUserId=<self>` → rejected, not a self-match. |
| MATCH-10 | U | `POST /api/match/v1/like?targetUserId=<nonexistent-guid>` → clean 4xx, not a crash. |
| MATCH-11 | U | Free-plan user likes past the **daily_likes = 10** cap → 11th like rejected with a clear "limit reached" response, not silently accepted and not a 500. This is the key `IFeatureGateService.TryConsume` gating case. |
| MATCH-12 | H | Same user upgraded to Plus (via faked webhook, §5.6) → cap now 50; previously-rejected like now succeeds (cache invalidation on plan change actually takes effect, not stuck on the old `EffectivePlan` cache entry). |
| MATCH-13 | H | `GET /api/match/v1/seenProfiles` → reflects everyone swiped on so far. |

### 5.5 Notifications (NOTIF)

| ID | Type | Case |
|---|---|---|
| NOTIF-01 | H | `GET /api/notification/v1` as the user who received NewLike/NewMatch from §5.4 → both rows present, correct `type`, correct (privacy-respecting) content. |
| NOTIF-02 | H | `GET /api/notification/v1/unreadcount` → matches count of unread rows. |
| NOTIF-03 | H | `POST /api/notification/v1/markasread?notificationid=...` → that row flips to read; unread count decrements. |
| NOTIF-04 | H | `POST /api/notification/v1/markallasread` → all flip; unread count → 0. |
| NOTIF-05 | H | `POST /api/notification/v1/devices` registers a `device_token` (Web/iOS/Android) → `user_device_token` row created/upserted. |
| NOTIF-06 | H | Re-register the **same** `device_token` for the **same** user → no-op upsert (just `last_seen_at_utc` bump), not a duplicate row. |
| NOTIF-07 | H | Re-register the same `device_token` for a **different** user → re-points to the new user (documented anti-leak behavior) — confirm old owner no longer resolves to that token. |
| NOTIF-08 | H | `DELETE /api/notification/v1/devices?device_token=...` → row removed. |
| NOTIF-09 | H (push dark) | Trigger any notification (e.g. a fresh Like) while a device token is registered → in-app row **always** created; push send attempt logged as a swallowed failure (no `serviceaccountjson` configured) — confirms the never-throws `Notify` contract end-to-end, not just in isolation. |
| NOTIF-10 | U | `POST /api/notification/v1/markasread?notificationid=<not-mine>` → rejected/no-op, can't mark someone else's notification read. |

### 5.6 Subscriptions & payments — Stripe faked (SUB)

| ID | Type | Case |
|---|---|---|
| SUB-01 | H | `GET /api/subscription/v1/plans` → 200, returns FREE/PLUS/PREMIUM from `subscription_plan`, matches DB. |
| SUB-02 | H | `GET /api/subscription/v1/mysubscription` for a brand-new user with no row → resolves to **FREE** fallback, not an error. |
| SUB-03 | U (dark dependency) | `POST /api/subscription/v1/checkout` with a valid `plan_code` → expected to fail cleanly (blank Stripe secret key) — document the actual status code/message; must not be a raw unhandled-exception page. This is the honest "Stripe isn't connected yet" behavior, asserted on purpose. |
| SUB-04 | U (dark dependency) | `POST /api/subscription/v1/purchaseConsumable` similarly → same class of clean failure. |
| SUB-05 | H (faked) | Hand-sign a `checkout.session.completed` event (`client_reference_id`/`metadata.userid` = test user, `plan_code=PLUS`) with a throwaway test webhook secret → `POST /subscription/v1/webhook` → 200. DB: `user_subscription` row Active, correct plan. Notification: **SubscriptionActivated** fires. `EffectivePlan` cache invalidated (verified by MATCH-12 above actually seeing the new limit immediately). |
| SUB-06 | H (faked) | Hand-sign `invoice.payment_succeeded` for that subscription → **SubscriptionRenewed** fires; status stays Active. |
| SUB-07 | H (faked) | Hand-sign `invoice.payment_failed` → **PaymentFailed** fires; `user_subscription.status` → PastDue. Confirm a PastDue user is treated as Free-equivalent by `IFeatureGateService` (per enum doc comment). |
| SUB-08 | H (faked) | Hand-sign `customer.subscription.deleted` → **SubscriptionCanceled** fires; status → Canceled; plan reverts to FREE gating. |
| SUB-09 | U | Replay the **same** webhook event id twice → confirm idempotent handling (no duplicate notification / no double-counted transaction) — check whether this is actually guarded in code; flag as a gap if not. |
| SUB-10 | U | POST to `/webhook` with a **bad/missing** `Stripe-Signature` header → rejected (400), proving the signature check isn't accidentally bypassed just because we're using a test secret. |
| SUB-11 | H | `GET /api/subscription/v1/transactions` → reflects the faked events that created `payment_transaction` rows (if `HandleWebhook` persists transactions for these event types — confirm from code during execution, not assumed). |
| SUB-12 | U (dark dependency, informational) | `POST /api/subscription/v1/cancelSubscription` on a subscription that only ever existed via faked webhooks (no real Stripe subscription id, or one Stripe has never heard of) → expected to fail calling out to the real Stripe API; document actual behavior, same honesty as SUB-03. |

## 6. Single-user pass — execution order

Run strictly in this order so each step's preconditions are satisfied by the previous one's
output (token, userid, matchid, notificationid are threaded through):

SIGNUP-01 → AUTH-01…08 → PROFILE-01…04 → CRITERIA-01…04 → *(sign up User B)* → MATCH-01…13 →
NOTIF-01…10 → SUB-01…12 → SIGNUP-02..08 (negative signups last, so they don't pollute the primary
identity) → cleanup (deactivate/delete test users, remove throwaway webhook secret, delete test
`.json` payloads from `/tmp`).

After every single case: tail the Serilog file for that request's correlation/timestamp window
and confirm (a) no unexpected `LogError`/stack trace, (b) the specific info-level line the service
is documented to emit, (c) a direct `psql` read of the row(s) the case claims changed.

## 7. Bulk / scale pass

**Goal:** stress the app's *own* logic (matching, swiping, notification fan-out, feature gating,
caching) at a scale where concurrency bugs/cache races would actually show up — not re-hammer the
paid external scraper.

- **Population:** ~20 distinct real signed-up users (reusing the same handful of real
  `public_identifier`s from §3 where possible/cached, plus a few more distinct real profiles to
  get real variety in city/industry for match scoring) — kept to 20 to keep Apify spend trivial
  (worst case 20 × $0.004 ≈ $0.08) while giving C(20,2)=190 possible pairs, enough to generate a
  non-trivial number of real matches.
- **Load generator:** a small script (bash/curl or a short Python script using `asyncio`/
  `httpx`) that, per user: sets criteria, fetches recommendations, likes/passes a randomized
  subset of the other 19 concurrently across all 20 users (so reciprocal likes/matches happen in
  real overlapping time, the actual race condition that matters for `MatchService.Swipe`'s
  idempotency guarantees), fetches notifications, and (for ~5 of the 20) gets upgraded via a faked
  Plus webhook mid-run to confirm cache invalidation holds up under concurrent traffic.
- **Pass/fail bar:** zero `LogError`-level entries attributable to our traffic; no duplicate
  `matches` rows for the same pair in either direction; `daily_likes` consumption count matches
  actual accepted likes per user (no over/under counting from a race); no notification
  duplicated/dropped for a given triggering event; p95/p99 latency noted (informational — no hard
  SLA defined yet) per endpoint from the Serilog request-logging middleware's own timing field.
- **Cleanup:** same as §6, scaled to all N test users, plus a final `psql` row-count check that
  nothing bulk-test-related survives.

## 8. Reporting

Every run (single-user and bulk) gets a dated entry appended to `TEST_RUN_LOG.md`: which case
IDs passed/failed/were-gap-flagged, the concrete evidence (log excerpt or query result) per
failure, and any follow-up fix needed. `PROJECT_NOTES.md` gets a short changelog line pointing at
that log once a full pass is green, consistent with how every other phase in this project has
been documented.
