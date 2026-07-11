## 2024-07-07 - Stream-based JSON parsing in .NET
**Learning:** In .NET `System.Text.Json`, parsing directly from a `Stream` (`JsonDocument.Parse(stream)`) is significantly faster and uses much less memory than reading the entire file into a string first (`JsonDocument.Parse(File.ReadAllText(path))`), because it avoids allocating large UTF-16 strings and allows the parser to work directly with UTF-8 bytes.
**Action:** When working with `System.Text.Json`, always prefer stream-based parsing (e.g. `File.OpenRead`) over string-based parsing when reading from files.
## 2024-05-24 - File.ReadLines over File.ReadAllText
**Learning:** Using `File.ReadLines(path)` with a line-by-line check in a loop is much more efficient than `File.ReadAllText(path).Contains(value)`. It reduces memory footprint from O(file size) to O(line size) and allows for early exit when searching for substrings that don't span multiple lines.
**Action:** Always prefer `File.ReadLines` for searching files for short string values over reading the entire file into a string.
