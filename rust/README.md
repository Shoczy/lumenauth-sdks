# LumenAuth — Rust SDK

Protect a desktop program with LumenAuth licenses. Lightweight (blocking
`ureq`, pure-Rust TLS), with a cross-platform HWID helper.

Bind your app with three values — **name + ownerid + version** — no URL and no
secret in your code.

## Add it

```toml
[dependencies]
lumenauth = { path = "path/to/sdks/rust" }   # or from crates.io once published
```

## Usage

```rust
use lumenauth::{LumenAuthClient, hwid};

fn main() -> Result<(), lumenauth::LumenError> {
    let mut auth = LumenAuthClient::new("My app", "aBcD3fGh1J", "1.0")
        .with_hwid(hwid()); // optional device lock

    auth.init()?; // resolves the app + checks the version

    // Register a new user by redeeming a license key
    auth.register("user@app.com", "secret123", "LUMEN-XXXXX-XXXXX-XXXXX-XXXXX")?;
    // …or auth.login("user@app.com", "secret123")?;
    // …or auth.license("LUMEN-XXXXX-XXXXX-XXXXX-XXXXX")?;

    if !auth.check()? {
        std::process::exit(1);
    }

    let user = auth.user.as_ref().unwrap();
    println!("level {}, expires {:?}", user.level, user.expires_at);

    let bytes = auth.file("<file-id>")?; // license-gated download
    println!("{} bytes", bytes.len());
    Ok(())
}
```

Every call returns `Result<_, LumenError>`; `LumenError.status` carries the HTTP
status (`426` outdated version, `403` HWID mismatch / expired / blacklist /
level).

## HWID

`hwid()` returns a stable, SHA-256-hashed machine id (Windows `MachineGuid`,
Linux `/etc/machine-id`, macOS `IOPlatformUUID`). See `examples/guard.rs`
(`cargo run --example guard`).

> Self-hosting or local dev? Chain `.with_url("http://localhost:8787")`.
