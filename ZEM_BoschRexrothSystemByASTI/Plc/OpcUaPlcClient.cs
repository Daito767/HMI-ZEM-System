using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>
/// Talks to the CODESYS / ctrlX OPC UA server.
///
/// The exact browse path differs from server to server, so nothing here is hard coded: on connect
/// the client browses down to the application node, records every variable it finds, and binds the
/// logical names from <see cref="PlcSymbols"/> against that. What could not be bound is kept and
/// shown on the symbol page instead of failing silently.
/// </summary>
public sealed class OpcUaPlcClient : IPlcClient
{
    private const int MaxBrowseDepth = 12;
    private const int MaxNodes = 20000;
    private const int DiscoverTimeout = 15_000;

    /// <summary>How deep the search for the application node goes, and how many nodes it may open.
    /// The ctrlX data layer is wide, so the search needs a ceiling of its own.</summary>
    private const int MaxRootSearchDepth = 8;
    private const int MaxRootSearchNodes = 4000;

    /// <summary>
    /// How many of the five root names have to sit side by side before the search stops looking.
    /// A real application node has all of them; a chance collision has one.
    /// </summary>
    private const int ConvincingRootScore = 3;

    private readonly HmiSettings _settings;
    private readonly string _pkiRoot;
    private readonly ILogger _log;

    /// <summary>What the server accepts as a publishing interval. Its own limits are 10 ms to 10 s.</summary>
    public const int MinPublishingMs = 50;
    public const int MaxPublishingMs = 1000;

    private readonly SymbolTable _symbols = new();

    /// <summary>The on demand reads are planned from the UI thread while the loop plans its own.</summary>
    private readonly ConcurrentDictionary<string, ReadPlan> _plans = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Last value of every monitored node, by NodeId. Written from the publish thread.</summary>
    private readonly Dictionary<string, DataValue> _live = new();
    private readonly object _liveGate = new();

    /// <summary>Client handle to NodeId, so a notification can be filed without searching.</summary>
    private readonly Dictionary<uint, string> _handles = new();
    private readonly HashSet<string> _watched = new();

    /// <summary>Nodes the server would not monitor. Their group keeps being read instead.</summary>
    private readonly HashSet<string> _unwatchable = new();

    /// <summary>Raised once per publish, so the refresh loop can follow the data instead of a clock.</summary>
    private readonly SemaphoreSlim _published = new(0, 1);

    /// <summary>
    /// The gaps between the last publishes. An animation is only as even as the values behind it,
    /// so when the picture stutters this is the first thing that has to be looked at: a tight
    /// spread means the data is fine and the drawing is at fault, a wide one means the opposite.
    /// </summary>
    private readonly Queue<double> _gaps = new();
    private long _lastPublishTicks;

    private ApplicationConfiguration? _configuration;
    private ISession? _session;
    private List<SymbolBinding> _bindings = new();
    private Subscription? _subscription;
    private bool _subscriptionRefused;

    public OpcUaPlcClient(HmiSettings settings, string pkiRoot, ILogger log)
    {
        _settings = settings;
        _pkiRoot = pkiRoot;
        _log = log;
    }

    public string Description => _settings.EndpointUrl;

    public bool IsConnected => _session is { Connected: true };

    public IReadOnlyList<SymbolBinding> Bindings => _bindings;

    /// <summary>The whole browsed address space, for the symbol page.</summary>
    public IReadOnlyCollection<BrowsedVariable> BrowsedVariables => _symbols.Variables;

    public string? ApplicationPath { get; private set; }

    /// <summary>True while the server is pushing values, so the loop no longer reads over the wire.</summary>
    public bool IsLive => _subscription is { Created: true, PublishingStopped: false };

    /// <summary>How many nodes the server is watching for us. Zero means the loop is still reading.</summary>
    public int LiveNodeCount { get { lock (_liveGate) return _watched.Count; } }

    /// <summary>
    /// Once the subscription exists this is the interval the server agreed to, not the one asked
    /// for - it is free to revise it, and the drawings pace themselves by this number.
    /// </summary>
    public int PublishingIntervalMs => _subscription is { Created: true } live
        ? (int)live.CurrentPublishingInterval
        : Math.Clamp(_settings.PollIntervalMs, MinPublishingMs, MaxPublishingMs);

    /// <summary>Set when the server would not give us a subscription, so the reason stays visible.</summary>
    public string? SubscriptionError { get; private set; }

    /// <summary>
    /// The sampling interval the server actually granted, which is not always the one asked for -
    /// it may revise it, and a slow sampling is invisible from the publishing rate: the values
    /// arrive on time, they are just old by the time they leave.
    /// </summary>
    public double SamplingIntervalMs { get; private set; }

    /// <summary>How far apart the last publishes really were, in milliseconds.</summary>
    public (double Min, double Average, double Max, int Count) PublishGaps
    {
        get
        {
            lock (_liveGate)
            {
                return _gaps.Count == 0
                    ? (0, 0, 0, 0)
                    : (_gaps.Min(), _gaps.Average(), _gaps.Max(), _gaps.Count);
            }
        }
    }

    // --- connection -------------------------------------------------------------------

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await DisconnectCoreAsync();

            // A new session deserves a new attempt, even if the last one would not subscribe.
            _subscriptionRefused = false;
            SubscriptionError = null;

            _configuration ??= await BuildConfigurationAsync(ct);

