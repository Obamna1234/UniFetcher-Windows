using System.ComponentModel;
using WinRT;

namespace Nickvision.Parabolic.WinUI.Helpers;

/// <summary>UniFetcher: one site with a saved session, shown in Settings.</summary>
[GeneratedBindableCustomProperty]
public sealed partial class BindableSiteSession : INotifyPropertyChanged
{
    public string Site { get; }
    public string Description { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BindableSiteSession(string site, string description)
    {
        Site = site;
        Description = description;
    }
}
