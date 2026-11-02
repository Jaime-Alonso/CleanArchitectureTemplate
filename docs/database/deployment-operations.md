# Database deployment and administrative bootstrap

Use separate deployment, bootstrap and runtime identities. Supply `DatabaseProvider` (`SqlServer` or `PostgreSql`) and `ConnectionStrings__DefaultConnection` through a protected configuration provider or injected environment, never command arguments or logs. The target database must already exist. Set `DOTNET_ENVIRONMENT=Production` for deployment. The following arguments contain no secrets:

```powershell
dotnet src/CleanTemplate.Host/bin/Release/net10.0/CleanTemplate.Host.dll migrate
dotnet src/CleanTemplate.Host/bin/Release/net10.0/CleanTemplate.Host.dll bootstrap-admin
dotnet src/CleanTemplate.Host/bin/Release/net10.0/CleanTemplate.Host.dll
```

Build/publish the application first. Run migration as a single deployment job using a DDL-capable owner, after backup and review of [versioned migrations](versioned-migrations.md); do not run multiple migration jobs concurrently. The bootstrap job uses a separate identity with read/write access to Identity tables, no schema ownership required. Inject `IdentitySeed__AdminEmail` and `IdentitySeed__AdminPassword` only into that job, using a secret manager; remove them after it exits. Password validation follows the configured Identity policy. Check the exit code; errors deliberately log only their type. Do not enable SQL parameter-sensitive logging or capture secret environment variables. Environment variables can be visible to privileged local processes; use the deployment platform's secret injection and access controls.

Bootstrap serializes cooperating jobs with a transaction-scoped PostgreSQL advisory lock or SQL Server application lock; role creation, user creation and Admin assignment commit together. It refuses if an Admin exists or the requested normalized email belongs to any existing account; it never elevates or changes an existing account. Repeating it after removing Admin from that account also fails, rather than restoring the role. Recovery or deliberate elevation of existing accounts requires a separate audited administrative procedure, not this command. The lock does not serialize unrelated direct database writes or other provisioning mechanisms; quiesce those during bootstrap.

The API runtime never invokes migration or bootstrap for persistent databases, in any environment. It needs no bootstrap secret or DDL. For the default named in-memory SQLite only, Development/Testing creates the ephemeral schema and initial admin on startup; this is not a persistent deployment mechanism. Production never initializes SQLite.

## Runtime database privileges

Grant database connect plus only the DML needed by the enabled application features. A tested baseline for the current schema is SELECT, INSERT, UPDATE and DELETE on the application tables (Identity, products, refresh tokens and external grants); no CREATE, ALTER, DROP, ownership, migration-history write, role-management or database creation. SQL Server can use per-table GRANTs (or `db_datareader` and `db_datawriter` as a broader baseline), with a database user mapped to a dedicated login, not db_owner. PostgreSQL needs CONNECT on the database, USAGE on `public`, and SELECT/INSERT/UPDATE/DELETE on the application tables, without CREATE on the schema; do not make the runtime role an owner or superuser. Reapply narrowly scoped grants after migrations add tables. Restrict the optional Dapper read connection to SELECT when configured separately. Additional features may require a reviewed privilege change. These are database permissions, not application authorization.

## Verification

`pwsh -NoProfile -File scripts/tests/Test-VersionedMigrations.ps1` creates and removes its own randomly named SQL Server and PostgreSQL containers and databases. It runs migration acceptance and `DeploymentAcceptanceTests`: explicit migration without the bootstrap password, two secret-free runtime replicas using a principal denied DDL, concurrent bootstrap using that same limited principal, verification of the single user and Admin assignment, and revocation across restart. Docker and the required images are needed. The test is not a substitute for production deployment rehearsal, external identity/IdP testing or backup/recovery validation.

The final T-10 review passed this acceptance in Release on both engines (four migration and three deployment cases per engine). The complete Release solution test run also passed; its five database acceptance cases are skipped when the isolated engine configuration is absent and were exercised separately by the script.
