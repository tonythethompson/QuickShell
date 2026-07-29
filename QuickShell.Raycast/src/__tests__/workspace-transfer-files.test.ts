import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import {
  DEFAULT_EXPORT_FILE_NAME,
  buildMacTransferOsascript,
  buildWindowsTransferPowerShell,
  readWorkspaceImportFile,
  writeWorkspaceExportFile,
} from "../lib/workspace-transfer-files";

describe("workspace-transfer-files", () => {
  const dirs: string[] = [];

  afterEach(() => {
    for (const dir of dirs.splice(0)) {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it("writes and reads UTF-8 JSON round-trip", () => {
    const dir = mkdtempSync(path.join(tmpdir(), "qs-transfer-"));
    dirs.push(dir);
    const filePath = path.join(dir, DEFAULT_EXPORT_FILE_NAME);
    const json = '{"version":1,"workspaces":[]}';

    writeWorkspaceExportFile(filePath, json);

    expect(readFileSync(filePath, "utf8")).toBe(json);
    expect(readWorkspaceImportFile(filePath)).toBe(json);
  });

  it("seeds Windows dialogs on the Desktop for faster first paint", () => {
    expect(buildWindowsTransferPowerShell("open")).toContain("GetFolderPath('Desktop')");
    expect(buildWindowsTransferPowerShell("save")).toContain("GetFolderPath('Desktop')");
  });

  it("reactivates Raycast after Windows and macOS file dialogs", () => {
    const windowsOpen = buildWindowsTransferPowerShell("open");
    const windowsSave = buildWindowsTransferPowerShell("save");
    expect(windowsOpen).toContain("AppActivate");
    expect(windowsSave).toContain("AppActivate");
    expect(windowsOpen).toContain("catch [System.Runtime.InteropServices.COMException]");
    expect(windowsOpen).not.toContain("catch {}");

    const macOpen = buildMacTransferOsascript("open");
    const macSave = buildMacTransferOsascript("save");
    expect(macOpen).toContain('tell application "Raycast" to activate');
    expect(macSave).toContain('tell application "Raycast" to activate');
    // Activation is best-effort and must not discard a valid selection.
    expect(macOpen.indexOf("end try")).toBeLessThan(macOpen.lastIndexOf('tell application "Raycast" to activate'));
    expect(macOpen.indexOf('tell application "Raycast" to activate')).toBeLessThan(
      macOpen.lastIndexOf("return chosenPath"),
    );
  });
});
