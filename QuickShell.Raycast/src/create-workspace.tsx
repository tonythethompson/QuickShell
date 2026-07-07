import WorkspaceForm, {
  createBlankWorkspace,
} from "./components/workspace-form";

export default function CreateWorkspaceCommand() {
  return (
    <WorkspaceForm mode="create" initialWorkspace={createBlankWorkspace()} />
  );
}
