using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Nickvision.Desktop.Globalization;
using Nickvision.Parabolic.Shared.Controllers;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Nickvision.Parabolic.WinUI.Views;

/// <summary>
/// UniFetcher Bulk Import (spec section 5): paste, open or drop a list of links (or "Send all tabs" from the extension),
/// check the summary, start, and follow progress until the end report.
/// Built in code (no XAML) to keep it self-contained.
/// </summary>
public sealed class BulkImportWindow : Window
{
    private readonly BulkImportController _controller;
    private readonly ITranslationService _t;
    private readonly Grid _root;
    private readonly DispatcherQueueTimer _refreshTimer;
    private BulkPlan? _plan;
    private string? _jobId;
    private bool _refreshPending;

    public BulkImportWindow(BulkImportController controller, ITranslationService translationService)
    {
        _controller = controller;
        _t = translationService;
        Title = _t._("UniFetcher – Bulk Import");
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 760));
        AppWindow.SetIcon("./Assets/unifetcher.ico");
        _root = new Grid { Padding = new Thickness(24), AllowDrop = true };
        _root.DragOver += Root_DragOver;
        _root.Drop += Root_Drop;
        Content = _root;
        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(1);
        _refreshTimer.Tick += async (_, _) =>
        {
            if (_refreshPending && _jobId is not null)
            {
                _refreshPending = false;
                await ShowProgressAsync(_jobId);
            }
        };
        _refreshTimer.Start();
        _controller.JobChanged += (_, id) =>
        {
            if (id == _jobId)
            {
                _refreshPending = true;
            }
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    // ---------------- Entry points ----------------

    public async Task LoadLinksAsync(IReadOnlyList<Uri> urls) => await ShowSummaryAsync(await _controller.PlanFromLinksAsync(urls));

    public async Task LoadFileAsync(string path)
    {
        try
        {
            await ShowSummaryAsync(await _controller.PlanFromFileAsync(path));
        }
        catch (Exception e)
        {
            await ShowMessageAsync(_t._("Couldn't read that file"), e.Message);
        }
    }

    public async Task ShowJobAsync(string jobId) => await ShowProgressAsync(jobId);

    // ---------------- Step 1: input ----------------

    public async Task ShowInputAsync()
    {
        _jobId = null;
        var panel = Page(_t._("Bulk Import"));
        panel.Children.Add(Text(_t._("Paste links below (one per line, or any text with links in it), open an exported file, or drag a file onto this window. Works with plain text, bookmarks HTML exports, Firefox bookmark backups (JSON), tab-manager exports (JSON, CSV, OneTab) and documents: PDF, Word, OpenDocument, RTF, e-mails (.eml), EPUB and Samsung Notes files."), wrap: true));
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 300,
            PlaceholderText = _t._("https://…\nhttps://…"),
            FontFamily = new FontFamily("Consolas")
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        panel.Children.Add(box);
        var buttons = Row();
        var next = Primary(_t._("Next"));
        next.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                await ShowMessageAsync(_t._("Nothing to import"), _t._("Paste some links first, or open a file."));
                return;
            }
            await ShowSummaryAsync(await _controller.PlanFromTextAsync(box.Text));
        };
        var open = new Button { Content = _t._("Import from file…") };
        open.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.Downloads, FileTypeFilter = { "*", ".txt", ".pdf", ".docx", ".doc", ".odt", ".rtf", ".sdocx", ".eml", ".mht", ".html", ".htm", ".json", ".csv", ".md", ".epub", ".xlsx", ".pptx", ".url" } };
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await LoadFileAsync(file.Path);
            }
        };
        buttons.Children.Add(next);
        buttons.Children.Add(open);
        panel.Children.Add(buttons);
        var jobs = await _controller.GetJobsAsync();
        if (jobs.Count > 0)
        {
            panel.Children.Add(Heading(_t._("Earlier imports")));
            foreach (var job in jobs.Take(10))
            {
                var row = Row();
                var openJob = new Button { Content = _t._("Open") };
                openJob.Click += async (_, _) => await ShowProgressAsync(job.Id);
                row.Children.Add(openJob);
                row.Children.Add(Text($"{job.Created:g}  ·  {job.Name}  ·  {await _controller.GetCountsAsync(job.Id)}", wrap: true));
                panel.Children.Add(row);
            }
        }
        SetPage(panel);
    }

    // ---------------- Step 2: summary ----------------

    private async Task ShowSummaryAsync(BulkPlan plan)
    {
        _plan = plan;
        _jobId = null;
        var panel = Page(_t._("Before starting"));
        var summary = Text(_controller.Summarize(plan), wrap: true);
        panel.Children.Add(summary);

        // Download folder
        var folderRow = Row();
        var folderText = Text(_t._("Save to: {0}", _controller.SaveFolder), wrap: true);
        var change = new Button { Content = _t._("Change…") };
        change.Click += async (_, _) =>
        {
            var picker = new FolderPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.Downloads };
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                _controller.SaveFolder = folder.Path;
                folderText.Text = _t._("Save to: {0}", folder.Path);
                summary.Text = _controller.Summarize(plan);
            }
        };
        folderRow.Children.Add(change);
        folderRow.Children.Add(folderText);
        panel.Children.Add(folderRow);

        var selectable = plan.Entries.Where(x => x.Status == PlanStatus.New).ToList();
        var linkBoxes = new List<(PlanEntry Entry, CheckBox Box)>();

        // Sites
        panel.Children.Add(Heading(_t._("Sites")));
        var sites = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        var siteBoxes = new Dictionary<string, CheckBox>();
        foreach (var group in selectable.Where(x => !x.IsCollection).GroupBy(x => x.Site).OrderByDescending(g => g.Count()))
        {
            var box = new CheckBox { Content = $"{group.Key}  ({group.Count()})", IsChecked = true };
            var site = group.Key;
            box.Click += (_, _) =>
            {
                foreach (var (entry, linkBox) in linkBoxes.Where(x => x.Entry.Site == site && !x.Entry.IsCollection))
                {
                    linkBox.IsChecked = box.IsChecked == true;
                    entry.Selected = box.IsChecked == true;
                }
                summary.Text = _controller.Summarize(plan);
            };
            siteBoxes[site] = box;
            sites.Children.Add(box);
        }
        panel.Children.Add(sites);

        // Links
        panel.Children.Add(Heading(_t._("Links")));
        var links = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        foreach (var entry in selectable.OrderBy(x => x.IsCollection))
        {
            var label = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url.ToString() : $"{entry.Title}  —  {entry.Url}";
            var status = _controller.StatusLabel(entry);
            var box = new CheckBox { Content = string.IsNullOrEmpty(status) ? label : $"[{status}]  {label}", IsChecked = entry.Selected };
            ToolTipService.SetToolTip(box, entry.Url.ToString());
            box.Click += (_, _) =>
            {
                entry.Selected = box.IsChecked == true;
                summary.Text = _controller.Summarize(plan);
            };
            linkBoxes.Add((entry, box));
            links.Children.Add(box);
        }
        if (selectable.Count == 0)
        {
            links.Children.Add(Text(_t._("Nothing new to download in this list."), wrap: true));
        }
        panel.Children.Add(links);

        var buttons = Row();
        var start = Primary(_t._("Start"));
        start.Click += async (_, _) =>
        {
            var count = plan.Entries.Count(x => x.Selected);
            if (count == 0)
            {
                await ShowMessageAsync(_t._("Nothing ticked"), _t._("Tick at least one link."));
                return;
            }
            var collections = plan.Entries.Count(x => x.Selected && x.IsCollection);
            if (collections > 0 && !await ConfirmAsync(_t._("Expand playlists and channels?"), _t._("You ticked {0} playlist/channel/profile link(s). Each will be expanded into all its videos, which can be hundreds. Downloading more than 50 from one site at once can get you blocked. Continue?", collections)))
            {
                return;
            }
            start.IsEnabled = false;
            var id = await _controller.StartAsync(plan);
            await ShowProgressAsync(id);
        };
        var back = new Button { Content = _t._("Back") };
        back.Click += async (_, _) => await ShowInputAsync();
        buttons.Children.Add(start);
        buttons.Children.Add(back);
        panel.Children.Add(buttons);
        SetPage(panel);
    }

    // ---------------- Step 3: progress and report ----------------

    private async Task ShowProgressAsync(string jobId)
    {
        _jobId = jobId;
        var items = (await _controller.GetItemsAsync(jobId)).Where(x => x.State != BulkItemState.Expanded).ToList();
        var finished = items.All(x => x.IsFinal);
        var panel = Page(finished ? _t._("Bulk Import – report") : _t._("Bulk Import – working through the list"));
        panel.Children.Add(Text(finished ? _t._("Done. Nothing else will happen with this list unless you press Retry failed.") : _t._("You can close this window; UniFetcher keeps going, even after a restart. Downloads appear in the main window."), wrap: true));
        var counts = Text(await _controller.GetCountsAsync(jobId), wrap: true);
        counts.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(counts);

        var buttons = Row();
        var retry = new Button { Content = _t._("Retry failed") };
        retry.Click += async (_, _) =>
        {
            await _controller.RetryFailedAsync(jobId);
            await ShowProgressAsync(jobId);
        };
        var export = new Button { Content = _t._("Export list of failed links") };
        export.Click += async (_, _) =>
        {
            var text = await _controller.GetFailedLinksAsync(jobId);
            if (string.IsNullOrEmpty(text))
            {
                await ShowMessageAsync(_t._("No failed links"), _t._("Every link either downloaded or was skipped as a duplicate."));
                return;
            }
            var picker = new FileSavePicker(AppWindow.Id) { SuggestedFileName = "UniFetcher failed links", SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add(_t._("Text file"), [".txt"]);
            var file = await picker.PickSaveFileAsync();
            if (file is not null)
            {
                await File.WriteAllTextAsync(file.Path, text);
            }
        };
        var copy = new Button { Content = _t._("Copy report") };
        copy.Click += async (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(await _controller.GetReportAsync(jobId));
            Clipboard.SetContent(package);
            copy.Content = _t._("Copied");
        };
        var newImport = new Button { Content = _t._("New import") };
        newImport.Click += async (_, _) => await ShowInputAsync();
        buttons.Children.Add(retry);
        buttons.Children.Add(export);
        buttons.Children.Add(copy);
        buttons.Children.Add(newImport);
        panel.Children.Add(buttons);

        panel.Children.Add(Heading(_t._("Links")));
        var order = new[] { BulkItemState.NeedsAction, BulkItemState.InQueue, BulkItemState.FailedWillRetry, BulkItemState.Waiting, BulkItemState.Failed, BulkItemState.Unavailable, BulkItemState.NoVideoFound, BulkItemState.Stopped, BulkItemState.SkippedDuplicate, BulkItemState.Downloaded };
        foreach (var item in items.OrderBy(x => Array.IndexOf(order, x.State)))
        {
            var line = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 4, 0, 4) };
            var head = Text($"{_controller.StateLabel(item.State)}  ·  {(string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title)}", wrap: true);
            head.FontWeight = item.State == BulkItemState.NeedsAction ? FontWeights.SemiBold : FontWeights.Normal;
            line.Children.Add(head);
            var detail = Text(string.IsNullOrEmpty(item.Note) ? item.Url : $"{item.Url}  —  {item.Note}", wrap: true);
            detail.Opacity = 0.7;
            detail.FontSize = 12;
            detail.IsTextSelectionEnabled = true;
            line.Children.Add(detail);
            panel.Children.Add(line);
        }
        SetPage(panel);
    }

    // ---------------- Drag and drop ----------------

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = _t._("Import links");
        }
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.FirstOrDefault() is StorageFile file)
            {
                await LoadFileAsync(file.Path);
            }
        }
        else if (e.DataView.Contains(StandardDataFormats.Text))
        {
            await ShowSummaryAsync(await _controller.PlanFromTextAsync(await e.DataView.GetTextAsync()));
        }
    }

    // ---------------- Little helpers ----------------

    private void SetPage(StackPanel panel)
    {
        _root.Children.Clear();
        _root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    private static StackPanel Page(string title)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Padding = new Thickness(0, 0, 16, 16) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.SemiBold });
        return panel;
    }

    private static TextBlock Heading(string text) => new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) };

    private static TextBlock Text(string text, bool wrap = false) => new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };

    private static StackPanel Row() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

    private static Button Primary(string text) => new Button { Content = text, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog { Title = title, Content = message, CloseButtonText = _t._("OK"), XamlRoot = _root.XamlRoot };
        await dialog.ShowAsync();
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog { Title = title, Content = message, PrimaryButtonText = _t._("Continue"), CloseButtonText = _t._("Cancel"), DefaultButton = ContentDialogButton.Close, XamlRoot = _root.XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
