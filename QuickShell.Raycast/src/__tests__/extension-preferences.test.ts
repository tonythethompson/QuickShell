import { describe, expect, it } from "vitest";
import { type ExtensionPreferences, preferencesToSettings } from "../lib/preferences";

describe("extension-preferences", () => {
  it("maps Raycast preferences to QuickShell settings", () => {
    const settings = preferencesToSettings({
      terminalApplication: "conhost",
      defaultProfile: "__default__",
      showRecents: false,
      blockDirtyBranchSwitch: false,
    });

    expect(settings.terminalApplication).toBe("conhost");
    expect(settings.defaultProfile).toBe("__default__");
    expect(settings.recentWorkspaceCount).toBe(0);
    expect(settings.multiLaunchPresentation).toBe("singleWindowTabs");
    expect(settings.blockDirtyBranchSwitch).toBe(false);
  });

  it("falls back to defaults for missing preference values", () => {
    const settings = preferencesToSettings({});
    expect(settings.terminalApplication).toBe("wt");
    expect(settings.defaultProfile).toBe("__default__");
    expect(settings.recentWorkspaceCount).toBe(8);
    expect(settings.multiLaunchPresentation).toBe("singleWindowTabs");
    expect(settings.blockDirtyBranchSwitch).toBe(true);
  });

  it("falls back to wt for unknown terminalApplication values", () => {
    const settings = preferencesToSettings({
      terminalApplication: "bogus" as ExtensionPreferences["terminalApplication"],
    });
    expect(settings.terminalApplication).toBe("wt");
  });

  it("maps singleWindowTabs preference to multiLaunchPresentation", () => {
    const tabs = preferencesToSettings({ singleWindowTabs: true });
    expect(tabs.multiLaunchPresentation).toBe("singleWindowTabs");

    const windows = preferencesToSettings({ singleWindowTabs: false });
    expect(windows.multiLaunchPresentation).toBe("separateWindows");
  });
});
