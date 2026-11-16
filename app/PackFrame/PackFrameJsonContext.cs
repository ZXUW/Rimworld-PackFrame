using System.Text.Json.Serialization;

namespace PackFrame;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true
)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, bool>))]
[JsonSerializable(typeof(InstanceMetadata))]
[JsonSerializable(typeof(ModColorData))]
[JsonSerializable(typeof(ModNoteData))]
[JsonSerializable(typeof(ModFullNameDocument))]
[JsonSerializable(typeof(ModpackIndex))]
[JsonSerializable(typeof(ModpackCatalogDocument))]
[JsonSerializable(typeof(ModpackDetailsDocument))]
[JsonSerializable(typeof(ModpackDefinition))]
[JsonSerializable(typeof(ModpackRepoManifest))]
[JsonSerializable(typeof(PreparedAppUpdate))]
[JsonSerializable(typeof(AppUpdatePlan))]
[JsonSerializable(typeof(AppUpdateResult))]
[JsonSerializable(typeof(CommunityRulesCacheEntry))]
internal partial class PackFrameJsonContext : JsonSerializerContext
{
}
