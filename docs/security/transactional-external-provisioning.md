# Transactional external provisioning (T-08)

## Scope and guarantees

Each external provisioning attempt owns one **serializable relational transaction** containing:

1. Lookup by the exact issuer/subject-derived login keys.
2. Verified-email collision and account/tenant checks.
3. User creation, external login association and verified profile changes.
4. Source-owned role removal, insertion and renewal.
5. Reading effective internal permissions and validating token expiration before commit.

Only a successful commit returns a successful ProvisionResult. Expected rejections explicitly roll back; exceptions and cancellation dispose the uncommitted transaction. A failed login association or grant write cannot leave an orphan user, a half-updated profile or partially replaced permissions.

The outer ExternalIdentityProvisioningService creates a **fresh DI scope for each attempt**. Its UserManager, ApplicationDbContext and authorization reader must use the same EF-backed store/context within that scope. No tracked entities from a failed attempt are reused. The service neither saves nor clears the caller's pending changes, and refuses an ambient System.Transactions transaction instead of promising cross-unit atomicity.

This is a provisioning-specific unit, not the generic application unit-of-work extraction pending in T-13. Register it through AddInfrastructure, or use AddExternalIdentityProvisioning alongside the required Identity/persistence/authorization adapters in isolated tests.

## Concurrency and uniqueness

- Identity's existing unique normalized username index protects the deterministic external username.
- The external-login primary key protects the canonical issuer/subject pair.
- A new **unique EmailIndex on NormalizedEmail** protects concurrent creations and profile changes. RequireUniqueEmail validation alone is not sufficient, since a check followed by an insert is not atomic.
- Grants remain unique per user, login and role.
- Serializable isolation protects the lookup/check/write sequence. Deadlocks, serialization conflicts and uniqueness races can require restarting the entire unit; locks or semaphores held only by one application instance are not the correctness mechanism.

Concurrent accesses for the same identity converge on one account after the winner commits. Different identities with a colliding normalized email are rejected; retries never reinterpret email as permission to link accounts. Changing email, provider display label or tenant does not change the identity trust rules documented in [the trust model](external-identity-trust.md).

## Bounded retries and failure semantics

The service performs at most **three attempts**, with cancellable TimeProvider-based waits of 25–49 ms and 50–74 ms between them. Small random jitter avoids deliberately waking competing instances in lockstep. This bounds attempts, not total response time: provider connection/command timeouts and request cancellation still govern database waits.

Retries are restricted to:

| Source | Recognized conflicts |
| --- | --- |
| EF Core | DbUpdateConcurrencyException |
| Identity results | ConcurrencyFailure, DuplicateUserName, DuplicateEmail, LoginAlreadyAssociated |
| PostgreSQL | SQLSTATE 40001, 40P01, 23505 |
| SQL Server | Error numbers 1205, 1222, 2601, 2627, 3960 |
| SQLite | BUSY/LOCKED; unique/primary-key constraint violations only |

Every failed attempt is rolled back and disposed before waiting or retrying. Foreign-key/check failures, unknown configuration errors and ordinary network/connection failures are not blindly retried. In particular, an ambiguous failure during commit is not replayed automatically. A later client request must re-read the canonical identity; this service does not report success for an unknown commit outcome.

Cancellation stops retries, and each attempt revalidates the external token after acquiring the transaction and again before commit. Requests already in progress are not a promise of continuous provider-token introspection or retroactive interruption after the validation point.

If contention exhausts the budget, the service returns Auth.ExternalProvisioningConflict. The current claims-transformation boundary grants no internal identity for failures and exposes no database exception or account-existence detail. Unexpected errors continue through normal error handling; there is no cached-permissions fallback.

The current provider configuration uses no EF automatic retrying execution strategy. Do not independently enable per-command retries or wrap provisioning in another transaction: any future strategy must preserve fresh-context, whole-unit retries and handle ambiguous commits deliberately.

## SQLite development connections

The application's SqliteInMemory profile now requires a **named** database with Mode=Memory;Cache=Shared. A keeper connection preserves its lifetime; each DbContext receives its own connection. Sharing one physical SqliteConnection among simultaneous contexts cannot support independent transactions.

Private `:memory:` connections and SQLite files are rejected by AddInfrastructure. Standalone test contexts can still own a private connection, and the concurrency tests deliberately use a uniquely named temporary file with multiple independent connections/providers. Those tests do not introduce SQLite as a supported production database or bypass the application's Production schema guard.

## Schema rollout and duplicate-email review

EnforceUniqueIdentityEmail is an **additive migration for both SQL Server and PostgreSQL**. The initial migrations are not rewritten. SQL Server uses a filtered unique index for non-null normalized emails; PostgreSQL's normal unique-index semantics also allow multiple null values.

Before applying the migration:

1. Back up and rehearse recovery on the actual provider. Stop or coordinate incompatible writers for the reviewed deployment window.
2. Inventory non-null NormalizedEmail values with more than one owner. Protect that output as personal data. Review ownership, legacy linking and normalization explicitly; do not delete, merge or relink accounts merely because their emails match.
3. Resolve duplicate normalized values through an approved administrative process. The migration deliberately fails on unresolved duplicates; it contains no data-merging workaround.
4. Generate and review the idempotent provider script using [the migration procedure](../database/versioned-migrations.md). Test upgrading a copy of the existing baseline and verify user IDs, logins, roles and constraints. Until T-10 separates deployment tasks, startup still applies migrations: review duplicates before starting the upgraded Host.
5. Deploy with the unique index in place before relying on these concurrent-creation guarantees. Rolling back the index weakens the invariant and requires a separate deployment decision.

Existing EnsureCreated databases still need the explicit migration procedure; neither T-08 nor its index migration silently adopts their schemas. Legacy role provenance review from T-03 also remains required.

## Evidence and remaining validation

- Real relational SQLite tests synchronize the start of transactions across two independent DI containers and multiple connections, then verify one canonical user/login/grant and no cross-subject linking by email.
- Failure injection checks rollback after user creation, login association, profile updates and role changes; bounded retries use distinct contexts. Tests also cover cancellation, token expiry while waiting, ambient-transaction rejection, caller change-tracker isolation and non-retryable/commit failures.
- SQLite BUSY/LOCKED and PostgreSQL SQLSTATE injection exercise the retry classifier. Injecting PostgreSQL errors into SQLite tests is **not** a PostgreSQL integration test. SQL Server retry error numbers still require provider-level verification.
- Provider snapshots match the model; generated upgrade SQL creates the unique index without dropping tables. This does not prove applying the migration or concurrency behavior on SQL Server/PostgreSQL.
- Before production, run equivalent multi-instance contention, upgrade, cancellation and failure-recovery scenarios against both supported production providers. Bootstrap/deployment separation (T-10), atomic refresh rotation (T-06), session families/replay handling (T-07), and complete operational hardening remain separate work.
