# Refresh session families (T-07)

## Policy

- Every successful local login creates an independent persisted family. Rotation preserves its ID; no family state is trusted from a JWT or client input.
- `RefreshTokenDays` is the individual token lifetime (default 14 days). `AbsoluteSessionDays` caps the entire session from its original login (default 30 days). Both accept 1–90 days; rotation expires at the earlier of these deadlines and never extends the family deadline. Expiration is exclusive: equality is expired. There is no concurrent-session count limit in this increment.
- Logout revokes the entire family, even when presented with a consumed ancestor. It does not revoke another login's family. The HTTP response remains enumeration-resistant `204`.
- Presenting a consumed token with a replacement link is reuse: the entire family is permanently revoked, including all descendants. This applies even when that ancestor's individual expiration has passed. Unknown tokens do not revoke arbitrary sessions.
- The reuse policy is **strict**, without a grace period or idempotent refresh retry. Two concurrent requests can produce exactly one replacement, but the loser revokes that replacement's family. Clients must serialize refresh, must not retry consumed tokens, and must reauthenticate after replay or an ambiguous/lost response. An attacker possessing an ancestor can force logout of that family; this is the deliberate tradeoff of strict replay revocation.

## Withdrawal and sensitive changes

The application rejects refresh for a missing/locked user and invokes `RevokeAllForUserAsync` to revoke that user's existing families. Administrative account-disable and other withdrawal workflows must invoke the same contract. This template does not add public administration/password-change endpoints in this increment.

Each family snapshots ASP.NET Identity's security stamp at login. Rotation compares the current persisted stamp; a mismatch (including deletion of a normal Identity user) revokes that family before any replacement is issued. Password/reset/security operations must use Identity APIs that update the stamp, or explicitly update it and invoke `RevokeAllForUserAsync`. Direct database edits that leave the stamp unchanged are not detected as sensitive changes. Other families with an old stamp are denied when next refreshed; no background push invalidation is claimed. Authorization still loads current effective permissions under T-03: role withdrawal removes permissions, not necessarily the ability to retain a lower-privilege session.

An operation already in flight may complete while an account changes. There is no cross-transaction atomicity between Identity changes, a new concurrent login and session revocation; T-13 is not implemented by this increment.

## Access tokens are not session-revoked

Logout, replay and security-stamp changes stop **refresh**, not a local JWT already issued. It can still authenticate until its configured expiration (`AccessTokenMinutes`, default 30), subject to the validator's clock skew and existing live authorization/lockout checks. Family state is not checked per access-token request. The HTTP regression explicitly verifies a still-authorized access token works after family logout. Do not claim immediate access-token invalidation; short access-token lifetimes remain important.

## Atomicity and retention

Rotation first conditionally increments the family's version in its own database transaction, acquiring its write lock. It rechecks family expiration after obtaining the lock, compares the security stamp, conditionally consumes the token and inserts the replacement before commit. Reuse found after waiting commits family revocation instead. Logout/revoke-all update those same family rows, so they serialize with rotation in the database, not in a process-local mutex. Separate DI containers/connections are tested; this is not a deployment test with multiple API processes.

The family's revocation flag is authoritative; descendant token rows need not all be individually marked revoked. Failures before the database commit roll back the family version, consumption and insert together. Post-commit acknowledgment loss is ambiguous and is never automatically retried. The service still uses a request-scoped context; unrelated pending changes are not isolated and outer transactions are rejected.

Cleanup retains **all** ancestors of a still-active family, even consumed/individually expired tokens, so cleanup cannot erase evidence and disable replay detection. It deletes expired families or families past the configured revoked retention, cascading their token rows. Legacy rows without a family are cleaned separately.

## Deployment / compatibility

Additive `AddRefreshTokenFamilies` migrations exist for SQL Server and PostgreSQL. Apply migrations with the existing deployment procedure before running the new binary. Drain old application instances: old binaries do not enforce family revocation. This is not a rolling mixed-version-compatible security policy.

Existing token rows keep nullable `FamilyId` and are deliberately rejected; users must log in again. Do not backfill families by guessing links or treating each old descendant as an independent active session. No existing user/product data or refresh history is deleted by the migration. Rolling back to an old binary loses the new security guarantees and must not be used to restore revoked sessions.

## Validation

Run `pwsh -NoProfile -File scripts/tests/Test-RefreshTokenRotation.ps1` for the same rotation/family cases on real SQL Server and PostgreSQL containers with provider migrations. The fixture also tests upgrading the previous schema with an existing legacy token, its safe rejection and successful new login. Normal `dotnet test CleanArchitectureTemplate.slnx -c Release` runs SQLite and the Application/HTTP regressions. See [rotation validation](refresh-token-rotation.md) for the Docker harness and isolation guarantees.
