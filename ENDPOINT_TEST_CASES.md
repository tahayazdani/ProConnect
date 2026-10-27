# ProConnect API — Full Endpoint Test Catalog

Exhaustive happy-path / unhappy-path / edge-case test cases for **every endpoint on every controller**,
derived from reading the actual controller/service/repository/validator source (not guesses). Each case
states the exact expected HTTP status, response-body shape, and expected log line(s), based on how the
code actually behaves today (after the CRITERIA-04 + BUG-01..15 fixes already shipped to `develop`).

This supersedes `TEST_PLAN.md`, which predates this research and in places assumes HTTP status codes the
code does not actually return.

## 0. How to read this document

- **TC ID scheme**: `<CTRL><endpoint#>-<H|U|E><n>` — H=happy, U=unhappy, E=edge/gap. e.g. `USR1-H1`.
- **"Envelope"** = `ProConnectResponse<T>`: `{ body, message, statuscode, errormessage, errors[], issuccessfull }`.
  `statuscode` defaults to `200`, `issuccessfull` defaults to `true`. The controller action always does
  `return StatusCode(response.statuscode, response)` — so **the HTTP status you receive is whatever the
  service put in `response.statuscode`**, not an ASP.NET convention.
- Every log line listed is the **minimum** guaranteed line for that scenario; `LoggingMiddleware` additionally
  logs one Information-level line per request/response pair with full headers+body (see §1.5) — omitted from
  each row below to avoid repeating it 39×, but it is real and always fires for any request that completes
  the pipeline.
- "Validator" = FluentValidation `IValidator<T>` wired into the global `ValidateModelStateAttribute` filter.
  "No validator registered" means FluentValidation never runs for that request type — only manual null/empty
  checks inside the service (if any) can reject the input.

## 1. Cross-cutting behavior (applies to every endpoint below)

### 1.1 Response envelope & status codes
Virtually all business-rule rejections across the whole codebase set `issuccessfull=false` but leave
`statuscode` at its default — meaning **the HTTP status is 200 even for a rejected/failed request**. There
are exactly **three** places in the entire reviewed codebase that deviate from plain 200:
1. `ValidateModelStateAttribute` — any FluentValidation failure → **HTTP 500**, double-JSON-encoded body (see §1.3).
2. `ExceptionHandlingMiddleware` — any unhandled exception anywhere in the pipeline → **HTTP 500**.
3. `SubscriptionService.HandleWebhook`'s bad-signature branch → the **only explicit non-200/500**, HTTP **400**.

So for nearly every "unhappy path" test case in this document, **do not expect a 4xx** — expect `200` with
`issuccessfull=false` and a populated `errormessage`/`message`, unless the row explicitly says 500/400.

### 1.2 Auth
Global fallback policy requires a valid JWT bearer token for every action **except** those marked
`[AllowAnonymous]` (listed per-endpoint below). `UserController.Ping`/`AccountController.Ping`,
`UserController.GenerateConsentLinkedInLogInPage`/`LinkedInAuthentication`/`LinkedInSignUp`/`SignUp`, and
`SubscriptionController.Webhook` are the only anonymous endpoints in the whole API.
- **Missing/invalid/expired JWT on a protected endpoint → HTTP 401.** This 401 is produced by
  `app.UseAuthorization()`, which runs *before* `MapControllers()` and *before*
  `ExceptionHandlingMiddleware`/`LoggingMiddleware` in `Program.cs`'s pipeline order. **Unverified test case
  (flagged, not yet empirically confirmed): a 401 may never reach `LoggingMiddleware`, i.e. 401s may not be
  logged at all.** → see Gap **G-13**.
- `BaseController.CurrentUserId` reads claim `"userid"` and falls back to `Guid.Empty` via `Guid.TryParse`
  if the claim is missing/unparsable — this does **not** 401; a validly-signed token simply missing
  `"userid"` sails through auth and resolves to `Guid.Empty` downstream. Every domain that calls this is
  exposed to this edge case — see Gap **G-14**.

### 1.3 Validation-pipeline quirk (FluentValidation failures)
When a request type **has** a registered validator and validation fails:
`ValidateModelStateAttribute` builds an envelope with `statuscode=450` *inside the body*, but wraps it in
`new OkObjectResult(json){ StatusCode = 500 }` — so the **actual wire-level HTTP status is 500**, and the
body is **double-JSON-encoded** (a JSON string containing escaped JSON, because the pre-serialized string is
serialized again by MVC's formatter). Body fields when this fires: `errormessage="Validation"`,
`message="Something went wrong, Please call administrator."`, `issuccessfull=false`,
`errors=[{errormessage, errorcode}, ...]` one per broken rule.
Log: `LogValidationFilter` logs **Warning**: `JsonConvert.SerializeObject(context.Result)`.
When a request type has **no validator registered**, this filter never fires at all — only manual
null-checks inside the service layer (if present) can reject the input, and those return plain 200
`issuccessfull=false`, not 500.
Pure ASP.NET model-binding failures (malformed route `Guid`, wrong JSON types, bad enum values) are caught
by **neither** mechanism, since `SuppressModelStateInvalidFilter=true` disables ASP.NET's own automatic 400.
**Behavior for these is unverified — flagged as a dedicated edge case per affected endpoint below (G-6).**

### 1.4 Unhandled exceptions
`ExceptionHandlingMiddleware` catches everything else: `LogError(ex, "Something went wrong...")`, HTTP 500,
body `{ body:"", statuscode:500, errormessage:<raw exception.Message>, message:"Something went wrong, Please call administrator.", errors:null, issuccessfull:false }`
— note this **leaks the raw exception message** verbatim to the client (Gap **G-7**).
Because almost every service method's own `catch` block logs a specific Error-level message (naming the
operation and the relevant id) before rethrowing, **every unhandled-exception scenario in this document
produces exactly two Error-level log lines**: the service's own, then the middleware's generic one. Both are
listed per row below.

### 1.5 Logging middleware
`LoggingMiddleware` runs last in the pipeline and logs the **full raw request and response — headers and
body** — at **Information** level for every request that reaches it. This includes raw `Authorization: Bearer <jwt>`
headers, any PII in request/response bodies, and freshly-issued JWTs in sign-up/sign-in responses
(Gap **G-8**, privacy/security concern — flagged, not fixed).

### 1.6 Empty-stub controllers — explicitly zero endpoints, zero test cases possible
The following controllers exist as empty classes with **no actions at all**:
- `FollowerController` — `public class FollowerController : BaseController { }`
- `FollowingController` — `public class FollowingController : BaseController { }`
- `PaymentController` — `public class PaymentController : BaseController { }`

**No test cases exist or can exist for these three controllers until endpoints are implemented.** Any test
plan claiming coverage for "follower/following/payment APIs" today is testing nothing — there is no route to
hit. This is itself worth flagging to product/QA: three domains implied by the controller names are entirely
unimplemented.

### 1.7 Endpoint inventory
7 real controllers, **39 real endpoints** total: User (8), Account (5), Match (10), Message (2),
Notification (6), Subscription (7), Search (1). Plus the 3 empty controllers above (0 endpoints).

---

## 2. UserController — base route `/api/user/v1`

### U1. GenerateConsentLinkedInLogInPage
**GET** `/generateconsentlinkedinloginpage` — `[AllowAnonymous]`. No body/query/validator. Builds a LinkedIn
OIDC consent URL with a `state` param; the outbound HTTP call to LinkedIn has a 100s timeout.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| U1-H1 | Happy | Normal call | 200 | `issuccessfull=true`; `body`=consent URL string containing `client_id`, `redirect_uri`, `state`, `scope` | none beyond §1.5 |
| U1-U1 | Unhappy | Downstream/LinkedIn-config exception inside the 100s-timeout call | 500 | `issuccessfull=false`; `errormessage`=raw exception text | 2× Error (service + `ExceptionHandlingMiddleware`, §1.4) |
| U1-E1 | Edge | The returned `state` is **never validated** when `LinkedInAuthentication` later receives it back — classic CSRF gap. Not testable in isolation; cross-ref **G-1** and test case **U2-E1**. | n/a | n/a | n/a |

### U2. LinkedInAuthentication
**GET** `/linkedinauthentication?code=&state=` — `[AllowAnonymous]`. No validator. Exchanges `code` for a
LinkedIn access token, fetches userinfo, extracts email+sub, finds/creates the user, issues a fresh JWT.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| U2-H1 | Happy | Valid `code`; existing active user matched by `sub` | 200 | `issuccessfull=true`; `body`=JWT + user summary | fire-and-forget cache warm-up triggered, no Error/Warning |
| U2-H2 | Happy | Valid `code`; existing user was `is_active=false` | 200 | `issuccessfull=true`; same as H1 | LogInformation: `"Userid '{id}' signed back in and was reactivated (was previously deactivated)."` |
| U2-U1 | Unhappy | LinkedIn token-exchange or userinfo call returns non-2xx (bad/expired `code` or `state`) | 200 | `issuccessfull=false`; `message`="LinkedIn authentication failed. Please try signing in again." | LogWarning: `"LinkedIn token exchange or userinfo call returned a non-2xx response (likely a bad/expired code or state)."` |
| U2-U2 | Unhappy | Token response missing `access_token` | 200 | `issuccessfull=false`; `message`="Failed to obtain an access token from LinkedIn." | — (handled inline, no Warning/Error) |
| U2-U3 | Unhappy | Userinfo response missing `sub` | 200 | `issuccessfull=false`; `message`="The user is not exist in LinkedIn." | — |
| U2-U4 | Unhappy | Unhandled exception (e.g. LinkedIn outage) | 500 | `issuccessfull=false`; raw exception leaked | 2× Error: service `"LinkedInAuthentication failed."` + middleware |
| U2-E1 | Edge | `state` param is accepted but **never checked against the one issued in U1** — CSRF token is vestigial. A forged/replayed `state` is accepted as long as `code` is valid. | 200 (if code valid) | normal success body | none — this is the silent gap, see **G-1** |
| U2-E2 | Edge | Lookup (`GetBySubForAuthentication`) is **not filtered by `is_active`** — a deactivated user with a matching `sub` is found and silently reactivated (same as U2-H2); there is no separate "deactivated, please contact support" branch. | 200 | success + reactivation log | LogInformation reactivation line |
| U2-E3 | Edge | Malformed/empty `code` query param — pure model-binding, no validator exists for this action's primitive params | unverified | — | — → **G-6** |

