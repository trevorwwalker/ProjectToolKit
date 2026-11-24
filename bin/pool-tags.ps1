<#
.SYNOPSIS
  Read the kernel pool by tag (NtQuerySystemInformation class 22, SystemPoolTagInformation) - card 1160-B.
.DESCRIPTION
  No poolmon needed, nothing installed. Three uses:
    -Snapshot <file>      write the whole tag table plus \Memory\Pool Paged/Nonpaged Bytes as JSON
    -Top <n> [-From file] print the n largest tags by paged and by nonpaged bytes, with the table sum against the counters
                          (reads a snapshot file with -From, else the live table)
    -Diff <before>,<after> print tags whose paged or nonpaged bytes changed by more than 0.5 MB, largest first
  Never elevates; if the call is refused it says so and exits 3.
#>
[CmdletBinding()]
param(
    [string]$Snapshot,
    [int]$Top = 0,
    [string]$From,
    [string[]]$Diff
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class PoolTagNative {
    [DllImport("ntdll.dll")]
    static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);
    public class Row { public string Tag; public uint PagedAllocs, PagedFrees; public long PagedBytes; public uint NonPagedAllocs, NonPagedFrees; public long NonPagedBytes; }
    // Returns rows; status is the NTSTATUS of the last call (0 = ok).
    public static List<Row> Read(out int status) {
        int size = 1 << 20, ret = 0;
        for (int attempt = 0; attempt < 20; attempt++) {
            IntPtr p = Marshal.AllocHGlobal(size);
            try {
                status = NtQuerySystemInformation(22, p, size, out ret);
                if (status == unchecked((int)0xC0000004)) { size = Math.Max(ret + (256 << 10), size * 2); continue; } // length mismatch
                var rows = new List<Row>();
                if (status != 0) return rows;
                int count = Marshal.ReadInt32(p);
                int off = IntPtr.Size == 8 ? 8 : 4, entry = IntPtr.Size == 8 ? 40 : 28;
                for (int i = 0; i < count; i++) {
                    IntPtr e = IntPtr.Add(p, off + i * entry);
                    var r = new Row();
                    byte[] t = new byte[4]; Marshal.Copy(e, t, 0, 4);
                    var sb = new StringBuilder(); foreach (byte b in t) sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                    r.Tag = sb.ToString();
                    if (IntPtr.Size == 8) {
                        r.PagedAllocs = (uint)Marshal.ReadInt32(e, 4); r.PagedFrees = (uint)Marshal.ReadInt32(e, 8);
                        r.PagedBytes = Marshal.ReadInt64(e, 16);
                        r.NonPagedAllocs = (uint)Marshal.ReadInt32(e, 24); r.NonPagedFrees = (uint)Marshal.ReadInt32(e, 28);
                        r.NonPagedBytes = Marshal.ReadInt64(e, 32);
                    } else {
                        r.PagedAllocs = (uint)Marshal.ReadInt32(e, 4); r.PagedFrees = (uint)Marshal.ReadInt32(e, 8);
                        r.PagedBytes = Marshal.ReadInt32(e, 12);
                        r.NonPagedAllocs = (uint)Marshal.ReadInt32(e, 16); r.NonPagedFrees = (uint)Marshal.ReadInt32(e, 20);
                        r.NonPagedBytes = Marshal.ReadInt32(e, 24);
                    }
                    rows.Add(r);
                }
                return rows;
            } finally { Marshal.FreeHGlobal(p); }
        }
        status = unchecked((int)0xC0000004);
        return new List<Row>();
    }
}
'@

function Get-Snapshot {
    $st = 0
    $rows = [PoolTagNative]::Read([ref]$st)
    if ($st -ne 0) {
        $hex = '0x{0:X8}' -f $st
        if ($st -eq [int]0xC0000022) { Write-Error "NtQuerySystemInformation(22) refused: access denied ($hex). It needs elevation; not elevating." }
        else { Write-Error "NtQuerySystemInformation(22) failed: $hex" }
        exit 3
    }
    $c = (Get-Counter '\Memory\Pool Paged Bytes', '\Memory\Pool Nonpaged Bytes').CounterSamples
    [pscustomobject]@{
        time            = (Get-Date).ToString('o')
        pagedBytes      = [long]($c | Where-Object Path -like '*pool paged bytes').CookedValue
        nonpagedBytes   = [long]($c | Where-Object Path -like '*pool nonpaged bytes').CookedValue
        tags            = @($rows | ForEach-Object {
            [pscustomobject]@{ tag = $_.Tag; pagedAllocs = $_.PagedAllocs; pagedFrees = $_.PagedFrees; pagedBytes = $_.PagedBytes
                               nonpagedAllocs = $_.NonPagedAllocs; nonpagedFrees = $_.NonPagedFrees; nonpagedBytes = $_.NonPagedBytes }
        })
    }
}

