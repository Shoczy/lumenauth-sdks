"""
lumenauth — the official Python SDK for LumenAuth.

Zero dependencies (standard library only), so you can vendor this single file
straight into a desktop program. Bind your app with three values — name +
ownerid + version — no URL and no secret in your code.

    from lumenauth import LumenAuthClient, get_hwid

    auth = LumenAuthClient(
        name="My app",
        ownerid="aBcD3fGh1J",   # from your LumenAuth dashboard
        version="1.0",
        secret="lumsk_...",     # app secret from Manage Apps
        hwid=get_hwid(),        # optional device lock
    )
    auth.init()                 # resolve the app + check the version
    auth.register(email="user@app.com", password="secret123",
                  license="LUMEN-XXXXX-XXXXX-XXXXX-XXXXX")
    if not auth.check():
        raise SystemExit("Please log in again")
    data = auth.file("<file-id>")
"""

from __future__ import annotations

import hashlib
import json
import platform
import subprocess
import urllib.error
import urllib.request
from typing import Any, Optional

__all__ = ["LumenAuthClient", "LumenAuthError", "get_hwid"]

DEFAULT_URL = "https://api.lumenauth.dev"


class LumenAuthError(Exception):
    """Raised on any failed LumenAuth call. ``status`` is the HTTP status."""

    def __init__(self, message: str, status: Optional[int] = None) -> None:
        super().__init__(message)
        self.status = status


