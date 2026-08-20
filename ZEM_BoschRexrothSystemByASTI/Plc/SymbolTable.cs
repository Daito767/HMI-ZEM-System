using System.Text;
using Opc.Ua;

namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>A variable found while browsing, with the dotted path it got relative to the application node.</summary>
public sealed record BrowsedVariable(string Path, NodeId NodeId)
{
    /// <summary>-1 scalar, 0 or more means the server publishes this as an array node.</summary>
    public int ValueRank { get; set; } = ValueRanks.Scalar;

    /// <summary>What the server expects on a write. A BOOL will not accept an Int32.</summary>
    public BuiltInType BuiltInType { get; set; } = BuiltInType.Null;

    public bool IsArray => ValueRank >= 1 || ValueRank == ValueRanks.OneOrMoreDimensions
                                          || ValueRank == ValueRanks.OneDimension;
}

/// <summary>A logical name bound to a node, plus the element index when the node is an array.</summary>
public sealed record ResolvedSymbol(
    string Logical, NodeId NodeId, int? ElementIndex, string MatchedPath, BuiltInType BuiltInType);

/// <summary>
/// Maps the logical names from <see cref="PlcSymbols"/> onto whatever the server really exposes.
///
/// Two things are unknown until you browse a given server: where the application sits in the
/// address space, and whether arrays come out as one node per element or as a single array node.
/// Both are handled here - the rest of the HMI only ever asks for "Main.Layout.Rows[-1]._count".
/// </summary>
public sealed class SymbolTable
{
    /// <summary>
    /// A name with two indexes in it can be spelled several ways at each of them, and the right
    /// combination has to be among the ones tried. The cost is a dictionary lookup per candidate,
    /// paid once per symbol when the connection is made.
    /// </summary>
    private const int MaxCandidates = 64;


