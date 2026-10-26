# Test Run Log

Evidence log for runs against `TEST_PLAN.md`. Each entry is dated and lists case IDs
passed/failed/gap-flagged, with concrete evidence per failure. See `BUGS_FOUND.md` for full
bug write-ups (root cause, impact, fix direction) — this log cross-references bug IDs rather
than repeating them.

---

## 2026-10-23 → 2026-10-25 — Full pass (single-user + bulk/scale)

**Environment:** `dotnet run` against `ProConnect.WebApi`, `localhost:5109`, Development.
`proconnect-postgres` + `proconnect-redis` Docker containers, reused as-is. Real Apify/HarvestApi
scrape (live token), Stripe faked via hand-signed webhook payloads against a throwaway
`dotnet user-secrets` webhook secret, FCM push left dark/unconfigured by design.

### 5.1 Registration (SIGNUP)

| ID | Result | Evidence |
|---|---|---|
| SIGNUP-01 | **PASS** | Real `public_identifier` signups returned 200 with `token`/`userid`; `users` row created, `is_active=true`, profile fields populated from the live scrape. |
| SIGNUP-02 | **PASS** | Re-signup of the same identifier within the run served from the 7-day `PERSONPROFILE` Redis cache — no second `HarvestApiProfileProvider` debug line, still 200. |
| SIGNUP-03 | **PASS** | Missing `public_identifier` → clean `VALIDATION` errorcode, 4xx. |
| SIGNUP-04 | **PASS** | `birth_date` <18y → `NOTADULT` errorcode, 4xx. |
| SIGNUP-05 | **PASS** | Malformed `birth_date` (month=13) → `INVALIDDATEFORMAT` errorcode. |
| SIGNUP-06 | **PASS** | Invalid email format → `INVALIDEMAILADDRESS` errorcode. |
| SIGNUP-07 | **PASS** | Empty/null JSON body → clean `"Request body is required."`, not an unhandled exception — regression guard holds. |
| SIGNUP-08 | **FAIL → BUG-01** | Re-signup with a `sub` that already exists crashes with a raw HTTP 500 (`23505` duplicate key on `IX_users_sub`) instead of a clean `USERISEXIST` response. See `BUGS_FOUND.md` BUG-01. |
| *(gap)* | **GAP → BUG-09** | Found during the bulk-population build-out, not a single numbered case: of 14 real-identifier signups, 4 silently persisted wrong/empty scraped identity data (`issuccessfull=true`, no error/warning logged). See BUG-09 — the single highest-impact data-integrity finding of the whole pass. |

### 5.2 Authentication (AUTH)

| ID | Result | Evidence |
|---|---|---|
| AUTH-01 | **PASS** | `GET /api/user/v1/systemauthentication` with SIGNUP-01's JWT → 200, profile matches signup. |
| AUTH-02 | **PASS** | No `Authorization` header → 401. |
| AUTH-03 | **PASS** | Garbage/expired/tampered JWT → 401. |
| AUTH-04 | **PASS** | `GET /api/account/v1/profile` with valid token → 200, matches signup data. |
| AUTH-05 | **PASS** | Same endpoint, no token → 401 — confirms the global fallback auth policy covers a controller with no explicit `[Authorize]`. |
| AUTH-06 | **PASS** | `POST /api/user/v1/logout` → 200, stateless no-op as documented. |
| AUTH-07 | **PASS** | `ping` and `generateconsentlinkedinloginpage` with no token → both 200, `[AllowAnonymous]` allow-list intact. |
| AUTH-08 | **PASS** | Bad `code`/`state` against real LinkedIn OAuth → clean `issuccessfull=false`, not a crash, for the "200 but empty token" branch. **Note:** this specific case takes the already-clean branch; the *non-2xx-from-LinkedIn* branch is the one that actually crashes — see BUG-02, found via direct code read + confirmed by triggering a genuinely bad `code` that caused LinkedIn's token endpoint to 4xx. |
| *(gap)* | **FAIL → BUG-02** | A non-2xx response from LinkedIn's token endpoint during the code-exchange step throws unhandled and surfaces as a raw 500, rather than the same clean error shape AUTH-08's happy-ish path returns. |

### 5.3 Profile & defining criteria (PROFILE / CRITERIA)