### U3. SystemAuthentication
**GET** `/systemauthentication` — requires JWT (not `[AllowAnonymous]`). Re-validates the current token's
user against the DB (cached, `is_active`-filtered `GetById`); does **not** issue a new token.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| U3-H1 | Happy | Valid JWT for an active, existing user | 200 | `issuccessfull=true`; `body`=user summary (same token, not reissued) | none |
| U3-U1 | Unhappy | JWT valid but user no longer exists or `is_active=false` (deactivated after token was issued) | 200 | `issuccessfull=false`; `message`="User not found." | — |
| U3-U2 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error: `"SystemAuthentication failed for userid '{userid}'."` + middleware |
| U3-E1 | Edge | No/invalid JWT at all | 401 | (auth middleware short-circuit, not the envelope) | possibly unlogged — see **G-13** |
| U3-E2 | Edge | JWT valid but lacks `"userid"` claim → `CurrentUserId=Guid.Empty` → DB lookup for `Guid.Empty` → "User not found." | 200 | `issuccessfull=false`; "User not found." | none — silently treated as a normal not-found, see **G-14** |

### U4. Logout
**POST** `/logout` — requires JWT. Bare `return Ok()` — no body envelope, no logging, no side effects, no
token revocation.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| U4-H1 | Happy | Valid JWT | 200 | empty body (`Ok()`, not the `ProConnectResponse` envelope — inconsistent with every other endpoint) | none at all — not even an Information line from the action itself (§1.5 middleware line still fires) |
| U4-U1 | Unhappy | No/invalid JWT | 401 | n/a | possibly unlogged, **G-13** |
| U4-E1 | Edge | The JWT used to call Logout **remains valid** for its full lifetime afterward — there is no server-side revocation/blacklist. A client that calls Logout then replays the same token on any other endpoint will still succeed. | 200 on subsequent calls | normal success | none — documented design gap, **G-15** |

### U5. LinkedInSignUp
**GET** `/linkedinsignup` — `[AllowAnonymous]`. Synchronous, not async. Returns a **raw string**
`"https://www.linkedin.com/signup"` via `Ok(...)`, not the `ProConnectResponse` envelope.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| U5-H1 | Happy | Any call | 200 | raw string `"https://www.linkedin.com/signup"` — **not JSON-wrapped like every other endpoint**, a client expecting the envelope shape will break here | none |
| U5-E1 | Edge | Response-shape inconsistency vs. the rest of the API (no `issuccessfull`/`statuscode` fields at all) — flagged as a contract gap for any generic client-side response parser, **G-16** | 200 | raw string | none |

### U6. SignUp
**POST** `/signup` — `[AllowAnonymous]`. Body: `SignUpRequest`, validated by `SignUpValidator`
(global filter active — failures take the §1.3 path: HTTP 500, double-encoded body).

Validator rules: `email` — `.EmailAddress()` **without** `.NotEmpty()`, so an **empty email string passes
validation** (only a malformed non-empty one fails); `public_identifier` — `NotEmpty`; `birth_date` — two
separate `Must(...)` rules, **both fire simultaneously on a null birth_date** (2 distinct errors in `errors[]`
for one bad field); adult-check is a strict `<` comparison (exact-18th-birthday edge).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| U6-H1 | Happy | All fields valid, new `public_identifier`/email/sub, LinkedIn profile scrape succeeds | 200 | `issuccessfull=true`; `body`=JWT+user; `userid` always a **new** GUID regardless of any `userid` sent in request | fire-and-forget warm-up; no Warning/Error |
| U6-U1 | Unhappy | `email` empty string, everything else valid | 500 (validator bypassed the empty-email hole only for *malformed* emails, not empty — this is itself the hole) — **actually passes validation**, proceeds to service layer | 200 if no other service rejection | `issuccessfull` depends on downstream dedup; email-uniqueness check still runs on the empty string, so a **second** empty-email signup collides | none special |
| U6-U2 | Unhappy | `email` = `"not-an-email"` (malformed, non-empty) | 500 | double-encoded body; `errors=[{errormessage:"...", errorcode:"VALIDATION"}]` | Warning: `LogValidationFilter` serialized result |
| U6-U3 | Unhappy | `public_identifier` empty/whitespace | 500 | same validator-failure shape | Warning (validator) |
| U6-U4 | Unhappy | `birth_date` = null | 500 | `errors[]` contains **2** entries for birth_date (both Must-rules fire) | Warning (validator) |
| U6-U5 | Unhappy | `birth_date` = exactly 18 years before today (boundary) | depends on `<` vs `<=` — strict `<` means exactly-18-today **fails** the adult check (is NOT yet 18 by the strict comparison used) | 500 or 200-reject depending which layer owns the check — flagged, verify exact operator | — |
| U6-U6 | Unhappy | Scraped LinkedIn profile is null/empty (BUG-09 fix) | 200 | `issuccessfull=false`; exact rejection message from BUG-09 fix | LogWarning (BUG-09 exact text, see `BUGS_FOUND.md`) |
| U6-U7 | Unhappy | Request body itself is null (malformed JSON/empty body) | 200 | `issuccessfull=false`; null-request guard message | — |
| U6-U8 | Unhappy | Race: two concurrent SignUps with the same `sub`/email hit the DB unique index simultaneously (BUG-15 fix) | 200 | `issuccessfull=false`; clean conflict message (not a raw 500) | LogWarning: exact `DuplicateUserException` catch text from BUG-15 fix |
| U6-E1 | Edge | `sub` provided but a **different** existing user already owns that `sub` (BUG-01 dedup, only fires when `sub` is actually supplied) | 200 | `issuccessfull=false`; dedup rejection | — |
| U6-E2 | Edge | Request email differs from the LinkedIn-scraped email — scraped value wins silently, override is observability-only | 200 success | `body` reflects **scraped** email, not requested one | LogWarning noting the mismatch (observability only, not a rejection) |
| U6-E3 | Edge | `email`/case variants collide after `NormalizeEmail` (trim+lowercase) against an existing account (BUG-14 fix) | 200 | `issuccessfull=false`; dedup on normalized+effective (scraped-or-request) email | — |
| U6-E4 | Edge | Geocoding of the submitted address fails/times out (`PopulateCoordinates`) | 200 success anyway | `issuccessfull=true`; coordinates simply left null — best-effort, swallowed | no Error (swallowed) |

