using System.Text.Json;
using System.Text.Json.Serialization;

namespace HadalStream.Infrastructure.Persistence;

public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // A corrupt file is kept as .bak and treated as missing.
    public static T? Load<T>(string path)
    {
        if (!File.Exists(path)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            File.Move(path, path + ".bak", overwrite: true);
            return default;
        }
    }

    // Write then rename, so a crash never leaves a half-written file.
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
