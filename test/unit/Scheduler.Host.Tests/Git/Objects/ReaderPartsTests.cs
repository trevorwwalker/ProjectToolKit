// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Buffers.Binary;
using System.Text;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Tests.Git.Objects;

/// <summary>1061-B: the reader's parts on bytes made here, no git (MM-1, MM-2).</summary>
public sealed class ReaderPartsTests
{
    [Fact]
    public void MM_1_ObjectId_RoundTripsAndOrdersAsBytes()
    {
        var a = ObjectId.Parse("00ff00000000000000000000000000000000ab01");
        var b = ObjectId.Parse("0100000000000000000000000000000000000000");
        Assert.Equal("00ff00000000000000000000000000000000ab01", a.ToString());
        Assert.True(a.CompareTo(b) < 0);
        Assert.Equal(0, a.FirstByte);
        Assert.Equal(1, b.FirstByte);
        var raw = new byte[20];
        a.WriteTo(raw);
        Assert.Equal(a, ObjectId.FromBytes(raw));
        Assert.False(ObjectId.TryParse("abc", out _));
        Assert.False(ObjectId.TryParse(new string('g', 40), out _));
    }

    [Fact]
    public void MM_1_Delta_CopiesAndInserts()
    {
        var baseData = Encoding.ASCII.GetBytes("hello, world");
        // base 12, result 11: copy offset 7 length 5 ("world"), insert 6 bytes " again"
        byte[] delta = [12, 11, 0x80 | 0x01 | 0x10, 7, 5, 6, .. Encoding.ASCII.GetBytes(" again")];
        Assert.Equal("world again", Encoding.ASCII.GetString(Delta.Apply(baseData, delta)));
        Assert.Throws<GitObjectException>(() => Delta.Apply(Encoding.ASCII.GetBytes("short"), delta));
        Assert.Throws<GitObjectException>(() => Delta.Apply(baseData, [12, 1, 0]));
    }

    [Fact]
    public void MM_1_PackIndex_SmallAndLargeOffsets()
    {
        var low = ObjectId.Parse("0a00000000000000000000000000000000000001");
        var high = ObjectId.Parse("f000000000000000000000000000000000000002");
        var data = new byte[8 + 1024 + 2 * 20 + 2 * 4 + 2 * 4 + 8 + 40];
        data[0] = 0xff;
        "tOc"u8.CopyTo(data.AsSpan(1));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 2);
        for (var b = 0; b < 256; b++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8 + b * 4), (uint)((b >= 0x0a ? 1 : 0) + (b >= 0xf0 ? 1 : 0)));
        }

        var at = 8 + 1024;
        low.WriteTo(data.AsSpan(at));
        high.WriteTo(data.AsSpan(at + 20));
        at += 40 + 8; // ids, then crcs
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), 12);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at + 4), 0x8000_0000u);
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(at + 8), 0x1_0000_0010UL);

        var index = PackIndex.Parse(data);
        Assert.Equal(2, index.Count);
        Assert.Equal(12, index.Find(low));
        Assert.Equal(0x1_0000_0010L, index.Find(high));
        Assert.Null(index.Find(ObjectId.Parse("0a00000000000000000000000000000000000002")));
        Assert.Equal([low, high], index.Ids());
        data[7] = 1;
        Assert.Throws<GitObjectException>(() => PackIndex.Parse(data));
    }

    [Fact]
    public void MM_2_Config_SectionsSubsectionsCommentsAndBooleans()
    {
        var c = GitConfig.Parse("[core]\n\trepositoryformatversion = 1 ; a comment\n[extensions]\n\tobjectFormat = sha256\n[remote \"Origin\"]\n\tpromisor\n\turl = \"C:\\\\x # y\"\n");
        Assert.Equal("1", c.Get("core.repositoryformatversion"));
        Assert.Equal("sha256", c.Get("extensions.objectformat"));
        Assert.True(GitConfig.IsTrue(c.Get("remote.Origin.promisor")!));
        Assert.Equal("C:\\x # y", c.Get("remote.Origin.url"));
        Assert.Null(c.Get("core.bare"));
    }

    [Fact]
    public void MM_1_Commit_FieldsTimesAndSignatureLinesSkipped()
    {
        var text = "tree 4b825dc642cb6eb9a060e54bf8d69288fbee4904\nparent 0000000000000000000000000000000000000001\n"
            + "author A <a@x> 1700000000 +0130\ncommitter C <c@x> 1700000100 -0200\ngpgsig -----BEGIN-----\n tree 1\n -----END-----\n\nSubject line\n\nbody\n";
        var c = CommitParse.Parse(Encoding.UTF8.GetBytes(text));
        Assert.Equal("4b825dc642cb6eb9a060e54bf8d69288fbee4904", c.Tree.ToString());
        Assert.Single(c.Parents);
        Assert.Equal(1700000100, c.CommitTime.ToUnixTimeSeconds());
        Assert.Equal(TimeSpan.FromHours(-2), c.CommitTime.Offset);
        Assert.Equal(new TimeSpan(1, 30, 0), c.AuthorTime.Offset);
        Assert.Equal("Subject line", c.Subject);
        Assert.Equal("Subject line\n\nbody\n", c.Message);
    }
}
