using OMniX.WinUI.Services.Notifications;
using Newtonsoft.Json;

namespace OMniX.WinUI.Models.Settings;

public class ModAttentionSettings
{
    [JsonIgnore] public const string Key = "ModAttentionSettings";

    public Dictionary<string, ModNotification[]> ModNotifications { get; set; } = new();
}