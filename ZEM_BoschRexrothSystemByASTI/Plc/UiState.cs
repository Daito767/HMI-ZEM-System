namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>
/// Navigation, in one place: which tabs exist, which sections each of them has, and which section
/// each tab is showing. The menu draws every tab's sections at once, so they cannot be something a
/// page announces when it opens - they have to be known before the page is ever visited.
/// </summary>
public sealed class UiState
{
    public static readonly (string Href, string Label)[] Tabs =
    {
        ("", "Home"),
        ("manual", "Control manual"),
        ("stare", "Stare sistem"),
        ("service", "Service")
    };

    /// <summary>"Conexiune", not "Setari": everything on that page is about the link to the PLC.</summary>
    private static readonly Dictionary<string, string[]> Sections = new()
    {
        [""] = Array.Empty<string>(),
        ["manual"] = new[] { "Miscare", "Pneumatic", "RFID", "Analogice" },
        ["stare"] = new[] { "Valori", "Animat sus", "Animat fata" },
        ["service"] = new[] { "Diagnostic", "Configuratie", "Conexiune", "Simboluri" }
    };

    private readonly Dictionary<string, string> _selected = new();

    public event Action? Changed;

    public static IReadOnlyList<string> SectionsOf(string tab) =>
        Sections.TryGetValue(tab, out var items) ? items : Array.Empty<string>();

    /// <summary>Each tab keeps its own section, so moving between tabs does not reset them.</summary>
    public string SelectedOf(string tab)
    {
        if (_selected.TryGetValue(tab, out var name)) return name;

        var items = SectionsOf(tab);
        return items.Count > 0 ? items[0] : string.Empty;
    }

    public void Select(string tab, string name)
    {
        if (SelectedOf(tab) == name) return;
        if (!SectionsOf(tab).Contains(name)) return;

        _selected[tab] = name;
        Changed?.Invoke();
    }

    /// <summary>The tab a route belongs to. The empty href is Home and matches only the root.</summary>
    public static string TabOf(string path)
    {
        path = path.Trim('/');
        foreach (var (href, _) in Tabs)
        {
            if (href.Length == 0)
            {
                if (path.Length == 0) return href;
                continue;
            }

            if (path.StartsWith(href, StringComparison.OrdinalIgnoreCase)) return href;
        }

        return string.Empty;
    }
}
