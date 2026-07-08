import { LaunchProps, Form } from "@raycast/api";
import { useMemo } from "react";
import WorkspaceForm, { launchOpenWorkspaceAfterCreate } from "./components/workspace-form";
import WindowsRequiredView from "./components/windows-required-view";
import { createWorkspaceFromDirectory } from "./lib/create-workspace-initial";
import { isWindowsPlatform } from "./lib/platform";

export default function CreateWorkspaceCommand({
  arguments: args,
  draftValues,
}: LaunchProps<{ arguments: Arguments.CreateWorkspace; draftValues?: Form.Values }>) {
  const initialWorkspace = useMemo(() => createWorkspaceFromDirectory(args?.directory), [args?.directory]);

  if (!isWindowsPlatform()) {
    return <WindowsRequiredView />;
  }

  return (
    <WorkspaceForm
      mode="create"
      initialWorkspace={initialWorkspace}
      draftValues={args?.directory ? undefined : draftValues}
      enableDrafts
      onCreated={launchOpenWorkspaceAfterCreate}
    />
  );
}
