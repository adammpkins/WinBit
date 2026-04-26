"""
Send a UDP KRPC ping to a BitTorrent DHT node and verify the response.

Requirements:
    pip install bencode.py

Usage:
    python dht_ping.py [host] [port]

Defaults to router.bittorrent.com:6881.
"""
import os
import socket
import sys
import bencode


def krpc_ping(host: str = "router.bittorrent.com", port: int = 6881, timeout: float = 5.0) -> None:
    # Random 20-byte node ID for our "client"
    node_id = os.urandom(20)

    # Random 2-byte transaction ID
    txn_id = os.urandom(2)

    # Build the KRPC ping query per BEP 0005
    query = {
        b"t": txn_id,
        b"y": b"q",
        b"q": b"ping",
        b"a": {b"id": node_id},
    }
    payload = bencode.bencode(query)

    # Resolve + send
    addr = (socket.gethostbyname(host), port)
    print(f"Sending KRPC ping to {host} ({addr[0]}):{port}")
    print(f"  txn_id = {txn_id.hex()}")
    print(f"  our id = {node_id.hex()}")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.settimeout(timeout)

    try:
        sock.sendto(payload, addr)
        data, src = sock.recvfrom(4096)
    except socket.timeout:
        print(f"ERROR: no response within {timeout}s (DHT service may be down or blocked)")
        sys.exit(1)
    finally:
        sock.close()

    # Decode response
    try:
        resp = bencode.bdecode(data)
    except Exception as e:
        print(f"ERROR: failed to bdecode response from {src}: {e}")
        print(f"  raw: {data!r}")
        sys.exit(1)

    print(f"Got {len(data)} bytes from {src[0]}:{src[1]}")

    # Validate: response type ('r'), matching transaction id, and a 20-byte node id
    r_txn = resp.get(b"t")
    r_type = resp.get(b"y")

    if r_type == b"r" and r_txn == txn_id and b"id" in resp.get(b"r", {}):
        remote_id = resp[b"r"][b"id"]
        print("SUCCESS: DHT service is up")
        print(f"  remote node id = {remote_id.hex()}")
    elif r_type == b"e":
        print(f"ERROR response: {resp.get(b'e')!r}")
        sys.exit(1)
    else:
        print(f"Unexpected response: {resp!r}")
        sys.exit(1)


if __name__ == "__main__":
    host = sys.argv[1] if len(sys.argv) > 1 else "router.bittorrent.com"
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 6881
    krpc_ping(host, port)
