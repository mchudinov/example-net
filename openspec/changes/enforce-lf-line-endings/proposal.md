## Why

Nothing in this repository decides line endings. There is no `.gitattributes`. Git for Windows' system configuration sets `core.autocrlf=true`, and the global configuration of the machine that commits overrides it with `false`, so Git converts nothing. All 136 text files are LF today, in the index and on disk, and no commit on any branch or tag has ever stored a CR. That holds only because every writer so far happened to save LF.

EuGo-mcp shows what happens when that stops being true. It is committed from the same machine with the same settings. Its commit `287bf27` turned the repository to CRLF. Its chart check then failed on every run for two months, because anchored `grep … $` checks never matched the CRLF render, and its release would have read the chart version as `0.1.1\r`. Its change `normalize-line-endings-to-lf` puts the rule into that repository. This change puts the same rule into example-net. The other repositories will adopt it as well, and they copy example-net.

example-net already works around CR in one place: `check-chart-labels.sh` strips it with `tr -d '\r'` before it reads the chart. That hides the symptom. A chart with CRLF would pass the check and be published.

## What Changes

- **`.gitattributes`** at the root: `* text=auto eol=lf`, plus the binary types a web project plausibly adds, marked `binary`. Git stores every text file with LF and checks out LF on every platform, whatever the local Git settings say.
- **`.editorconfig`** at the root with one rule, `end_of_line = lf`, so that editors save LF in the first place.
- Both files carry the same rules as EuGo-mcp's, line for line, so every repository has the same rule. Only the comments differ, and they name this repository's check and spec.
- **A `Check-Line-Endings` workflow.** It fails when a committed text file has CRLF line endings, and names each such file. It runs on every push and pull request to `main`, with no path filter, and on manual runs.
- **Release calls it as a third gate,** next to `Test` and `Check-Helm`. The image push waits for all three.
- **`check-chart-labels.sh` stops stripping CR.**
- **Documentation.**
  - `CLAUDE.md` states the rule and describes the new workflow and gate.
  - `docs/ci-cd-best-practices.md` gets a Repository section for the rule. The items that the new workflow and gate change are updated, and the page's line references into `release.yaml` follow the new job.

Nothing is renormalized. No tracked file changes its bytes, and no `.git-blame-ignore-revs` is needed.

**Out of scope:**
- Any other editor rule, such as charset, final newline, indentation or trailing whitespace. Six `.cs` files start with a UTF-8 BOM and fourteen files have no final newline. Both stay as they are.
- The gaps that `docs/ci-cd-best-practices.md` lists for every workflow, such as `concurrency` groups and workflow linting in CI.
- Hardening other scripts against CR. The repository rule and the check make that redundant.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`:
  - Adds a requirement. Every text file is stored and checked out with LF. Every push and pull request to `main` runs a line-ending check that names each text file stored with CRLF. A release run runs the same check before it pushes the image, and publishes nothing if the check fails.
  - "Releases wait for passing tests": the scenario "Passing tests release as before" also requires the line-ending check to pass. Passing tests and chart checks are no longer enough for a release to publish.

## Impact

- **Repository:** two new files at the root. No tracked file changes its bytes.
- **CI:**
  - new `.github/workflows/check-line-endings.yaml`;
  - `.github/workflows/release.yaml` gains a job, and `Build-and-Push-Docker` gains a third `needs` entry;
  - `.github/scripts/check-chart-labels.sh` loses its two `tr -d '\r'`.
- **Docs:** `CLAUDE.md` and `docs/ci-cd-best-practices.md`.
- **Unchanged:**
  - the application code, the tests, `Dockerfile` and the chart;
  - what a release publishes, and how it signs it.

  With no C# or project file changes, all the work stays in the main session.
- **Cost:** every push and pull request to `main` runs one more workflow, made of a checkout and one `git` command.
- **When it takes effect:**
  - `.gitattributes` applies in every clone from the next `git add` after it arrives.
  - `Check-Line-Endings` first runs on the push that adds it. `Build-Helm` runs on that push too, because the label script changes.
  - The release gate first runs on the next `v*` tag.
- **Other clones:** existing working copies are not rewritten, because no stored file changes. This machine's copy is LF. A clone checked out with `core.autocrlf=true` keeps CRLF on disk until it is checked out again. See design.md.
