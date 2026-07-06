import { List } from "@raycast/api";

export default function SettingsCommand() {
  return (
    <List>
      <List.EmptyView title="Settings" description="QuickShell settings are coming in issue #28." />
    </List>
  );
}
