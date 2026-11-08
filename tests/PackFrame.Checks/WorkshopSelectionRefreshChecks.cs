using Avalonia.Controls;
using PackFrame;

internal static class WorkshopSelectionRefreshChecks
{
    public static void Run()
    {
        var alpha = Mod("alpha");
        var beta = Mod("beta");
        var gamma = Mod("gamma");
        var inactive = Pane(alpha, beta);
        var first = Pane(alpha with { }, beta with { });
        var second = Pane(beta with { }, gamma with { });
        inactive.SelectedItems!.Add(inactive.Items[0]);
        first.SelectedItems!.Add(first.Items[0]);
        first.SelectedItems.Add(first.Items[1]);
        second.SelectedItems!.Add(second.Items[1]);
        var panes = new[] { inactive, first, second };
        var anchors = new[] { "alpha", "beta", "gamma" };
        var expectedIds = new[] { new[] { "alpha" }, new[] { "alpha", "beta" }, new[] { "gamma" } };

        // Automatic scans replace every catalog model, including selected rows.
        // Repeat to catch selections or anchors surviving only the first scan.
        for (var scan = 0; scan < 3; scan++)
        {
            for (var paneIndex = 0; paneIndex < panes.Length; paneIndex++)
            {
                var pane = panes[paneIndex];
                var scope = "instance-" + paneIndex;
                var snapshot = PaneSelectionSnapshot.Capture(pane, scope, anchors[paneIndex]);
                var rows = new[] { gamma with { Name = "new gamma" }, beta with { Name = "new beta" }, alpha with { Name = "new alpha" } };
                var anchor = snapshot.Restore(pane, rows, scope, selectFirst: false);
                Require(pane.SelectedItems!.Cast<ModInfo>().Select(mod => mod.PackageId).Order()
                        .SequenceEqual(expectedIds[paneIndex].Order()) &&
                        pane.SelectedItems!.Cast<ModInfo>().All(mod => rows.Any(row => ReferenceEquals(row, mod))) &&
                        rows[anchor].PackageId == anchors[paneIndex],
                    $"Workshop scan {scan + 1} restores pane {paneIndex + 1} selections and anchor onto fresh models");
            }
            var details = PaneSelectionSnapshot.ResolveDetails(first, "ALPHA");
            Require(details?.PackageId == "alpha" && details.Name == "new alpha" &&
                    !ReferenceEquals(details, first.SelectedItem),
                "Workshop refresh keeps the displayed multi-selection member rather than switching to SelectedItem");
        }

        var state = PaneSelectionSnapshot.Capture(first, "instance-1", "alpha");
        var remaining = beta with { Name = "remaining beta" };
        state.Restore(first, new[] { remaining }, "instance-1", selectFirst: false);
        Require(ReferenceEquals(PaneSelectionSnapshot.ResolveDetails(first, "alpha"), remaining),
            "Workshop refresh falls back to a surviving selected row if the displayed row disappears");
        state = PaneSelectionSnapshot.Capture(first, "instance-1", "beta");
        state.Restore(first, new[] { gamma with { } }, "instance-1", selectFirst: false);
        Require(first.SelectedItems!.Count == 0 && PaneSelectionSnapshot.ResolveDetails(first, "beta") is null,
            "Workshop refresh clears stale details when every selected row disappears");
        Require(PaneSelectionSnapshot.ResolveDetails(null, "alpha") is null,
            "Workshop refresh does not invent an active pane or selection");
    }

    private static ListBox Pane(params ModInfo[] mods) => new()
    {
        SelectionMode = SelectionMode.Multiple,
        ItemsSource = mods
    };

    private static ModInfo Mod(string id) => new(id, "Test", id, id, string.Empty,
        null, ModSource.Local, "1.7", DateTime.MinValue);

    private static void Require(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
