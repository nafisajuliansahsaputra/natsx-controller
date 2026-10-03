import tempfile
import unittest
from pathlib import Path
from release_readiness import assess


class ReleaseReadinessTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        for name, content in {"VERSION": "1.0.0", "VERSION_CODE": "3", "LICENSE": "Owner-approved license", "CHANGELOG.md": "## 1.0.0\n"}.items():
            (self.root / name).write_text(content)
        self.env = {
            "NATSX_INNO_LICENSE_CONFIRMED": "true", "RELEASE_SHA": "a" * 40,
            "NATSX_PHYSICAL_RELEASE_APPROVED_SHA": "a" * 40,
            "NATSX_PRODUCTION_DRIVER_BUNDLE_URL": "https://example.test/drivers.zip",
            "NATSX_PRODUCTION_DRIVER_BUNDLE_SHA256": "b" * 64,
            "WINDOWS_TIMESTAMP_URL": "https://example.test/timestamp",
        }
        for secret in ("ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD", "WINDOWS_SIGNING_PFX_BASE64", "WINDOWS_SIGNING_PFX_PASSWORD"):
            self.env["HAS_" + secret] = "true"

    def test_missing_inputs_are_reported_together(self):
        report = assess(self.root, {})
        self.assertFalse(report["inputs_ready"])
        self.assertGreater(sum(c["status"] == "blocked" for c in report["checks"]), 8)

    def test_complete_configuration_does_not_claim_hardware_certification(self):
        report = assess(self.root, self.env)
        self.assertTrue(report["inputs_ready"])
        self.assertIn("Configuration inventory only", report["scope"])

    def test_later_commit_invalidates_approval(self):
        self.env["RELEASE_SHA"] = "c" * 40
        self.assertFalse(assess(self.root, self.env)["inputs_ready"])

    def test_changelog_prefix_is_not_a_final_release_section(self):
        (self.root / "CHANGELOG.md").write_text("## 1.0.0-dev\n")
        self.assertFalse(assess(self.root, self.env)["inputs_ready"])

    def test_development_version_cannot_release(self):
        (self.root / "VERSION").write_text("1.0.0-dev")
        (self.root / "CHANGELOG.md").write_text("## 1.0.0-dev\n")
        self.assertFalse(assess(self.root, self.env)["inputs_ready"])

    def test_report_does_not_echo_secret_or_url_values(self):
        report = str(assess(self.root, self.env))
        self.assertNotIn("example.test", report)
        self.assertNotIn("b" * 64, report)


if __name__ == "__main__":
    unittest.main()
