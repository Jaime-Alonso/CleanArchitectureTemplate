# External identity trust and internal authorization

Status: T-01/T-02/T-03/T-08 implementation. Explicit account linking is designed below but has no public endpoint yet. Refresh-session hardening (T-06/T-07) remains separate work. See [role ownership and revocation](role-ownership-and-revocation.md) and [transactional provisioning](transactional-external-provisioning.md) for bounds, migrations, validation limits and mandatory legacy-data review.

## Trust boundary

1. The configured JWT validator verifies signature, issuer, audience and lifetime before transformation. OIDC discovery through the configured HTTPS Authority establishes the accepted issuer; a provider display name does not establish trust.
2. The validator sets AuthenticationType to LocalJwt or Oidc. A token claim cannot set that runtime property or materialize the application's internal identity type.
3. If AllowedTenantIds is nonempty, exactly one tenant claim must match an allowed value using ordinal comparison. Missing, duplicate and disallowed tenants fail authentication before provisioning. Configure this list for deployments that need tenant restrictions; an empty list adds no restriction beyond JWT issuer validation.
4. Provisioning requires a unique standard sub, the validated iss, a unique email and an explicitly true verification claim (email_verified by default). Missing, false, invalid or conflicting verification claims are not interpreted as verified.
5. Some providers, including particular Entra configurations, do not emit email_verified. Such tokens can authenticate, but cannot use this JIT provisioning policy until a trustworthy provider-specific verification integration is designed. Do not substitute a constant true or infer verification from a domain, username or email presence.

Example configuration, with issuer/tenant values supplied for the actual deployment:

```json
{
  "Auth": {
    "Schemes": {
      "Oidc": {
        "Enabled": true,
        "Provider": "CorporateIdentity",
        "Authority": "https://identity.example.com",
        "Audience": "corporate-api",
        "AllowedTenantIds": ["approved-tenant"],
        "Claims": {
          "Subject": "sub",
          "Email": "email",
          "EmailVerified": "email_verified",
          "TenantId": "tid"
        }
      }
    },
    "Provisioning": { "Enabled": true, "RoleGrantLifetimeMinutes": 5 }
  }
}
```

This is an illustrative configuration, not a claim that every provider emits tid or supports this exact Authority. Choose the tenant claim for the provider. Subject must remain sub; email and display name are mutable profile attributes, not identifiers.

## Account identity and verified profile

- The external account key is the exact pair (issuer, subject), without case folding, trimming or URL normalization. Identity stores `oidc:` plus SHA-256(issuer) as LoginProvider and SHA-256(subject) as ProviderKey to fit bounded key columns. The display Provider value is only a label.
- New accounts get a deterministic external username derived from an unambiguously serialized issuer/subject pair. An email/profile update does not change this username or login ownership.
- No account is searched for ownership by email. An email lookup only detects a collision and rejects provisioning; even a verified email cannot link a local account or another external login.
- Because this template requires unique emails, JIT creation without a verified email is rejected rather than weakening Identity's uniqueness policy.
- A linked account cannot change tenant through a profile synchronization. A different tenant value, including a change between missing and present, is rejected without updating the user.
- Provisioning decisions are not cached. Current verification and tenant attributes are checked on each request. This is intentionally a correctness-first choice; a future cache must include explicit invalidation and safe failure semantics.
- Expected rejections return Result failures. The HTTP transformation does not expose the rejection reason or account existence: authenticated external users without an internal authorization context receive 403 from internal-role policies. Invalid JWTs or restricted tenants receive 401.

## Internal authorization

