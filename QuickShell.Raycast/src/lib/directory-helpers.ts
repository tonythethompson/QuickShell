export function deriveNameFromDirectory(directory: string): string {
  const trimmed = directory.trim().replace(/[\\/]+$/, "");
  if (!trimmed) {
    return "";
  }
  const segments = trimmed.split(/[\\/]/).filter(Boolean);
  if (segments.length === 0) {
    return trimmed;
  }
  return segments[segments.length - 1] ?? trimmed;
}

export function deriveAbbreviationFromName(name: string): string {
  const normalized = name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return normalized.slice(0, 32);
}
