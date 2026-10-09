"""Exercise public packaging guards without using a real API or credential."""
import json
import base64
import ctypes
import os
import subprocess
import sys
import zipfile
from pathlib import Path

development = Path(__file__).resolve().parents[1]
root = development / "tests/output/public-package"
payload = root / "payload"
payload.mkdir(parents=True, exist_ok=True)
private = root / "private.json"
secret = "fixture-ciphertext-only-value"
private.write_text(json.dumps({"EncryptedAppSecret": secret}), encoding="utf-8")
snapshot = root / "source.zip"
guard = development / "scripts/verify-public-package.py"
report = []


def source(name="LICENSE", content="MIT fixture"):
    with zipfile.ZipFile(snapshot, "w") as archive:
        archive.writestr(name, content)


def check(success, description):
    process = subprocess.run([sys.executable, str(guard), str(payload), str(snapshot), str(private)],
                             capture_output=True, text=True)
    if (process.returncode == 0) != success:
        raise RuntimeError(description + ": unexpected guard result")
    if secret in process.stdout or secret in process.stderr:
        raise RuntimeError("Credential appeared in diagnostic output")
    report.append("PASS: " + description)


source()
check(True, "clean public package accepted")
file = payload / "app.bin"
for encoding in ("utf-8", "utf-16-le"):
    file.write_bytes(secret.encode(encoding))
    check(False, "encrypted credential rejected in " + encoding)
file.unlink()
config = payload / "config"
config.mkdir(exist_ok=True)
file = config / "dandanplay.example.json"
file.write_text("{}")
check(False, "configuration/template rejected")
file.unlink()
source("development/src/Main.cs", secret)
check(False, "source credential rejected")
source("STRUCTURE.md", "fixture explanation")
check(False, "source explanation rejected")
source()
licenses = payload / "licenses"
licenses.mkdir(exist_ok=True)
file = licenses / "THIRD-PARTY.md"
file.write_text("fixture explanation")
check(False, "non-license documentation rejected")
file.unlink()
check(True, "clean package accepted again")
if os.name == "nt":
    class Blob(ctypes.Structure):
        _fields_ = [("size", ctypes.c_uint32), ("data", ctypes.POINTER(ctypes.c_ubyte))]
    data = secret.encode("utf-8")
    buffer = (ctypes.c_ubyte * len(data)).from_buffer_copy(data)
    incoming, outgoing = Blob(len(data), buffer), Blob()
    crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    crypt32.CryptProtectData.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p, ctypes.c_void_p,
                                       ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(Blob)]
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p
    if not crypt32.CryptProtectData(ctypes.byref(incoming), None, None, None, None, 1, ctypes.byref(outgoing)):
        raise RuntimeError("Fixture DPAPI encryption failed")
    try:
        encrypted = base64.b64encode(ctypes.string_at(outgoing.data, outgoing.size)).decode("ascii")
    finally:
        kernel32.LocalFree(ctypes.cast(outgoing.data, ctypes.c_void_p))
    private.write_text(json.dumps({"EncryptedAppSecret": encrypted}), encoding="utf-8")
    file = payload / "app.bin"
    file.write_bytes(secret.encode("utf-8"))
    check(False, "plaintext recovered from private DPAPI config is rejected")
    file.unlink()
(development / "bin/reports/public-package-1.0.2.txt").write_text("\n".join(report) + "\n", encoding="utf-8")
print("\n".join(report))
