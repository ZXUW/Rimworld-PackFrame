using System.Text.Json.Serialization;

namespace PackFrame;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true
)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(InstanceMetadata))]
[JsonSerializable(typeof(ModColorData))]
[JsonSerializable(typeof(ModFullNameDocument))]
[JsonSerializable(typeof(ModpackIndex))]
[JsonSerializable(typeof(ModpackDefinition))]
[JsonSerializable(typeof(ModpackRepoManifest))]
internal partial class PackFrameJsonContext : JsonSerializerContext
{
}
