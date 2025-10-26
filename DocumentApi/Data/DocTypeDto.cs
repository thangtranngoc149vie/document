using System.Text.Json.Serialization;

namespace DocumentApi.Data;

public sealed record DocTypeDto(
    Guid Id,
    string Code,
    string Name,
    [property: JsonPropertyName("is_active")] bool IsActive,
    int Order);
