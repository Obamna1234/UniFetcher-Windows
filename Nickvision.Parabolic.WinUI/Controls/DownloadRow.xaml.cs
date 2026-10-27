using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Desktop.Globalization;
using Nickvision.Parabolic.Shared.Events;
using Nickvision.Parabolic.Shared.Models;
using Nickvision.Parabolic.Shared.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.System;

namespace Nickvision.Parabolic.WinUI.Controls;

public sealed partial class DownloadRow : UserControl
{
    private enum ButtonsPage
    {
        Running = 0,
        Done,
        Error
    }

    private readonly IThumbnailService _thumbnailService;
    private readonly ITranslationService _translator;
    private int _id;
    private string _path;
    private string _log;
    private bool _isPaused;

    public event EventHandler<int>? PauseRequested;
    public event EventHandler<int>? ResumeRequested;
    public event EventHandler<int>? StopRequested;
    public event EventHandler<int>? RetryRequested;

    public DownloadStatus Status { get; private set; }

    /// <summary>UniFetcher: plain-language reason when it failed.</summary>
    public FailureReason Reason { get; private set; }

    /// <summary>UniFetcher: failed in a way only you can fix (login, person check, VPN): shown under "Action needed".</summary>
    public bool NeedsAction => Status == DownloadStatus.Error && Reason.NeedsAction();

    /// <summary>UniFetcher: the website this download comes from.</summary>
    public string Site { get; private set; } = string.Empty;

    public string Url { get; private set; } = string.Empty;

    public string Title => LblTitle.Text;

    private DownloadCompletedEventArgs? _completedArgs;

    public DownloadRow(IThumbnailService thumbnailService, ITranslationService translator)
    {
        InitializeComponent();
        _thumbnailService = thumbnailService;
        _translator = translator;
        _id = -1;
        _path = string.Empty;
        _log = string.Empty;
        _isPaused = false;
        LblShowLog.Text = _translator._("Log");
        LblPauseResume.Text = _translator._("Pause");
        LblStop.Text = _translator._("Stop");
        LblPlay.Text = _translator._("Play");
        LblOpenFolder.Text = _translator._("Open");
        LblRetry.Text = _translator._("Retry");
        LblCopyDiagnostic.Text = _translator._("Copy diagnostic");
        ToolTipService.SetToolTip(BtnCopyDiagnostic, _translator._("Copies what went wrong and what UniFetcher tried, with cookies and private details removed, for a bug report"));
        ActualThemeChanged += (_, _) => UpdateBorder();
    }

    public async Task TriggerAddedStateAsync(DownloadAddedEventArgs args)
    {
        using var thumbnailMemoryStream = await _thumbnailService.GetImageStreamAsync(args.Url);
        using var thumbnailStream = thumbnailMemoryStream.AsRandomAccessStream();
        var thumbnailDecoder = await BitmapDecoder.CreateAsync(thumbnailStream);
        var thumbnailBitmap = await thumbnailDecoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var thumbnailSource = new SoftwareBitmapSource();
        await thumbnailSource.SetBitmapAsync(thumbnailBitmap);
        _id = args.Id;
        _path = args.Path;
        Url = args.Url.ToString();
        Site = SiteSessionService.SiteOf(args.Url);
        IcnStatus.Glyph = "\uE896";
        ImgThumbnail.Source = thumbnailSource;
        LblTitle.Text = Path.GetFileName(_path);
        ProgBar.Value = 0.0;
        if (args.Status == DownloadStatus.Queued)
        {
            Status = DownloadStatus.Queued;
            LblStatus.Text = _translator._("Queued");
            ProgBar.IsIndeterminate = false;
        }
        else if (args.Status == DownloadStatus.Running)
        {
            Status = DownloadStatus.Running;
            LblStatus.Text = _translator._("Running");
            ProgBar.IsIndeterminate = true;
        }
        else
        {
            Status = DownloadStatus.Running;
            LblStatus.Text = _translator._("Processing");
            ProgBar.IsIndeterminate = true;
        }
        ViewStackButtons.SelectedIndex = (int)ButtonsPage.Running;
    }

    public void TriggerCompletedState(DownloadCompletedEventArgs args)
    {
        _path = args.Path;
        if (args.Log.Length > 0)
        {
            _log = args.Log.ToString();
        }
        LblTitle.Text = Path.GetFileName(_path);
        LblLog.Text = _log;
        ScrollLog.ChangeView(null, ScrollLog.ScrollableHeight, null);
        ProgBar.Value = 1.0;
        ProgBar.IsIndeterminate = false;
        _completedArgs = args;
        if (args.Status == DownloadStatus.Error)
        {
            Status = DownloadStatus.Error;
            Reason = args.Reason == FailureReason.None ? FailureReason.Other : args.Reason;
            IcnStatus.Glyph = Reason.NeedsAction() ? "\uE7BA" : "\uEA39";
            // UniFetcher: one plain-language reason instead of just "Error"
            LblStatus.Text = string.IsNullOrEmpty(args.CookieSource) ? Reason.Label(_translator) : $"{Reason.Label(_translator)} · 🍪";
            ToolTipService.SetToolTip(LblStatus, Reason.Explanation(_translator) + (string.IsNullOrEmpty(args.CookieSource) ? string.Empty : "\n" + _translator._("Cookies were used, from: {0}", args.CookieSource)));
            BtnCopyDiagnostic.Visibility = Visibility.Visible;
            ViewStackButtons.SelectedIndex = (int)ButtonsPage.Error;
        }
        else if (args.Status == DownloadStatus.Success)
        {
            Status = DownloadStatus.Success;
            IcnStatus.Glyph = "\uE930";
            LblStatus.Text = _translator._("Success");
            ViewStackButtons.SelectedIndex = (int)ButtonsPage.Done;
        }
        UpdateBorder();
    }

