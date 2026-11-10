#!/usr/bin/env python3
"""Publish allure-results to Allure Docker Service.

Credentials stay in the environment and in a temporary login body. This script
prints status codes and the report URL, never the username, password, or cookies.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

MAX_FILES = 20
MAX_BYTES = 8_000_000


def result_directories(root: Path) -> list[Path]:
    return sorted(
        path
        for path in root.rglob("allure-results")
        if path.is_dir() and "bin" in path.parts and "obj" not in path.parts
    )


def qualify(result: dict, framework: str) -> dict:
    """Keep each target framework as its own test. Same history ids would collapse into retries."""
    suffix = f" [{framework}]"
    name = result.get("name")
    if isinstance(name, str) and not name.endswith(suffix):
        result["name"] = f"{name}{suffix}"
    full_name = result.get("fullName")
    if isinstance(full_name, str) and not full_name.endswith(suffix):
        result["fullName"] = f"{full_name}{suffix}"
    for key in ("historyId", "testCaseId"):
        value = result.get(key)
        if isinstance(value, str) and not value.endswith(f":{framework}"):
            result[key] = f"{value}:{framework}"
    title_path = result.get("titlePath")
    if isinstance(title_path, list) and (not title_path or title_path[0] != framework):
        result["titlePath"] = [framework, *title_path]
    labels = result.get("labels")
    if not isinstance(labels, list):
        labels = []
        result["labels"] = labels
    if not any(
        isinstance(label, dict) and label.get("name") == "tag" and label.get("value") == framework
        for label in labels
    ):
        labels.append({"name": "tag", "value": framework})
    return result


def stage(directories: list[Path], destination: Path) -> int:
    seen: dict[str, int] = {}
    written = 0
    for directory in directories:
        framework = directory.parent.name
        for source in sorted(path for path in directory.iterdir() if path.is_file()):
            seen[source.name] = seen.get(source.name, 0) + 1
            target_name = (
                source.name
                if seen[source.name] == 1
                else f"{framework}-{seen[source.name]}-{source.name}"
            )
            target = destination / target_name
            if source.name.endswith("-result.json"):
                document = json.loads(source.read_text(encoding="utf-8"))
                if isinstance(document, dict):
                    qualify(document, framework)
                target.write_text(
                    json.dumps(document, ensure_ascii=False, separators=(",", ":")),
                    encoding="utf-8",
                )
            else:
                target.write_bytes(source.read_bytes())
            written += 1
    return written


def batches(files: list[Path], max_files: int = MAX_FILES, max_bytes: int = MAX_BYTES) -> list[list[Path]]:
    groups: list[list[Path]] = []
    current: list[Path] = []
    size = 0
    for file in files:
        file_size = file.stat().st_size
        if current and (len(current) >= max_files or size + file_size > max_bytes):
            groups.append(current)
            current = []
            size = 0
        current.append(file)
        size += file_size
    if current:
        groups.append(current)
    return groups


def csrf_token(jar: Path) -> str | None:
    if not jar.exists():
        return None
    for line in jar.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("#") and not line.startswith("#HttpOnly_"):
            continue
        parts = line.split("\t")
        if len(parts) >= 7 and parts[5] == "csrf_access_token":
            return parts[6]
    return None


def redact(text: str, secrets: list[str]) -> str:
    redacted = text
    for secret in secrets:
        if secret:
            redacted = redacted.replace(secret, "***")
    return redacted


def curl_request(
    url: str,
    *,
    method: str,
    jar: Path,
    csrf: str | None = None,
    body_file: Path | None = None,
    files: list[Path] | None = None,
    timeout: int = 180,
) -> tuple[int, bytes]:
    with tempfile.NamedTemporaryFile() as response:
        command = [
            "curl",
            "-sS",
            "--max-time",
            str(timeout),
            "-o",
            response.name,
            "-w",
            "%{http_code}",
            "-X",
            method,
            "-c",
            str(jar),
            "-b",
            str(jar),
        ]
        if csrf is not None:
            command += ["-H", f"X-CSRF-TOKEN: {csrf}"]
        if body_file is not None:
            command += ["-H", "Content-Type: application/json", "--data-binary", f"@{body_file}"]
        for file in files or []:
            command += ["-F", f"files[]=@{file}"]
        command.append(url)
        completed = subprocess.run(command, capture_output=True)
        payload = Path(response.name).read_bytes()
    if completed.returncode != 0:
        detail = completed.stderr.decode(errors="replace").strip()
        raise RuntimeError(f"curl exited {completed.returncode} for {method} {url.split('?', 1)[0]}: {detail[:300]}")
    status_text = completed.stdout.decode().strip()
    if not status_text.isdigit():
        raise RuntimeError(f"curl returned no HTTP status for {method} {url.split('?', 1)[0]}")
    return int(status_text), payload


def report_url(payload: bytes) -> str:
    document = json.loads(payload)
    data = document.get("data") if isinstance(document, dict) else None
    url = data.get("report_url") if isinstance(data, dict) else None
    if not isinstance(url, str) or url == "":
        raise RuntimeError("generate-report response did not include data.report_url")
    return url


def write_summary(url: str) -> None:
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary:
        return
    with open(summary, "a", encoding="utf-8") as handle:
        handle.write(f"### Allure report\n\n[{url}]({url})\n")


def api_base(server: str) -> str:
    return f"{server.rstrip('/')}/allure-docker-service"


def fail(status: int, payload: bytes, action: str, secrets: list[str]) -> None:
    snippet = redact(payload.decode(errors="replace"), secrets).replace("\n", " ")[:240]
    print(f"Allure {action} failed with HTTP {status}. {snippet}", file=sys.stderr)


def publish(root: Path) -> int:
    server = os.environ.get("ALLURE_SERVER_URL", "")
    user = os.environ.get("ALLURE_USER", "")
    password = os.environ.get("ALLURE_PASSWORD", "")
    if server == "" or user == "" or password == "":
        print("Allure secrets are not available; skipping upload.")
        return 0

    directories = result_directories(root)
    if not directories:
        print(f"No allure-results directories under {root}.", file=sys.stderr)
        return 1

    project_id = os.environ.get("ALLURE_PROJECT_ID", "recdiffy")
    execution_name = os.environ.get("ALLURE_EXECUTION_NAME", "recdiffy")
    execution_from = os.environ.get("ALLURE_EXECUTION_FROM", "github-actions")
    base = api_base(server)
    secrets = [user, password]

    with tempfile.TemporaryDirectory(prefix="allure-publish-") as temporary:
        staging = Path(temporary) / "results"
        staging.mkdir()
        jar = Path(temporary) / "cookies"
        count = stage(directories, staging)
        files = sorted(path for path in staging.iterdir() if path.is_file())
        print(f"Staged {count} Allure files from {len(directories)} result directories.")
        if not files:
            print("Allure result directories were empty.", file=sys.stderr)
            return 1

        login_body = Path(temporary) / "login.json"
        login_body.write_text(
            json.dumps({"username": user, "password": password}),
            encoding="utf-8",
        )
        os.chmod(login_body, 0o600)
        status, payload = curl_request(
            f"{base}/login",
            method="POST",
            jar=jar,
            body_file=login_body,
            timeout=60,
        )
        login_body.unlink(missing_ok=True)
        if status < 200 or status >= 300:
            fail(status, payload, "login", secrets)
            return 1

        csrf = csrf_token(jar)
        if csrf is None or "\n" in csrf or "\r" in csrf:
            print("Login did not store a csrf_access_token cookie.", file=sys.stderr)
            return 1
        secrets.append(csrf)

        try:
            status, payload = curl_request(
                f"{base}/clean-results?project_id={project_id}",
                method="GET",
                jar=jar,
                csrf=csrf,
                timeout=60,
            )
            if status < 200 or status >= 300:
                fail(status, payload, "clean-results", secrets)
                return 1

            for index, group in enumerate(batches(files), start=1):
                status, payload = curl_request(
                    f"{base}/send-results?project_id={project_id}",
                    method="POST",
                    jar=jar,
                    csrf=csrf,
                    files=group,
                )
                if status < 200 or status >= 300:
                    fail(status, payload, f"send-results batch {index}", secrets)
                    return 1
                print(f"Sent batch {index} ({len(group)} files).")

            query = (
                f"project_id={project_id}"
                f"&execution_name={quote(execution_name)}"
                f"&execution_from={quote(execution_from)}"
                f"&execution_type=github"
            )
            status, payload = curl_request(
                f"{base}/generate-report?{query}",
                method="GET",
                jar=jar,
                csrf=csrf,
                timeout=300,
            )
            if status < 200 or status >= 300:
                fail(status, payload, "generate-report", secrets)
                return 1
            url = report_url(payload)
            if not url.startswith("http"):
                url = f"{server.rstrip('/')}/{url.lstrip('/')}"
            print(f"Allure report: {url}")
            write_summary(url)
            return 0
        finally:
            try:
                logout_status, _ = curl_request(
                    f"{base}/logout",
                    method="DELETE",
                    jar=jar,
                    csrf=csrf,
                    timeout=30,
                )
                if logout_status < 200 or logout_status >= 300:
                    print(f"Allure logout returned HTTP {logout_status}.", file=sys.stderr)
            except Exception as error:
                print(f"Allure logout failed: {error}", file=sys.stderr)


def quote(value: str) -> str:
    from urllib.parse import quote as url_quote

    return url_quote(value, safe="")


def self_check() -> int:
    sample = {
        "name": "Adds",
        "fullName": "Tests.Adds",
        "historyId": "abc",
        "testCaseId": "def",
        "titlePath": ["Tests"],
        "labels": [{"name": "framework", "value": "xUnit.net"}],
    }
    once = qualify(dict(sample), "net8.0")
    twice = qualify(dict(once), "net8.0")
    assert once == twice
    assert once["name"] == "Adds [net8.0]"
    assert once["historyId"] == "abc:net8.0"
    assert once["titlePath"] == ["net8.0", "Tests"]
    assert any(label.get("value") == "net8.0" and label.get("name") == "tag" for label in once["labels"])

    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        files = []
        for index in range(25):
            path = root / f"{index}.json"
            path.write_text("x", encoding="utf-8")
            files.append(path)
        grouped = batches(files, max_files=20, max_bytes=10_000)
        assert [len(group) for group in grouped] == [20, 5]

        jar = root / "cookies"
        jar.write_text(
            "# Netscape HTTP Cookie File\n"
            "#HttpOnly_recdiffytests.cobblerhill.net\tFALSE\t/\tTRUE\t0\tcsrf_access_token\ttoken-value\n",
            encoding="utf-8",
        )
        assert csrf_token(jar) == "token-value"
        assert redact("user secret", ["secret"]) == "user ***"
    print("self-check ok")
    return 0


def main() -> int:
    if len(sys.argv) > 1 and sys.argv[1] == "--self-check":
        return self_check()
    root = Path(os.environ.get("ALLURE_RESULTS_ROOT", ".")).resolve()
    if len(sys.argv) > 1 and sys.argv[1] == "--prepare":
        if len(sys.argv) != 3:
            print("usage: publish_allure.py --prepare <directory>", file=sys.stderr)
            return 2
        destination = Path(sys.argv[2])
        destination.mkdir(parents=True, exist_ok=True)
        directories = result_directories(root)
        print(f"Staged {stage(directories, destination)} files from {len(directories)} directories into {destination}")
        return 0 if directories else 1
    return publish(root)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"Allure upload failed: {error}", file=sys.stderr)
        sys.exit(1)
