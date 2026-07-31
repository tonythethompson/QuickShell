## 2024-05-24 - WslPathResolver string splitting
**Learning:** `WslPathResolver.TryParseUncRemainder` used `string.Split()`, `string.Join()`, and `Enumerable.Skip()` which leads to unnecessary GC pressure and multiple string/array allocations for hot path code.
**Action:** Replace `string.Split` with `ReadOnlySpan<char>.Split` and use `StringBuilder` to append portions sequentially when parsing and reconstructing path parts. This saves multiple array/string allocations and makes parsing faster.
