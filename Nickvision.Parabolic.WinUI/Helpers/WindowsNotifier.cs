using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;

namespace Nickvision.Parabolic.WinUI.Helpers;

/// <summary>UniFetcher: shows Windows notifications (toasts). Text is decided by the "Notifications" setting before it gets here.</summary>
public static class WindowsNotifier
{
    private static bool _registered;
    private static bool _failed;

    public static void Show(string title, string text)
    {
        if (_failed)
        {
            return;
        }
        try
        {
            if (!_registered)
            {
                AppNotificationManager.Default.Register();
                _registered = true;
            }
            var builder = new AppNotificationBuilder().AddText(title);
            if (!string.IsNullOrEmpty(text))
            {
                builder.AddText(text);
            }
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception)
        {
            // Notifications aren't available (e.g. turned off in Windows); don't try again this session
            _failed = true;
        }
    }

    public static void Unregister()
    {
        if (_registered)
        {
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch
            {
                // closing anyway
            }
        }
    }
}