### U7. GetLinkedInProfile
**GET** `/linkedinprofile?publicIdentifier=` — requires JWT (deliberately **not** `[AllowAnonymous]`, "costs
money" against the scraper API, per code comment). Shares the 7-day `PERSONPROFILE` cache with SignUp.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| U7-H1 | Happy | Valid `publicIdentifier`, cache miss, scrape succeeds | 200 | `issuccessfull=true`; profile body; cached for 7 days under that identifier | none |
| U7-H2 | Happy | Same `publicIdentifier` within 7 days (cache hit) | 200 | identical body, served from Redis — no outbound scraper call this time | none (no explicit cache-hit log) |
| U7-U1 | Unhappy | Empty/whitespace `publicIdentifier` | 200 | `issuccessfull=false`; empty-param guard message | — |
| U7-U2 | Unhappy | Identifier not found on LinkedIn | 200 | `issuccessfull=false`; not-found message that **reflects the raw input back** (`publicIdentifier` echoed verbatim — minor reflected-input note, not XSS since it's JSON not HTML) | — |
| U7-U3 | Unhappy | Unhandled exception (scraper outage, timeout) | 500 | raw exception leaked | 2× Error: `"GetLinkedInProfile failed for public_identifier '{publicIdentifier}'."` + middleware |
| U7-E1 | Edge | No/invalid JWT | 401 | n/a | **G-13** |
| U7-E2 | Edge | Cache is keyed **only** by raw `public_identifier` string — two different-cased inputs (`"JohnDoe"` vs `"johndoe"`) that LinkedIn treats as the same profile produce **two separate cache entries** and two scraper calls instead of one | 200 both times | duplicate successful bodies | no dedup log — minor cache-efficiency gap, **G-17** |

### U8. Ping
**GET** `/ping` — `[AllowAnonymous]`. Byte-identical implementation to `AccountController.Ping` (A5).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| U8-H1 | Happy | Any call, any auth state | 200 | **anonymous object**, not the `ProConnectResponse` envelope — e.g. `{ message: "pong" }`-shaped, not `issuccessfull`/`statuscode` fields | LogInformation: `"Ping request"` |
| U8-E1 | Edge | Called with an `Authorization` header present anyway (client always attaches one) — header is simply ignored since action is anonymous | 200 | same | same |

---

## 3. AccountController — base route `/api/account/v1`

### A1. GetProfile
**GET** `/profile` — requires JWT. Cached (`USERPROFILE`, 10min), `is_active`-filtered `GetById`.
`MapToSignUpResponse` deliberately omits `emailaddress`, `sub`, `lat`/`long`, `industry`, `company`,
`interests` from the response (privacy-by-omission, not a bug).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| A1-H1 | Happy | Valid JWT, active user, cache miss | 200 | `issuccessfull=true`; profile body minus the omitted fields; result cached | none |
| A1-H2 | Happy | Same user within 10min (cache hit) | 200 | identical body, served from Redis | none |
| A1-U1 | Unhappy | User was deactivated after JWT issuance (`is_active=false`) | 200 | `issuccessfull=false`; not-found-style message (same filtered lookup as elsewhere) | — |
| A1-U2 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error: `"GetProfile failed for userid '{userid}'."` + middleware |
| A1-E1 | Edge | No/invalid JWT | 401 | n/a | **G-13** |
| A1-E2 | Edge | JWT missing `"userid"` claim → `Guid.Empty` lookup | 200 | `issuccessfull=false`; not-found | **G-14** |

### A2. UpdateProfile
**PUT** `/profile` — requires JWT. Body: `UpdateProfileRequest`. **No validator registered at all** — the
global FluentValidation filter never fires for this type; only manual null-checks inside the service apply.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| A2-H1 | Happy | Valid partial update (e.g. only `headline` set, everything else null) | 200 | `issuccessfull=true`; merge semantics `user.X = request.X ?? user.X` — null fields on the request **leave existing values untouched**, they are not cleared | USERPROFILE cache invalidated on success |
| A2-H2 | Happy | Update includes a new address → `locationChanged` (plain string inequality) → re-geocode | 200 | `issuccessfull=true`; new `lat/long` populated | — |
| A2-U1 | Unhappy | Request body is null/empty JSON `{}` | 200 | `issuccessfull=false` or no-op success depending on null-guard — since **no validator exists**, an empty `{}` body is accepted as "update nothing", distinct from "malformed JSON" which would 400/500 at the model-binding layer (unverified, **G-6**) | — |
| A2-U2 | Unhappy | User record deleted concurrently between read and write (delete-race) | 200 | `issuccessfull=false`; `message`="Failed to update the profile." | — |
| A2-U3 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error: `"UpdateProfile failed for userid '{userid}'."` + middleware |
| A2-E1 | Edge | Client sends an **explicit empty string** `""` for a field vs. **omitting** the field (`null`) — because there is no validator, both are accepted, but only `null` triggers the fallback-to-existing-value branch; empty string **overwrites** the existing value with `""`. A client intending "no change" must omit the field, not send `""` — easy client-side bug to trigger silently. | 200 success | field now `""` in DB | — |
| A2-E2 | Edge | The service's own existence lookup for the write path is **not** `is_active`-filtered (unlike `GetById`'s read path) — a deactivated account can still have its profile mutated via this endpoint if the caller still holds a valid JWT | 200 success | profile updated despite `is_active=false` | **G-18**, cross-domain inconsistency |
| A2-E3 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### A3. DeactivateAccount
**POST** `/deactivate` — requires JWT. Idempotent, unfiltered-by-`is_active` lookup (can be called again on
an already-deactivated account with no error). No email sent, no cascading effect on Matches/Swipes/Blocks.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| A3-H1 | Happy | Active user calls | 200 | `issuccessfull=true`; `is_active` now false; USERPROFILE cache invalidated | none |
| A3-H2 | Happy (idempotent) | Already-deactivated user calls again | 200 | `issuccessfull=true`, same success shape — **no distinct "already deactivated" message** | none |
| A3-U1 | Unhappy | User record doesn't exist (deleted) | 200 | `issuccessfull=false`; not-found-style message | — |
| A3-U2 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error: `"DeactivateAccount failed for userid '{userid}'."` + middleware |
| A3-E1 | Edge | After deactivation, the user's existing Matches/Likes/conversations are **untouched** — other users can still see stale data (e.g. `GetMessages` on a shared match) referencing a now-inactive account, since Message/Match reads are not universally `is_active`-filtered on the *other* party (see M-domain notes). Cross-domain gap, **G-19**. | n/a | n/a | n/a |
| A3-E2 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### A4. DeleteAccount
**DELETE** `/profile` — requires JWT. **Permanent**, unfiltered existence check, explicit DB transaction with
deliberate delete order `Swipes → Matches → Blocks → MatchCriteria → Users` (FK `OnDelete(Restrict)` forces
this order). No soft-delete, no backup, no grace period, no confirmation email.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| A4-H1 | Happy | Existing user calls | 200 | `issuccessfull=true`; all rows across the 5 tables removed inside one transaction; Messages cascade-delete at the DB level via the Match FK (`OnDelete(Cascade)`) | none |
| A4-U1 | Unhappy | User doesn't exist (already deleted / race) | 200 | `issuccessfull=false`; not-found-style message | — |
| A4-U2 | Unhappy | Unhandled exception mid-transaction (rolled back) | 500 | raw exception leaked; transaction rolled back, no partial delete | 2× Error: `"DeleteAccount failed for userid '{userid}'."` + middleware |
| A4-E1 | Edge | **Irreversibility**: there is no confirmation step, no "are you sure" re-auth, no grace period before this fires — a stolen/replayed JWT can permanently delete the account in one call. Flagged as a product/security consideration, not a code bug per se, **G-20**. | n/a | n/a | n/a |
| A4-E2 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### A5. Ping
**GET** `/ping` — `[AllowAnonymous]`. Byte-identical to `UserController.Ping` (U8). Same test cases apply
verbatim (A5-H1/E1 mirror U8-H1/E1) — included here only for completeness of the controller inventory.

---

## 4. MatchController — base route `/api/match/v1`

### M1. GetCriteria
**GET** `/criteria` — requires JWT. `ResolveEffectiveCriteria` merges stored criteria with plan defaults;
this endpoint **always succeeds** (no rejection branch exists).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M1-H1 | Happy | User has stored criteria | 200 | `issuccessfull=true`; stored values merged with any plan-level overrides | none |
| M1-H2 | Happy | User has never set criteria (first call) | 200 | `issuccessfull=true`; all-default criteria synthesized, nothing persisted yet | none |
| M1-U1 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| M1-E1 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### M2. UpdateCriteria
**PUT** `/criteria` — requires JWT. Body: `MatchCriteriaRequest`, validated by `MatchCriteriaRequestValidator`
(the CRITERIA-04 fix — see file header comment in the validator itself): `min_age>=18` (when supplied),
`max_age<=120` (when supplied), cross-field `min_age<=max_age` (only when **both** supplied),
`radius_km>0` (when supplied). All fields optional/nullable — omitting a field skips its rule entirely.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M2-H1 | Happy | All fields omitted (empty request) | 200 | `issuccessfull=true`; no-op update | cache invalidated for caller only |
| M2-H2 | Happy | `min_age=25, max_age=40, radius_km=10` all valid, user on radius-enabled plan | 200 | `issuccessfull=true`; criteria persisted | USERPROFILE-adjacent `MATCHRECOMMENDATION` cache key invalidated for this user only |
| M2-U1 | Unhappy (validator) | `min_age=17` | 500 | double-encoded body; `errors=[{errormessage:"min_age must be at least 18.", errorcode:"VALIDATION"}]` | Warning (LogValidationFilter) |
| M2-U2 | Unhappy (validator) | `max_age=121` | 500 | `errors=[{errormessage:"max_age must be at most 120."}]` | Warning |
| M2-U3 | Unhappy (validator) | `min_age=40, max_age=25` (both supplied, inverted) | 500 | `errors=[{errormessage:"min_age must be less than or equal to max_age."}]` — this is the exact CRITERIA-04 fix; pre-fix this silently produced a permanently-empty recommendation set instead of an error | Warning |
| M2-U4 | Unhappy (validator) | `radius_km=0` or negative | 500 | `errors=[{errormessage:"radius_km must be greater than 0."}]` | Warning |
| M2-U5 | Unhappy (service, defense-in-depth) | `min_age`/`max_age` individually in-range but service-layer duplicate range re-check fails for some other combination the validator doesn't cover (per Agent 2 notes: service re-validates independently as defense-in-depth) | 200 | `issuccessfull=false` | — |
| M2-U6 | Unhappy (feature gate) | `radius_km` supplied by a **Free**-tier user (no `RadiusSearchEnabled`) | 200 | `issuccessfull=false`; `message` explains radius search requires upgrade — **the only explicit paid-tier gate with an error response in the whole Match domain** (contrast M8's silent-empty-page gate) | — |
| M2-U7 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| M2-E1 | Edge | `min_age=18` exactly (boundary, should pass) | 200 | `issuccessfull=true` | — |
| M2-E2 | Edge | `max_age=120` exactly (boundary, should pass) | 200 | `issuccessfull=true` | — |
| M2-E3 | Edge | `min_age=max_age` (equal, should pass — rule is `<=`) | 200 | `issuccessfull=true` | — |
| M2-E4 | Edge | Only `min_age` supplied, no `max_age` (cross-field rule skipped entirely since `.When()` requires both) | 200 | `issuccessfull=true` | — |
| M2-E5 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### M3. GetRecommendations
**GET** `/recommendations?page=&pagesize=&refresh=` — requires JWT, query bound to `MatchCandidatePageRequest`,
validated by `MatchCandidatePageRequestValidator`: `page>=1`, `pagesize` between 1 and 50.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M3-H1 | Happy | `page=1, pagesize=20`, cache miss, enough candidates in DB | 200 | `issuccessfull=true`; page of candidates; `MATCHRECOMMENDATION` cache populated (`recommendations:{userid}`, 15min) | none |
| M3-H2 | Happy | Same params within 15min, `refresh` omitted/false (cache hit) | 200 | identical page served from Redis | none |
| M3-H3 | Happy | `refresh=true` | 200 | `issuccessfull=true`; forces cache bypass and regenerates the batch | none |
| M3-U1 | Unhappy (validator) | `page=0` | 500 | `errors=[{errormessage matching "page" rule}]` | Warning |
| M3-U2 | Unhappy (validator) | `pagesize=51` | 500 | `errors=[...]` | Warning |
| M3-U3 | Unhappy (validator) | `pagesize=0` | 500 | `errors=[...]` | Warning |
| M3-U4 | Unhappy | Background batch-refill fails (per Agent 2's noted exact LogError text for batch growth failures) | 200 (degrades to whatever's already cached/available, does not fail the whole request) | `issuccessfull=true`; possibly a smaller-than-requested page | LogError (background refill failure, exact text per `IMatchService` impl — service continues serving from what it has) |
| M3-U5 | Unhappy | Unhandled exception in main path | 500 | raw exception leaked | 2× Error + middleware |
| M3-E1 | Edge | User's criteria includes `radius_km` but the user has no geocoded `lat/long` (ungeocoded address) | 200 success | radius filter **silently ignored**, falls back to the 3-way location priority (city/state/country) | no warning that radius was dropped — silent, **G-21** |
| M3-E2 | Edge | SQL hard filters correctly exclude: `is_active=false` candidates, already-swiped candidates (either direction), blocked candidates (either direction, BUG-08 fix), candidates outside age range | 200 success | returned page contains none of the excluded categories | — regression test for BUG-08 |
| M3-E3 | Edge | `GrowBatch` interest-only scoring caps contribution at max 60 points — verify a candidate with many shared interests doesn't exceed the cap and isn't over-ranked relative to a city/state/country match | 200 | scoring order reflects the cap | — |
| M3-E4 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### M4. Like
**POST** `/{targetUserId}/like` — requires JWT. Inline check sequence: `Guid.Empty` → self-swipe →
`is_active`-filtered target lookup (BUG-03) → `IsBlockedEitherDirection` (BUG-08) → `TryConsume` DailyLikes
quota *before* recording the swipe → `RecordSwipe` (advisory-lock ordered-pair upsert, returns
`wasNewLike`/`previousAction`) → refund-if-`!wasNewLike` (BUG-11) → refund-if-non-Like-overwrites-prior-Like
(BUG-04) → reciprocal `HasLiked` check → match creation via canonicalized-pair advisory lock → notify.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M4-H1 | Happy (no match yet) | Valid target, not previously swiped, quota available, target hasn't liked back | 200 | `issuccessfull=true`; `ismatch=false` | none |
| M4-H2 | Happy (mutual match) | Target already liked the caller (reciprocal) | 200 | `issuccessfull=true`; `ismatch=true`, `matchid` populated via `CreateMatch` | notification attempted to both parties (push/live) |
| M4-H3 | Happy (match already existed, race-safe) | Match row already exists from a concurrent duplicate call (BUG-12) — `CreateMatch` returns `created=false` but **both callers still receive `ismatch=true` with the correct existing `matchid`** | 200 | `issuccessfull=true`; `ismatch=true`, correct `matchid` | notify only fires once (gated on `created`, BUG-12 fix) — regression test |
| M4-U1 | Unhappy | `targetUserId = Guid.Empty` | 200 | `issuccessfull=false`; validation-style rejection message | — |
| M4-U2 | Unhappy | `targetUserId` == caller's own id (self-swipe) | 200 | `issuccessfull=false`; self-swipe rejection message | — |
| M4-U3 | Unhappy | `targetUserId` doesn't exist or `is_active=false` (BUG-03 fix) | 200 | `issuccessfull=false`; not-found-style message — **regression test**: pre-fix this likely threw an FK-violation 500 or silently created orphan data | — |
| M4-U4 | Unhappy | Caller has blocked target or target has blocked caller, either direction (BUG-08 fix) | 200 | `issuccessfull=false`; blocked-rejection message | — |
| M4-U5 | Unhappy | Daily like quota exhausted (`TryConsume` fails) | 200 | `issuccessfull=false`; quota-exceeded message, upgrade hint | — |
| M4-U6 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| M4-E1 | Edge | Caller already `Like`d this target previously (duplicate Like, `wasNewLike=false`) | 200 | `issuccessfull=true`; **consumed quota unit is refunded** (BUG-11 fix) so repeated duplicate Likes don't drain the daily quota; response still has correct `ismatch` reflecting current state | — regression test for BUG-11 |
| M4-E2 | Edge | Caller previously `Pass`ed this same target, now calls `Like` (overwrite) | 200 | `issuccessfull=true`; swipe row updated from Pass→Like; this does **not** double-charge since only *new* Likes consume quota properly post BUG-04/11 fixes | — regression test for BUG-04 |
| M4-E3 | Edge | Two concurrent `Like` calls for the same ordered pair (double-submit) — advisory lock on the ordered-pair hash serializes them | 200 both | one sees `wasNewLike=true`, the other sees `wasNewLike=false` and gets refunded | — |
| M4-E4 | Edge | Two concurrent mutual `Like`s creating the same match from both sides simultaneously — canonicalized-pair advisory lock in `CreateMatch` serializes creation (BUG-12) | 200 both | both get `ismatch=true` with the **same** `matchid`; only one triggers the notification | — regression test for BUG-12 |
| M4-E5 | Edge | Push notification delivery fails (FCM dark-config or genuine failure) after a successful match | 200 | `issuccessfull=true`; match still created, response unaffected | LogWarning (swallowed, Warning not Error) |
| M4-E6 | Edge | Malformed route `targetUserId` (not a GUID) | unverified — pure model-binding failure, no validator exists for route params | — | — → **G-6** |
| M4-E7 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### M5. Pass
**POST** `/{targetUserId}/pass` — same inline check sequence as Like minus quota/match-creation; Pass always
terminates with `ismatch=false` regardless of any prior reciprocal Like from the target.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M5-H1 | Happy | Valid, not-previously-swiped target | 200 | `issuccessfull=true`; `ismatch=false` always | none |
| M5-U1..U4 | Unhappy | Same `Guid.Empty`/self/not-found-or-inactive(BUG-03)/blocked(BUG-08) checks as M4-U1..U4 | 200 | same rejection shapes | — |
| M5-U5 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| M5-E1 | Edge | Target had already `Like`d the caller — Pass still reports `ismatch=false` and does **not** create a match, even though a Like+Like would have (Pass unilaterally forecloses the match) | 200 | `ismatch=false` | — |
| M5-E2 | Edge | Caller had previously `Like`d this target, now `Pass`es (overwrite, no quota refund logic needed here since Pass never consumed quota) | 200 | swipe row updated Like→Pass; no quota side effect either way (Pass doesn't consume, and overwriting a Like with Pass does trigger the BUG-04 refund since the prior action was Like) | — regression test for BUG-04 from the Pass direction |
| M5-E3 | Edge | Malformed route `targetUserId` | unverified | — | — → **G-6** |
| M5-E4 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### M6. GetMatches
**GET** `/matches?page=&pagesize=` — requires JWT, `MatchCandidatePageRequest` validator (same rules as M3).
Deliberately **not** `is_active`-filtered on the other party, and this list is **never cached**.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M6-H1 | Happy | User has 1+ matches | 200 | `issuccessfull=true`; page of matches, always freshly queried | none |
| M6-H2 | Happy | User has zero matches | 200 | `issuccessfull=true`; empty page, `has_more=false` | none |
| M6-U1 | Unhappy (validator) | `page=0` / `pagesize` out of 1-50 | 500 | `errors=[...]` | Warning |
| M6-U2 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| M6-E1 | Edge | A matched counterpart has since **deactivated** their account (`is_active=false`) — the match still appears in this list since the other-party lookup is deliberately not filtered, showing a stale/ghost profile | 200 | match still listed, counterpart fields reflect last-known state | — documented design choice, not a bug, but worth a dedicated regression test to ensure it stays intentional |
| M6-E2 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### M7. Unmatch
**DELETE** `/matches/{matchId}` — requires JWT. `ExecuteDeleteAsync` with the membership check **as** the
authorization mechanism (no separate 403 — a non-member simply gets "not found"). Unilateral, no consent
from the other party, does **not** reopen the candidate pool for either side, cascade-deletes Messages at
the DB level.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M7-H1 | Happy | Caller is a member of `matchId` | 200 | `issuccessfull=true`; match + its messages gone | LogInformation: `"Userid '{userid}' unmatched matchid '{matchId}'."` |
| M7-U1 | Unhappy | `matchId` doesn't exist | 200 | `issuccessfull=false`; `message`="Match not found." | — |
| M7-U2 | Unhappy | `matchId` exists but caller is **not** a member (authorization-via-membership-check) | 200 | `issuccessfull=false`; **same generic** "Match not found." — does not leak that the match exists for someone else, but also doesn't distinguish a genuine 404 from an authorization failure (ambiguous for client-side error handling) | — |
| M7-U3 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| M7-E1 | Edge | After unmatching, the counterpart is **not** returned to the other's candidate pool — they will never see each other again in recommendations even though they're no longer "matched". Documented design choice, not obviously a bug, but worth confirming with product — **G-22**. | n/a | n/a | n/a |
| M7-E2 | Edge | Malformed route `matchId` (not a GUID) | unverified | — | — → **G-6** |
| M7-E3 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### M8. GetIncomingLikes
**GET** `/liked-me?page=&pagesize=` — requires JWT, same page validator as M3/M6. Paid-tier gated, but the
gate is **silent**: a Free-tier user gets `issuccessfull=true` with an **empty page**, not an error (contrast
M2-U6's explicit error for radius). Full weighted `ScoreCandidate` scoring applies (city 40 / state 20 /
country 10 / industry 25 / interests 60).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M8-H1 | Happy | Paid-tier user, 1+ people have liked them | 200 | `issuccessfull=true`; scored/ranked page of likers | none |
| M8-H2 | Happy (silent gate) | Free-tier user, regardless of actual incoming likes | 200 | `issuccessfull=true`; **empty page, `has_more=false`** — looks indistinguishable from "genuinely nobody liked you" to the client | none — no message explaining the gate, **G-23** (UX/analytics ambiguity, flagged alongside M2's inconsistent paid-gate pattern) |
| M8-U1 | Unhappy (validator) | `page=0` / bad `pagesize` | 500 | `errors=[...]` | Warning |
| M8-U2 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| M8-E1 | Edge | Paid user with incoming likes from users later blocked (either direction) — must confirm these are excluded from the list consistent with BUG-08's general blocking semantics | 200 | blocked parties excluded | — |
| M8-E2 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### M9. GetSeenProfiles
**GET** `/seen?page=&pagesize=` — requires JWT, same page validator. **No feature gate at all** (unlike M8).
Shows only profiles the caller explicitly `Pass`ed — **not** unreciprocated Likes. Same full weighted scoring
as M8.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M9-H1 | Happy | Caller has Passed 1+ profiles, any plan tier | 200 | `issuccessfull=true`; scored/ranked page of passed profiles, available to Free tier too | none |
| M9-H2 | Happy | Caller has never Passed anyone | 200 | `issuccessfull=true`; empty page | none |
| M9-U1 | Unhappy (validator) | `page=0` / bad `pagesize` | 500 | `errors=[...]` | Warning |
| M9-U2 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| M9-E1 | Edge | Caller `Like`d (not Passed) a profile, then the target never reciprocated — confirm that profile does **not** appear here (this endpoint is Pass-only, Likes with no match live nowhere visible to the caller) | 200 | that profile absent from results | — |
| M9-E2 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### M10. BlockUser
**POST** `/{targetUserId}/block` — requires JWT. `Guid.Empty`/self checks exist, but **unlike Like/Pass,
there is no existence check on `targetUserId` at all**, and **no advisory lock** (unlike `RecordSwipe`/
`CreateMatch`). Directional-only `alreadyBlocked` check. Dissolves any existing match unconditionally.
Invalidates **both** parties' recommendation cache (unlike M2's self-only invalidation).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| M10-H1 | Happy | Valid existing target, not previously blocked | 200 | `issuccessfull=true`; block row created | LogInformation: `"Userid '{userid}' blocked userid '{targetUserId}'."` |
| M10-H2 | Happy | Blocking a current match dissolves it | 200 | `issuccessfull=true`; the match row is removed as a side effect | same LogInformation; no separate unmatch log emitted for this implicit dissolution |
| M10-U1 | Unhappy | `targetUserId = Guid.Empty` | 200 | `issuccessfull=false` | — |
| M10-U2 | Unhappy | `targetUserId` == caller (self-block) | 200 | `issuccessfull=false` | — |
| M10-U3 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| M10-E1 | **Gap** | `targetUserId` is a syntactically valid GUID that **does not exist** in the Users table at all — since there is **no existence check** (unlike M4/M5's BUG-03 fix), the insert likely hits a Blocks-table FK constraint and throws → falls through to the generic unhandled-exception path | **likely 500** (unverified empirically, flagged as the single highest-priority new gap found by this research — see **G-2**) | raw FK-violation exception text leaked | 2× Error + middleware |
| M10-E2 | **Gap** | Two concurrent `BlockUser` calls for the same (caller, target) pair — no advisory lock means a TOCTOU race on the `alreadyBlocked` check; both could pass the check and attempt to insert, risking either a duplicate-row 500 (if a unique constraint exists) or two silently-duplicated block rows (if it doesn't) | unverified, flagged | — | — → **G-3** |
| M10-E3 | Edge | Caller blocks target, target already has an **independent** block against caller (mutual block scenario) — directional-only check means this produces **two separate rows**, not a single mutual-block record; confirm both directions are still respected by `IsBlockedEitherDirection` reads elsewhere (M3/M4/M5/M8) | 200 | two distinct rows exist; reads still correctly treat them as mutually blocked | — |
| M10-E4 | Edge | Malformed route `targetUserId` | unverified | — | — → **G-6** |
| M10-E5 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

---

## 5. MessageController — base route `/api/message/v1`

### MSG1. GetMessages
**GET** `/{matchId}?page=&pagesize=` — requires JWT, `MessagePageRequest` validated by
`MessagePageRequestValidator`. `IsParticipant` membership check. **Side effect on a GET**: bulk
`ExecuteUpdateAsync` flips only the *other* participant's unread messages to read, then
`TryNotifyMessagesRead` pushes a SignalR event. Response reflects the **post**-mark-read state. Most-recent-
first pagination.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| MSG1-H1 | Happy | Caller is a participant, match has messages, some unread from the other party | 200 | `issuccessfull=true`; messages page (most-recent-first); unread messages from the *other* participant are now marked read as a side effect of this GET | SignalR push to `match-{matchId}` group via `TryNotifyMessagesRead` |
| MSG1-H2 | Happy | Caller is a participant, no messages yet | 200 | `issuccessfull=true`; empty page | none |
| MSG1-U1 | Unhappy (validator) | `page=0` or `pagesize` out of range | 500 | `errors=[...]` | Warning |
| MSG1-U2 | Unhappy | `matchId` doesn't exist, or caller is not a participant (membership-as-authorization, same pattern as M7) | 200 | `issuccessfull=false`; `message`="Match not found." | — |
| MSG1-U3 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error: `"GetMessages failed for userid '{userid}' -> matchId '{matchId}'."` + middleware |
| MSG1-E1 | Edge | Caller's **own** previously-sent-but-unread-by-the-other-party messages are **not** affected by this call — only the other participant's messages addressed to the caller get marked read | 200 | caller's sent messages' read-state unchanged | — |
| MSG1-E2 | Edge | Two rapid consecutive `GetMessages` calls by the same user — second call has nothing left to mark (already read), still succeeds, SignalR push still attempted (idempotent push, timestamp-skew caveat noted by Agent 3: the pushed timestamp may not exactly match the DB write across two near-simultaneous calls) | 200 both | — | push fires both times (harmless duplicate) |
| MSG1-E3 | Edge | Malformed route `matchId` (not a GUID) | unverified | — | — → **G-6** |
| MSG1-E4 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### MSG2. SendMessage
**POST** `/{matchId}` — requires JWT. Body: `MessageRequest`, validated by `MessageRequestValidator`
(`NotEmpty` + `MaxLength(2000)` on message text). Redundant dead-code check duplicated at the service layer.
`IsParticipant` check. `TryNotifyNewMessage` pushes to **both** participants (sender included, not excluded).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| MSG2-H1 | Happy | Caller is a participant, message text 1-2000 chars | 200 | `issuccessfull=true`; message persisted, returned with its new id/timestamp | SignalR push to both participants via `TryNotifyNewMessage` |
| MSG2-U1 | Unhappy (validator) | Empty/whitespace message text | 500 | `errors=[{errormessage matching NotEmpty rule}]` | Warning |
| MSG2-U2 | Unhappy (validator) | Message text > 2000 chars | 500 | `errors=[{errormessage matching MaxLength rule}]` | Warning |
| MSG2-U3 | Unhappy | `matchId` doesn't exist or caller not a participant | 200 | `issuccessfull=false`; `message`="Match not found." | — |
| MSG2-U4 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error: `"SendMessage failed for userid '{userid}' -> matchId '{matchId}'."` + middleware |
| MSG2-E1 | Edge | Message text exactly 2000 chars (boundary, should pass) | 200 | `issuccessfull=true` | — |
| MSG2-E2 | Edge | Message text exactly 2001 chars (boundary, should fail) | 500 | `errors=[...]` | Warning |
| MSG2-E3 | Edge | Sending a message into a match that was concurrently `Unmatch`ed (M7) between the participant-check and the insert — FK `OnDelete(Cascade)` on `messages.matchid` means the match row is gone; insert would either fail an FK constraint (→likely unhandled exception→500) or the participant-check itself already caught the deletion and returned "Match not found." depending on exact timing — flagged as a race worth a dedicated concurrency test | unverified exact outcome | — | — → **G-24** |
| MSG2-E4 | Edge | Push notification to one/both participants fails (device unreachable, FCM dark-config) | 200 success anyway | message still persisted and returned | LogWarning (swallowed) |
| MSG2-E5 | Edge | Malformed route `matchId` | unverified | — | — → **G-6** |
| MSG2-E6 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

---

## 6. NotificationController — base route `/api/notification/v1`

All actions require JWT (no `[AllowAnonymous]` anywhere in this controller — nothing external calls it
directly; `MatchService`/`SubscriptionService` call `INotificationService.Notify` in-process instead).

### N1. GetNotifications
**GET** `` (controller root) `?page=&pagesize=` — bound to `NotificationPageRequest`. **No validator
registered at all** — not even a manual guard.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| N1-H1 | Happy | `page=1, pagesize=20`, user has notifications | 200 | `issuccessfull=true`; page ordered `created_at_utc DESC, notificationid ASC` (asymmetric ordering vs. Messages' pure-timestamp order — worth a dedicated sort-stability test when two notifications share a timestamp) | none |
| N1-H2 | Happy | User has zero notifications | 200 | `issuccessfull=true`; empty page | none |
| N1-U1 | **Gap** | `page=0` (no validator exists to reject this) | **500**, via an **uncaught** `ArgumentOutOfRangeException` from EF's `Skip`/`Take` on a negative computed offset — this is a **distinct, unfixed bug** from the generic exception-leak path since it's a predictable, client-triggerable 500 | raw `ArgumentOutOfRangeException` message leaked verbatim | 2× Error (service generic catch, if any, + middleware) — flagged as **G-4**, a direct regression-shaped gap akin to the already-fixed CRITERIA-04 |
| N1-U2 | Edge/Gap | `page` negative (e.g. `-1`) | same as N1-U1 | same | same, **G-4** |
| N1-U3 | **Gap** | `pagesize` with **no upper bound at all** (e.g. `pagesize=100000`) | 200, but a single request can force the DB to materialize an arbitrarily large result set — potential DoS/perf gap, no validator caps it | large body | none — flagged **G-5** |
| N1-U4 | Unhappy | Unhandled exception elsewhere in the query | 500 | raw exception leaked | 2× Error + middleware |
| N1-E1 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### N2. GetUnreadCount
**GET** `/unread-count` — always succeeds.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| N2-H1 | Happy | User has unread notifications | 200 | `issuccessfull=true`; `body`=integer count > 0 | none |
| N2-H2 | Happy | New user, zero notifications ever | 200 | `issuccessfull=true`; `body`=0 | none |
| N2-U1 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| N2-E1 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### N3. MarkAsRead
**POST** `/{notificationid}/read` — single predicate conflates **three distinct failure reasons** into one
identical response: notification doesn't exist, notification belongs to a different user, or
`notificationid` is otherwise invalid.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| N3-H1 | Happy | Valid, unread, owned notification | 200 | `issuccessfull=true` | `TryNotifyUnreadCountChanged` SignalR push on success |
| N3-H2 | Unhappy (conflated, not idempotent) | Already-read notification, re-marked | 200 | **Correction (previously documented inaccurately as a soft-idempotent success):** confirmed live against the current implementation that this is **not** a success/soft-idempotent response at all. `NotificationRepository.MarkAsRead`'s lookup predicate includes `!n.is_read`, so an already-read notification simply doesn't match the query and `MarkAsRead` returns `false` — the service then returns the exact same `issuccessfull=false`, `message`="Notification not found." shape as N3-U1's genuinely-nonexistent-id case. A caller re-marking an already-read notification cannot distinguish that case from the notification never having existed at all. | — (no push, since the early-return happens before `TryNotifyUnreadCountChanged`) |
| N3-U1 | Unhappy (conflated) | `notificationid` doesn't exist | 200 | `issuccessfull=false`; `message`="Notification not found." | — |
| N3-U2 | Unhappy (conflated) | `notificationid` exists but belongs to a **different** user | 200 | `issuccessfull=false`; **identical** "Notification not found." — does not leak existence to a non-owner, but also gives the caller no way to distinguish "doesn't exist" from "not yours" (same ambiguity pattern as M7-U2) | — |
| N3-U3 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| N3-E1 | Edge | Malformed route `notificationid` (not a GUID) | unverified | — | — → **G-6** |
| N3-E2 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### N4. MarkAllAsRead
**POST** `/read-all` — loads **all unread notifications into memory** (not a bulk `ExecuteUpdateAsync` like
Messages' mark-read path), updates each, saves. **Always succeeds**, even when count is already 0.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| N4-H1 | Happy | User has 5 unread notifications | 200 | `issuccessfull=true`; `message`="5 notification(s) marked as read." | `TryNotifyUnreadCountChanged` push only since count>0 |
| N4-H2 | Happy (zero-count, still "success") | User has zero unread notifications | 200 | `issuccessfull=true`; `message`="0 notification(s) marked as read." — **not an error**, a deliberate always-succeeds design | no push (gated on count>0) |
| N4-U1 | Unhappy | Unhandled exception (e.g. mid-batch save failure) | 500 | raw exception leaked | 2× Error + middleware |
| N4-E1 | Edge | A user with a very large unread backlog (thousands) — in-memory load-all-then-save pattern (unlike Messages' bulk update) is a performance concern at scale, worth a load test, **G-25** | 200 eventually | success | — |
| N4-E2 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### N5. RegisterDevice
**POST** `/devices` — body: `RegisterDeviceTokenRequest`, **no validator registered**; only a manual
null/whitespace check. **Upsert keyed by `device_token` ALONE, not `(userid, device_token)`.**

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| N5-H1 | Happy | New `device_token` for this user, valid platform | 200 | `issuccessfull=true`; device row created | none |
| N5-H2 | Happy | Same user re-registers the same token (e.g. app relaunch) | 200 | `issuccessfull=true`; row updated in place | none |
| N5-U1 | Unhappy | `device_token` null/whitespace (manual check, since no validator exists) | 200 | `issuccessfull=false`; manual-guard rejection message | — |
| N5-U2 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| N5-E1 | **Gap** | A **different** user registers the exact same `device_token` already owned by user A (e.g. shared/reset device, or a malicious client replaying a captured token) — since the upsert key is the token alone, this **silently re-points ownership to user B with no audit trail and no rejection**; user A will stop receiving push notifications on that device with zero warning to either party | 200 success for user B | `issuccessfull=true`, ownership silently transferred | none — **G-9**, a real, currently-undocumented correctness/security gap (push-hijack-adjacent) |
| N5-E2 | Edge | Invalid/unrecognized `platform` enum value in the request (no validator to reject it, and raw enum deserialization behavior is unverified — could be a model-binding 500/undefined, or could bind to a default enum value silently) | unverified | — | — → **G-6**, specifically for enum binding this time |
| N5-E3 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### N6. UnregisterDevice
**DELETE** `/devices/{device_token}` — lookup scoped by **both** `userid` + `device_token` (unlike N5's
token-only upsert — an intentional asymmetry worth flagging). Generic "Device not found." conflates two
distinct reasons (token doesn't exist at all vs. exists but belongs to someone else).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| N6-H1 | Happy | Caller owns this exact `device_token` | 200 | `issuccessfull=true`; row removed | none |
| N6-U1 | Unhappy (conflated) | `device_token` doesn't exist at all | 200 | `issuccessfull=false`; `message`="Device not found." | — |
| N6-U2 | Unhappy (conflated) | `device_token` exists but is owned by a **different** user | 200 | `issuccessfull=false`; **identical** "Device not found." | — |
| N6-U3 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| N6-E1 | Edge | `device_token` route segment contains characters requiring URL-encoding (e.g. `+`, `/`, `=` common in base64-shaped push tokens) — free-form string route segment, encoding/decoding mismatch could cause a token that exists to be reported "not found" purely due to a casing/encoding mismatch between what was registered and what's sent in the URL | unverified, flagged | — | — → **G-26** |
| N6-E2 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

---

## 7. SubscriptionController — base route `/api/subscription/v1`

All actions require JWT **except** `Webhook`, which is `[AllowAnonymous]` — the only anonymous endpoint in
this controller, and the only one in the whole API authenticated purely by a Stripe signature header instead
of a JWT.

### S1. GetPlans
**GET** `/plans` — requires JWT. **Gap**: the code's own doc-comment implies this should be public, but it
is **not** `[AllowAnonymous]` — a prospective (not-yet-signed-up) user cannot see pricing without a token.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| S1-H1 | Happy | Valid JWT | 200 | `issuccessfull=true`; full seed-data feature matrix per plan tier (Free/paid tiers with their `RadiusSearchEnabled`/`DailyLikes`/etc. flags) | none |
| S1-U1 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error: `"GetPlans failed."` + middleware |
| S1-E1 | **Gap** | No JWT at all (simulating a prospective signup-page visitor wanting to see pricing before registering) | **401** — cannot view plans without already being authenticated, contradicting the doc-comment's implied intent | n/a | **G-10**, product/UX gap |

### S2. GetMySubscription
**GET** `/me` — requires JWT. No DB row for the user → **synthesizes** a FREE/Active-hardcoded response
rather than erroring.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| S2-H1 | Happy | User has never subscribed (no row) | 200 | `issuccessfull=true`; synthesized `{ plan: "FREE", status: "Active", ... }` — not a real persisted row | none |
| S2-H2 | Happy | User has an active/past-due/canceled subscription row | 200 | `issuccessfull=true`; reflects stored values verbatim | none |
| S2-U1 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error: `"GetMySubscription failed for userid '{userid}'."` + middleware |
| S2-E1 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### S3. Checkout
**POST** `/checkout` — body: `CheckoutRequest`, **no validator registered**. Rejects FREE-plan selection and
unknown/inactive plan codes before calling Stripe.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| S3-H1 | Happy | Valid paid `plan_code`, Stripe session created successfully | 200 | `issuccessfull=true`; `body`=Stripe Checkout Session URL/id | none |
| S3-U1 | Unhappy | `plan_code` = FREE / the free-tier code | 200 | `issuccessfull=false`; "cannot checkout into the free plan"-style rejection | — |
| S3-U2 | Unhappy | `plan_code` unknown or `is_active=false` in the Plans table | 200 | `issuccessfull=false`; unknown/inactive-plan rejection | — |
| S3-U3 | Unhappy (BUG-05, fixed) | Stripe API call throws a genuine `StripeException` (card/account/API error) | 200 | `issuccessfull=false`; clean `message`="Payment provider error, please try again later." — regression test for BUG-05 | — (caught cleanly, no Error log for this specific branch per Agent 4's notes) |
| S3-U4 | **Gap, unfixed** | The resolved plan's Stripe price id is **null/empty** (misconfigured plan row) — this throws a **plain `InvalidOperationException`**, which is **not** a `StripeException`, so it **bypasses BUG-05's catch** and falls through to the generic handler | **500**, with the **raw Stripe-configuration detail leaked verbatim** in `errormessage` (e.g. internal price-id/config strings) | 2× Error + middleware — flagged **G-11**, a distinct unfixed sibling of BUG-05 that needs the same treatment (catch `InvalidOperationException` too, or validate price-id presence before calling Stripe) |
| S3-U5 | Unhappy | Request body null/malformed | 200 or 500 depending on binding — no validator exists, only manual null-guard if present | `issuccessfull=false` if guarded | — |
| S3-E1 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### S4. PurchaseConsumable
**POST** `/consumable/purchase` — body: `ConsumablePurchaseRequest`, **no validator registered**.
`quantity<1` manually rejected; **no upper bound at all** on quantity. Same unfixed `InvalidOperationException`
leak path as S3. `CustomerCreation="always"` only applied when the user has no existing Stripe customer id.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| S4-H1 | Happy | Valid consumable key, `quantity=1`, Stripe session created | 200 | `issuccessfull=true`; checkout session body | none |
| S4-U1 | Unhappy | `quantity < 1` (e.g. 0 or negative) | 200 | `issuccessfull=false`; quantity-rejection message | — |
| S4-U2 | Unhappy | Unknown/inactive consumable key | 200 | `issuccessfull=false`; unknown-key rejection | — |
| S4-U3 | Unhappy (BUG-05) | Genuine `StripeException` | 200 | `issuccessfull=false`; clean message | — |
| S4-U4 | **Gap, unfixed** | Misconfigured consumable price id → `InvalidOperationException` | **500**, raw Stripe-config leak | 2× Error + middleware — same **G-11** |
| S4-E1 | **Gap** | `quantity` has **no upper bound** — a caller can request e.g. `quantity=999999999`, which Stripe itself may reject, but locally nothing stops the attempt or flags it as abuse before the Stripe call | unverified downstream behavior, flagged | — | **G-12** |
| S4-E2 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### S5. CancelSubscription
**POST** `/cancel?at_period_end=true` (default `true`). No-subscription-or-empty-Stripe-id rejected first.
**BUG-07 (fixed)**: idempotent short-circuit only for local `status==Canceled` — `PastDue`/`Expired` still
call Stripe live. `at_period_end=true` only flags cancel-at-period-end without changing status immediately;
`at_period_end=false` sets `status=Canceled` right away. **Both branches unconditionally invalidate the
`EFFECTIVEPLAN` cache even when entitlement didn't actually change** (e.g. flagging at-period-end doesn't
change current entitlement, yet the cache is still busted).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| S5-H1 | Happy | Active subscription, `at_period_end=true` (default) | 200 | `issuccessfull=true`; `cancel_at_period_end=true`, `status` unchanged (still Active) until period end | EFFECTIVEPLAN cache invalidated (even though entitlement is unchanged right now) |
| S5-H2 | Happy | Active subscription, `at_period_end=false` | 200 | `issuccessfull=true`; `status=Canceled` immediately | EFFECTIVEPLAN cache invalidated |
| S5-U1 | Unhappy | No subscription row, or row has empty Stripe subscription id | 200 | `issuccessfull=false`; "nothing to cancel"-style rejection | — |
| S5-U2 | Unhappy (BUG-05) | Genuine `StripeException` during the live cancel call | 200 | `issuccessfull=false`; clean message | — |
| S5-U3 | Unhappy | Unhandled exception (non-Stripe) | 500 | raw exception leaked | 2× Error + middleware |
| S5-E1 | Edge (BUG-07 regression) | Local `status` already `Canceled` | 200 | `issuccessfull=true`; **short-circuits, no live Stripe call made** — regression test for BUG-07 | — |
| S5-E2 | Edge (BUG-07 boundary) | Local `status` is `PastDue` or `Expired` (NOT `Canceled`) | 200 | still calls Stripe live (not short-circuited) — confirms the fix's exact scope, a status just short of `Canceled` is **not** treated as idempotent | — |
| S5-E3 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### S6. GetTransactions
**GET** `/transactions?page=&pagesize=` — bound to `TransactionPageRequest`, **no validator registered**.
Not cached by design.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| S6-H1 | Happy | `page=1, pagesize=20`, user has transactions | 200 | `issuccessfull=true`; page of transactions | none |
| S6-H2 | Happy | User has zero transactions | 200 | `issuccessfull=true`; empty page, `has_more=false` | none |
| S6-U1 | **Gap** | `page=0` or negative (no validator) | **500**, via an `ArgumentException` that is caught-and-**rethrown** (confirmed distinct from Notifications' uncaught `ArgumentOutOfRangeException` — same symptom, different exception type/path) | raw exception text leaked | 2× Error + middleware — flagged **G-4b** (sibling of N1-U1) |
| S6-U2 | **Gap, real pagination bug** | `pagesize=0` | **200**, `issuccessfull=true`, **empty result set** (`Take(0)`), but **`has_more` is computed as `(long)page * 0 < totalCount`, which is always `true` whenever the user has any transactions at all** — so the response claims "there are more pages" forever while returning zero items per page, an infinite-scroll/pagination deadlock for any client that trusts `has_more` | 200 | `issuccessfull=true`; `body=[]`, `has_more=true` (contradictory) | none — flagged **G-27**, a concrete, reproducible bug distinct from the generic page/pagesize-no-validator gap |
| S6-U3 | Unhappy | Unhandled exception elsewhere | 500 | raw exception leaked | 2× Error + middleware |
| S6-E1 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

### S7. Webhook
**POST** `/webhook` — `[AllowAnonymous]`. Raw body + raw `Stripe-Signature` header read manually (not model-
bound). `ParseWebhookEvent` throws `StripeException` for missing/malformed/mismatched-signature or an
unconfigured webhook secret. **BUG-06 (fixed)**: that specific `StripeException` is caught **before** the
main try block, logged at **Warning** (not Error), and returns the **only non-200/500 response in the whole
API**: explicit `statuscode=400`, `message`="Invalid webhook signature." Any *other* exception type thrown
from signature parsing would escape uncaught to the generic middleware → 500 (verify this boundary
explicitly). Idempotency guard `TransactionExistsByStripeEventId` is a **plain, unlocked `SELECT`** — a real,
currently undocumented, **unfixed** race: two concurrent deliveries of the same event could both pass the
existence check before either has written its row, and the second `CreateTransaction` call would then throw
an uncaught **unique-violation** exception → 500 (the exact shape of BUG-10/BUG-15, but never actually fixed
for this specific table/path).

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| S7-H1 | Happy | Valid signature, `checkout.session.completed` for a subscription plan purchase | 200 | `issuccessfull=true`; `CreateTransaction` + `Notify(SubscriptionActivated)`; `periodEnd` computed **provisionally** (never corrected by a later webhook — flagged **G-28**, a real factual gap, not just theoretical); `cancel_at_period_end` reset to `false`; EFFECTIVEPLAN cache invalidated immediately | none beyond normal success |
| S7-H2 | Happy | Valid signature, `checkout.session.completed` for a consumable purchase | 200 | `issuccessfull=true`; `totalUnits = product.quantity_granted * bundles`; balance adjusted via plain EF read-then-write (same theoretical race shape as the original BUG-10, **never fixed for this table** — flagged **G-29**); **no notification sent** for consumable purchases (asymmetric vs. the subscription path's `SubscriptionActivated` notify) | none |
| S7-H3 | Happy | Valid signature, `customer.subscription.updated`/`.deleted` (`HandleSubscriptionStatusChanged`) | 200 | resolves by `stripe_subscription_id` (not userid); **BUG-13 fix** always writes a `PaymentTransaction`, making these idempotency-ledger-protected; notification only fires when the resulting status is `Canceled` | none beyond normal |
| S7-H4 | Happy | Valid signature, `invoice.payment_succeeded`/`.failed` (`HandleInvoiceEvent`) | 200 | resolves via `invoice.Parent?.SubscriptionDetails?.SubscriptionId`; `Failed` sets `PastDue`; always writes a transaction; documented-acceptable duplicate notification for first-time signups (`SubscriptionActivated` + `SubscriptionRenewed` both fire) | none beyond normal |
| S7-H5 | Happy | Valid signature, unhandled/unrecognized event type | 200 | `issuccessfull=true`; no-op, nothing persisted | **2× Information** (not Error/Warning) — one at `SubscriptionService`, one at `StripePaymentGateway.ParseWebhookEvent` — both logging the unrecognized type |
| S7-U1 | Unhappy (BUG-06, fixed) | Missing/malformed/mismatched `Stripe-Signature` header, or unconfigured webhook secret | **400** — the one deliberate non-200/500 in the app | `issuccessfull=false`; `message`="Invalid webhook signature."; `statuscode=400` explicitly set | LogWarning (not Error): exact BUG-06 fix text, e.g. `"SubscriptionService.HandleWebhook rejected a webhook with a bad/missing Stripe-Signature."` |
| S7-U2 | Unhappy | Any other exception during event processing (not the signature check) | 500 | raw exception leaked | 2× Error: `"HandleWebhook failed."` + middleware |
| S7-E1 | **Gap, unfixed race** | Stripe (or a malicious replayer) delivers the **same event id twice concurrently** | First: 200 success. **Second: likely an uncaught unique-violation exception → 500**, since `TransactionExistsByStripeEventId` is an unlocked plain SELECT with a TOCTOU window | second call: raw unique-violation text leaked | 2× Error on the second call — flagged **G-30**, the single most concrete unfixed-bug-shaped gap in the Subscription domain, directly analogous to the already-fixed BUG-10/BUG-15 |
| S7-E2 | Edge | `.deleted` event whose Stripe-reported status is, unusually, `"active"` (out-of-order/odd delivery) — `HandleSubscriptionStatusChanged`'s mapping would map this to local `Active` instead of `Canceled`, since the mapping trusts the embedded status string over the event type's implication | 200, but **incorrect final status** (`Active` when it should be `Canceled`) | — | flagged **G-31**, a specific status-mapping correctness gap worth a dedicated test with a hand-crafted event payload |
| S7-E3 | Edge | `invoice.payment_succeeded` arrives for a subscription that is currently `PastDue` (recovered payment) | 200, but **status is NOT reset back to `Active`** by this handler — only a separate `customer.subscription.updated` event does that; if Stripe doesn't also send that second event promptly, the user remains incorrectly `PastDue` after paying | 200 | — | flagged **G-32**, "PastDue → invoice.payment_succeeded alone should leave status at PastDue" — a sequencing gap worth testing explicitly with webhook-order permutations |
| S7-E4 | Edge | Unresolvable userid or unknown `plan_code`/consumable key inside `checkout.session.completed` (metadata corrupted or plan deleted after checkout session was created) | 200, no-op | — | LogWarning (per-branch, exact text in `SubscriptionService`) |
| S7-E5 | Edge | Neither `consumable_key` nor `plan_code` present in `checkout.session.completed` metadata | 200, **silent no-op** — nothing persisted, nothing logged beyond the generic flow | — | none distinct — flagged **G-33**, a silent-swallow gap (should probably at least Warning-log an unrecognized checkout shape) |

---

## 8. SearchController — base route `/api/search/v1`

### SE1. Search
**GET** `` (controller root) `?query=&city=&state=&country=&industry=&company=&radius_km=&page=&pagesize=` —
requires JWT. Bound to `SearchRequest`, validated by `SearchRequestValidator`: rules on `page`/`pagesize`,
and `radius_km` requires **at least one** of `city`/`state`/`country` to also be present. **Correction
(previously documented inaccurately):** `SearchRequest` has no `lat`/`long` fields — the caller never
supplies coordinates directly. When `radius_km` is set, the server geocodes whichever of
`city`/`state`/`country` were supplied via `IGeocodingProvider` into a center point, then filters by
`ST_Distance` against each candidate's own stored `location`. **No validation** on `query`,
`industry`, or `company` free-text fields. Base filter is only `userid != self` + `is_active` —
**zero Blocks-table awareness** (a real, undocumented gap contrasted directly with Match's BUG-08 fix).
**No `IFeatureGateService` dependency at all** — `radius_km` works for **every** tier, including Free, even
though the equivalent radius feature in Match (`M2-U6`) is explicitly paywalled. Free-text `query` searches
`full_name`/`headline`/`occupation`/`company` via OR'd `ILike` (trigram-indexed); `industry`/`company` are
separate ANDed filters. Radius-vs-plain-text location modes are mutually exclusive; radius is silently
ignored if geocoding fails (explicit documented design choice, mirroring M3-E1). `SEARCHRESULT` cache, 5min
TTL, purely time-based, **no active invalidation** — key is every filter param + page/pagesize concatenated
and lowercased.

| TC | Type | Scenario | Status | Body highlights | Log(s) |
|----|------|----------|--------|------------------|--------|
| SE1-H1 | Happy | `query="engineer"`, no location filters, cache miss | 200 | `issuccessfull=true`; matching users excluding self and inactive accounts; response mapping omits `industry`/`country_full_name`; result cached 5min under the exact param-concatenation key | none |
| SE1-H2 | Happy | Same exact query params within 5min (cache hit) | 200 | identical body served from Redis | none |
| SE1-H3 | Happy | `industry="Finance", company="Acme"` (both ANDed) | 200 | only users matching **both** | none |
| SE1-H4 | Happy | `radius_km=10` + `city`/`state`/`country` supplied (server geocodes these, no client-supplied coordinates) | 200 | radius-mode results, nearest-first ordering | none |
| SE1-U1 | Unhappy (validator) | `radius_km` supplied with **no** location field at all | 500 | `errors=[{errormessage matching the "at least one location field" rule}]` | Warning |
| SE1-U2 | Unhappy (validator) | `page`/`pagesize` out of configured bounds | 500 | `errors=[...]` | Warning |
| SE1-U3 | Unhappy | Unhandled exception | 500 | raw exception leaked | 2× Error + middleware |
| SE1-E1 | **Gap** | A user the caller has **blocked** (or who has blocked the caller) appears in search results, since `Search`'s base filter has **no Blocks-table check at all** — directly inconsistent with Match's BUG-08-protected surfaces | 200 | blocked party **still appears** in results — flagged **G-34**, arguably the most product-relevant unfixed gap found in this entire research pass (a blocked user remains fully discoverable and contactable-via-profile-view through Search even though Match hides them everywhere) | — |
| SE1-E2 | **Gap** | Free-tier user supplies `radius_km` + `city`/`state`/`country` (server-side geocoded, no client-supplied coordinates) | 200, **radius search works with no paywall at all** — `SearchService` has no feature-gate dependency, unlike `MatchService`'s explicit `RadiusSearchEnabled` check (M2-U6) | — | flagged **G-35**, a monetization/consistency gap: the same "feature" is paywalled in one domain and free in another (confirmed live 2026-10-26, still unfixed by design pending a product decision) |
| SE1-E3 | Edge | Geocoding/radius inputs malformed or geocode lookup fails | 200, radius **silently ignored**, falls back to non-radius filtering (documented design choice, same pattern as M3-E1) | — | no warning logged — silent, same **G-21** pattern recurring here |
| SE1-E4 | Edge | `query`/`industry`/`company` contain `%`/`_` (ILike wildcard metacharacters) — no sanitization/escaping mentioned in the research; unverified whether user-supplied wildcards let a caller broaden their own search unexpectedly (low severity, not a security issue since results are still scoped by `is_active`/self-exclusion, but worth a correctness test) | unverified | — | — flagged **G-36** |
| SE1-E5 | Edge | Two requests with filter values differing only in case (`"Engineer"` vs `"engineer"`) | 200 both, **cache key is lowercased** so both correctly hit the same cache entry — confirm this is actually true for every field, not just `query` | 200 | identical cached body | none |
| SE1-E6 | Edge | `SEARCHRESULT` cache is never actively invalidated when a cached user later deactivates, updates their profile, or gets blocked — a stale result can be served for up to 5 minutes after any of these changes | 200 | stale data for up to 5min (purely time-based TTL, documented design, not necessarily a bug but worth a dedicated staleness test) | — |
| SE1-E7 | Edge | No/invalid JWT | 401 | n/a | **G-13** |

---

## 9. Appendix A — Regression test cases for already-fixed bugs

These must stay in the automated suite permanently so a future change can't silently reopen them.

| Fix | Test case(s) in this doc | Mechanism being guarded |
|-----|---------------------------|--------------------------|
| CRITERIA-04 | M2-U1..U4, M2-E1..E4 | `min_age`/`max_age`/`radius_km` bounds + cross-field ordering, all `.When()`-guarded for optionality |
| BUG-01 | U6-E1 | Sub-dedup only fires when `sub` is actually supplied on SignUp |
| BUG-03 | M4-U3, M5-U3 | `is_active`-filtered target lookup before Like/Pass — prevents swiping on deactivated/nonexistent users |
| BUG-04 | M4-E2, M5-E2 | Refund when a non-Like swipe overwrites a prior Like |
| BUG-05 | S3-U3, S4-U3, S5-U2 | `StripeException` caught cleanly → 200 "Payment provider error..." instead of a raw leak |
| BUG-06 | S7-U1 | Bad webhook signature → clean 400, not 500 |
| BUG-07 | S5-E1, S5-E2 | Cancel idempotent only for local `status==Canceled`, not PastDue/Expired |
| BUG-08 | M3-E2, M4-U4, M5 (U1..U4 shared), M8-E1 | `IsBlockedEitherDirection` excludes blocked parties from swiping/recommendations |
| BUG-09 | U6-U6 | Null/empty scraped LinkedIn profile rejected cleanly on SignUp |
| BUG-10 | (see S7-E1/G-30 — the *webhook* idempotency path was never actually given this fix; BUG-10's original fix covered a different table. Needs a direct cross-check against `BUGS_FOUND.md`'s exact BUG-10 description before finalizing this row — flagged for follow-up.) | — |
| BUG-11 | M4-E1, M4-E3 | Refund when `RecordSwipe` reports `wasNewLike=false` (duplicate Like) |
| BUG-12 | M4-H3, M4-E4 | Canonicalized-pair advisory lock on `CreateMatch`; both concurrent callers get `ismatch=true` + correct `matchid`, notify fires once |
| BUG-13 | S7-H3 | `HandleSubscriptionStatusChanged` always writes a transaction, making it idempotency-ledger-protected |
| BUG-14 | U6-E3 | Dedup against normalized + effective (scraped-or-request) email, not raw request email |
| BUG-15 | U6-U8 | `DuplicateUserException` race on concurrent SignUp caught cleanly, not leaked as a raw 500 |

## 10. Appendix B — Newly-identified gaps (found during this test-planning pass, NOT yet fixed)

These surfaced while deriving test cases from the actual code and are **not** in `BUGS_FOUND.md` yet. Listed
here per the user's request to document test cases including edge cases — fixing them is a separate,
not-yet-authorized step.

| ID | Endpoint(s) | Gap | Likely impact |
|----|-------------|-----|----------------|
| G-1 | U1/U2 | LinkedIn OIDC `state` generated but never validated on callback | CSRF — low-moderate, LinkedIn's own `code` validity bounds the blast radius |
| G-2 | M10 (BlockUser) | No existence check on `targetUserId`, unlike Like/Pass's BUG-03 fix | Likely 500 via FK-violation on a nonexistent target |
| G-3 | M10 (BlockUser) | No advisory lock, unlike RecordSwipe/CreateMatch | TOCTOU race on concurrent double-block |
| G-4 / G-4b | N1 (GetNotifications), S6 (GetTransactions) | No validator on page/pagesize; `page<=0` throws an uncaught framework exception | Client-triggerable 500 |
| G-5 | N1 (GetNotifications) | `pagesize` has no upper bound | Perf/DoS surface |
| G-6 | Many (M4/M5/M7/M10/MSG1/MSG2/N3/N5/U2) | Malformed route GUIDs / bad enum values bypass both validation mechanisms (model-binding failures uncaught by either) | Undefined behavior, needs live empirical test |
| G-7 | All (middleware-level) | `ExceptionHandlingMiddleware` leaks raw `exception.Message` to the client on every 500 | Information disclosure |
| G-8 | All (middleware-level) | `LoggingMiddleware` logs full raw headers+body at Information, including JWTs and PII | Privacy/security, log-retention exposure |
| G-9 | N5 (RegisterDevice) | Upsert keyed by `device_token` alone, not `(userid, token)` | Silent push-notification ownership hijack across users sharing/reusing a token |
| G-10 | S1 (GetPlans) | Missing `[AllowAnonymous]` despite doc-comment implying public pricing | Product/UX — can't see pricing pre-signup |
| G-11 | S3/S4 (Checkout/PurchaseConsumable) | Misconfigured Stripe price id throws `InvalidOperationException`, NOT `StripeException` — bypasses BUG-05's catch | 500 with raw Stripe-config leak |
| G-12 | S4 (PurchaseConsumable) | `quantity` has no upper bound | Abuse/cost surface against Stripe |
| G-13 | All protected endpoints | 401 produced by `UseAuthorization()` running *before* `ExceptionHandlingMiddleware`/`LoggingMiddleware` in pipeline order — unverified whether 401s are logged at all | Observability gap — needs live empirical test |
| G-14 | All protected endpoints via `CurrentUserId` | JWT missing `"userid"` claim resolves to `Guid.Empty` instead of 401 | Security-relevant edge case across every domain |
| G-15 | U4 (Logout) | No server-side token revocation — logged-out JWTs remain valid for their full lifetime | Session-management gap |
| G-16 | U5 (LinkedInSignUp) | Returns a raw string, not the standard envelope | Client-contract inconsistency |
| G-17 | U7 (GetLinkedInProfile) | `PERSONPROFILE` cache keyed by raw (case-sensitive) `public_identifier` | Cache-efficiency gap, duplicate scrapes |
| G-18 | A2 (UpdateProfile) | Write-path existence lookup not `is_active`-filtered unlike the read path | Deactivated accounts can still be mutated |
| G-19 | A3 (DeactivateAccount) | No cascading effect on Matches/Messages visible to the counterpart | Stale ghost-profile data surfaced elsewhere |
| G-20 | A4 (DeleteAccount) | No confirmation/grace-period before permanent irreversible delete | Product/security consideration |
| G-21 | M3 (GetRecommendations), SE1 (Search) | `radius_km` silently ignored when ungeocoded, no warning to caller | Silent degraded behavior |
| G-22 | M7 (Unmatch) | Unmatching doesn't reopen the candidate pool for either side | Product question, not clearly a bug |
| G-23 | M8 (GetIncomingLikes) | Free-tier paywall is silent (empty page) vs. M2's explicit error — inconsistent UX pattern | Analytics/UX ambiguity |
| G-24 | MSG2 (SendMessage) | Race between participant-check and insert vs. a concurrent Unmatch (cascade delete) | Unclear/unverified failure mode |
| G-25 | N4 (MarkAllAsRead) | Loads all unread into memory rather than bulk-updating | Perf concern at scale |
| G-26 | N6 (UnregisterDevice) | Free-form route segment, no explicit encoding handling | Possible false "not found" on encoding mismatch |
| G-27 | S6 (GetTransactions) | `pagesize=0` → `has_more` computed as always `true` while returning zero items | Concrete pagination deadlock bug |
| G-28 | S7 (Webhook, checkout.session.completed) | Provisional `periodEnd` never corrected by later webhooks | Data-accuracy gap |
| G-29 | S7 (Webhook, consumable checkout) | Consumable balance adjusted via plain read-then-write, same race shape as original BUG-10, never fixed here | Theoretical double-credit/lost-update race |
| G-30 | S7 (Webhook) | `TransactionExistsByStripeEventId` is an unlocked SELECT — concurrent duplicate deliveries race | Likely 500 on the second delivery via unique-violation |
| G-31 | S7 (Webhook, `.deleted` with status `"active"`) | Status-mapping trusts embedded status string over event-type semantics | Incorrect final subscription status |
| G-32 | S7 (Webhook, invoice.payment_succeeded) | Doesn't reset `PastDue`→`Active`; only `.updated` does | User stuck PastDue after paying, if Stripe delays the second event |
| G-33 | S7 (Webhook, checkout.session.completed) | Neither `consumable_key` nor `plan_code` present → silent no-op, no log | Silent-swallow, no observability |
| G-34 | SE1 (Search) | Zero Blocks-table awareness, unlike Match's BUG-08 | Blocked users remain fully discoverable via Search |
| G-35 | SE1 (Search) | `radius_km` has no feature-gate, unlike Match's paywalled radius | Monetization/consistency gap |
| G-36 | SE1 (Search) | No sanitization of ILike wildcard metacharacters in free-text fields | Low-severity correctness question |

## 11. Appendix C — Summary

- **39 real endpoints** across **7 controllers** now have documented happy-path, unhappy-path, and
  edge-case test coverage with exact expected HTTP status, response shape, and log output, derived directly
  from reading the controller/service/validator/middleware source — not assumptions.
- **3 controllers (`Follower`, `Following`, `Payment`) are empty stubs with zero endpoints** — explicitly
  called out in §1.6; there is nothing to test there today.
- **15 previously-fixed bugs + the CRITERIA-04 gap** each have at least one regression test case mapped to
  their exact mechanism in Appendix A (one row, BUG-10, needs a direct cross-check against
  `BUGS_FOUND.md`'s original wording before being considered fully verified — flagged, not blocking).
- This pass surfaced **36 new, not-yet-fixed gaps (G-1 through G-36)** purely from writing out test cases
  faithfully against the real code paths — most are edge cases around validation gaps, missing
  existence/lock checks, and inconsistent feature-gating, but a handful are concrete, reproducible bugs
  worth prioritizing: **G-27** (pagination deadlock), **G-11** (Stripe-config leak bypassing BUG-05's own
  fix), **G-2/G-3** (BlockUser missing the same protections Like/Pass already got), **G-30** (webhook
  idempotency race, the unfixed sibling of BUG-10/15), and **G-34** (Search has no blocking awareness at
  all, unlike every other domain).
- Per the almost-universal `200`-with-`issuccessfull=false` response pattern (§1.1), **automated tests must
  assert on the envelope's `issuccessfull`/`errormessage` fields, not on HTTP status codes**, for the vast
  majority of unhappy-path cases — asserting status-code-only would pass even when the business logic is
  completely broken, since nearly everything returns 200 regardless of outcome.

