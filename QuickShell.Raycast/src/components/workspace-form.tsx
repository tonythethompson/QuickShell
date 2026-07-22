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
import { listInstalledCompanionPresets, resolveCompanionPreset } from "../lib/companion-catalog";
import { detectCompanionSeed } from "../lib/companion-detection";
import { deriveAbbreviationFromName, deriveNameFromDirectory } from "../lib/directory-helpers";
import { showStorageFailure, showWorkspaceValidationFailure } from "../lib/failure-feedback";
import { createStableId } from "../lib/ids";
import type { OpenWorkspaceLaunchContext } from "../lib/launch-context";
import { buildProjectSetupSuggestions } from "../lib/project-setup-suggestion";
import { resolveWorkspaceSetupSuggestions, type SuggestionPill } from "../lib/suggest-commands";
import type { Workspace } from "../lib/schema";
import { choiceForTerminalState, discoverWorkspaceTerminalChoices } from "../lib/terminal-catalog";
import { TERMINAL_APPLICATION_CHOICES } from "../lib/terminal-options";
import {
  buildWorkspaceFromFormState,
  launchRowsFromSuggestions,
  type CompanionFormRow,
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
  const [companions, setCompanions] = useState<CompanionFormRow[]>(initialState.companions);
  const [suggestionPills, setSuggestionPills] = useState<SuggestionPill[]>([]);
  const [suggestionSource, setSuggestionSource] = useState<"suggest" | "local" | null>(null);
  const nameCustomizedRef = useRef(mode === "edit" && Boolean(initialState.name));
  const abbreviationCustomizedRef = useRef(mode === "edit" && Boolean(initialState.abbreviation));
  const commandsCustomizedRef = useRef(
    mode === "edit" && initialState.launches.some((launch) => launch.command.trim()),
  );
  const companionsCustomizedRef = useRef(mode === "edit" && initialState.companions.length > 0);
  const suggestionGenerationRef = useRef(0);
  const companionPresets = useMemo(() => listInstalledCompanionPresets(), []);
  const unusedSuggestionPills = useMemo(() => {
    const used = new Set(launches.map((launch) => launch.command.trim().toLowerCase()).filter(Boolean));
    return suggestionPills.filter((pill) => !used.has(pill.command.trim().toLowerCase()));
  }, [launches, suggestionPills]);

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

  async function applyDirectorySuggestions(nextDirectory: string) {
    if (!nameCustomizedRef.current && nextDirectory.trim()) {
      const derivedName = deriveNameFromDirectory(nextDirectory);
      setValue("name", derivedName);
      if (!abbreviationCustomizedRef.current && derivedName) {
        setValue("abbreviation", deriveAbbreviationFromName(derivedName));
      }
    }

    if (!nextDirectory.trim()) {
      setSuggestionPills([]);
      setSuggestionSource(null);
      return;
    }

    const generation = ++suggestionGenerationRef.current;
    const usedCommands = launches.map((launch) => launch.command.trim()).filter(Boolean);
    const resolved = commandsCustomizedRef.current
      ? {
          source: "local" as const,
          tasks: [] as Array<{ label: string; command: string }>,
          pills: [] as SuggestionPill[],
        }
      : await resolveWorkspaceSetupSuggestions(nextDirectory, usedCommands);
    if (generation !== suggestionGenerationRef.current) {
      return;
    }

    if (!commandsCustomizedRef.current) {
      setSuggestionSource(resolved.source);
      setSuggestionPills(resolved.pills);
      if (resolved.tasks.length > 0) {
        setLaunches(launchRowsFromSuggestions(resolved.tasks, selectedTerminal?.terminal ?? "default"));
      } else {
        const localFallback = buildProjectSetupSuggestions(nextDirectory);
        if (localFallback.length > 0) {
          setSuggestionSource("local");
          setLaunches(launchRowsFromSuggestions(localFallback, selectedTerminal?.terminal ?? "default"));
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

    if (!companionsCustomizedRef.current) {
      const seed = detectCompanionSeed(nextDirectory);
      setCompanions(
        seed
          ? [
              {
                id: createStableId(),
                path: seed.path,
                arguments: seed.arguments,
                openOnLaunch: true,
              },
            ]
          : [],
      );
    }
  }

  function handleDirectoryChange(paths: string[]) {
    const nextDirectory = paths[0] ?? "";
    setValue("directory", nextDirectory);
    void applyDirectorySuggestions(nextDirectory);
  }

  function applySuggestionPill(pill: SuggestionPill) {
    commandsCustomizedRef.current = true;
    setLaunches((current) => {
      if (current.length === 1 && !current[0].command.trim()) {
        return [
          {
            ...current[0],
            command: pill.command,
            label: pill.displayTitle || pill.typeTitle || pill.command,
          },
        ];
      }
      return [
        ...current,
        {
          id: createStableId(),
          command: pill.command,
          terminal: selectedTerminal?.terminal ?? "default",
          wtProfile: selectedTerminal?.wtProfile ?? null,
          runAsAdmin: values.runAsAdmin,
          isEnabled: true,
          label: pill.displayTitle || pill.typeTitle || pill.command,
        },
      ];
    });
    setSuggestionPills((current) =>
      current.filter((entry) => entry.command.trim().toLowerCase() !== pill.command.trim().toLowerCase()),
    );
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

  function updateCompanion(index: number, patch: Partial<CompanionFormRow>) {
    companionsCustomizedRef.current = true;
    setCompanions((current) => current.map((row, rowIndex) => (rowIndex === index ? { ...row, ...patch } : row)));
  }

  function addCompanionRow(presetId?: string) {
    companionsCustomizedRef.current = true;
    if (companions.length >= VALIDATION_LIMITS.MAX_COMPANIONS) {
      void showToast({
        style: Toast.Style.Failure,
        title: "Companion limit reached",
        message: `A workspace can have at most ${VALIDATION_LIMITS.MAX_COMPANIONS} companions.`,
      });
      return;
    }

    const resolved = presetId ? resolveCompanionPreset(presetId) : null;
    setCompanions((current) => [
      ...current,
      {
        id: createStableId(),
        path: resolved?.path ?? "",
        arguments: resolved?.arguments ?? "{folder}",
        openOnLaunch: true,
      },
    ]);
  }

  function removeCompanionRow(index: number) {
    companionsCustomizedRef.current = true;
    setCompanions((current) => current.filter((_, rowIndex) => rowIndex !== index));
  }

  function applyCompanionPreset(index: number, presetId: string) {
    companionsCustomizedRef.current = true;
    const resolved = resolveCompanionPreset(presetId);
    if (!resolved) {
      void showToast({
        style: Toast.Style.Failure,
        title: "Preset not installed",
        message: "That companion app was not found on this machine.",
      });
      return;
    }
    updateCompanion(index, { path: resolved.path, arguments: resolved.arguments, openOnLaunch: true });
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
      companions,
      devServerUrl: formValues.devServerUrl,
      openDevServerOnLaunch: formValues.openDevServerOnLaunch,
      repoUrl: formValues.repoUrl,
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
      setCompanions([]);
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
      companionsCustomizedRef.current = false;
      setSuggestionPills([]);
      setSuggestionSource(null);
    } catch (error) {
      await showStorageFailure(mode === "create" ? "Create workspace" : "Save workspace", error);
    }
  }

  return (
    <Form
      enableDrafts={enableDrafts}
      actions={
        <ActionPanel>
          <Action.SubmitForm
            title={mode === "create" ? "Create Workspace" : "Save Workspace"}
            icon={Icon.Check}
            onSubmit={handleSubmit}
          />
          <Action title="Add Command" icon={Icon.Plus} onAction={addLaunchRow} />
          <Action title="Add Companion" icon={Icon.AppWindow} onAction={() => addCompanionRow()} />
          {companionPresets.length > 0 ? (
            <ActionPanel.Section title="Add companion preset">
              {companionPresets.map((preset) => (
                <Action
                  key={`add-preset-${preset.id}`}
                  title={preset.title}
                  icon={Icon.AppWindow}
                  onAction={() => addCompanionRow(preset.id)}
                />
              ))}
            </ActionPanel.Section>
          ) : null}
          {unusedSuggestionPills.length > 0 ? (
            <ActionPanel.Section title="Suggestions">
              {unusedSuggestionPills.map((pill) => (
                <Action
                  key={`${pill.taskType}-${pill.command}`}
                  title={pill.displayTitle || pill.typeTitle || pill.command}
                  icon={Icon.LightBulb}
                  onAction={() => applySuggestionPill(pill)}
                />
              ))}
            </ActionPanel.Section>
          ) : null}
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
          {companions.length > 0 && companionPresets.length > 0 ? (
            <ActionPanel.Section title="Apply companion preset">
              {companions.flatMap((companion, index) =>
                companionPresets.map((preset) => (
                  <Action
                    key={`apply-${companion.id}-${preset.id}`}
                    title={`Companion ${index + 1}: ${preset.title}`}
                    icon={Icon.AppWindow}
                    onAction={() => applyCompanionPreset(index, preset.id)}
                  />
                )),
              )}
            </ActionPanel.Section>
          ) : null}
          {companions.length > 0 ? (
            <ActionPanel.Section title="Remove companion">
              {companions.map((companion, index) => (
                <Action
                  key={`remove-companion-${companion.id}`}
                  title={`Remove Companion ${index + 1}`}
                  icon={Icon.Trash}
                  onAction={() => removeCompanionRow(index)}
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
        {...itemProps.name}
        title="Name"
        placeholder="Project name"
        onChange={(value) => {
          nameCustomizedRef.current = true;
          itemProps.name.onChange?.(value);
        }}
      />
      <Form.TextField
        {...itemProps.abbreviation}
        title="Home keyword"
        info="Type this in Open Workspace for a fast match (for example: home, api, fe)."
        placeholder="home"
        onChange={(value) => {
          abbreviationCustomizedRef.current = true;
          itemProps.abbreviation.onChange?.(value);
        }}
      />
      <Form.Dropdown {...itemProps.terminalChoiceId} title="Terminal">
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
      {suggestionSource ? (
        <Form.Description
          title="Command suggestions"
          text={
            suggestionSource === "suggest"
              ? "Seeded from QuickShell.Suggest. Use Actions → Suggestions to apply additional pills."
              : "Seeded from local folder heuristics (Suggest.exe unavailable). Install Suggest beside the extension or set QUICKSHELL_SUGGEST_EXE."
          }
        />
      ) : null}
      <Form.Checkbox {...itemProps.isPinned} label="Favorite" />
      <Form.Checkbox {...itemProps.runAsAdmin} label="Run as administrator" />
      <Form.Separator />
      <Form.TextField {...itemProps.repoUrl} title="Repository URL" placeholder="https://github.com/org/repo" />
      <Form.TextField {...itemProps.devServerUrl} title="Dev Server URL" placeholder="http://localhost:5173" />
      <Form.Checkbox {...itemProps.openDevServerOnLaunch} label="Open dev server link on launch" />
      <Form.Separator />
      {companions.length === 0 ? (
        <Form.Description
          title="Companions"
          text="No companion apps yet. Use Actions → Add Companion or Add companion preset."
        />
      ) : null}
      {companions.map((companion, index) => (
        <Form.TextField
          key={`companion-path-${companion.id}`}
          id={`companion-path-${companion.id}`}
          title={companions.length === 1 ? "Companion App" : `Companion ${index + 1}`}
          value={companion.path}
          onChange={(value) => updateCompanion(index, { path: value })}
          placeholder="C:\\Program Files\\Microsoft VS Code\\Code.exe"
        />
      ))}
      {companions.map((companion, index) => (
        <Form.TextField
          key={`companion-args-${companion.id}`}
          id={`companion-args-${companion.id}`}
          title={companions.length === 1 ? "Companion Arguments" : `Companion ${index + 1} Arguments`}
          value={companion.arguments}
          onChange={(value) => updateCompanion(index, { arguments: value })}
          info="Use {folder} or . for the workspace directory."
          placeholder="{folder}"
        />
      ))}
      {companions.map((companion, index) => (
        <Form.Checkbox
          key={`companion-open-${companion.id}`}
          id={`companion-open-${companion.id}`}
          label={companions.length === 1 ? "Open companion app on launch" : `Open companion ${index + 1} on launch`}
          value={companion.openOnLaunch}
          onChange={(value) => updateCompanion(index, { openOnLaunch: value })}
        />
      ))}
      <Form.Description
        title="Defaults"
        text={`Commands and names auto-fill from the selected folder when possible. Terminals marked "default" use ${TERMINAL_APPLICATION_CHOICES.find((choice) => choice.id === "wt")?.title ?? "Raycast extension preferences"}. Companion apps open before terminals on full workspace launch; the dev server URL opens afterward.`}
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
