namespace Todoo.Business.Options;

public class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Bildirimlerin Redis'te kalma suresi (gun).</summary>
    public int RetentionDays { get; set; } = 14;

    /// <summary>Kullanici basina tutulacak maksimum bildirim sayisi.</summary>
    public int MaxPerUser { get; set; } = 50;
}
