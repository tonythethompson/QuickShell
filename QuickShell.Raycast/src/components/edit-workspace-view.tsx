import { Form, ActionPanel, Action, Icon, showToast, Toast, useNavigation } from "@raycast/api";
import { useEffect, useState } from "react";
import { getQuickShellStorage } from "../lib/raycast-storage";
import type { Workspace } from "../lib/schema";
import { createStableId } from "../lib/ids";
import { normalizeWorkspace } from "../lib/validation";

type EditWorkspaceViewProps = {
  workspaceId: string;
  onSaved?: () => Promise<void> | void;
};

export default function EditWorkspaceView({ workspaceId, onSaved }: EditWorkspaceViewProps) {
  const { pop } = useNavigation();
  const storage = getQuickShellStorage();
  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [name, setName] = useState("");
  const [abbreviation, setAbbreviation] = useState("");
  const [directory, setDirectory] = useState("");
  const [command, setCommand] = useState("");
  const [isPinned, setIsPinned] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const workspaces = await storage.getWorkspaces();
        const found = workspaces.find((item) => item.id === workspaceId);
        if (!found || cancelled) {
          return;
        }
        setWorkspace(found);
        setName(found.name);
        setAbbreviation(found.abbreviation ?? "");
        setDirectory(found.directory);
        const firstLaunch = found.launches.find((entry) => entry.isEnabled) ?? found.launches[0];
        setCommand(firstLaunch?.command ?? found.command ?? "");
        setIsPinned(found.isPinned);
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [storage, workspaceId]);

  async function handleSave() {
    if (!workspace) {
      return;
    }

    const launches = workspace.launches.map((launch, index) => {
      if (index === 0) {
        return { ...launch, command: command.trim() || null, label: launch.label || name.trim() || "Launch" };
      }
      return launch;
    });

    const next = normalizeWorkspace({
      ...workspace,
      name: name.trim(),
      abbreviation: abbreviation.trim() || null,
      directory: directory.trim(),
      command: command.trim() || null,
      isPinned,
      launches,
    });

    try {
      await storage.upsertWorkspace(next);
      await onSaved?.();
      await showToast({ style: Toast.Style.Success, title: "Workspace saved" });
      pop();
    } catch (saveError) {
      const message = saveError instanceof Error ? saveError.message : "Save failed.";
      await showToast({ style: Toast.Style.Failure, title: "Save failed", message });
    }
  }

  return (
    <Form
      isLoading={isLoading}
      actions={
        <ActionPanel>
          <Action title="Save Workspace" icon={Icon.Check} onAction={handleSave} />
        </ActionPanel>
      }
    >
      <Form.TextField id="name" title="Name" value={name} onChange={setName} />
      <Form.TextField
        id="abbreviation"
        title="Abbreviation"
        value={abbreviation}
        onChange={setAbbreviation}
        placeholder="home"
      />
      <Form.TextField
        id="directory"
        title="Directory"
        value={directory}
        onChange={setDirectory}
        placeholder="C:\\Projects\\MyApp"
      />
      <Form.TextField
        id="command"
        title="Command"
        value={command}
        onChange={setCommand}
        placeholder="npm run dev"
      />
      <Form.Checkbox id="favorite" label="Favorite" value={isPinned} onChange={setIsPinned} />
    </Form>
  );
}

export function createBlankWorkspace(): Workspace {
  const id = createStableId();
  return normalizeWorkspace({
    id,
    name: "",
    abbreviation: null,
    directory: "",
    isPinned: false,
    pinOrder: null,
    lastUsedUtc: null,
    terminal: "default",
    wtProfile: null,
    command: null,
    runAsAdmin: false,
    launches: [
      {
        id: createStableId(),
        label: "Launch",
        terminal: "default",
        wtProfile: null,
        command: null,
        runAsAdmin: false,
        isEnabled: true,
        order: 0,
        taskType: "none",
      },
    ],
  });
}