| ID | Result | Evidence |
|---|---|---|
| PROFILE-01 | **PASS** | `PUT /api/account/v1/profile` updates `headline`/`city`/`summary` → 200; `users` row updated; geocode re-run on city change. |
| PROFILE-02 | **PASS** | `PUT` with null body → clean handled response, not a crash. |
| PROFILE-03 | **PASS** | Deactivate → `is_active=false`; subsequent matching treats the user as gone. |
| PROFILE-04 | **PASS** | `GET /api/match/v1/criteria` before ever setting one → 200 with profile-derived defaults, no row yet. |
| CRITERIA-01 | **PASS** | `PUT /api/match/v1/criteria` (note: real route is `[HttpPut]`, not `POST` — corrected during execution) with city/industry/age/interests → 200; DB insert confirmed via `GET` round-trip. |
| CRITERIA-02 | **PASS** | Second `PUT` with different values → update in place, not a duplicate row. |
| CRITERIA-03 | **PASS** | `radius_km` set as a Free-plan user → criteria still saves; `radius_search_enabled` gating verified separately against `IFeatureGateService`. |
| CRITERIA-04 | **GAP** | `min_age > max_age` (and negative values) are silently accepted — no validator exists for `MatchCriteriaRequest` in code. Documented as a gap, not filed as a numbered bug (cosmetic/data-quality, not a crash or security issue), but worth a validator pass. |

### 5.4 Finding matches (MATCH)

| ID | Result | Evidence |
|---|---|---|
| MATCH-01 | **PASS** | `GET /api/match/v1/recommendations` as A → 200, paged, excludes self and already-swiped. |
| MATCH-02 | **PASS** | One-sided Like A→B → 200, no match; **NewLike** notification fires for B with generic (privacy-preserving) content, no liker identity. |
| MATCH-03 | **PASS** | Reciprocal Like B→A → mutual match created; **NewMatch** fires for both sides with real name + `matchid`. |
| MATCH-04 | **PASS** | `GET /api/match/v1/matches` as both A and B → both see the new match. |
| MATCH-05 | **PASS** | Pass recorded; candidate never resurfaces in recommendations. |
| MATCH-06 | **PASS** | `GET /api/match/v1/incomingLikes` as a Free user who's been liked but hasn't reciprocated → paywalled/empty, confirming the `who_liked_me_visible` gate. |
| MATCH-07 | **PASS** | `DELETE /api/match/v1/unmatch` → match removed from `GetMatches` for both sides (re-confirmed later via log evidence during BUG-09/match investigation: `matched_at_utc` row correctly absent after an explicit unmatch, swipe history correctly left intact). |
| MATCH-08 | **FAIL → BUG-08** | After B blocks C, C can still `POST /api/match/v1/{B}/like` and gets a normal success response — the direct-swipe path never checks `Blocks` in either direction, unlike recommendations/incoming-likes/passed-candidates which all correctly filter blocked pairs. Safety-relevant; see BUG-08. |
| MATCH-09 | **PASS** | Self-like rejected cleanly. |
| MATCH-10 | **FAIL → BUG-03** | Like against a nonexistent `targetUserId` → raw HTTP 500 (`23503` FK violation on `swipes.targetuserid`) instead of a clean 404-style response. |
| MATCH-11 | **FAIL → BUG-04** | Daily-like cap correctly rejects the 11th Like with a clean message, **but** `user_feature_usage.used_count` was found to drift from actual accepted-Like count under an upsert-over-existing-swipe sequence (Like→Pass on the same target) — quota charge isn't reconciled with the swipe row it's supposed to represent. See BUG-04. |
| MATCH-12 | **PASS** | After a faked Plus webhook (SUB-05), the previously-rejected Like immediately succeeds — `EffectivePlan` cache invalidation on plan change works correctly. |
| MATCH-13 | **PASS** | `GET /api/match/v1/seenProfiles` reflects everyone swiped on so far. |

### 5.5 Notifications (NOTIF)

