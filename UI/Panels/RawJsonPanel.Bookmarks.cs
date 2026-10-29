using NMSE.Config;
using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI.Panels;

/// <summary>
/// Named path bookmarks for the Raw JSON Editor. Bookmarks are stored per save or
/// account file and can restore Split view with Isolate Node enabled.
/// </summary>
public partial class RawJsonPanel
{
    private readonly ToolTip _bookmarksToolTip = new();
    private Label _bookmarkLabel = null!;
    private ComboBox _bookmarkSelector = null!;
    private Button _addBookmarkButton = null!;
    private Button _manageBookmarksButton = null!;

    private List<RawJsonBookmark> _bookmarks = [];
    private List<RawJsonBookmark> _unsavedBookmarks = [];
    private string _bookmarkScope = "unsaved";
    private bool _loadingBookmarks;

    /// <summary>Creates the bookmark toolbar row.</summary>
    private Control CreateBookmarksRow()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 2, 0, 2)
        };

        _bookmarkLabel = new Label
        {
            Text = UiStrings.Get("raw_json.bookmarks"),
            AutoSize = true,
            Margin = new Padding(0, 6, 4, 0)
        };

        _bookmarkSelector = new ComboBox
        {
            Width = 300,
            DropDownWidth = 520,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 3, 3, 3)
        };
        _bookmarkSelector.SelectedIndexChanged += OnBookmarkSelected;

        _addBookmarkButton = new Button
        {
            Text = UiStrings.Get("raw_json.bookmark_add"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Enabled = false
        };
        _addBookmarkButton.Click += OnAddBookmark;

        _manageBookmarksButton = new Button
        {
            Text = UiStrings.Get("raw_json.bookmark_manage"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        _manageBookmarksButton.Click += (_, _) => ShowBookmarkManager();

        row.Controls.AddRange([_bookmarkLabel, _bookmarkSelector, _addBookmarkButton, _manageBookmarksButton]);
        _bookmarksToolTip.SetToolTip(_bookmarkSelector, UiStrings.Get("raw_json.bookmark_tooltip"));
        _bookmarksToolTip.SetToolTip(_addBookmarkButton, UiStrings.Get("raw_json.bookmark_tooltip"));
        return row;
    }

    /// <summary>Applies localisation to the bookmark row.</summary>
    private void ApplyBookmarkLocalisation()
    {
        _bookmarkLabel.Text = UiStrings.Get("raw_json.bookmarks");
        _addBookmarkButton.Text = UiStrings.Get("raw_json.bookmark_add");
        _manageBookmarksButton.Text = UiStrings.Get("raw_json.bookmark_manage");
        _bookmarksToolTip.SetToolTip(_bookmarkSelector, UiStrings.Get("raw_json.bookmark_tooltip"));
        _bookmarksToolTip.SetToolTip(_addBookmarkButton, UiStrings.Get("raw_json.bookmark_tooltip"));
        RefreshBookmarkSelector();
    }

    /// <summary>Gets the persistence scope for the currently displayed document.</summary>
    private string GetBookmarkScope()
    {
        if (_isShowingAccount)
        {
            return string.IsNullOrWhiteSpace(_accountFilePath)
                ? "unsaved-account"
                : "account-" + AppConfig.BuildSaveScopeKey(_accountFilePath);
        }

        return string.IsNullOrWhiteSpace(_saveFilePath)
            ? "unsaved-save"
            : "save-" + AppConfig.BuildSaveScopeKey(_saveFilePath);
    }

    private static bool IsPersistedScope(string scope) =>
        !scope.StartsWith("unsaved", StringComparison.Ordinal);

    /// <summary>Reloads bookmarks for the current save/account scope.</summary>
    private void RefreshBookmarks()
    {
        string newScope = GetBookmarkScope();
        if (string.Equals(newScope, _bookmarkScope, StringComparison.Ordinal))
        {
            RefreshBookmarkSelector();
            UpdateBookmarkAddButtonState();
            return;
        }

        if (IsPersistedScope(newScope))
        {
            var loaded = AppConfig.Instance.GetRawJsonBookmarks(newScope);
            if (!IsPersistedScope(_bookmarkScope) && _bookmarks.Count > 0)
            {
                // Preserve bookmarks created before the save path was assigned.
                foreach (var bookmark in _bookmarks)
                {
                    if (RawJsonBookmarksLogic.FindByName(loaded, bookmark.Name) == null)
                        loaded.Add(bookmark.Clone());
                }
                _unsavedBookmarks.Clear();
                AppConfig.Instance.SetRawJsonBookmarks(newScope, loaded);
                AppConfig.Instance.Save();
            }
            _bookmarks = loaded;
        }
        else
        {
            _bookmarks = RawJsonBookmarksLogic.CloneAll(_unsavedBookmarks);
        }

        _bookmarkScope = newScope;
        RefreshBookmarkSelector();
        UpdateBookmarkAddButtonState();
    }

    /// <summary>Persists the current bookmark list, or keeps it in memory when unsaved.</summary>
    private void PersistBookmarks()
    {
        if (IsPersistedScope(_bookmarkScope))
        {
            AppConfig.Instance.SetRawJsonBookmarks(_bookmarkScope, _bookmarks);
            AppConfig.Instance.Save();
            return;
        }

        _unsavedBookmarks = RawJsonBookmarksLogic.CloneAll(_bookmarks);
    }

    /// <summary>Repopulates the bookmark dropdown without triggering navigation.</summary>
    private void RefreshBookmarkSelector()
    {
        if (_bookmarkSelector == null)
            return;

        _loadingBookmarks = true;
        try
        {
            _bookmarkSelector.Items.Clear();
            if (_bookmarks.Count == 0)
            {
                _bookmarkSelector.Items.Add(UiStrings.Get("raw_json.bookmark_empty"));
                _bookmarkSelector.SelectedIndex = -1;
                _bookmarkSelector.Enabled = false;
                return;
            }

            foreach (var bookmark in _bookmarks
                         .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
            {
                _bookmarkSelector.Items.Add(bookmark);
            }
            _bookmarkSelector.Enabled = true;
            _bookmarkSelector.SelectedIndex = -1;
        }
        finally
        {
            _loadingBookmarks = false;
        }
    }

    /// <summary>Enables Add only when a tree/split node is selected.</summary>
    private void UpdateBookmarkAddButtonState()
    {
        if (_addBookmarkButton == null)
            return;
        _addBookmarkButton.Enabled = GetSelectedBookmarkPath() != null;
    }

    /// <summary>
    /// Gets the current tree path and isolation state, or null when there is no
    /// selected bookmarkable node.
    /// </summary>
    private (string[] Path, bool Isolated)? GetSelectedBookmarkPath()
    {
        if (_viewMode == ViewMode.Text)
            return null;

        if (_viewMode == ViewMode.Split && _isolateNode && _isolatedPath is { Count: > 0 })
            return ([.. _isolatedPath], true);

        var tree = ActiveTreeView;
        var node = tree?.SelectedNode;
        if (node == null || node.Parent == null)
            return null;

        var path = GetNodePath(node);
        return path.Count == 0 ? null : ([.. path], false);
    }

    private void OnAddBookmark(object? sender, EventArgs e)
    {
        var selected = GetSelectedBookmarkPath();
        if (selected == null)
            return;

        string initialName = selected.Value.Path[^1];
        string? name = PromptBookmarkName(
            UiStrings.Get("raw_json.bookmark_add_title"), initialName, null);
        if (name == null)
            return;

        _bookmarks.Add(new RawJsonBookmark
        {
            Name = name,
            Path = selected.Value.Path,
            Isolated = selected.Value.Isolated
        });
        PersistBookmarks();
        RefreshBookmarkSelector();
        _statusLabel.Text = UiStrings.Format("raw_json.bookmark_added", name);
        _statusLabel.ForeColor = StatusColor(ThemeManager.Effective, Color.Green, ThemeColors.Dark.SuccessGreen);
    }

    private void OnBookmarkSelected(object? sender, EventArgs e)
    {
        if (_loadingBookmarks)
            return;
        if (_bookmarkSelector.SelectedItem is not RawJsonBookmark bookmark)
            return;
        NavigateToBookmark(bookmark);
    }

    private void NavigateToBookmark(RawJsonBookmark bookmark)
    {
        if (bookmark.Path.Length == 0)
            return;

        if (bookmark.Isolated)
        {
            if (_viewMode != ViewMode.Split)
                ShowSplitView();
            if (!_isolateNode)
                _isolateNodeCheck.Checked = true;
            if (_viewMode == ViewMode.Split)
                _isolatedPath = [.. bookmark.Path];
        }
        else if (_isolateNode)
        {
            _isolateNodeCheck.Checked = false;
        }

        if (!TryNavigateToPath(bookmark.Path))
        {
            _statusLabel.Text = UiStrings.Get("raw_json.bookmark_not_found");
            _statusLabel.ForeColor = StatusColor(ThemeManager.Effective, Color.Red, ThemeColors.Dark.ErrorRed);
        }
    }

    /// <summary>Prompts for a unique bookmark name, returning null on cancel.</summary>
    private string? PromptBookmarkName(string title, string initialName, RawJsonBookmark? ignore)
    {
        while (true)
        {
            using var dialog = new Form
            {
                Text = title,
                Size = new Size(430, 170),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var label = new Label
            {
                Text = UiStrings.Get("raw_json.bookmark_name_label"),
                Location = new Point(10, 18),
                AutoSize = true
            };
            var textBox = new TextBox
            {
                Text = initialName,
                Location = new Point(10, 42),
                Width = 395
            };
            var okButton = new Button
            {
                Text = UiStrings.Get("common.ok"),
                DialogResult = DialogResult.OK,
                Location = new Point(245, 88),
                Width = 75
            };
            var cancelButton = new Button
            {
                Text = UiStrings.Get("common.cancel"),
                DialogResult = DialogResult.Cancel,
                Location = new Point(330, 88),
                Width = 75
            };
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;
            dialog.Controls.AddRange([label, textBox, okButton, cancelButton]);

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return null;

            string name = textBox.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, UiStrings.Get("raw_json.bookmark_name_empty"),
                    title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                initialName = textBox.Text;
                continue;
            }

            bool duplicate = _bookmarks.Any(b =>
                !ReferenceEquals(b, ignore)
                && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                MessageBox.Show(this, UiStrings.Format("raw_json.bookmark_name_duplicate", name),
                    title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                initialName = name;
                continue;
            }

            return name;
        }
    }

    private void ShowBookmarkManager()
    {
        using var dialog = new Form
        {
            Text = UiStrings.Get("raw_json.bookmark_manage_title"),
            Size = new Size(470, 350),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft
        };
        var closeButton = new Button
        {
            Text = UiStrings.Get("common.close"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        var deleteButton = new Button
        {
            Text = UiStrings.Get("common.delete"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Enabled = false
        };
        var renameButton = new Button
        {
            Text = UiStrings.Get("raw_json.bookmark_rename"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Enabled = false
        };
        buttonPanel.Controls.AddRange([closeButton, deleteButton, renameButton]);
        dialog.CancelButton = closeButton;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(list, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);
        dialog.Controls.Add(layout);

        void Populate(string? selectName = null)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var bookmark in _bookmarks)
                    list.Items.Add(bookmark);
            }
            finally
            {
                list.EndUpdate();
            }

            if (selectName != null)
            {
                var match = list.Items.Cast<RawJsonBookmark>().FirstOrDefault(b =>
                    string.Equals(b.Name, selectName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    list.SelectedItem = match;
            }

            bool hasSelection = list.SelectedItem is RawJsonBookmark;
            renameButton.Enabled = hasSelection;
            deleteButton.Enabled = hasSelection;
        }

        list.SelectedIndexChanged += (_, _) =>
        {
            bool hasSelection = list.SelectedItem is RawJsonBookmark;
            renameButton.Enabled = hasSelection;
            deleteButton.Enabled = hasSelection;
        };

        renameButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not RawJsonBookmark bookmark)
                return;
            string? newName = PromptBookmarkName(
                UiStrings.Get("raw_json.bookmark_rename_title"), bookmark.Name, bookmark);
            if (newName == null)
                return;

            bookmark.Name = newName;
            PersistBookmarks();
            Populate(newName);
            _statusLabel.Text = UiStrings.Format("raw_json.bookmark_renamed", newName);
            _statusLabel.ForeColor = StatusColor(ThemeManager.Effective, Color.Green, ThemeColors.Dark.SuccessGreen);
        };

        deleteButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not RawJsonBookmark bookmark)
                return;
            if (MessageBox.Show(this,
                    UiStrings.Format("raw_json.bookmark_delete_confirm", bookmark.Name),
                    UiStrings.Get("raw_json.bookmark_manage_title"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _bookmarks.Remove(bookmark);
            PersistBookmarks();
            Populate();
            _statusLabel.Text = UiStrings.Format("raw_json.bookmark_deleted", bookmark.Name);
            _statusLabel.ForeColor = StatusColor(ThemeManager.Effective, Color.Green, ThemeColors.Dark.SuccessGreen);
        };

        Populate();
        dialog.ShowDialog(this);
        RefreshBookmarkSelector();
        UpdateBookmarkAddButtonState();
    }
}
