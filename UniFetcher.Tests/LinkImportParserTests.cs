using Nickvision.Parabolic.Shared.Helpers;
using System.Linq;
using Xunit;

namespace UniFetcher.Tests;

public class LinkImportParserTests
{
    [Fact]
    public void NumberedListsFromSamsungNotesWork()
    {
        var parsed = LinkImportParser.Parse("1. https://www.youtube.com/watch?v=abc\n2. https://vimeo.com/123\n3) https:\\\\example.com\\videos\\5\n10.https://site.org/x?y=1");
        var urls = parsed.Links.Select(x => x.Url.ToString()).ToList();
        Assert.Contains("https://www.youtube.com/watch?v=abc", urls);
        Assert.Contains("https://vimeo.com/123", urls);
        Assert.Contains("https://example.com/videos/5", urls);
        Assert.Contains("https://site.org/x?y=1", urls);
        Assert.Equal(4, urls.Count);
    }
}