    /// <summary>PLC arrays declared with a lower bound other than 0. OPC UA delivers them zero based.</summary>
    private static readonly Dictionary<string, int> LowerBounds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Rows"] = -1,
        ["IsAtFront"] = -1,
        ["DroppedCount"] = -1
        // ColorCount looked like an ARRAY[2..9] because the HMI only ever reads NoColor(2) to
        // Black(9), but the PLC declares it ARRAY[0..OBJECT_TYPE_MAX], indexed straight by
        // ENUM_ObjectType. Rebasing it would have read the counter of a different colour.
    };

    private readonly Dictionary<string, BrowsedVariable> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BrowsedVariable>> _byLeaf = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResolvedSymbol?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<BrowsedVariable> Variables => _byPath.Values;

    public void Add(BrowsedVariable variable)
    {
        _byPath[variable.Path] = variable;
        var leaf = LeafOf(variable.Path);
        if (!_byLeaf.TryGetValue(leaf, out var list))
            _byLeaf[leaf] = list = new List<BrowsedVariable>();
        list.Add(variable);
    }

    public void Clear()
    {
        _byPath.Clear();
        _byLeaf.Clear();
        _resolved.Clear();
    }

    public ResolvedSymbol? Resolve(string logical)
    {
        if (_resolved.TryGetValue(logical, out var cached))
            return cached;

        var result = ResolveCore(logical);
        _resolved[logical] = result;
        return result;
    }

    private ResolvedSymbol? ResolveCore(string logical)
    {
        // 1. the whole name as a leaf variable, in every index spelling the server might use
        foreach (var candidate in Candidates(logical))
        {
            var hit = Find(candidate);
            if (hit is not null)
                return new ResolvedSymbol(logical, hit.NodeId, null, hit.Path, hit.BuiltInType);
        }

        // 2. the last segment is an index into an array node
        var (basePath, index) = SplitTrailingIndex(logical);
        if (basePath is null) return null;

        foreach (var candidate in Candidates(basePath))
        {
            var hit = Find(candidate);
            if (hit is null) continue;
            // A scalar match here would mean the name collides with something else; only take arrays.
            if (!hit.IsArray) continue;
            return new ResolvedSymbol(logical, hit.NodeId, index, hit.Path, hit.BuiltInType);
        }

        return null;
    }

    /// <summary>Exact path first, then any browsed path that ends with the candidate.</summary>
    private BrowsedVariable? Find(string candidate)
    {
        if (_byPath.TryGetValue(candidate, out var exact))
            return exact;

        if (!_byLeaf.TryGetValue(LeafOf(candidate), out var sameLeaf))
            return null;

        var suffix = "." + candidate;
        BrowsedVariable? match = null;
        foreach (var variable in sameLeaf)
        {
            if (!variable.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            // Prefer the shortest path when several branches end the same way.
            if (match is null || variable.Path.Length < match.Path.Length)
                match = variable;
        }
        return match;
    }

    private static string LeafOf(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path[(dot + 1)..];
    }

    /// <summary>
    /// "Main.Layout.Rows[-1]._count" also has to be tried as "Main.Layout.Rows[0]._count" and as
    /// "Main.Layout.Rows.-1._count": an <c>ARRAY[-1..1]</c> element may be published under any of
    /// those spellings, and every combination of them along the path has to be tried.
    /// </summary>
    private static IEnumerable<string> Candidates(string logical)
    {
        var segments = logical.Split('.');
        var variants = segments.Select(SegmentVariants).ToArray();

        var total = 1;
        foreach (var v in variants) total *= v.Length;
        if (total > MaxCandidates) total = MaxCandidates;

        for (var combo = 0; combo < total; combo++)
        {
            var builder = new StringBuilder();
            var rest = combo;
            for (var i = 0; i < variants.Length; i++)
            {
                var options = variants[i];
                var pick = options[rest % options.Length];
                rest /= options.Length;
                if (builder.Length > 0 && !pick.StartsWith('[')) builder.Append('.');
                builder.Append(pick);
            }
            yield return builder.ToString();
        }
    }

    /// <summary>
    /// The spellings one array element can carry, in the order they are worth trying.
    ///
    /// A CODESYS server keeps the brackets. The ctrlX data layer makes the element a child node
    /// named with the bare index - <c>Rows/-1/_count</c> - and keeps the index the PLC declared,
    /// so a lower bound of -1 stays -1 and is not rebased. Both the declared and the zero based
    /// index are offered, because which one a server uses cannot be known before browsing it.
    /// </summary>
    private static string[] SegmentVariants(string segment)
    {
        var open = segment.IndexOf('[');
        if (open <= 0 || !segment.EndsWith(']')) return new[] { segment };

        var name = segment[..open];
        var inner = segment[(open + 1)..^1];
        if (!int.TryParse(inner, out var declared)) return new[] { segment };

        var lower = LowerBounds.TryGetValue(name, out var l) ? l : 0;
        var zeroBased = declared - lower;

        if (zeroBased == declared)
            return new[] { segment, $"{name}.{declared}" };

        // The bare "[i]" and ".i" spellings are for servers that make the element a child node.
        return new[]
        {
            segment,
            $"{name}.{declared}",
            $"{name}[{zeroBased}]",
            $"{name}.{zeroBased}",
            $"[{zeroBased}]"
        };
    }

    /// <summary>Splits "Pool[2]._objectTypes[3]" into ("Pool[2]._objectTypes", 3), zero based.</summary>
    private static (string? BasePath, int Index) SplitTrailingIndex(string logical)
    {
        if (!logical.EndsWith(']')) return (null, 0);
        var open = logical.LastIndexOf('[');
        if (open <= 0) return (null, 0);

        var inner = logical[(open + 1)..^1];
        if (!int.TryParse(inner, out var declared)) return (null, 0);

        var basePath = logical[..open];
        var name = LeafOf(basePath);
        var lower = LowerBounds.TryGetValue(name, out var l) ? l : 0;
        return (basePath, declared - lower);
    }
}
