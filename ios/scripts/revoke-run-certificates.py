#!/usr/bin/env python3
"""Revokes the Apple Development certificates this CI run created.

Automatic signing on a fresh runner creates a new development certificate for every archive,
and the team may only have a few. After the upload this finds the development certificates in
the runner's keychains and revokes exactly those through the App Store Connect API, matching by
serial number, so no one else's certificate is touched.

Needs ASC_KEY_ID, ASC_ISSUER_ID and ASC_KEY_PATH, and the packages pyjwt and cryptography.
"""
import json
import os
import subprocess
import sys
import time
import urllib.request

import jwt
from cryptography import x509

API = "https://api.appstoreconnect.apple.com/v1"


def token() -> str:
    with open(os.environ["ASC_KEY_PATH"], encoding="utf-8") as key:
        private_key = key.read()
    now = int(time.time())
    return jwt.encode(
        {"iss": os.environ["ASC_ISSUER_ID"], "iat": now, "exp": now + 600, "aud": "appstoreconnect-v1"},
        private_key,
        algorithm="ES256",
        headers={"kid": os.environ["ASC_KEY_ID"], "typ": "JWT"},
    )


def call(method: str, path: str, bearer: str):
    request = urllib.request.Request(f"{API}{path}", method=method, headers={"Authorization": f"Bearer {bearer}"})
    with urllib.request.urlopen(request) as response:
        body = response.read()
        return json.loads(body) if body else None


def local_serials() -> set[str]:
    pem = subprocess.run(
        ["security", "find-certificate", "-a", "-c", "Apple Development", "-p"],
        capture_output=True, text=True, check=False,
    ).stdout
    serials = set()
    for block in pem.split("-----END CERTIFICATE-----"):
        if "BEGIN CERTIFICATE" not in block:
            continue
        cert = x509.load_pem_x509_certificate((block + "-----END CERTIFICATE-----").strip().encode())
        serials.add(format(cert.serial_number, "X").lstrip("0"))
    return serials


def main() -> int:
    serials = local_serials()
    if not serials:
        print("No development certificate on this runner")
        return 0
    bearer = token()
    listed = call("GET", "/certificates?filter[certificateType]=DEVELOPMENT&limit=200", bearer)["data"]
    revoked = 0
    for certificate in listed:
        serial = certificate["attributes"]["serialNumber"].upper().lstrip("0")
        if serial in serials:
            call("DELETE", f"/certificates/{certificate['id']}", bearer)
            revoked += 1
    print(f"Revoked {revoked} development certificate(s) created by this run")
    return 0


if __name__ == "__main__":
    sys.exit(main())
