export type OpenWorkspaceLaunchContext = {
  focusWorkspaceId?: string;
  focusWorkspaceName?: string;
};

export function resolveOpenWorkspaceSearchSeed(
  fallbackText?: string,
  launchContext?: OpenWorkspaceLaunchContext,
): string {
  return (
    fallbackText?.trim() || launchContext?.focusWorkspaceName?.trim() || launchContext?.focusWorkspaceId?.trim() || ""
  );
}
