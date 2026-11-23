using System.Net;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

public class PageLogFileTests
{
    // PAGE-9, WRK-9 (931-A, developer 2026-11-19): a card number is a positive integer of any
    // width, so /log?file= takes the id as the card file writes it - three digits, four digits or
    // leading zeros. Anything that is not <id>-<stamp>.log is still refused (PAGE-9).
    [Fact]
    public async Task Page_LogFile_CardIdOfAnyWidth()
    {
        using var web = new WebHarness();
        web.WriteLog("050-C-20261118-120000.log", "three digit worker output\n");
        web.WriteLog("1200-A-20261118-120000.log", "four digit worker output\n");
        web.WriteLog("007-B-20261118-120000.log", "leading zero worker output\n");

        foreach (var (name, body) in new[]
        {
            ("050-C-20261118-120000.log", "three digit worker output"),
            ("1200-A-20261118-120000.log", "four digit worker output"),
            ("007-B-20261118-120000.log", "leading zero worker output"),
        })
        {
            var ok = await web.Client.GetAsync($"/log?file={name}");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(body, (await ok.Content.ReadAsStringAsync()).TrimEnd());
        }

        foreach (var name in new[] { "1200-A-20261118-120000.txt", "1200-A/20261118-120000.log", "../accounting.csv" })
        {
            var bad = await web.Client.GetAsync($"/log?file={name}");
            Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
        }
    }
}
