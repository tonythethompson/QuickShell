import { describe, expect, it } from "vitest";
import * as workspaceFormState from "../lib/workspace-form-state";

const encodePillKey =
  workspaceFormState.encodePillKey ??
  workspaceFormState.default?.encodePillKey;
const decodePillKey =
  workspaceFormState.decodePillKey ??
  workspaceFormState.default?.decodePillKey;

describe("pill key codec", () => {
  it("exports codec functions", () => {
    expect(typeof encodePillKey).toBe("function");
    expect(typeof decodePillKey).toBe("function");
  });

  it("round-trips task type and command", () => {
    if (typeof encodePillKey !== "function" || typeof decodePillKey !== "function") {
      throw new Error("Pill key codec functions are not exported from workspace-form-state");
    }
    const key = encodePillKey({ taskType: "frontend", command: "npm run dev" });
    const decode = decodePillKey;
    expect(decode).toBeTypeOf("function");
    expect(decode(key)).toEqual({ taskType: "frontend", command: "npm run dev" });
  });

  it("supports commands containing commas", () => {
    if (typeof encodePillKey !== "function" || typeof decodePillKey !== "function") {
      throw new Error("Pill key codec functions are not exported from workspace-form-state");
    }
    const key = encodePillKey({ taskType: "api", command: 'git commit -m "a, b"' });
    expect(typeof decodePillKey).toBe("function");
    const decode = decodePillKey as (key: string) => { command?: string } | undefined;
    expect(decode(key)?.command).toBe('git commit -m "a, b"');
  });
});
