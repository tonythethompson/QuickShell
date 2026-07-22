import { Detail } from "@raycast/api";

export default function WindowsRequiredView() {
  return (
    <Detail
      markdown={`# Windows required

Quick Shell Raycast launches **Windows Terminal**, **PowerShell**, **cmd**, and **WSL** workspaces.

Install and use **Raycast for Windows** with this extension. The manifest restricts installation to \`platforms: ["Windows"]\`.`}
    />
  );
}