            var endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                _configuration, _settings.EndpointUrl, _settings.UseSecurity, DiscoverTimeout, ct);
            var endpointConfiguration = EndpointConfiguration.Create(_configuration);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            IUserIdentity identity = _settings.Anonymous
                ? new UserIdentity()
                : new UserIdentity(_settings.Username, Encoding.UTF8.GetBytes(_settings.Password));

            _session = await DefaultSessionFactory.Instance.CreateAsync(
                _configuration, endpoint, false, false,
                "ZEM HMI", 60_000, identity, null, ct);

            _log.LogInformation("OPC UA connected to {Endpoint}", endpointDescription.EndpointUrl);

            await BuildSymbolTableAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync();
        try { await DisconnectCoreAsync(); }
        finally { _gate.Release(); }
    }

    private async Task DisconnectCoreAsync()
    {
        var session = _session;
        _session = null;
        _plans.Clear();
        DropSubscription();
        if (session is null) return;

        try { await session.CloseAsync(); }
        catch (Exception ex) { _log.LogDebug(ex, "closing the OPC UA session failed"); }
        finally { session.Dispose(); }
    }

    private async Task<ApplicationConfiguration> BuildConfigurationAsync(CancellationToken ct)
    {
        var pki = Path.Combine(_pkiRoot, "pki");
        Directory.CreateDirectory(pki);

        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "ZEM HMI",
            ApplicationUri = $"urn:localhost:ASTI:ZemHmi",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "own"),
                    SubjectName = "CN=ZEM HMI, O=ASTI"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "issuers")
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "trusted")
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "rejected")
                },
                // A lab stand: the PLC certificate is not in anyone's trust list and never will be.
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 1024
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15_000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60_000 },
            TraceConfiguration = new TraceConfiguration()
        };

        await configuration.ValidateAsync(ApplicationType.Client, ct);

        configuration.CertificateValidator.CertificateValidation +=
            (_, e) => e.Accept = true;

        var application = new ApplicationInstance(configuration);
        await application.CheckApplicationInstanceCertificatesAsync(true, null, ct);

        return configuration;
    }

    // --- browsing ---------------------------------------------------------------------

    private async Task BuildSymbolTableAsync(CancellationToken ct)
    {
        _symbols.Clear();
        _plans.Clear();

        var root = await FindApplicationNodeAsync(ct);
        ApplicationPath = root.Path;

        var variables = new List<BrowsedVariable>();
        await CollectAsync(root.NodeId, "", variables, 0, ct);

        await FillAttributesAsync(variables, ct);
        foreach (var variable in variables)
            _symbols.Add(variable);

        _bindings = PlcSymbols.All().Distinct().Select(logical =>
        {
            var resolved = _symbols.Resolve(logical);
            return resolved is null
                ? SymbolBinding.Missing(logical, "negasit in spatiul de adrese")
                : new SymbolBinding(
                    logical,
                    resolved.NodeId.ToString(),
                    true,
                    resolved.ElementIndex is { } i ? $"{resolved.MatchedPath}[{i}] (nod array)" : resolved.MatchedPath);
        }).ToList();

        var bound = _bindings.Count(b => b.Bound);
        _log.LogInformation("OPC UA: {Bound}/{Total} simboluri legate sub {Root}",
            bound, _bindings.Count, root.Path);
    }

    /// <summary>
    /// Finds the node that owns Main / HMI / IOs / GVL_Diag / GVL_Config.
    ///
    /// Every candidate is scored by how many of those five names sit among its children, and the
    /// best one wins. Stopping at the first node with a single matching child is what sent the
    /// search into the ctrlX data layer's diagnosis area, where one node happened to carry a
    /// matching name and none of the 422 symbols could bind. One name is a coincidence; three are
    /// the application.
    ///
    /// The walk descends through variables as well as objects: on a CODESYS target the path is all
    /// objects, but a data layer publishes plain nodes as variables that still have children.
    /// </summary>
    private async Task<(NodeId NodeId, string Path)> FindApplicationNodeAsync(CancellationToken ct)
    {
        // Two queues, not one: a data layer is far wider than it is deep, and a plain breadth-first
        // walk spends its whole budget on diagnosis entries before it ever reaches the application.
        // Names that lie on the way to a PLC application jump the line.
        var likely = new Queue<(NodeId Node, string Path, int Depth)>();
        var rest = new Queue<(NodeId Node, string Path, int Depth)>();
        rest.Enqueue((ObjectIds.ObjectsFolder, "", 0));
        var seen = new HashSet<string> { ObjectIds.ObjectsFolder.ToString() };

        (NodeId Node, string Path, int Score) best = (ObjectIds.ObjectsFolder, "Objects", 0);
        var visited = 0;

        while ((likely.Count > 0 || rest.Count > 0) && visited < MaxRootSearchNodes)
        {
            ct.ThrowIfCancellationRequested();
            var (node, path, depth) = likely.Count > 0 ? likely.Dequeue() : rest.Dequeue();
            if (depth > MaxRootSearchDepth) continue;

            visited++;
            var children = await BrowseAsync(node, ct);

            var score = children
                .Select(NameOf)
                .Where(name => PlcSymbols.Roots.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (score > best.Score)
            {
                best = (node, string.IsNullOrEmpty(path) ? "Objects" : path, score);
                if (score >= ConvincingRootScore) break;
            }

            // Nothing under a root itself is worth walking - the collect pass does that.
            if (score > 0) continue;

            foreach (var child in children)
            {
                if (child.NodeClass is not (NodeClass.Object or NodeClass.Variable)) continue;

                var name = NameOf(child);
                if (IsNoise(name)) continue;

                var childId = ExpandedNodeId.ToNodeId(child.NodeId, _session!.NamespaceUris);
                if (childId is null || !seen.Add(childId.ToString())) continue;

                var next = (childId, string.IsNullOrEmpty(path) ? name : $"{path}.{name}", depth + 1);
                if (IsOnTheWay(name)) likely.Enqueue(next); else rest.Enqueue(next);
            }
        }

        _log.LogInformation("OPC UA: radacina {Path}, scor {Score}/{Total}, {Visited} noduri deschise",
            best.Path, best.Score, PlcSymbols.Roots.Length, visited);

        // Nothing recognisable: the whole Objects folder, and let the suffix match sort it out.
        return (best.Node, best.Path);
    }

    private static bool IsNoise(string name) =>
        name is "Server" or "Types" or "Views" or "Aliases" or "ServerCapabilities" or "Locales";

    /// <summary>
    /// Names that lie between the address space root and a PLC application, on the two targets this
    /// HMI meets: a CODESYS device set and a ctrlX data layer (<c>plc / app / Application / sym</c>).
    /// Only a hint for the search order - a name that is not here is still visited, just later.
    /// </summary>
    private static bool IsOnTheWay(string name) => name.ToLowerInvariant() is
        "plc" or "app" or "apps" or "application" or "applications" or "sym" or "symbols" or
        "deviceset" or "resources" or "programs" or "globalvars" or "datalayer" or "plc1";

    private async Task CollectAsync(
        NodeId node, string path, List<BrowsedVariable> into, int depth, CancellationToken ct)
    {
        if (depth > MaxBrowseDepth || into.Count > MaxNodes) return;
        ct.ThrowIfCancellationRequested();

        foreach (var child in await BrowseAsync(node, ct))
        {
            var name = NameOf(child);
            if (IsNoise(name)) continue;

            var childId = ExpandedNodeId.ToNodeId(child.NodeId, _session!.NamespaceUris);
            if (childId is null) continue;

            var childPath = Join(path, name);

            if (child.NodeClass == NodeClass.Variable)
                into.Add(new BrowsedVariable(childPath, childId));

            // A CODESYS structure is an Object; an FB instance can be a Variable that still has children.
            if (child.NodeClass is NodeClass.Object or NodeClass.Variable)
                await CollectAsync(childId, childPath, into, depth + 1, ct);
        }
    }

    /// <summary>Element children may be published as "Rows[0]" or as a bare "[0]".</summary>
    private static string Join(string path, string name)
    {
        if (string.IsNullOrEmpty(path)) return name;
        return name.StartsWith('[') ? path + name : $"{path}.{name}";
    }

    private static string NameOf(ReferenceDescription reference) =>
        reference.BrowseName?.Name ?? reference.DisplayName?.Text ?? string.Empty;

    private async Task<List<ReferenceDescription>> BrowseAsync(NodeId node, CancellationToken ct)
    {
        var session = _session ?? throw new InvalidOperationException("nu exista sesiune OPC UA");
        var results = new List<ReferenceDescription>();

        var description = new BrowseDescription
        {
            NodeId = node,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
            ResultMask = (uint)BrowseResultMask.All
        };

        try
        {
            var response = await session.BrowseAsync(
                null, null, 0, new BrowseDescriptionCollection { description }, ct);

            var browseResult = response.Results.FirstOrDefault();
            if (browseResult is null) return results;
            results.AddRange(browseResult.References);

            var continuationPoint = browseResult.ContinuationPoint;
            while (continuationPoint is { Length: > 0 })
            {
                var next = await session.BrowseNextAsync(
                    null, false, new ByteStringCollection { continuationPoint }, ct);
                var nextResult = next.Results.FirstOrDefault();
                if (nextResult is null) break;
                results.AddRange(nextResult.References);
                continuationPoint = nextResult.ContinuationPoint;
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "browse esuat pe {Node}", node);
        }

        return results;
    }

    /// <summary>
    /// One pass over the browsed variables to learn two things per node: whether it is an array,
    /// so indexing can be done locally, and what type it wants on a write.
    /// </summary>
    private async Task FillAttributesAsync(List<BrowsedVariable> variables, CancellationToken ct)
    {
        if (variables.Count == 0 || _session is null) return;

        var toRead = new ReadValueIdCollection();
        foreach (var variable in variables)
        {
            toRead.Add(new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.ValueRank });
            toRead.Add(new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.DataType });
        }

        try
        {
            var response = await _session.ReadAsync(null, 0, TimestampsToReturn.Neither, toRead, ct);
            for (var i = 0; i < variables.Count; i++)
            {
                var rank = i * 2 < response.Results.Count ? response.Results[i * 2] : null;
                if (rank?.Value is not null && StatusCode.IsNotBad(rank.StatusCode))
                {
                    try { variables[i].ValueRank = Convert.ToInt32(rank.Value); }
                    catch (InvalidCastException) { /* leave it a scalar */ }
                }

                var type = i * 2 + 1 < response.Results.Count ? response.Results[i * 2 + 1] : null;
                if (type?.Value is NodeId dataType && StatusCode.IsNotBad(type.StatusCode))
                    variables[i].BuiltInType = Opc.Ua.TypeInfo.GetBuiltInType(dataType);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "citirea atributelor ValueRank/DataType a esuat");
        }
    }

    // --- the live values ----------------------------------------------------------------

    /// <summary>
    /// Polling put a network round trip between the machine and every frame of the cell drawing:
    /// three reads per cycle, each one a request the tablet had to wait for over WiFi, and the
    /// jitter of that wait was visible as the picture stopping and jumping.
    ///
    /// So the loop groups are subscribed instead. The server watches those nodes and pushes what
    /// changed, the client keeps the last value of each, and a "read" becomes a lookup. Nothing
    /// above this point changed: the decoding still takes a <see cref="PlcValues"/>.
    ///
    /// Reading is kept as the fallback, and it is used whenever the pushing is not known to be
    /// working - a server that refuses subscriptions, items it would not create, or a subscription
    /// that stopped publishing. A frozen picture on a machine HMI has to become a slow picture,
    /// never a wrong one.
    /// </summary>
    private async Task<bool> WatchAsync(ReadPlan plan, CancellationToken ct)
    {
        if (_subscriptionRefused) return false;

        var session = _session;
        if (session is null) return false;

        try
        {
            if (_subscription is null)
                await CreateSubscriptionAsync(session, ct);

            var subscription = _subscription!;

            // Nothing that touches the subscription happens under _liveGate: the publish thread
            // enters OnDataChange holding the subscription's own lock, so taking the two in the
            // other order here would eventually meet it head on.
            var missing = new List<int>();
            lock (_liveGate)
            {
                for (var i = 0; i < plan.Keys.Count; i++)
                {
                    var nodeKey = plan.Keys[i];
                    if (!_watched.Contains(nodeKey) && !_unwatchable.Contains(nodeKey))
                        missing.Add(i);
                }
            }

            if (missing.Count > 0)
            {
                foreach (var i in missing)
                {
                    subscription.AddItem(new MonitoredItem(subscription.DefaultItem)
                    {
                        DisplayName = plan.Keys[i],
                        StartNodeId = plan.Nodes[i].NodeId,
                        AttributeId = Attributes.Value,
                        MonitoringMode = MonitoringMode.Reporting,
                        SamplingInterval = PublishingIntervalMs,
                        // Only the newest value matters; a queue would just deliver stale frames.
                        QueueSize = 1,
                        DiscardOldest = true,
                        Handle = plan.Keys[i]
                    });
                }

                await subscription.ApplyChangesAsync(ct);
                TakeStockOfItems(subscription);
                // The first values are one publishing interval away, so this cycle still reads.
                return false;
            }

            if (!plan.Live)
            {
                lock (_liveGate)
                    plan.Live = plan.Keys.All(_watched.Contains);
            }

            return plan.Live && IsLive;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "serverul nu a acceptat subscriptia; se ramane pe citire ciclica");
            SubscriptionError = ex.Message;
            _subscriptionRefused = true;
            DropSubscription();
            return false;
        }
    }

    private async Task CreateSubscriptionAsync(ISession session, CancellationToken ct)
    {
        var interval = PublishingIntervalMs;

        // The ctrlX server wants the subscription lifetime between 1 s and 100 s, and the SDK wants
        // a lifetime of at least three keep-alives. Counting in milliseconds keeps both true at any
        // publishing interval the settings page allows.
        var keepAlive = (uint)Math.Max(3, 2_000 / interval);

        var subscription = new Subscription(session.DefaultSubscription)
        {
            DisplayName = "ZEM HMI",
            PublishingInterval = interval,
            KeepAliveCount = keepAlive,
            LifetimeCount = keepAlive * 6,
            MaxNotificationsPerPublish = 0,
            PublishingEnabled = true,
            TimestampsToReturn = TimestampsToReturn.Neither,
            Priority = 0
        };

        subscription.FastDataChangeCallback = OnDataChange;

        session.AddSubscription(subscription);
        await subscription.CreateAsync(ct);

        _subscription = subscription;
        SubscriptionError = null;
        _log.LogInformation("subscriptie OPC UA creata, la {Interval} ms", interval);
    }

    /// <summary>
    /// Files the items the server did create and remembers the ones it would not, so a single node
    /// it refuses does not send the whole group back to reading on every cycle.
    /// </summary>
    private void TakeStockOfItems(Subscription subscription)
    {
        // Read the subscription first, then take the lock - see the note in WatchAsync.
        var items = new List<(string Key, uint ClientHandle, bool Good, string? Error)>();
        var granted = 0.0;

        foreach (var item in subscription.MonitoredItems)
        {
            if (item.Handle is not string nodeKey) continue;

            var error = item.Status.Error;
            var good = item.Created && StatusCode.IsNotBad(error?.StatusCode ?? StatusCodes.Good);
            items.Add((nodeKey, item.ClientHandle, good, error?.ToString()));

            // The slowest one is the one that decides how stale the picture can get.
            if (good) granted = Math.Max(granted, item.Status.SamplingInterval);
        }

        SamplingIntervalMs = granted;
        if (granted > PublishingIntervalMs)
            _log.LogWarning("serverul esantioneaza la {Granted} ms, desi s-au cerut {Asked} ms",
                granted, PublishingIntervalMs);

        lock (_liveGate)
        {
            // Added to, never rebuilt: clearing it would drop the notifications arriving meanwhile,
            // and a value that changes once would then be lost for good.
            foreach (var item in items)
            {
                if (item.Good)
                {
                    _watched.Add(item.Key);
                    _handles[item.ClientHandle] = item.Key;
                }
                else
                {
                    _unwatchable.Add(item.Key);
                }
            }

            // A group is only live again once every one of its nodes is watched.
            foreach (var plan in _plans.Values)
                plan.Live = false;
        }

        foreach (var item in items.Where(i => !i.Good))
            _log.LogWarning("nodul {Node} nu a putut fi monitorizat: {Error}",
                item.Key, item.Error ?? "motiv necunoscut");
    }

    /// <summary>
    /// Waits for the next publish, up to <paramref name="timeoutMs"/>. The refresh loop uses this
    /// instead of a delay: a loop on its own clock drifts against the publishing, so one pass sees
    /// a value the moment it lands and the next sees one that has been sitting there - the steps
    /// come out uneven, and uneven steps are exactly what the eye reads as stuttering.
    /// </summary>
    public Task<bool> WaitForPublishAsync(int timeoutMs, CancellationToken ct) =>
        _published.WaitAsync(timeoutMs, ct);

    /// <summary>Runs on the publish thread: file the values and get out of the way.</summary>
    private void OnDataChange(Subscription subscription, DataChangeNotification notification, IList<string> _)
    {
        lock (_liveGate)
        {
            foreach (var item in notification.MonitoredItems)
            {
                if (_handles.TryGetValue(item.ClientHandle, out var nodeKey))
                    _live[nodeKey] = item.Value;
            }

            var now = Stopwatch.GetTimestamp();
            if (_lastPublishTicks != 0)
            {
                _gaps.Enqueue((now - _lastPublishTicks) * 1000.0 / Stopwatch.Frequency);
                while (_gaps.Count > 32) _gaps.Dequeue();
            }
            _lastPublishTicks = now;
        }

        // One slot, released outside the lock: the loop only needs to know that something new is
        // waiting, and if it is already awake there is nothing to signal.
        if (_published.CurrentCount == 0)
        {
            try { _published.Release(); }
            catch (SemaphoreFullException) { }
        }
    }

    private PlcValues FromCache(ReadPlan plan)
    {
        var values = new DataValue?[plan.Nodes.Count];
        lock (_liveGate)
        {
            for (var i = 0; i < values.Length; i++)
                values[i] = _live.GetValueOrDefault(plan.Keys[i]);
        }

        return new PlcValues(values, plan.Map);
    }

    private void DropSubscription()
    {
        var subscription = _subscription;
        _subscription = null;

        lock (_liveGate)
        {
            _live.Clear();
            _handles.Clear();
            _watched.Clear();
            _unwatchable.Clear();
        }

        if (subscription is null) return;
        subscription.FastDataChangeCallback = null;
        try { subscription.Dispose(); }
        catch (Exception ex) { _log.LogDebug(ex, "inchiderea subscriptiei a esuat"); }
    }

    // --- reading ----------------------------------------------------------------------

    private sealed class ReadPlan
    {
        public ReadValueIdCollection Nodes { get; } = new();
        public Dictionary<string, (int Node, int? Element)> Map { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The NodeIds as text, in the order of <see cref="Nodes"/>, to key the live cache.</summary>
        public List<string> Keys { get; } = new();

        /// <summary>True once every node of the plan has a monitored item the server accepted.</summary>
        public bool Live { get; set; }
    }

    private ReadPlan PlanFor(string key, Func<IEnumerable<string>> logicals)
    {
        if (_plans.TryGetValue(key, out var cached)) return cached;

        var plan = new ReadPlan();
        var nodeIndex = new Dictionary<string, int>();

        foreach (var logical in logicals())
        {
            var resolved = _symbols.Resolve(logical);
            if (resolved is null) continue;

            var nodeKey = resolved.NodeId.ToString();
            if (!nodeIndex.TryGetValue(nodeKey, out var index))
            {
                index = plan.Nodes.Count;
                nodeIndex[nodeKey] = index;
                plan.Nodes.Add(new ReadValueId
                {
                    NodeId = resolved.NodeId,
                    AttributeId = Attributes.Value
                });
                plan.Keys.Add(nodeKey);
            }

            plan.Map[logical] = (index, resolved.ElementIndex);
        }

        _plans[key] = plan;
        return plan;
    }

    /// <summary>
    /// One group of logical names, as values. <paramref name="live"/> marks the groups the refresh
    /// loop asks for over and over: those get a subscription, and then a "read" is a lookup in the
    /// last values the server pushed. Everything else stays a real read - the heavy groups are read
    /// once or on demand, and watching them would cost the server for nothing.
    /// </summary>
    private async Task<PlcValues> ReadAsync(string key, Func<IEnumerable<string>> logicals,
                                            CancellationToken ct, bool live = false)
    {
        var session = _session ?? throw new InvalidOperationException("nu exista sesiune OPC UA");
        var plan = PlanFor(key, logicals);
        if (plan.Nodes.Count == 0)
            return new PlcValues(Array.Empty<DataValue?>(), plan.Map);

        if (live && await WatchAsync(plan, ct))
            return FromCache(plan);

        var response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, plan.Nodes, ct);
        var values = new DataValue?[plan.Nodes.Count];
        for (var i = 0; i < values.Length && i < response.Results.Count; i++)
            values[i] = response.Results[i];

        return new PlcValues(values, plan.Map);
    }

    public async Task ReadCellAsync(CellSnapshot target, CancellationToken ct)
    {
        var values = await ReadAsync(nameof(PlcSymbols.CellLoop), PlcSymbols.CellLoop, ct, live: true);

        target.Run = values.GetBool(PlcSymbols.Run);
        target.ResetStarted = values.GetBool(PlcSymbols.ResetStarted);
        target.MainStep = values.GetInt(PlcSymbols.MainStep);
        target.InGripper = values.GetInt(PlcSymbols.InGripper, -1);
        target.InVacuum = values.GetEnum(PlcSymbols.InVacuum, ObjectType.Missing);
        target.PalletCount = values.GetInt(PlcSymbols.PalletCount);

        foreach (var region in PlcEnums.AllRegions)
        {
            var row = target.Rows[region];
            row.Count = values.GetInt(PlcSymbols.RowCount(region));
            row.Capacity = values.GetInt(PlcSymbols.RowCapacity(region), RowState.MaxCapacity);
            row.IsAtFront = values.GetBool(PlcSymbols.IsAtFront(region));
            row.DroppedCount = values.GetInt(PlcSymbols.DroppedCount(region));
            for (var i = 0; i < RowState.MaxCapacity; i++)
                row.PalletIds[i] = values.GetInt(PlcSymbols.RowPalletId(region, i), -1);
        }

        for (var p = 0; p < CellSnapshot.PoolSize; p++)
        {
            var pallet = target.Pool[p];
            pallet.IsValid = values.GetBool(PlcSymbols.PoolIsValid(p));
            pallet.VirtualId = values.GetInt(PlcSymbols.PoolVirtualId(p), p);
            pallet.RealId = values.GetInt(PlcSymbols.PoolRealId(p));
            for (var s = 0; s < PalletInfo.SlotCount; s++)
                pallet.Slots[s] = values.GetEnum(PlcSymbols.PoolObjectType(p, s), ObjectType.Missing);
        }

        target.UpdatedAt = DateTime.Now;
    }

    public async Task ReadMachineAsync(MachineSnapshot target, CancellationToken ct)
    {
        var values = await ReadAsync(nameof(PlcSymbols.MachineLoop), PlcSymbols.MachineLoop, ct, live: true);

        ReadAxis(values, target.Arm, PlcSymbols.ArmController, PlcSymbols.ArmDeactivateInputs,
            hasPosition: true, hasMoveAbsolute: true);
        ReadAxis(values, target.Conveyor, PlcSymbols.ConveyorController, PlcSymbols.ConveyorDeactivateInputs,
            hasPosition: false, hasMoveAbsolute: false);

        var hmi = target.Hmi;
        hmi.ConveyorDistance = values.GetDouble(PlcSymbols.ConveyorDistance);
        hmi.CurrentObjectColor = values.GetUInt(PlcSymbols.CurrentObjectColor);
        hmi.AllowRelativeMovement = values.GetBool(PlcSymbols.ArmAllowRelativeMovement);
        hmi.WaitForMoveAbsolute = values.GetBool(PlcSymbols.ArmWaitForMoveAbsolute);
        hmi.WaitForMoveRelative = values.GetBool(PlcSymbols.ArmWaitForMoveRelative);

        for (var type = (int)ObjectType.NoColor; type <= (int)ObjectType.Black; type++)
            hmi.ColorCounts[type - (int)ObjectType.NoColor] = values.GetInt(PlcSymbols.ColorCount(type));

        for (var i = 0; i < hmi.AnalogOutValue.Length; i++)
            hmi.AnalogOutValue[i] = values.GetDouble(PlcSymbols.ValueAnalogOut(i + 1));

        ReadIo(values, target.Io);
        target.UpdatedAt = DateTime.Now;
    }

    private static void ReadAxis(
        PlcValues values, AxisState axis, string controller, string deactivateInputs,
        bool hasPosition, bool hasMoveAbsolute)
    {
        if (hasPosition)
            axis.Position = values.GetDouble(PlcSymbols.AxisPosition(controller));
        axis.PowerOn = values.GetBool(PlcSymbols.AxisPowerStatus(controller));
        axis.Busy = values.GetBool(PlcSymbols.AxisBusy(controller));
        axis.StepError = values.GetBool(PlcSymbols.AxisStepError(controller));
        axis.JogError = values.GetBool(PlcSymbols.AxisJogError(controller));
        axis.MoveAbsoluteError = hasMoveAbsolute && values.GetBool(PlcSymbols.AxisMoveAbsoluteError(controller));
        axis.InterruptError = values.GetBool(PlcSymbols.AxisInterruptError(controller));
        axis.ContinueError = values.GetBool(PlcSymbols.AxisContinueError(controller));
        axis.InputsDisabled = values.GetBool(deactivateInputs);
    }

    private static void ReadIo(PlcValues values, IoState io)
    {
        io.ArmExtended = values.GetBool(PlcSymbols.ArmExtended);
        io.ArmRetracted = values.GetBool(PlcSymbols.ArmRetracted);
        io.ArmExtendCmd = values.GetBool(PlcSymbols.ArmExtendCmd);
        io.ArmRetractCmd = values.GetBool(PlcSymbols.ArmRetractCmd);

        io.GripperClosed = values.GetBool(PlcSymbols.GripperClosed);
        io.GripperCloseCmd = values.GetBool(PlcSymbols.GripperCloseCmd);
        io.VacuumDetected = values.GetBool(PlcSymbols.VacuumDetected);
        io.VacuumCmd = values.GetBool(PlcSymbols.VacuumCmd);

        io.PullerLeftExtended = values.GetBool(PlcSymbols.PullerLeftExtended);
        io.PullerRightExtended = values.GetBool(PlcSymbols.PullerRightExtended);
        io.PullerLeftRetracted = values.GetBool(PlcSymbols.PullerLeftRetracted);
        io.PullerRightRetracted = values.GetBool(PlcSymbols.PullerRightRetracted);
        io.PullerExtendCmd = values.GetBool(PlcSymbols.PullerExtendCmd);
        io.PullerRetractCmd = values.GetBool(PlcSymbols.PullerRetractCmd);

        io.GateTopForwardRetracted = values.GetBool(PlcSymbols.GateTopForwardRetracted);
        io.GateTopBackwardRetracted = values.GetBool(PlcSymbols.GateTopBackwardRetracted);
        io.GateBottomLeftRetracted = values.GetBool(PlcSymbols.GateBottomLeftRetracted);
        io.GateBottomRightRetracted = values.GetBool(PlcSymbols.GateBottomRightRetracted);
        io.GatesTopRetractCmd = values.GetBool(PlcSymbols.GatesTopRetractCmd);
        io.GatesBottomRetractCmd = values.GetBool(PlcSymbols.GatesBottomRetractCmd);

        io.ExistForwardNear = values.GetBool(PlcSymbols.ExistForwardNear);
        io.ExistForwardFar = values.GetBool(PlcSymbols.ExistForwardFar);
        io.ExistBackwardNear = values.GetBool(PlcSymbols.ExistBackwardNear);
        io.ExistBackwardFar = values.GetBool(PlcSymbols.ExistBackwardFar);

        io.AirPressureOk = values.GetBool(PlcSymbols.AirPressureOk);
        io.StorageNotEmpty = values.GetBool(PlcSymbols.StorageNotEmpty);
        io.ButtonStart = values.GetBool(PlcSymbols.ButtonStart);

        io.DistanceCenter1 = values.GetInt(PlcSymbols.DistanceCenter1);
        io.DistanceCenter2 = values.GetInt(PlcSymbols.DistanceCenter2);

        for (var i = 0; i < PlcSymbols.ColorSensors.Length; i++)
            io.ColorSensors[i] = values.GetBool(PlcSymbols.ColorSensors[i]);

        for (var i = 0; i < io.AnalogIn.Length; i++)
            io.AnalogIn[i] = values.GetInt(PlcSymbols.AnalogIn(i + 1));

        for (var i = 0; i < io.AnalogOut.Length; i++)
            io.AnalogOut[i] = values.GetInt(PlcSymbols.AnalogOut(i + 1));

        io.RfidPresent = values.GetBool(PlcSymbols.RfidPresent);
        io.RfidExistTag = values.GetBool(PlcSymbols.RfidExistTag);
        io.RfidReady = values.GetBool(PlcSymbols.RfidReadyFlag);
        io.RfidError = values.GetBool(PlcSymbols.RfidError);
        io.RfidAlarm1 = values.GetBool(PlcSymbols.RfidAlarm1);
        io.RfidAlarm2 = values.GetBool(PlcSymbols.RfidAlarm2);
        io.RfidAntennaEnabled = values.GetBool(PlcSymbols.RfidAntennaEnabled);
        io.RfidStatusByte = values.GetInt(PlcSymbols.RfidStatusByte);
        io.RfidSignalLevel = values.GetInt(PlcSymbols.RfidSignalLevel);

        for (var i = 0; i < 8; i++)
        {
            io.RfidReadBytes[i] = values.GetInt(PlcSymbols.RfidReadByte(i));
            io.RfidWriteBytes[i] = values.GetInt(PlcSymbols.RfidWriteByte(i));
        }
    }

    public async Task ReadPoliciesAsync(PolicyState target, CancellationToken ct)
    {
        var values = await ReadAsync(nameof(PlcSymbols.Policies), PlcSymbols.Policies, ct);

        for (var i = 0; i < PolicyState.ObjectPolicyCount; i++)
            target.DropObject[i] = values.GetInt(PlcSymbols.DropObjectPolicy(i));

        for (var i = 0; i < PolicyState.PalletPolicyCount; i++)
            target.DropPallet[i] = values.GetInt(PlcSymbols.DropPalletPolicy(i));

        target.LoadedAt = DateTime.Now;
    }

    public async Task ReadDiagAsync(DiagSnapshot target, CancellationToken ct)
    {
        var values = await ReadAsync(nameof(PlcSymbols.DiagLoop), PlcSymbols.DiagLoop, ct, live: true);

        target.Active = values.GetBool(PlcSymbols.DiagActive);
        target.Count = values.GetInt(PlcSymbols.DiagCount);
        target.Head = values.GetInt(PlcSymbols.DiagHead);
        target.Cycle = values.GetUInt(PlcSymbols.DiagCycle);
        target.Last = new DiagEntry
        {
            Source = values.GetString(PlcSymbols.DiagLastSource),
            Step = values.GetInt(PlcSymbols.DiagLastStep),
            Code = values.GetEnum(PlcSymbols.DiagLastCode, HaltCode.None),
            Cycle = values.GetUInt(PlcSymbols.DiagLastCycle)
        };
    }

    public async Task ReadDiagHistoryAsync(DiagSnapshot target, CancellationToken ct)
    {
        var values = await ReadAsync(nameof(PlcSymbols.DiagHistory), PlcSymbols.DiagHistory, ct);

        var history = new DiagEntry[DiagSnapshot.HistorySize];
        for (var i = 0; i < history.Length; i++)
        {
            history[i] = new DiagEntry
            {
                Source = values.GetString(PlcSymbols.DiagHistorySource(i)),
                Step = values.GetInt(PlcSymbols.DiagHistoryStep(i)),
                Code = values.GetEnum(PlcSymbols.DiagHistoryCode(i), HaltCode.None),
                Cycle = values.GetUInt(PlcSymbols.DiagHistoryCycle(i))
            };
        }

        target.History = history;
        target.HistoryLoadedAt = DateTime.Now;
    }

    public async Task ReadColorNamesAsync(CellSnapshot target, CancellationToken ct)
    {
        var values = await ReadAsync(nameof(PlcSymbols.ColorNames), PlcSymbols.ColorNames, ct);

        for (var p = 0; p < CellSnapshot.PoolSize; p++)
            for (var s = 0; s < PalletInfo.SlotCount; s++)
                target.Pool[p].SlotNames[s] = values.GetString(PlcSymbols.PoolColorName(p, s));

        target.HasColorNames = true;
    }

    public async Task ReadConfigAsync(PlcConfigSnapshot target, CancellationToken ct)
    {
        var values = await ReadAsync(nameof(PlcSymbols.Config), PlcSymbols.Config, ct);

        var arm = target.Arm;
        arm.Home = values.GetDouble(PlcSymbols.ArmPos("Home"));
        arm.PalletCenter = values.GetDouble(PlcSymbols.ArmPos("PalletCenter"));
        arm.PalletLeft = values.GetDouble(PlcSymbols.ArmPos("PalletLeft"));
        arm.PalletRight = values.GetDouble(PlcSymbols.ArmPos("PalletRight"));
        arm.SlotLeft = values.GetDouble(PlcSymbols.ArmPos("SlotLeft"));
        arm.SlotRight = values.GetDouble(PlcSymbols.ArmPos("SlotRight"));
        arm.DropLeft = values.GetDouble(PlcSymbols.ArmPos("DropLeft"));
        arm.DropRight = values.GetDouble(PlcSymbols.ArmPos("DropRight"));
        arm.ColorSensor = values.GetDouble(PlcSymbols.ArmPos("ColorSensor"));
        arm.TravelMin = values.GetDouble(PlcSymbols.ArmPos("TravelMin"));
        arm.TravelMax = values.GetDouble(PlcSymbols.ArmPos("TravelMax"));
        arm.JogMin = values.GetDouble(PlcSymbols.ArmPos("JogMin"));
        arm.JogMax = values.GetDouble(PlcSymbols.ArmPos("JogMax"));

        arm.MoveVelocity = values.GetDouble(PlcSymbols.ArmMotion("MoveVelocity"));
        arm.MoveAccel = values.GetDouble(PlcSymbols.ArmMotion("MoveAccel"));
        arm.MoveDecel = values.GetDouble(PlcSymbols.ArmMotion("MoveDecel"));
        arm.MoveJerk = values.GetDouble(PlcSymbols.ArmMotion("MoveJerk"));
        arm.JogVelocity = values.GetDouble(PlcSymbols.ArmMotion("JogVelocity"));
        arm.JogAccel = values.GetDouble(PlcSymbols.ArmMotion("JogAccel"));
        arm.JogDecel = values.GetDouble(PlcSymbols.ArmMotion("JogDecel"));
        arm.JogJerk = values.GetDouble(PlcSymbols.ArmMotion("JogJerk"));
        arm.StopDecel = values.GetDouble(PlcSymbols.ArmMotion("StopDecel"));

        arm.VacuumDetectionTimeout = values.GetTime(PlcSymbols.ArmField("VacuumDetectionTimeout"));
        arm.ResetSettleTime = values.GetTime(PlcSymbols.ArmField("ResetSettleTime"));
        arm.ResetStopTimeout = values.GetTime(PlcSymbols.ArmField("ResetStopTimeout"));
        arm.KeepPoweredAfterMove = values.GetBool(PlcSymbols.ArmKeepPowered);

        var conveyor = target.Conveyor;
        conveyor.FirstRow = values.GetDouble(PlcSymbols.ConveyorDist("FirstRow"));
        conveyor.SecondRow = values.GetDouble(PlcSymbols.ConveyorDist("SecondRow"));
        conveyor.Rfid = values.GetDouble(PlcSymbols.ConveyorDist("RFID"));
        conveyor.Storage = values.GetDouble(PlcSymbols.ConveyorDist("Storage"));
        conveyor.PalletOffset = values.GetDouble(PlcSymbols.ConveyorDist("PalletOffset"));

        conveyor.MoveVelocity = values.GetDouble(PlcSymbols.ConveyorMotion("MoveVelocity"));
        conveyor.MoveAccel = values.GetDouble(PlcSymbols.ConveyorMotion("MoveAccel"));
        conveyor.MoveDecel = values.GetDouble(PlcSymbols.ConveyorMotion("MoveDecel"));
        conveyor.MoveJerk = values.GetDouble(PlcSymbols.ConveyorMotion("MoveJerk"));
        conveyor.JogVelocity = values.GetDouble(PlcSymbols.ConveyorMotion("JogVelocity"));
        conveyor.JogAccel = values.GetDouble(PlcSymbols.ConveyorMotion("JogAccel"));
        conveyor.JogDecel = values.GetDouble(PlcSymbols.ConveyorMotion("JogDecel"));
        conveyor.JogJerk = values.GetDouble(PlcSymbols.ConveyorMotion("JogJerk"));
        conveyor.StopDecel = values.GetDouble(PlcSymbols.ConveyorMotion("StopDecel"));

        conveyor.SlowDownFactor = values.GetDouble(PlcSymbols.ConveyorField("SlowDownFactor"));
        conveyor.SlowDownMargin = values.GetDouble(PlcSymbols.ConveyorField("SlowDownMargin"));
        conveyor.PositionTolerance = values.GetDouble(PlcSymbols.ConveyorField("PositionTolerance"));

        for (var i = 0; i < target.SlotOrder.Length; i++)
            target.SlotOrder[i] = values.GetEnum(PlcSymbols.SlotOrder(i), PalletSlot.Invalid);

        target.LoadedAt = DateTime.Now;
    }

    // --- writing ----------------------------------------------------------------------

    public Task SendCommandAsync(PlcCommand command, CancellationToken ct) =>
        WriteBoolAsync(command.Symbol(), true, ct);

    public Task WriteBoolAsync(string symbol, bool value, CancellationToken ct) =>
        WriteAsync(symbol, value, ct);

    public Task WriteRealAsync(string symbol, double value, CancellationToken ct) =>
        WriteAsync(symbol, value, ct);

    public Task WriteIntAsync(string symbol, int value, CancellationToken ct) =>
        WriteAsync(symbol, value, ct);

    private async Task WriteAsync(string symbol, object value, CancellationToken ct)
    {
        var session = _session ?? throw new InvalidOperationException("nu exista sesiune OPC UA");
        var resolved = _symbols.Resolve(symbol)
                       ?? throw new InvalidOperationException($"simbolul {symbol} nu a fost gasit pe server");

        var response = await session.WriteAsync(null, new WriteValueCollection { WriteValueFor(resolved, value) }, ct);
        var status = response.Results.FirstOrDefault();
        if (StatusCode.IsBad(status))
            throw new InvalidOperationException($"scrierea in {symbol} a esuat: {status}");
    }

    public async Task ClearCommandFlagsAsync(CancellationToken ct)
    {
        var session = _session;
        if (session is null) return;

        var writes = new WriteValueCollection();
        foreach (var flag in PlcSymbols.CommandFlags)
        {
            var resolved = _symbols.Resolve(flag);
            if (resolved is null) continue;
            writes.Add(WriteValueFor(resolved, false));
        }

        // The valves are commands too: a gripper left closed holds a pallet nobody is tracking.
        foreach (var valve in PlcSymbols.ValveCommands)
        {
            var resolved = _symbols.Resolve(valve);
            if (resolved is null) continue;
            writes.Add(WriteValueFor(resolved, false));
        }

        if (writes.Count == 0) return;
        await session.WriteAsync(null, writes, ct);
        _log.LogInformation("stinse {Count} steaguri de comanda", writes.Count);
    }

    /// <summary>
    /// A BOOL will not accept an Int32 and a REAL will not accept a Double, so the value is coerced
    /// into whatever the server said the node is. When the node turned out to be an array, the write
    /// goes to the one element through an index range.
    /// </summary>
    private static WriteValue WriteValueFor(ResolvedSymbol resolved, object value)
    {
        var typed = Coerce(value, resolved.BuiltInType);

        if (resolved.ElementIndex is not { } index)
        {
            return new WriteValue
            {
                NodeId = resolved.NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(typed)
            };
        }

        var element = typed.Value;
        var array = Array.CreateInstance(element?.GetType() ?? typeof(object), 1);
        array.SetValue(element, 0);

        return new WriteValue
        {
            NodeId = resolved.NodeId,
            AttributeId = Attributes.Value,
            IndexRange = index.ToString(),
            Value = new DataValue(new Variant(array))
        };
    }

    private static Variant Coerce(object value, BuiltInType type) => type switch
    {
        BuiltInType.Boolean => new Variant(Convert.ToBoolean(value)),
        BuiltInType.SByte => new Variant(Convert.ToSByte(value)),
        BuiltInType.Byte => new Variant(Convert.ToByte(value)),
        BuiltInType.Int16 => new Variant(Convert.ToInt16(value)),
        BuiltInType.UInt16 => new Variant(Convert.ToUInt16(value)),
        BuiltInType.Int32 => new Variant(Convert.ToInt32(value)),
        BuiltInType.UInt32 => new Variant(Convert.ToUInt32(value)),
        BuiltInType.Int64 => new Variant(Convert.ToInt64(value)),
        BuiltInType.UInt64 => new Variant(Convert.ToUInt64(value)),
        BuiltInType.Float => new Variant(Convert.ToSingle(value)),
        BuiltInType.Double => new Variant(Convert.ToDouble(value)),
        BuiltInType.String => new Variant(Convert.ToString(value)),
        _ => new Variant(value)
    };

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _gate.Dispose();
    }
}
