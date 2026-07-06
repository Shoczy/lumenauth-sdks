# LumenAuth — Python SDK

Protect a desktop program with LumenAuth licenses. **Zero dependencies** —
`lumenauth.py` is a single standard-library file you can drop straight into
your project.

Bind your app with three values — **name + ownerid + version** — no URL and no
secret in your code.

## Install

Copy `lumenauth.py` next to your code (or `pip install` once published).
Requires Python 3.9+.

## Usage

```python
from lumenauth import LumenAuthClient, get_hwid, LumenAuthError

auth = LumenAuthClient(
    name="My app",
    ownerid="aBcD3fGh1J",   # from Manage Apps in your dashboard
    version="1.0",
    hwid=get_hwid(),        # optional device lock
)

auth.init()  # resolves the app + checks the version

# Register a new user by redeeming a license key
auth.register(email="user@app.com", password="secret123",
              license="LUMEN-XXXXX-XXXXX-XXXXX-XXXXX")

# …or log in later
auth.login(email="user@app.com", password="secret123")
# …or with just a license
auth.license(license="LUMEN-XXXXX-XXXXX-XXXXX-XXXXX")

if not auth.check():
    raise SystemExit("Please log in again")

print(auth.user)               # {"id":..., "email":..., "level":1, ...}
data = auth.file("<file-id>")  # license-gated download -> bytes
```

Every call raises `LumenAuthError` on failure (with `.status`), e.g. `426` for
an outdated version, or `403` for an HWID mismatch / expired subscription /
blacklist / insufficient level.

## HWID

`get_hwid()` returns a stable, SHA-256-hashed machine id (Windows `MachineGuid`,
Linux `/etc/machine-id`, macOS `IOPlatformUUID`). See `example.py` for a full
desktop guard.

> Self-hosting or local dev? Pass an optional `url=` to `LumenAuthClient`.
