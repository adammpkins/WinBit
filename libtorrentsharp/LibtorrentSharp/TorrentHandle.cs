// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp;

public class TorrentHandle
{
    private readonly string _savePath;

    // Constructed with IntPtr.Zero; set by AttachTorrent once the add_torrent_alert fires.
    // Initialising to zero prevents a race where the alert fires synchronously before
    // the TorrentHandle object is fully constructed.
    internal IntPtr TorrentSessionHandle { get; private set; }

    private bool _detached;
    private IReadOnlyList<TorrentManagerFile> _files;

    internal TorrentHandle(IntPtr torrentSessionHandle, string savePath, TorrentInfo info)
    {
        Info = info;
        TorrentSessionHandle = torrentSessionHandle;

        _savePath = savePath;
    }

    /// <summary>
    /// Assigns the native handle after <c>AttachTorrent</c> returns. Called
    /// exactly once by <see cref="LibtorrentSession.AttachTorrentInternal"/>
    /// per the race fix.
    /// </summary>
    internal void SetNativeHandle(IntPtr handle)
    {
        TorrentSessionHandle = handle;
    }

    /// <summary>
    /// Information about the .torrent file
    /// </summary>
    public TorrentInfo Info { get; }

    /// <summary>
    /// Information about the files contained within the torrent, with additional properties including file priorities and target save paths.
    /// </summary>
    public IReadOnlyList<TorrentManagerFile> Files => _files ??= Info.Files.Select(x => new TorrentManagerFile(TorrentSessionHandle, _savePath, x)).ToList();

