---
name: architecture-check
description: Check a Stabby diff against the eight CLAUDE.md rules before committing or reviewing. Use on every change to src/, tools/, tests/ or content/, and when asked to review a branch or diff.
---

# Architecture check

Read `CLAUDE.md`, then the diff (`git diff`, or the files named). Report each
broken rule as `file:line - rule N - fix`. Say "clean" if none.

1. **World purity.** `src/Stabby.World` has no `UnityEngine`, `System.IO`,
   `Console` or `System.Text.Json`; no records, no `init`, nothing above
   C# 9 / netstandard2.1. `WorldLibraryHasNoEngineOrFileDependencies` guards it.
2. **One representation.** Height, water surface and rivers come from
   `Drainage`. Flag any second flow-direction, lake or river array.
3. **Content only via Stabby.Content.** New JSON goes in `content/`, a schema in
   `content/schemas/`, a loader in `Stabby.Content`. World code takes typed data.
4. **Checks in WorldChecks.** A new invariant or bug fix comes with a check in
   `WorldChecks.Run`, not an ad hoc test assert or CLI-only test.
5. **Randomness.** Only `Rng.Stream(seed, "name")`, a new name per purpose, never
   reusing or reordering an existing stream. No `System.Random`. No
   `Math.Sin/Cos/Atan2` etc. in generation; no new `Math.Pow/Exp`.
6. **Version.** Anything that changes generated output bumps
   `WorldConfig.GeneratorVersion`. Changes to output with no bump = broken.
7. **Comments.** Only reason lines (why, not what). Flag narration, banners,
   commented-out code; trim in touched functions.
8. **LF.** `git ls-files --eol <files>` shows `w/lf` for touched text files.

Also flag: CLI or tests calling generation internals instead of
`WorldGenerator`/`WorldChecks`, and new options missing from README usage.
