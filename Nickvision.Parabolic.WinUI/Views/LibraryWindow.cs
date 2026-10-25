using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Nickvision.Desktop.Globalization;
using Nickvision.Parabolic.Shared.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Nickvision.Parabolic.WinUI.Views;

/// <summary>
/// UniFetcher downloads library (spec section 6): list or thumbnail grid, search, sort by date/size/duration/name/site,
/// source site and original link, Open file, Show in folder, Copy link, Download again, Remove (file kept unless you tick "Also delete file").
/// </summary>
public sealed class LibraryWindow : Window
{
    private readonly LibraryController _controller;
    private readonly ITranslationService _t;
    private readonly TextBox _search;
    private readonly ComboBox _sort;
    private readonly ToggleSwitch _grid;
    private readonly TextBlock _count;
    private readonly ScrollViewer _results;
    private List<LibraryEntry> _all;

    public LibraryWindow(LibraryController controller, ITranslationService translationService)
    {
        _controller = controller;
        _t = translationService;
        _all = new List<LibraryEntry>();
        Title = _t._("UniFetcher – Downloads");
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 760));
        AppWindow.SetIcon("./Assets/unifetcher.ico");

        var root = new Grid { Padding = new Thickness(24), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TextBlock { Text = _t._("Downloads"), FontSize = 26, FontWeight = FontWeights.SemiBold });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _search = new TextBox { PlaceholderText = _t._("Search titles, sites and links"), Width = 320 };
        _search.TextChanged += (_, _) => Render();
        _sort = new ComboBox { Width = 170 };
        foreach (var sort in Enum.GetValues<LibrarySort>())
        {
            _sort.Items.Add(new ComboBoxItem { Content = _controller.SortLabel(sort), Tag = sort });
        }
        _sort.SelectedIndex = 0;
        _sort.SelectionChanged += (_, _) => Render();
        _grid = new ToggleSwitch { OnContent = _t._("Thumbnails"), OffContent = _t._("List"), IsOn = false };
        _grid.Toggled += (_, _) => Render();
        _count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        toolbar.Children.Add(_search);
        toolbar.Children.Add(_sort);
        toolbar.Children.Add(_grid);
        toolbar.Children.Add(_count);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        _results = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(_results, 2);
        root.Children.Add(_results);
        Content = root;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _count.Text = _t._("Loading…");
        _all = await _controller.GetAsync();
        Render();
    }

    private void Render()
    {
        var sort = (_sort.SelectedItem as ComboBoxItem)?.Tag is LibrarySort s ? s : LibrarySort.Newest;
        var entries = LibraryController.Filter(_all, _search.Text, sort).ToList();
        _count.Text = _t._("{0} of {1}", entries.Count, _all.Count);
        if (entries.Count == 0)
        {
            _results.Content = new TextBlock { Text = _all.Count == 0 ? _t._("Nothing downloaded yet (or history is off / incognito mode was on).") : _t._("Nothing matches your search."), Opacity = 0.7, Margin = new Thickness(0, 24, 0, 0) };
            return;
        }
        if (_grid.IsOn)
        {
            var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 236, ItemHeight = 250 };
            foreach (var entry in entries)
            {
                wrap.Children.Add(Card(entry));
            }
            _results.Content = wrap;
        }
        else
        {
            var list = new StackPanel { Spacing = 8 };
            foreach (var entry in entries)
            {
                list.Children.Add(Row(entry));
            }
            _results.Content = list;
        }
    }

    private FrameworkElement Row(LibraryEntry entry)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12), CornerRadius = new CornerRadius(6), Background = Res("CardBackgroundFillColorDefaultBrush") };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = entry.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = _controller.Describe(entry), FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = entry.Url.ToString(), FontSize = 12, Opacity = 0.7, IsTextSelectionEnabled = true, TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(text);
        var actions = Actions(entry);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private FrameworkElement Card(LibraryEntry entry)
    {
        var card = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(8), CornerRadius = new CornerRadius(6), Background = Res("CardBackgroundFillColorDefaultBrush") };
        var image = new Image { Height = 120, Stretch = Stretch.UniformToFill };
        card.Children.Add(new Border { Height = 120, CornerRadius = new CornerRadius(4), Child = image, Background = Res("SubtleFillColorSecondaryBrush") });
        if (entry.Exists)
        {
            _ = LoadThumbnailAsync(entry.Path, image);
        }
        card.Children.Add(new TextBlock { Text = entry.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, TextWrapping = TextWrapping.Wrap });
        card.Children.Add(new TextBlock { Text = _controller.Describe(entry), FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
        card.Children.Add(Actions(entry, compact: true));
        return card;
    }

    private static async Task LoadThumbnailAsync(string path, Image image)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.VideosView, 220);
            if (thumbnail is not null)
            {
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(thumbnail);
                image.Source = bitmap;
            }
        }
        catch
        {
            // no thumbnail available
        }
    }

    private StackPanel Actions(LibraryEntry entry, bool compact = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(IconButton("", _t._("Open file"), entry.Exists, () =>
        {
            Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
            return Task.CompletedTask;
        }));
        panel.Children.Add(IconButton("", _t._("Show in folder"), entry.Exists, () =>
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.Path}\"") { UseShellExecute = true });
            return Task.CompletedTask;
        }));
        panel.Children.Add(IconButton("", _t._("Copy link"), true, () =>
        {
            var package = new DataPackage();
            package.SetText(entry.Url.ToString());
            Clipboard.SetContent(package);
            return Task.CompletedTask;
        }));
        panel.Children.Add(IconButton("", _t._("Download again"), true, () =>
        {
            _controller.DownloadAgain(entry.Url);
            return Task.CompletedTask;
        }));
        panel.Children.Add(IconButton("", _t._("Remove from list"), true, async () => await RemoveAsync(entry)));
        return panel;
    }

    private static Button IconButton(string glyph, string tooltip, bool enabled, Func<Task> action)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 16 }, IsEnabled = enabled, Padding = new Thickness(8, 6, 8, 6) };
        ToolTipService.SetToolTip(button, tooltip);
        AutomationHelpers.SetName(button, tooltip);
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch
            {
                // e.g. the file was moved meanwhile
            }
        };
        return button;
    }

    private async Task RemoveAsync(LibraryEntry entry)
    {
        var alsoDelete = new CheckBox { Content = _t._("Also delete file"), IsChecked = false, IsEnabled = entry.Exists };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = _t._("Remove \"{0}\" from the downloads list? The video file stays on your drive unless you tick the box.", entry.Title), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(alsoDelete);
        var dialog = new ContentDialog
        {
            Title = _t._("Remove from list"),
            Content = content,
            PrimaryButtonText = _t._("Remove"),
            CloseButtonText = _t._("Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }
        await _controller.RemoveAsync(entry, alsoDelete.IsChecked == true);
        _all.Remove(entry);
        Render();
    }

    private static Brush? Res(string key) => Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;

    private static class AutomationHelpers
    {
        public static void SetName(DependencyObject element, string name) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
    }
}