    /// <summary>
    /// Gets the current status of the torrent.
    /// </summary>
    /// <remarks>
    /// Every call crosses the P/Invoke boundary and allocates a fresh snapshot on
    /// the native side. Cache the result where the per-frame overhead would matter.
    /// </remarks>
    public TorrentStatus GetCurrentStatus()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        return TorrentStatusMarshaller.GetStatus(TorrentSessionHandle);
    }

    /// <summary>
    /// Starts or resumes the torrent.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.StartTorrent(TorrentSessionHandle);
    }

    /// <summary>
    /// Stops the torrent.
    /// </summary>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.StopTorrent(TorrentSessionHandle);
    }

    /// <summary>
    /// Discards cached piece state and re-hashes the torrent's files on disk.
    /// Progress surfaces through the usual status alerts (the torrent transitions
    /// through <c>checking_files</c> before returning to its prior state).
    /// </summary>
    public void ForceRecheck()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.ForceRecheck(TorrentSessionHandle);
    }

    /// <summary>
    /// Sends a scrape request to the torrent's trackers. Completes asynchronously;
    /// success surfaces as <see cref="Alerts.ScrapeReplyAlert"/>, failure as
    /// <see cref="Alerts.ScrapeFailedAlert"/>. Distinct from announces, which run
    /// on libtorrent's own schedule — scrape requests are on-demand peer-count queries.
    /// </summary>
    public void ScrapeTracker()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.ScrapeTracker(TorrentSessionHandle);
    }

    /// <summary>
    /// Queue-aware pause. Sets the paused flag and enables auto-management so
    /// libtorrent can resume the torrent when queue slots free up. Distinct from
    /// <see cref="Stop"/>, which is a manual force-pause that bypasses the queue.
    /// </summary>
    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.PauseTorrent(TorrentSessionHandle);
    }

    /// <summary>
    /// Queue-aware resume. Clears the paused flag and enables auto-management so
    /// libtorrent's queue governs running/pausing. Distinct from <see cref="Start"/>,
    /// which force-resumes immediately irrespective of queue limits.
    /// </summary>
    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.ResumeTorrent(TorrentSessionHandle);
    }

    /// <summary>
    /// Enables or disables force-start mode. When enabled, clears auto-management
    /// and resumes the torrent so libtorrent's queue cannot re-pause it. When
    /// disabled, re-enables auto-management so the queue resumes normal governance.
    /// </summary>
    public void SetForceStart(bool forceStart)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.ForceStartTorrent(TorrentSessionHandle, forceStart);
    }

    /// <summary>
    /// Relocates the torrent's data to <paramref name="newPath"/>. The move happens
    /// asynchronously on libtorrent's disk thread; success/failure surfaces via the
    /// storage_moved[_failed]_alert (managed alert exposure deferred).
    /// </summary>
    /// <param name="newPath">Destination directory. Created by libtorrent if it doesn't exist.</param>
    /// <param name="flags">Collision strategy for files already present at the destination.</param>
    /// <exception cref="ArgumentException"><paramref name="newPath"/> is null or empty.</exception>
    public void MoveStorage(string newPath, MoveStorageFlags flags = MoveStorageFlags.AlwaysReplaceFiles)
    {
        ObjectDisposedException.ThrowIf(_detached, this);

        if (string.IsNullOrEmpty(newPath))
        {
            throw new ArgumentException("New path must not be null or empty.", nameof(newPath));
        }

        NativeMethods.MoveStorage(TorrentSessionHandle, newPath, (int)flags);
    }

    /// <summary>
    /// Snapshot of the peers currently connected to this torrent.
    /// </summary>
    public IReadOnlyList<PeerInfo> GetPeers()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        return PeerInfoMarshaller.GetPeers(TorrentSessionHandle);
    }

    /// <summary>
    /// Snapshot of the trackers attached to this torrent (aggregate, one row per URL).
    /// </summary>
    public IReadOnlyList<TrackerInfo> GetTrackers()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        return TrackerInfoMarshaller.GetTrackers(TorrentSessionHandle);
    }

    /// <summary>
    /// Adds a tracker URL at the specified tier. No-op when the handle is detached.
    /// </summary>
    public void AddTracker(string url, int tier = 0)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.AddTracker(TorrentSessionHandle, url, tier);
    }

    /// <summary>
    /// Removes a tracker by URL. No-op when the handle is detached.
    /// </summary>
    public void RemoveTracker(string url)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.RemoveTracker(TorrentSessionHandle, url);
    }

    /// <summary>
    /// Replaces <paramref name="oldUrl"/> with <paramref name="newUrl"/> and updates the tier.
    /// No-op when the handle is detached.
    /// </summary>
    public void EditTracker(string oldUrl, string newUrl, int newTier = 0)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.EditTracker(TorrentSessionHandle, oldUrl, newUrl, newTier);
    }

    /// <summary>
    /// Snapshot of the web seeds attached to this torrent.
    /// </summary>
    public IReadOnlyList<WebSeedInfo> GetWebSeeds()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        return WebSeedInfoMarshaller.GetWebSeeds(TorrentSessionHandle);
    }

    /// <summary>
    /// Adds a web seed URL. No-op when the handle is detached.
    /// </summary>
    public void AddWebSeed(string url)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.AddWebSeed(TorrentSessionHandle, url);
    }

    /// <summary>
    /// Removes a web seed by URL. No-op when the handle is detached.
    /// </summary>
    public void RemoveWebSeed(string url)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.RemoveWebSeed(TorrentSessionHandle, url);
    }

    /// <summary>
    /// Exports the torrent's current metadata as a bencoded .torrent byte buffer.
    /// Returns null when metadata is not yet available (pre-metadata magnet case).
    /// </summary>
    public byte[] ExportTorrentBytes()
    {
        if (!NativeMethods.ExportTorrentToBytes(TorrentSessionHandle, out var dataPtr, out var size))
            return null;
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(dataPtr, bytes, 0, size);
            return bytes;
        }
        finally
        {
            NativeMethods.FreeBytes(dataPtr);
        }
    }

    /// <summary>
    /// Per-torrent upload cap in bytes/second. Any value &lt;= 0 means unlimited; the
    /// getter may return either 0 or -1 for the unlimited state depending on libtorrent's
    /// internal representation — callers should treat both as "no limit".
    /// </summary>
    public int UploadRateLimit
    {
        get
        {
            ObjectDisposedException.ThrowIf(_detached, this);
            return NativeMethods.GetUploadLimit(TorrentSessionHandle);
        }
        set
        {
            ObjectDisposedException.ThrowIf(_detached, this);
            NativeMethods.SetUploadLimit(TorrentSessionHandle, value);
        }
    }

    /// <summary>
    /// Per-torrent download cap in bytes/second. Zero is unlimited.
    /// </summary>
    public int DownloadRateLimit
    {
        get
        {
            ObjectDisposedException.ThrowIf(_detached, this);
            return NativeMethods.GetDownloadLimit(TorrentSessionHandle);
        }
        set
        {
            ObjectDisposedException.ThrowIf(_detached, this);
            NativeMethods.SetDownloadLimit(TorrentSessionHandle, value);
        }
    }

    /// <summary>
    /// Enables or disables libtorrent's super-seeding mode. Takes effect once the
    /// torrent enters the seeding state. Read the flag back via
    /// <see cref="TorrentStatus.Flags"/>.
    /// </summary>
    public void SetSuperSeeding(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.SetSuperSeeding(TorrentSessionHandle, enabled);
    }

    /// <summary>
    /// Reads the torrent's current <c>torrent_flags_t</c> bitset. Zero on an invalid handle.
    /// </summary>
    public TorrentFlags Flags
    {
        get
        {
            ObjectDisposedException.ThrowIf(_detached, this);
            return (TorrentFlags)NativeMethods.GetTorrentFlags(TorrentSessionHandle);
        }
    }

    /// <summary>
    /// Rewrites the flag bits covered by <paramref name="mask"/> to the value in
    /// <paramref name="flags"/>. Bits not in <paramref name="mask"/> are left untouched.
    /// </summary>
    public void SetFlags(TorrentFlags flags, TorrentFlags mask)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.SetTorrentFlags(TorrentSessionHandle, (ulong)flags, (ulong)mask);
    }

    /// <summary>Convenience: sets every bit in <paramref name="flags"/> and leaves the rest unchanged.</summary>
    public void SetFlags(TorrentFlags flags) => SetFlags(flags, flags);

    /// <summary>Clears every bit set in <paramref name="flags"/>.</summary>
    public void UnsetFlags(TorrentFlags flags)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.UnsetTorrentFlags(TorrentSessionHandle, (ulong)flags);
    }

    /// <summary>
    /// Enables or disables sequential download — pieces are requested in order
    /// instead of rarest-first. Useful for streaming. Read-back via
    /// <see cref="TorrentStatus.Flags"/>.
    /// </summary>
    public void SetSequentialDownload(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.SetSequentialDownload(TorrentSessionHandle, enabled);
    }

    /// <summary>
    /// Sets the priority of the first and last piece of a file. Makes media
    /// headers/footers available earlier — the streaming-UX complement to
    /// <see cref="SetSequentialDownload"/>.
    /// </summary>
    /// <param name="fileIndex">Index into <see cref="Files"/>.</param>
    /// <param name="priority">Priority to apply to both boundary pieces.</param>
    public void SetFileFirstLastPiecePriority(int fileIndex, FileDownloadPriority priority)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.SetFileFirstLastPiecePriority(TorrentSessionHandle, fileIndex, (byte)priority);
    }

    /// <summary>
    /// Snapshot of bytes downloaded per file. Index <c>i</c> is the number of bytes
    /// completed for file <c>i</c> (matches <see cref="TorrentManagerFile.Info"/>'s
    /// <c>Index</c> field). Returns an empty array when the torrent has no files.
    /// </summary>
    public long[] GetFileProgress()
    {
        var count = Files.Count;
        if (count == 0) return Array.Empty<long>();
        var result = new long[count];
        NativeMethods.GetFileProgress(TorrentSessionHandle, result, count);
        return result;
    }

    /// <summary>
    /// Reads a single piece's download priority. Returns
    /// <see cref="FileDownloadPriority.DoNotDownload"/> when metadata hasn't
    /// resolved yet or the index is out of range.
    /// </summary>
    /// <param name="pieceIndex">Index into the torrent's piece array (0..NumPieces-1).</param>
    public FileDownloadPriority GetPiecePriority(int pieceIndex)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        return (FileDownloadPriority)NativeMethods.GetPiecePriority(TorrentSessionHandle, pieceIndex);
    }

    /// <summary>
    /// Sets a single piece's download priority. No-op when metadata hasn't
    /// resolved yet or the index is out of range.
    /// </summary>
    public void SetPiecePriority(int pieceIndex, FileDownloadPriority priority)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.SetPiecePriority(TorrentSessionHandle, pieceIndex, (byte)priority);
    }

    /// <summary>
    /// Snapshot of every piece's download priority. Returns an empty array when
    /// metadata hasn't resolved yet. <c>priorities[i]</c> is the priority for
    /// piece <c>i</c>.
    /// </summary>
    public FileDownloadPriority[] GetPiecePriorities()
    {
        ObjectDisposedException.ThrowIf(_detached, this);

        NativeMethods.GetPiecePriorities(TorrentSessionHandle, out var list);
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
    /// Replaces every piece's priority in one call. No-op when
    /// <paramref name="priorities"/> is empty or metadata hasn't resolved yet.
    /// Entries beyond the torrent's piece count are silently truncated.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="priorities"/> is null.</exception>
    public void SetPiecePriorities(ReadOnlySpan<FileDownloadPriority> priorities)
    {
        ObjectDisposedException.ThrowIf(_detached, this);

        if (priorities.IsEmpty)
        {
            return;
        }

        var bytes = new byte[priorities.Length];
        for (var i = 0; i < priorities.Length; i++)
        {
            bytes[i] = (byte)priorities[i];
        }

        NativeMethods.SetPiecePriorities(TorrentSessionHandle, bytes, bytes.Length);
    }

    /// <summary>
    /// True when the piece is fully downloaded and verified on disk. Returns
    /// false when metadata hasn't resolved yet or the index is out of range.
    /// </summary>
    public bool HavePiece(int pieceIndex)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        return NativeMethods.HavePiece(TorrentSessionHandle, pieceIndex);
    }

    /// <summary>
    /// Returns the full piece completion bitfield in a single native call.
    /// </summary>
    public bool[] GetPieceBitfield(int numPieces)
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        if (numPieces <= 0) return [];
        var numBytes = (numPieces + 7) / 8;
        var bits = new byte[numBytes];
        NativeMethods.GetPieceBitfield(TorrentSessionHandle, bits, numBytes);
        var result = new bool[numPieces];
        for (var i = 0; i < numPieces; i++)
            result[i] = (bits[i / 8] & (1 << (i % 8))) != 0;
        return result;
    }

    /// <summary>
    /// Explicitly queues a peer-connect attempt on this torrent. Connect
    /// completion is surfaced via peer-alert categories. Returns true when
    /// the attempt was queued on the native side.
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

        ObjectDisposedException.ThrowIf(_detached, this);
        var v6 = AddressMarshal.ToV6Mapped(address);
        return NativeMethods.ConnectPeer(TorrentSessionHandle, v6, (ushort)port);
    }

    /// <summary>
    /// Clears any sticky error state on the torrent — e.g. after an out-of-disk
    /// hash-check failure. Lets the torrent retry once the underlying problem
    /// has been resolved.
    /// </summary>
    public void ClearError()
    {
        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.ClearError(TorrentSessionHandle);
    }

    /// <summary>
    /// Renames file at <paramref name="fileIndex"/> to <paramref name="newName"/>
    /// (which may be a relative path inside the torrent). Rename is asynchronous:
    /// result fires via <c>file_renamed_alert</c> / <c>file_rename_failed_alert</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="newName"/> is null, empty, or whitespace.</exception>
    public void RenameFile(int fileIndex, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("New name must not be null, empty, or whitespace.", nameof(newName));
        }

        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.RenameFile(TorrentSessionHandle, fileIndex, newName);
    }

    /// <summary>
    /// Reannounces the torrent to all trackers.
    /// </summary>
    /// <param name="interval">The delay between making this call and the announcement taking place</param>
    /// <param name="force">Whether to ignore any internal cooldowns between announcements</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="interval"/> was not valid</exception>
    public void ReannounceAllTrackers(TimeSpan interval, bool force = false)
    {
        if (interval.Seconds <= -1)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be a positive value.");
        }

        ObjectDisposedException.ThrowIf(_detached, this);
        NativeMethods.ReannounceTorrent(TorrentSessionHandle, (int)interval.TotalSeconds, force);
    }

    // internal method to trigger a detached status, essentially making the object functionally unusable.
    internal void MarkAsDetached()
    {
        _detached = true;
    }

    public class TorrentManagerFile
    {
        private readonly IntPtr _torrentSessionHandle;

        internal TorrentManagerFile(IntPtr torrentSessionHandle, string savePath, TorrentFileInfo info)
        {
            _torrentSessionHandle = torrentSessionHandle;

            Info = info;
            Path = System.IO.Path.IsPathRooted(Info.Path) ? Info.Path : System.IO.Path.Combine(savePath, Info.Path);
        }

        /// <summary>
        /// File information, as provided by the .torrent file.
        /// </summary>
        public TorrentFileInfo Info { get; }

        /// <summary>
        /// The full path to the file on disk.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// The download priority of the file.
        /// </summary>
        public FileDownloadPriority Priority
        {
            get => NativeMethods.GetFilePriority(_torrentSessionHandle, Info.Index);
            set => NativeMethods.SetFilePriority(_torrentSessionHandle, Info.Index, value);
        }
    }
}