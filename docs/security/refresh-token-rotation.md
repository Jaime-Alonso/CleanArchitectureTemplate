# Atomic refresh-token rotation (T-06)

`FindValidAsync` is an advisory, no-tracking lookup: it does not consume the token or grant a right to rotate. The handler checks the user and lockout, loads current roles and generates the access token before calling `RotateAsync`. No token pair is returned unless rotation commits successfully.

`RotateAsync` starts a database transaction and conditionally updates the original row by token hash, expected user, absence of revocation and non-expiration. It records the replacement hash and inserts the replacement in the same transaction. An affected-row count other than one rejects the request without issuing a replacement. The database write, not a local mutex or an earlier lookup, arbitrates concurrent requests across instances.

Failure or cancellation before commit rolls back both changes. A failed replacement is detached from the EF tracker so a later save cannot accidentally insert it. There are no automatic retries: a connection failure during commit can have an ambiguous outcome. If commit succeeds but the response is lost, the original remains consumed; clients must reauthenticate rather than assume the old token is reusable. `IssueAsync` remains for initial login only, not rotation. The schema already contains the replacement hash, so this increment needs no migration.

## Evidence and limits

- The same 15 cases run on SQLite, SQL Server 2022 and PostgreSQL 17. Each test uses separate contexts/connections and two independent DI containers. Both rotation requests pass the advisory lookup before racing; exactly one returns a replacement, with two total rows and a matching replacement hash. These are real database/service tests, not two deployed API processes.
- Further tests cover failed insertion, failure/cancellation after insertion but before commit, pre-canceled requests, invalid token, wrong user, revocation, expiration, hash-only storage and sequential replay. Rollback is verified from a fresh context. Handler tests cover a lost rotation race, missing/locked users and access-token generation failure before consumption. An HTTP regression verifies rejection of the consumed token while its replacement remains usable (family-wide revocation is not implemented yet).
- Production-provider acceptance passed in isolated Docker containers: 15/15 on SQL Server and 15/15 on PostgreSQL, with no skipped tests. Each case creates a uniquely named database and applies its provider's real migrations, not `EnsureCreated`. New cases exercise an actual unique-index insertion failure, logout racing rotation and a real connection close after insertion but before commit. Another case throws after the database has committed to simulate acknowledgment loss: both rows remain committed, the exception propagates and retrying the original is rejected. This does not simulate every network/server failure or validate an existing production installation.
- This transaction uses the request-scoped context and is not the application-wide unit-of-work extraction in T-13. Existing context transactions and ambient transactions are explicitly rejected before rotation, with regression tests for both. Do not treat this as isolation from unrelated tracked changes: `SaveChangesAsync` still saves the context's pending changes.
- T-07 remains pending: no session-family revocation, replay-triggered descendant revocation, absolute session lifetime or sensitive-change session invalidation is claimed. Logout currently revokes only the presented token. Already-issued access tokens remain valid until expiration unless another existing authorization check denies access; there is no new immediate access-token revocation mechanism.

## Reproduce production-provider acceptance

With Docker running, from the repository root:

```powershell
pwsh -NoProfile -File scripts/tests/Test-RefreshTokenRotation.ps1
# Or one provider:
pwsh -NoProfile -File scripts/tests/Test-RefreshTokenRotation.ps1 -Provider PostgreSql
```

The script uses `mcr.microsoft.com/mssql/server:2022-latest` (Developer edition) and `postgres:17`, generated credentials, loopback-only random ports and bounded readiness checks. It removes only containers it created, even if a test fails, and restores process environment variables. It does not use persistent volumes or print generated secrets. Images remain cached locally. The fixture always replaces the supplied database name with a random owned name and drops only that database. Do not point test connection settings at shared or production servers.

`REFRESH_ROTATION_TEST_PROVIDER` selects `Sqlite` (default), `SqlServer` or `PostgreSql`; non-SQLite providers require `REFRESH_ROTATION_TEST_CONNECTION`. A missing connection or unknown provider fails, rather than skipping or silently falling back to SQLite. Only the refresh-rotation test fixture reads these settings; other tests retain their existing setup.

Latest full workspace validation: `dotnet test CleanArchitectureTemplate.slnx -c Release --nologo` passed all 228 tests (7 Domain, 19 Application, 38 Architecture, 98 Infrastructure, 66 API), without skips, in one solution invocation. Separately, the Docker script passed all 30 provider cases and left no labeled test containers. T-06 acceptance is complete; T-07 and the other production-readiness tasks remain pending.
