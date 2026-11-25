// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Reflection;

namespace Scheduler.Web.Server;

/// <summary>The scheduler's icon as the page's favicon (973-B), embedded in this assembly.</summary>
public static class PageFavicon
{
    /// <summary>The link element for every page head.</summary>
    public const string Link = "<link rel='icon' href='/favicon.ico'>";

    /// <summary>The /favicon.ico answer.</summary>
    public static PageResponse Response()
    {
        using var stream = typeof(PageFavicon).Assembly.GetManifestResourceStream("scheduler.ico");
        if (stream is null)
        {
            return PageResponse.NotFound("no icon");
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return new PageResponse { Status = 200, ContentType = "image/x-icon", Bytes = copy.ToArray() };
    }
}
