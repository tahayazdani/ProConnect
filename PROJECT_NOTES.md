# ProConnect — Project Notes

> Living document. Update this file whenever a phase starts/finishes, a decision changes, or something new is discovered — this is the source of truth for where the project stands, not the conversation history.

## What this is

ProConnect is a **professional-networking dating app** — "Hinge/Tinder, but for professionals, matched on LinkedIn data." Users sign up/sign in with their LinkedIn account; their profile (name, photo, company, location, skills, etc.) is fetched and used to recommend matches based on shared signals (location, company, school, interests). Matched users can follow each other, message, and manage subscription/payment.

## Architecture

Clean-architecture layering across 4 projects (+ tests would go alongside if added later):

| Project | Responsibility |
|---|---|
| `ProConnect.Domain` | Models, request/response DTOs, config POCOs, enums/constants. No dependencies on other projects. |
| `ProConnect.Application` | Business logic (`*Service` classes), FluentValidation validators, `ITokenService`, global `ExceptionHandlingMiddleware`. |
| `ProConnect.Infrastructure` | Repositories (`I*Repository`), HTTP client wrapper, Redis-based cache-aside layer (`Cache` + `ICache`/`RedisCache`). |
| `ProConnect.WebApi` | Controllers, DI wiring (`Program.cs`, `DI/DI.cs`), Serilog logging, Swagger, API versioning, JWT auth. |

Cross-cutting conventions already established in the codebase — follow these for consistency:
- Every service call returns `ProConnectResponse<T>` ([ProConnectResponse.cs](ProConnect.Domain/Response/ProConnectResponse.cs)) — `body`, `message`, `statuscode`, `errormessage`, `errors`, `issuccessfull`.
- Controllers are thin: call the service, `return StatusCode(response.statuscode, response);`. All inherit `BaseController` ([BaseController.cs](ProConnect.WebApi/Controllers/BaseController.cs)) which exposes `CorrelationToken` and `CurrentUserId`.
- Config sections are POCOs under `ProConnect.Domain/Config`, nested inside `ProConnectConfig`, bound via `IOptions<ProConnectConfig>`.
- Repositories are stubbed with `throw new NotImplementedException();` until a database is chosen (see Decisions below) — this is intentional, not a bug.
- Request/response property names are `snake_case` to match LinkedIn/Proxycurl's own JSON shape.

## Key decisions (don't re-litigate without asking)

- **Database: deferred.** No DB technology chosen yet. Every repository interface gets a real implementation class that throws `NotImplementedException` for now — matches the pre-existing `UserRepository.SignUp` pattern. This lets the full vertical (controller → service → validation → repository contract) get built and tested for auth/shape, ready to plug into a real store later.
- **Proxycurl: kept as-is.** Third-party LinkedIn scraper used to enrich profiles at signup. User is not certain it's still active — verify/rotate the API key separately before relying on it. Not abstracted behind a swappable provider interface (decided against that for now).
- **Auth: our own JWT.** Issued after LinkedIn sign-up/sign-in via `ITokenService`. Bearer tokens, `[Authorize]`-by-default (global fallback policy in `Program.cs` requires authentication on every endpoint unless `[AllowAnonymous]`).
- **Messaging: REST only for now.** No SignalR/real-time in the first pass.
- Follow and messaging are **open** (not gated behind a mutual match) unless told otherwise — easy one-line change later if that's wrong.
- `AccountController` = the user's own profile + (eventually) an aggregate settings view. `Payment`/`Subscription`/`Match` own their specific domains.

## Known pre-existing issues (found along the way, not yet fixed)

- [appsettings.json](ProConnect.WebApi/appsettings.json) has real-looking secrets committed in plaintext (Redis password, LinkedIn `client_secret`, Proxycurl token). Should be rotated and moved to user-secrets/environment/vault — the new `JwtConfig:secret` was set up correctly via `dotnet user-secrets` as the model to follow.
- Local `NuGet.Config` was added at the repo root, scoped to `nuget.org` only — the machine's global NuGet config points at internal/HTTP feeds (looks like a work environment leftover) that broke package restore for this project.

## Phase plan

Each phase after Phase 0/1 only depends on the Phase 0 auth foundation — they can be reordered or dropped independently.

