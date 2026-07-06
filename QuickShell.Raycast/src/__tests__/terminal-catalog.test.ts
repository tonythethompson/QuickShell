import { describe, expect, it } from "vitest";
import {
  discoverDefaultProfileChoices,
  discoverWorkspaceTerminalChoices,
  resetTerminalCatalogCacheForTests,
} from "../lib/terminal-catalog";

describe("terminal-catalog", () => {
  it("returns workspace terminal choices on non-windows platforms", () => {
    resetTerminalCatalogCacheForTests();
    const choices = discoverWorkspaceTerminalChoices();
    expect(choices.some((choice) => choice.id === "default")).toBe(true);
    expect(choices.every((choice) => choice.terminal)).toBe(true);
  });

  it("includes a default profile sentinel for windows terminal settings", () => {
    const choices = discoverDefaultProfileChoices("wt");
    expect(choices[0]?.id).toBe("__default__");
    expect(choices.length).toBeGreaterThan(1);
  });

  it("includes conhost shell choices for console host settings", () => {
    const choices = discoverDefaultProfileChoices("conhost");
    expect(choices.some((choice) => choice.id === "__default__")).toBe(true);
    expect(choices.some((choice) => choice.id === "powershell" || choice.id === "pwsh" || choice.id === "cmd")).toBe(
      true,
    );
  });
});
