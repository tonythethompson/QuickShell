import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import path from "node:path";

export const DEFAULT_EXPORT_FILE_NAME = "quickshell-workspaces.json";

type DialogKind = "save" | "open";

/**
 * Windows-only Save/Open dialogs via WinForms (Raycast has no native save panel).
 * Returns an absolute path, or null when the user cancels.
 */
export function pickWorkspaceTransferJsonPath(kind: DialogKind): string | null {
  if (process.platform !== "win32") {
    return null;
  }

  const script =
    kind === "save"
      ? [
          "Add-Type -AssemblyName System.Windows.Forms",
          "$d = New-Object System.Windows.Forms.SaveFileDialog",
          "$d.Title = 'Export Quick Shell workspaces'",
          "$d.Filter = 'JSON files (*.json)|*.json|All files (*.*)|*.*'",
          `$d.FileName = '${DEFAULT_EXPORT_FILE_NAME}'`,
          "$d.DefaultExt = 'json'",
          "$d.AddExtension = $true",
          "$d.OverwritePrompt = $true",
          "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.FileName) }",
        ].join("; ")
      : [
          "Add-Type -AssemblyName System.Windows.Forms",
          "$d = New-Object System.Windows.Forms.OpenFileDialog",
          "$d.Title = 'Import Quick Shell workspaces'",
          "$d.Filter = 'JSON files (*.json)|*.json|All files (*.*)|*.*'",
          "$d.CheckFileExists = $true",
          "$d.Multiselect = $false",
          "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.FileName) }",
        ].join("; ");

  try {
    const output = execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-STA", "-Command", script], {
      encoding: "utf8",
      windowsHide: true,
      timeout: 120_000,
    });
    const selected = output.trim();
    return selected.length > 0 ? path.resolve(selected) : null;
  } catch {
    return null;
  }
}

export function writeWorkspaceExportFile(filePath: string, json: string): void {
  writeFileSync(filePath, json, "utf8");
}

export function readWorkspaceImportFile(filePath: string): string {
  return readFileSync(filePath, "utf8");
}
