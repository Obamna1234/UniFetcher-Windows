using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Globalization;
using Nickvision.Desktop.Network;
using Nickvision.Desktop.Notifications;
using Nickvision.Desktop.WinUI.Helpers;
using Nickvision.Parabolic.Shared.Controllers;
using Nickvision.Parabolic.Shared.Events;
using Nickvision.Parabolic.Shared.Models;
using Nickvision.Parabolic.Shared.Services;
using Nickvision.Parabolic.WinUI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace Nickvision.Parabolic.WinUI.Views;

public sealed partial class MainWindow : Window
{
    private enum Pages
    {
        Home = 0,
        Downloads,
        Custom
    }

    private readonly IServiceProvider _serviceProvider;
    private readonly MainWindowController _controller;
    private readonly AppInfo _appInfo;
    private readonly ITranslationService _translationService;
    private readonly Dictionary<int, DownloadRow> _downloadRows;
    private RoutedEventHandler? _notificationClickHandler;
    // Only one dialog can be open at a time; links arriving from the extension wait their turn
    private readonly System.Threading.SemaphoreSlim _dialogGate = new System.Threading.SemaphoreSlim(1, 1);
    private BulkImportWindow? _bulkImportWindow;
    private LibraryWindow? _libraryWindow;
    private bool _isForeground = true;

