# End-to-end tests

Playwright + TypeScript against a running frontend and real API/database. Tests call `POST /api/auth/login` with username/password to obtain an internal JWT. No Google/Microsoft login, signing key, database security stamp, authentication bypass, or mocked API is needed by the test runner.

## Setup

1. Start the API and frontend against a dedicated local/dev/test database. The frontend API URL and API CORS settings must match the test URLs. Do not target production.
2. Preconfigure two enabled Identity accounts with password authentication enabled, passwords and without MFA: one with only `User`, one with `Global Admin`. The password-management test temporarily disables the dedicated regular user’s password authentication, then restores it and resets its password to the configured test password. Roles are unchanged. See password setup below.
3. Copy `.env.example` to `.env` in this folder. Configure `E2E_USER_USERNAME`, `E2E_USER_PASSWORD`, `E2E_ADMIN_USERNAME`, and `E2E_ADMIN_PASSWORD`. Environment variables override `.env`. Never commit credentials.

## Manual GitHub Actions run

Use **Actions → E2E tests → Run workflow** and select `dev` or `test` and the branch containing the tests. The workflow must exist on the default branch to appear in Actions. It tests the already deployed applications; it does not deploy code or run database migrations.

Configure each GitHub environment with the same `FRONTEND_URL` and `API_URL` variables as the release workflow (HTTPS origins without trailing slashes), plus these secrets:

- `E2E_USER_USERNAME`
- `E2E_USER_PASSWORD`
- `E2E_ADMIN_USERNAME`
- `E2E_ADMIN_PASSWORD`

Enter passwords literally in GitHub Secrets, without the surrounding quotes used in `.env` files. Use dedicated, distinct test accounts with the roles and password settings described above. No Azure login credentials or JWT signing key are needed.

The workflow installs Chromium, checks both applications, and runs tests sequentially with no retries, stopping on the first failure to limit repeated failed logins. Results appear in workflow logs; authenticated artifacts are not uploaded. It shares the release workflow's per-environment concurrency group to prevent overlapping deployments/tests. GitHub can replace an older pending run when another run queues in that group. Restrict allowed branches and configure environment approvals because tests receive account credentials and modify test data. Do not run local tests concurrently against these same accounts.

## Password setup

As Global Admin, create the user first, then open **Users → Password authentication**, select **Enable password authentication**, enter an initial/new password and save. An initial password is required if none exists; leave the field blank to retain an existing password. Passwords require at least 12 characters including uppercase, lowercase, a digit and a symbol.

This administrator approval permits password login without Google/Microsoft or email verification; it does not mark the email as verified. New and existing users default to password authentication disabled after migration. No password is emailed or returned by the API.

Changing a password or the flag invalidates existing JWTs. An enabled password user can also change their own password through `PUT /api/auth/me/password` with `currentPassword` and `newPassword`. Only Global Admin can change the flag or reset another user's password through `PUT /api/users/{userId}/password-authentication`. Create/Edit user does not accept or modify password settings. There is no anonymous registration/reset endpoint.

From this folder:

```powershell
npm ci
npx playwright install chromium
npm run typecheck
npm test
# Optional visible browser:
npm run test:headed
```

The fixture obtains a token from `/api/auth/login`, verifies `/api/auth/me`, and places an API-issued token in the frontend's session storage once before reloading. Token lifetime comes from the API configuration. No token files are written. Traces, screenshots and video are off to avoid saving credentials or user data; do not publish debug logs/authenticated artifacts. The JWT signing key stays exclusively on the API.

Coverage: tenant create/update/enable/disable/delete through UI; queue-route configuration; SharePoint validation echo; Global Admin cross-tenant read-only visibility; regular-user isolation; tampered/anonymous API access; invalid login; logout persistence. The queue test never sends a message to Azure Service Bus.

Login returns a generic 401 for invalid, disabled, password-authentication-disabled, locked or MFA-enabled accounts. Identity locks password login for 15 minutes after 5 failures. Password endpoints share a per-process limit of 60 requests/minute (429 when exceeded); production ingress should provide additional distributed abuse protection. Use HTTPS outside localhost. Do not repeatedly retry bad test credentials, as this can lock test accounts.

Tests use unique tenant names and delete only the tenant IDs they created, including cascading test topics, in fixture teardown. Abrupt process termination can leave `e2e-` test data behind; review it manually before removal. The existing application is not started or reconfigured by the suite. Never use real personal/admin accounts as test accounts.
