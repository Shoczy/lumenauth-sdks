<div align="center">

<img src="assets/logo.png" alt="LumenAuth" width="120" />

# LumenAuth — Official SDKs

**License & authentication for desktop software — from any language.**

License keys · HWID locking · user auth · gated file downloads · reseller tokens.

<br/>

![Python](https://img.shields.io/badge/Python-3776AB?style=flat&logo=python&logoColor=white)
![Rust](https://img.shields.io/badge/Rust-000000?style=flat&logo=rust&logoColor=white)
![JavaScript](https://img.shields.io/badge/JS%2FTS-F7DF1E?style=flat&logo=javascript&logoColor=black)
![C#](https://img.shields.io/badge/C%23-512BD4?style=flat&logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/License-MIT-6c6c78?style=flat)

</div>

---

Thin, official client SDKs for [LumenAuth](https://lumenauth.dev). Each one wraps
the same HTTP API, ships a cross-platform **HWID** helper, and includes a
runnable desktop-guard example.

Bind your app with four values from the dashboard (Manage Apps → Application
credentials): **name + ownerid + version + secret**. `init()` verifies them and
returns a short-lived session the SDK uses for every later call — the secret is
sent only once, at start.

## SDKs

| Language | Folder | Notes |
| --- | --- | --- |
| JavaScript / TypeScript | [`javascript/`](javascript) | `@lumenauth/client` |
| Python | [`python/`](python) | single-file, zero dependencies |
| Rust | [`rust/`](rust) | blocking `ureq`, pure-Rust TLS |
| C# / .NET | [`csharp/`](csharp) | `netstandard2.0`, async, `HttpClient` |

## Quick start

### JavaScript / TypeScript

```ts
import { createClient } from "@lumenauth/client";

const auth = createClient({
  name: "My app",
  ownerid: "aBcD3fGh1J",
  version: "1.0",
  secret: "lumsk_…",
});

await auth.init();
await auth.license("LUMEN-XXXXX-XXXXX-…");   // or register / login
if (!(await auth.check())) process.exit(1);
```

### Python

```python
from lumenauth import LumenAuthClient, get_hwid

auth = LumenAuthClient(
    name="My app", ownerid="aBcD3fGh1J",
    version="1.0", secret="lumsk_…", hwid=get_hwid(),
)
auth.init()
auth.login(email="user@app.com", password="secret")
if not auth.check():
    raise SystemExit("Please log in again")
```

### Rust

```rust
use lumenauth::{hwid, LumenAuthClient};

let mut auth = LumenAuthClient::new("My app", "aBcD3fGh1J", "1.0", "lumsk_…")
    .with_hwid(hwid());
auth.init()?;
auth.license("LUMEN-XXXXX-XXXXX-…")?;
```

### C#

```csharp
using LumenAuth;

using var auth = new LumenAuthClient("My app", "aBcD3fGh1J", "1.0", "lumsk_…", Hwid.Get());
await auth.InitAsync();
await auth.LicenseAsync("LUMEN-XXXXX-XXXXX-…");
if (!await auth.CheckAsync()) Environment.Exit(1);
```

## The API (any language)

No SDK for your language? Call the HTTP API directly — it's plain JSON.

| Endpoint | Purpose |
| --- | --- |
| `POST /api/1.x/init` | resolve app + check version → session token |
| `POST /api/1.x/register` | redeem a license → create a user |
| `POST /api/1.x/login` | email + password (HWID-locked) |
| `POST /api/1.x/license` | log in with just a license key |
| `POST /api/1.x/check` | is this session still valid? |
| `POST /api/1.x/var` | read a level-gated server variable |
| `POST /api/1.x/file` | download a license-gated file |

All calls after `init` send the session token as `Authorization: Bearer <token>`.

## License

MIT — see [LICENSE](LICENSE). Use the SDKs freely in your own projects.
