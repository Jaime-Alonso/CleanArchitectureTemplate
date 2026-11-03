# Observability and security operations

The template does not deploy a collector, Prometheus, alert manager or storage. The operator must configure, secure and verify all pipelines, labels, routing, thresholds, retention and delivery. The examples are starting points, not production SLOs or a guarantee of detection. Monitor exporter/collector failures, missing series and target availability separately: `increase` cannot alert on absent telemetry and needs multiple samples. Sampling affects traces, not these metrics.

## Correlation and privacy

Default structured JSON logs go to stdout (including bootstrap); no local files are enabled by default. Enabled traces and metrics use the configured OTLP exporter, not files. `service.version` is the Host assembly informational version: set it at build/publish time and identify the artifact; it is not `OpenTelemetry:ServiceVersion`. W3C trace/span IDs correlate request logs, exported traces and ProblemDetails `traceId` when an activity exists; otherwise the HTTP trace identifier is a fallback, not necessarily a W3C trace ID or an exported trace. Sampling and export failure can leave no trace. Request logs use controlled method, route template, status, outcome and duration; never place secrets or personal data in route literals.

HTTP client/server spans identified by `http.request.method` or legacy `http.method` are minimized before export. Other spans, including database instrumentation, are not sanitized by this processor: review every additional instrument, enrichment, exporter, listener, collector transformation and output. On .NET 10 a processor-owned `System.Net.Http` ActivityListener suppresses native exception-event tags while the processor is alive; the event remains, and existing events or other listeners are not generally scrubbed. Do not capture tokens, passwords, bodies, headers, raw URLs, SQL values or arbitrary exception messages. Validate actual emitted data, including third-party components and configuration overrides.

## Retention and access policy (proposed defaults)

The operator should enforce **logs 14 days, traces 7 days, metrics 30 days, privilege audit 90 days** in the respective backend, including collector queues, replicas and backups according to an explicit deletion policy. Separate/reroute audit event `PrivilegeAssignmentCommitted` (event ID 2601) to a restricted audit stream with its own 90-day TTL; otherwise the ordinary 14-day log TTL also deletes it. GUID user and role identifiers are pseudonymous personal data, not anonymous; restrict RBAC to designated responders/auditors, protect transport and storage, set collector/queue TTLs and review access. Avoid secrets and direct PII in all signals. These are proposed operational defaults, not legal advice or a claim of immutable, complete or durable delivery. Best-effort in-process emission and external pipelines can lose events.

## Metric semantics and audit boundary

| OTLP counter | Meaning |
| --- | --- |
| `security.refresh.replay` | Reuse detected, including concurrent rotation losers; normal expiry is not replay. Investigate clients that fail to serialize refreshes as well as potential compromise. |
| `security.provisioning.rejected` | Rejected provisioning attempts, not unique identities or committed writes. |
| `security.provisioning.contention` | Contention/retry attempts; one request can contribute more than once. |
| `security.cleanup.failure` | Failed cleanup runs, not an inventory of expired tokens. |
| `security.privilege.committed` | Net tracked assignment changes after EF autocommit or explicit EF commit, with bounded `source` and `change` labels (`grant`, `revoke`, `renew`). Not a count of effective privileges or users. |
| `cleantemplate.http.errors` | Non-cancelled 5xx or faulted HTTP requests; dimensions method, route template, status and outcome. Probes may contribute. 401/403 are separate rejection counters. |

`renew` means a net validity timestamp change, including shortening; it does not assert that effective access was extended. The informational privilege-change alert includes routine external renewals: tune its `change` selection or routing to avoid treating normal synchronization as an incident.

Privilege audit consolidates net changes per assignment (user, role and source, plus the internal login key for ExternalGrant, which is not emitted), not effective permission: other assignments or expiry can change access independently. It covers EF-tracked manual and external grant changes only. Actor is `unknown`; direct SQL, database cascades, ambient or provider-managed transactions outside EF and expiry itself are out of scope. An explicit rollback to savepoint conservatively discards the whole buffered audit and may omit valid earlier changes; a failed save discards its candidate. Logs and metrics are best effort, not an outbox. Review the [role policy](../security/role-ownership-and-revocation.md) and reconcile independently when completeness matters.

## Prometheus examples

The [rules](alerts.yml) and [tests](alerts.test.yml) assume an operator-configured OTLP-to-Prometheus translation that replaces dots with underscores and appends `_total` to monotonic counters: for example `security.refresh.replay` becomes `security_refresh_replay_total`, and `cleantemplate.http.errors` becomes `cleantemplate_http_errors_total`. Translation, suffixes and resource-to-label promotion vary by exporter/collector/backend; inspect actual series and adapt rules before use. The examples require `service.name` promoted to `service_name` and `job="cleantemplate"`; both are explicitly filtered and grouped, then summed across instances. Scope further by environment, cluster, namespace and tenant as appropriate: preserve and filter those trusted resource labels and add them to `sum by`, otherwise deployments sharing the two labels are combined. Never use user IDs, tokens, raw URLs or exception messages as labels.

Thresholds are illustrative 10-minute increases, not baselines. Tune for traffic and cleanup interval; privilege change is informational, not automatically malicious. The HTTP rule excludes `/health`, `/health/live` and `/health/ready` from business errors; configure separate probe/readiness alerts. Route `unmatched` may include early failures and needs review. Counter resets, sparse samples, collection intervals, delayed exports and missing series affect accuracy. A zero counter or normal expiry must not trigger replay; a real detected reuse, repeated rejection/contention, cleanup failure, committed assignment change and non-probe 5xx volume are relevant signals. Route alerts to appropriate responders without sensitive annotations.

From the repository root in PowerShell, Docker can run only the pinned official tool image, with no server or listening ports, no container network and a read-only rules mount:

```powershell
docker run --rm --network none --entrypoint promtool --mount "type=bind,source=$((Resolve-Path 'docs/observability').Path),target=/rules,readonly" prom/prometheus:v3.4.2 check rules /rules/alerts.yml
docker run --rm --network none --entrypoint promtool --mount "type=bind,source=$((Resolve-Path 'docs/observability').Path),target=/rules,readonly" prom/prometheus:v3.4.2 test rules /rules/alerts.test.yml
```

The first invocation may pull the image via Docker's host-side registry connection; `--network none` applies to the tool container. On 2026-11-03 these commands returned `SUCCESS: 6 rules found` and `SUCCESS`, respectively, using image digest `sha256:3b1d5be5c3eef4f027665ddaa3b1a7de8a58d96a0a6de5dd45629afd267ecaf0`. Tests use synthetic series; they do not validate a deployed collector, real label mapping, retention, delivery or alert routing.