- All input identities are replaced by a sanitized validated identity. Reserved internal identifiers, role claims and their case variants are removed before exposing it to application authorization.
- The internal user ID is derived from the validated local GUID sub, not from a supplied internal_user_id marker. Current roles are read from the database on each request; even locally signed JWT role claims are not authorization authority.
- External tokens receive internal roles only from a successful IExternalIdentityProvisioningService result. External roles/groups may feed explicitly configured mapping rules, but do not authorize directly.
- Email/domain mapping rules require EmailVerified even when the mapper is called directly.
- Internal roles use the internal_role claim and an application-created InternalClaimsIdentity runtime type. AdminOnly and UserOrAdmin explicitly inspect this type. A signed external token containing internal_user_id, internal_role, auth_provider, ClaimTypes.Role or an AuthenticationType-like claim cannot create it.
- Repeated transformation is idempotent based on this runtime type, never on a token marker. With provisioning disabled or rejected, external identities retain no internal roles.
- The existing local JWT wire format remains compatible, but signed ClaimTypes.Role values are only an issuance-time snapshot. Manual roles and unexpired, linked external grants are resolved through the same reader for local and external flows. Internal-role claim names are not a new token-issuance contract.

## Explicit account linking: design only

There is currently **no linking endpoint**. A rejected email collision must not be resolved by changing this service back to email-based matching. A future linking feature must implement all of the following:

1. Begin from an authenticated internal account with fresh reauthentication, plus MFA when the account policy requires it. An existing bearer session alone is insufficient proof of recent control.
2. Store a short-lived, single-use linking intent bound to the internal account, the intended issuer, the operation purpose and a cryptographic nonce. Protect the initiating and callback flow against CSRF, replay and session swapping.
3. Obtain fresh authentication from the external provider, using the appropriate authorization-code/PKCE flow and validated state/nonce. Derive issuer, subject and tenant only from validated provider output, never from user-supplied account IDs or raw claims in a linking request.
4. Require explicit confirmation of both account ownerships. A matching email is neither necessary proof nor permission to merge accounts. Deny if the external login belongs to another internal account.
5. Bind AddLogin, intent consumption, concurrency checks and audit persistence to one transaction. The unique external key must make concurrent conflicting links fail safely. Apply the documented session-revocation policy.
6. Test expired/replayed intents, wrong issuer/tenant, missing reauthentication, conflicting links, callback swapping, partial failure and concurrent confirmation before publishing the endpoint.

Until that work exists, operators must use a separately reviewed administrative migration procedure rather than automatic linking.

## Compatibility and rollout

This changes how external logins are keyed and therefore requires an explicit rollout plan for existing databases. T-03 additionally introduces the ExternalRoleGrants table and a role-ownership compatibility change; reviewed migrations and a legacy-role audit are required before deployment.

- Legacy logins stored as Provider + Subject are not automatically migrated or trusted. If their email collides, provisioning is rejected without altering the account or its roles.
- Do not infer the original issuer from a display alias or a current token. Establish the canonical original issuer/subject through reliable administrative evidence, review collisions and tenants, and back up before a controlled data migration to the hashed keys.
- Review registered provider claims before enabling provisioning. Previously accepted users without verified email may now be denied internal permissions.
- Change external enablement/tenant configuration through controlled deployment and restart the application; do not rely on partial live reload of authentication options.
- Synchronization now removes obsolete grants belonging to the current login without altering manual assignments. Legacy mixed-origin AspNetUserRoles rows cannot be classified automatically and require review. Still-valid provider JWTs can reassert old roles until their original expiration; the configured grant TTL is not a promise of instantaneous IdP revocation. See the linked role policy for exact bounds.
- Provisioning now uses isolated serializable transactions and bounded whole-unit retries, with a unique normalized-email index. Apply the additive index migration and review duplicate normalized emails first. SQLite concurrency tests and provider SQL generation do not replace validation on real SQL Server/PostgreSQL. Do not declare external identity production-ready on the strength of these increments alone.

## Verification

- Relational SQLite/Identity tests exercise email collisions, verification, exact issuer/subject separation, stable profile updates, tenant changes and refusal to silently migrate legacy logins.
- HTTP tests validate signed JWTs with static test metadata; they exercise spoofed internal claims, sanitized roles, tenant restrictions, duplicate claims, disabled provisioning and repeated transformation. They do not contact a real provider.
- A full-host test verifies that a User token can read products but cannot create, update or delete them. Existing Admin CRUD tests remain part of the regression suite.
- Role tests additionally verify TTL, local-token permission re-evaluation, manual/source ownership and atomic grant replacement. Real-provider and production-engine testing, migration rehearsals, legacy-role review and production-provider concurrency validation remain required before production.
