import { describe, expect, it } from "vitest";
import { preferencesToSettings } from "../lib/preferences";

describe("extension-preferences", () => {
  it("maps Raycast preferences to QuickShell settings", () => {
    const settings = preferencesToSettings({
      terminalApplication: "conhost",
      defaultProfile: "__default__",
      showRecents: false,
    });

    expect(settings.terminalApplication).toBe("conhost");
    expect(settings.defaultProfile).toBe("__default__");
    expect(settings.recentWorkspaceCount).toBe(0);
  });

  it("falls back to defaults for missing preference values", () => {
    const settings = preferencesToSettings({});
    expect(settings.terminalApplication).toBe("wt");
    expect(settings.defaultProfile).toBe("__default__");
    expect(settings.recentWorkspaceCount).toBe(8);
  });
});
