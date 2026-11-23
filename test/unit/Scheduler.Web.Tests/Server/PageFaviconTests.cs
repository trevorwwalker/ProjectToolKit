namespace Scheduler.Web.Tests.Server;

public class PageFaviconTests
{
    [Fact]
    public async Task Favicon_Is_The_Icon_And_The_Page_Links_It()
    {
        using var web = new WebHarness();

        var icon = await web.Client.GetAsync("/favicon.ico");
        var bytes = await icon.Content.ReadAsByteArrayAsync();
        var page = new Scheduler.Web.Views.PageRenderer().Render(Views.ViewsFixture.Plain(), remote: false, controlsAllowed: true);

        Assert.Equal(System.Net.HttpStatusCode.OK, icon.StatusCode);
        Assert.Equal("image/x-icon", icon.Content.Headers.ContentType?.MediaType);
        Assert.True(bytes.Length > 100);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, bytes[..4]);
        Assert.Contains("<link rel='icon' href='/favicon.ico'>", page);
    }
}
