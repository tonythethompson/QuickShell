# ltm/ — Local Long-Term Memory

Project-local memory managed by ltm-power.

## Commit policy: repo-portable tooling, local-private memory

**Commit:** `ltm/bin/ltm.py`, `ltm/bin/run-ltm.py`, `ltm/config.json`, `ltm/manifest.json`, this README.
**Do NOT commit:** `ltm/store/`, `ltm/runtime/`, `ltm/reports/`, `ltm/snapshots/`.

If the hook uses an absolute path, review `.kiro/hooks/ltm-postturn-capture.kiro.hook` before committing.

The post-turn hook calls `ltm/bin/run-ltm.py`, which reads `python_cmd` from config before invoking `ltm/bin/ltm.py`. If your bootstrap Python is not `python`, edit the hook command accordingly (for example `py` or `python3`) and update `python_cmd` in `ltm/config.json` to match.

## Commands

Use `python_cmd` from `ltm/config.json` (default: `python`).

- `python ltm/bin/ltm.py files --limit 10`
- `python ltm/bin/ltm.py health`
- `python ltm/bin/ltm.py checkpoint --summary "..."`
- `python ltm/bin/ltm.py validate`
- `python ltm/bin/ltm.py repair`
- `python ltm/bin/ltm.py purge-last --confirm`
- `python ltm/bin/ltm.py purge-all --confirm`
- `python ltm/bin/ltm.py teardown --confirm`
