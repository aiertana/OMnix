using System.Text.Json.Serialization;
using OMniX.WinUI.Services.ModHandling;

namespace OMniX.WinUI.Services.Notifications;

public sealed class ModNotification
{
    public DateTime Time { get; init; } = DateTime.Now;
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ModId { get; init; }
    public string CharacterInternalName { get; init; } = string.Empty;
    public string ModCustomName { get; init; } = string.Empty;
    public string ModFolderName { get; init; } = string.Empty;
    public bool ShowOnOverview { get; init; }
    public AttentionType AttentionType { get; init; }
    public string Message { get; init; } = string.Empty;
    [JsonIgnore] public bool IsPersistent { get; set; }


    public ModsRetrievedResult? ModsRetrievedResult { get; init; }
}