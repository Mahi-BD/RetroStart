using System.Windows;
using WinlyStart.Core;

namespace WinlyStart.UI;

/// <summary>Rows of the virtualised A–Z app list. Each subclass has its own implicit DataTemplate.</summary>
public abstract class ListRow
{
    public abstract bool Focusable { get; }
}

/// <summary>"Recently added" / "Most used" section titles and the A–Z letter headers.</summary>
public sealed class HeaderRow : ListRow
{
    public required string Text { get; init; }
    public bool IsLetter { get; init; }
    public override bool Focusable => false;
}

public sealed class AppRow : ListRow
{
    public required AppEntry App { get; init; }
    public int Indent { get; init; }
    public bool IsPinned { get; init; }
    public string PinText => IsPinned ? "Unpin from Start" : "Pin to Start";
    public bool CanRunAsAdmin => !App.IsPackaged;
    public Thickness IndentMargin => new(12 + Indent * 24, 0, 12, 0);
    public override bool Focusable => true;
}

public sealed class FolderRow : ListRow
{
    public required string Name { get; init; }
    public bool IsOpen { get; init; }
    public string Chevron => IsOpen ? "" : "";
    public override bool Focusable => true;
}

/// <summary>"Expand" / "Collapse" under "Recently added".</summary>
public sealed class ExpandRow : ListRow
{
    public required bool Expanded { get; init; }
    public string Text => Expanded ? "Collapse" : "Expand";
    public string Chevron => Expanded ? "" : "";
    public override bool Focusable => true;
}

public static class RowBuilder
{
    public static string LetterOf(string name)
    {
        if (name.Length == 0) return "&";
        char c = char.ToUpperInvariant(name[0]);
        if (char.IsDigit(c)) return "#";
        return char.IsLetter(c) ? c.ToString() : "&";
    }

    private static int LetterRank(string letter) =>
        letter == "#" ? 0 : letter.Length == 1 && letter[0] is >= 'A' and <= 'Z' ? 1 : 2;

    public static List<ListRow> Build(IReadOnlyList<AppEntry> apps, Settings settings, ISet<string> pinned,
        ISet<string> openFolders, bool recentExpanded, string? filter)
    {
        var rows = new List<ListRow>(apps.Count + 40);
        AppRow Row(AppEntry a, int indent = 0) => new() { App = a, Indent = indent, IsPinned = pinned.Contains(a.Id) };

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var q = filter.Trim();
            foreach (var a in apps.Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                                  .OrderByDescending(a => a.Name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase))
                                  .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
                rows.Add(Row(a));
            return rows;
        }

        if (settings.ShowRecentlyAdded)
        {
            var since = DateTime.Now.AddDays(-7);
            var recent = apps.Where(a => a.Created > since).OrderByDescending(a => a.Created).ToList();
            if (recent.Count > 0)
            {
                rows.Add(new HeaderRow { Text = "Recently added" });
                foreach (var a in recentExpanded ? recent : recent.Take(3)) rows.Add(Row(a));
                if (recent.Count > 3) rows.Add(new ExpandRow { Expanded = recentExpanded });
            }
        }

        if (settings.ShowMostUsed)
        {
            var most = apps.Where(a => a.Launches > 0).OrderByDescending(a => a.Launches).ThenBy(a => a.Name).Take(6).ToList();
            if (most.Count > 0)
            {
                rows.Add(new HeaderRow { Text = "Most used" });
                foreach (var a in most) rows.Add(Row(a));
            }
        }

        // A–Z: root apps and folders interleaved, folders expand inline
        var entries = new List<(string name, AppEntry? app, string? folder)>();
        foreach (var a in apps.Where(a => a.Folder == null)) entries.Add((a.Name, a, null));
        foreach (var f in apps.Where(a => a.Folder != null).Select(a => a.Folder!).Distinct(StringComparer.OrdinalIgnoreCase))
            entries.Add((f, null, f));
        entries.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.CurrentCultureIgnoreCase));

        string? current = null;
        foreach (var (name, app, folder) in entries.OrderBy(e => LetterRank(LetterOf(e.name))).ThenBy(e => LetterOf(e.name), StringComparer.CurrentCulture).ThenBy(e => e.name, StringComparer.CurrentCultureIgnoreCase))
        {
            string letter = LetterOf(name);
            if (letter != current) { current = letter; rows.Add(new HeaderRow { Text = letter, IsLetter = true }); }
            if (app != null) { rows.Add(Row(app)); continue; }

            bool open = openFolders.Contains(folder!);
            rows.Add(new FolderRow { Name = folder!, IsOpen = open });
            if (open)
                foreach (var a in apps.Where(a => string.Equals(a.Folder, folder, StringComparison.OrdinalIgnoreCase)))
                    rows.Add(Row(a, 1));
        }
        return rows;
    }
}
