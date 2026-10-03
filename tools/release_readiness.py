"""Report production release inputs without exposing credentials or approving hardware tests."""
import argparse
import json
import os
import re
from pathlib import Path


def assess(root, env):
    checks = []

    def add(name, passed, needed):
        checks.append({"id": name, "status": "configured" if passed else "blocked", "required": needed})

    version = (root / "VERSION").read_text().strip()
    code = (root / "VERSION_CODE").read_text().strip()
    stable = bool(re.fullmatch(r"\d+\.\d+\.\d+", version))
    add("stable_version", stable, "Stable VERSION x.y.z after release acceptance")
    add("android_version_code", bool(re.fullmatch(r"[1-9]\d*", code)), "Positive monotonic VERSION_CODE")
    license_file = root / "LICENSE"
    add("product_license", license_file.is_file() and bool(license_file.read_text().strip()), "Product owner selects and records LICENSE")
    changelog = (root / "CHANGELOG.md").read_text()
    add("final_changelog", stable and bool(re.search(r"^## " + re.escape(version) + r"\s*$", changelog, re.MULTILINE)), "Exact final CHANGELOG section matching VERSION")
    add("installer_license", env.get("NATSX_INNO_LICENSE_CONFIRMED", "").lower() == "true", "Owner confirms permitted Inno Setup use")
    sha = env.get("RELEASE_SHA", "")
    add("physical_acceptance", bool(re.fullmatch(r"[0-9a-f]{40}", sha)) and env.get("NATSX_PHYSICAL_RELEASE_APPROVED_SHA") == sha,
        "Clean install, hardware soak, sleep/resume and resource evidence for the exact source SHA")
    url = env.get("NATSX_PRODUCTION_DRIVER_BUNDLE_URL", "")
    add("driver_bundle_url", bool(re.fullmatch(r"https://[^\s]+", url)), "Microsoft retail-signed driver bundle at an immutable HTTPS location")
    add("driver_bundle_hash", bool(re.fullmatch(r"[0-9a-fA-F]{64}", env.get("NATSX_PRODUCTION_DRIVER_BUNDLE_SHA256", ""))), "Pinned SHA-256 of returned Microsoft driver bundle")
    for secret in ("ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD",
                   "WINDOWS_SIGNING_PFX_BASE64", "WINDOWS_SIGNING_PFX_PASSWORD"):
        add(secret.lower(), env.get("HAS_" + secret, "").lower() == "true", "Configure repository secret " + secret)
    add("windows_timestamp", bool(re.fullmatch(r"https?://[^\s]+", env.get("WINDOWS_TIMESTAMP_URL", ""))), "Approved RFC3161 timestamp service")
    return {"source_sha": sha, "version": version, "version_code": code,
            "inputs_ready": all(c["status"] == "configured" for c in checks),
            "scope": "Configuration inventory only; release jobs must verify signatures, catalogs and physical acceptance evidence.",
            "checks": checks}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--require-ready", action="store_true")
    args = parser.parse_args()
    report = assess(args.root, os.environ)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    lines = ["# Production release input audit", "", report["scope"], "", "| Input | Status | Required action |", "|---|---|---|"]
    lines.extend(f'| {c["id"]} | {c["status"]} | {c["required"]} |' for c in report["checks"])
    markdown = "\n".join(lines) + "\n"
    args.output.with_suffix(".md").write_text(markdown)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as summary:
            summary.write(markdown)
    print("Release inputs ready" if report["inputs_ready"] else "Production release blocked; see the complete input audit.")
    return 1 if args.require_ready and not report["inputs_ready"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
