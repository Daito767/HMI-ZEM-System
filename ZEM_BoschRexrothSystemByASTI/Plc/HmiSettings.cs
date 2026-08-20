namespace ZEM_BoschRexrothSystemByASTI.Plc;

public sealed class HmiSettings
{
    /// <summary>ctrlX / CODESYS runtimes publish OPC UA on 4840 by default.</summary>
    public string EndpointUrl { get; set; } = "opc.tcp://192.168.1.1:4840";

    /// <summary>Off means the anonymous, unencrypted endpoint - the usual case on a lab stand.</summary>
    public bool UseSecurity { get; set; }

    /// <summary>The stand's OPC UA server asks for a user, so anonymous is off by default.</summary>
    public bool Anonymous { get; set; }

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>MainTask runs at 50 ms; anything below that only costs bandwidth.</summary>
    public int PollIntervalMs { get; set; } = 250;

    /// <summary>Run against the built-in cell simulator instead of a real PLC.</summary>
    public bool UseSimulator { get; set; } = true;

    public bool AutoConnect { get; set; } = true;

    public HmiSettings Clone() => (HmiSettings)MemberwiseClone();
}

/// <summary>
/// Persists the settings so they survive a restart of the HMI. Everything goes to preferences
/// except the password, which goes to the platform's secure storage - a tablet on a shop floor is
/// not a private device.
/// </summary>
public sealed class HmiSettingsStore
{
    private const string Prefix = "hmi.";
    private const string PasswordKey = "hmi.opcua.password";

    private readonly IPreferences _preferences;
    private readonly ISecureStorage _secureStorage;

    public HmiSettingsStore(IPreferences preferences, ISecureStorage secureStorage)
    {
        _preferences = preferences;
        _secureStorage = secureStorage;
        Current = Load();
    }

    public HmiSettings Current { get; private set; }

    public event Action<HmiSettings>? Changed;

    /// <summary>Pulls the password out of secure storage. Call once, before the first connection.</summary>
    public async Task LoadPasswordAsync()
    {
        try
        {
            Current.Password = await _secureStorage.GetAsync(PasswordKey) ?? string.Empty;
        }
        catch (Exception)
        {
            // Secure storage is not available everywhere (an unpackaged Windows build, for one).
            // Losing a stored password is recoverable; failing to start the HMI is not.
            Current.Password = string.Empty;
        }
    }

    public async Task SaveAsync(HmiSettings settings)
    {
        _preferences.Set(Prefix + nameof(settings.EndpointUrl), settings.EndpointUrl);
        _preferences.Set(Prefix + nameof(settings.UseSecurity), settings.UseSecurity);
        _preferences.Set(Prefix + nameof(settings.Anonymous), settings.Anonymous);
        _preferences.Set(Prefix + nameof(settings.Username), settings.Username);
        _preferences.Set(Prefix + nameof(settings.PollIntervalMs), settings.PollIntervalMs);
        _preferences.Set(Prefix + nameof(settings.UseSimulator), settings.UseSimulator);
        _preferences.Set(Prefix + nameof(settings.AutoConnect), settings.AutoConnect);

        try
        {
            if (string.IsNullOrEmpty(settings.Password))
                _secureStorage.Remove(PasswordKey);
            else
                await _secureStorage.SetAsync(PasswordKey, settings.Password);
        }
        catch (Exception)
        {
            // Same as above: the password stays in memory for this session and is asked for again
            // on the next start.
        }

        Current = settings.Clone();
        Changed?.Invoke(Current);
    }

    private HmiSettings Load()
    {
        var defaults = new HmiSettings();
        return new HmiSettings
        {
            EndpointUrl = _preferences.Get(Prefix + nameof(defaults.EndpointUrl), defaults.EndpointUrl),
            UseSecurity = _preferences.Get(Prefix + nameof(defaults.UseSecurity), defaults.UseSecurity),
            Anonymous = _preferences.Get(Prefix + nameof(defaults.Anonymous), defaults.Anonymous),
            Username = _preferences.Get(Prefix + nameof(defaults.Username), defaults.Username),
            PollIntervalMs = _preferences.Get(Prefix + nameof(defaults.PollIntervalMs), defaults.PollIntervalMs),
            UseSimulator = _preferences.Get(Prefix + nameof(defaults.UseSimulator), defaults.UseSimulator),
            AutoConnect = _preferences.Get(Prefix + nameof(defaults.AutoConnect), defaults.AutoConnect)
        };
    }
}
