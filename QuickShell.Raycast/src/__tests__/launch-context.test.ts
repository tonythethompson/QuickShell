import { describe, expect, it } from "vitest";
import { resolveOpenWorkspaceSearchSeed } from "../lib/launch-context";

describe("launch-context", () => {
  it("prefers fallback text over launch context", () => {
    expect(
      resolveOpenWorkspaceSearchSeed("api", {
        focusWorkspaceName: "Frontend",
      }),
    ).toBe("api");
  });

  it("uses launch context when fallback text is empty", () => {
    expect(
      resolveOpenWorkspaceSearchSeed(undefined, {
        focusWorkspaceName: "QuickShell",
      }),
    ).toBe("QuickShell");
  });
});
