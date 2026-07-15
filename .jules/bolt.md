## 2024-07-07 - Stream-based JSON parsing in .NET
**Learning:** In .NET `System.Text.Json`, parsing directly from a `Stream` (`JsonDocument.Parse(stream)`) is significantly faster and uses much less memory than reading the entire file into a string first (`JsonDocument.Parse(File.ReadAllText(path))`), because it avoids allocating large UTF-16 strings and allows the parser to work directly with UTF-8 bytes.
**Action:** When working with `System.Text.Json`, always prefer stream-based parsing (e.g. `File.OpenRead`) over string-based parsing when reading from files.
## 2026-07-15 - File Scanning Allocation Overhead
**Learning:** Using `File.ReadAllText(path).Contains(...)` creates a large single string allocation for the entire file content before performing the search, increasing memory pressure, especially on larger files like csproj or config files.
**Action:** Use `File.ReadLines(path).Any(...)` which lazily reads line-by-line and exits early, keeping memory low and speeding up the scan.
