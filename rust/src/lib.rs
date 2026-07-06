//! Official Rust SDK for [LumenAuth] — license auth, HWID locking and
//! license-gated downloads for desktop programs.
//!
//! Bind your app with three values — name + ownerid + version — no URL and no
//! secret in your code.
//!
//! ```no_run
//! use lumenauth::{LumenAuthClient, hwid};
//!
//! let mut auth = LumenAuthClient::new("My app", "aBcD3fGh1J", "1.0", "lumsk_...")
//!     .with_hwid(hwid());
//!
//! auth.init()?; // resolve the app + check the version
//! auth.register("user@app.com", "secret123", "LUMEN-XXXXX-XXXXX-XXXXX-XXXXX")?;
//! if !auth.check()? {
//!     std::process::exit(1);
//! }
//! let bytes = auth.file("<file-id>")?;
//! # Ok::<(), lumenauth::LumenError>(())
//! ```
//!
//! [LumenAuth]: https://lumenauth.dev

use std::fmt;
use std::io::Read;

use serde::Deserialize;
use serde_json::json;
use sha2::{Digest, Sha256};

const DEFAULT_URL: &str = "https://api.lumenauth.dev";

/// An error from any LumenAuth call. `status` is the HTTP status, when known.
#[derive(Debug, Clone)]
pub struct LumenError {
    pub message: String,
    pub status: Option<u16>,
}

impl fmt::Display for LumenError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self.status {
            Some(s) => write!(f, "[{}] {}", s, self.message),
            None => write!(f, "{}", self.message),
        }
    }
}

impl std::error::Error for LumenError {}

impl LumenError {
    fn new(message: impl Into<String>, status: Option<u16>) -> Self {
        LumenError { message: message.into(), status }
    }
}

/// An authenticated end user of your app.
#[derive(Debug, Clone, Deserialize)]
pub struct User {
    pub id: String,
    pub email: String,
    pub level: i64,
    #[serde(rename = "expiresAt")]
    pub expires_at: Option<String>,
    #[serde(rename = "createdAt")]
    pub created_at: Option<String>,
}

#[derive(Deserialize)]
struct AuthResult {
    token: String,
    session: String,
    user: User,
}

#[derive(Deserialize)]
struct InitResult {
    session: String,
}

#[derive(Deserialize)]
struct CheckResult {
    user: User,
}

#[derive(Deserialize)]
struct VarResult {
    value: String,
}

#[derive(Deserialize)]
struct ErrorBody {
    message: Option<String>,
    error: Option<String>,
}

/// A LumenAuth client. Bind it with name + ownerid + version, call [`init`],
/// then authenticate.
///
/// [`init`]: LumenAuthClient::init
pub struct LumenAuthClient {
    base_url: String,
    name: String,
    ownerid: String,
    version: String,
    secret: String,
    hwid: Option<String>,
    app_session: Option<String>,
    pub session: Option<String>,
    pub token: Option<String>,
    pub user: Option<User>,
}

impl LumenAuthClient {
    pub fn new(name: &str, ownerid: &str, version: &str, secret: &str) -> Self {
        LumenAuthClient {
            base_url: DEFAULT_URL.to_string(),
            name: name.to_string(),
            ownerid: ownerid.to_string(),
            version: version.to_string(),
            secret: secret.to_string(),
            hwid: None,
            app_session: None,
            session: None,
            token: None,
            user: None,
        }
    }

    /// Override the API URL (self-hosting / local dev). Normally not needed.
    pub fn with_url(mut self, url: &str) -> Self {
        self.base_url = url.trim_end_matches('/').to_string();
        self
    }

    /// Attach a hardware id sent with every auth call (see [`hwid`]).
    pub fn with_hwid(mut self, hwid: String) -> Self {
        self.hwid = Some(hwid);
        self
    }

    /// Resolve the app and check the version. Call once at startup.
    pub fn init(&mut self) -> Result<(), LumenError> {
        let body = json!({
            "name": self.name, "ownerid": self.ownerid,
            "version": self.version, "secret": self.secret,
        });
        let resp = self.post("/api/1.x/init", body, false)?;
        let result: InitResult = resp
            .into_json()
            .map_err(|e| LumenError::new(e.to_string(), None))?;
        self.app_session = Some(result.session);
        Ok(())
    }

    fn ensure_init(&mut self) -> Result<(), LumenError> {
        if self.app_session.is_none() {
            self.init()?;
        }
        Ok(())
    }

    pub fn register(
        &mut self,
        email: &str,
        password: &str,
        license: &str,
    ) -> Result<(), LumenError> {
        let body = json!({
            "email": email, "password": password,
            "license": license, "hwid": self.hwid,
        });
        self.auth("/api/1.x/register", body)
    }

    pub fn login(&mut self, email: &str, password: &str) -> Result<(), LumenError> {
        let body = json!({ "email": email, "password": password, "hwid": self.hwid });
        self.auth("/api/1.x/login", body)
    }

    pub fn license(&mut self, license: &str) -> Result<(), LumenError> {
        let body = json!({ "license": license, "hwid": self.hwid });
        self.auth("/api/1.x/license", body)
    }

    fn auth(&mut self, path: &str, body: serde_json::Value) -> Result<(), LumenError> {
        self.ensure_init()?;
        let resp = self.post(path, body, true)?;
        let result: AuthResult = resp
            .into_json()
            .map_err(|e| LumenError::new(e.to_string(), None))?;
        self.session = Some(result.session);
        self.token = Some(result.token);
        self.user = Some(result.user);
        Ok(())
    }

