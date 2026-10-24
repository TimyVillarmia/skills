# Security

Use this reference whenever code handles authentication, authorization, untrusted input, secrets, sensitive data, network boundaries, serialization, or security-sensitive infrastructure.

## Security principles

- Treat external input as untrusted.
- Prefer established .NET/framework/library security primitives over custom cryptography or protocol implementations.
- Fail closed for authentication and authorization decisions.
- Minimize privileges, data exposure, and trust boundaries.
- Make security behavior explicit and testable.
- Do not weaken security merely to simplify development or tests.

## Authentication

Choose authentication from the actual client and deployment model:

- OAuth 2.0 / OpenID Connect for delegated or federated identity.
- JWT bearer authentication when stateless bearer tokens are actually required.
- Cookies for browser-oriented session authentication when appropriate.
- API keys only when their operational/security properties are acceptable.

Validate tokens using established framework components. Never manually implement JWT parsing/signature validation, key rotation, OAuth/OIDC protocol behavior, password hashing, or cryptography.

Authentication answers **who is the caller**. Authorization answers **what the caller may do**.

## Authorization

Authorize as close as practical to the protected resource and operation. Use policies, claims, roles, or resource-based authorization according to the access model.

For multi-tenant systems, enforce tenant boundaries server-side on every relevant access path. A client-supplied tenant identifier is not proof of access.

For APIs, distinguish `401 Unauthorized` (authentication required/invalid) from `403 Forbidden` (authenticated but not permitted).

## ASP.NET Core security pipeline

Keep security middleware/order deliberate. Authentication must establish the user before authorization evaluates policies. Endpoint-specific authorization must not be bypassed by placing business logic behind a UI-only check.

When using rate limiting, anti-forgery protection, CORS, HTTPS redirection, or other middleware, inspect the target ASP.NET Core version and existing pipeline rather than copying an unrelated middleware order.

CORS is not an authentication mechanism.

## Input and output

Validate shape and basic constraints at the boundary; enforce business invariants in the appropriate application/domain layer.

Avoid mass assignment and over-posting. Explicit request models should determine which fields a client may change.

Do not expose internal entities, stack traces, database details, secrets, tokens, or sensitive fields through API responses.

## Injection

Use parameterized APIs and framework abstractions for database access. Never concatenate untrusted values into SQL, shell commands, LDAP queries, or other interpreters.

Validate or constrain dynamic identifiers such as column names, file paths, hostnames, and URLs rather than treating them as ordinary values.

## SSRF and outbound requests

Treat user-controlled URLs and network destinations as security-sensitive. Validate allowed schemes and constrain hosts/domains where appropriate. Prevent access to internal/private network ranges when the threat model requires it. Limit redirects, timeouts, and response sizes as appropriate.

Do not assume a URL is safe merely because it uses HTTPS.

## Deserialization and files

Use safe, typed serialization formats and trusted framework configuration. Do not enable unsafe polymorphic deserialization for attacker-controlled type metadata without a clear security model.

Treat uploaded documents, images, archives, and other files as untrusted input. Apply size limits, content/extension validation, isolated storage, and safe generated names.

Never combine untrusted path fragments with filesystem paths without containment validation.

## Browser security

For browser applications, consider CSRF, XSS, CORS, cookie flags, content security policy, and secure transport as part of the threat model.

For cookie authentication, configure `Secure`, `HttpOnly`, and an appropriate `SameSite` policy for the deployment model.

## Secrets and telemetry

Do not commit credentials, private keys, access tokens, or connection secrets. Use the environment's supported secret-management mechanism.

Review logs, traces, exceptions, metrics, and telemetry for accidental sensitive-data disclosure.

## Rate limiting and abuse

Rate limiting is useful for endpoints vulnerable to high request volume, authentication abuse, expensive computation, enumeration, or downstream fan-out. Combine it with authentication, authorization, validation, quotas, and resource limits as appropriate.

For .NET 10, evaluate the built-in ASP.NET Core rate-limiting facilities before adding another dependency.

## Dependencies

Keep dependencies minimal and maintained, but do not reject a mature security library merely because it adds a package.

For security-sensitive primitives, established implementations are preferred over custom code even when the custom version would have fewer dependencies.

Review direct and transitive dependency vulnerabilities as normal maintenance.

## Security anti-patterns

❌ Hand-rolled password hashing, JWT validation, cryptography, or OAuth/OIDC protocol code.

❌ Authorization based only on UI visibility or client-supplied roles/tenant IDs.

❌ Disabling certificate validation to “fix” local development and carrying that change into production.

❌ Logging access tokens, passwords, secrets, or sensitive personal data.

❌ Accepting arbitrary URLs without an SSRF threat model.

❌ Returning raw exception details to clients.

❌ Treating CORS as authentication.

## Threat modeling

For meaningful security-sensitive features, identify assets, trust boundaries, actors/attacker capabilities, entry points, authorization decisions, abuse cases, mitigations, and residual risk.

## Security checklist

- Authentication is appropriate and correctly validated.
- Authorization protects the actual resource/action.
- Tenant boundaries are enforced when applicable.
- Inputs are validated and constrained.
- Injection paths are safely handled.
- Outbound requests cannot create an unintended SSRF boundary.
- Deserialization and file handling are safe.
- Secrets and sensitive data are not exposed in source, responses, or telemetry.
- Transport security is preserved.
- Abuse/rate limits are appropriate.
- Security-sensitive primitives use established implementations.
- Failure behavior does not fail open.
