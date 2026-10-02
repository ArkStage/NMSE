using NMSE.Core;
using NMSE.Data;
using NMSE.UI.Controls;
using NMSE.UI.Dialogs;

namespace NMSE.UI.Panels;

/// <summary>Collected Knowledge sub-tab: pages and story completers.</summary>
internal sealed class KnowledgeCompletionPanel : CompletionGridPanel
{
    private readonly record struct KnowledgeRow(KnowledgePage? Page, KnowledgeCompleterStatus? Completer);

    private int _pageComplete, _pageTotal, _completerComplete, _completerTotal;

    public KnowledgeCompletionPanel()
    {
        Grid.Columns.Add(CreateCheckColumn());
        Grid.Columns.Add(CreateTextColumn("Group", UiStrings.Get("discovery.col_category"), 16));
        Grid.Columns.Add(CreateTextColumn("Entry", UiStrings.Get("discovery.col_name"), 28));
        var progressColumn = CreateTextColumn("Progress", UiStrings.Get("discovery.col_progress"), 11);
        progressColumn.ReadOnly = false;
        Grid.Columns.Add(progressColumn);
        Grid.Columns.Add(CreateTextColumn("Target", UiStrings.Get("discovery.col_target"), 8));
        Grid.Columns.Add(CreateTextColumn("Status", UiStrings.Get("discovery.col_status"), 14));
        Grid.Columns.Add(CreateTextColumn("Details", UiStrings.Get("discovery.col_details"), 34));
        Grid.CellDoubleClick += OnGridCellDoubleClick;
    }

