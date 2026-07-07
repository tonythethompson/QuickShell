import { LaunchProps } from "@raycast/api";
import { useMemo } from "react";
import WorkspaceForm, { launchOpenWorkspaceAfterCreate } from "./components/workspace-form";
import WindowsRequiredView from "./components/windows-required-view";
import { createWorkspaceFromDirectory } from "./lib/create-workspace-initial";
import { isWindowsPlatform } from "./lib/platform";

export default function CreateWorkspaceCommand({
  arguments: args,
  draftValues,
}: LaunchProps<{ arguments: Arguments.CreateWorkspace }>) {
  if (!isWindowsPlatform()) {
    return <WindowsRequiredView />;
  }

  const initialWorkspace = useMemo(() => createWorkspaceFromDirectory(args?.directory), [args?.directory]);

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
