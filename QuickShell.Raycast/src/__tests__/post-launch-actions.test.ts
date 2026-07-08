import { describe, expect, it } from "vitest";
import { buildCompanionArguments } from "../lib/post-launch-actions";

describe("post-launch-actions", () => {
  it("expands folder placeholders in companion arguments", () => {
    expect(buildCompanionArguments("{folder}", "C:\\Projects\\web")).toEqual(["C:\\Projects\\web"]);
    expect(buildCompanionArguments(".", "C:\\Projects\\web")).toEqual(["C:\\Projects\\web"]);
    expect(buildCompanionArguments("-n {folder}", "C:\\Projects\\web")).toEqual(["-n", "C:\\Projects\\web"]);
    expect(buildCompanionArguments('--title "My Project"', "C:\\Projects\\web")).toEqual(["--title", "My Project"]);
  });
});
