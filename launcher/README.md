# CreteLauncher (MVP)

Installs and launches engine versions side by side. Two channels:

- `godot` — live. Pulls stable releases from `godotengine/godot` on GitHub.
- `crete` — activates once Crete publishes GitHub releases. Point it with
  `CRETE_REPO=owner/repo` or `channels.json` in the data dir.

Installs land in `%LOCALAPPDATA%\Crete\Launcher\versions\<id>\`.

## Run the GUI

```
dotnet run --project launcher/CreteLauncher
```

No args = window. Three views, same dark Hub styling as
`crete-hub-mockup.html`:

- **Projects** — scanned `project.godot` cards with search, filter chips
  (All / ★ Favorites / 4.x / 3.x / C#), grid + list views, star favorites
  (persisted), and a per-card engine picker that remembers each project's
  target install. Open launches that engine with that project.
- **New Project** — template cards + settings card (name, location,
  renderer, **target engine install**); creates a real `project.godot`
  on disk and records the engine mapping.
- **Installs** — installed versions as rows with Launch/Verify, plus an
  "Add engine version" panel: godot/crete channels, Standard/.NET flavor,
  per-version Install or Install latest stable.

## Headless (same operations, scriptable)

```
CreteLauncher.exe list-remote godot
CreteLauncher.exe install godot [--version TAG] [--dotnet]
CreteLauncher.exe list
CreteLauncher.exe verify <install-id>
CreteLauncher.exe launch <install-id> [--project PATH]
CreteLauncher.exe projects [DIR]
```

## Tests

```
dotnet test launcher/Crete.slnx
```

Covers asset selection, latest-stable picking (highest version, not newest
date — Godot backports 3.x after 4.x majors), install-id rules, and the
piped-process capture used by `verify`.
