/**
 * Copies shared/workspace-trust-features.json into the Raycast package so Core
 * and Raycast share one kill-switch source. Fail if the files diverge after copy.
 */
const fs = require("node:fs");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..", "..");
const sharedPath = path.join(repoRoot, "shared", "workspace-trust-features.json");
const raycastPath = path.join(__dirname, "..", "src", "lib", "workspace-trust-features.json");

if (!fs.existsSync(sharedPath)) {
  console.error(`Missing shared trust features file: ${sharedPath}`);
  process.exit(1);
}

fs.mkdirSync(path.dirname(raycastPath), { recursive: true });
fs.copyFileSync(sharedPath, raycastPath);

const shared = fs.readFileSync(sharedPath, "utf8").trim();
const copied = fs.readFileSync(raycastPath, "utf8").trim();
if (shared !== copied) {
  console.error("workspace-trust-features.json copy failed to match shared source.");
  process.exit(1);
}
