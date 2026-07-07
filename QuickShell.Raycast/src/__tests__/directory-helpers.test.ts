import { describe, expect, it } from "vitest";
import {
  deriveAbbreviationFromName,
  deriveNameFromDirectory,
} from "../lib/directory-helpers";

describe("directory-helpers", () => {
  it("derives the leaf folder name from a directory path", () => {
    expect(deriveNameFromDirectory("C:\\Projects\\Trackdub\\Agents")).toBe(
      "Agents",
    );
    expect(deriveNameFromDirectory("/home/user/repos/my-app/")).toBe("my-app");
  });

  it("derives a home keyword from the workspace name", () => {
    expect(deriveAbbreviationFromName("Trackdub Agents")).toBe(
      "trackdub-agents",
    );
    expect(deriveAbbreviationFromName("API v2")).toBe("api-v2");
  });
});