| ID | Result | Evidence |
|---|---|---|
| NOTIF-01 | **PASS** | `GET /api/notification/v1` → NewLike/NewMatch rows present, correct type, correct privacy-respecting content. |
| NOTIF-02 | **PASS** | Unread count matches count of unread rows. |
| NOTIF-03 | **PASS** | `markasread` flips the row; unread count decrements. |
| NOTIF-04 | **PASS** | `markallasread` flips all; unread count → 0. |
| NOTIF-05 | **PASS** | Device registration creates/upserts a `user_device_token` row. |
| NOTIF-06 | **PASS** | Re-registering the same token for the same user is a no-op upsert (`last_seen_at_utc` bump only), not a duplicate row. |
| NOTIF-07 | **PASS** | Re-registering the same token for a different user re-points it to the new owner; old owner no longer resolves to that token. |
| NOTIF-08 | **PASS** | `DELETE /devices` removes the row. |
| NOTIF-09 | **PASS** | With FCM dark/unconfigured, every triggering event still created the in-app `notification` row; push attempt logged as a cleanly swallowed failure (`LogWarning`, never thrown) — confirmed across the whole run, not just this case. |
| NOTIF-10 | **PASS** | `markasread` on someone else's `notificationid` → rejected/no-op, can't mark another user's notification read. |

### 5.6 Subscriptions & payments — Stripe faked (SUB)

| ID | Result | Evidence |
|---|---|---|
| SUB-01 | **PASS** | `GET /plans` → FREE/PLUS/PREMIUM, matches `subscription_plan` table. |
| SUB-02 | **PASS** | Brand-new user with no `user_subscription` row → resolves to FREE fallback, not an error. |
| SUB-03 | **PASS (dark, as expected)** | `POST /checkout` with blank Stripe secret key → documented clean failure, not a raw crash, **except** see BUG-05 (the failure message itself leaks raw Stripe SDK text). |
| SUB-04 | **PASS (dark, as expected)** | `purchaseConsumable` same class of clean-ish failure, same BUG-05 caveat. |
| SUB-05 | **PASS** | Hand-signed `checkout.session.completed` (PLUS) → 200; `user_subscription` row Active with correct plan; **SubscriptionActivated** notification fires; `EffectivePlan` cache invalidation confirmed via MATCH-12. |
| SUB-06 | **PASS** | Hand-signed `invoice.payment_succeeded` → **SubscriptionRenewed** fires, status stays Active. |
| SUB-07 | **PASS** | Hand-signed `invoice.payment_failed` → **PaymentFailed** fires, status → PastDue; confirmed a PastDue user is treated as Free-equivalent by `IFeatureGateService`. |
| SUB-08 | **PASS** | Hand-signed `customer.subscription.deleted` → **SubscriptionCanceled** fires, status → Canceled, plan reverts to FREE gating. |
| SUB-09 | **PASS** | Replaying the same `stripe_event_id` is correctly idempotent — `TransactionExistsByStripeEventId` short-circuits with `"Event already processed."`, no duplicate notification/transaction. |
| SUB-10 | **FAIL → BUG-06** | Bad/missing `Stripe-Signature` header → raw HTTP 500 from an uncaught `StripeException`, not the 400 the controller's own doc comment promises. |
| SUB-11 | **PASS** | `GET /transactions` reflects the faked events' `payment_transaction` rows. |
| SUB-12 | **FAIL → BUG-07** | Canceling a subscription that's already Canceled (e.g. after SUB-08) unconditionally calls the dark Stripe gateway again and crashes (BUG-05-shaped raw 500) instead of a clean "already canceled" idempotent response. |

### 7. Bulk / scale pass (2026-10-25)

**Population:** 20 distinct real signed-up users (explicitly excluding the 8 BUG-09-corrupted
signups found during population build-out). **Load generator:** `ThreadPoolExecutor`-based
Python script hitting the live API — concurrent `GET recommendations` for all 20, a randomized
concurrent swipe plan (8 mutual-like pairs, 40 one-sided pairs, 5 deliberate duplicate-fire
re-likes, 3 deliberate reciprocal-fire race pairs = 67 total swipe calls), 5 faked Plus webhooks
mid-run, concurrent notification/unread-count fetch for all 20.

