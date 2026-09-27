import os
import sys
import time
import urllib.request

url = "https://huggingface.co/bartowski/gemma-2-2b-it-GGUF/resolve/main/gemma-2-2b-it-Q4_K_M.gguf"
dest = os.path.abspath("models/gemma-2-2b-it-q4_k_m.gguf")
os.makedirs("models", exist_ok=True)

print(f"Downloading {url} to {dest}...")

headers = {"User-Agent": "Mozilla/5.0"}
req = urllib.request.Request(url, headers=headers)

with urllib.request.urlopen(req) as resp, open(dest + ".tmp", "wb") as f:
    total_size = int(resp.headers.get("content-length", 0))
    downloaded = 0
    start_time = time.time()
    last_print = 0
    chunk_size = 1024 * 1024 * 2 # 2MB

    while True:
        chunk = resp.read(chunk_size)
        if not chunk:
            break
        f.write(chunk)
        downloaded += len(chunk)
        now = time.time()
        if now - last_print >= 2.0 or downloaded == total_size:
            percent = (downloaded / total_size * 100) if total_size > 0 else 0
            mb = downloaded / (1024 * 1024)
            total_mb = total_size / (1024 * 1024)
            elapsed = now - start_time
            speed = mb / elapsed if elapsed > 0 else 0
            print(f"Progress: {mb:.1f}/{total_mb:.1f} MB ({percent:.1f}%) - Speed: {speed:.1f} MB/s", flush=True)
            last_print = now

if os.path.exists(dest):
    os.remove(dest)
os.rename(dest + ".tmp", dest)
print(f"Download completed successfully: {dest}")
