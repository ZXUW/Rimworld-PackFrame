# PackFrame regression checks

Run from the repository root (close the development app first):

```powershell
dotnet run --project tests/PackFrame.Checks -c Release
```

The checks use temporary instances and remove them afterward. They cover metadata
preservation, saving missing package IDs, interrupted atomic writes, successful
config commits, and rollback of overwritten/new files on commit failure.
They also exercise actual mod links (Windows junctions / Unix symbolic links),
cross-instance recovery, broken-link cleanup, ownership checks, preparation
rollback, color-cache invalidation, and corrupt-configuration diagnostics.
The tests call the application's internal implementations through
`InternalsVisibleTo`, rather than compiling separate copies of production files.
List tests cover contiguous/noncontiguous moves, insertion boundaries,
case-insensitive deduplication, two-instance undo/redo, and missing undo targets.
Launch tests inspect arguments without launching RimWorld; installer tests use
temporary mod/config files without downloading anything.
Failure checks cover damaged-profile isolation, stale pending edits, installation
rollback and staging-path rejection, recovery across damaged manifests, mutable
color snapshots and failed color writes. Linux/macOS CI additionally constructs
a tar.gz and checks executable permissions through the actual SteamCMD extractor.

Manual platform checks:

- Drag a contiguous selection within itself, move a noncontiguous selection to
  the beginning/end, drag between two profiles, and copy into a profile that
  already contains an item. Check the selection after each operation.
- Undo and redo a cross-profile move and a color change. Check Ctrl+Z, Ctrl+Y,
  Ctrl+Shift+Z and the toolbar buttons; new edits must clear redo history.
- Keep testing Shift/Ctrl selection, double-click enable/disable, and drag
  insertion with a search filter. These event/coordinate paths remain in the UI.

- Search quickly in each column: only that column should rebuild after 180ms;
  other columns clear selection without rebuilding. Details and keyboard actions
  must target the searched column; zero results must clear details. Switch the target instance and search
  again to check the inactive list uses the new target.
- Select mods rapidly: the preview must match the latest selection, including
  switching to a mod without an image. Close while an image is loading.
- Errors are recorded in `app/PackFrame/bin/Release/net10.0/packframe.log` in the
  local launcher layout, with one rotated `.1` file after 2 MB.

- With unsaved list edits, refresh and close must offer Save / Discard / Cancel.
  Cancel must leave the edits in place; failed saving must not close the window.
- Interrupt a configuration download: existing configuration files must remain
  unchanged. Malformed XML must be rejected before applying any file.
- On macOS, launch a profile containing local mods: its links must remain until
  the game exits (`open -W -n` waits for the new app instance).
- On Linux/macOS, test first-run SteamCMD installation without a preinstalled
  copy. The platform tar.gz must be extracted, including executable permissions.
  SteamCMD's native runtime prerequisites still apply; Apple Silicon may require
  Rosetta for Valve's Intel binaries.
