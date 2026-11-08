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
List tests cover contiguous/noncontiguous moves, insertion boundaries and previews,
case-insensitive deduplication, true load-order positions through filtering and
missing entries, pane selection restoration, two-instance undo/redo, and missing
undo targets.
Launch tests inspect arguments without launching RimWorld; installer tests use
temporary mod/config files without downloading anything.
Catalog checks use an injected HTTP handler to cover offline bundled startup,
ETag/304 and Last-Modified checks, changed hashes, persistent and corrupt caches,
source isolation, mirror-relative URLs, cancellation, and unwritable cache paths.
They also verify startup and window refreshes share one request while each caller
can cancel independently, and that saved source choices are preserved.
Pack-detail checks cover revisits without another fetch, restart persistence,
mirror aliases, mutable-model isolation, changed hashes, and failed/corrupt reads.
Failure checks cover damaged-profile isolation, stale pending edits, installation
rollback and staging-path rejection, recovery across damaged manifests, mutable
color snapshots and failed color writes. Linux/macOS CI additionally constructs
a tar.gz and checks executable permissions through the actual SteamCMD extractor.
Workshop checks cover bounded Steam KeyValues parsing, passive manifest/log observations,
unavailable records, deferred/bounded completion rescans, saved item/package mappings,
restart recovery, and preserving pending edits and unrelated instance metadata.
These checks use synthetic state and temporary files; they do not contact Steam.

Workshop checks cover installed-manifest changes completed between polls,
same-second log/ACF timestamps, and secondary-library discovery using Linux
and macOS client roots. Selection checks repeatedly replace all three panes'
models while retaining selected package IDs, range anchors and displayed details.

Manual platform checks:

- On Windows x64 with Steam running, install a pack whose local download finishes
  before its Workshop items. Queued/downloading items must not count as Missing;
  the status dialog updates while open. Verify Steam keeps downloading while
  PackFrame checks status and does not mark RimWorld as running. Preallocated file
  sizes must not appear as download percentages. Verify the lists refresh after
  each item finishes without pressing Refresh.
  Keep unsaved order edits, selections, searches and scroll positions in both panes
  during completion; they must remain intact. Reopen PackFrame during a download
  and confirm its saved Workshop targets are still monitored.
- Leave monitoring idle, then install a pack with a new Workshop item. Finishing
  the catalogue load must start a fresh Steam query without waiting for the idle
  30-second interval; subsequent pending checks return to four-second polling.
- Pause Steam downloads and resume them; incomplete items remain pending. Make
  Steam's manifest/log records unavailable: uncertain Workshop items show Status unavailable,
  while known missing local/GitHub mods remain Missing. Restart Steam and verify
  monitoring recovers. SteamCMD instances must keep their existing download flow.
- A completed Workshop item with unreadable metadata gets a bounded rescan retry;
  persistent invalid metadata becomes Missing rather than refreshing indefinitely.

- App update checks must only offer published release information. Opening the
  offer must not download, close or restart PackFrame. Download, Later and Install
  and restart are separate choices; closing/cancelling during download keeps the
  running installation intact. A deferred verified download should be reused.
- Cancelling unsaved changes before update installation keeps PackFrame open.
  Only an explicit install followed by the main window's actual close may commit
  the helper. Verify update success/failure receipts using an isolated installation,
  and preserve its settings, instances, saves and mods. Never test replacement
  against a player's running installation.
- The unsaved-changes prompt should fit its wrapped message and buttons without
  the fixed blank area below. Escape cancels; Tab reaches each action.

- Open Modpacks with no saved catalog and a slow/offline connection: the three
  bundled entries should appear immediately and remain selectable during the
  background check. Verify the source choice persists after reopening.
- Change packs or repository sources while details are loading: stale responses
  must not overwrite the new selection. Close during a check, reopen, and test a
  custom catalog without a cache; default bundled entries must never appear for it.
- Let pack A finish loading, select B, and return to A: saved details should appear
  immediately without another request or loading message. Reopen and check that
  saved details remain visible during background validation; Refresh must force
  a new read. A first visit still needs the pack's details from the server.
- Start an install, config apply, or pack binding while a background catalog
  update is pending: it must retain the selected pack until that operation ends.
- With cached pack details, install/apply must read a fresh manifest. If its
  Workshop collection changed, an auto-filled link must follow the fresh pack;
  explicitly edited links remain overrides. Refresh-and-resume must retain the
  original installation target while a newer catalog waits in the background.
- Start with settings that have no saved layout: only one enabled pane appears.
  Select several mods, click its header's "Open on right" action, and confirm the
  same instance opens on the right with search focused and the left selection intact.