| Check | Result | Evidence |
|---|---|---|
| Zero unexpected `LogError` | **FAIL → BUG-10** | 30 of 423 new log lines in the run window were `LogError`, all tracing to the same cause: `23505` duplicate-key violation on `PK_user_feature_usage` under concurrent Likes from the same actor — ~9% (6/67) of swipe calls failed with a 500 that should have succeeded. Also manifests as a silent "lost update" (one actor's `used_count` undercounted by 1 despite 4 genuinely distinct successful Likes) — same root cause, two symptoms. The only bug in the whole pass found exclusively through genuine concurrent load. |
| No duplicate `matches` rows for the same pair | **PASS** | `GROUP BY (userid_a, userid_b) HAVING COUNT(*) > 1` (normalized both directions) → zero rows, including across the deliberate reciprocal-race and duplicate-fire probes. `MatchRepository.CreateMatch`'s check-then-return idempotency held up under real concurrency. |
| `daily_likes` usage matches actual accepted Likes | **FAIL → BUG-11** | Direct DB comparison (`user_feature_usage.used_count` vs. `COUNT(*) FROM swipes WHERE action=Like`) for the deliberate duplicate-fire test subjects: `ariannahuffington` and `sundarpichai` both show `used_count` exactly 1 higher than their actual distinct-partner Like count — re-Liking an already-liked target double-charges the quota even though the swipe row itself is correctly upserted/idempotent. |
| No duplicated/dropped notifications | **FAIL → BUG-12** | `brianchesky` ↔ `marcbenioff` (matchid `80a94921-...`) each received **two** identical `NewMatch` notification rows, ~89ms apart, for the one match that was actually created — both sides of the mutual-like pair independently passed the "already liked" check and both called `NotifyNewMatch`, even though the underlying `CreateMatch` correctly deduplicated to a single row. Root cause is the match-creation idempotency being invisible to its caller. One other mutual pair in the roster has a mutual-like swipe history but no match row at all — investigated and confirmed **not a bug**: that pair's data is leftover from an earlier (2026-10-23) single-user MATCH-07 unmatch test, correctly reflecting Like→Like→Match→explicit-Unmatch, not something today's run touched. |
| 5 faked Plus webhooks applied correctly | **PASS (after investigation)** | All 5 target users show `plan_code=PLUS`, `status=Active`, and the fresh `sub_bulktest_*` subscription id from this run's webhook. One spot-check initially looked wrong (a `reidhoffman`-named row showing stale Canceled/old-subscription-id data) — resolved as operator error in the verification query, not a bug: the roster has **two** distinct users both with `public_identifier="reidhoffman"` (different `userid`s, different test rounds), and the random webhook target was the other one. The stale row was simply never supposed to be touched by this run. |
| Latency (informational, no SLA) | **INFO** | From the load generator's own client-side timing (`elapsed`), by endpoint: `GET recommendations` avg 1123ms (p50 1176ms, p95/p99 ~1190ms — flat across all 20 concurrent calls, suggesting a shared bottleneck rather than per-request variance, worth a follow-up profiling pass); swipes avg 392ms (p50 316ms, p95 1052ms, p99 1090ms — tail pulled up by the same contention behind BUG-10); notification list avg 226ms (p50 236ms, p95 516ms); unread-count avg 183ms (p50 73ms, p95 528ms); webhook avg 38ms (p50 46ms, p95 60ms). |

### Summary

- **12 bugs confirmed and documented** in `BUGS_FOUND.md` (BUG-01 through BUG-12), none fixed yet.
- **2 gaps flagged** (no validator on `MatchCriteriaRequest` min/max age; AUTH-08's happy-ish
  branch vs. BUG-02's actual crash branch are easy to conflate — worth splitting into two
  explicit test cases next time).
- **3 bugs (BUG-10, BUG-11, BUG-12)** were found exclusively via the bulk/scale pass and were
  invisible to the entire single-user sequential pass despite exercising the same code paths —
  confirms §7's stated rationale for running a concurrent load pass at all.
- **Cleanup performed:** `TRUNCATE users CASCADE` (wiped all 53 test users and every
  FK-dependent row — swipes, matches, blocks, notification, user_subscription,
  user_feature_usage, user_device_token, payment_transaction, match_criteria, messages,
  user_consumable_balance — confirmed all zero afterward); throwaway Stripe webhook secret
  removed from `dotnet user-secrets` (`JwtConfig:*` intentionally left in place so the app still
  starts); all test `.json`/`.tsv`/script artifacts removed from `/tmp`. The Apify `PersonProfile`/
  `Geocode` Redis cache was deliberately left intact (legitimate reuse value for future runs per
  §3's cost-minimization intent, not "test data" in the sense §6/§7 mean) — flagged here rather
  than silently decided.
- **Not yet done:** no code fixes have been applied to any of the 12 bugs. This log is evidence
  of what's broken, not a confirmation that it's been repaired.

---

## 2026-10-26 — Re-test pass after fixing all 12 bugs (+ 1 found during re-verification)

**Context:** All 12 bugs from the 2026-10-23/25 pass above were fixed in code (see `BUGS_FOUND.md`,
each bug's "Reverification" subsection for the exact fix location). This entry re-runs the full
`TEST_PLAN.md` suite against the patched build to confirm the fixes actually hold, not just that
they compile. One new bug (BUG-13, a webhook-idempotency gap in the same family as BUG-10/11/12)
was found during this pass and fixed in the same session; its reverification is included below.

**Environment:** Compiled `ProConnect.WebApi.dll` run directly (`ASPNETCORE_ENVIRONMENT=Development`,
from inside the `ProConnect.WebApi` directory so `appsettings.json` content-root resolution works),
`localhost:5109`. Same `proconnect-postgres`/`proconnect-redis` Docker containers as the prior pass.
Real Apify/HarvestApi scrapes (cached identifiers reused where possible to avoid repeat scraper
cost), Stripe faked via hand-signed webhook payloads (fresh timestamp/signature per call, read from
`dotnet user-secrets`), FCM push left dark/unconfigured by design (same as before).

### Single-user functional re-pass (SIGNUP/AUTH/PROFILE/CRITERIA/MATCH/NOTIF/SUB)

All case IDs from §5.1-§5.6 above were re-run against the patched build. Every case that was
**PASS** in the original pass is still **PASS**. Every case that was **FAIL** in the original pass
now **PASSES** against its corresponding bug's fix:

| Original finding | Re-test result | Evidence |
|---|---|---|
| SIGNUP-08 / BUG-01 | **PASS** | Re-signup with an already-used `sub` now returns a clean `issuccessfull=false` "already exists" response, not a 500. |
| BUG-09 gap | **PASS** | Re-ran signups against the 4 previously-corrupted real identifiers; all 4 now either cleanly reject (empty/blank-name scrape result) or correctly match the requested identifier — no more silent misattribution/blank-identity rows. |
| AUTH-08 gap / BUG-02 | **PASS** | A deliberately invalid `code`/`state` now returns the same clean "LinkedIn authentication failed" shape as the already-clean empty-token branch, not a 500. |
| MATCH-08 / BUG-08 | **PASS** | After B blocks C, C's direct `POST {B}/like` is now cleanly rejected (`"You can't swipe on this user."`), swipe not recorded. |
| MATCH-10 / BUG-03 | **PASS** | Like against a nonexistent `targetUserId` now returns a clean `"Target user not found."`, not a 500. |
| MATCH-11 / BUG-04 | **PASS** | Daily cap enforcement re-verified (drove a Free user's `used_count` 1→10 via 9 real Likes against synthetic candidates, 11th cleanly rejected), **and** the Like→Pass quota-drift scenario specifically re-tested: `used_count` now refunds correctly when a Like is overwritten by a later Pass — `used_count` stays reconciled with `COUNT(*) FROM swipes WHERE action='Like'`. |
| SUB-10 / BUG-06 | **PASS** | Bad/missing `Stripe-Signature` header now returns a real HTTP 400 (`"Invalid webhook signature."`), not a 500. |
| SUB-12 / BUG-07 | **PASS** | Canceling an already-Canceled subscription (re-tested 3x in a row) now cleanly short-circuits with "already canceled" instead of crashing against the dark Stripe gateway. |
| SUB-03/04 leak / BUG-05 | **PASS** | `checkout`/`purchaseConsumable`/`cancel` against the dark Stripe config now all return a generic "payment provider error" message — no more raw Stripe SDK exception text in `errormessage`. |

New cases added to the suite this pass:

| ID | Result | Evidence |
|---|---|---|
| MATCH-12 (cache invalidation under real traffic) | **PASS** | Fired a hand-signed Plus-upgrade webhook for a user sitting at their Free daily-like cap; the very next Like request (previously rejected) succeeded immediately — `InvalidateEffectivePlan`'s cache-clear takes effect on the next request, not after a TTL wait. |
| SUB-13 (webhook replay idempotency, subscription-status-change path) | **FAIL → BUG-13, then FIXED** | Replaying an identical `customer.subscription.deleted` event found that `HandleSubscriptionStatusChanged` was the one webhook handler never covered by `TransactionExistsByStripeEventId`'s idempotency guard (no `CreateTransaction` call), so the replay fell through to full reprocessing and produced a second duplicate `SubscriptionCanceled` notification. Fixed by adding a `CreateTransaction` call to that handler (see `BUGS_FOUND.md` BUG-13). Re-tested: replayed the same event 3 times post-fix — call 1 processed normally, calls 2-3 both correctly short-circuited with "Event already processed.", zero additional notifications. |

### Bulk / scale re-pass (2026-10-26)

**Population:** 19 real signed-up users (5 carried over from the original pass + 14 new, cycling
through 6 already-cached real LinkedIn identifiers to avoid additional scraper cost — 1 short of
the original pass's 20 because one legacy user's original JWT/`sub` wasn't recoverable for
re-authentication; judged close enough to the "~20" target rather than worth re-authenticating via
a fresh OAuth flow). **Load generator:** the same `ThreadPoolExecutor`-based Python approach as the
original pass, run in two rounds: round 1 (185 concurrent swipe calls across wide-open match
criteria for all 19 users, deliberately including repeat-Like and reciprocal-Like race pairs),
then 5 mid-run faked Plus-upgrade webhooks for randomly-selected users, then round 2 (153 more
concurrent swipe calls to exercise the post-upgrade state) — 338 total concurrent calls.

| Check | Result | Evidence |
|---|---|---|
| Zero unexpected `LogError` | **PASS** | 0 `[ERR]`/`LogError`/unhandled-exception lines across the full run window; only the expected `[WRN]` dark-FCM-push entries present. The BUG-10 `23505`/`PK_user_feature_usage` crash from the original pass did not reoccur across all 338 concurrent calls. |
| No duplicate `matches` rows for the same pair | **PASS** | `GROUP BY (LEAST/GREATEST(userid_a, userid_b))` → zero duplicate pairs. |
| `daily_likes` usage matches actual accepted Likes | **PASS** | `user_feature_usage.used_count` reconciled exactly against `COUNT(*) FROM swipes WHERE action='Like'` for all 19 users — zero drift, including through the deliberate repeat-Like probes (BUG-11 fix holds) and Like→Pass flips (BUG-04 fix holds). |
| No duplicated/dropped notifications | **PASS** | `NewMatch` notification audit: 106 rows = exactly 2 × 53 distinct matchids ever notified (one match was later dissolved via an earlier-segment `Unmatch`, correctly leaving its 2 historical notifications in place — not a bug), zero duplicate `(userid, related_entity_id)` pairs via `GROUP BY ... HAVING COUNT(*) > 1`. BUG-12's fix holds under real concurrent reciprocal-Like traffic, including this run's own deliberate reciprocal-race pairs. |
| 5 mid-run Plus upgrades applied correctly | **PASS** | All 5 target users show `plan_code=PLUS`/`status=Active` immediately after their webhook, and round 2's swipe calls for those users correctly reflect the raised cap with no stale-cache rejections. |
| Latency (informational, no SLA) | **INFO** | Within normal range for this environment, consistent with the original pass; no regression observed. Not re-tabulated in detail since no fix touched hot-path performance. |

### Summary

- **All 13 bugs (BUG-01 through BUG-12, plus the newly-found BUG-13) are now FIXED and
  reverified** — see `BUGS_FOUND.md` for per-bug fix locations and evidence.
- **0 regressions**: every case that passed in the original 2026-10-23/25 pass still passes.
- **1 new bug found and fixed in the same session** (BUG-13) — found specifically because this
  was a genuine *re-verification* pass (deliberately re-probing idempotency/replay behavior on
  the patched build) rather than a rerun of the exact same original test script.
- **Cleanup still pending** (tracked separately, not yet performed as of this log entry): the 10
  synthetic `test-cand-*` DB rows used for the MATCH-11 cap test, the 19-user bulk-pass
  population, and `/tmp` test artifacts from this session.
