namespace PADLOck.Services;

public sealed record AdbDeviceEntry(string Serial, string State, string TransportId, string Model);

public static class AdbDevices
{
    // Формат строки: <serial> <state> usb:1-1 product:X model:X device:X transport_id:3
    public static async Task<List<AdbDeviceEntry>> ListAsync()
    {
        var adb = new AdbClient(null, null);
        var result = await adb.RunAsync("devices -l").ConfigureAwait(false);

        var list = new List<AdbDeviceEntry>();
        foreach (var raw in result.StdOut.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("List of") || line.StartsWith('*'))
                continue;

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            var props = new Dictionary<string, string>();
            foreach (var token in tokens.Skip(2))
            {
                var kv = token.Split(':', 2);
                if (kv.Length == 2)
                    props[kv[0]] = kv[1];
            }

            if (!props.TryGetValue("transport_id", out var transportId))
                continue;

            props.TryGetValue("model", out var model);
            list.Add(new AdbDeviceEntry(tokens[0], tokens[1], transportId, model ?? ""));
        }
        return list;
    }
}
