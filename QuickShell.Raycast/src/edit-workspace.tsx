import { List } from "@raycast/api";

export default function EditWorkspaceCommand() {
  return (
    <List>
      <List.EmptyView
        title="Edit Workspace"
        description="Open a workspace from Open Workspace and choose Edit, or wait for issue #24."
      />
    </List>
  );
}
