import { execFile } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { promisify } from "node:util";
import { isMacPlatform, isWindowsPlatform } from "./platform";

export const DEFAULT_EXPORT_FILE_NAME = "quickshell-workspaces.json";

type DialogKind = "save" | "open";

const execFileAsync = promisify(execFile);

/**
 * Platform save/open dialogs (Raycast has no native save panel).
 * Returns an absolute path, or null when the user cancels / unsupported.
 *
 * Async so the Raycast UI can show a loading toast while PowerShell/osascript starts
 * (WinForms file dialogs pay a cold-start cost on Windows).
 */
export async function pickWorkspaceTransferJsonPath(kind: DialogKind): Promise<string | null> {
  if (isWindowsPlatform()) {
    return pickWindowsTransferJsonPath(kind);
  }
  if (isMacPlatform()) {
    return pickMacTransferJsonPath(kind);
  }
  return null;
}

/** Builds the PowerShell script for Windows file dialogs. Exported for unit tests. */
export function buildWindowsTransferPowerShell(kind: DialogKind): string {
  const initialDirectory = "[Environment]::GetFolderPath('Desktop')";
  if (kind === "save") {
    return [
      "Add-Type -AssemblyName System.Windows.Forms",
      "$d = New-Object System.Windows.Forms.SaveFileDialog",
      "$d.Title = 'Export Quick Shell workspaces'",
      "$d.Filter = 'JSON files (*.json)|*.json|All files (*.*)|*.*'",
      `$d.FileName = '${DEFAULT_EXPORT_FILE_NAME}'`,
      "$d.DefaultExt = 'json'",
      "$d.AddExtension = $true",
      "$d.OverwritePrompt = $true",
      `$d.InitialDirectory = ${initialDirectory}`,
      "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.FileName) }",
    ].join("; ");
  }

  return [
    "Add-Type -AssemblyName System.Windows.Forms",
    "$d = New-Object System.Windows.Forms.OpenFileDialog",
    "$d.Title = 'Import Quick Shell workspaces'",
    "$d.Filter = 'JSON files (*.json)|*.json|All files (*.*)|*.*'",
    "$d.CheckFileExists = $true",
    "$d.Multiselect = $false",
    `$d.InitialDirectory = ${initialDirectory}`,
    "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.FileName) }",
  ].join("; ");
}

async function pickWindowsTransferJsonPath(kind: DialogKind): Promise<string | null> {
  const script = buildWindowsTransferPowerShell(kind);
  const shells = ["pwsh", "powershell.exe"];

  for (const shell of shells) {
    try {
      const { stdout } = await execFileAsync(
        shell,
        ["-NoProfile", "-NoLogo", "-NonInteractive", "-STA", "-Command", script],
        {
          encoding: "utf8",
          windowsHide: true,
          timeout: 120_000,
          maxBuffer: 1024 * 1024,
        },
      );
      const selected = stdout.trim();
      return selected.length > 0 ? path.resolve(selected) : null;
    } catch (error) {
      // pwsh may be missing; fall through to Windows PowerShell 5.1.
      if (shell === "powershell.exe") {
        return null;
      }
      const errno = (error as NodeJS.ErrnoException | undefined)?.code;
      if (errno !== "ENOENT") {
        // Dialog cancel often surfaces as a non-zero exit with empty stdout; treat as cancel.
        return null;
      }
    }
  }

  return null;
}

/** Exported for unit tests. */
export function buildMacTransferOsascript(kind: DialogKind): string {
  if (kind === "save") {
    return [
      `set defaultName to "${DEFAULT_EXPORT_FILE_NAME}"`,
      'set chosenFile to choose file name with prompt "Export Quick Shell workspaces" default name defaultName',
      "return POSIX path of chosenFile",
    ].join("\n");
  }
  return [
    'set chosenFile to choose file with prompt "Import Quick Shell workspaces" of type {"public.json", "json"}',
    "return POSIX path of chosenFile",
  ].join("\n");
}

async function pickMacTransferJsonPath(kind: DialogKind): Promise<string | null> {
  try {
    const { stdout } = await execFileAsync("osascript", ["-e", buildMacTransferOsascript(kind)], {
      encoding: "utf8",
      timeout: 120_000,
    });
    const selected = stdout.trim();
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
