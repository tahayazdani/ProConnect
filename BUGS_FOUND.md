# Bugs Found During Test Plan Execution (2026-10-23/24, re-verified 2026-10-26)

Status key: **OPEN** = confirmed, not yet fixed. **FIXED** = patched and reverified.

**Update 2026-10-26 (live API re-test pass):** All 19 bugs below are now **FIXED**. BUG-01 through
BUG-15 were fixed and reverified in the prior update; this update adds BUG-16 (webhook NRE on a
null `api_version`), BUG-17 (daily Like quota cap bypassed under concurrent requests via a
check-then-write race surviving the BUG-10 fix), BUG-18 (`AdjustConsumableBalance` lost-update/crash
race, same shape as BUG-10 but in a different table, never covered by BUG-10's own fix or
reverification), and BUG-19 (genuinely concurrent identical-`stripe_event_id` webhook deliveries
double-crediting a purchase - distinct from BUG-13's sequential-replay case), all found by actually
running the live API against real Postgres/Redis and firing genuine concurrent traffic rather than
relying on prior documentation. See each bug's "Reverification" subsection for the exact in-code
fix location and the live-test evidence confirming it holds, and `TEST_RUN_LOG.md` for the full
re-test pass this update is based on.

**Update 2026-10-26 (part 2 - full `ENDPOINT_TEST_CASES.md` execution pass):** Running every case
in `ENDPOINT_TEST_CASES.md` end-to-end against the live API (UserController, AccountController,
MatchController, MessageController, NotificationController, SubscriptionController,
SearchController) found and fixed nine further bugs, BUG-20 through BUG-28, documented below -
bringing the total to **28 bugs, all FIXED and reverified**. Several test-document-flagged "gaps"
were also live-confirmed during this pass but deliberately left as-is because they are intentional,
documented design tradeoffs rather than defects (see "Confirmed-but-not-fixed design tradeoffs"
in the Notes section below) or, in two cases (G-29/G-30), turned out to already be fixed by the
prior BUG-19 work - the test document itself is simply stale on those two, not describing a live
bug. Two factual inaccuracies in `ENDPOINT_TEST_CASES.md` itself were also found (see "Test-document
inaccuracies found" in the Notes section) and should be corrected in that document, independent of
any code change. See `TEST_RUN_LOG.md` for the dated entry covering this specific pass.

---

## BUG-01 — Duplicate signup (same `sub`) crashes with raw HTTP 500 [FIXED]

- **Where:** `UserService.SignUp` (ProConnect.Application/Service/UserService.cs, ~line 62-172)
- **Symptom:** Re-signing up with a `sub` that already exists in `users` throws
  `Npgsql.PostgresException 23505: duplicate key value violates unique constraint "IX_users_sub"`,
  which bubbles up through the generic `ExceptionHandlingMiddleware` as:
  `{"statuscode":500,"errormessage":"An error occurred while saving the entity changes. See the inner exception for details.","issuccessfull":false}`
- **Root cause:** `SignUp` has a dead comment `// check in db , if the user is NOT exist` — the
  existence check was never implemented. `Const.ErrorCode.USERISEXIST = 300` is defined but
  **never referenced anywhere in the codebase**.
- **Fix direction:** Before building a new `userid`/profile, query `_userRepository` by `sub`
  (or add a `GetBySub`/`ExistsBySub` method). If found, return a clean
  `ProConnectResponse<SignUpResponse> { issuccessfull = false, errormessage = ..., }` using
  `Const.ErrorCode.USERISEXIST`, mapped to a 409/400 by the controller — not a 500.
- **Reverification (2026-10-26):** Fixed in `UserService.SignUp`, ~lines 81-91 — a
  `_userRepository.GetBySubForAuthentication(request.sub)` existence check now runs before any
  new profile/user row is built, returning a clean `issuccessfull=false` /
  `"A user with this account already exists. Please sign in instead."` response instead of
  reaching the insert. Live-reverified by re-signing up with an already-used `sub`
  (`marcbenioff`/`USER_F`): got the clean error response, HTTP 200/`issuccessfull=false`, no 500,
  no new row inserted (`userid` unchanged). `dotnet build ProConnect.sln`: 0 errors.

## BUG-02 — Bad/expired LinkedIn OAuth `code`/`state` crashes with raw HTTP 500 [FIXED]

- **Where:** `UserService.LinkedInAuthentication` (ProConnect.Application/Service/UserService.cs, ~line 233-303)
- **Symptom:** A genuinely invalid `code` causes LinkedIn's token endpoint to return a non-2xx
  HTTP response. `_httpClientUtilities.PostAsync` throws on the non-2xx response *before*
  execution ever reaches the existing clean-error branch that checks for an empty token/`sub`.
  Result is the same generic 500 wrapper as BUG-01.
- **Root cause:** The method has a clean-error path for "200 OK but empty token/sub", but no
  try/catch (or non-throwing HTTP wrapper check) around the token-exchange call itself for the
  non-2xx case.
- **Fix direction:** Wrap the LinkedIn token-exchange call in a try/catch (or check
  `response.IsSuccessStatusCode` before parsing), and return the same clean
  "LinkedIn authentication failed" `ProConnectResponse` shape used for the empty-token case.
- **Reverification (2026-10-26):** Fixed in `UserService.LinkedInAuthentication`, ~lines 303-345
  — the token-exchange (`PostAsync<TokenResponse>`) and userinfo (`GetAsync<SignInResponse>`)
  calls are now wrapped in their own try/catch (`catch (Exception ex)`, logged as `LogWarning`
  since a bad/expired code is an expected client-driven condition, not a server fault), returning
  the same clean "LinkedIn authentication failed" `ProConnectResponse` shape already used for the
  empty-token case. Live-reverified by calling `LinkedInAuthentication` with a deliberately
  invalid `code`/`state`: got the clean error response (HTTP 200/`issuccessfull=false`), not a
  500. `dotnet build ProConnect.sln`: 0 errors.

## BUG-03 — Liking a nonexistent `targetUserId` crashes with raw HTTP 500 [FIXED]

- **Where:** `MatchService.Swipe` (ProConnect.Application/Service/MatchService.cs, ~line 324-399),
  via `MatchRepository.RecordSwipe`.
- **Symptom:** `POST /api/match/v1/{targetUserId}/like` (or `/pass`) with a `targetUserId` that
  doesn't exist in `users` throws
  `Npgsql.PostgresException 23503: ... violates foreign key constraint "FK_swipes_users_targetuserid"`,
  surfaced as the same generic 500.
- **Root cause:** `Swipe` validates `targetUserId == Guid.Empty` and `targetUserId == userid`,
  but never checks that the target actually exists before calling `RecordSwipe`.
- **Fix direction:** Add an existence check (`_userRepository.GetById(targetUserId)` or a
  lighter `ExistsById`) before the daily-like-cap check / `RecordSwipe`, and return a clean
  "Target user not found" 404-style `ProConnectResponse` if missing.
- **Reverification (2026-10-26):** Fixed in `MatchService.Swipe`, ~line 344-356 — a
  `_userRepository.GetById(targetUserId)` existence check (same `is_active`-filtered lookup used
  by `GetCandidates`) now runs before `RecordSwipe`, returning a clean `"Target user not found."`
  response on a miss. Live-reverified by Liking/Passing a random nonexistent `Guid`: got the
  clean error response, not the FK-violation 500. `dotnet build ProConnect.sln`: 0 errors.

## BUG-04 — Daily-like quota consumption is not reconciled with the swipe upsert [FIXED] — most important, billing-adjacent

- **Where:** `MatchService.Swipe` (~line 347-361) + `MatchRepository.RecordSwipe`
  (ProConnect.Infrastructure/Repository/MatchRepository.cs, ~line 174-189) +
  `FeatureGateService.TryConsume` (ProConnect.Application/Service/FeatureGateService.cs).
- **Symptom (reproduced and confirmed via DB):** For test User A, `user_feature_usage` shows
  `daily_likes used_count = 10`, but the `swipes` table shows only **9** rows with
  `action = Like` (plus 1 `action = Pass` row) — a discrepancy of exactly 1.
- **Root cause:**
  1. `swipes` has a composite-key **upsert**: `RecordSwipe` does
     `FirstOrDefault(userid, targetUserId)` → if found, **overwrites** `entity.action` in place;
     it does not insert a second row and does not care what the previous action was.
  2. `Swipe()` calls `_featureGateService.TryConsume(userid, FeatureKey.DailyLikes)`
     **unconditionally** on every `Like` call — it has no awareness of whether this is a brand
     new swipe or a re-swipe of a target the user already swiped (in either direction).
  3. Sequence observed: User A **Liked** target `filler-1` (TryConsume succeeds, quota
     incremented, row inserted with `action=Like`), then later **Passed** on the same
     `filler-1` (TryConsume is skipped for Pass per design — correct — but `RecordSwipe`
     overwrites the existing row's `action` to `Pass`). The original Like's row is now gone;
     only the Pass is visible. The quota decrement from step 1 is never reversed.
- **Impact:** `user_feature_usage.used_count` can silently drift above the number of Likes a
  user actually has "on the books," with **no way to audit or reconcile the drift from the
  `swipes` table** — the evidence of what caused the charge is gone. This directly affects paid
  feature gating correctness (a Plus/Premium user's usage dashboard, or a Free user hitting
  their cap early because of swipe direction changes, would be wrong/unfair). It cannot
  under-charge (every Like call that's `allowed` still consumes), but it can over-charge and
  destroy the audit trail.
- **Fix direction (needs a product decision, not just a patch):** Decide the intended rule, then
  implement it consistently:
  - **Option A (recommended):** Make quota consumption derive from a true "net new Like"
    check: only call `TryConsume` when the target hasn`t previously been swiped `Like` by this
    user (i.e., skip consuming if `RecordSwipe` is about to upsert an existing `Like` row back to
    `Like`, and — bigger decision — decide whether changing Pass→Like should consume quota at
    insert time only, not on every call). Simplest robust fix: check existing swipe action first;
    only call `TryConsume` when the new action is `Like` AND there was no previous row (or the
    previous row's action wasn't already `Like`).
  - **Option B:** Make swipes append-only (no upserting over a prior action) and treat "already
    swiped" as a hard rejection (`"You've already swiped on this user."`), which matches how
    most swipe-based apps behave and would make quota and swipe-history trivially consistent.
  - Either way, add a DB-level or application-level invariant test so `used_count` can be
    reconciled against `COUNT(*) FROM swipes WHERE action = Like` per period.
- **Reverification (2026-10-26):** Fixed via Option A, in `MatchService.Swipe` ~lines 374-411 and
  `MatchRepository.RecordSwipe`. `RecordSwipe` now returns `(bool wasNewLike, SwipeAction?
  previousAction)` (race-free, via an advisory lock on the pair) instead of silently upserting.
  `Swipe()` still optimistically calls `TryConsume` before `RecordSwipe` (to guarantee a denied
  Like is never persisted), but now refunds the unit via `RefundConsumption` in both directions
  once it knows the truth: (a) `action == Like && !wasNewLike` (a re-Like of an already-liked
  target — also the BUG-11 fix, see below), and (b) `action != Like && previousAction == Like`
  (a Like being overwritten by a Pass/Unlike — the original BUG-04 scenario). Reverified via the
  19-user bulk/scale pass: `daily_likes.used_count` reconciled exactly against
  `COUNT(*) FROM swipes WHERE action='Like'` for every one of the 19 users, zero drift, after 338
  concurrent Like/Pass calls including deliberate repeat-Likes and Like→Pass flips.
  `dotnet build ProConnect.sln`: 0 errors.

## BUG-05 — Raw Stripe SDK exception text leaked verbatim into API error responses [FIXED]

- **Where:** `SubscriptionService.Checkout`, `PurchaseConsumable`, `CancelSubscription`
  (ProConnect.Application/Service/SubscriptionService.cs, ~line 129-250), via
  `StripePaymentGateway` (ProConnect.Infrastructure/Provider/StripePaymentGateway.cs).
- **Symptom (reproduced):** With Stripe dark/unconfigured (`secretkey` empty in this
  environment), calling any of the three endpoints returns a 500 whose `errormessage` is the
  literal Stripe.net exception message, e.g. something like
  `"Invalid API Key provided: *****. ... as it is an empty string."` surfaced straight through
  to the HTTP client.
- **Root cause:** All three methods wrap the `_paymentGateway` call in
  `try { ... } catch (Exception ex) { LogError(...); throw; }` — the `throw` rethrows the raw
  `StripeException` unchanged, and `ExceptionHandlingMiddleware` puts `ex.Message` directly into
  `errormessage` for unhandled exceptions.
- **Impact:** Low severity in this dev environment (the leaked detail is just "API key is
  empty"), but shaped like an info-disclosure bug — in production this pattern would leak
  Stripe-internal error text (potentially including partial key fragments, internal object ids,
  or rate-limit/account details) to API clients.
- **Fix direction:** Catch `Stripe.StripeException` specifically in these three methods (or
  centrally in `ExceptionHandlingMiddleware`) and map it to a generic clean message
  (e.g. `"Payment provider error, please try again later."`) with a stable `ErrorCode`, logging
  the real exception server-side only.
- **Reverification (2026-10-26):** Fixed in `SubscriptionService.Checkout`, `PurchaseConsumable`,
  and `CancelSubscription` (lines ~163, ~218, ~285) — each now catches `Stripe.StripeException`
  specifically, before the generic `catch (Exception ex) { throw; }`, and returns a clean generic
  `ProConnectResponse` (`"Payment provider error, please try again later."`-shaped message) with
  the real `StripeException` logged server-side only via `LogError`. Live-reverified against the
  dark/unconfigured Stripe config in this environment: `Checkout`, `PurchaseConsumable`, and
  `CancelSubscription` all now return the clean generic message instead of the raw
  `"Invalid API Key provided..."` SDK text. `dotnet build ProConnect.sln`: 0 errors.

## BUG-06 — Webhook with bad/missing `Stripe-Signature` returns HTTP 500, not the 400 the code claims [FIXED]

- **Where:** `SubscriptionController.Webhook` (ProConnect.WebApi/Controllers/SubscriptionController.cs)
  + `StripePaymentGateway.ParseWebhookEvent` (~line 131-213).
- **Symptom (reproduced):** POSTing to `/api/subscription/v1/webhook` with a missing or
  incorrectly-signed `Stripe-Signature` header throws `Stripe.StripeException` ("No signatures
  found matching the expected signature for payload" / similar) from
  `EventUtility.ConstructEvent`, which is **not caught** anywhere in the call chain and is mapped
  by `ExceptionHandlingMiddleware` to a generic HTTP 500 — identical shape to BUG-01/02/03.
- **Root cause:** The controller's own doc comment on the `Webhook` action explicitly states
  that signature-verification failures "bubble up as a 400", but there is no actual try/catch
  around `ParseWebhookEvent`'s call anywhere in the stack to make that true.
- **Impact:** Stripe's retry/alerting behavior (and any ops dashboards) distinguish 4xx
  (don't retry, bad request from Stripe's POV — shouldn't happen with a real signing secret) from
  5xx (retry, treated as *our* outage). A bad-signature event causing a 500 will make Stripe
  retry the same (undeliverable) event repeatedly and can also trip server-error alerting for
  what is actually a client-side signature mismatch — worth getting right before going live.
- **Fix direction:** Wrap the `ParseWebhookEvent` call in the controller (or in
  `HandleWebhook`) in a try/catch for `StripeException`, and return
  `StatusCode(400, ...)` directly, matching the existing doc comment's intent.
- **Reverification (2026-10-26):** Fixed in `SubscriptionService.HandleWebhook`, ~lines 303-326 —
  `ParseWebhookEvent` is now called in its own try/catch for `Stripe.StripeException` (logged as
  `LogWarning`), returning `ProConnectResponse { statuscode = 400, issuccessfull = false,
  errormessage = "Invalid webhook signature." }`; `SubscriptionController.Webhook` already
  forwards whatever `statuscode` the service sets via `StatusCode(response.statuscode,
  response)`, so no controller change was needed. Live-reverified by POSTing to
  `/api/subscription/v1/webhook` with a deliberately wrong `Stripe-Signature` header: got a real
  HTTP 400 with `"Invalid webhook signature."`, not a 500. `dotnet build ProConnect.sln`: 0
  errors.

## BUG-07 — `CancelSubscription` has no idempotency guard for an already-canceled subscription [FIXED]

- **Where:** `SubscriptionService.CancelSubscription` (ProConnect.Application/Service/SubscriptionService.cs,
  ~line 220-250).
- **Symptom (reproduced):** Calling cancel a second time on a subscription whose local
  `user_subscription.status` is already `Canceled` (e.g. after a `customer.subscription.deleted`
  webhook already landed) still unconditionally calls
  `_paymentGateway.CancelSubscription(subscription.stripe_subscription_id, at_period_end)` against
  the (dark) Stripe gateway, which throws and surfaces as the same raw-500 (BUG-05) shape instead
  of a clean "already canceled" response.
- **Root cause:** The existing precondition check only guards against `subscription is null` or
  a missing `stripe_subscription_id` — it never checks `subscription.status`:
  ```csharp
  var subscription = await _subscriptionRepository.GetUserSubscription(userid);
  if (subscription is null || string.IsNullOrEmpty(subscription.stripe_subscription_id))
      return new ProConnectResponse<string> { issuccessfull = false, errormessage = "No active subscription was found.", ... };
  await _paymentGateway.CancelSubscription(subscription.stripe_subscription_id, at_period_end);
  ```
- **Impact:** Double-cancel (e.g. user double-clicks "Cancel subscription" in the UI, or a retry
  after a slow/timed-out first request) crashes instead of behaving idempotently; in production
  this would also make an unnecessary live call to Stripe for a subscription that's already gone
  there too.
- **Fix direction:** Add `if (subscription.status == SubscriptionStatus.Canceled) return` a
  clean "Subscription is already canceled." response before calling the gateway.
- **Reverification (2026-10-26):** Fixed in `SubscriptionService.CancelSubscription`, ~line 249 —
  a `subscription.status == SubscriptionStatus.Canceled` check now returns a clean
  `"Subscription is already canceled."` response before the gateway call. Live-reverified by
  calling `POST /api/subscription/v1/cancel` three times in a row on the same subscription: call
  1 succeeded normally (status flips to Canceled), calls 2 and 3 both cleanly returned "already
  canceled" with no Stripe-gateway call and no 500. `dotnet build ProConnect.sln`: 0 errors.

## BUG-08 — Blocking a user does not stop them from Liking/Passing you directly [FIXED] — safety-relevant

- **Where:** `MatchService.Swipe` (ProConnect.Application/Service/MatchService.cs, ~line 300-399),
  shared by both public `Like` and `Pass` methods.
- **Symptom (reproduced live):** After User B blocks User C (`POST /api/match/v1/{USER_C}/block`,
  confirmed success + confirmed `Blocks` row written + confirmed both users' recommendation
  caches invalidated), User C can still call
  `POST /api/match/v1/{USER_B}/like` and gets back a normal success response
  (`"Like recorded.", issuccessfull=true`) — the swipe is recorded and a notification/match flow
  could still fire.
- **Root cause:** `Swipe()` only validates `targetUserId != Guid.Empty`, `targetUserId != userid`,
  and the daily-like cap before calling `RecordSwipe`. It never queries the `Blocks` table in
  either direction. This is inconsistent with the rest of the match surface:
  `MatchRepository.GetCandidates` (recommendations), `BuildIncomingLikesQuery` (liked-me), and
  `BuildPassedCandidatesQuery` (passed-candidates) all correctly filter out blocked-either-direction
  pairs — only the direct swipe-by-id path was missed.
- **Impact:** A user who has gone to the trouble of blocking someone (e.g. for harassment/safety
  reasons) can still be Liked/Passed by that person, could still theoretically be matched with
  them if the blocker had also already liked them before blocking, and would still receive
  "new like"/"new match" notifications from the person they explicitly blocked — defeats the
  purpose of the block feature.
- **Fix direction:** Add a `_matchRepository.IsBlockedEitherDirection(userid, targetUserId)` (or
  equivalent) check at the top of `Swipe()`, alongside the existing empty/self checks, and return
  a clean "You can't interact with this user." response (without revealing which side blocked
  which) if true.
- **Reverification (2026-10-26):** Fixed in `MatchService.Swipe`, ~line 358-368 — an
  `_matchRepository.IsBlockedEitherDirection(userid, targetUserId)` check now runs right after
  the target-existence check, returning a clean `"You can't swipe on this user."` response
  without revealing which side blocked which. Live-reverified by having User C attempt
  `POST /api/match/v1/{USER_B}/like` after User B blocked User C: got the clean rejection, no
  swipe row written, no notification fired. `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-09 — SignUp silently persists wrong/empty identity data when the scraper mismatches or finds nothing [FIXED] — data-integrity

- **Where:** `HarvestApiProfileProvider.GetProfile` (ProConnect.Infrastructure/Provider/HarvestApiProfileProvider.cs,
  line 54: `var profile = results?.FirstOrDefault();`) and `UserService.SignUp`
  (ProConnect.Application/Service/UserService.cs, line 116: `public_identifier = profileResponse.public_identifier`).
- **Symptom (reproduced live, found during bulk/scale test population build-out):** Of 14
  real-identifier bulk signups, 4 came back with corrupted identity data despite the API
  returning `issuccessfull=true` / HTTP 200 with a usable token:
  - Requested `public_identifier:"sherylsandberg"` → the persisted row
    (`userid=96db9cf4-2557-4d99-a9f8-7865dcb981ee`) has `full_name:"Andy Gu"`,
    `public_identifier:"andygu35829"`, `city:NULL` — an entirely unrelated real person's profile
    was scraped and silently accepted as if it were the requested identifier.
  - Requested `"billgates"`, `"tobiasluetke"`, `"stewartbutterfield"` → all three persisted with
    `full_name:""` (empty string), `public_identifier:NULL`, `country:NULL` — i.e. the actor
    returned a dataset item that is not a real profile (likely a "not found" placeholder/stub
    row for the query), and the app happily mapped its all-null fields into a "successful" user
    record anyway.
- **Root cause:** Two missing checks compound:
  1. `GetProfile` only checks `results?.FirstOrDefault()` for **null** before accepting it as
     the answer. It never checks whether the returned item is actually a *valid* profile
     (e.g. has a non-empty `PublicIdentifier`/`FirstName`/`LastName`) nor whether its
     `PublicIdentifier` actually matches the `publicIdentifierOrUrl` that was requested. A
     not-found stub item and a fuzzy/wrong match both sail through indistinguishably from a
     correct hit.
  2. `UserService.SignUp` then trusts `profileResponse.public_identifier` (and every other
     scraped field) unconditionally, with no cross-check against `request.public_identifier`
     and no minimum-completeness check (e.g. non-empty name) before calling
     `_userRepository.SignUp` and returning a success token. The code's own null-check
     (`if (profileResponse is null) throw ...`) assumes "not found" always comes back as a
     literal `null`, which this provider doesn't guarantee.
  3. Compounding both: `FetchLinkedInProfile`'s cache (`CacheRegion.PERSONPROFILE`) caches
     this bad result keyed by the *requested* identifier (per its own doc comment, "Cache.Get
     only caches non-null results"), so once a bad/empty profile is fetched for a given
     identifier it will keep being served from cache on every subsequent signup/lookup attempt
     for that same identifier — a one-time scraper hiccup becomes a persistent poisoned
     cache entry.
- **Impact:** A user can "successfully" sign up and receive a valid session token while their
  account silently holds another real person's identity (privacy/impersonation-shaped — the
  Andy Gu case) or no usable identity/location data at all (breaks matching, since
  `has_location`/name-based candidate scoring can't work). Neither case produces any error,
  warning log, or visible signal to the user or to app operators — this was only caught by
  manually diffing requested vs. persisted `public_identifier` across a batch of signups.
  In production this would mean some unknown fraction of real signups silently end up as
  either misattributed or blank/broken accounts, with no telemetry to detect it.
- **Fix direction:** In `HarvestApiProfileProvider.GetProfile`, after getting
  `results?.FirstOrDefault()`, reject (treat as not-found / return `null`) any item whose
  `PublicIdentifier` doesn't case-insensitively match the requested `publicIdentifierOrUrl`
  (when the actor echoes it) or whose core identity fields (`FirstName`/`LastName`/
  `PublicIdentifier`) are all empty. In `UserService.SignUp`, add a defensive check that
  `profileResponse` has a non-empty `full_name` (or `first_name`/`last_name`) before
  proceeding, and log a `LogWarning`/`LogError` with the requested vs. returned identifier
  whenever they differ, so a mismatch is at least observable instead of silent. Consider also
  not caching results that fail the new validity check, so a transient scraper miss doesn't
  permanently poison `CacheRegion.PERSONPROFILE` for that identifier.
- **Reverification (2026-10-26):** Fixed across both layers named in the root cause, plus a
  follow-up found while reverifying:
  - `HarvestApiProfileProvider.GetProfile` (~line 119-124) now rejects (treats as not-found) a
    scraped result whose core identity fields are all empty, and does not cache it — closing the
    "poisoned cache entry for a one-time scraper miss" compounding factor.
  - `UserService.SignUp` (~lines 97-117) now checks for a null `profileResponse` *and* (follow-up
    discovered during this reverification, documented inline as `"BUG-09 fix (follow-up
    discovered during reverification)"`) rejects an empty/blank-name profile the same way,
    returning a clean error instead of persisting a blank-identity row — this closes the
    `"billgates"/"tobiasluetke"/"stewartbutterfield"` all-null-field case specifically.
  - The cache no longer serves a bad result for a previously-poisoned identifier
    (`andygu35829`'s original mis-scrape case, ~line 145) on retry.
  Live-reverified by re-running signups against the same real identifiers that previously
  produced the 4 corrupted records: all 4 now either return a clean rejection or a correctly-
  matched profile, with no blank-identity/mismatched-identity row persisted. `dotnet build
  ProConnect.sln`: 0 errors.

---

## BUG-10 — Concurrent Likes from the same user race on `user_feature_usage` and 500 instead of succeeding [FIXED] — concurrency/reliability

- **Where:** `SubscriptionRepository.IncrementFeatureUsage` (ProConnect.Infrastructure/Repository/SubscriptionRepository.cs,
  lines 185-209), called from `FeatureGateService.TryConsume` (ProConnect.Application/Service/FeatureGateService.cs,
  line 66), called from `MatchService.Swipe` (ProConnect.Application/Service/MatchService.cs, line 349)
  before `RecordSwipe` is ever reached.
- **Symptom (reproduced live, bulk/scale test, 20 concurrent real users, 67 concurrent Like/Pass
  calls fired via a thread pool):** 6 of 67 concurrent Like requests (~9%) failed with HTTP 500
  (`"Something went wrong, Please call administrator."`, `errormessage: "An error occurred while
  saving the entity changes. See the inner exception for details."`) even though every failing
  request was well within its actor's daily-like quota. The app log's inner exception for all 6
  is identical:
  ```
  Npgsql.PostgresException (0x80004005): 23505: duplicate key value violates unique constraint
  "PK_user_feature_usage"
  ```
  In every case, the failing request's actor had another concurrent Like request in flight at
  the same instant (e.g. `ariannahuffington` fired 6 concurrent Likes as part of the load test;
  exactly 2 of her 6 - the ones that lost the race - got this 500; her other 4 succeeded
  normally). This reproduces independent of any of the test's deliberate "race" tagging - it hit
  plain `mutual`/`one_sided` swipe calls too, anywhere the same actor happened to have two Likes
  in flight concurrently.
- **Root cause:** `IncrementFeatureUsage` is a plain read-then-write, not an atomic upsert:
  ```csharp
  var entity = await db.UserFeatureUsages
      .FirstOrDefaultAsync(u => u.userid == userid && u.feature_key == feature_key && u.period_key == period_key);
  if (entity is null)
  {
      entity = new UserFeatureUsageEntity { userid = userid, feature_key = feature_key, period_key = period_key, used_count = 0 };
      db.UserFeatureUsages.Add(entity);
  }
  entity.used_count += amount;
  await db.SaveChangesAsync();
  ```
  When two requests from the same user hit this concurrently for a `(userid, feature_key,
  period_key)` combination that doesn't have a row yet (e.g. a user's very first Like of the
  day, or - since `period_key` is date-based - their first Like after midnight UTC), both read
  "no row exists," both construct a new `UserFeatureUsageEntity`, and both call
  `SaveChangesAsync()`. The first `INSERT` wins; the second violates
  `PK_user_feature_usage` (`(userid, feature_key, period_key)`) and throws an unhandled
  `DbUpdateException`, which `ExceptionHandlingMiddleware` maps to the generic 500 - identical
  shape to BUG-01 through BUG-04. Because this happens inside `TryConsume`, which `Swipe()` calls
  *before* `RecordSwipe`, no inconsistent/partial row is left behind - but the user's Like is
  simply lost and reported as a scary unhandled-error 500, with no retry anywhere in the stack.
- **Impact:** Any real client that can fire two Likes close together for the same user - a
  double-tap, a flaky-network client-side retry, the same account open on two devices/tabs - has
  a real chance of a perfectly valid, well-within-quota Like failing with a 500 instead of
  succeeding, purely due to timing. This is a pure reliability bug (not a quota-correctness bug
  like BUG-04 - no over-counting or under-counting was observed; the loser's request is cleanly
  rejected, not partially applied), but it is the single highest-frequency failure found in this
  entire test pass (~9% of concurrent Likes in this run) and is trivially reachable by ordinary
  real-world client behavior, not just adversarial/scripted concurrency.
- **Fix direction:** Replace the read-then-write with a real upsert - either a raw SQL
  `INSERT ... ON CONFLICT (userid, feature_key, period_key) DO UPDATE SET used_count =
  user_feature_usage.used_count + EXCLUDED.used_count, updated_at_utc = now()` (atomic,
  single round trip, no race window), or catch `DbUpdateException` /
  `PostgresException` with SqlState `23505` around the existing insert path and retry as an
  `UPDATE` on conflict. Either fix should be covered by a concurrency-specific test (N parallel
  Likes from one fresh user) rather than relying on single-request tests, since this bug is
  invisible under any sequential test.
- **Related symptom, same root cause, opposite direction:** the same read-then-write gap also
  produced a silent **lost update** for a different user in the same run (`timcook`: 4 distinct
  successful Likes to 4 different targets, confirmed as 4 real `swipes` rows, but
  `user_feature_usage.used_count` only reached 3) - two of his concurrent increments both read
  the row's existing `used_count` before either had saved, so the second `SaveChangesAsync()`
  silently clobbered the first's increment with a stale value instead of throwing. This doesn't
  crash a request (unlike the insert-collision case above) and is strictly user-favorable (quota
  undercounted, not overcounted), but it's the same non-atomic-increment defect and confirms the
  fix needs to cover both the insert-path race (`23505`) and the update-path lost-update, which
  an `INSERT ... ON CONFLICT DO UPDATE SET used_count = used_count + EXCLUDED.used_count`
  (computed relative to the *current* row, not a value read into memory beforehand) fixes for
  both cases at once.
- **Reverification (2026-10-26):** Fixed in `SubscriptionRepository.IncrementFeatureUsage`,
  ~lines 196-210 — the read-then-write is replaced with a single atomic
  `INSERT INTO user_feature_usage (...) VALUES (...) ON CONFLICT (userid, feature_key,
  period_key) DO UPDATE SET used_count = user_feature_usage.used_count + EXCLUDED.used_count,
  updated_at_utc = EXCLUDED.updated_at_utc RETURNING used_count` — a single round trip with no
  read-then-write window, fixing both the insert-collision (`23505`) crash and the update-path
  lost-update. Reverified via the 19-user bulk/scale pass: 338 concurrent Like/Pass calls across
  2 rounds, 0 non-200 responses (no `23505`/`PK_user_feature_usage` 500s), and 0 usage drift for
  any of the 19 users (i.e. no silent lost-updates either).

## BUG-11 — Re-Liking an already-liked target double-charges the daily quota with no new swipe recorded [FIXED]

- **Where:** `MatchService.Swipe` (ProConnect.Application/Service/MatchService.cs, lines 324-361)
  calls `_featureGateService.TryConsume` unconditionally for every Like, before ever checking
  whether `(userid, targetUserId)` has already been swiped; `MatchRepository.RecordSwipe`
  (ProConnect.Infrastructure/Repository/MatchRepository.cs, lines 174-189) then silently
  upserts the existing `SwipeEntity` row in place rather than erroring or signaling "no-op."
- **Symptom (reproduced live, bulk/scale test):** The test deliberately fired the identical
  `Like` call twice concurrently for 5 pairs (`dup_race` tag) to probe idempotency. For the
  pairs where both concurrent calls returned 200 (no `PK_user_feature_usage` crash - see
  BUG-10), the `swipes` table correctly ended up with exactly **one** row for the pair (upsert
  behavior worked as intended there), but `user_feature_usage.used_count` was incremented
  **twice**. Confirmed directly against the DB for two different users:
  - `ariannahuffington`: 2 distinct real swipe partners today, but `used_count = 3`.
  - `sundarpichai`: 2 distinct real swipe partners today, but `used_count = 3`.
  Both discrepancies are explained exactly by one repeated Like each on an already-liked target.
- **Root cause:** `Swipe()` has no "already swiped this target with this action?" short-circuit
  before spending a quota unit. Every call to `Like()` - whether it's the user's first Like of
  a target or their fifth retry/double-tap on the same target - re-runs the full
  `TryConsume` → `RecordSwipe` sequence. `RecordSwipe`'s upsert makes the *swipe* idempotent,
  but nothing makes the *quota charge* idempotent, so the two are now out of sync: `used_count`
  measures "how many times Like was called" instead of "how many distinct Likes this user has
  sent," which is what the FREE/PLUS caps (BUG-04's `plan_feature.daily_likes`) are supposed to
  be gating.
- **Impact:** Primarily a quota-accuracy bug, not a security hole (it only ever *undercounts*
  the user's effective remaining Likes, i.e. hurts the user rather than letting them exceed
  their cap) - but it means a client that retries a failed-looking request (slow network, double
  tap, optimistic-UI re-send) can burn through a FREE user's entire 10-like daily cap by
  accidentally repeating the same handful of Likes, long before they've actually liked 10
  distinct people. It also means `used_count` is not a trustworthy audit trail of distinct Likes
  sent, which `TEST_PLAN.md`'s own scale-test pass/fail bar ("daily_likes consumption matches
  actual accepted likes exactly") explicitly flags as a requirement this fails.
- **Fix direction:** In `Swipe()`, check `_matchRepository.HasLiked(userid, targetUserId)` (or
  an equivalent "does a swipe already exist for this pair" lookup) before calling `TryConsume`
  when `action == Like`; if a Like already exists for that target, skip the quota charge and
  return the existing result idempotently (e.g. `ismatch` recomputed, no new charge) rather than
  re-spending a quota unit for a no-op write.
- **Reverification (2026-10-26):** Fixed as part of the same change described under BUG-04 above
  — `MatchRepository.RecordSwipe` now returns `wasNewLike`, and `MatchService.Swipe` (~line 400)
  refunds the optimistically-charged unit via `RefundConsumption` whenever
  `action == Like && !wasNewLike`, i.e. a re-Like of an already-liked target. A follow-up gap was
  found and fixed in the same change: `NotifyNewLike` previously fired unconditionally on every
  non-reciprocated Like, including re-Likes — now gated on `wasNewLike` too (~line 423-434,
  commented inline as `"Found during BUG-11 reverification"`), so a repeated/double-tapped Like
  no longer produces duplicate "someone liked you" notifications either. Reverified via the
  19-user bulk/scale pass's deliberate `dup_race` pairs (identical Like fired twice concurrently
  for 5 pairs): exactly one `swipes` row and exactly one quota unit charged per pair, confirmed
  against `user_feature_usage.used_count` matching `COUNT(*) FROM swipes WHERE action='Like'`
  exactly for all 19 users after the full run.

---

## BUG-12: Reciprocal concurrent Likes create duplicate "It's a match!" notifications for both users [FIXED] — concurrency/reliability

- **Where:** `MatchService.Swipe` (lines 368-392), specifically the sequence
  `targetAlreadyLikedUser = HasLiked(...)` → `CreateMatch(...)` → `NotifyNewMatch(...)` with no
  "did *this* call actually create the match, or did it already exist?" distinction;
  `MatchRepository.CreateMatch` (lines 199-224) is itself correctly idempotent (check-then-return
  existing `matchid` if the pair already matched), but that idempotency is invisible to its
  caller.
- **Symptom:** In the bulk/scale test, 3 pairs were deliberately fired as "reciprocal_race" (A
  likes B and B likes A sent concurrently, same instant) and 8 pairs were fired as normal mutual
  pairs (A likes B, then B likes A). For at least one of the mutual pairs
  (brianchesky↔marcbenioff, matchid `80a94921-...`), **both** users ended up with **two**
  identical `NewMatch` notification rows each (`"It's a match! You and <name> matched!"`,
  same `related_entity_id` = the one matchid, created ~89ms apart) — 4 duplicate rows total for
  one logical match event. Only **one** row was created in `matches` itself (verified via
  `GROUP BY ... HAVING COUNT(*)>1` on normalized pairs — zero duplicate match rows across the
  whole run), so the match-creation idempotency works; only the notification side double-fires.
- **Root cause:** When A's swipe and B's swipe to each other both land close enough in time,
  each of the two concurrent `Swipe()` calls independently reaches the
  `targetAlreadyLikedUser == true` branch (because by the time either call checks `HasLiked`,
  the other's `RecordSwipe` has already landed) and each one calls `CreateMatch` — which
  correctly returns the *same* `matchid` to both callers instead of inserting a second row — but
  each caller then unconditionally calls `NotifyNewMatch(userid, targetUserId, matchId)` on its
  own thread, with no check for "was this matchid newly created by me, or did I just get back an
  existing one." The result: one match, but as many `NotifyNewMatch` calls (and thus as many
  duplicate notification pairs) as there were concurrent `Swipe()` calls that raced into the
  already-matched branch for that pair.
- **Impact:** User-facing noise/confusion (seeing "It's a match!" twice for the same person,
  and once FCM is configured in production, two duplicate push notifications too) and a
  data-integrity gap in the `notification` table (two rows for one real-world event). Directly
  violates `TEST_PLAN.md`'s own scale-test pass/fail bar ("no duplicated/dropped
  notifications"). Likely under-detected in this run specifically because it requires near-exact
  concurrent timing on *both* sides of a pair — real-world frequency depends on how often two
  users' Like taps land within the same request-processing window, but any client-side retry
  (double-tap, optimistic-UI re-send, flaky network causing a duplicate submit) on either side
  of an already-mutual pair reproduces it just as easily as true simultaneity does.
- **Fix direction:** Make `CreateMatch` signal whether it inserted a new row or returned an
  existing one (e.g. return `(Guid matchId, bool created)` instead of just `Guid`), and have
  `Swipe()` only call `NotifyNewMatch` when `created == true`. This is the same
  "idempotent-write-but-caller-doesn't-know-it" shape as BUG-11 (quota charge not short-circuited
  on an already-existing swipe) — recommend reviewing both together, since the general lesson is
  that an idempotent repository method must expose *whether it was a no-op* to any caller that
  does side effects (notifications, quota charges, etc.) conditional on the write actually being
  new.
- **Reverification (2026-10-26):** Fixed in `MatchRepository.CreateMatch` (now returns
  `(Guid matchId, bool created)`, race-free via an advisory lock on the canonicalized pair) and
  `MatchService.Swipe` ~line 443-452, which now only calls `NotifyNewMatch` when `created ==
  true`. Reverified two ways: (1) the bulk/scale pass's 3 deliberate `reciprocal_race` pairs plus
  8 normal mutual pairs — a full audit of all `NewMatch` notifications found `106 = 2 × 53`
  exactly (53 distinct matchids ever notified, 2 notifications each, zero duplicate
  `(userid, related_entity_id)` pairs via `GROUP BY ... HAVING COUNT(*) > 1`); (2) a dedicated
  5-way concurrent reciprocal-Like race test, which produced exactly one `matches` row and
  exactly 2 `NewMatch` notifications (one per side) with no duplicates.

---

## BUG-13 — `customer.subscription.updated`/`.deleted` webhook replay is not idempotent (duplicate `SubscriptionCanceled` notifications) [FIXED] — found during 2026-10-26 re-verification

- **Where:** `SubscriptionService.HandleSubscriptionStatusChanged` (ProConnect.Application/Service/SubscriptionService.cs),
  the shared handler for both `customer.subscription.updated` and `customer.subscription.deleted`
  webhook events, called from `HandleWebhook`.
- **Symptom (reproduced live):** `HandleWebhook`'s idempotency guard
  (`_subscriptionRepository.TransactionExistsByStripeEventId(stripe_event_id)`, checked before
  dispatching to a handler) only has something to find if the handler that processed the event
  the first time called `CreateTransaction`. Every other webhook handler
  (`Checkout`/`PurchaseConsumable`'s completion paths) does; `HandleSubscriptionStatusChanged` did
  not. Replaying an identical `customer.subscription.deleted` event therefore always fell through
  to full reprocessing instead of being recognized as already-handled — observed live as a
  second, duplicate `SubscriptionCanceled` notification from a single replayed event id.
- **Root cause:** `HandleSubscriptionStatusChanged` updated `user_subscription` (via
  `UpsertUserSubscription`, itself idempotent) and invalidated the effective-plan cache, but never
  wrote a `PaymentTransaction` row keyed by `stripe_event_id` — so it was never a member of the
  same idempotency ledger every other webhook-driven write participates in, even though this
  event type has no monetary amount of its own.
- **Impact:** Stripe explicitly recommends designing webhook handlers to be replay-safe (retries
  on timeout, manual redelivery from the Stripe dashboard, etc. are all expected to be benign).
  Without this, a single Stripe-side retry of a subscription-cancellation event would re-fire
  user-facing notifications (and, if any other side effect were ever added to this handler, could
  re-apply it too) once per redelivery.
- **Fix direction / fix applied:** Added a `CreateTransaction` call in
  `HandleSubscriptionStatusChanged`, immediately after `UpsertUserSubscription`/
  `InvalidateEffectivePlan` and before the `SubscriptionCanceled` notification block — a
  zero-`amount` `PaymentTransaction` row keyed by `evt.stripe_event_id`, whose only purpose is to
  make this handler a member of the same `TransactionExistsByStripeEventId` idempotency ledger as
  every other webhook-driven write.
- **Reverification (2026-10-26):** Replayed an identical hand-signed `customer.subscription.deleted`
  event 3 times total after the fix: call 1 processed normally (status flips to Canceled, one new
  `SubscriptionCanceled` notification written); calls 2 and 3 both correctly short-circuited with
  `"Event already processed."` and added zero additional notifications. `dotnet build
  ProConnect.sln`: 0 errors.

---

## BUG-14 — Duplicate signup accepted for an email address that already has an account [FIXED] — found during 2026-10-26 session via direct question about LinkedIn-email duplicate signups

- **Where:** `UserService.SignUp` (ProConnect.Application/Service/UserService.cs); DB schema in
  `UserEntityConfiguration.cs`.
- **Symptom (reproduced live):** Two `POST /api/user/v1/signup` requests with different
  `public_identifier`s (`jeffweiner08`, `reidhoffman`) and no `sub`, but the identical `email`
  (`duplicate_email_test@example.com`), both returned HTTP 200 / `issuccessfull:true`, creating
  two fully separate `userid`s that shared one `emailaddress` value in the `users` table (confirmed
  via direct `psql` query returning both rows).
- **Root cause:** `UserService.SignUp` only had BUG-01's `sub`-based duplicate check; there was no
  equivalent check for `email`. `UserEntityConfiguration.cs` had no index or uniqueness constraint
  of any kind on `emailaddress` either (only `sub` was `.IsUnique()`), so nothing at either layer
  prevented two accounts from sharing an email address.
- **Impact:** Same LinkedIn user (or two different signup flows carrying the same email, e.g. a
  user who changes which LinkedIn profile they authorize against this app) could silently end up
  with two unrelated accounts/`userid`s, splitting their matches, swipes, and subscription state
  across both — with no error ever surfaced to let them notice.
- **Fix direction / fix applied:**
  1. Added `IUserRepository.GetByEmailForAuthentication(string email)` /
     `UserRepository.GetByEmailForAuthentication` — the email-equivalent of
     `GetBySubForAuthentication`, deliberately unfiltered by `is_active` so a deactivated user's
     email still blocks a new duplicate signup (same rationale as the existing `sub` check).
  2. Added an app-level check in `UserService.SignUp`, directly mirroring the existing BUG-01
     `sub` check: `if (!string.IsNullOrEmpty(request.email))` → look up
     `GetByEmailForAuthentication(request.email)` → if found, return a clean
     `issuccessfull:false` / `"A user with this email already exists. Please sign in instead."`
     response instead of proceeding.
  3. Added a DB-level unique index as defense-in-depth against a race between two concurrent
     requests for the same new email slipping past the app-level check: `builder.HasIndex(u =>
     u.emailaddress).IsUnique();` in `UserEntityConfiguration.cs`, applied via new migration
     `20261026041155_AddUserEmailUniqueIndex`. Postgres unique indexes don't treat multiple `NULL`
     values as duplicates of each other, so this does not affect users who sign up with no email
     at all (same precedent already relied on for the `sub` unique index).
- **Reverification (2026-10-26):** Restarted the compiled API against the patched build + migrated
  schema. Repeated the exact live reproduction: first signup (`jeffweiner08`,
  `bug14_retest@example.com`) succeeded normally; second signup (`reidhoffman`, same email)
  returned `issuccessfull:false` / `"A user with this email already exists. Please sign in
  instead."` with **no** new row written (confirmed via `psql`: only the first `userid` present).
  Edge case also verified: two separate signups with no `email` field at all (`satyanadella`,
  `reidhoffman`) both succeeded, and `psql` confirmed both stored a genuine SQL `NULL` (not an
  empty string) in `emailaddress` with no false-positive unique-constraint violation. `dotnet build
  ProConnect.sln`: 0 warnings, 0 errors.
- **Follow-up (same day, found by asking "are there any other untested edge cases?" after BUG-14
  shipped) — the BUG-14 check itself validated the wrong field:** `UserService.SignUp` ran its new
  email-duplicate check against raw `request.email` *before* `FetchLinkedInProfile` was even
  called, but the value actually persisted (and enforced by `IX_users_emailaddress`) is
  `profileResponse.emailaddress` - which `HarvestApiProfileProvider.MapToPersonProfile` populates
  from the scrape's own `Emails` field whenever the scrape returns one, silently overriding
  `request.email` a few lines later (`profileResponse.emailaddress = string.IsNullOrEmpty(...) ?
  request.email : profileResponse.emailaddress`). So a scraped email that collided with an
  existing user could sail past the proactive check (which only looked at `request.email`) and
  either crash on the DB unique-constraint insert, or - worse - two signups whose *scraped* emails
  collided but whose *request* emails didn't would both look clean to the old check. Reproduced
  live by seeding two crafted LinkedIn-profile cache entries with the same scraped
  `emailaddress` (`scraped@example.com` / `SCRAPED@Example.com `, deliberately differing in case
  and whitespace) behind two different `request.email` values that did **not** collide
  (`totallydifferent@example.com` / `unused@example.com`) - the first signup succeeded, the second
  was correctly rejected only after the fix (`psql` confirmed exactly one row).
  - **Also fixed in the same change: no email normalization anywhere.** `Test@Example.com ` and
    `test@example.com` were treated as different emails by both the proactive check and the DB
    unique index, so case/whitespace variants could each get their own account. Added a
    `NormalizeEmail` helper (`Trim().ToLowerInvariant()`, null-safe) in `UserService.cs`, applied
    once at the point `profileResponse.emailaddress` is resolved (covering both the
    request-supplied and scraped-email branches) so every downstream consumer - the duplicate
    check, the DB write, the JWT claim - sees the same normalized value. Reproduced live: signing
    up with `Test@Example.com ` stored `test@example.com` in `users.emailaddress`; a second signup
    with `" test@example.com"` (different case/whitespace, same mailbox) was correctly rejected as
    a duplicate.
  - **Fix:** moved the email-duplicate check to *after* `profileResponse.emailaddress` is resolved
    and normalized, so it checks the actual effective/persisted value via
    `GetByEmailForAuthentication(profileResponse.emailaddress)` instead of raw `request.email`.
  - `dotnet build ProConnect.sln` - 0 errors. See BUG-15 below for the second half of this
    follow-up pass (the true-concurrency race this same code path had never been tested against).

---

## BUG-15 — True concurrent duplicate signup (same `sub` or `email`) would have crashed with a raw HTTP 500 [FIXED] — found during the same 2026-10-26 follow-up pass as the BUG-14 correction above

- **Where:** `UserRepository.SignUp` (ProConnect.Infrastructure/Repository/UserRepository.cs);
  `UserService.SignUp` (ProConnect.Application/Service/UserService.cs); new
  `ProConnect.Domain/Exception/DuplicateUserException.cs`.
- **Symptom (never previously tested, confirmed live this session):** `UserService.SignUp`'s
  `sub`/email duplicate checks (BUG-01, BUG-14) are check-then-act: look up, then insert, with no
  transaction or lock spanning the gap. Two genuinely simultaneous signups for the same new
  `sub` or `email` can both pass the lookup before either one's insert commits - at which point
  one insert succeeds and the other hits Postgres's `23505` unique-violation on `IX_users_sub` or
  `IX_users_emailaddress`. Nothing in the codebase caught that exception type anywhere (confirmed
  via `grep -rn "23505|PostgresException|DbUpdateException"` - the only hits were comments
  describing this exact risk, not actual handling code), so it would have escaped as an unhandled
  HTTP 500 - the same failure shape BUG-01/BUG-10 exist to avoid in their own non-race cases.
- **Root cause:** No code anywhere translated a Postgres unique-violation on `users` into a clean
  response; `UserRepository.SignUp` just let `DbUpdateException` propagate straight out of
  `SaveChangesAsync`.
- **Impact:** Low-probability but real - two devices/tabs submitting the same brand-new signup at
  the same instant (e.g. a double-tap, or two LinkedIn OAuth callbacks for the same account
  racing) would see one clean success and one raw 500 with no actionable message, instead of both
  seeing a clean outcome.
- **Fix applied:**
  1. New `ProConnect.Domain.Exception.DuplicateUserException(string conflictField)` - a plain
     Domain-layer exception (`ConflictField` is `"sub"`, `"email"`, or `"unknown"`), keeping
     Npgsql-specific types confined to `UserRepository` and never leaking into `UserService`, the
     same "seam confinement" convention already used for `IPaymentGateway`/`IPushNotificationGateway`.
  2. `UserRepository.SignUp` now wraps `SaveChangesAsync` in `try { ... } catch (DbUpdateException
     ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState ==
     PostgresErrorCodes.UniqueViolation)`, mapping `pgEx.ConstraintName` (`IX_users_sub` /
     `IX_users_emailaddress`) to `"sub"` / `"email"` and throwing `DuplicateUserException`.
  3. `UserService.SignUp` gained a `catch (DuplicateUserException dupEx)` block (before the
     generic `catch (Exception ex)`) that maps `ConflictField` back to the exact same clean
     message its own proactive check would have returned (`"A user with this account already
     exists..."` / `"A user with this email already exists..."`), logged via
     `_logger.LogWarning` so a real production occurrence is visible without being treated as an
     error.
- **Reverification (live, genuine concurrency, not simulated):** Fired 3 truly concurrent `POST
  /api/user/v1/signup` requests (background `curl` processes, same instant) sharing one new `sub`
  across 3 different `public_identifier`s/emails - exactly one succeeded, the other two got the
  clean `"account already exists"` message with HTTP 200/`issuccessfull:false`, **not** a 500;
  server log confirmed both losers actually hit the new `catch (DuplicateUserException)` path
  (`"SignUp hit a concurrent-signup race on 'sub' for public_identifier 'qa-dedup-test-5/7'"`), not
  just the proactive check serializing them. Repeated for the `email` axis (3 concurrent requests,
  one shared new email, 3 different `sub`s/identifiers) with the same result - one success, two
  clean rejections, both confirmed via server log as genuine `DuplicateUserException` catches, not
  proactive-check hits. `psql` confirmed exactly one row per race in both cases. `dotnet build
  ProConnect.sln` - 0 errors.

---

## BUG-16 — Webhook payload with a null `api_version` crashes with raw HTTP 500 instead of a clean 400 [FIXED] — found during 2026-10-26 live API re-test

- **Where:** `SubscriptionService.HandleWebhook` (ProConnect.Application/Service/SubscriptionService.cs,
  ~line 318), specifically the call to `_paymentGateway.ParseWebhookEvent(payload,
  stripe_signature_header)` and the `catch (StripeException ex)` block immediately above it (the
  BUG-06 fix's signature-failure handler).
- **Symptom (reproduced live):** Posting a validly-HMAC-signed webhook envelope whose top-level
  `api_version` field is `null` (Stripe's own event schema documents this field as nullable, and
  a hand-crafted/legacy-shaped payload can legitimately omit it) returned a raw HTTP 500
  (`"Something went wrong, Please call administrator."`) instead of either processing normally or
  returning the clean 400 the BUG-06 fix established for malformed webhook input.
- **Root cause:** Stripe.net's `EventUtility.ConstructEvent` (called via its 3-arg overload, so
  `throwOnApiVersionMismatch` defaults to `true`) unconditionally calls
  `IsCompatibleApiVersion(eventApiVersion)` to decide whether to throw on an API-version mismatch
  - and that comparison throws a bare `NullReferenceException` (not a `StripeException`) when
  `eventApiVersion` is `null`, regardless of the `throwOnApiVersionMismatch` flag's value. Because
  the only catch block in scope was `catch (StripeException ex)` (the BUG-06 fix), this NRE fell
  straight through it into the generic `catch (Exception ex) { throw; }` further down, which
  `ExceptionHandlingMiddleware` maps to a generic 500 - for a payload whose HMAC signature was
  perfectly valid.
- **Impact:** A 500 response tells Stripe "this was our outage, please retry" - but this failure
  is deterministic on the exact same payload and will reproduce identically on every retry
  Stripe attempts, so the correct response is a 400 ("this request is malformed, don't retry")
  like the adjacent signature-failure path. Low real-world frequency against modern Stripe
  (current API versions are never null), but a legitimate risk against replayed/legacy-shaped
  test fixtures or any future Stripe API migration edge case, and a direct regression of the same
  "don't surface a 500 for a deterministically-bad webhook payload" guarantee BUG-06 was fixed to
  provide.
- **Fix applied:** Added a dedicated `catch (Exception ex)` immediately after the existing
  `catch (StripeException ex)` block around `ParseWebhookEvent`, narrowly scoped to just that one
  call (not the whole method), returning the same 400 shape as the signature-failure path
  (`statuscode = 400`, `issuccessfull = false`, `"Unable to process webhook payload."`), logged via
  `_logger.LogError` so the underlying NRE is still visible in logs for diagnosis.
- **Reverification (2026-10-26):** Posted a hand-signed webhook envelope with `api_version: null`
  (valid HMAC signature) directly against the running API - confirmed HTTP 400 with
  `"Unable to process webhook payload."`, not a 500; server log shows the NRE caught and logged,
  not propagated. `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-17 — Daily Like quota cap can be bypassed under concurrent requests via a check-then-write race [FIXED] — found during 2026-10-26 live concurrency re-test

- **Where:** `FeatureGateService.TryConsume` (ProConnect.Application/Service/FeatureGateService.cs,
  previously ~lines 58-68), called from `MatchService.Swipe`'s Like path.
- **Symptom (reproduced live):** 15 genuinely concurrent `POST /api/match/v1/{targetUserId}/like`
  requests fired for a Free-plan user sitting exactly at 0/10 of their daily cap (via backgrounded
  `curl` processes against 15 distinct `qa-race-N` synthetic targets, launched together and
  `wait`ed) resulted in **more than 10 accepted Likes** before the BUG-10 fix's atomic
  `IncrementFeatureUsage` upsert was the only protection in place - `user_feature_usage.used_count`
  overshot the `plan_feature.limit_value` cap of 10.
- **Root cause:** Even after BUG-10's fix made the increment itself atomic
  (`IncrementFeatureUsage`'s `INSERT ... ON CONFLICT DO UPDATE SET used_count = used_count +
  EXCLUDED.used_count`), `TryConsume` still composed the admission-control decision from three
  separate, non-atomic steps: (1) a live `GetFeatureUsage` read, (2) an in-memory comparison
  against `entitlement.limit_value`, and (3) - only if the comparison passed - a separate
  `IncrementFeatureUsage` call. BUG-10 made step 3 safe against *itself* (two concurrent increments
  can no longer crash or lose an update), but it did nothing to make steps 1-3 atomic *together*:
  N concurrent callers can all execute step 1 and read the same pre-increment `used_count`, all
  pass step 2's check against the same stale number, and all proceed to step 3 - each one's
  increment individually succeeds (thanks to BUG-10's fix), so the cap is overshot by however many
  callers raced past the check at once, with no error anywhere in the stack.
- **Impact:** Billing-adjacent - the entire purpose of `plan_feature.limit_value` is to cap a
  Free-plan user's daily Likes at a number the business has decided is free-tier-appropriate; a
  client that fires several Likes in a tight burst (normal behavior for a fast swiper, not just
  adversarial scripting) can exceed that cap with no resistance at all. Same risk profile as
  BUG-04 (quota-accuracy, billing-adjacent) but more severe: BUG-04 *undercounted* (user-unfavorable
  only in the sense of wasting quota), this *overcounts* in the user's favor, i.e. directly costs
  the business its intended free-tier limit.
- **Fix applied:** New `ISubscriptionRepository.TryIncrementFeatureUsage(userid, feature_key,
  period_key, amount, limit)` (`SubscriptionRepository.cs`, ~line 229) folds the check and the
  write into one atomic, row-locked Postgres statement:
  ```sql
  INSERT INTO user_feature_usage (userid, feature_key, period_key, used_count, updated_at_utc)
  SELECT @userid, @feature_key, @period_key, @amount, @now
  WHERE @limit IS NULL OR @amount <= @limit
  ON CONFLICT (userid, feature_key, period_key)
  DO UPDATE SET used_count = user_feature_usage.used_count + @amount, updated_at_utc = @now
  WHERE @limit IS NULL OR user_feature_usage.used_count + @amount <= @limit
  RETURNING used_count
  ```
  returning the new count if the increment was allowed, or no row at all (mapped to `null`) if
  applying it would have exceeded `limit` - in which case nothing is written, so usage is
  unchanged. `FeatureGateService.TryConsume` now calls this directly instead of
  `GetFeatureUsage`-then-check-then-`IncrementFeatureUsage`; on a `null` result it falls back to a
  `GetFeatureUsage` read purely to report the current count in the rejection response.
  `IncrementFeatureUsage` itself is kept (now documented as "unconditional, only safe for
  callers that don't need a limit enforced atomically with the write") since `RefundConsumption`'s
  negative-amount credit-back legitimately has no cap to check.
- **Reverification (2026-10-26):** Reset a Free user's quota and swipe history to a clean
  `0/10` state for the test's period key, then fired 15 genuinely concurrent Likes against 15
  distinct `qa-race-N` targets (backgrounded `curl` + `wait`). Result, repeated across 2 full
  clean re-runs: exactly 10 requests succeed, exactly 5 get a clean "Daily like limit reached"
  rejection, and `user_feature_usage.used_count` lands at exactly 10 - never more - every time.
  `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-18 — `AdjustConsumableBalance` loses updates and crashes under concurrency (same shape as BUG-10, different table) [FIXED] — found while reverifying BUG-19 below

- **Where:** `SubscriptionRepository.AdjustConsumableBalance` (ProConnect.Infrastructure/Repository/SubscriptionRepository.cs,
  previously ~lines 260-280), called from `SubscriptionService.HandleConsumableCheckoutCompleted`
  on every consumable-purchase webhook.
- **Symptom (reproduced live):** While probing the webhook-idempotency concurrency scenario that
  led to BUG-19 below, two concurrent consumable-purchase webhooks for the same user (distinct
  `stripe_event_id`s, 15-rose bundle each, expected final balance 30) instead produced a final
  `user_consumable_balance.balance` of **15** (one credit silently lost) on one run, and a raw
  HTTP 500 (`"An error occurred while saving the entity changes."`) on another run for one of the
  two requests, with the other succeeding.
- **Root cause:** `AdjustConsumableBalance` was a plain EF read-then-add-or-insert, the exact same
  vulnerable shape `IncrementFeatureUsage` had before the BUG-10 fix:
  ```csharp
  var entity = await db.UserConsumableBalances.FirstOrDefaultAsync(b => b.userid == userid && b.feature_key == feature_key);
  if (entity is null) { entity = new UserConsumableBalanceEntity { ... balance = 0 }; db.UserConsumableBalances.Add(entity); }
  entity.balance += delta;
  await db.SaveChangesAsync();
  ```
  `user_consumable_balance`'s primary key is the composite `(userid, feature_key)` - identical
  constraint shape to `user_feature_usage`'s `PK_user_feature_usage` that caused BUG-10. Two
  concurrent credits to a not-yet-existing row both read "no row," both insert, and the second
  insert throws `23505`; two concurrent credits to an *existing* row both read the same
  pre-increment `balance` into memory, and the second `SaveChangesAsync()` silently overwrites the
  first's increment with a stale value - a lost update, no error at all. BUG-10's own fix had only
  ever touched `IncrementFeatureUsage`; this sibling method in the same file was never covered by
  that fix or its reverification, because the original BUG-10 bulk/scale pass had no concurrent
  consumable-purchase traffic in its load plan.
- **Impact:** Billing-critical and worse than BUG-10's analogous gap: BUG-10 only affected a
  non-monetary quota counter, but this directly affects a consumable balance a real user paid real
  money for via Stripe - a lost update here means a customer who was charged for (e.g.) two 15-rose
  bundles could end up with only 15 roses credited, with no error surfaced to anyone to investigate
  the discrepancy (the crash variant is actually the *better* of the two outcomes here, since at
  least one side gets a visible 500 to retry/investigate).
- **Fix applied:** Rewrote `AdjustConsumableBalance` using the same atomic upsert pattern as the
  BUG-10/BUG-17 fixes, unconditional (no cap to check, so no `WHERE` guard needed unlike BUG-17's
  `TryIncrementFeatureUsage`):
  ```sql
  INSERT INTO user_consumable_balance (userid, feature_key, balance, updated_at_utc)
  VALUES (@userid, @feature_key, @delta, @now)
  ON CONFLICT (userid, feature_key)
  DO UPDATE SET balance = user_consumable_balance.balance + @delta, updated_at_utc = @now
  RETURNING balance
  ```
- **Reverification (2026-10-26):** Fired two consumable-purchase webhooks for the same user with
  two different `stripe_event_id`s (distinct logical purchases, not a replay) concurrently, twice
  in a row - both runs landed at the correct summed balance (30 for two 15-unit credits) with no
  500s and no lost updates. `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-19 — Genuinely concurrent identical-`stripe_event_id` webhook deliveries can both apply their side effect before either is recorded as processed, double-crediting a purchase [FIXED] — found during 2026-10-26 live concurrency re-test; distinct from BUG-13 above despite a similar idempotency theme

- **Where:** `SubscriptionService.HandleWebhook` (ProConnect.Application/Service/SubscriptionService.cs,
  ~line 303 onward), specifically the gap between the idempotency check
  (`TransactionExistsByStripeEventId`) and each handler's `CreateTransaction` insert, with
  unprotected side-effect calls (`AdjustConsumableBalance`, `UpsertUserSubscription`, etc.)
  sandwiched in between.
- **Symptom (reproduced live):** BUG-13 above already fixed and verified Stripe's documented
  *sequential* replay case (a retry/redelivery of the same event, processed one at a time) via
  `TransactionExistsByStripeEventId`. This is a different scenario: two deliveries of the
  **identical** `stripe_event_id` arriving **genuinely concurrently** (not sequentially - fired as
  backgrounded Python requests launched together, as could happen from e.g. a duplicated webhook
  endpoint registration, a load balancer fanning out one delivery to two app instances, or Stripe's
  own at-least-once delivery guarantee landing two copies within the same instant). Both requests
  passed `TransactionExistsByStripeEventId` (returned `false` for both, since neither had
  committed yet) before either had inserted its `PaymentTransaction` row, both applied their side
  effect, and only collided at the very end on `CreateTransaction`'s unique-index insert. Result:
  one consumable-purchase webhook for a 15-rose bundle credited the user's balance to **30** (double
  the purchase), with one request returning 200 `"Webhook processed."` and the other a raw HTTP
  500 from the losing `CreateTransaction`'s unique-violation - instead of the idempotent "already
  processed" response a sequential replay correctly gets.
- **Root cause:** The idempotency guard was a bare existence check, not a lock: "does a
  `PaymentTransaction` for this `stripe_event_id` already exist?" followed, several non-atomic
  repository calls and DbContexts later, by the insert that would make that check return `true`
  for a future caller. Nothing serialized two callers that both ask the question before either has
  answered it for the ledger. (This bug only became reliably reproducible, rather than
  intermittent, after the BUG-18 fix above removed the `AdjustConsumableBalance` race that had
  been *masking* it - before BUG-18's fix, the same concurrent-delivery scenario usually crashed
  or lost an update inside `AdjustConsumableBalance` itself before ever reaching the
  `CreateTransaction` collision this bug describes.)
- **Impact:** Directly monetary - a customer could be credited double the consumable units (or,
  for the subscription-checkout path, have `UpsertUserSubscription` applied twice, though that call
  is idempotent on its own and so has no visible symptom) for a single Stripe charge, with one of
  the two concurrent requests also surfacing an unhandled 500 instead of the clean idempotent
  response Stripe webhook consumers are documented to expect from a duplicate delivery.
- **Fix applied:** New `ISubscriptionRepository.AcquireEventLock(string stripe_event_id)`
  (`SubscriptionRepository.cs`, ~line 380) takes a session-scoped Postgres advisory lock
  (`pg_advisory_lock(hashtext(stripe_event_id))`) on a DbContext whose connection is explicitly
  kept open (`OpenConnectionAsync`) across the entire critical section, returning an
  `IAsyncDisposable AdvisoryLockHandle` that releases the lock (`pg_advisory_unlock`) and closes
  the connection on disposal. This differs from `MatchRepository`'s existing
  `pg_advisory_xact_lock` use in `RecordSwipe`/`CreateMatch` (transaction-scoped, released at
  commit) because the critical section here spans multiple separate `DbContextFactory`-created
  DbContexts across several repository calls, not one transaction - a session-scoped lock is the
  only shape that can hold across that. `HandleWebhook` now acquires this lock (keyed on the
  event's `stripe_event_id`, skipped entirely for the - defensively handled elsewhere - case of no
  `stripe_event_id` at all) immediately before the idempotency check and holds it via a
  `try/finally` across the idempotency check, the full event-type dispatch, and every handler's
  side effects plus its `CreateTransaction` insert, disposing it only after all of that completes
  (or throws). A genuinely concurrent second delivery now blocks on lock acquisition until the
  first has fully committed (including `CreateTransaction`), then correctly finds
  `TransactionExistsByStripeEventId` `true` and exits via the ordinary idempotent path - no double
  side effect, no 500.
- **Reverification (2026-10-26):** Fired 3 truly concurrent deliveries of the identical
  `stripe_event_id` for a 15-rose consumable purchase (backgrounded Python requests launched
  together) - exactly 1 returned 200 `"Webhook processed."`, the other 2 both returned 200
  `"Event already processed."`, final balance landed at exactly 15 (not 30/45), and exactly 1
  `PaymentTransaction` row exists for that event id. Repeated across 3 additional rounds of 5
  concurrent identical-event-id deliveries each - every round: 1 credit, 1 transaction row, all
  200s, zero 500s. Also reverified that BUG-11/BUG-12's existing advisory-lock-based fixes (in the
  unrelated `MatchRepository.RecordSwipe`/`CreateMatch` path) still hold under fresh concurrent
  load, confirming this new lock doesn't interact badly with the pre-existing ones. `dotnet build
  ProConnect.sln`: 0 errors.

---

## BUG-20 — `MatchService.BlockUser`/`MatchRepository.BlockUser` had no existence check and no concurrency lock [FIXED] — found running the `ENDPOINT_TEST_CASES.md` MatchController (M1-M10) pass

- **Where:** `MatchService.BlockUser` (ProConnect.Application/Service/MatchService.cs, ~line 608)
  and `MatchRepository.BlockUser` (ProConnect.Infrastructure/Repository/MatchRepository.cs,
  ~line 374 onward).
- **Symptom (reproduced live):** Two independent crashes behind the same endpoint. (1) Blocking a
  syntactically-valid but nonexistent `targetUserId` fell straight through to
  `MatchRepository.BlockUser`'s insert, which hit the `Blocks` table's FK constraint and surfaced
  as a raw unhandled-exception 500 instead of a clean "Target user not found." (2) Unlike
  `Swipe`/`Unmatch` (BUG-03/BUG-21's sibling concern), the existing "already blocked" no-op check
  and the insert ran inside one transaction but under no lock at all - two genuinely concurrent
  `BlockUser` calls for the identical (blocker, blocked) pair could both see `alreadyBlocked=false`
  and both attempt the insert; since `(blocker_user_id, blocked_user_id)` is the `Blocks` table's
  composite primary key, the loser's `SaveChangesAsync` threw an unhandled `DbUpdateException`
  wrapping a Postgres `23505` unique violation (raw 500) instead of the idempotent no-op the
  existing check was clearly meant to provide.
- **Root cause:** No analogue of `Swipe`'s existence check existed for `BlockUser` at all (missing
  precondition, same family as BUG-01/02/03/04), and the check-then-insert pair that *did* exist
  was unguarded by any lock, same shape as BUG-11/12's original swipe/match race before those were
  fixed.
- **Impact:** A client blocking a bad/stale `targetUserId`, or two concurrent block attempts (e.g.
  a double-tap on "Block" in the client), both degraded to a raw 500 instead of a clean,
  specific response.
- **Fix applied:** New `IUserRepository.Exists(Guid userid)` (`UserRepository.cs`, ~line 205) -
  deliberately a bare, unfiltered-by-`is_active` existence check (unlike `GetById`), since blocking
  a deactivated target must still succeed. `MatchService.BlockUser` now calls it before touching
  `MatchRepository.BlockUser` and returns `"Target user not found."` cleanly if it fails.
  Separately, `MatchRepository.BlockUser` now takes a `pg_advisory_xact_lock` keyed on the
  *ordered* `(blockerUserId, blockedUserId)` pair (not canonicalized/sorted, since blocking is
  directional - A blocking B and B blocking A are independent rows) before the existing
  check-then-insert, serializing a duplicate call against itself instead of racing.
- **Reverification:** Live-tested as part of the MatchController M1-M10 pass this session: blocking
  a random nonexistent `targetUserId` now returns a clean `"Target user not found."` instead of a
  500; a genuinely concurrent duplicate `BlockUser` call for the same pair now resolves to exactly
  one successful block with the second call observing the already-blocked no-op instead of a
  raw 500. `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-21 — Concurrent `Unmatch` + `SendMessage` can insert a message against a just-deleted match, or vice versa [FIXED] (G-24) — found running the MessageController (MSG1-MSG2) pass

- **Where:** `MessageService.SendMessage` / `IMessageRepository.SendMessageIfParticipant`
  (ProConnect.Application/Service/MessageService.cs ~line 39;
  ProConnect.Infrastructure/Interface/IMessageRepository.cs ~line 19) and
  `MatchRepository.Unmatch` (ProConnect.Infrastructure/Repository/MatchRepository.cs, ~line 348).
- **Symptom (reproduced live, G-24):** Sending a message used to be two separate, unlocked round
  trips: a plain `IsParticipant` check, then a plain insert referencing `matchid` as an FK. If a
  concurrent `Unmatch` deleted the `matches` row in between those two steps, the insert's FK
  reference pointed at a now-gone match and threw an unhandled `DbUpdateException`, surfacing as a
  raw 500 instead of a clean "Match not found."
- **Root cause:** Same check-then-write race shape as BUG-20/BUG-11/12 - the participant check and
  the message insert were not atomic with respect to a concurrent delete of the same match.
- **Impact:** A message send racing a genuine unmatch (realistic - either party can unmatch at any
  time) could crash with a raw 500 instead of the expected clean error, and in principle could also
  leave a message row that gets silently cascade-deleted moments later with no feedback to the
  sender about why.
- **Fix applied:** New `IMessageRepository.SendMessageIfParticipant(matchId, senderUserId, content)`
  combines the `IsParticipant` check and the insert into a single transaction guarded by a
  `pg_advisory_xact_lock` keyed on `matchId` - the *identical* lock key `MatchRepository.Unmatch`
  now also takes before its delete (BUG-20's fix pattern applied here too), so the two operations
  serialize instead of racing. Returns `null` if the sender is not a participant at the moment the
  lock is acquired (including a `matchId` just unmatched out from under the call);
  `MessageService.SendMessage` maps `null` to a clean `"Match not found."` response. This superseded
  the old plain `SendMessage`, which trusted a separate, unlocked `IsParticipant` call made moments
  earlier by the caller.
- **Reverification:** Live-tested as part of the MessageController MSG1-MSG2 pass this session:
  fired a `SendMessage` concurrently with an `Unmatch` on the same match repeatedly - every race
  resolved cleanly to either a successful send on a still-valid match or a clean "Match not found.",
  with zero raw 500s observed across repeated attempts. `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-22 — `NotificationPageRequest`/`TransactionPageRequest` had no validator registered, so `page<=0` leaked a raw Postgres error and `pagesize<=0` could deadlock pagination [FIXED] (G-4 / G-4b / G-27) — found running the NotificationController (N1-N6) pass

- **Where:** `ProConnect.Application/Validation/NotificationPageRequestValidator.cs` and
  `TransactionPageRequestValidator.cs` (both new files), registered in
  `ProConnect.WebApi/Program.cs` ~line 117.
- **Symptom (reproduced live, G-4/G-4b):** `GET /api/notification?page=0` (and the equivalent on
  the subscription transaction-history endpoint) flowed `page<=0` straight into EF's `.Skip()` as a
  negative offset, which Postgres itself rejects with `2201X: OFFSET must not be negative` - an
  unhandled exception leaking a raw 500 with a Postgres-specific error message, instead of a clean
  validation error. Separately (G-27), `pagesize=0` produced a `has_more` calculation that was
  always `true` regardless of how many results actually existed, a pagination dead-end a client
  could get stuck in.
- **Root cause:** Unlike every other paginated request type in the app
  (`MatchCandidatePageRequest`, `MessagePageRequest`), these two request types had no
  `IValidator<T>` implementation at all, and no manual guard existed in the corresponding service
  methods either - nothing stood between a bad `page`/`pagesize` and the repository call.
- **Impact:** Any client (or naive integration) passing `page=0`/`pagesize=0` on these two specific
  endpoints got a raw, implementation-leaking 500 or a non-terminating pagination loop, instead of
  the clean `450`/`VALIDATION` response every other paginated endpoint in the app already gives for
  the same bad input.
- **Fix applied:** New `NotificationPageRequestValidator`/`TransactionPageRequestValidator`, same
  bounds as the app's other paginated-request validators (`page >= 1`,
  `pagesize` in `[1, 50]`), registered as singleton `IValidator<T>` in `Program.cs` alongside the
  rest.
- **Reverification:** Live-tested as part of the NotificationController N1-N6 pass this session:
  `page=0` and `pagesize=0` against `GET /api/notification` both now return a clean `450`
  `VALIDATION` response instead of a raw 500 or an unbounded page; a valid page/pagesize still
  returns results normally. `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-23 — `RegisterDevice`/`UpsertDeviceToken` silently moved a device token away from a different user with no audit trail [FIXED] (G-9) — found running the NotificationController (N1-N6) pass

- **Where:** `NotificationService.RegisterDevice` (ProConnect.Application/Service/NotificationService.cs,
  ~line 210) and `NotificationRepository.UpsertDeviceToken`
  (ProConnect.Infrastructure/Repository/NotificationRepository.cs, ~line 153).
- **Symptom (reproduced live, G-9):** `UpsertDeviceToken` is deliberately keyed by `device_token`
  alone (not `(userid, device_token)`) to correctly support the shared/reset-device scenario - a
  second user registering the same physical device's token should take it over, not create a
  duplicate row. That re-pointing is intended behavior and correctly succeeded. What was missing was
  any record of it happening: if userid B's `RegisterDevice` call silently moved a token away from
  userid A (e.g. A sold/gave away a phone without logging out, or a flaky client re-registered under
  the wrong account), A would simply stop getting push notifications with zero trace anywhere of
  why.
- **Root cause:** The upsert path had no observability at all for the
  "token moved to a different owner" case specifically (as opposed to the normal
  "same user re-registering" case, which needs no comment).
- **Impact:** Not a functional defect (the takeover behavior is correct/intended) but a support and
  debugging gap - a user reporting "I stopped getting push notifications" for this reason was
  previously unexplainable from the logs alone.
- **Fix applied:** `NotificationRepository.UpsertDeviceToken` now returns the previous owner's
  `userid` (if any) alongside the upsert result; `NotificationService.RegisterDevice` logs a
  `LogWarning` when that previous owner differs from the caller, naming both userids, while leaving
  the takeover itself unchanged (still succeeds, still intended behavior).
- **Reverification:** Live-tested as part of the NotificationController N1-N6 pass this session:
  registered the same `device_token` for userid A, then for userid B - the call still succeeds for
  B (200, "Device has been registered for push notifications."), and the server log now contains a
  `Device token re-registered: userid 'B' took over a device_token previously owned by userid 'A'.`
  warning that did not exist before the fix. `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-24 — `UnregisterDevice` could never match a token containing a literal `/`, because the route left it percent-encoded while storage held it decoded [FIXED] (G-26) — found running the NotificationController (N1-N6) pass

- **Where:** `NotificationController.UnregisterDevice` (ProConnect.WebApi/Controllers/NotificationController.cs,
  ~line 73) and `NotificationService.UnregisterDevice`
  (ProConnect.Application/Service/NotificationService.cs, ~line 241).
- **Symptom (reproduced live, G-26):** A device token containing a literal `/` (e.g.
  `"simple/slash"`) could be registered fine via `POST .../devices` (stored decoded, exactly as
  received in the JSON body), but `DELETE .../devices/{device_token}` for that same token always
  returned "Device not found.", even immediately after registering it.
- **Root cause:** Confirmed live via diagnostic logging that Kestrel binds the
  `{device_token}`/`{*device_token}` route segment to the literal, **still percent-encoded** text
  (`"simple%2Fslash"`) without ever decoding it back into `/` during routing - a routing-pattern
  change alone (plain segment vs. catch-all) was never going to fix this, since neither one decodes
  `%2F`. The actual mismatch was downstream: the value `NotificationService.UnregisterDevice`
  received was still percent-encoded, while `RegisterDevice`/`UpsertDeviceToken` had persisted the
  raw, already-decoded token straight from the JSON body - so the two never compared equal.
- **Impact:** Any device token containing a literal `/` (plausible for some token formats, though
  FCM/APNs registration tokens typically don't contain one) could be registered but never
  unregistered through the normal API, leaving a stale device permanently subscribed to push
  notifications for that user with no way to remove it via this endpoint.
- **Fix applied:** `NotificationService.UnregisterDevice` now un-escapes exactly one sequence -
  `%2F`/`%2f` back to a literal `/` - before looking the token up, repairing the comparison without
  touching any other character; a no-op for the vast majority of tokens that never contain an
  encoded slash to begin with. The `{*device_token}` catch-all route segment was left in place
  (harmless/backward-compatible either way) since it was never actually the cause.
- **Reverification:** Live-tested as part of the NotificationController N1-N6 pass this session:
  registered a device token containing a literal `/`, confirmed `DELETE
  /api/notification/devices/{url-encoded-token}` now correctly finds and removes it (200, "Device
  has been unregistered.") instead of "Device not found.". `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-25 — `customer.subscription.deleted` webhook could leave (or re-set) a subscription as Active after Stripe had already deleted it [FIXED] (G-31) — found running the SubscriptionController (S1-S7) pass

- **Where:** `SubscriptionService.HandleWebhook` / `HandleSubscriptionStatusChanged`
  (ProConnect.Application/Service/SubscriptionService.cs, ~line 391 and ~line 523).
- **Symptom (reproduced live, G-31):** `customer.subscription.deleted` was dispatched through the
  exact same `payment_status`-driven status mapping as `customer.subscription.updated`. On an
  out-of-order/edge delivery where the embedded `Subscription.Status` snapshot in the `.deleted`
  payload still read `"active"`, that embedded status won the mapping and left (or even re-set) the
  local `user_subscription` row as `Active` - directly contradicting the fact that Stripe had
  already deleted the subscription object server-side.
- **Root cause:** `HandleSubscriptionStatusChanged` had no way to distinguish "trust the embedded
  status snapshot" (correct for `.updated`) from "the event type itself is the authoritative fact,
  ignore the snapshot" (required for `.deleted`) - both dispatch paths called it identically.
- **Impact:** A customer whose subscription Stripe had genuinely deleted (e.g. after exhausting
  retries on a failed payment, or an explicit cancellation finalizing) could be left with - or
  have restored to - full Active-tier access in ProConnect's own records, a direct entitlement/
  billing-integrity bug.
- **Fix applied:** New `trustEmbeddedStatus` parameter on `HandleSubscriptionStatusChanged`. The
  `customer.subscription.updated` dispatch passes `true` (unchanged behavior). The
  `customer.subscription.deleted` dispatch now passes `false` and a `fallbackStatus` of
  `SubscriptionStatus.Canceled` - when `false`, the embedded status snapshot is never consulted at
  all and `fallbackStatus` unconditionally wins, so a `.deleted` event always lands the local row as
  `Canceled` regardless of what the payload's embedded status string says.
- **Reverification:** Live-tested as part of the SubscriptionController S1-S7 pass this session:
  sent a `customer.subscription.deleted` webhook event with an embedded `Subscription.Status` of
  `"active"` against a previously-Active local subscription - the local row correctly transitioned
  to `Canceled`, not `Active`. `dotnet build ProConnect.sln`: 0 errors.

---

## BUG-26 — `SearchController`/`SearchRepository.Search` had no blocking exclusion at all [FIXED] (G-34) — found running the SearchController (SE1) pass

- **Where:** `SearchRepository.Search` (ProConnect.Infrastructure/Repository/SearchRepository.cs,
  ~line 35).
- **Symptom (reproduced live):** A user who had blocked another user (or been blocked by them, in
  either direction) still saw that user appear in full, unfiltered, in their own Search results.
  Confirmed live: searched as `jeffweiner08` for `query=Reid H` after he had blocked `reidhoffman` -
  Reid Hoffman's full profile still came back in the results.
- **Root cause:** Unlike `MatchRepository`, which excludes blocked-either-direction users from
  candidate listing, "who liked me", and resurfacing (three separate call sites, all using the
  identical `blockedEitherDirection` pattern), `SearchRepository.Search`'s base filter was only
  `u.userid != excludeUserId && u.is_active` - no blocking check of any kind.
  Search was simply never wired into the Block feature at all, the same category of gap BUG-08
  originally found for direct swipe.
- **Impact:** A user could be fully discoverable via Search by someone they'd specifically blocked
  (or who had blocked them) - the exact safety/privacy guarantee Block is supposed to provide,
  bypassed through one of the app's primary discovery surfaces.
- **Fix applied:** Added the identical `blockedEitherDirection` subquery (`db.Blocks` filtered to
  rows where either side is `excludeUserId`, projected to "the other party") already used three
  times in `MatchRepository.cs`, and extended `Search`'s base `query` filter to also exclude it.
- **Reverification:** Live-tested this session: after the fix, the same `query=Reid H` search as
  `jeffweiner08` (the blocker) now returns `total_count: 0`; the identical `query=Reid` search run
  as an unrelated third user (no block relationship) still correctly returns Reid Hoffman - confirming
  the fix is scoped to the actual blocking relationship, not overly broad. `dotnet build
  ProConnect.WebApi/ProConnect.WebApi.csproj`: 0 errors.

---

## BUG-27 — A failed geocode lookup during radius search silently dropped ALL location filtering (city/state/country text filters too), not just the radius - despite the code's own comment claiming otherwise [FIXED] (SE1-E3) — found running the SearchController (SE1) pass

- **Where:** `SearchRepository.Search` (ProConnect.Infrastructure/Repository/SearchRepository.cs,
  ~line 66 onward, before the fix).
- **Symptom (reproduced live):** Searching with `radius_km` set and a `city` value Nominatim could
  not resolve to coordinates (e.g. a nonsense string) returned **every active user in the system**,
  completely unfiltered by location - not just ignoring the radius (as documented/intended) but
  also skipping the city/state/country text-match fallback the code's own comment explicitly
  claimed would apply ("falls through to any text filters below"). Live test: a nonsense `city` +
  `radius_km=50` returned all 5 active test users, including ones with completely unrelated real
  cities.
- **Root cause:** The text-filter block genuinely lived in the `else` branch of
  `if (request.radius_km.HasValue)` - i.e. it could only ever run when `radius_km` was *not* set.
  Whenever `radius_km` *was* set and the geocode lookup failed, control fell out of the `if` block
  entirely with no filter of any kind applied, despite the inline comment asserting a text-filter
  fallback that the control flow made structurally unreachable.
- **Impact:** Worse than "radius is silently ignored" (the documented/intended behavior per
  `ENDPOINT_TEST_CASES.md` SE1-E3 and the code's own comment) - a geocoding hiccup on a real city
  name (transient Nominatim failure/timeout, not just a nonsense input) would have produced the
  same fully-unfiltered "every active user" result set instead of gracefully degrading to a text
  match on the city/state/country the caller actually provided.
- **Fix applied:** Extracted the city/state/country `ILike` text-filter block into a shared
  `ApplyTextLocationFilters(query, request)` helper, and now call it from **both** the
  `radius_km`-not-set `else` branch (unchanged behavior) and the new `else` of the geocode-success
  check (the branch that previously did nothing at all), so a geocode miss now actually falls back
  to the text filters the comment always claimed it did.
- **Reverification:** Live-tested this session, before/after: before the fix, a nonsense `city` +
  `radius_km=50` returned `total_count: 5` (everyone); after the fix, the identical request returns
  `total_count: 0` (correctly, since no active user's `city` matches the nonsense string) - and a
  regression check confirmed the true-geocode-success radius path (SE1-H4) and the no-radius text
  path were both unaffected. `dotnet build ProConnect.WebApi/ProConnect.WebApi.csproj`: 0 errors.

---

## BUG-28 — `query`/`industry`/`company`/`city`/`state`/`country` search filters let a caller's own `%`/`_` act as SQL `LIKE` wildcards instead of literal characters [FIXED] (G-36) — found running the SearchController (SE1) pass

- **Where:** `SearchRepository.Search` / `ApplyTextLocationFilters`
  (ProConnect.Infrastructure/Repository/SearchRepository.cs).
- **Symptom (reproduced live):** `GET /api/search?industry=_` (a literal underscore, presumably
  intended as literal search text) matched **every** active user with any non-empty `industry`
  value - none of whose industries actually contained a literal underscore. Confirmed live:
  `industry=_` returned 3 users (every user with a non-null industry) vs. the correct 0; `industry=%`
  behaved the same way for the same reason.
- **Root cause:** Every `ILike` call site built its pattern via raw string interpolation
  (`$"%{request.industry}%"`), so a caller-supplied `%` or `_` was passed straight through to
  Postgres' `LIKE`/`ILIKE` as the wildcard metacharacters they are, rather than as literal text to
  search for. Not a security/injection issue (parameterized via EF's `ILike` translation, and
  results are still scoped by `is_active`/self-exclusion/blocking), but a genuine correctness bug:
  a caller's own filter value could unintentionally broaden their search far past what they
  actually typed.
- **Impact:** Any filter value containing a literal `%` or `_` (plausible in real company/industry
  names, e.g. `"Smith_Co"`, not just adversarial input) silently returned a broader result set than
  the caller asked for, with no error or indication anything was off.
- **Fix applied:** New `EscapeLikePattern` helper escapes `\`, `%`, and `_` (backslash-prefixed) in
  every user-supplied filter value before it's interpolated into a pattern, paired with the
  3-argument `EF.Functions.ILike(column, pattern, escapeChar: "\\")` overload so Postgres treats the
  escaped sequences as literal characters. Applied uniformly to all six `ILike` call sites
  (`query`'s four OR'd fields, `industry`, `company`, and the three fields now routed through
  `ApplyTextLocationFilters`: `city`, `state`, `country`).
- **Reverification:** Live-tested this session, before/after: before the fix, `industry=_` returned
  `total_count: 3`; after the fix, the identical request returns `total_count: 0`. Regression
  checks confirmed ordinary substring matching (`industry=Internet` → 2 results) and a literal
  `industry=%` (→ 0 results, correctly no industry contains a literal `%`) both behave correctly.
  `dotnet build ProConnect.WebApi/ProConnect.WebApi.csproj`: 0 errors.

---

## Notes

- Fifteen bugs documented in total (twelve from the original 2026-10-23/24 pass, BUG-13 found
  during the 2026-10-26 re-verification pass, BUG-14 found later the same day via a direct
  question about LinkedIn-email-based duplicate signups, and BUG-15 found in a same-day follow-up
  pass prompted by the question "are there any other untested edge cases?" after BUG-14 shipped -
  that same follow-up pass also caught and corrected a real gap in the BUG-14 fix itself, see the
  addendum at the end of the BUG-14 entry above). BUG-01 through BUG-04 share one pattern: **missing
  precondition/existence checks (or missing idempotency awareness) before a DB write**, surfaced
  as generic 500s via `ExceptionHandlingMiddleware` instead of clean, specific error responses —
  despite the codebase clearly having the infrastructure for clean errors elsewhere (self-like
  check, min/max age validation, `SignUpValidator`).
- BUG-05/06/07 are all Stripe-adjacent and share a second pattern: **the payment gateway is
  treated as infallible** — no specific handling for `StripeException`, no idempotency guard for
  already-terminal subscription states, and a controller doc comment that promises behavior
  (400 on bad signature) the code doesn't actually implement.
- BUG-08 is its own category: a **safety/authorization feature (Block) that is correctly
  enforced in three read paths but was simply never wired into the one write path** (direct
  swipe) that most directly matters for user safety. Recommend prioritizing BUG-08 fix alongside
  BUG-04 (billing) as the two most impactful findings; BUG-01/02/03 and BUG-06/07 are the same
  "add a precondition, return a clean error" shape and cheap to batch together; BUG-05 is the
  lowest urgency (cosmetic/info-disclosure in this dev context) but still worth fixing before any
  production Stripe integration.
- BUG-09 is a fourth, distinct category: **external-data trust without validation** — the only
  bug of the nine found via data auditing (diffing requested vs. persisted identifiers across a
  signup batch) rather than via an HTTP error response, and the only one where the app actively
  reports success while quietly persisting wrong/broken data. Given it affects real production
  signups (not just a dev-mode Stripe/FCM stub), recommend treating this as equally high
  priority to BUG-04/BUG-08.
- BUG-10 was the only bug found exclusively through genuine concurrent load (20 real users
  firing dozens of simultaneous Likes) rather than single-user sequential testing - it was
  invisible in every single-user MATCH test case despite exercising the exact same
  `TryConsume`/`IncrementFeatureUsage` code path many times. It shares BUG-01-04's "unhandled
  DB exception becomes a generic 500" surface shape, but the underlying defect is a true
  read-then-write race condition, not a missing precondition check - this is the strongest
  evidence in the whole test pass for why the planned bulk/scale pass was necessary in addition
  to the single-user suite.
- BUG-11 is a close cousin of BUG-10 (same `Swipe`/`TryConsume` neighborhood, same bulk-test
  discovery method) but a distinct defect: a missing idempotency check rather than a race
  condition, and it undercounts (user-favorable) rather than crashing. Recommend fixing BUG-10
  and BUG-11 together, in the same code review, since both touch `MatchService.Swipe`'s Like
  path and `IFeatureGateService.TryConsume`.
- BUG-12 is a third sibling in the same `MatchService.Swipe` neighborhood as BUG-10/BUG-11, and
  the second finding (after BUG-10) that only surfaced because of genuine concurrent load rather
  than sequential single-user testing - specifically the "reciprocal_race" probe designed into
  the bulk test. Recommend fixing BUG-10, BUG-11, and BUG-12 as one batch: all three stem from
  the same underlying gap (an idempotent write whose caller doesn't know whether it was a no-op)
  in the Like/match code path, just manifesting on three different side effects (quota charge,
  swipe duplication risk, and now notifications).
- BUG-13 is a fourth sibling of the BUG-10/11/12 family, discovered only once BUG-01 through
  BUG-12 were already fixed and a dedicated re-test pass was run against the patched build: same
  underlying shape (an idempotent write — here, the webhook-replay guard itself — whose
  participating handler didn't fully opt in), same discovery method (deliberately replaying a
  request rather than single-shot sequential testing), different subsystem (webhook idempotency
  rather than the Like/match path). This is a reminder that fixing a bug family in one code path
  doesn't guarantee every sibling path was already covered — worth a final idempotency audit
  across all `HandleWebhook` dispatch targets if any new webhook event type is added later.
- BUG-14 is its own category, distinct from every bug above: a **missing uniqueness guarantee
  on a user-identity field that isn't the primary auth key**. BUG-01's `sub` check already
  covered the LinkedIn-OIDC-identity axis; `email` is a second, independent identity axis that
  had zero protection at either the application or database layer until this fix. Found not
  through code auditing or bulk/scale testing, but by directly asking whether this specific case
  had been tested — worth remembering that "every bug found so far was found via X method" does
  not mean X method is exhaustive; it's worth periodically asking "what identity/uniqueness axis
  have we *not* checked yet" even after a thorough pass.
- BUG-15 is a fifth sibling of the BUG-01/09/10/11/12/13 "needed genuine live reproduction, not
  just a build-passing state" family, but closest in spirit to BUG-10: a true read-then-write
  race, not a missing precondition check (the preconditions - BUG-01's and BUG-14's checks - were
  already present and correct; the gap was that nothing caught the exception on the rare occasion
  they both raced past those checks anyway). Also notable for *how* it was found: not via bulk
  load or an HTTP probe, but by directly asking "is there anything about this fix that wasn't
  tested" immediately after shipping BUG-14 - the same sibling finding (the BUG-14 field-mismatch
  correction, documented as an addendum to the BUG-14 entry rather than its own bug number since
  it's a correction to an unreleased-to-production fix from earlier the same day, not an
  independent defect) came from the exact same question. Recommend treating "what did we *not*
  test about the fix we just shipped" as a standing follow-up question after every bug fix, not
  just a one-time exercise.
- **CRITERIA-04** (`MatchCriteriaRequest.min_age`/`max_age` accepting `min_age > max_age` and
  negative ages with no validation - flagged as a non-bug gap during the 2026-10-25 pass, never
  filed as a numbered bug since it was a silent data-quality issue rather than a crash) was also
  fixed in this same follow-up session: new `MatchCriteriaRequestValidator`
  (`ProConnect.Application/Validation/MatchCriteriaRequestValidator.cs`), registered in
  `Program.cs` alongside the other `IValidator<T>` singletons, enforcing `min_age >= 18` (matching
  `SignUpValidator`'s existing adult requirement), `max_age <= 120`, `min_age <= max_age` when
  both are supplied, and `radius_km > 0` when supplied - every rule conditioned on the field
  actually being present, since every field on `MatchCriteriaRequest` is optional. Reverified live
  against `PUT /api/match/v1/criteria`: `min_age=40,max_age=25` and `min_age=-5` both now return a
  clean `450`/`VALIDATION` error instead of silently saving; a valid range still saves normally.
- BUG-16 is its own category: a **third-party SDK throwing an undocumented exception type**
  (`Stripe.net`'s `EventUtility.ConstructEvent` throwing a bare `NullReferenceException`, not a
  `StripeException`, on a null `api_version`) slipping past a catch block that was deliberately
  scoped to the exception type the SDK's own public contract promises. Same "a 500 shouldn't be
  the final answer for a deterministically-bad request" lesson as BUG-01/02/03/06, just one layer
  further into a dependency's internals - worth remembering that narrowly-typed catch blocks
  around third-party calls should be paired with a broader fallback for the specific call site,
  not just the method as a whole, when the dependency's exception contract isn't fully trustworthy.
- BUG-17 is a direct sequel to BUG-10/11/12: it was found specifically by re-probing the exact
  scenario BUG-10 was fixed for (concurrent Likes from one user) *after* that fix shipped, and
  discovering that making the increment atomic was necessary but not sufficient - the
  admission-control decision built on top of it (read usage, check against limit, then increment)
  was still a three-step non-atomic sequence. This is the clearest evidence in the whole project
  that "the write is now atomic" and "the business rule enforced by composing several calls
  around that write is now atomic" are two different claims, and fixing the first doesn't
  automatically fix the second - worth a standing check, any time an atomic upsert is introduced
  to fix a race, for whether anything upstream of it still does a non-atomic check-then-call.
- BUG-18 is a near-exact sibling of BUG-10 (same defect shape: non-atomic read-then-write on a
  table with a composite primary key, same crash-or-lose-silently dual symptom) but in a
  *different* repository method (`AdjustConsumableBalance` vs. `IncrementFeatureUsage`) that
  BUG-10's own fix and reverification never touched, because BUG-10's bulk/scale test plan had no
  concurrent consumable-purchase traffic in it. Found only because this session's live
  concurrency probing deliberately extended past the Like/match path BUG-10/11/12 already covered
  - a reminder that "we already fixed this exact bug class once" doesn't mean every method with
  the same vulnerable shape was caught by that fix, especially when the original discovery method
  (a specific load-test scenario) never exercised the sibling code path at all.
- BUG-19 looks like a second instance of BUG-13 (both are "webhook replay isn't fully idempotent")
  but is a genuinely distinct defect once examined closely: BUG-13 was about a handler that never
  joined the idempotency ledger at all (missing a `CreateTransaction` call), reproducible with
  ordinary *sequential* replay; BUG-19 is about the ledger-check-then-ledger-write gap itself being
  non-atomic, only reproducible under genuine *concurrent* delivery of the identical event id -
  every handler already correctly joins the ledger, the gap is in the timing between checking it
  and writing to it. Notably, BUG-19 was partially masked by BUG-18 until BUG-18 was fixed: before
  that fix, the same concurrent-delivery scenario usually surfaced as an `AdjustConsumableBalance`
  crash or lost update before ever reaching the `CreateTransaction` collision BUG-19 describes -
  fixing one race fully exposed the next one underneath it, which is why this session treated
  "the symptom changed after a fix, not just disappeared" as a signal to keep investigating rather
  than a sign the investigation was done.
- **All nineteen bugs (plus the CRITERIA-04 validation gap) are now FIXED and reverified** as of
  2026-10-26. Each bug entry above has a
  "Reverification" subsection naming the exact in-code fix location and the specific live-test or
  bulk/scale-test evidence confirming it holds under real concurrent load, not just a single
  sequential request. The two sub-fixes discovered only during the initial reverification pass
  (the `UserService.SignUp` empty-profile check under BUG-09, and the `NotifyNewLike` re-Like
  duplicate-notification gating under BUG-11/12) are documented inline in their parent bugs'
  reverification notes rather than given separate bug numbers, since the current source code
  itself labels them as sub-fixes of those bugs (`"BUG-09 fix (follow-up discovered during
  reverification)"` / `"Found during BUG-11 reverification"`). BUG-16 through BUG-19 were found by
  actually starting the live API against real Postgres/Redis and exercising it end-to-end (both
  single-request and genuinely concurrent traffic) rather than by re-reading prior documentation
  or git history - BUG-11 and BUG-12's existing advisory-lock fixes were also explicitly
  re-verified live during this pass and confirmed to still hold, with no regression. See
  `TEST_RUN_LOG.md` for the full re-test pass (SIGNUP/AUTH/PROFILE/CRITERIA/MATCH/NOTIF/SUB
  single-user suite, the 19-user bulk/scale pass, the dedicated BUG-14 email-duplicate-signup case,
  and this latest live concurrency-focused pass) this update is based on, and `PROJECT_NOTES.md`
  for the changelog entries.
- **BUG-20 through BUG-28 (full `ENDPOINT_TEST_CASES.md` execution pass, 2026-10-26, part 2)**
  were all found by running every case in `ENDPOINT_TEST_CASES.md` live against the running API,
  in controller order (User, Account, Match, Message, Notification, Subscription, Search), rather
  than by re-reading prior documentation. BUG-20/21 are a matched pair with BUG-11/12/19's pattern
  (unguarded check-then-write races) applied to two paths those earlier bugs' own fixes never
  touched (`BlockUser`, `SendMessage` vs. concurrent `Unmatch`). BUG-22 is a straightforward
  missing-validator gap, same shape as the missing-precondition family (BUG-01 through BUG-04) but
  for pagination parameters instead of entity existence. BUG-23 is the same category as BUG-05
  (an observability gap, not a functional defect - the underlying takeover behavior was always
  correct and intended). BUG-24 is notable for how it was diagnosed: the obvious-looking fix
  (changing the route from a plain segment to a catch-all) was tried first and confirmed, via
  diagnostic logging added temporarily to the controller, to not be the cause at all - Kestrel
  never decodes `%2F` during routing regardless of route shape; the real mismatch was one layer
  deeper, in `UnregisterDevice` comparing a still-encoded value against a repository that stored
  the decoded one. BUG-25 is a sibling of BUG-13/19's webhook-handling theme but about which
  signal (event type vs. embedded payload snapshot) should be authoritative, not about idempotency.
  BUG-26 is a direct sibling of BUG-08 (a safety feature - Block - correctly wired into some paths
  but never wired into another), this time Search instead of direct Swipe. BUG-27 is unusual in
  that the bug was in the code's own explanatory comment being wrong about what the code actually
  did - the comment described an intended fallback that the control-flow structure made literally
  unreachable. BUG-28 is a straightforward input-sanitization gap (unescaped SQL LIKE
  metacharacters) with real but limited impact (broadened results, not an injection/security
  issue, since every call site uses parameterized `ILike` translation). All nine were reproduced
  live before any fix and reverified live after, per each entry's Reverification subsection above;
  `dotnet build` reports 0 errors after the full set of changes.
- **Gaps confirmed real-but-intentionally-unfixed during this pass** - each a deliberate design
  tradeoff the codebase and/or `ENDPOINT_TEST_CASES.md` itself frames as "worth testing" rather
  than "broken", so fixing them would mean overriding an intentional decision, not correcting a
  defect: G-10 (`GetPlans` requires auth - a product decision, not an oversight), G-11 (a
  misconfigured Stripe price id surfaces as a specific, logged error rather than failing silently -
  acceptable), G-12 (no upper bound on consumable purchase quantity - a pricing/business decision,
  not a correctness bug), G-25 (`MarkAllAsRead`'s in-memory load-all is an accepted perf tradeoff
  at current scale), G-28 (a webhook's `periodEnd` is never corrected after the fact - a documented
  limitation of the current webhook model; this gap number is unrelated to, and should not be
  confused with, this update's new BUG-28 above, which is a different issue entirely), G-32
  (`invoice.payment_succeeded` while `PastDue` doesn't auto-reset the subscription to `Active` -
  confirmed live; the doc itself hedges this as defensible, since auto-reactivating on one
  successful invoice could mask a still-failing payment method), G-33 (silent no-op when a
  `checkout.session.completed` webhook carries neither `plan_code` nor `consumable_key` -
  confirmed live; left as-is per the doc's own hedged "should probably Warning-log" framing), and
  G-35 (no feature-gate/paywall on Search's radius mode - confirmed via code read and live radius
  searches succeeding with no subscription at all; whether radius search should be a paid feature
  is a product decision this test pass has no authority to make, so it is reported rather than
  silently "fixed" by adding a paywall the product may not want).
- **G-29/G-30 turned out to already be fixed, not live bugs.** Both describe symptoms that the
  prior session's BUG-19 fix (the `AcquireEventLock` advisory-lock fix for concurrent identical
  `stripe_event_id` webhook deliveries) already resolves as a side effect; live-testing the exact
  scenarios these two gaps describe during this pass found both already behaving correctly.
  `ENDPOINT_TEST_CASES.md` is simply stale on these two entries - a documentation-staleness
  finding, not a newly-discovered bug - and should be updated to note that BUG-19's fix already
  covers them.
- **Test-document inaccuracies found** (not code bugs - `ENDPOINT_TEST_CASES.md` itself is wrong):
  (1) the SearchController section references `lat`/`long` query parameters that do not exist
  anywhere in `SearchRequest.cs`/`SearchRequestValidator.cs` - the real mechanism is that
  `radius_km` triggers server-side geocoding of the supplied `city`/`state`/`country` via
  `IGeocodingProvider`, with no client-supplied coordinates accepted at all; (2) a prior session's
  N3-H2 case likewise referenced behavior that doesn't match the current
  `NotificationController`/`NotificationService` implementation. Both should be corrected directly
  in `ENDPOINT_TEST_CASES.md`; neither required any code change.
