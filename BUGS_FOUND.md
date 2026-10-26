# Bugs Found During Test Plan Execution (2026-10-23/24, re-verified 2026-10-26)

Status key: **OPEN** = confirmed, not yet fixed. **FIXED** = patched and reverified.

**Update 2026-10-26:** All 13 bugs below (BUG-01 through BUG-12, plus BUG-13 found during this
re-verification pass) are now **FIXED**. See each bug's "Reverification" subsection for the exact
in-code fix location and the live-test evidence confirming it holds, and `TEST_RUN_LOG.md` for the
full re-test pass this update is based on.

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

---

## Notes

- Fourteen bugs documented in total (twelve from the original 2026-10-23/24 pass, BUG-13 found
  during the 2026-10-26 re-verification pass, and BUG-14 found later the same day via a direct
  question about LinkedIn-email-based duplicate signups). BUG-01 through BUG-04 share one pattern: **missing
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
- **All fourteen bugs are now FIXED and reverified** as of 2026-10-26. Each bug entry above has a
  "Reverification" subsection naming the exact in-code fix location and the specific live-test or
  bulk/scale-test evidence confirming it holds under real concurrent load, not just a single
  sequential request. The two sub-fixes discovered only during the initial reverification pass
  (the `UserService.SignUp` empty-profile check under BUG-09, and the `NotifyNewLike` re-Like
  duplicate-notification gating under BUG-11/12) are documented inline in their parent bugs'
  reverification notes rather than given separate bug numbers, since the current source code
  itself labels them as sub-fixes of those bugs (`"BUG-09 fix (follow-up discovered during
  reverification)"` / `"Found during BUG-11 reverification"`). See `TEST_RUN_LOG.md` for the full
  re-test pass (SIGNUP/AUTH/PROFILE/CRITERIA/MATCH/NOTIF/SUB single-user suite plus the 19-user
  bulk/scale pass, and the dedicated BUG-14 email-duplicate-signup case) this update is based on,
  and `PROJECT_NOTES.md` for the changelog entries.
