const { spawnSync } = require("node:child_process");
const path = require("node:path");

const root = path.resolve(__dirname, "..");

const nodeVersion = process.versions.node;
const [major, minor, patch] = nodeVersion.split(".").map(Number);
const meetsNodeRequirement =
  major > 22 || (major === 22 && (minor > 14 || (minor === 14 && patch >= 0)));

if (!meetsNodeRequirement) {
  console.error(`QuickShell Raycast requires Node.js >= 22.14.0 (current: ${nodeVersion}).`);
  console.error("Install Node 22.14+ from https://nodejs.org/ and rerun npm install.");
  process.exit(1);
}

const npxCommand = process.platform === "win32" ? "npx.cmd" : "npx";
const result = spawnSync(npxCommand, ["--no-install", "ray", "--version"], {
  cwd: root,
  encoding: "utf8",
});

if (result.status !== 0) {
  console.error("Raycast CLI is unavailable or incomplete.");
  if (result.stderr?.trim()) {
    console.error(result.stderr.trim());
  }
  console.error("");
  console.error("Repair steps (PowerShell):");
  console.error("  cd QuickShell.Raycast");
  console.error("  Remove-Item -Recurse -Force node_modules");
  console.error("  Remove-Item -Force package-lock.json");
  console.error("  npm install");
  console.error("  npm run dev");
  process.exit(1);
}
