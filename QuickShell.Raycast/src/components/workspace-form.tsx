import {
  Action,
  ActionPanel,
  Form,
  Icon,
  launchCommand,
  LaunchType,
  showToast,
  Toast,
  useNavigation,
  type Form as FormTypes,
} from "@raycast/api";
import { FormValidation, useForm } from "@raycast/utils";
import { useMemo, useRef, useState } from "react";
import { getQuickShellStorage } from "../lib/raycast-storage";
import { deriveAbbreviationFromName, deriveNameFromDirectory } from "../lib/directory-helpers";
import { showStorageFailure, showWorkspaceValidationFailure } from "../lib/failure-feedback";
import { createStableId } from "../lib/ids";
import type { OpenWorkspaceLaunchContext } from "../lib/launch-context";
import { buildProjectSetupSuggestions } from "../lib/project-setup-suggestion";
import type { Workspace } from "../lib/schema";
import { choiceForTerminalState, discoverWorkspaceTerminalChoices } from "../lib/terminal-catalog";
import { TERMINAL_APPLICATION_CHOICES } from "../lib/terminal-options";
import {
  buildWorkspaceFromFormState,
  launchRowsFromSuggestions,
  type LaunchFormRow,
  workspaceFormStateFromWorkspace,
} from "../lib/workspace-form-state";
import { isAbsoluteDirectory, validateWorkspace, VALIDATION_LIMITS } from "../lib/validation";

type WorkspaceFormValues = {
  name: string;
  abbreviation: string;
  directory: string;
  terminalChoiceId: string;
  isPinned: boolean;
  runAsAdmin: boolean;
  devServerUrl: string;
  openDevServerOnLaunch: boolean;
  repoUrl: string;
  openCompanionAppOnLaunch: boolean;
  companionAppPath: string;
  companionAppArguments: string;
};

export type WorkspaceFormProps = {
  mode: "create" | "edit";
  initialWorkspace: Workspace;
  draftValues?: FormTypes.Values;
  enableDrafts?: boolean;
  onSaved?: () => Promise<void> | void;
  onCreated?: (workspace: Workspace) => Promise<void> | void;
};

function directoryFromDraftValue(value: unknown): string | undefined {
  if (typeof value === "string") {
    return value;
  }
  if (Array.isArray(value) && typeof value[0] === "string") {
    return value[0];
  }
  return undefined;
}

function valuesFromState(
  state: ReturnType<typeof workspaceFormStateFromWorkspace>,
  terminalChoices: ReturnType<typeof discoverWorkspaceTerminalChoices>,
  draftValues?: FormTypes.Values,
): WorkspaceFormValues {
  const base: WorkspaceFormValues = {
    name: state.name,
    abbreviation: state.abbreviation,
    directory: state.directory,
    terminalChoiceId: choiceForTerminalState(state.terminal, state.wtProfile, terminalChoices),
    isPinned: state.isPinned,
    runAsAdmin: state.runAsAdmin,
    devServerUrl: state.devServerUrl,
    openDevServerOnLaunch: state.openDevServerOnLaunch,
    repoUrl: state.repoUrl,
    openCompanionAppOnLaunch: state.openCompanionAppOnLaunch,
    companionAppPath: state.companionAppPath,
    companionAppArguments: state.companionAppArguments,
  };

  if (!draftValues) {
    return base;
  }

  return {
    ...base,
    name: typeof draftValues.name === "string" ? draftValues.name : base.name,
    abbreviation: typeof draftValues.abbreviation === "string" ? draftValues.abbreviation : base.abbreviation,
    directory: directoryFromDraftValue(draftValues.directory) ?? base.directory,
    terminalChoiceId:
      typeof draftValues.terminalChoiceId === "string" ? draftValues.terminalChoiceId : base.terminalChoiceId,
    isPinned: typeof draftValues.isPinned === "boolean" ? draftValues.isPinned : base.isPinned,
    runAsAdmin: typeof draftValues.runAsAdmin === "boolean" ? draftValues.runAsAdmin : base.runAsAdmin,
    devServerUrl: typeof draftValues.devServerUrl === "string" ? draftValues.devServerUrl : base.devServerUrl,
    openDevServerOnLaunch:
      typeof draftValues.openDevServerOnLaunch === "boolean"
        ? draftValues.openDevServerOnLaunch
        : base.openDevServerOnLaunch,
    repoUrl: typeof draftValues.repoUrl === "string" ? draftValues.repoUrl : base.repoUrl,
    openCompanionAppOnLaunch:
      typeof draftValues.openCompanionAppOnLaunch === "boolean"
        ? draftValues.openCompanionAppOnLaunch
        : base.openCompanionAppOnLaunch,
    companionAppPath:
      typeof draftValues.companionAppPath === "string" ? draftValues.companionAppPath : base.companionAppPath,
    companionAppArguments:
      typeof draftValues.companionAppArguments === "string"
        ? draftValues.companionAppArguments
        : base.companionAppArguments,
  };
}

