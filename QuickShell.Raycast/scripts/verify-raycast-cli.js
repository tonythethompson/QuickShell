const { existsSync } = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const developCommand = path.join(
  root,
  "node_modules",
  "@raycast",
  "api",
  "dist",
  "commands",
  "develop",
  "index.js",
);

const nodeVersion = process.versions.node;
const [major, minor, patch] = nodeVersion.split(".").map(Number);
const meetsNodeRequirement =
  major > 22 || (major === 22 && (minor > 14 || (minor === 14 && patch >= 0)));

if (!meetsNodeRequirement) {
  console.error(
    `QuickShell Raycast requires Node.js >= 22.14.0 (current: ${nodeVersion}).`,
  );
  console.error("Install Node 22.14+ from https://nodejs.org/ and rerun npm install.");
  process.exit(1);
}

if (!existsSync(developCommand)) {
  console.error("Raycast CLI is incomplete: missing @raycast/api develop command.");
  console.error(`Expected file:\n  ${developCommand}`);
  console.error("");
  console.error("Repair steps (PowerShell):");
  console.error("  cd QuickShell.Raycast");
  console.error("  Remove-Item -Recurse -Force node_modules");
  console.error("  Remove-Item -Force package-lock.json");
  console.error("  npm install");
  console.error("  npm run dev");
  process.exit(1);
}
