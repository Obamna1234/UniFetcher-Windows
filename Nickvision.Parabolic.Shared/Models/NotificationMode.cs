namespace Nickvision.Parabolic.Shared.Models;

/// <summary>UniFetcher: what Windows notifications show when downloads finish (spec section 7).</summary>
public enum NotificationMode
{
    /// <summary>Video titles are shown.</summary>
    Normal = 0,
    /// <summary>Generic text only: no titles, no thumbnails.</summary>
    Discreet = 1,
    /// <summary>No Windows notifications.</summary>
    Off = 2
}
