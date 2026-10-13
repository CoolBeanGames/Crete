---
name: crete-engine
description: Build the Crete Godot-fork engine in gated steps — tests, then commit, then push, every step.
---

# Crete Engine Workflow

Use for all work on the Crete engine fork (the Godot-based editor), not for the
launcher, mockup, or plans. The fork lives in `engine/` as its own nested git
repo (own `origin`, ignored by the parent repo) so engine commits never mix
with launcher/mockup files.

## Repos and branches

- Engine repo root: `engine/`, forked from `godotengine/godot` branch `4.7`.
- Work on branch `crete`, cut from upstream `4.7`. Never commit engine work in
  the parent repo, and never commit launcher/mockup files in the engine repo.
- Record the upstream SHA each step is based on in its commit message footer
  (`Upstream: godotengine/godot@<sha>`).

## Per-step gate (every step, no exceptions)

1. Implement exactly one plan step.
2. Add or update unit tests for the changed behavior:
   - C++ engine code: doctest cases under `tests/` (or the module's `tests/`),
     following the existing `TEST_CASE` layout. New suite files need an entry
     in the owning `SCsub`.
   - C# tooling/plugins: `dotnet test` coverage in the owning test project.
3. Build with the Crete flags, always including the module set and tests:
   `scons platform=windows module_mono_enabled=yes module_gdscript_enabled=no tests=yes` plus the step's extra flags.
4. Run the focused tests: `bin/<editor>.exe --test --test-case="<pattern>"`.
   The full `--test` run is required before steps that touch shared code.
5. Commit the step alone: one step, one commit, step-scoped files only. Never
   sweep unrelated or parent-repo files into an engine commit.
6. Push the branch to `origin` and confirm the remote accepted it.
7. Report per step: build command + result, test command + pass counts, commit
   SHA, push result. A step is done only when all four exist.

Never start the next step while tests are red or a step is unpushed. Each step
is independently revertible: prefer one commit per step so `git revert` is the
rollback.

## Standing invariants (verify, don't assume)

- GDScript is fully absent: `module_gdscript_enabled=no` in every build, no
  `.gd`/GDScript option in any script dialog, no `GDScript*` in ClassDB.
- C#/.NET builds only: `module_mono_enabled=yes`, glue generated
  (`--generate-mono-glue`), assemblies built (`build_assemblies.py`).
- VS Code is the default IDE: external editor + solution generation point at
  VS Code out of the box; confirm the exact editor-setting keys in-tree.
- Mergeability: prefer SCons flags over file deletion, deletion over patches.
  Deletions of whole modules/platforms are fine; surgical core edits get a
  comment citing the reason and the upstream file version.

## Removals already decided (from the approved plan)

- `module_gdscript_enabled=no` (GDScript gone, not legacy).
- `module_openxr_enabled=no`, delete `modules/openxr`.
- `module_multiplayer_enabled=no` (spawner/synchronizer/replication gone);
  keep core `MultiplayerAPI` + `modules/enet`, and prove an ENet lobby sample
  still runs or narrow the cut to the spawner/synchronizer files.
- Delete `platform/android`, `platform/ios`, `platform/macos` (+ `visionos`);
  keep `windows`, `linuxbsd`, `web`; unregister the removed ones from export UI.
- Delete `editor/asset_library/` and unregister its tab.
- Touchscreen: hide/disable only (`emulate_touch_from_mouse` off, presets out
  of Input Map UI). No core input cuts.
- Groups UI removed; Tags (`_tags` metadata + inspector row + scene lookup)
  replace them. `add_to_group` keeps working for imported projects.