class LumenAuthClient:
    def __init__(
        self,
        name: str,
        ownerid: str,
        version: str,
        secret: str,
        hwid: Optional[str] = None,
        url: Optional[str] = None,
        timeout: float = 15.0,
    ) -> None:
        if not name:
            raise LumenAuthError("name is required")
        if not ownerid:
            raise LumenAuthError("ownerid is required")
        if not version:
            raise LumenAuthError("version is required")
        if not secret:
            raise LumenAuthError("secret is required")
        self.name = name
        self.ownerid = ownerid
        self.version = version
        self.secret = secret
        self.hwid = hwid
        self.base_url = (url or DEFAULT_URL).rstrip("/")
        self.timeout = timeout

        self._app_session: Optional[str] = None
        self.session: Optional[str] = None
        self.token: Optional[str] = None
        self.user: Optional[dict[str, Any]] = None

    # ---- public API -----------------------------------------------------

    def init(self) -> None:
        """Resolve the app and check the version. Call once at startup."""
        res, _ = self._post(
            "/api/1.x/init",
            {
                "name": self.name,
                "ownerid": self.ownerid,
                "version": self.version,
                "secret": self.secret,
            },
            authed=False,
        )
        self._app_session = res["session"]

    def register(
        self,
        email: str,
        password: str,
        license: str,
        hwid: Optional[str] = None,
    ) -> dict[str, Any]:
        """Register a new user by redeeming a license key."""
        return self._auth(
            "/api/1.x/register",
            {
                "email": email,
                "password": password,
                "license": license,
                "hwid": hwid or self.hwid,
            },
        )

    def login(
        self,
        email: str,
        password: str,
        hwid: Optional[str] = None,
    ) -> dict[str, Any]:
        """Log in with email + password (HWID-locked if set)."""
        return self._auth(
            "/api/1.x/login",
            {"email": email, "password": password, "hwid": hwid or self.hwid},
        )

    def license(self, license: str, hwid: Optional[str] = None) -> dict[str, Any]:
        """Log in with just a license key."""
        return self._auth(
            "/api/1.x/license",
            {"license": license, "hwid": hwid or self.hwid},
        )

    def check(self) -> bool:
        """True if the current session is still valid, else False."""
        if not self.session:
            return False
        self._ensure_init()
        try:
            res, _ = self._post("/api/1.x/check", {"session": self.session})
            self.user = res.get("user")
            return True
        except LumenAuthError as err:
            if err.status is not None and err.status < 500:
                self.logout()
                return False
            raise

    def file(self, file_id: str) -> bytes:
        """Download a license-gated file. Returns the raw bytes."""
        if not self.session:
            raise LumenAuthError("Not logged in", 401)
        self._ensure_init()
        body = json.dumps({"session": self.session, "fileId": file_id}).encode()
        req = urllib.request.Request(
            self.base_url + "/api/1.x/file",
            data=body,
            method="POST",
            headers=self._headers(),
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                return resp.read()
        except urllib.error.HTTPError as err:
            raise LumenAuthError(_error_message(err), err.code)
        except urllib.error.URLError as err:
            raise LumenAuthError(f"Could not reach LumenAuth at {self.base_url}: {err.reason}")

    def var(self, name: str) -> str:
        """
        Read a level-gated server variable by name. The value is only returned
        if the logged-in user's level is high enough, so secrets (download URLs,
        config, feature flags) stay off the client.
        """
        if not self.session:
            raise LumenAuthError("Not logged in", 401)
        self._ensure_init()
        res, _ = self._post("/api/1.x/var", {"session": self.session, "name": name})
        return res["value"]

    def logout(self) -> None:
        """Forget the current session locally."""
        self.session = None
        self.token = None
        self.user = None

    # ---- internals ------------------------------------------------------

    def _ensure_init(self) -> None:
        if not self._app_session:
            self.init()

    def _auth(self, path: str, body: dict[str, Any]) -> dict[str, Any]:
        self._ensure_init()
        res, _ = self._post(path, {k: v for k, v in body.items() if v is not None})
        self.session = res["session"]
        self.token = res["token"]
        self.user = res["user"]
        return res

    def _headers(self, authed: bool = True) -> dict[str, str]:
        headers = {"Content-Type": "application/json"}
        if authed and self._app_session:
            headers["Authorization"] = f"Bearer {self._app_session}"
        return headers

    def _post(
        self,
        path: str,
        body: dict[str, Any],
        authed: bool = True,
    ) -> tuple[dict[str, Any], int]:
        data = json.dumps(body).encode()
        req = urllib.request.Request(
            self.base_url + path,
            data=data,
            method="POST",
            headers=self._headers(authed),
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                return json.loads(resp.read().decode() or "{}"), resp.status
        except urllib.error.HTTPError as err:
            raise LumenAuthError(_error_message(err), err.code)
        except urllib.error.URLError as err:
            raise LumenAuthError(f"Could not reach LumenAuth at {self.base_url}: {err.reason}")


def _error_message(err: urllib.error.HTTPError) -> str:
    try:
        payload = json.loads(err.read().decode())
        return payload.get("message") or payload.get("error") or f"Request failed ({err.code})"
    except Exception:
        return f"Request failed ({err.code})"


def get_hwid() -> str:
    """
    A stable, hashed hardware id for this machine. LumenAuth stores and compares
    it; the raw machine id never leaves the device un-hashed.
    """
    return hashlib.sha256(_raw_machine_id().encode()).hexdigest()[:32].upper()


def _raw_machine_id() -> str:
    system = platform.system()
    try:
        if system == "Windows":
            import winreg  # type: ignore

            key = winreg.OpenKey(
                winreg.HKEY_LOCAL_MACHINE,
                r"SOFTWARE\Microsoft\Cryptography",
            )
            value, _ = winreg.QueryValueEx(key, "MachineGuid")
            return str(value)
        if system == "Linux":
            for path in ("/etc/machine-id", "/var/lib/dbus/machine-id"):
                try:
                    with open(path) as handle:
                        text = handle.read().strip()
                        if text:
                            return text
                except OSError:
                    continue
        if system == "Darwin":
            out = subprocess.check_output(
                ["ioreg", "-rd1", "-c", "IOPlatformExpertDevice"]
            ).decode()
            for line in out.splitlines():
                if "IOPlatformUUID" in line:
                    return line.split('"')[-2]
    except Exception:
        pass
    import uuid

    return f"{platform.node()}-{uuid.getnode()}"