    /// Returns `true` if the current session is still valid.
    pub fn check(&mut self) -> Result<bool, LumenError> {
        let session = match &self.session {
            Some(s) => s.clone(),
            None => return Ok(false),
        };
        self.ensure_init()?;
        match self.post("/api/1.x/check", json!({ "session": session }), true) {
            Ok(resp) => {
                let result: CheckResult = resp
                    .into_json()
                    .map_err(|e| LumenError::new(e.to_string(), None))?;
                self.user = Some(result.user);
                Ok(true)
            }
            Err(err) => {
                if matches!(err.status, Some(s) if s < 500) {
                    self.logout();
                    Ok(false)
                } else {
                    Err(err)
                }
            }
        }
    }

    /// Download a license-gated file. Returns the raw bytes.
    pub fn file(&mut self, file_id: &str) -> Result<Vec<u8>, LumenError> {
        let session = self
            .session
            .clone()
            .ok_or_else(|| LumenError::new("Not logged in", Some(401)))?;
        self.ensure_init()?;
        let resp = self.post(
            "/api/1.x/file",
            json!({ "session": session, "fileId": file_id }),
            true,
        )?;
        let mut buf = Vec::new();
        resp.into_reader()
            .read_to_end(&mut buf)
            .map_err(|e| LumenError::new(e.to_string(), None))?;
        Ok(buf)
    }

    /// Read a level-gated server variable by name. The value is only returned
    /// if the logged-in user's level is high enough, so secrets (download URLs,
    /// config, feature flags) stay off the client.
    pub fn var(&mut self, name: &str) -> Result<String, LumenError> {
        let session = self
            .session
            .clone()
            .ok_or_else(|| LumenError::new("Not logged in", Some(401)))?;
        self.ensure_init()?;
        let resp = self.post(
            "/api/1.x/var",
            json!({ "session": session, "name": name }),
            true,
        )?;
        let result: VarResult = resp
            .into_json()
            .map_err(|e| LumenError::new(e.to_string(), None))?;
        Ok(result.value)
    }

    /// Forget the current session locally.
    pub fn logout(&mut self) {
        self.session = None;
        self.token = None;
        self.user = None;
    }

    fn post(
        &self,
        path: &str,
        body: serde_json::Value,
        authed: bool,
    ) -> Result<ureq::Response, LumenError> {
        let url = format!("{}{}", self.base_url, path);
        let mut req = ureq::post(&url);
        if authed {
            if let Some(token) = &self.app_session {
                req = req.set("Authorization", &format!("Bearer {token}"));
            }
        }
        match req.send_json(body) {
            Ok(resp) => Ok(resp),
            Err(ureq::Error::Status(code, resp)) => {
                let msg = resp
                    .into_json::<ErrorBody>()
                    .ok()
                    .and_then(|b| b.message.or(b.error))
                    .unwrap_or_else(|| format!("Request failed ({code})"));
                Err(LumenError::new(msg, Some(code)))
            }
            Err(ureq::Error::Transport(t)) => Err(LumenError::new(
                format!("Could not reach LumenAuth: {t}"),
                None,
            )),
        }
    }
}

/// A stable, SHA-256-hashed hardware id for this machine. The raw id never
/// leaves the device un-hashed.
///
/// - **Windows** — `MachineGuid` from the registry
/// - **Linux** — `/etc/machine-id`
/// - **macOS** — `IOPlatformUUID`
pub fn hwid() -> String {
    let mut hasher = Sha256::new();
    hasher.update(raw_machine_id().as_bytes());
    let digest = hasher.finalize();
    hex::encode(&digest[..16]).to_uppercase()
}

fn raw_machine_id() -> String {
    #[allow(unused_imports)]
    use std::process::Command;

    #[cfg(target_os = "windows")]
    {
        if let Ok(out) = Command::new("reg")
            .args([
                "query",
                r"HKLM\SOFTWARE\Microsoft\Cryptography",
                "/v",
                "MachineGuid",
            ])
            .output()
        {
            let text = String::from_utf8_lossy(&out.stdout);
            for line in text.lines() {
                if line.contains("MachineGuid") {
                    if let Some(value) = line.split_whitespace().last() {
                        return value.to_string();
                    }
                }
            }
        }
    }

    #[cfg(target_os = "linux")]
    {
        for path in ["/etc/machine-id", "/var/lib/dbus/machine-id"] {
            if let Ok(text) = std::fs::read_to_string(path) {
                let trimmed = text.trim();
                if !trimmed.is_empty() {
                    return trimmed.to_string();
                }
            }
        }
    }

    #[cfg(target_os = "macos")]
    {
        if let Ok(out) = Command::new("ioreg")
            .args(["-rd1", "-c", "IOPlatformExpertDevice"])
            .output()
        {
            let text = String::from_utf8_lossy(&out.stdout);
            for line in text.lines() {
                if line.contains("IOPlatformUUID") {
                    let parts: Vec<&str> = line.split('"').collect();
                    if parts.len() >= 2 {
                        return parts[parts.len() - 2].to_string();
                    }
                }
            }
        }
    }

    std::env::var("COMPUTERNAME")
        .or_else(|_| std::env::var("HOSTNAME"))
        .unwrap_or_else(|_| "unknown-host".to_string())
}
