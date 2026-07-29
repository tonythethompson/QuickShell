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
  const dialogSetup =
    kind === "save"
      ? [
          "$d = New-Object System.Windows.Forms.SaveFileDialog",
          "$d.Title = 'Export Quick Shell workspaces'",
          "$d.Filter = 'JSON files (*.json)|*.json|All files (*.*)|*.*'",
          `$d.FileName = '${DEFAULT_EXPORT_FILE_NAME}'`,
          "$d.DefaultExt = 'json'",
          "$d.AddExtension = $true",
          "$d.OverwritePrompt = $true",
          `$d.InitialDirectory = ${initialDirectory}`,
        ]
      : [
          "$d = New-Object System.Windows.Forms.OpenFileDialog",
          "$d.Title = 'Import Quick Shell workspaces'",
          "$d.Filter = 'JSON files (*.json)|*.json|All files (*.*)|*.*'",
          "$d.CheckFileExists = $true",
          "$d.Multiselect = $false",
          `$d.InitialDirectory = ${initialDirectory}`,
        ];

  // WinForms dialogs steal foreground focus; restore Raycast before returning so the
  // extension UI is visible again for follow-up toasts / confirmations.
  return [
    "Add-Type -AssemblyName System.Windows.Forms",
    ...dialogSetup,
    "$selected = $null",
    "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $selected = $d.FileName }",
    reactivateRaycastPowerShellSnippet(),
    "if ($selected) { [Console]::Out.Write($selected) }",
  ].join("; ");
}

/** Best-effort: restore Raycast after an external dialog. Exported for unit tests. */
export function reactivateRaycastPowerShellSnippet(): string {
  // Keep this free of nested Add-Type quoting; AppActivate by PID is enough to unminimize.
  // Only swallow expected process/COM failures — unexpected errors should surface.
  return [
    "try {",
    "  $ray = Get-Process -Name Raycast -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1;",
    "  if ($ray) { $null = (New-Object -ComObject WScript.Shell).AppActivate($ray.Id) }",
    "} catch [System.Runtime.InteropServices.COMException] {",
    "} catch [System.Management.Automation.MethodInvocationException] {",
    "}",
  ].join(" ");
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
      // Dialog script already tries AppActivate; repeat from Node in case focus raced.
      await reactivateRaycastWindow();
      const selected = stdout.trim();
      return selected.length > 0 ? path.resolve(selected) : null;
    } catch (error) {
      await reactivateRaycastWindow();
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

/** Best-effort foreground restore after WinForms/osascript dialogs. */
export async function reactivateRaycastWindow(): Promise<void> {
  try {
    if (isWindowsPlatform()) {
      await execFileAsync(
        "powershell.exe",
        ["-NoProfile", "-NoLogo", "-NonInteractive", "-Command", reactivateRaycastPowerShellSnippet()],
        { windowsHide: true, timeout: 5_000 },
      );
      return;
    }
    if (isMacPlatform()) {
      await execFileAsync("osascript", ["-e", 'tell application "Raycast" to activate'], {
        timeout: 5_000,
      });
    }
  } catch {
    // Focus restore is best-effort; never fail the transfer path.
  }
}

/** Exported for unit tests. */
export function buildMacTransferOsascript(kind: DialogKind): string {
  if (kind === "save") {
    return [
      `set defaultName to "${DEFAULT_EXPORT_FILE_NAME}"`,
      "try",
      '  set chosenFile to choose file name with prompt "Export Quick Shell workspaces" default name defaultName',
      "  set chosenPath to POSIX path of chosenFile",
      "on error",
      '  set chosenPath to ""',
      "end try",
      // Focus restore is best-effort and must not discard a valid selection.
      "try",
      '  tell application "Raycast" to activate',
      "end try",
      "return chosenPath",
    ].join("\n");
  }
  return [
    "try",
    '  set chosenFile to choose file with prompt "Import Quick Shell workspaces" of type {"public.json", "json"}',
    "  set chosenPath to POSIX path of chosenFile",
    "on error",
    '  set chosenPath to ""',
    "end try",
    "try",
    '  tell application "Raycast" to activate',
    "end try",
    "return chosenPath",
  ].join("\n");
}

async function pickMacTransferJsonPath(kind: DialogKind): Promise<string | null> {
  try {
    const { stdout } = await execFileAsync("osascript", ["-e", buildMacTransferOsascript(kind)], {
      encoding: "utf8",
      timeout: 120_000,
    });
    await reactivateRaycastWindow();
    const selected = stdout.trim();
    return selected.length > 0 ? path.resolve(selected) : null;
  } catch {
    await reactivateRaycastWindow();
    return null;
  }
}

export function writeWorkspaceExportFile(filePath: string, json: string): void {
  writeFileSync(filePath, json, "utf8");
}

export function readWorkspaceImportFile(filePath: string): string {
  return readFileSync(filePath, "utf8");
}
