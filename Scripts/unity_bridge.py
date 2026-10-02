"""Use the existing Codely bridge; never starts or reconfigures the editor."""
import json
import socket
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def execute(script, description="工程操作"):
    port = json.loads((ROOT / "Temp/.com-unity-codely.json").read_text(encoding="utf-8"))["unity_port"]
    with socket.create_connection(("127.0.0.1", port), timeout=5) as connection:
        connection.settimeout(60)

        def exact(size):
            data = b""
            while len(data) < size:
                chunk = connection.recv(size - len(data))
                if not chunk:
                    raise RuntimeError("Unity bridge closed")
                data += chunk
            return data

        while exact(1) != b"\n":
            pass
        payload = json.dumps({"type": "execute_csharp_script", "params": {
            "script": script, "description": description, "enable_repl": False,
        }}, ensure_ascii=False).encode()
        connection.sendall(struct.pack(">Q", len(payload)) + payload)
        result = json.loads(exact(struct.unpack(">Q", exact(8))[0]))
        value = result.get("data", {}).get("data", {}).get("result")
        if value is None:
            return result
        try:
            return json.loads(value)
        except (ValueError, TypeError):
            return value


if __name__ == "__main__":
    script = Path(sys.argv[1]).read_text(encoding="utf-8") if len(sys.argv) > 1 else sys.stdin.read()
    print(json.dumps(execute(script), ensure_ascii=False, indent=2))
