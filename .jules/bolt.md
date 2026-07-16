## 2024-07-07 - Stream-based JSON parsing in .NET
**Learning:** In .NET `System.Text.Json`, parsing directly from a `Stream` (`JsonDocument.Parse(stream)`) is significantly faster and uses much less memory than reading the entire file into a string first (`JsonDocument.Parse(File.ReadAllText(path))`), because it avoids allocating large UTF-16 strings and allows the parser to work directly with UTF-8 bytes.
**Action:** When working with `System.Text.Json`, always prefer stream-based parsing (e.g. `File.OpenRead`) over string-based parsing when reading from files.

## 2024-07-16 - Lazy File Line Iteration
**Learning:** `File.ReadAllLines(path)` reads the entire file into memory as a `string[]` array. For simple line-by-line scanning and sequential iteration, `File.ReadLines(path)` yields an `IEnumerable<string>` lazily, reducing memory allocation and enabling early termination.
**Action:** Default to `File.ReadLines(path)` when iterating files line-by-line sequentially without needing indexing or a complete array immediately.