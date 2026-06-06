using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var logPath = Environment.GetEnvironmentVariable("VBNET_ZED_DAP_TEST_LOG");
using var log = logPath is { Length: > 0 }
    ? new StreamWriter(File.Open(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8) { AutoFlush = true }
    : TextWriter.Null;

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var sequence = 1;

while (TryReadMessage(input, out var message))
{
    var node = JsonNode.Parse(message);
    if (node is null)
    {
        continue;
    }

    Log(log, node);

    var command = node["command"]?.GetValue<string>() ?? "";
    var requestSeq = node["seq"]?.GetValue<int>() ?? 0;

    Send(output, new JsonObject
    {
        ["seq"] = sequence++,
        ["type"] = "response",
        ["request_seq"] = requestSeq,
        ["success"] = true,
        ["command"] = command,
        ["body"] = command == "initialize"
            ? JsonNode.Parse("""{ "supportsConfigurationDoneRequest": true, "supportsTerminateRequest": true }""")
            : new JsonObject()
    });

    if (command == "initialize")
    {
        Send(output, Event(ref sequence, "initialized"));
    }

    if (command is "disconnect" or "terminate")
    {
        Send(output, Event(ref sequence, "terminated"));
        break;
    }
}

static bool TryReadMessage(Stream input, out string message)
{
    message = "";
    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    while (true)
    {
        var line = ReadAsciiLine(input);
        if (line is null)
        {
            return false;
        }

        if (line.Length == 0)
        {
            break;
        }

        var separator = line.IndexOf(':');
        if (separator > 0)
        {
            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
    }

    if (!headers.TryGetValue("Content-Length", out var lengthText) ||
        !int.TryParse(lengthText, out var length))
    {
        return false;
    }

    var buffer = new byte[length];
    var offset = 0;
    while (offset < length)
    {
        var read = input.Read(buffer, offset, length - offset);
        if (read == 0)
        {
            return false;
        }

        offset += read;
    }

    message = Encoding.UTF8.GetString(buffer);
    return true;
}

static string? ReadAsciiLine(Stream input)
{
    var bytes = new List<byte>();
    while (true)
    {
        var value = input.ReadByte();
        if (value < 0)
        {
            return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
        }

        if (value == '\n')
        {
            if (bytes.Count > 0 && bytes[^1] == '\r')
            {
                bytes.RemoveAt(bytes.Count - 1);
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        bytes.Add((byte)value);
    }
}

static JsonObject Event(ref int sequence, string name) => new()
{
    ["seq"] = sequence++,
    ["type"] = "event",
    ["event"] = name
};

static void Send(Stream output, JsonObject response)
{
    var payload = JsonSerializer.Serialize(response);
    var bytes = Encoding.UTF8.GetBytes(payload);
    var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
    output.Write(header);
    output.Write(bytes);
    output.Flush();
}

static void Log(TextWriter log, JsonNode message)
{
    var entry = new JsonObject
    {
        ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        ["direction"] = "received",
        ["command"] = message["command"]?.GetValue<string>(),
        ["seq"] = message["seq"]?.DeepClone(),
        ["message"] = message.DeepClone()
    };
    log.WriteLine(entry.ToJsonString());
}