export default function WorkspaceForm({
  mode,
  initialWorkspace,
  draftValues,
  enableDrafts = mode === "create",
  onSaved,
  onCreated,
}: WorkspaceFormProps) {
  const { pop } = useNavigation();
  const storage = getQuickShellStorage();
  const initialState = workspaceFormStateFromWorkspace(initialWorkspace);
  const terminalChoices = useMemo(() => discoverWorkspaceTerminalChoices(), []);
  const initialValues = useMemo(
    () => valuesFromState(initialState, terminalChoices, draftValues),
    [draftValues, initialState, terminalChoices],
  );

  const [launches, setLaunches] = useState<LaunchFormRow[]>(initialState.launches);
  const nameCustomizedRef = useRef(mode === "edit" && Boolean(initialState.name));
  const abbreviationCustomizedRef = useRef(mode === "edit" && Boolean(initialState.abbreviation));
  const commandsCustomizedRef = useRef(
    mode === "edit" && initialState.launches.some((launch) => launch.command.trim()),
  );

  const { handleSubmit, itemProps, setValue, values } = useForm<WorkspaceFormValues>({
    initialValues,
    onSubmit: async (formValues) => {
      await handleSave(formValues);
    },
    validation: {
      name: FormValidation.Required,
      abbreviation: (value) => {
        if (value && value.trim().length > VALIDATION_LIMITS.MAX_ABBREVIATION_LENGTH) {
          return `Abbreviation must be ${VALIDATION_LIMITS.MAX_ABBREVIATION_LENGTH} characters or fewer.`;
        }
      },
      directory: (value) => {
        if (!value?.trim()) {
          return "Workspace directory is required.";
        }
        if (value.trim().length > VALIDATION_LIMITS.MAX_DIRECTORY_LENGTH) {
          return `Directory must be ${VALIDATION_LIMITS.MAX_DIRECTORY_LENGTH} characters or fewer.`;
        }
        if (!isAbsoluteDirectory(value.trim())) {
          return "Directory must be an absolute path.";
        }
      },
    },
  });

  const selectedTerminal =
    terminalChoices.find((choice) => choice.id === values.terminalChoiceId) ?? terminalChoices[0];

  function applyDirectorySuggestions(nextDirectory: string) {
    if (!nameCustomizedRef.current && nextDirectory.trim()) {
      const derivedName = deriveNameFromDirectory(nextDirectory);
      setValue("name", derivedName);
      if (!abbreviationCustomizedRef.current && derivedName) {
        setValue("abbreviation", deriveAbbreviationFromName(derivedName));
      }
    }

    if (!commandsCustomizedRef.current && nextDirectory.trim()) {
      const suggestions = buildProjectSetupSuggestions(nextDirectory);
      if (suggestions.length > 0) {
        setLaunches(launchRowsFromSuggestions(suggestions, selectedTerminal?.terminal ?? "default"));
      } else {
        setLaunches([
          {
            id: createStableId(),
            command: "",
            terminal: selectedTerminal?.terminal ?? "default",
            wtProfile: selectedTerminal?.wtProfile ?? null,
            runAsAdmin: values.runAsAdmin,
            isEnabled: true,
            label: "Launch",
          },
        ]);
      }
    }
  }

  function handleDirectoryChange(paths: string[]) {
    const nextDirectory = paths[0] ?? "";
    setValue("directory", nextDirectory);
    applyDirectorySuggestions(nextDirectory);
  }

  function updateLaunch(index: number, patch: Partial<LaunchFormRow>) {
    commandsCustomizedRef.current = true;
    setLaunches((current) => current.map((row, rowIndex) => (rowIndex === index ? { ...row, ...patch } : row)));
  }

  function addLaunchRow() {
    commandsCustomizedRef.current = true;
    setLaunches((current) => [
      ...current,
      {
        id: createStableId(),
        command: "",
        terminal: selectedTerminal?.terminal ?? "default",
        wtProfile: selectedTerminal?.wtProfile ?? null,
        runAsAdmin: values.runAsAdmin,
        isEnabled: true,
        label: `Launch ${current.length + 1}`,
      },
    ]);
  }

  function removeLaunchRow(index: number) {
    commandsCustomizedRef.current = true;
    setLaunches((current) => {
      if (current.length <= 1) {
        return current;
      }
      return current.filter((_, rowIndex) => rowIndex !== index);
    });
  }

  function updateLaunchTerminal(index: number, choiceId: string) {
    const choice = terminalChoices.find((item) => item.id === choiceId);
    if (!choice) {
      return;
    }
    updateLaunch(index, {
      terminal: choice.terminal,
      wtProfile: choice.wtProfile ?? null,
    });
  }

  function buildWorkspace(formValues: WorkspaceFormValues): Workspace {
    return buildWorkspaceFromFormState(initialWorkspace, {
      name: formValues.name,
      abbreviation: formValues.abbreviation,
      directory: formValues.directory,
      terminal: selectedTerminal?.terminal ?? "default",
      wtProfile: selectedTerminal?.wtProfile ?? null,
      isPinned: formValues.isPinned,
      runAsAdmin: formValues.runAsAdmin,
      launches,
      devServerUrl: formValues.devServerUrl,
      openDevServerOnLaunch: formValues.openDevServerOnLaunch,
      repoUrl: formValues.repoUrl,
      openCompanionAppOnLaunch: formValues.openCompanionAppOnLaunch,
      companionAppPath: formValues.companionAppPath,
      companionAppArguments: formValues.companionAppArguments,
    });
  }

  async function handleSave(formValues: WorkspaceFormValues) {
    const workspace = buildWorkspace(formValues);
    const validation = validateWorkspace(workspace);
    if (!validation.ok) {
      await showWorkspaceValidationFailure(validation.message);
      return;
    }

    try {
      await storage.upsertWorkspace(workspace);
      await onSaved?.();
      await showToast({
        style: Toast.Style.Success,
        title: mode === "create" ? "Workspace created" : "Workspace saved",
        message: workspace.name,
      });

      if (mode === "edit") {
        pop();
        return;
      }

      if (onCreated) {
        await onCreated(workspace);
        return;
      }

      setValue("name", "");
      setValue("abbreviation", "");
      setValue("directory", "");
      setValue("isPinned", false);
      setValue("runAsAdmin", false);
      setValue("devServerUrl", "");
      setValue("openDevServerOnLaunch", false);
      setValue("repoUrl", "");
      setValue("openCompanionAppOnLaunch", false);
      setValue("companionAppPath", "");
      setValue("companionAppArguments", "");
      setLaunches([
        {
          id: createStableId(),
          command: "",
          terminal: "default",
          wtProfile: null,
          runAsAdmin: false,
          isEnabled: true,
          label: "Launch",
        },
      ]);
      nameCustomizedRef.current = false;
      abbreviationCustomizedRef.current = false;
      commandsCustomizedRef.current = false;
    } catch (error) {
      await showStorageFailure(mode === "create" ? "Create workspace" : "Save workspace", error);
    }
  }

  return (
    <Form
      enableDrafts={enableDrafts}
      draftValues={draftValues}
      actions={
        <ActionPanel>
          <Action.SubmitForm
            title={mode === "create" ? "Create Workspace" : "Save Workspace"}
            icon={Icon.Check}
            onSubmit={handleSubmit}
          />
          <Action title="Add Command" icon={Icon.Plus} onAction={addLaunchRow} />
          {launches.length > 1 ? (
            <ActionPanel.Section title="Remove command">
              {launches.map((launch, index) => (
                <Action
                  key={`remove-${launch.id}`}
                  title={`Remove Command ${index + 1}`}
                  icon={Icon.Minus}
                  onAction={() => removeLaunchRow(index)}
                />
              ))}
            </ActionPanel.Section>
          ) : null}
        </ActionPanel>
      }
    >
      <Form.FilePicker
        id="directory"
        title="Directory"
        value={values.directory ? [values.directory] : []}
        onChange={handleDirectoryChange}
        canChooseDirectories
        canChooseFiles={false}
        error={itemProps.directory.error}
      />
      <Form.TextField
        id="name"
        title="Name"
        placeholder="Project name"
        {...itemProps.name}
        onChange={(value) => {
          nameCustomizedRef.current = true;
          itemProps.name.onChange?.(value);
        }}
      />
      <Form.TextField
        id="abbreviation"
        title="Home keyword"
        info="Type this in Open Workspace for a fast match (for example: home, api, fe)."
        placeholder="home"
        {...itemProps.abbreviation}
        onChange={(value) => {
          abbreviationCustomizedRef.current = true;
          itemProps.abbreviation.onChange?.(value);
        }}
      />
      <Form.Dropdown id="terminalChoiceId" title="Terminal" {...itemProps.terminalChoiceId}>
        {terminalChoices.map((choice) => (
          <Form.Dropdown.Item key={choice.id} value={choice.id} title={choice.title} />
        ))}
      </Form.Dropdown>
      {launches.map((launch, index) => (
        <Form.TextField
          key={launch.id}
          id={`command-${launch.id}`}
          title={launches.length === 1 ? "Startup Command" : `Command ${index + 1}`}
          value={launch.command}
          onChange={(value) => updateLaunch(index, { command: value })}
          placeholder={index === 0 ? "npm run dev" : "dotnet watch"}
        />
      ))}
      {launches.length > 1
        ? launches.map((launch, index) => (
            <Form.Dropdown
              key={`terminal-${launch.id}`}
              id={`terminal-${launch.id}`}
              title={`Terminal ${index + 1}`}
              value={choiceForTerminalState(launch.terminal, launch.wtProfile, terminalChoices)}
              onChange={(value) => updateLaunchTerminal(index, value)}
            >
              {terminalChoices.map((choice) => (
                <Form.Dropdown.Item key={`${launch.id}-${choice.id}`} value={choice.id} title={choice.title} />
              ))}
            </Form.Dropdown>
          ))
        : null}
      {launches.length > 1 ? (
        <Form.Description
          title="Multiple commands"
          text="Each command opens as its own launch entry. Use Actions → Remove command to delete a row."
        />
      ) : null}
      <Form.Checkbox id="isPinned" label="Favorite" {...itemProps.isPinned} />
      <Form.Checkbox id="runAsAdmin" label="Run as administrator" {...itemProps.runAsAdmin} />
      <Form.Separator />
      <Form.TextField
        id="devServerUrl"
        title="Dev Server URL"
        placeholder="http://localhost:5173"
        {...itemProps.devServerUrl}
      />
      <Form.Checkbox
        id="openDevServerOnLaunch"
        label="Open dev server link on launch"
        {...itemProps.openDevServerOnLaunch}
      />
      <Form.TextField
        id="repoUrl"
        title="Repository URL"
        placeholder="https://github.com/org/repo"
        {...itemProps.repoUrl}
      />
      <Form.TextField
        id="companionAppPath"
        title="Companion App"
        placeholder="C:\\Program Files\\Microsoft VS Code\\Code.exe"
        {...itemProps.companionAppPath}
      />
      <Form.TextField
        id="companionAppArguments"
        title="Companion Arguments"
        info="Use {folder} or . for the workspace directory."
        placeholder="{folder}"
        {...itemProps.companionAppArguments}
      />
      <Form.Checkbox
        id="openCompanionAppOnLaunch"
        label="Open companion app on launch"
        {...itemProps.openCompanionAppOnLaunch}
      />
      <Form.Description
        title="Defaults"
        text={`Commands and names auto-fill from the selected folder when possible. Terminals marked "default" use ${TERMINAL_APPLICATION_CHOICES.find((choice) => choice.id === "wt")?.title ?? "Raycast extension preferences"}.`}
      />
    </Form>
  );
}

export async function launchOpenWorkspaceAfterCreate(workspace: Workspace): Promise<void> {
  const context: OpenWorkspaceLaunchContext = {
    focusWorkspaceName: workspace.name,
    focusWorkspaceId: workspace.id,
  };

  await launchCommand({
    name: "open-workspace",
    type: LaunchType.UserInitiated,
    context,
  });
}

export { createBlankWorkspace } from "../lib/create-workspace-initial";
