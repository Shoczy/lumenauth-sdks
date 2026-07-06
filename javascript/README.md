# @lumenauth/client

The official JavaScript/TypeScript SDK for [LumenAuth](../../PLAN.md) — add
license auth, HWID locking and gated downloads to your app in a few lines.

Bind your app with three values — **name + ownerid + version** — no URL and no
secret in your code.

## Install

```bash
npm install @lumenauth/client
```

## Usage

```ts
import { createClient } from "@lumenauth/client";

const auth = createClient({
  name: "My app",
  ownerid: "aBcD3fGh1J",   // from Manage Apps in your dashboard
  version: "1.0",
  hwid: getMachineId(),    // optional: device lock
  storage: typeof window !== "undefined" ? window.localStorage : undefined,
});

await auth.init(); // resolves the app + checks the version

// Register a new user by redeeming a license key
await auth.register({
  email: "user@app.com",
  password: "secret123",
  license: "LUMEN-XXXXX-XXXXX-XXXXX-XXXXX",
});

// …or log in later
await auth.login({ email: "user@app.com", password: "secret123" });
// …or with just a license
await auth.license({ license: "LUMEN-XXXXX-XXXXX-XXXXX-XXXXX" });

if (!(await auth.check())) throw new Error("Please log in again");

console.log(auth.user);            // { id, email, level, expiresAt, … }
const bytes = await auth.file("<file-id>"); // license-gated download

auth.logout();
```

`init()` is also called automatically before your first `register`/`login` if
you skip it. On a version mismatch it throws a `LumenError` with status `426`
("update available").

## Errors

Every call throws a `LumenError` with a `message` and HTTP `status` (e.g. `403`
for an HWID mismatch, expired subscription, blacklist, or insufficient level).

> Self-hosting or local dev? Pass an optional `url` to `createClient`. Normal
> users never set it.
