using System.Text.Json.Serialization;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Posts;

[JsonSerializable(typeof(MasjidPost))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public sealed partial class PostDetailsJsonContext : JsonSerializerContext;