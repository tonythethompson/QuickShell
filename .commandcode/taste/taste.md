# Taste (Continuously Learned by [CommandCode][cmd])

[cmd]: https://commandcode.ai/


# glyph-naming
- Use `*Label` suffix for glyph constants that are display strings (e.g., `RefreshLabel`, `BrowseLabel`, `PasteLabel`), reserving bare action verbs for command/event identifiers. Confidence: 0.90
- Prefer descriptive, unabbreviated glyph/icon constant names over short action verbs (e.g., `RemoveLabel` over `Remove`, `BrowseLabel` over `FolderOpen`). Confidence: 0.85

# test-naming
- Name test methods with explicit domain terms — include "Placeholder" when testing placeholder behavior, not just "Blank". Confidence: 0.80

# assertion-style
- Use typed assertions (`Assert.True`/`Assert.False`) over string-comparison assertions (`Assert.Equal("true", ...)`) for boolean checks. Confidence: 0.85
- When calling functions in assertions, prefer extracting and type-checking the function reference first to satisfy static analysis (avoid direct invocation inside assertion args). Confidence: 0.70

# file-paths
- Use `Path.Join` over `Path.Combine` to avoid silent argument dropping with earlier path components. Confidence: 0.75

# placeholder-modeling
- Use an explicit `IsEditorPlaceholder` property on row/draft models rather than inferring placeholder state from empty command strings. Confidence: 0.80

# quoting
- Use explicit literal `'"'` for quote character comparison instead of a variable alias like `quoteChar`. Confidence: 0.75

# file-naming
- Use uppercase `CHANGELOG.md` instead of lowercase `changelog.md` for version history files. Confidence: 0.70

# codeql-safety
- Prefer `spawnSync` from `node:child_process` over `existsSync` from `node:fs` when the intent is running a CLI verification script. Confidence: 0.70

# import-style
- Collapse multi-line import destructuring into single-line when the import names are short (e.g., `import { deriveAbbreviationFromName, deriveNameFromDirectory }` over a multi-line spread). Confidence: 0.70

# graphql-api
- Use `gh api graphql --input <jsonfile>` (query/mutation stored in a separate JSON file) instead of inline `-f query=` to avoid shell quote-stripping issues with nested quotes. Confidence: 0.75

# skills-directory
- Store reusable skills globally at `C:\Users\tonyt\.agents\skills` for cross-repo availability, not inside a repo's `.github/skills/`. Confidence: 0.75