function Read-Snap([string]$f) { Get-Content -Raw -Encoding UTF8 -LiteralPath $f | ConvertFrom-Json }
function MB([double]$b) { '{0,9:N1}' -f ($b / 1MB) }

if ($Snapshot) {
    $s = Get-Snapshot
    $dir = Split-Path -Parent ([IO.Path]::GetFullPath($Snapshot))
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $s | ConvertTo-Json -Depth 4 -Compress | Set-Content -Encoding UTF8 -LiteralPath $Snapshot
    Write-Host ("snapshot {0}: {1} tags, paged counter {2:N1} MB, nonpaged counter {3:N1} MB" -f $Snapshot, $s.tags.Count, ($s.pagedBytes / 1MB), ($s.nonpagedBytes / 1MB))
}
elseif ($Diff) {
    $Diff = @($Diff | ForEach-Object { $_ -split ',' })
    if ($Diff.Count -ne 2) { throw '-Diff takes two files: -Diff before.json,after.json' }
    $b = Read-Snap $Diff[0]; $a = Read-Snap $Diff[1]
    $bm = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal); foreach ($t in $b.tags) { $bm[$t.tag] = $t }
    $out = foreach ($t in $a.tags) {
        $o = $null; [void]$bm.TryGetValue($t.tag, [ref]$o)
        $pb = if ($o) { $o.pagedBytes } else { 0 }; $nb = if ($o) { $o.nonpagedBytes } else { 0 }
        $pl = if ($o) { $o.pagedAllocs - $o.pagedFrees } else { 0 }; $nl = if ($o) { $o.nonpagedAllocs - $o.nonpagedFrees } else { 0 }
        [pscustomobject]@{ tag = $t.tag; dPaged = $t.pagedBytes - $pb; dPagedLive = ($t.pagedAllocs - $t.pagedFrees) - $pl
                           dNonpaged = $t.nonpagedBytes - $nb; dNonpagedLive = ($t.nonpagedAllocs - $t.nonpagedFrees) - $nl }
    }
    $lim = 0.5MB
    $sel = $out | Where-Object { [math]::Abs($_.dPaged) -gt $lim -or [math]::Abs($_.dNonpaged) -gt $lim } |
        Sort-Object { [math]::Max([math]::Abs($_.dPaged), [math]::Abs($_.dNonpaged)) } -Descending
    Write-Host ("diff {0} -> {1}: paged counter {2:+0.0;-0.0} MB, nonpaged counter {3:+0.0;-0.0} MB" -f (Split-Path -Leaf $Diff[0]), (Split-Path -Leaf $Diff[1]), (($a.pagedBytes - $b.pagedBytes) / 1MB), (($a.nonpagedBytes - $b.nonpagedBytes) / 1MB))
    Write-Host ("table sum change: paged {0:+0.0;-0.0} MB, nonpaged {1:+0.0;-0.0} MB" -f ((($a.tags | Measure-Object pagedBytes -Sum).Sum - ($b.tags | Measure-Object pagedBytes -Sum).Sum) / 1MB), ((($a.tags | Measure-Object nonpagedBytes -Sum).Sum - ($b.tags | Measure-Object nonpagedBytes -Sum).Sum) / 1MB))
    if (-not $sel) { Write-Host '(no tag changed by more than 0.5 MB)' }
    else { $sel | Format-Table tag, @{n='dPaged_bytes';e={$_.dPaged}}, dPagedLive, @{n='dNonpaged_bytes';e={$_.dNonpaged}}, dNonpagedLive -AutoSize | Out-String -Width 200 | Write-Host }
}
else {
    $n = if ($Top -gt 0) { $Top } else { 25 }
    $s = if ($From) { Read-Snap $From } else { Get-Snapshot }
    $sp = ($s.tags | Measure-Object pagedBytes -Sum).Sum; $sn = ($s.tags | Measure-Object nonpagedBytes -Sum).Sum
    Write-Host ("table sum: paged {0:N1} MB (counter {1:N1} MB), nonpaged {2:N1} MB (counter {3:N1} MB)" -f ($sp / 1MB), ($s.pagedBytes / 1MB), ($sn / 1MB), ($s.nonpagedBytes / 1MB))
    Write-Host "`nTop $n by paged bytes"
    $s.tags | Sort-Object pagedBytes -Descending | Select-Object -First $n |
        Format-Table tag, @{n='MB';e={[math]::Round($_.pagedBytes / 1MB, 1)}}, @{n='live';e={$_.pagedAllocs - $_.pagedFrees}} -AutoSize | Out-String -Width 200 | Write-Host
    Write-Host "Top $n by nonpaged bytes"
    $s.tags | Sort-Object nonpagedBytes -Descending | Select-Object -First $n |
        Format-Table tag, @{n='MB';e={[math]::Round($_.nonpagedBytes / 1MB, 1)}}, @{n='live';e={$_.nonpagedAllocs - $_.nonpagedFrees}} -AutoSize | Out-String -Width 200 | Write-Host
}
