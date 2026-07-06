# LumenAuth SDKs

Client libraries for [LumenAuth](https://lumenauth.dev) - license keys, HWID
locking, user auth and license-gated downloads for desktop apps. Use one of the
SDKs below, or just call the HTTP API directly.

You bind an app with four values from the dashboard (Manage Apps): `name`,
`ownerid`, `version` and `secret`. Call `init()` once, then register / login /
license and check the session. The secret is only sent at init.

## SDKs

| Language | Folder | |
| --- | --- | --- |
| JavaScript / TypeScript | [javascript/](javascript) | `@lumenauth/client` |
| Python | [python/](python) | single file, no dependencies |
| Rust | [rust/](rust) | blocking, pure-Rust TLS |
| C# / .NET | [csharp/](csharp) | netstandard2.0 |

Each folder has its own README and a runnable example.

## Example

```ts
import { createClient } from "@lumenauth/client";

const auth = createClient({
  name: "My app",
  ownerid: "aBcD3fGh1J",
  version: "1.0",
  secret: "lumsk_...",
});

await auth.init();
await auth.license("LUMEN-XXXXX-XXXXX-...");   // or register / login
if (!(await auth.check())) process.exit(1);
```

Python, Rust and C# work the same way - see their folders.

## API

No SDK for your language? The API is plain HTTP + JSON:

```
POST /api/1.x/init       name + ownerid + version + secret -> session token
POST /api/1.x/register   redeem a license, create a user
POST /api/1.x/login      email + password (HWID-locked)
POST /api/1.x/license    log in with a license key
POST /api/1.x/check      is the session still valid?
POST /api/1.x/var        read a level-gated variable
POST /api/1.x/file       download a license-gated file
```

Every call after `init` sends the session token as `Authorization: Bearer`.

## License

MIT - see [LICENSE](LICENSE).
