namespace NS2Ping;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class IPAddressConverter : JsonConverter<IPAddress>
{
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var ip = reader.GetString();
        var ipAddress = IPAddress.Parse(ip!);

        return ipAddress;
    }

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
    {
        var ip = value.ToString();
        writer.WriteStringValue(ip);
    }
}