- Select the same instance in both enabled panes: both show "Same list", with
  independent search and scroll positions. Select different instances (including
  instances with identical display names): the badges disappear. Change language
  and confirm the badges and launch target are translated.
- Drag within a list or between views of the same instance: the destination is
  outlined, the ghost names the mod before/after the insertion point, and the
  insertion line follows the drop position. A drop that changes nothing says
  "Already here". Drag between different instances: it says "Add to <name>",
  "Reorder in <name>", or "Add and reorder in <name>" depending on existing mods,
  and leaves the source intact. Drag to the inactive pane: it says "Disable in <name>".
  Leaving a valid list, releasing the pointer, or losing pointer capture must
  clear the destination outline and action label.
- Click either enabled pane and change its instance: its header is highlighted
  and the launch button names that target. Check long names at the minimum window width; the complete launch name
  remains available in its tooltip and accessible name.
- Drag a contiguous selection within itself, move a noncontiguous selection to
  the beginning/end, drag between two profiles, and copy into a profile that
  already contains an item. Check the selection after each operation.
- Undo and redo a cross-profile move and a color change. Check Ctrl+Z, Ctrl+Y,
  Ctrl+Shift+Z and the toolbar buttons; new edits must clear redo history.
- Keep testing Shift/Ctrl selection, double-click enable/disable, and drag
  insertion with a search filter. These event/coordinate paths remain in the UI.

- Search quickly in each column: only that column should rebuild after 180ms;
  other columns retain selection and scroll position without rebuilding. Visible
  selected rows in the searched column remain selected; hidden selections are
  cleared safely. Details and keyboard actions
  must target the searched column; zero results must clear details. Switch the target instance and search
  again to check the inactive list uses the new target.
- Select mods rapidly: the preview must match the latest selection, including
  switching to a mod without an image. Close while an image is loading.

- Select several mods in the left enabled pane, search in the right pane, then
  drag the original selection to a distant destination. Repeat with different
  instances. Extend and shrink a range with repeated Shift+Down/Up, then visit the
  other pane and return. Check Shift/Ctrl anchors survive and Delete,
  double-click, color actions and context menus affect only their current pane.
- Filter an enabled list and verify each number stays its full load-order position,
  including gaps for missing entries. Reorder and undo: both views must renumber
  consistently without resetting their independent positions. Gutter width must
  remain stable during filtering, including three/four-digit orders. Status badges
  stay at the right edge; hover them to check the same details and actions.
- All three pane footers show L: <local count>, including zero. Like
  S/V/R, the count follows search/source filters but precedes footer filters.
  Switch each instance, change search/source filters, hide errors, and clear the
  catalogue: L must refresh without changing enabled mods or their order.
  Click L in each pane: strikethrough excludes local mods, underline shows only
  local mods, and the third click clears the filter. The three panes filter
  independently; combining L with S/V/R uses all selected conditions. L keeps
  its pre-filter count even when the visible list becomes empty.
- Hover an error badge or bottom problem summary: it opens after 300ms, not on
  entry. Leaving before 300ms, switching badges, clicking, scrolling, or closing
  the pane/window must cancel the pending opening. Leaving an open popup closes
  it after 100ms; moving into its content keeps copy, text selection, and Apply
  usable. Move between text, column gaps, blank space, and the outer padding:
  all of the visible popup must keep it open, including version/duplicate summaries.
  Click its text or padding, then drag to select text: the popup must stay open
  and the selected span must visibly highlight, including after mouse release.
  A nonempty SelectedText value alone does not verify the selection is visible.
  Popup interaction must not change the summary filter or list selection. Check
  Ctrl+C and the badge copy menu; Apply must still work, and clicking the mod list
  must still close it.
  A copy menu opened from the popup must stay usable beyond the 100ms close delay.
  After clicking or selecting text, move below the whole popup: retained keyboard
  focus must not stop its 100ms dismissal. Drag outside while holding the mouse:
  keep the popup open until release, then resume dismissal. Close a copy menu with
  the pointer outside the popup: dismissal must resume without another mouse move.
  Color-label, launch-target, and damaged-instance tooltips are unchanged.
- Close the second enabled pane using its header control while it is the
  enable/launch target: the target and header highlight must return to the visible
  pane. Open the same instance on the right again: its search and scroll position
  remain intact. Open a different left instance on the right: the previous right
  search is cleared. Reopen the app and check the saved layout preference.
  Hidden panes must not receive keyboard actions or drag targets.
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
