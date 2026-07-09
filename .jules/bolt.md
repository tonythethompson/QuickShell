## 2024-07-07 - Stream-based JSON parsing in .NET
**Learning:** In .NET `System.Text.Json`, parsing directly from a `Stream` (`JsonDocument.Parse(stream)`) is significantly faster and uses much less memory than reading the entire file into a string first (`JsonDocument.Parse(File.ReadAllText(path))`), because it avoids allocating large UTF-16 strings and allows the parser to work directly with UTF-8 bytes.
**Action:** When working with `System.Text.Json`, always prefer stream-based parsing (e.g. `File.OpenRead`) over string-based parsing when reading from files.

## 2024-07-09 - Avoid fully loading large files for simple checks
**Learning:** Checking for substrings or configurations (like `<OutputType>`) within large files using `File.ReadAllText(path).Contains()` results in massive memory overhead and slow execution by reading the entire file into a string at once.
**Action:** When checking if a file contains a specific text, prefer streaming the file line-by-line using `File.ReadLines(path)`, exiting early once a match is found. This avoids full file allocation in memory.
