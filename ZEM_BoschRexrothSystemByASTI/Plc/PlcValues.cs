using Opc.Ua;

namespace ZEM_BoschRexrothSystemByASTI.Plc;

/// <summary>
/// The result of one batched read, addressed by logical name. A CODESYS server is free to send
/// an INT as Int16, an enum as Int16 or Int32 and a TIME as UInt32 milliseconds, so every getter
/// converts defensively rather than casting.
/// </summary>
public sealed class PlcValues
{
    private readonly DataValue?[] _values;
    private readonly IReadOnlyDictionary<string, (int Node, int? Element)> _map;

    public PlcValues(DataValue?[] values, IReadOnlyDictionary<string, (int Node, int? Element)> map)
    {
        _values = values;
        _map = map;
    }

    public bool Has(string logical) => TryGetRaw(logical, out _);

    public bool TryGetRaw(string logical, out object? value)
    {
        value = null;
        if (!_map.TryGetValue(logical, out var slot)) return false;
        if (slot.Node < 0 || slot.Node >= _values.Length) return false;

        var dataValue = _values[slot.Node];
        if (dataValue is null || StatusCode.IsBad(dataValue.StatusCode)) return false;

        var raw = dataValue.Value;
        if (slot.Element is { } index)
        {
            if (raw is not Array array) return false;
            if (index < 0 || index >= array.Length) return false;
            raw = array.GetValue(index);
        }

        value = raw;
        return raw is not null;
    }

    public bool GetBool(string logical, bool fallback = false) =>
        TryGetRaw(logical, out var v) && v is not null ? SafeConvert(() => Convert.ToBoolean(v), fallback) : fallback;

    public int GetInt(string logical, int fallback = 0) =>
        TryGetRaw(logical, out var v) && v is not null ? SafeConvert(() => Convert.ToInt32(v), fallback) : fallback;

    public uint GetUInt(string logical, uint fallback = 0) =>
        TryGetRaw(logical, out var v) && v is not null ? SafeConvert(() => Convert.ToUInt32(v), fallback) : fallback;

    public double GetDouble(string logical, double fallback = 0) =>
        TryGetRaw(logical, out var v) && v is not null ? SafeConvert(() => Convert.ToDouble(v), fallback) : fallback;

    public string GetString(string logical, string fallback = "") =>
        TryGetRaw(logical, out var v) && v is not null
            ? v as string ?? v.ToString() ?? fallback
            : fallback;

    public TEnum GetEnum<TEnum>(string logical, TEnum fallback) where TEnum : struct, Enum
    {
        if (!TryGetRaw(logical, out var v) || v is null) return fallback;
        if (v is string text)
            return Enum.TryParse<TEnum>(text, true, out var parsed) ? parsed : fallback;

        var numeric = SafeConvert(() => Convert.ToInt32(v), int.MinValue);
        if (numeric == int.MinValue) return fallback;
        return (TEnum)Enum.ToObject(typeof(TEnum), numeric);
    }

    /// <summary>CODESYS TIME comes across as milliseconds.</summary>
    public TimeSpan GetTime(string logical) =>
        TimeSpan.FromMilliseconds(GetDouble(logical));

    private static T SafeConvert<T>(Func<T> convert, T fallback)
    {
        try { return convert(); }
        catch (InvalidCastException) { return fallback; }
        catch (FormatException) { return fallback; }
        catch (OverflowException) { return fallback; }
    }
}
