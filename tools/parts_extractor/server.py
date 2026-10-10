#!/usr/bin/env python3
"""立ち絵パーツ自動分割ツールのローカル WebUI サーバー。
ブラウザから画像をドラッグ＆ドロップして、分割パーツをプレビュー・ZIPダウンロードできます。

起動:
    python tools/parts_extractor/server.py [--port 8080]
"""
from __future__ import annotations

import argparse
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import webbrowser
from http import HTTPStatus
from http.server import HTTPServer, SimpleHTTPRequestHandler
from pathlib import Path
import zipfile

REPO = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO / "tools" / "extract_portrait_parts.py"
WEB_DIR = Path(__file__).resolve().parent


class PartsExtractorHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(WEB_DIR), **kwargs)

    def do_POST(self):
        if self.path == "/api/process":
            self.handle_process()
        else:
            self.send_error(HTTPStatus.NOT_FOUND, "Not Found")

    def handle_process(self):
        content_type = self.headers.get("Content-Type", "")
        if not content_type.startswith("multipart/form-data"):
            self.send_error(HTTPStatus.BAD_REQUEST, "Expected multipart/form-data")
            return

        # boundary parsing
        boundary = content_type.split("boundary=")[-1].strip()
        if not boundary:
            self.send_error(HTTPStatus.BAD_REQUEST, "Missing boundary")
            return
        boundary_bytes = boundary.encode("latin-1")

        content_length = int(self.headers.get("Content-Length", 0))
        if content_length <= 0:
            self.send_error(HTTPStatus.BAD_REQUEST, "Empty request body")
            return

        body = self.rfile.read(content_length)

        # Parse multipart body
        delimiter = b"--" + boundary_bytes
        parts = body.split(delimiter)

        file_bytes = None
        filename = "portrait.png"
        erase_eyes = False
        despill = 1.0
        bg_tol = 90.0

        for p in parts:
            if not p or p == b"--\r\n" or p == b"--":
                continue
            headers_and_content = p.split(b"\r\n\r\n", 1)
            if len(headers_and_content) < 2:
                continue
            head_raw, content = headers_and_content
            content = content.rstrip(b"\r\n")
            head_str = head_raw.decode("latin-1", errors="replace")

            if 'name="file"' in head_str:
                file_bytes = content
                for line in head_str.split("\r\n"):
                    if "filename=" in line:
                        for token in line.split(";"):
                            token = token.strip()
                            if token.startswith("filename="):
                                raw_fn = token.split("=", 1)[1].strip('"')
                                if raw_fn:
                                    filename = Path(raw_fn).name
            elif 'name="erase_eyes"' in head_str:
                erase_eyes = content.strip().lower() in (b"true", b"1", b"on")
            elif 'name="despill"' in head_str:
                try:
                    despill = float(content.strip().decode("latin-1"))
                except ValueError:
                    pass
            elif 'name="bg_tol"' in head_str:
                try:
                    bg_tol = float(content.strip().decode("latin-1"))
                except ValueError:
                    pass

        if not file_bytes:
            self.send_error(HTTPStatus.BAD_REQUEST, "No image file provided")
            return

        with tempfile.TemporaryDirectory() as tmp_dir:
            tmp_path = Path(tmp_dir)
            input_file = tmp_path / filename
            input_file.write_bytes(file_bytes)
            output_dir = tmp_path / "output"
            output_dir.mkdir(parents=True, exist_ok=True)

            cmd = [
                sys.executable,
                str(SCRIPT_PATH),
                "--input",
                str(input_file),
                "--output-dir",
                str(output_dir),
                "--despill",
                str(despill),
                "--bg-tol",
                str(bg_tol),
            ]
            if erase_eyes:
                cmd.append("--erase-eyes-in-base")

            env = os.environ.copy()
            env["PYTHONUTF8"] = "1"
            env["PYTHONIOENCODING"] = "utf-8"

            try:
                proc = subprocess.run(
                    cmd,
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    env=env,
                    check=False,
                )
            except Exception as e:
                self.send_json({"error": f"Execution failed: {e}"}, status=HTTPStatus.INTERNAL_SERVER_ERROR)
                return

            if proc.returncode not in (0, 2):
                self.send_json(
                    {
                        "error": proc.stderr or proc.stdout or "Processing failed.",
                        "stdout": proc.stdout,
                        "stderr": proc.stderr,
                    },
                    status=HTTPStatus.BAD_REQUEST,
                )
                return

            # Read result images and meta
            import base64

            result_data = {
                "stdout": proc.stdout,
                "parts": {},
                "meta": {},
            }

            meta_file = output_dir / "parts_meta.json"
            if meta_file.exists():
                try:
                    result_data["meta"] = json.loads(meta_file.read_text(encoding="utf-8"))
                except Exception:
                    pass

            for part_name in ("body_base", "eyes", "hair_front", "composite_test"):
                img_p = output_dir / f"{part_name}.png"
                if img_p.exists():
                    b64 = base64.b64encode(img_p.read_bytes()).decode("ascii")
                    result_data["parts"][part_name] = f"data:image/png;base64,{b64}"

            # Create in-memory zip
            zip_buf = io.BytesIO()
            with zipfile.ZipFile(zip_buf, "w", zipfile.ZIP_DEFLATED) as zf:
                for f in output_dir.iterdir():
                    if f.is_file():
                        zf.write(f, arcname=f.name)
            zip_buf.seek(0)
            result_data["zip"] = "data:application/zip;base64," + base64.b64encode(zip_buf.read()).decode("ascii")

            self.send_json(result_data)

    def send_json(self, data: dict, status: HTTPStatus = HTTPStatus.OK):
        body = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def main():
    parser = argparse.ArgumentParser(description="Portrait Parts Extractor WebUI")
    parser.add_argument("--port", type=int, default=8080, help="Server port (default: 8080)")
    parser.add_argument("--no-browser", action="store_true", help="Do not open browser automatically")
    args = parser.parse_args()

    server_address = ("", args.port)
    httpd = HTTPServer(server_address, PartsExtractorHandler)
    url = f"http://localhost:{args.port}/"
    print(f"=====================================================")
    print(f"  立ち絵パーツ自動分割 WebUI サーバー起動中")
    print(f"  URL: {url}")
    print(f"  終了するには Ctrl+C を押してください")
    print(f"=====================================================")

    if not args.no_browser:
        webbrowser.open(url)

    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nサーバーを停止しました。")
        httpd.server_close()


if __name__ == "__main__":
    main()
