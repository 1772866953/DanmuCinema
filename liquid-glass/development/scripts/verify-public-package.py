"""Fail a public build if local credentials, API config, or user data leaked."""
import json
import sys
import zipfile
import base64
import ctypes
import os
from pathlib import Path

stage, snapshot = map(Path, sys.argv[1:3])
private_values = set()


def unprotect(value):
    if os.name != "nt":
        return None
    class Blob(ctypes.Structure):
        _fields_ = [("size", ctypes.c_uint32), ("data", ctypes.POINTER(ctypes.c_ubyte))]
    try:
        data = base64.b64decode(value, validate=True)
        buffer = (ctypes.c_ubyte * len(data)).from_buffer_copy(data)
        incoming = Blob(len(data), buffer)
        outgoing = Blob()
        crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        crypt32.CryptUnprotectData.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p, ctypes.c_void_p,
                                              ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(Blob)]
        kernel32.LocalFree.argtypes = [ctypes.c_void_p]
        kernel32.LocalFree.restype = ctypes.c_void_p
        if not crypt32.CryptUnprotectData(ctypes.byref(incoming), None, None, None, None, 1, ctypes.byref(outgoing)):
            return None
        try:
            return ctypes.string_at(outgoing.data, outgoing.size).decode("utf-8")
        finally:
            kernel32.LocalFree(ctypes.cast(outgoing.data, ctypes.c_void_p))
    except (ValueError, UnicodeError):
        return None


for name in sys.argv[3:]:
    file = Path(name)
    if file.exists():
        config = json.loads(file.read_text(encoding="utf-8-sig"))
        for key in ("AppId", "AppSecret", "EncryptedAppSecret", "DandanAppId",
                    "EncryptedDandanSecret", "EncryptedToken", "EncryptedAdditionalApis"):
            value = config.get(key)
            if isinstance(value, str) and len(value) >= 8:
                private_values.add(value)
                if key.startswith("Encrypted"):
                    plaintext = unprotect(value)
                    if plaintext:
                        private_values.add(plaintext)
needles = [value.encode(encoding) for value in private_values for encoding in ("utf-8", "utf-16-le")]


def inspect(name, data):
    if any(needle in data for needle in needles):
        # Never include the matched value in diagnostics.
        raise RuntimeError(f"Personal credentials detected in {name}")


for file in stage.rglob("*"):
    if not file.is_file():
        continue
    relative = file.relative_to(stage)
    if relative.parts[0].lower() in ("config", "data"):
        raise RuntimeError("Private configuration directory in public payload")
    if relative.parts[0] == "licenses" and not file.name.upper().startswith("LICENSE"):
        raise RuntimeError("Non-license file in license directory")
    inspect(str(relative), file.read_bytes())

with zipfile.ZipFile(snapshot) as archive:
    for entry in archive.infolist():
        name = Path(entry.filename)
        if "config" in name.parts or "data" in name.parts:
            raise RuntimeError("Private configuration directory in source snapshot")
        if name.suffix.lower() in (".md", ".txt") and not name.name.upper().startswith("LICENSE"):
            raise RuntimeError("Explanatory document in source snapshot")
        inspect(entry.filename, archive.read(entry))
print("PASS: no personal credentials or configuration in public payload/source snapshot; LICENSE files only.")