| Phase | Scope | Status |
|---|---|---|
| 0 | JWT auth foundation — package, `ITokenService`, `JwtConfig`, `Program.cs` wiring, `CurrentUserId`, `[Authorize]`-by-default | ✅ Done |
| 1 | Complete authentication — JWT issuance on signup/signin, `SystemAuthentication` as "/me", `AccountController` profile GET/PUT | ✅ Done |
| 2 | Match criteria + recommendation engine (`MatchController`) — criteria CRUD, scored recommendations, like/pass, mutual-match detection, Redis-cached results | ⬜ Not started |
| 3 | Search (`SearchController`) — ad hoc user lookup, distinct from the recommendation engine | ⬜ Not started |
| 4 | Follow system (`FollowerController`, `FollowingController`) | ⬜ Not started |
| 5 | Messaging (`MessageController`) — REST send/list, scoped to conversations | ⬜ Not started |
| 6 | Notifications (`NotificationController`) — triggered internally from Match/Follow/Message events | ⬜ Not started |
| 7 | Subscription & Payment (`SubscriptionController`, `PaymentController`) — plans, transaction history; real payment-gateway integration deliberately deferred (moving real money is a separate, explicit step) | ⬜ Not started |

Full original plan detail (context, assumptions, verification approach) lives at the plan file this was approved from — but treat *this* file as the up-to-date source once the two diverge.

## Changelog

- **2026-08-11** — Phase 0 + Phase 1 implemented and verified (build, auth-wall behavior, validation behavior). See "Known pre-existing issues" above for what was found but not fixed.
- **2026-08-11** — Fixed the `SignUpValidator` `NullReferenceException` noted above: `ValidateBirthDate`/`IsAdult` now null-check `birth_date` and fail validation cleanly instead of crashing. Verified: a signup request without `birth_date` now returns a proper `INVALIDDATEFORMAT`/`NOTADULT` validation response instead of an unhandled exception.
- **2026-08-11** — Full codebase null-reference audit (all 49 `.cs` files read). Found and fixed:
  - `SignUpValidator`: `public_identifier` had no rule at all — a missing one made it all the way to the Redis cache-key hasher and threw `ArgumentNullException`. Added `NotEmpty()`.
  - `UserService.SignUp`/`UpdateProfile`: a literal JSON `null` request body reaches the service layer as `null` — `ApiBehaviorOptions.SuppressModelStateInvalidFilter = true` (set in `Program.cs`) disables ASP.NET Core's automatic "non-nullable body is implicitly required" check, and there's no FluentValidation validator for `UpdateProfileRequest`, so nothing upstream catches it. Both methods now guard `request is null` and return a clean `ProConnectResponse` instead of crashing.
  - `UserService.SignUp`: `GetPersonLinkedInProfile` (Proxycurl) can return `null` if the API responds with an empty body or JSON `null` on a 200 — guarded before use.
  - `UserService.LinkedInAuthentication`: both the LinkedIn token-exchange response and the userinfo response can come back `null` the same way — guarded before dereferencing `.access_token`/`.sub`.
  - `UserService.SystemAuthentication`/`GetProfile`/`UpdateProfile`: guarded the `IUserRepository.GetById`/`Update` "not found" case (returns `null`) — not reachable today since repos are stubbed with `NotImplementedException`, but this is the normal contract for a lookup-by-id method once a real database is wired in (Phase 8+), so guarding now avoids a latent NRE later.
  - `TokenService`: constructor now throws a clear `InvalidOperationException` if `ProConnectConfig:JwtConfig` fails to bind, instead of a deferred NRE the first time a token is generated.
  - Reviewed and ruled out as non-issues: `Cache`/`RedisCache`/`BaseCache` (struct-based `CacheItem<T>`, all `.Value` accesses already guarded by `Exists`/`foundInCache` checks), `HttpClientUtilities` (all optional params consistently null-checked before use), `BaseController.CurrentUserId` (always assigned before read), `DI.GetConfigValue` (only reachable at startup with already-valid config, not a live-request path).
  - Verified via running API: missing `public_identifier` now returns a clean validation error; a literal `null` signup body now returns `"Request body is required."` instead of crashing.