    /// <summary>Opens the developer commentary editor when its row is double-clicked.</summary>
    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= Grid.Rows.Count) return;
        if (Grid.Rows[e.RowIndex].Tag is not KnowledgeRow info) return;
        if (info.Page == null
            || !string.Equals(info.Page.Id, "jr_devnotes", StringComparison.OrdinalIgnoreCase)) return;
        if (PlayerState == null) return;

        var localisation = (FindForm() as MainFormResources)?.CurrentLocalisation;
        var devPage = CatalogueCompletionLogic.ResolvePage(
            KnowledgeCatalogue.Pages.First(p => p.Id == "jr_devnotes"), Catalogue);
        using var dialog = new DevNotesEditorDialog(PlayerState, localisation, devPage.Slot, devPage.PageIndex);
        dialog.ShowDialog(this);
        if (!dialog.Modified) return;

        RaiseDataModified();
        Reload();
    }

    /// <inheritdoc/>
    public override void ApplyUiLocalisation()
    {
        base.ApplyUiLocalisation();
        if (Grid.Columns["Group"] is DataGridViewColumn group) group.HeaderText = UiStrings.Get("discovery.col_category");
        if (Grid.Columns["Entry"] is DataGridViewColumn entry) entry.HeaderText = UiStrings.Get("discovery.col_name");
        if (Grid.Columns["Progress"] is DataGridViewColumn progress) progress.HeaderText = UiStrings.Get("discovery.col_progress");
        if (Grid.Columns["Target"] is DataGridViewColumn target) target.HeaderText = UiStrings.Get("discovery.col_target");
        if (Grid.Columns["Status"] is DataGridViewColumn status) status.HeaderText = UiStrings.Get("discovery.col_status");
        if (Grid.Columns["Details"] is DataGridViewColumn details) details.HeaderText = UiStrings.Get("discovery.col_details");
    }

    private static string LocalisedStatus(string status) => status switch
    {
        "OK" => UiStrings.Get("discovery.status_complete"),
        "UNDER" => UiStrings.Get("discovery.status_under"),
        "MISSING" => UiStrings.Get("discovery.status_missing"),
        "SKIP" => UiStrings.Get("discovery.status_skip"),
        _ => status,
    };

    protected override void PopulateRows()
    {
        _pageComplete = _pageTotal = _completerComplete = _completerTotal = 0;
        var catalogue = Catalogue;
        var playerState = PlayerState;
        if (catalogue == null || playerState == null) return;

        foreach (var status in CatalogueCompletionLogic.GetKnowledgeStatuses(playerState, catalogue))
        {
            bool wordsPage = status.Page.Recipe == KnowledgeRecipe.Words;
            bool complete = status.Status == "OK";
            int? progress = status.LastSeen;
            int target = CatalogueCompletionLogic.GetPageTarget(status.Page, catalogue);

            // Words/glyphs pages complete through the Words and Glyphs tabs; show their
            // real progress instead of an opaque skip marker.
            if (wordsPage)
            {
                var (have, total) = CatalogueCompletionLogic.GetWordPageCompletion(playerState, catalogue, status.Page);
                complete = total > 0 && have >= total;
                progress = have;
                target = total;
            }

            if (!wordsPage)
            {
                _pageTotal++;
                if (complete) _pageComplete++;
            }

            var row = new DataGridViewRow();
            row.CreateCells(Grid,
                complete,
                status.Page.Category,
                status.Page.Name,
                DisplayValue(progress),
                target > 0 ? target : "-",
                LocalisedStatus(wordsPage ? (complete ? "OK" : "UNDER") : status.Status),
                wordsPage ? "" : FormatDetail(status));
            row.Tag = new KnowledgeRow(status.Page, null);
            Grid.Rows.Add(row);

            // Column-name lookups only resolve once the row belongs to the grid.
            if (wordsPage && Grid.Columns["Done"] is DataGridViewColumn done)
                row.Cells[done.Index].ReadOnly = true;
            ApplyProgressEditability(row);
        }

        foreach (var status in CatalogueCompletionLogic.GetKnowledgeCompleterStatuses(playerState, catalogue.StoryCompleters, catalogue))
        {
            // The Developer Commentary page row covers this completer; it is kept in the
            // logic (for Complete All / Clear All parity) but not shown twice.
            if (status.Kind == KnowledgeCompleterKind.DevNotes) continue;

            _completerTotal++;
            if (status.Complete) _completerComplete++;

            var row = new DataGridViewRow();
            row.CreateCells(Grid,
                status.Complete,
                UiStrings.Get("discovery.knowledge_completers"),
                status.Name,
                DisplayValue(status.Current),
                status.Target > 0 ? status.Target : "-",
                LocalisedStatus(status.Complete ? "OK" : (status.Current is null or 0 ? "MISSING" : "UNDER")),
                status.Detail);
            row.Tag = new KnowledgeRow(null, status);
            Grid.Rows.Add(row);
            ApplyProgressEditability(row);
        }
    }

    private static string DisplayValue(int? value) =>
        value is int v ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";

    /// <summary>Formats a page's detail text, hinting at the commentary editor.</summary>
    private static string FormatDetail(KnowledgePageStatus status) =>
        string.Equals(status.Page.Id, "jr_devnotes", StringComparison.OrdinalIgnoreCase)
            ? status.Detail + " · " + UiStrings.Get("devnotes.row_hint")
            : status.Detail;

    protected override (int Have, int Total) GetCompletion() =>
        (_pageComplete + _completerComplete, _pageTotal + _completerTotal);

    protected override bool ToggleRow(DataGridViewRow row, bool complete)
    {
        if (row.Tag is not KnowledgeRow info || PlayerState == null || Catalogue == null) return false;

        if (info.Page != null)
        {
            return complete
                ? CatalogueCompletionLogic.ApplyKnowledgePage(PlayerState, info.Page, Catalogue.StoryCompleters, Catalogue, includeCompleters: false)
                : CatalogueCompletionLogic.ClearKnowledgePage(PlayerState, info.Page);
        }

        if (info.Completer == null) return false;
        return complete
            ? CatalogueCompletionLogic.ApplyKnowledgeCompleter(PlayerState, info.Completer, Catalogue.StoryCompleters, Catalogue)
            : CatalogueCompletionLogic.ClearKnowledgeCompleter(PlayerState, info.Completer, Catalogue);
    }

    protected override (int Min, int Max)? GetProgressRange(DataGridViewRow row)
    {
        if (row.Tag is not KnowledgeRow info) return null;
        if (info.Page != null)
            return info.Page.Recipe == KnowledgeRecipe.Words
                ? null
                : (0, Math.Max(0, CatalogueCompletionLogic.GetPageTarget(info.Page, Catalogue)));
        if (info.Completer == null) return null;

        return info.Completer.Kind switch
        {
            KnowledgeCompleterKind.Global => (0, Math.Max(0, info.Completer.Target)),
            KnowledgeCompleterKind.BaseComputer => (0, Math.Max(0, info.Completer.Target)),
            KnowledgeCompleterKind.DevNotes => (0, Math.Max(0, info.Completer.Target)),
            KnowledgeCompleterKind.Mission => (0, Math.Max(0, info.Completer.Target)),
            _ => null,
        };
    }

    protected override bool TryApplyProgress(DataGridViewRow row, int value)
    {
        if (row.Tag is not KnowledgeRow info || PlayerState == null) return false;
        if (info.Page != null)
            return CatalogueCompletionLogic.ApplyKnowledgePageProgress(PlayerState, info.Page, value);
        if (info.Completer == null) return false;
        return CatalogueCompletionLogic.ApplyKnowledgeCompleterProgress(PlayerState, info.Completer, value, Catalogue);
    }

    protected override int CompleteAll()
    {
        if (Catalogue == null || PlayerState == null) return 0;

        int changed = CatalogueCompletionLogic.ApplyKnowledgeCompleters(PlayerState, Catalogue.StoryCompleters, Catalogue);

        // The language word pages complete from KnownWordGroups, not SeenStories.
        var (wordsAdded, wordsUpgraded) =
            CatalogueCompletionLogic.AddMissingWordGroups(PlayerState, Catalogue.KnownWordGroups);
        changed += wordsAdded + wordsUpgraded;

        // The Atlas glyphs page completes from the KnownPortalRunes bitfield.
        int runes = Catalogue.KnownPortalRunes;
        if (runes > 0 && CatalogueLogic.LoadGlyphBitfield(PlayerState) != runes)
        {
            CatalogueLogic.SaveGlyphBitfield(PlayerState, runes);
            changed++;
        }

        foreach (var page in KnowledgeCatalogue.Pages)
        {
            if (page.Recipe == KnowledgeRecipe.Words) continue;
            if (CatalogueCompletionLogic.ApplyKnowledgePage(PlayerState, page, Catalogue.StoryCompleters, Catalogue, includeCompleters: false))
                changed++;
        }

        foreach (var status in CatalogueCompletionLogic.GetKnowledgeCompleterStatuses(PlayerState, Catalogue.StoryCompleters, Catalogue))
        {
            if (status.Complete) continue;
            if (CatalogueCompletionLogic.ApplyKnowledgeCompleter(PlayerState, status, Catalogue.StoryCompleters, Catalogue)) changed++;
        }
        return changed;
    }

    protected override int ClearAll()
    {
        if (Catalogue == null || PlayerState == null) return 0;

        int changed = 0;
        foreach (var page in KnowledgeCatalogue.Pages)
        {
            if (page.Recipe == KnowledgeRecipe.Words) continue;
            if (CatalogueCompletionLogic.ClearKnowledgePage(PlayerState, page)) changed++;
        }

        foreach (var status in CatalogueCompletionLogic.GetKnowledgeCompleterStatuses(PlayerState, Catalogue.StoryCompleters, Catalogue))
        {
            if (!status.Complete) continue;
            if (CatalogueCompletionLogic.ClearKnowledgeCompleter(PlayerState, status, Catalogue)) changed++;
        }
        return changed;
    }
}
