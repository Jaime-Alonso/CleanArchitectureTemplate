# Role ownership, synchronization and revocation (T-03)

## Source of truth

Role ownership is explicit and additive:

| Source | Storage | Who changes it |
| --- | --- | --- |
| Manual | Identity's `AspNetUserRoles` | Administrative code using `UserManager` |
| External | `ExternalRoleGrants`, keyed by user, login provider, provider subject key and role ID | Synchronization of that validated external login |

`ExternalRoleGrants` uses the hashed login keys described in [the identity trust model](external-identity-trust.md). It never treats the provider's display label or email as ownership proof. Each grant references an existing internal user and role. A grant only contributes permissions while its exact login still belongs to the user.

The effective roles of an unlocked, existing account are:

```text
manual assignments ∪ unexpired grants from currently linked external logins
```

The union is account-wide, including when more than one external login has been deliberately linked. A role remains effective if any independent source still grants it. Removing one source is not a global deny. Automatic linking remains prohibited; no linking API is introduced by this work.

External synchronization never calls `AddToRoleAsync` or `RemoveFromRoleAsync`. It replaces only its own grants: removes obsolete roles, adds new ones and updates expiration. Removals/additions/renewals are saved together by one relational EF `SaveChangesAsync` transaction. This does **not** make user creation, linking and profile updates one transaction: first-access concurrency and partial provisioning remain T-08.

Manual and external ownership may overlap safely. A manual Admin assignment is preserved when an external Admin grant is removed. Conversely, removing manual Admin does not erase an independently valid external Admin grant.

## One permission reader for both flows

`IUserAuthorizationService` reads current roles and account lockout state from the primary `ApplicationDbContext`. It does not cache permission decisions or use the read-replica connection. Missing and locked accounts have no internal authorization context. Disabling provisioning excludes all external grants from this reader; manual roles remain available.

- Local JWT validation still verifies signature, issuer, audience and lifetime. After validation, the GUID subject selects the internal account and its **current database roles**, not the roles embedded in the JWT.
- External provisioning updates the source's grants, then uses the same permission reader for its result.
- Login and refresh use this reader through `IAuthUserService.GetRolesAsync`, so newly issued tokens reflect the same effective roles.
- `UserManager.GetRolesAsync` now means manual assignments only. Do not use it as the business API's effective-role reader. Use `IUserAuthorizationService` or the application auth service.
- The local JWT wire format remains compatible, but role claims are only an issuance-time snapshot for clients; they do not authorize requests on this API.
- There is no shared Redis/memory permission cache to invalidate. Database/read failures propagate and do not fall back to token roles. A request reuses its own transformed identity; in-flight requests are not retroactively interrupted by later changes.

This adds primary-database reads to authenticated requests and makes internal authorization dependent on database availability. This is an intentional correctness-first trade-off. Any future cache must preserve the stated revocation bounds and fail-closed behavior.

## Expiration and revocation bounds

`Auth:Provisioning:RoleGrantLifetimeMinutes` defaults to 5 and must be between 1 and 60. Each synchronized grant expires at:

```text
min(current UTC time + configured lifetime, original external JWT exp)
```

The unique validated `exp` is carried into the application request. Missing/invalid expiration cannot provision permissions. An already expired token cannot renew grants, even inside the JWT validator's normal clock-skew window. Renewing with the same token never extends a grant beyond that token's original expiration.

| Event | Observable behavior |
| --- | --- |
| Manual role removal | Absent on the next authenticated request, unless another source independently grants it |
| Observed external mapping downgrade | That login's obsolete grants are removed during the request; following local/OIDC requests use the new effective union |
| No further external traffic | External grants stop contributing at their stored expiration, without a cleanup job |
| External login removal | Its grants stop contributing on the next permission lookup, even if the rows remain stored |
| Account lockout or deletion | No internal permission context on the next permission lookup |
| Provisioning disabled through deployment/restart | Stored external grants are ignored; manual roles are retained |

**These bounds do not imply instantaneous revocation at the identity provider.** A self-contained, still-valid OIDC token may continue to assert an old group or role and can renew matching grants up to its original expiration. A newer downgraded token is not an anti-rollback watermark: an older valid token may reassert its privileges. This template does not introspect tokens, consume provider revocation events, or maintain a token-issuance watermark. Use short external token lifetimes and a reviewed provider integration if stricter revocation is required.

For immediate internal denial independent of provider token claims, use account lockout or remove the external login as appropriate. Merely deleting grant rows is not a durable ban while their source can still authenticate and synchronize them again. A manually retained privilege is not an obsolete external grant.

Configuration/mapping changes require a controlled restart; partial live reload is not the contract. Keep node clocks synchronized. The existing bootstrap administrator may still be reassigned Admin during startup until T-10 is addressed; this synchronization feature does not override that explicit administrative behavior.

## Scope of session revocation

Role re-evaluation affects the internal policies `AdminOnly` and `UserOrAdmin` without waiting for local access-token expiration. It does not revoke the JWT signature or make an otherwise valid token unauthenticated. Generic authentication-only endpoints can still recognize the bearer identity without granting internal permissions.

Logout and refresh-token family revocation remain T-06/T-07. This feature neither makes refresh rotation atomic nor revokes refresh-token families. Deleted, locked or demoted users cannot restore lost permissions by refreshing, because permission resolution uses current state; full session lifecycle guarantees remain separate work.

## Deployment and legacy assignments

This increment adds the `ExternalRoleGrants` table. Apply reviewed provider-specific migrations before enabling it on a persistent existing database. The concurrently developed SQL Server/PostgreSQL migration baselines include the entity; this feature does not regenerate or claim production validation of those migrations. See [the versioned-migrations procedure](../database/versioned-migrations.md) and T-09's remaining acceptance criteria.

Older versions persisted both manual and externally mapped roles in `AspNetUserRoles`, without ownership metadata. Their origin cannot be inferred safely from role name, email or the next external token. **Existing rows remain manual; they are not silently deleted, reclassified or adopted by synchronization.** Before rollout, audit those assignments and explicitly remove externally originated obsolete rows or confirm their manual ownership. Otherwise a legacy Admin assignment will intentionally continue to contribute as manual Admin. This review is a mandatory deployment prerequisite, not an automated migration supplied by T-03.

Expiry revokes permissions without deleting stored rows. Physical cleanup of expired/orphan grants can be added separately; it is not needed for authorization correctness. Migration, role audit and recovery should be rehearsed on the production provider before rollout.

## Verification

- Relational Identity tests cover external demotion, overlapping manual roles, independent login sources, expiration boundaries with a controlled clock, token-expiration caps, unlinking, lockout, deleted users, disabled provisioning, invalid mappings and rollback on grant-persistence failure.
- HTTP policy tests use an unchanged signed local Admin token before/after a permission change, proving there is no fallback to stale token roles.
- The composed HTTP integration uses the real product endpoints, handlers, Identity stores, synchronizer and permission reader with SQLite and signed external JWTs/static metadata. External demotion removes write access from an already issued local token and its refresh emits no obsolete Admin role; manual grants survive synchronization and explicit manual removal is reflected in both flows.
- These tests do not replace real-provider revocation tests, production-engine integration, first-access concurrency tests (T-08), or the session work (T-06/T-07).
