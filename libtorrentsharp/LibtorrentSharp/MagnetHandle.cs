using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp;

/// <summary>
/// Handle to a torrent added via a BEP-9 magnet URI or loaded from a resume blob. Returned from <see cref="LibtorrentSession.Add"/>.
/// </summary>
/// <remarks>
/// Preliminary shape. Metadata arrives asynchronously via alerts and is not yet surfaced through this type;
/// a subsequent commit will unify <see cref="MagnetHandle"/> with <see cref="TorrentHandle"/> once the alert
/// pump exposes the magnet's resolved <see cref="TorrentInfo"/>.
/// </remarks>
public sealed class MagnetHandle
{
    internal readonly IntPtr SessionHandle;
    internal readonly IntPtr Handle;

    internal MagnetHandle(IntPtr sessionHandle, IntPtr handle, string savePath)
    {
        SessionHandle = sessionHandle;
        Handle = handle;
        SavePath = savePath;
    }

    /// <summary>
    /// Absolute path the torrent's data will be written to once metadata is available.
    /// </summary>
    public string SavePath { get; }

    /// <summary>
    /// Whether the magnet URI was parsed and the torrent was added to the session.
    /// When false, the URI was malformed or libtorrent rejected the resulting handle.
    /// </summary>
    public bool IsValid => Handle != IntPtr.Zero;