    /// <summary>
    /// UniFetcher colour code: orange = needs your action, red = failed and can't be downloaded,
    /// grey = stopped by you, no colour = queued, downloading or finished. Help → What the colours mean explains it.
    /// </summary>
    public void UpdateBorder()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        if (NeedsAction)
        {
            RowRoot.BorderThickness = new Thickness(2);
            RowRoot.BorderBrush = new SolidColorBrush(dark ? Color(0xFF, 0xB7, 0x4D) : Color(0xE6, 0x7E, 0x00));
        }
        else if (Status == DownloadStatus.Error)
        {
            RowRoot.BorderThickness = new Thickness(2);
            RowRoot.BorderBrush = new SolidColorBrush(dark ? Color(0xE5, 0x39, 0x35) : Color(0xC6, 0x28, 0x28));
        }
        else if (Status == DownloadStatus.Stopped)
        {
            RowRoot.BorderThickness = new Thickness(2);
            RowRoot.BorderBrush = Application.Current.Resources.TryGetValue("ControlStrongStrokeColorDefaultBrush", out var grey) ? grey as Brush : new SolidColorBrush(Color(0x88, 0x88, 0x88));
        }
        else
        {
            RowRoot.BorderThickness = new Thickness(1);
            RowRoot.BorderBrush = Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var normal) ? normal as Brush : null;
        }
    }

    private static Windows.UI.Color Color(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(0xFF, r, g, b);

    private void CopyDiagnostic(object sender, RoutedEventArgs e)
    {
        var text = Diagnostic.Build(_completedArgs?.Url ?? Url, Reason, _completedArgs?.CookieSource ?? string.Empty, _completedArgs?.Steps ?? [], _log, _translator);
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        LblCopyDiagnostic.Text = _translator._("Copied");
    }

    public void TriggerPausedState()
    {
        Status = DownloadStatus.Paused;
        IcnStatus.Glyph = "\uE769";
        LblStatus.Text = _translator._("Paused");
        ProgBar.IsIndeterminate = false;
        FntPauseResume.Glyph = "\uE768";
        LblPauseResume.Text = _translator._("Resume");
        _isPaused = true;
    }

    public void TriggerProgressState(DownloadProgressChangedEventArgs args)
    {
        IcnStatus.Glyph = "\uE896";
        if (args.LogChunk.Length > 0)
        {
            _log += $"{args.LogChunk.ToString()}\n";
        }
        if (double.IsNaN(args.Progress))
        {
            LblStatus.Text = _translator._("Processing");
            ProgBar.Value = 0.0;
            ProgBar.IsIndeterminate = true;
        }
        else if (double.IsNegativeInfinity(args.Progress))
        {
            LblStatus.Text = _translator._("Sleeping for {0}", TimeSpan.FromSeconds(args.Speed).ToString("g"));
            ProgBar.Value = 0.0;
            ProgBar.IsIndeterminate = true;
        }
        else
        {
            LblStatus.Text = _translator._("{0}% Complete • {1} • {2} Remaining",
                Math.Round(args.Progress * 100, 2),
                args.Speed > 0 ? args.SpeedString : _translator._("Unknown"),
                args.Eta > 0 ? args.EtaString : _translator._("Unknown"));
            ProgBar.Value = args.Progress;
            ProgBar.IsIndeterminate = false;
        }
        LblLog.Text = _log;
        ScrollLog.ChangeView(null, ScrollLog.ScrollableHeight, null);
    }

    public void TriggerResumedState()
    {
        Status = DownloadStatus.Running;
        FntPauseResume.Glyph = "\uE769";
        LblPauseResume.Text = _translator._("Pause");
        _isPaused = false;
    }

    public void TriggerStartedFromQueueState()
    {
        Status = DownloadStatus.Running;
        LblStatus.Text = _translator._("Running");
        ProgBar.Value = 0.0;
        ProgBar.IsIndeterminate = true;
    }

    public void TriggerStoppedState()
    {
        Status = DownloadStatus.Stopped;
        IcnStatus.Glyph = "\uE783";
        LblStatus.Text = _translator._("Stopped");
        ProgBar.Value = 1.0;
        ProgBar.IsIndeterminate = false;
        BtnCopyDiagnostic.Visibility = Visibility.Collapsed;
        ViewStackButtons.SelectedIndex = (int)ButtonsPage.Error;
        UpdateBorder();
    }

    private async void OpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo()
            {
                FileName = Path.GetDirectoryName(_path)!,
                UseShellExecute = true
            });
        }
        catch
        {
            await Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(_path)!));
        }
    }

    private void PauseResume(object sender, RoutedEventArgs e)
    {
        if (_isPaused)
        {
            ResumeRequested?.Invoke(this, _id);
        }
        else
        {
            PauseRequested?.Invoke(this, _id);
        }
    }

    private async void Play(object sender, RoutedEventArgs e)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo()
            {
                FileName = _path,
                UseShellExecute = true
            });
        }
        catch
        {
            await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(_path));
        }
    }

    private void Retry(object sender, RoutedEventArgs e) => RetryRequested?.Invoke(this, _id);

    private void ShowLog(object sender, RoutedEventArgs e) => GridLog.Visibility = BtnShowLog.IsChecked ?? false ? Visibility.Visible : Visibility.Collapsed;

    private void Stop(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, _id);
}
