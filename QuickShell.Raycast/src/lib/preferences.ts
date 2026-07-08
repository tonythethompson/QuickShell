import type { QuickShellSettings, TerminalApplication } from "./schema";
import { DEFAULT_SETTINGS } from "./schema";
import { recentCountFromEnabled } from "./settings";
import { normalizeDefaultProfile } from "./terminal-options";

export type ExtensionPreferences = {
  terminalApplication?: TerminalApplication;
  defaultProfile?: string;
  showRecents?: boolean;
};

export function preferencesToSettings(prefs: ExtensionPreferences): QuickShellSettings {
  const terminalApplication = prefs.terminalApplication ?? DEFAULT_SETTINGS.terminalApplication;
  const profileTerminal = terminalApplication === "system" ? "wt" : terminalApplication;
  const defaultProfile = normalizeDefaultProfile(
    profileTerminal,
    prefs.defaultProfile?.trim() || DEFAULT_SETTINGS.defaultProfile,
  );

  return {
    terminalApplication,
    defaultProfile,
    recentWorkspaceCount: recentCountFromEnabled(prefs.showRecents ?? true),
  };
}