    /// <summary>
    /// Discards cached piece state and re-hashes the torrent's files on disk.
    /// No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void ForceRecheck()
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.ForceRecheck(Handle);
    }

    /// <summary>
    /// Queue-aware pause. Sets paused + enables auto-management.
    /// No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void Pause()
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.PauseTorrent(Handle);
    }

    /// <summary>
    /// Queue-aware resume. Clears paused + enables auto-management.
    /// No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void Resume()
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.ResumeTorrent(Handle);
    }

    /// <summary>
    /// Enables or disables force-start mode. When enabled, clears auto-management
    /// and resumes the torrent so the queue cannot re-pause it. When disabled,
    /// re-enables auto-management so the queue resumes normal governance.
    /// No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void SetForceStart(bool forceStart)
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.ForceStartTorrent(Handle, forceStart);
    }

    /// <summary>
    /// Relocates the torrent's data to <paramref name="newPath"/>. No-op when
    /// <see cref="IsValid"/> is false.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="newPath"/> is null or empty.</exception>
    public void MoveStorage(string newPath, MoveStorageFlags flags = MoveStorageFlags.AlwaysReplaceFiles)
    {
        if (string.IsNullOrEmpty(newPath))
        {
            throw new ArgumentException("New path must not be null or empty.", nameof(newPath));
        }

        if (!IsValid)
        {
            return;
        }

        NativeMethods.MoveStorage(Handle, newPath, (int)flags);
    }

    /// <summary>
    /// Snapshot of the peers currently connected to this torrent. Returns an
    /// empty list when <see cref="IsValid"/> is false.
    /// </summary>
    public IReadOnlyList<PeerInfo> GetPeers()
    {
        return IsValid
            ? PeerInfoMarshaller.GetPeers(Handle)
            : Array.Empty<PeerInfo>();
    }

    /// <summary>
    /// Snapshot of the trackers attached to this torrent. Returns an empty list
    /// when <see cref="IsValid"/> is false.
    /// </summary>
    public IReadOnlyList<TrackerInfo> GetTrackers()
    {
        return IsValid
            ? TrackerInfoMarshaller.GetTrackers(Handle)
            : Array.Empty<TrackerInfo>();
    }

    /// <summary>
    /// Adds a tracker URL at the specified tier. No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void AddTracker(string url, int tier = 0)
    {
        if (!IsValid) return;
        NativeMethods.AddTracker(Handle, url, tier);
    }

    /// <summary>
    /// Removes a tracker by URL. No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void RemoveTracker(string url)
    {
        if (!IsValid) return;
        NativeMethods.RemoveTracker(Handle, url);
    }

    /// <summary>
    /// Replaces <paramref name="oldUrl"/> with <paramref name="newUrl"/> and updates the tier.
    /// No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void EditTracker(string oldUrl, string newUrl, int newTier = 0)
    {
        if (!IsValid) return;
        NativeMethods.EditTracker(Handle, oldUrl, newUrl, newTier);
    }

    public IReadOnlyList<WebSeedInfo> GetWebSeeds() =>
        IsValid ? WebSeedInfoMarshaller.GetWebSeeds(Handle) : Array.Empty<WebSeedInfo>();

    public void AddWebSeed(string url)
    {
        if (!IsValid) return;
        NativeMethods.AddWebSeed(Handle, url);
    }

    public void RemoveWebSeed(string url)
    {
        if (!IsValid) return;
        NativeMethods.RemoveWebSeed(Handle, url);
    }

    /// <summary>
    /// Magnets only carry metadata after resolution; before that there is no
    /// .torrent to export, so this always returns null.
    /// </summary>
    public byte[] ExportTorrentBytes() => null;

    /// <summary>
    /// Current runtime status of this torrent.
    /// </summary>
    /// <exception cref="InvalidOperationException">The handle is not valid.</exception>
    public TorrentStatus GetCurrentStatus()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("Cannot get status for an invalid handle.");
        }

        return TorrentStatusMarshaller.GetStatus(Handle);
    }

    /// <summary>
    /// Per-torrent upload cap in bytes/second. Zero is unlimited. Returns zero and
    /// ignores sets when <see cref="IsValid"/> is false.
    /// </summary>
    public int UploadRateLimit
    {
        get => IsValid ? NativeMethods.GetUploadLimit(Handle) : 0;
        set
        {
            if (IsValid)
            {
                NativeMethods.SetUploadLimit(Handle, value);
            }
        }
    }

    /// <summary>
    /// Per-torrent download cap in bytes/second. Zero is unlimited.
    /// </summary>
    public int DownloadRateLimit
    {
        get => IsValid ? NativeMethods.GetDownloadLimit(Handle) : 0;
        set
        {
            if (IsValid)
            {
                NativeMethods.SetDownloadLimit(Handle, value);
            }
        }
    }

    /// <summary>
    /// Enables or disables libtorrent's super-seeding mode. No-op when
    /// <see cref="IsValid"/> is false.
    /// </summary>
    public void SetSuperSeeding(bool enabled)
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.SetSuperSeeding(Handle, enabled);
    }

    /// <summary>
    /// The torrent's current <see cref="TorrentFlags"/> bitset. Returns
    /// <see cref="TorrentFlags.None"/> when <see cref="IsValid"/> is false.
    /// </summary>
    public TorrentFlags Flags => IsValid
        ? (TorrentFlags)NativeMethods.GetTorrentFlags(Handle)
        : TorrentFlags.None;

    /// <summary>
    /// Rewrites the bits selected by <paramref name="mask"/> with the matching
    /// bits from <paramref name="flags"/>; bits outside the mask are left
    /// untouched. No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void SetFlags(TorrentFlags flags, TorrentFlags mask)
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.SetTorrentFlags(Handle, (ulong)flags, (ulong)mask);
    }

    /// <summary>
    /// Sets every bit in <paramref name="flags"/>. Equivalent to
    /// <see cref="SetFlags(TorrentFlags, TorrentFlags)"/> with mask == flags.
    /// </summary>
    public void SetFlags(TorrentFlags flags) => SetFlags(flags, flags);

    /// <summary>
    /// Clears every bit set in <paramref name="flags"/>; leaves other bits
    /// untouched. No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void UnsetFlags(TorrentFlags flags)
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.UnsetTorrentFlags(Handle, (ulong)flags);
    }

    /// <summary>
    /// Enables or disables sequential download. No-op when <see cref="IsValid"/> is false.
    /// </summary>
    public void SetSequentialDownload(bool enabled)
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.SetSequentialDownload(Handle, enabled);
    }

    /// <summary>
    /// Sets the priority of the first and last piece of a file. No-op when the
    /// handle is invalid or metadata hasn't resolved yet.
    /// </summary>
    public void SetFileFirstLastPiecePriority(int fileIndex, FileDownloadPriority priority)
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.SetFileFirstLastPiecePriority(Handle, fileIndex, (byte)priority);
    }

    /// <summary>
    /// Reads a single piece's download priority. Returns
    /// <see cref="FileDownloadPriority.DoNotDownload"/> when the handle is
    /// invalid, metadata hasn't resolved yet, or the index is out of range.
    /// </summary>
    public FileDownloadPriority GetPiecePriority(int pieceIndex)
    {
        if (!IsValid)
        {
            return FileDownloadPriority.DoNotDownload;
        }

        return (FileDownloadPriority)NativeMethods.GetPiecePriority(Handle, pieceIndex);
    }

    /// <summary>
    /// Sets a single piece's download priority. No-op when the handle is
    /// invalid, metadata hasn't resolved yet, or the index is out of range.
    /// </summary>
    public void SetPiecePriority(int pieceIndex, FileDownloadPriority priority)
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.SetPiecePriority(Handle, pieceIndex, (byte)priority);
    }

    /// <summary>
    /// Snapshot of every piece's download priority. Returns an empty array
    /// when the handle is invalid or metadata hasn't resolved yet.
    /// </summary>
    public FileDownloadPriority[] GetPiecePriorities()
    {
        if (!IsValid)
        {
            return Array.Empty<FileDownloadPriority>();
        }

        NativeMethods.GetPiecePriorities(Handle, out var list);
        try
        {
            if (list.length <= 0 || list.priorities == IntPtr.Zero)
            {
                return Array.Empty<FileDownloadPriority>();
            }

            var raw = new byte[list.length];
            Marshal.Copy(list.priorities, raw, 0, list.length);

            var result = new FileDownloadPriority[list.length];
            for (var i = 0; i < raw.Length; i++)
            {
                result[i] = (FileDownloadPriority)raw[i];
            }
            return result;
        }
        finally
        {
            NativeMethods.FreePiecePriorities(ref list);
        }
    }

    /// <summary>
    /// Replaces every piece's priority in one call. No-op when the handle is
    /// invalid, metadata hasn't resolved yet, or <paramref name="priorities"/>
    /// is empty. Entries beyond the torrent's piece count are silently
    /// truncated.
    /// </summary>
    public void SetPiecePriorities(ReadOnlySpan<FileDownloadPriority> priorities)
    {
        if (!IsValid || priorities.IsEmpty)
        {
            return;
        }

        var bytes = new byte[priorities.Length];
        for (var i = 0; i < priorities.Length; i++)
        {
            bytes[i] = (byte)priorities[i];
        }

        NativeMethods.SetPiecePriorities(Handle, bytes, bytes.Length);
    }

    /// <summary>
    /// Total number of pieces in this torrent. Returns 0 when metadata has not yet
    /// resolved (pre-metadata magnet or resume-loaded handle before checking completes).
    /// </summary>
    public int NumPieces => IsValid ? NativeMethods.TorrentHandleNumPieces(Handle) : 0;

    /// <summary>
    /// Total size of all files in bytes. Returns 0 when metadata has not yet
    /// resolved (pre-metadata magnet or resume-loaded handle before checking completes).
    /// </summary>
    public long TotalSize => IsValid ? NativeMethods.TorrentHandleTotalSize(Handle) : 0;

    /// <summary>
    /// True when the piece is fully downloaded and verified on disk. Returns
    /// false when the handle is invalid, metadata hasn't resolved yet, or the
    /// index is out of range.
    /// </summary>
    public bool HavePiece(int pieceIndex)
    {
        if (!IsValid)
        {
            return false;
        }

        return NativeMethods.HavePiece(Handle, pieceIndex);
    }

    /// <summary>
    /// Returns the full piece completion bitfield in a single native call.
    /// </summary>
    public bool[] GetPieceBitfield(int numPieces)
    {
        if (!IsValid || numPieces <= 0) return [];
        var numBytes = (numPieces + 7) / 8;
        var bits = new byte[numBytes];
        NativeMethods.GetPieceBitfield(Handle, bits, numBytes);
        var result = new bool[numPieces];
        for (var i = 0; i < numPieces; i++)
            result[i] = (bits[i / 8] & (1 << (i % 8))) != 0;
        return result;
    }

    /// <summary>
    /// Explicitly queues a peer-connect attempt on this torrent. Returns false
    /// when <see cref="IsValid"/> is false.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is zero.</exception>
    public bool ConnectPeer(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (port is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be in 1..65535.");
        }

        if (!IsValid)
        {
            return false;
        }

        var v6 = AddressMarshal.ToV6Mapped(address);
        return NativeMethods.ConnectPeer(Handle, v6, (ushort)port);
    }

    /// <summary>
    /// Clears any sticky error state on the torrent. No-op when
    /// <see cref="IsValid"/> is false.
    /// </summary>
    public void ClearError()
    {
        if (!IsValid)
        {
            return;
        }

        NativeMethods.ClearError(Handle);
    }

    /// <summary>
    /// Renames a file inside the torrent. No-op when <see cref="IsValid"/> is
    /// false or metadata hasn't resolved yet.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="newName"/> is null, empty, or whitespace.</exception>
    public void RenameFile(int fileIndex, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("New name must not be null, empty, or whitespace.", nameof(newName));
        }

        if (!IsValid)
        {
            return;
        }

        NativeMethods.RenameFile(Handle, fileIndex, newName);
    }

    /// <summary>
    /// Returns file entries backed by this torrent handle, using metadata from
    /// <paramref name="info"/>. For resume-loaded torrents the metadata is
    /// embedded in the resume blob; pass the original <see cref="TorrentInfo"/>
    /// that was used when the torrent was first added.
    /// </summary>
    public IReadOnlyList<TorrentHandle.TorrentManagerFile> GetFiles(TorrentInfo info)
    {
        return info.Files
            .Select(x => new TorrentHandle.TorrentManagerFile(Handle, SavePath, x))
            .ToList();
    }

    /// <summary>
    /// Returns file entries directly from the native handle without requiring a
    /// separate <see cref="TorrentInfo"/> object. Works for resume-loaded handles
    /// (cold-start torrents). Returns an empty list when the handle is invalid or
    /// metadata hasn't resolved yet.
    /// </summary>
    public IReadOnlyList<TorrentHandle.TorrentManagerFile> GetFiles()
    {
        if (!IsValid)
        {
            return Array.Empty<TorrentHandle.TorrentManagerFile>();
        }

        NativeMethods.GetTorrentHandleFileList(Handle, out var list);
        try
        {
            if (list.length <= 0 || list.items == IntPtr.Zero)
            {
                return Array.Empty<TorrentHandle.TorrentManagerFile>();
            }

            var size = Marshal.SizeOf<NativeStructs.TorrentFile>();
            var files = new List<TorrentHandle.TorrentManagerFile>(list.length);

            for (var i = 0; i < list.length; i++)
            {
                var nf = Marshal.PtrToStructure<NativeStructs.TorrentFile>(list.items + (size * i));
                var fi = new TorrentFileInfo(nf.index, nf.offset, nf.file_name, nf.file_path, nf.file_size, (FileFlags)nf.flags);
                files.Add(new TorrentHandle.TorrentManagerFile(Handle, SavePath, fi));
            }

            return files;
        }
        finally
        {
            NativeMethods.FreeTorrentFileList(ref list);
        }
    }

    /// <summary>
    /// Reannounces the torrent to all trackers. No-op when <see cref="IsValid"/> is false.
    /// </summary>
    /// <param name="interval">Delay between this call and the announcement taking place.</param>
    /// <param name="force">Whether to ignore tracker-internal cooldowns between announcements.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is negative.</exception>
    public void ReannounceAllTrackers(TimeSpan interval, bool force = false)
    {
        if (interval.TotalSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be a non-negative value.");
        }

        if (!IsValid)
        {
            return;
        }

        NativeMethods.ReannounceTorrent(Handle, (int)interval.TotalSeconds, force);
    }
}
