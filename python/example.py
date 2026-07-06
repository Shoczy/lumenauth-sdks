"""
Minimal desktop guard: block the program unless the user has a valid license.

Run:  python example.py
Set your app's name + ownerid + version below (or via env vars).
"""

import os
import sys

from lumenauth import LumenAuthClient, LumenAuthError, get_hwid

NAME = os.environ.get("LUMENAUTH_NAME", "My app")
OWNERID = os.environ.get("LUMENAUTH_OWNERID", "your-ownerid")
VERSION = os.environ.get("LUMENAUTH_VERSION", "1.0")
SECRET = os.environ.get("LUMENAUTH_SECRET", "your-app-secret")
URL = os.environ.get("LUMENAUTH_URL")  # optional: self-hosting / local dev


def main() -> None:
    auth = LumenAuthClient(
        name=NAME,
        ownerid=OWNERID,
        version=VERSION,
        secret=SECRET,
        hwid=get_hwid(),
        url=URL,
    )

    try:
        auth.init()  # resolves the app + checks the version
    except LumenAuthError as err:
        print(f"init failed [{err.status}]: {err}")
        sys.exit(1)

    email = input("Email: ").strip()
    password = input("Password: ").strip()
    license_key = input("License (leave blank if already registered): ").strip()

    try:
        if license_key:
            auth.register(email=email, password=password, license=license_key)
            print("Registered + logged in.")
        else:
            auth.login(email=email, password=password)
            print("Logged in.")
    except LumenAuthError as err:
        print(f"Auth failed [{err.status}]: {err}")
        sys.exit(1)

    if not auth.check():
        print("Session invalid — exiting.")
        sys.exit(1)

    user = auth.user or {}
    print(f"Welcome! level={user.get('level')} expires={user.get('expiresAt')}")
    print(">>> your protected program runs here <<<")


if __name__ == "__main__":
    main()