    public MainWindow(IServiceProvider serviceProvider, MainWindowController controller, AppInfo appInfo, IEventsService eventsService, ITranslationService translationService)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _controller = controller;
        _appInfo = appInfo;
        _translationService = translationService;
        _downloadRows = new Dictionary<int, DownloadRow>();
        _notificationClickHandler = null;
        // Config
        AppWindow.TitleBar.PreferredTheme = _controller.Theme switch
        {
            Theme.Light => TitleBarTheme.Light,
            Theme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode
        };
        MainGrid.RequestedTheme = _controller.Theme switch
        {
            Theme.Light => ElementTheme.Light,
            Theme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        this.Geometry = _controller.WindowGeometry;
        // TitleBar
        AppWindow.SetIcon(_appInfo.Version!.IsPreview ? "./Assets/unifetcher-devel.ico" : "./Assets/unifetcher.ico");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        BtnPreview.Visibility = _appInfo.Version.IsPreview ? Visibility.Visible : Visibility.Collapsed;
        // Events
        AppWindow.Closing += Window_Closing;
        eventsService.AppNotificationSent += (sender, e) => DispatcherQueue.TryEnqueue(() => App_AppNotificationSent(sender, e));
        eventsService.ConfigurationSaved += App_ConfigurationSaved;
        eventsService.DownloadAdded += (sender, e) => DispatcherQueue.TryEnqueue(() => Controller_DownloadAdded(sender, e));
        eventsService.DownloadProgressChanged += (sender, e) => DispatcherQueue.TryEnqueue(() => Controller_DownloadProgressChanged(sender, e));
        eventsService.DownloadCompleted += (sender, e) => DispatcherQueue.TryEnqueue(() => Controller_DownloadCompleted(sender, e));
        eventsService.DownloadStopped += (sender, e) => DispatcherQueue.TryEnqueue(() => Controller_DownloadStopped(sender, e));
        eventsService.DownloadStartedFromQueue += (sender, e) => DispatcherQueue.TryEnqueue(() => Controller_DownloadStartedFromQueue(sender, e));
        eventsService.DownloadRetired += (sender, e) => DispatcherQueue.TryEnqueue(() => Controller_DownloadRetired(sender, e));
        _controller.LinksFromExtension += (sender, e) => DispatcherQueue.TryEnqueue(async () => await Controller_LinksFromExtensionAsync(e));
        _controller.ActionNeededChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateDownloadsList);
        _controller.ShowWindowRequested += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }
            AppWindow.Show();
            Activate();
        });
        // UniFetcher: Windows notifications only while UniFetcher isn't the window you're looking at
        _controller.WindowsNotificationRequested += (_, n) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isForeground)
            {
                Nickvision.Parabolic.WinUI.Helpers.WindowsNotifier.Show(n.Title, n.Text);
            }
        });
        Activated += (_, e) => _isForeground = e.WindowActivationState != WindowActivationState.Deactivated;
        Closed += (_, _) => Nickvision.Parabolic.WinUI.Helpers.WindowsNotifier.Unregister();
        eventsService.DownloadRequested += async (s, args) => await AddDownloadAsync(args.Url);
        // Translations
        AppWindow.Title = _appInfo.ShortName;
        LblTitle.Text = _appInfo.ShortName;
        MenuFile.Title = _translationService._("File");
        MenuAddDownload.Text = _translationService._("Add Download");
        MenuBulkImport.Text = _translationService._("Bulk Import…");
        MenuExit.Text = _translationService._("Exit");
        MenuShowDownloads.Text = _translationService._("Show Downloads Folder");
        MenuShowLast.Text = _translationService._("Show Last Download");
        MenuEdit.Title = _translationService._("Edit");
        MenuKeyring.Text = _translationService._("Keyring");
        MenuSettings.Text = _translationService._("Settings");
        MenuView.Title = _translationService._("View");
        MenuHistory.Text = _translationService._("Downloads Library");
        MenuDownloads.Title = _translationService._("Downloads");
        MenuStopAllRemaining.Text = _translationService._("Stop All Remaining");
        MenuRetryAllFailed.Text = _translationService._("Retry All Failed");
        MenuClearAllQueued.Text = _translationService._("Clear All Queued");
        MenuClearAllCompleted.Text = _translationService._("Clear All Completed");
        MenuHelp.Title = _translationService._("Help");
        MenuCheckForUpdates.Text = _translationService._("Check for Updates");
        MenuGitHubRepo.Text = _translationService._("GitHub Repo");
        MenuReportABug.Text = _translationService._("Report a Bug");
        MenuAbout.Text = _translationService._("About {0}", _appInfo.ShortName!);
        ToolTipService.SetToolTip(BtnPreview, _translationService._("You are running a preview version of {0}", _appInfo.ShortName!));
        LblPreview.Text = _translationService._("Thank you for testing the upcoming features and changes! ❤️");
        LblHomeTitle.Text = _translationService._("Download Media");
        LblHomeDescription.Text = _translationService._("Add a video, audio, or playlist URL to start downloading");
        LblAddDownload.Text = _translationService._("Add Download");
        LblKeyring.Text = _translationService._("Keyring");
        LblSettings.Text = _translationService._("Settings");
        BtnStopAllRemaining.Label = _translationService._("Stop All Remaining");
        BtnRetryAllFailed.Label = _translationService._("Retry All Failed");
        BtnClearAllQueued.Label = _translationService._("Clear All Queued");
        BtnClearAllCompleted.Label = _translationService._("Clear All Completed");
        LblDownloadsAddDownload.Text = _translationService._("Add");
        NavDownloadsAll.Content = _translationService._("All");
        NavDownloadsRunning.Content = _translationService._("Downloading");
        NavDownloadsActionNeeded.Content = _translationService._("Action needed");
        NavDownloadsQueued.Content = _translationService._("Queued");
        NavDownloadsCompleted.Content = _translationService._("Finished");
        NavDownloadsFailed.Content = _translationService._("Failed");
        NavDownloadsStopped.Content = _translationService._("Stopped");
        BtnBannerView.Content = _translationService._("View");
        MenuResumeStopped.Text = _translationService._("Resume Stopped");
        MenuClearFailedStopped.Text = _translationService._("Clear Failed and Stopped");
        MenuColourKey.Text = _translationService._("What the Colours Mean");
        StatusNoneDownloads.Title = _translationService._("No Downloads");
        StatusNoneDownloads.Description = _translationService._("There are no downloads of this type");
        LblNoneAddDownload.Text = _translationService._("Add Download");
        DlgCredential.Title = _translationService._("Credential Required");
        TxtCredentialUsername.PlaceholderText = _translationService._("Enter username here");
        TxtCredentialPassword.PlaceholderText = _translationService._("Enter password here");
        DlgCredential.PrimaryButtonText = _translationService._("Submit");
        DlgCredential.CloseButtonText = _translationService._("Cancel");
    }

    private async void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        ViewStack.SelectedIndex = (int)Pages.Home;
        ViewStackDownloads.SelectedIndex = 0;
        _ = _controller.ReportWaitingAsync();
        MenuCheckForUpdates.IsEnabled = false;
        var updatesTask = _controller.CheckForUpdatesAsync(false);
        if (_controller.ShowDisclaimerOnStartup)
        {
            var checkBox = new CheckBox()
            {
                Content = _translationService._("Don't show this message again")
            };
            var disclaimerDialog = new ContentDialog()
            {
                Title = _translationService._("Legal Copyright Disclaimer"),
                Content = new StackPanel()
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock()
                        {
                            Text = _translationService._("Videos on YouTube and other sites may be subject to DMCA protection. The authors of UniFetcher do not endorse, and are not responsible for, the use of this application in means that will violate these laws."),
                            TextWrapping = TextWrapping.WrapWholeWords
                        },
                        checkBox
                    }
                },
                CloseButtonText = _translationService._("I understand"),
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = MainGrid.ActualTheme,
                XamlRoot = MainGrid.XamlRoot
            };
            await disclaimerDialog.ShowAsync();
            if (checkBox.IsChecked ?? false)
            {
                _controller.ShowDisclaimerOnStartup = false;
            }
        }
        if (_controller.RecoverableDownloadsCount > 0)
        {
            var recoverDialog = new ContentDialog()
            {
                Title = _translationService._("Recover Downloads?"),
                Content = _translationService._("There are downloads available to recover from when UniFetcher crashed. Would you like to download them again?"),
                PrimaryButtonText = _translationService._("Yes"),
                CloseButtonText = _translationService._("No"),
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = MainGrid.ActualTheme,
                XamlRoot = MainGrid.XamlRoot
            };
            if ((await recoverDialog.ShowAsync()) == ContentDialogResult.Primary)
            {
                await _controller.RecoverAllDownloadsAsync();
            }
            else
            {
                await _controller.ClearRecoverableDownloadsAsync();
            }
        }
        if (_controller.UrlFromArgs is not null)
        {
            await AddDownloadAsync(_controller.UrlFromArgs);
        }
        await updatesTask;
        MenuCheckForUpdates.IsEnabled = true;
    }

    private async void Window_Closing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (!_controller.CanShutdown)
        {
            e?.Cancel = true;
            var confirmDialog = new ContentDialog()
            {
                Title = _appInfo.ShortName,
                Content = _translationService._("There are downloads still in progress. Would you like to stop them and exit?"),
                PrimaryButtonText = _translationService._("Yes"),
                CloseButtonText = _translationService._("No"),
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = MainGrid.ActualTheme,
                XamlRoot = MainGrid.XamlRoot
            };
            if ((await confirmDialog.ShowAsync()) == ContentDialogResult.Primary)
            {
                _controller.WindowGeometry = this.Geometry;
                await _controller.StopAllDownloadsAsync();
                Close();
                _serviceProvider.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                ExitWatchdog();
            }
            return;
        }
        _controller.WindowGeometry = this.Geometry;
        _serviceProvider.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        ExitWatchdog();
    }

    /// <summary>UniFetcher: if shutting down hangs, end the process anyway so it never lingers without a window.</summary>
    private static void ExitWatchdog()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(15));
            Environment.Exit(0);
        })
        {
            IsBackground = true
        };
        thread.Start();
    }

    private void App_AppNotificationSent(object? sender, AppNotificationSentEventArgs e)
    {
        if (_notificationClickHandler is not null)
        {
            BtnInfoBar.Click -= _notificationClickHandler;
            _notificationClickHandler = null;
        }
        InfoBar.Message = e.Notification.Message;
        InfoBar.Severity = e.Notification.Severity switch
        {
            NotificationSeverity.Success => InfoBarSeverity.Success,
            NotificationSeverity.Warning => InfoBarSeverity.Warning,
            NotificationSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
        if (e.Notification.Action == "update")
        {
            BtnInfoBar.Content = _translationService._("Update");
            _notificationClickHandler = WindowsUpdate;
            BtnInfoBar.Click += _notificationClickHandler;
        }
        else if (e.Notification.Action == "update-ytdlp")
        {
            BtnInfoBar.Content = _translationService._("Update");
            _notificationClickHandler = YtdlpUpdate;
            BtnInfoBar.Click += _notificationClickHandler;
        }
        else if (e.Notification.Action == "needs-action" && !string.IsNullOrEmpty(e.Notification.ActionParam))
        {
            // UniFetcher: logins / human checks happen in the UniFetcher Firefox profile
            BtnInfoBar.Content = _translationService._("Open in browser");
            var url = e.Notification.ActionParam;
            _notificationClickHandler = async (_, _) =>
            {
                InfoBar.IsOpen = false;
                await _controller.OpenInProfileAsync(url);
            };
            BtnInfoBar.Click += _notificationClickHandler;
        }
        else if (e.Notification.Action == "bulk-report" && !string.IsNullOrEmpty(e.Notification.ActionParam))
        {
            BtnInfoBar.Content = _translationService._("Open report");
            var jobId = e.Notification.ActionParam;
            _notificationClickHandler = async (_, _) =>
            {
                InfoBar.IsOpen = false;
                await GetBulkImportWindow().ShowJobAsync(jobId);
            };
            BtnInfoBar.Click += _notificationClickHandler;
        }
        else if (e.Notification.Action == "action-needed")
        {
            BtnInfoBar.Content = _translationService._("View");
            _notificationClickHandler = (_, _) =>
            {
                InfoBar.IsOpen = false;
                ViewActionNeeded(null, new RoutedEventArgs());
            };
            BtnInfoBar.Click += _notificationClickHandler;
        }
        else if (e.Notification.Action == "update-deno")
        {
            BtnInfoBar.Content = _translationService._("Update");
            _notificationClickHandler = DenoUpdate;
            BtnInfoBar.Click += _notificationClickHandler;
        }
        else if (e.Notification.Action == "error" && !string.IsNullOrEmpty(e.Notification.ActionParam))
        {
            BtnInfoBar.Content = _translationService._("Details");
            _notificationClickHandler = async (_, _) =>
            {
                InfoBar.IsOpen = false;
                var errorDialog = new ContentDialog()
                {
                    Title = _translationService._("Error"),
                    Content = new ScrollViewer()
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                        Content = new TextBlock()
                        {
                            Text = e.Notification.ActionParam,
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                    CloseButtonText = _translationService._("Close"),
                    DefaultButton = ContentDialogButton.Close,
                    RequestedTheme = MainGrid.ActualTheme,
                    XamlRoot = MainGrid.XamlRoot
                };
                await errorDialog.ShowAsync();
            };
            BtnInfoBar.Click += _notificationClickHandler;
        }
        BtnInfoBar.Visibility = _notificationClickHandler is not null ? Visibility.Visible : Visibility.Collapsed;
        InfoBar.IsOpen = true;
    }

    private void App_ConfigurationSaved(object? sender, ConfigurationSavedEventArgs args)
    {
        if (args.ChangedPropertyName == "Theme")
        {
            AppWindow.TitleBar.PreferredTheme = _controller.Theme switch
            {
                Theme.Light => TitleBarTheme.Light,
                Theme.Dark => TitleBarTheme.Dark,
                _ => TitleBarTheme.UseDefaultAppMode
            };
            MainGrid.RequestedTheme = _controller.Theme switch
            {
                Theme.Light => ElementTheme.Light,
                Theme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        TitleBar.IsBackButtonVisible = false;
        ViewStack.SelectedIndex = ViewStack.PreviousSelectedIndex;
    }

    private async void Controller_DownloadAdded(object? sender, DownloadAddedEventArgs e)
    {
        var row = _serviceProvider.GetRequiredService<DownloadRow>();
        row.PauseRequested += DownloadRow_PauseRequested;
        row.ResumeRequested += DownloadRow_ResumeRequested;
        row.StopRequested += DownloadRow_StopRequested;
        row.RetryRequested += DownloadRow_RetryRequested;
        await row.TriggerAddedStateAsync(e);
        _downloadRows[e.Id] = row;
        UpdateDownloadsList();
        ViewStack.SelectedIndex = (int)Pages.Downloads;
        TitleBar.IsBackButtonVisible = false;
    }

    private void Controller_DownloadCompleted(object? sender, DownloadCompletedEventArgs e)
    {
        if (_downloadRows.TryGetValue(e.Id, out var row))
        {
            row.TriggerCompletedState(e);
            UpdateDownloadsList();
        }
    }

    private void Controller_DownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
    {
        if (_downloadRows.TryGetValue(e.Id, out var row))
        {
            row.TriggerProgressState(e);
        }
    }

    private void Controller_DownloadStartedFromQueue(object? sender, DownloadEventArgs e)
    {
        if (_downloadRows.TryGetValue(e.Id, out var row))
        {
            row.TriggerStartedFromQueueState();
            UpdateDownloadsList();
        }
    }

    private void Controller_DownloadStopped(object? sender, DownloadEventArgs e)
    {
        if (_downloadRows.TryGetValue(e.Id, out var row))
        {
            row.TriggerStoppedState();
            UpdateDownloadsList();
        }
    }

    private void Controller_DownloadRetired(object? sender, DownloadEventArgs e)
    {
        if (_downloadRows.TryGetValue(e.Id, out var row))
        {
            _downloadRows.Remove(e.Id);
            UpdateDownloadsList();
        }
    }

    private void DownloadRow_PauseRequested(object? sender, int id)
    {
        if (_controller.PauseDownload(id) && _downloadRows.TryGetValue(id, out var row))
        {
            row.TriggerPausedState();
        }
    }

    private void DownloadRow_ResumeRequested(object? sender, int id)
    {
        if (_controller.ResumeDownload(id) && _downloadRows.TryGetValue(id, out var row))
        {
            row.TriggerResumedState();
        }
    }

    private async void DownloadRow_RetryRequested(object? sender, int id) => await _controller.RetryDownloadAsync(id);

    private async void DownloadRow_StopRequested(object? sender, int id) => await _controller.StopDownloadAsync(id);

    private void NavViewDownloads_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) => UpdateDownloadsList();

    private void UpdateProgress_Changed(object? sender, DownloadProgress e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Completed)
            {
                FlyoutUpdateProgress.Hide();
                BtnUpdateProgress.Visibility = Visibility.Collapsed;
                return;
            }
            var message = _translationService._("Downloading update: {0}%", Math.Round(e.Percentage * 100));
            BtnUpdateProgress.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(BtnUpdateProgress, message);
            RingUpdateProcess.Value = e.Percentage * 100;
            LblUpdateProgress.Text = message;
        });
    }

    private async void About(object? sender, RoutedEventArgs e)
    {
        var progressDialog = new ContentDialog()
        {
            Title = _translationService._("About {0}", _appInfo.ShortName!),
            Content = new ProgressRing()
            {
                IsActive = true,
            },
            RequestedTheme = MainGrid.ActualTheme,
            XamlRoot = MainGrid.XamlRoot
        };
        DispatcherQueue.TryEnqueue(async () => await progressDialog.ShowAsync());
        var aboutDialog = _serviceProvider.GetRequiredService<AboutDialog>();
        aboutDialog.DebugInformation = await _controller.GetDebugInformationAsync();
        aboutDialog.RequestedTheme = MainGrid.ActualTheme;
        aboutDialog.XamlRoot = MainGrid.XamlRoot;
        progressDialog.Hide();
        await aboutDialog.ShowAsync();
    }

    private async void AddDownload(object? sender, RoutedEventArgs e) => await AddDownloadAsync(null);

    private void ShowDownloadsFolder(object sender, RoutedEventArgs args) => _controller.ShowDownloadsFolder();

    private void ShowLastDownload(object sender, RoutedEventArgs args) => _controller.ShowLastDownload();

    private void Exit(object sender, RoutedEventArgs args) => Window_Closing(AppWindow, null);

    private async void Keyring(object sender, RoutedEventArgs args)
    {
        var keyringDialog = _serviceProvider.GetRequiredService<KeyringDialog>();
        keyringDialog.RequestedTheme = MainGrid.ActualTheme;
        keyringDialog.XamlRoot = MainGrid.XamlRoot;
        await keyringDialog.ShowAsync();
    }

    private void Settings(object sender, RoutedEventArgs args)
    {
        TitleBar.IsBackButtonVisible = true;
        ViewStack.SelectedIndex = (int)Pages.Custom;
        var settings = _serviceProvider.GetRequiredService<SettingsPage>();
        settings.WindowId = AppWindow.Id;
        FrameCustom.Content = settings;
    }

    private void History(object sender, RoutedEventArgs args)
    {
        // UniFetcher: the downloads library replaces Parabolic's history dialog
        if (_libraryWindow is null)
        {
            _libraryWindow = new LibraryWindow(_serviceProvider.GetRequiredService<LibraryController>(), _translationService);
            _libraryWindow.Closed += (_, _) => _libraryWindow = null;
        }
        _libraryWindow.Activate();
    }

    private async void CheckForUpdates(object? sender, RoutedEventArgs e)
    {
        MenuCheckForUpdates.IsEnabled = false;
        await _controller.CheckForUpdatesAsync(true);
        MenuCheckForUpdates.IsEnabled = true;
    }

    private void ClearAllCompleted(object? sender, RoutedEventArgs e)
    {
        foreach (var id in _controller.ClearCompletedDownloads())
        {
            _downloadRows.Remove(id);
        }
        UpdateDownloadsList();
    }

    private void ClearAllQueued(object? sender, RoutedEventArgs e)
    {
        foreach (var id in _controller.ClearQueuedDownloads())
        {
            _downloadRows.Remove(id);
        }
        UpdateDownloadsList();
    }

    private async void DenoUpdate(object? sender, RoutedEventArgs e)
    {
        var progress = new Progress<DownloadProgress>();
        progress.ProgressChanged += UpdateProgress_Changed;
        InfoBar.IsOpen = false;
        await _controller.DenoUpdateAsync(progress);
        progress.ProgressChanged -= UpdateProgress_Changed;
    }

    private async void GitHubRepo(object? sender, RoutedEventArgs e) => await LaunchUriAsync(_appInfo.SourceRepository);

    private async void ReportABug(object? sender, RoutedEventArgs e) => await LaunchUriAsync(_appInfo.IssueTracker);

    private async void RetryAllFailed(object? sender, RoutedEventArgs e) => await _controller.RetryFailedDownloadsAsync();

    private async void StopAllRemaining(object? sender, RoutedEventArgs e) => await _controller.StopAllDownloadsAsync();

    private async void WindowsUpdate(object? sender, RoutedEventArgs e)
    {
        var progress = new Progress<DownloadProgress>();
        progress.ProgressChanged += UpdateProgress_Changed;
        InfoBar.IsOpen = false;
        await _controller.WindowsUpdateAsync(progress);
        progress.ProgressChanged -= UpdateProgress_Changed;
    }

    private async void YtdlpUpdate(object? sender, RoutedEventArgs e)
    {
        var progress = new Progress<DownloadProgress>();
        progress.ProgressChanged += UpdateProgress_Changed;
        InfoBar.IsOpen = false;
        await _controller.YtdlpUpdateAsync(progress);
        progress.ProgressChanged -= UpdateProgress_Changed;
    }

    /// <summary>UniFetcher: a page/link/video or all tabs sent from the browser extension.</summary>
    private async Task Controller_LinksFromExtensionAsync(LinksReceivedEventArgs e)
    {
        Activate();
        if (e.IsBulk || e.Urls.Count > 1)
        {
            await OpenBulkImportAsync(e.Urls);
            return;
        }
        await _dialogGate.WaitAsync();
        try
        {
            await AddDownloadAsync(e.Urls[0]);
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    /// <summary>UniFetcher: opens (or brings back) the Bulk Import window.</summary>
    private BulkImportWindow GetBulkImportWindow()
    {
        if (_bulkImportWindow is null)
        {
            _bulkImportWindow = new BulkImportWindow(_serviceProvider.GetRequiredService<BulkImportController>(), _translationService);
            _bulkImportWindow.Closed += (_, _) => _bulkImportWindow = null;
        }
        _bulkImportWindow.Activate();
        return _bulkImportWindow;
    }

    private async Task OpenBulkImportAsync(IReadOnlyList<Uri> urls) => await GetBulkImportWindow().LoadLinksAsync(urls);

    private async void BulkImport(object? sender, RoutedEventArgs e) => await GetBulkImportWindow().ShowInputAsync();

    private void MainGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = _translationService._("Bulk Import these links");
        }
    }

    private async void MainGrid_Drop(object sender, DragEventArgs e)
    {
        // UniFetcher: drop an exported list (bookmarks HTML, JSON, CSV, text) onto the window to Bulk Import it
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 0 && items[0] is Windows.Storage.StorageFile file)
            {
                await GetBulkImportWindow().LoadFileAsync(file.Path);
            }
        }
    }

    private async Task AddDownloadAsync(Uri? uri)
    {
        var addDownloadDialog = _serviceProvider.GetRequiredService<AddDownloadDialog>();
        addDownloadDialog.WindowId = AppWindow.Id;
        addDownloadDialog.RequestedTheme = MainGrid.ActualTheme;
        addDownloadDialog.XamlRoot = MainGrid.XamlRoot;
        if (uri is not null)
        {
            await addDownloadDialog.ShowAsync(uri);
        }
        else
        {
            await addDownloadDialog.ShowAsync();
        }
        if (!string.IsNullOrEmpty(addDownloadDialog.ImportFilePath))
        {
            await GetBulkImportWindow().LoadFileAsync(addDownloadDialog.ImportFilePath);
        }
    }

    private async Task LaunchUriAsync(Uri? uri)
    {
        if (uri is null)
        {
            return;
        }
        await Launcher.LaunchUriAsync(uri);
    }

    private void UpdateDownloadsList()
    {
        var selectedTag = ((NavViewDownloads.SelectedItem as NavigationViewItem)?.Tag as string) ?? string.Empty;
        var rows = new List<DownloadRow>(_downloadRows.Count);
        foreach (var row in _downloadRows.Values)
        {
            if (selectedTag switch
            {
                "1" => row.Status == DownloadStatus.Running || row.Status == DownloadStatus.Paused,
                "2" => row.Status == DownloadStatus.Queued,
                "3" => row.Status == DownloadStatus.Success,
                "4" => row.Status == DownloadStatus.Error && !row.NeedsAction,
                "5" => row.NeedsAction,
                "6" => row.Status == DownloadStatus.Stopped,
                _ => true
            })
            {
                rows.Add(row);
            }
        }
        rows.Reverse();
        ListDownloads.ItemsSource = rows;
        if (selectedTag == "5")
        {
            // Action needed: grouped by site, including links kept from an earlier session
            _ = RenderActionNeededAsync();
        }
        else
        {
            ViewStackDownloads.SelectedIndex = rows.Count > 0 ? 1 : 0;
        }
        SetBadge(InfoBadgeDownloadsAll, _controller.RemainingDownloadsCount);
        SetBadge(InfoBadgeDownloadsRunning, _controller.RunningDownloadsCount);
        SetBadge(InfoBadgeDownloadsQueued, _controller.QueuedDownloadsCount);
        SetBadge(InfoBadgeDownloadsCompleted, _controller.FinishedDownloadsCount);
        SetBadge(InfoBadgeDownloadsFailed, _controller.FailedDownloadsCount);
        SetBadge(InfoBadgeDownloadsStopped, _controller.StoppedDownloadsCount);
        _ = UpdateActionNeededBannerAsync();
    }

    private static void SetBadge(InfoBadge badge, int value)
    {
        badge.Value = value;
        badge.Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- UniFetcher: Action needed ----------------

    private bool IsDark => MainGrid.ActualTheme == ElementTheme.Dark;

    private static Windows.UI.Color Rgb(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(0xFF, r, g, b);

    private Windows.UI.Color Orange => IsDark ? Rgb(0xFF, 0xB7, 0x4D) : Rgb(0xE6, 0x7E, 0x00);

    /// <summary>Everything waiting for you: failed rows from now, plus links kept from before UniFetcher was closed, by site.</summary>
    private async Task<List<(string Site, List<string> Titles, FailureReason Reason, string FirstUrl)>> GetWaitingGroupsAsync()
    {
        var groups = new Dictionary<string, (List<string> Titles, FailureReason Reason, string FirstUrl, HashSet<string> Urls)>();
        foreach (var row in _downloadRows.Values.Where(r => r.NeedsAction))
        {
            if (!groups.TryGetValue(row.Site, out var g))
            {
                g = (new List<string>(), row.Reason, row.Url, new HashSet<string>());
                groups[row.Site] = g;
            }
            if (g.Urls.Add(row.Url))
            {
                g.Titles.Add(row.Title);
            }
        }
        foreach (var link in await _controller.GetWaitingLinksAsync())
        {
            if (!groups.TryGetValue(link.Site, out var g))
            {
                g = (new List<string>(), link.Reason, link.Url, new HashSet<string>());
                groups[link.Site] = g;
            }
            if (g.Urls.Add(link.Url))
            {
                g.Titles.Add(string.IsNullOrEmpty(link.Options.SaveFilename) ? link.Url : link.Options.SaveFilename);
            }
        }
        return groups.Select(x => (x.Key, x.Value.Titles, x.Value.Reason, x.Value.FirstUrl)).OrderBy(x => x.Key).ToList();
    }

    private async Task UpdateActionNeededBannerAsync()
    {
        var groups = await GetWaitingGroupsAsync();
        var videos = groups.Sum(g => g.Titles.Count);
        SetBadge(InfoBadgeDownloadsActionNeeded, videos);
        if (videos == 0)
        {
            BannerActionNeeded.Visibility = Visibility.Collapsed;
            NavDownloadsActionNeeded.Foreground = null;
            return;
        }
        BannerActionNeeded.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(IsDark ? Rgb(0x4A, 0x2E, 0x00) : Rgb(0xFF, 0xE6, 0xC2));
        IcnBannerActionNeeded.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Orange);
        NavDownloadsActionNeeded.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Orange);
        LblBannerActionNeeded.Text = _translationService._n("{0} video needs your action", "{0} videos need your action", videos, videos) + " · " + _translationService._n("{0} site", "{0} sites", groups.Count, groups.Count);
        BannerActionNeeded.Visibility = Visibility.Visible;
    }

    private async Task RenderActionNeededAsync()
    {
        var groups = await GetWaitingGroupsAsync();
        PanelActionNeeded.Children.Clear();
        if (groups.Count == 0)
        {
            ViewStackDownloads.SelectedIndex = 0;
            return;
        }
        PanelActionNeeded.Children.Add(new TextBlock
        {
            Text = _translationService._("These need you: open the site in the UniFetcher browser, log in or complete the check, then press Done. UniFetcher then retries every waiting video from that site with a fresh session. Nothing here expires."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 6)
        });
        foreach (var group in groups)
        {
            var card = new Border
            {
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Orange),
                Background = Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var bg) ? bg as Microsoft.UI.Xaml.Media.Brush : null
            };
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = group.Site, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 });
            text.Children.Add(new TextBlock { Text = _translationService._n("{0} video waiting", "{0} videos waiting", group.Titles.Count, group.Titles.Count) + " · " + group.Reason.Label(_translationService), Opacity = 0.8 });
            text.Children.Add(new TextBlock { Text = group.Reason.Explanation(_translationService), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 });
            var titles = string.Join("\n", group.Titles.Take(8)) + (group.Titles.Count > 8 ? "\n" + _translationService._("…and {0} more", group.Titles.Count - 8) : string.Empty);
            text.Children.Add(new TextBlock { Text = titles, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.6, Margin = new Thickness(0, 4, 0, 0) });
            grid.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var open = new Button { Content = _translationService._("Open") };
            ToolTipService.SetToolTip(open, _translationService._("Opens the site in the UniFetcher browser (not your everyday browser)"));
            open.Click += async (_, _) => await _controller.OpenInProfileAsync(group.FirstUrl);
            var done = new Button { Content = _translationService._("Done"), Style = Application.Current.Resources["AccentButtonStyle"] as Style };
            ToolTipService.SetToolTip(done, _translationService._("Retry every waiting video from this site"));
            done.Click += async (_, _) =>
            {
                done.IsEnabled = false;
                await _controller.DoneForSiteAsync(group.Site);
                UpdateDownloadsList();
            };
            var remove = new Button { Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 } };
            ToolTipService.SetToolTip(remove, _translationService._("Remove these from the list (nothing is downloaded)"));
            remove.Click += async (_, _) =>
            {
                await _controller.RemoveWaitingSiteAsync(group.Site);
                UpdateDownloadsList();
            };
            buttons.Children.Add(open);
            buttons.Children.Add(done);
            buttons.Children.Add(remove);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
            card.Child = grid;
            PanelActionNeeded.Children.Add(card);
        }
        ViewStackDownloads.SelectedIndex = 2;
    }

    private void ViewActionNeeded(object? sender, RoutedEventArgs e)
    {
        ViewStack.SelectedIndex = (int)Pages.Downloads;
        NavViewDownloads.SelectedItem = NavDownloadsActionNeeded;
        UpdateDownloadsList();
    }

    private async void ResumeStopped(object? sender, RoutedEventArgs e) => await _controller.ResumeStoppedAsync();

    private async void ClearFailedAndStopped(object? sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = _translationService._("Clear failed and stopped downloads?"),
            Content = _translationService._("They're removed from the list, including the Action needed list. Finished downloads and your files aren't touched."),
            PrimaryButtonText = _translationService._("Clear"),
            CloseButtonText = _translationService._("Cancel"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = MainGrid.ActualTheme,
            XamlRoot = MainGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }
        foreach (var id in await _controller.ClearFailedAndStoppedAsync())
        {
            _downloadRows.Remove(id);
        }
        UpdateDownloadsList();
    }

    private async void ShowColourKey(object? sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 10 };
        void Row(Windows.UI.Color? colour, string label, string meaning)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            line.Children.Add(new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(colour is null ? 1 : 3),
                BorderBrush = colour is null ? (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var b) ? b as Microsoft.UI.Xaml.Media.Brush : null) : new Microsoft.UI.Xaml.Media.SolidColorBrush(colour.Value)
            });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = meaning, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, MaxWidth = 380 });
            line.Children.Add(text);
            panel.Children.Add(line);
        }
        var grey = Application.Current.Resources.TryGetValue("ControlStrongStrokeColorDefault", out var g) && g is Windows.UI.Color gc ? gc : Rgb(0x88, 0x88, 0x88);
        Row(Orange, _translationService._("Orange: needs your action"), _translationService._("A login, a \"verify you are human\" check, or a site blocking your VPN. See Action needed."));
        Row(IsDark ? Rgb(0xE5, 0x39, 0x35) : Rgb(0xC6, 0x28, 0x28), _translationService._("Red: failed"), _translationService._("It can't be downloaded (e.g. site not supported, removed, DRM). The status says why."));
        Row(grey, _translationService._("Grey: stopped"), _translationService._("You stopped it. Downloads → Resume Stopped puts it back in the queue."));
        Row(null, _translationService._("No colour"), _translationService._("Queued, downloading or finished."));
        var dialog = new ContentDialog
        {
            Title = _translationService._("What the colours mean"),
            Content = panel,
            CloseButtonText = _translationService._("Close"),
            RequestedTheme = MainGrid.ActualTheme,
            XamlRoot = MainGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
