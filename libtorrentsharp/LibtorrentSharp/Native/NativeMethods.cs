// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

#nullable enable
using System;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp.Native;

internal static partial class NativeMethods
{
    private const string LibraryName = "lts";

    /// <summary>
    /// Delegate representing the callback for session events.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SessionEventCallback(IntPtr alertPtr);

    /// <summary>
    /// Per-piece progress callback fired by <see cref="CreateTorrent"/>. The native
    /// side fires once with <paramref name="currentPiece"/>=0 before hashing starts
    /// and once per piece during hashing; <paramref name="pieceSize"/> and
    /// <paramref name="totalSize"/> are constant for the run.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CreateTorrentProgressCallback(
        long currentPiece,
        long totalPieces,
        long pieceSize,
        long totalSize,
        IntPtr ctx);

    /// <summary>
    /// Creates a session, optionally using a provided settings pack.
    /// </summary>
    /// <param name="settingsPack">A settings pack handle, set to <c>null</c> to initialise without customisation</param>
    /// <returns>A handle to the session</returns>
    [LibraryImport(LibraryName, EntryPoint = "create_session")]
    public static unsafe partial IntPtr CreateSession(void* settingsPack);

    /// <summary>
    /// Releases the unmanaged resources associated with a session.
    /// </summary>
    /// <param name="sessionHandle">The session to invalidate</param>
    [LibraryImport(LibraryName, EntryPoint = "destroy_session")]
    public static partial void FreeSession(IntPtr sessionHandle);

    /// <summary>
    /// Sets the event callback for a session.
    /// </summary>
    /// <param name="sessionHandle">The handle for the session to add the callback to</param>
    /// <param name="callback">The callback to run when an event is posted</param>
    /// <param name="includeUnmappedEvents">Whether to run the <see cref="callback"/> for events that aren't mapped, that only produce <see cref="NativeEvents.AlertBase"/> values with no additional data</param>
    [LibraryImport(LibraryName, EntryPoint = "set_event_callback")]
    public static partial void SetEventCallback(IntPtr sessionHandle, [MarshalAs(UnmanagedType.FunctionPtr)] SessionEventCallback callback, [MarshalAs(UnmanagedType.Bool)] bool includeUnmappedEvents);

    /// <summary>
    /// Clears the currently set event callback.
    /// </summary>
    /// <param name="sessionHandle">The session handle to remove the registered callback from</param>
    [LibraryImport(LibraryName, EntryPoint = "clear_event_callback")]
    public static partial void ClearEventCallback(IntPtr sessionHandle);

    /// <summary>
    /// Applies a settings pack to a session.
    /// </summary>
    /// <param name="sessionHandle">The session handle to apply the pack to</param>
    /// <param name="settingsPack">The pack handle to apply</param>
    [LibraryImport(LibraryName, EntryPoint = "apply_settings")]
    public static partial void ApplySettingsPack(IntPtr sessionHandle, IntPtr settingsPack);

    /// <summary>
    /// Create a torrent from a file on the local disk
    /// </summary>
    /// <param name="path">The path to the file to parse</param>
    /// <returns>A handle to the torrent or <see cref="IntPtr.Zero"/> if there an error occurred</returns>
    [LibraryImport(LibraryName, EntryPoint = "create_torrent_file", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr CreateTorrentFromFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    /// <summary>
    /// Create a torrent file from a byte array
    /// </summary>
    /// <param name="content">The memory region containing the torrent file</param>
    /// <param name="length">The size of the region</param>
    /// <returns>A handle to the torrent or <see cref="IntPtr.Zero"/> if there an error occurred</returns>
    [LibraryImport(LibraryName, EntryPoint = "create_torrent_bytes")]
    public static partial IntPtr CreateTorrentFromBytes(byte[] content, long length);

    /// <summary>
    /// Create a torrent file from a byte array
    /// </summary>
    /// <param name="content">A byte pointer that locates the first byte of the torrent file</param>
    /// <param name="length">The size of the region</param>
    /// <returns>A handle to the torrent or <see cref="IntPtr.Zero"/> if there an error occurred</returns>
    [LibraryImport(LibraryName, EntryPoint = "create_torrent_bytes")]
    public static partial IntPtr CreateTorrentFromBytes(IntPtr content, long length);

    /// <summary>
    /// Releases the unmanaged resources associated with a torrent.
    /// </summary>
    /// <param name="torrentHandle">
    /// A handle to the torrent.
    /// This can be obtained from either <see cref="CreateTorrentFromFile"/> or <see cref="CreateTorrentFromBytes"/>
    /// </param>
    [LibraryImport(LibraryName, EntryPoint = "destroy_torrent")]
    public static partial void FreeTorrent(IntPtr torrentHandle);

    /// <summary>
    /// Attach a torrent to a session, allowing it to be downloaded
    /// </summary>
    /// <param name="sessionHandle">The session handle to attach the torrent to</param>
    /// <param name="torrentHandle">The handle of the torrent to attach</param>
    /// <param name="savePath">The path to save the contents of the torrent to</param>
    /// <returns>A torrent-session handle</returns>
    [LibraryImport(LibraryName, EntryPoint = "attach_torrent", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr AttachTorrent(IntPtr sessionHandle, IntPtr torrentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string savePath);

    /// <summary>
    /// Detaches a torrent from a session, stopping the download.
    /// </summary>
    /// <param name="sessionHandle">The session handle to detach from</param>
    /// <param name="torrentSessionHandle">The torrent-session handle to detach</param>
    /// <param name="removeFlags">libtorrent remove_flags_t bits (0 = none, 1 = delete_files, 2 = delete_partfile).</param>
    /// <remarks>
    /// After this method has returned, the <see cref="sessionHandle"/> has been released and is no longer valid for use.
    /// </remarks>
    [LibraryImport(LibraryName, EntryPoint = "detach_torrent")]
    public static partial void DetachTorrent(IntPtr sessionHandle, IntPtr torrentSessionHandle, int removeFlags);

    /// <summary>
    /// Parses a magnet URI and adds the resulting torrent to the session.
    /// Metadata arrives asynchronously via alerts.
    /// </summary>
    /// <param name="sessionHandle">The session handle to attach the torrent to</param>
    /// <param name="magnetUri">A BEP-9 magnet URI</param>
    /// <param name="savePath">The path to save the contents of the torrent to</param>
    /// <returns>A torrent-session handle, or <see cref="IntPtr.Zero"/> on parse failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "lts_add_magnet", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr AddMagnet(IntPtr sessionHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string magnetUri, [MarshalAs(UnmanagedType.LPUTF8Str)] string savePath);

    /// <summary>
    /// Requests the torrent's resume data. Completes asynchronously — the resume blob
    /// surfaces via <see cref="Alerts.ResumeDataReadyAlert"/> on the event callback.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_request_resume_data")]
    public static partial void RequestResumeData(IntPtr torrentSessionHandle);

    /// <summary>
    /// Discards the torrent's piece-state and re-hashes the on-disk files from scratch.
    /// Progress surfaces asynchronously via the normal status alerts.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_force_recheck")]
    public static partial void ForceRecheck(IntPtr torrentSessionHandle);

    /// <summary>
    /// Sends a scrape request to the torrent's trackers. Completes asynchronously;
    /// success surfaces as <see cref="Alerts.ScrapeReplyAlert"/>, failure as
    /// <see cref="Alerts.ScrapeFailedAlert"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_scrape_tracker")]
    public static partial void ScrapeTracker(IntPtr torrentSessionHandle);

    /// <summary>
    /// Queue-aware pause: sets the paused flag and enables auto-management so
    /// libtorrent can resume the torrent when queue slots free up.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_pause_torrent")]
    public static partial void PauseTorrent(IntPtr torrentSessionHandle);

    /// <summary>
    /// Queue-aware resume: clears the paused flag and enables auto-management
    /// so libtorrent's queue governs running/pausing.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_resume_torrent")]
    public static partial void ResumeTorrent(IntPtr torrentSessionHandle);

    /// <summary>
    /// Force-starts or un-force-starts a torrent. When <paramref name="forceStart"/>
    /// is true, clears auto-management and resumes so the queue cannot re-pause the
    /// torrent. When false, re-enables auto-management so the queue resumes governance.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_force_start_torrent")]
    public static partial void ForceStartTorrent(IntPtr torrentSessionHandle, [MarshalAs(UnmanagedType.Bool)] bool forceStart);

    /// <summary>
    /// Relocates the torrent's data to <paramref name="newPath"/>. Completes
    /// asynchronously; outcome surfaces via storage_moved[_failed]_alert.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_move_storage", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void MoveStorage(IntPtr torrentSessionHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath, int flags);

    /// <summary>
    /// Populates <paramref name="peers"/> with a snapshot of the torrent's connected peers.
    /// The buffer is native-owned; release via <see cref="FreePeerList"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_get_peers")]
    public static partial void GetPeers(IntPtr torrentSessionHandle, out NativeStructs.PeerList peers);

    /// <summary>
    /// Releases the native array populated by <see cref="GetPeers"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_peers")]
    public static partial void FreePeerList(ref NativeStructs.PeerList peers);

    /// <summary>
    /// Populates <paramref name="trackers"/> with a snapshot of the torrent's trackers.
    /// Buffer is native-owned; release via <see cref="FreeTrackerList"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_get_trackers")]
    public static partial void GetTrackers(IntPtr torrentSessionHandle, out NativeStructs.TrackerList trackers);

    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_trackers")]
    public static partial void FreeTrackerList(ref NativeStructs.TrackerList trackers);

    [LibraryImport(LibraryName, EntryPoint = "lts_add_tracker", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void AddTracker(IntPtr torrentHandle, string url, int tier);

    [LibraryImport(LibraryName, EntryPoint = "lts_remove_tracker", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void RemoveTracker(IntPtr torrentHandle, string url);

    [LibraryImport(LibraryName, EntryPoint = "lts_edit_tracker", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void EditTracker(IntPtr torrentHandle, string oldUrl, string newUrl, int newTier);

    [LibraryImport(LibraryName, EntryPoint = "lts_get_web_seeds")]
    public static partial void GetWebSeeds(IntPtr torrentHandle, out NativeStructs.WebSeedList outList);

    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_web_seeds")]
    public static partial void FreeWebSeedList(ref NativeStructs.WebSeedList list);

    [LibraryImport(LibraryName, EntryPoint = "lts_add_web_seed", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void AddWebSeed(IntPtr torrentHandle, string url);

    [LibraryImport(LibraryName, EntryPoint = "lts_remove_web_seed", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void RemoveWebSeed(IntPtr torrentHandle, string url);

    [LibraryImport(LibraryName, EntryPoint = "lts_export_torrent_to_bytes")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool ExportTorrentToBytes(IntPtr torrentHandle, out IntPtr outData, out int outSize);

    [LibraryImport(LibraryName, EntryPoint = "lts_free_bytes")]
    public static partial void FreeBytes(IntPtr data);

    /// <summary>Per-torrent upload cap in bytes/sec. 0 = unlimited.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_set_torrent_upload_limit")]
    public static partial void SetUploadLimit(IntPtr torrentSessionHandle, int bytesPerSecond);

    [LibraryImport(LibraryName, EntryPoint = "lts_get_torrent_upload_limit")]
    public static partial int GetUploadLimit(IntPtr torrentSessionHandle);

    /// <summary>Per-torrent download cap in bytes/sec. 0 = unlimited.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_set_torrent_download_limit")]
    public static partial void SetDownloadLimit(IntPtr torrentSessionHandle, int bytesPerSecond);

    [LibraryImport(LibraryName, EntryPoint = "lts_get_torrent_download_limit")]
    public static partial int GetDownloadLimit(IntPtr torrentSessionHandle);

    /// <summary>
    /// Toggles libtorrent's super-seeding mode. Only has an effect once the torrent
    /// is in the seeding state.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_set_super_seeding")]
    public static partial void SetSuperSeeding(IntPtr torrentSessionHandle, [MarshalAs(UnmanagedType.I1)] bool enabled);

    /// <summary>
    /// Reads the torrent's current <c>torrent_flags_t</c> bitset. Returns 0 when
    /// the handle is invalid.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_get_torrent_flags")]
    public static partial ulong GetTorrentFlags(IntPtr torrentSessionHandle);

    /// <summary>
    /// Applies <paramref name="flags"/> to the torrent under <paramref name="mask"/> —
    /// only bits where mask is 1 are rewritten; the rest are left untouched.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_set_torrent_flags")]
    public static partial void SetTorrentFlags(IntPtr torrentSessionHandle, ulong flags, ulong mask);

    /// <summary>Clears every bit set in <paramref name="flags"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_unset_torrent_flags")]
    public static partial void UnsetTorrentFlags(IntPtr torrentSessionHandle, ulong flags);

    /// <summary>
    /// Toggles libtorrent's sequential download mode — pieces requested in order
    /// instead of rarest-first.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_set_sequential")]
    public static partial void SetSequentialDownload(IntPtr torrentSessionHandle, [MarshalAs(UnmanagedType.I1)] bool enabled);

    /// <summary>
    /// Sets the priority of a file's first and last piece. No-op when metadata
    /// hasn't resolved yet.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_set_file_piece_priority")]
    public static partial void SetFileFirstLastPiecePriority(IntPtr torrentSessionHandle, int fileIndex, byte priority);

    /// <summary>
    /// Reads a single piece's download priority. Returns 0 (skip) on invalid
    /// handle, missing metadata, or out-of-range index.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_get_piece_priority")]
    public static partial byte GetPiecePriority(IntPtr torrentSessionHandle, int pieceIndex);

    /// <summary>
    /// Sets a single piece's download priority. No-op on invalid handle,
    /// missing metadata, or out-of-range index.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_set_piece_priority")]
    public static partial void SetPiecePriority(IntPtr torrentSessionHandle, int pieceIndex, byte priority);

    /// <summary>
    /// Snapshot of every piece's download priority. Release via
    /// <see cref="FreePiecePriorities"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_get_piece_priorities")]
    public static partial void GetPiecePriorities(IntPtr torrentSessionHandle, out NativeStructs.PiecePriorityList list);

    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_piece_priorities")]
    public static partial void FreePiecePriorities(ref NativeStructs.PiecePriorityList list);

    /// <summary>
    /// Replaces every piece's download priority in one call. Indices beyond
    /// the torrent's piece count are silently truncated.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_set_piece_priorities")]
    public static partial void SetPiecePriorities(IntPtr torrentSessionHandle, byte[] priorities, int count);

    /// <summary>
    /// True when the piece is fully downloaded and verified on disk. Returns
    /// false on invalid handle, missing metadata, or out-of-range index.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_have_piece")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool HavePiece(IntPtr torrentSessionHandle, int pieceIndex);

    /// <summary>
    /// Fills <paramref name="outBits"/> with a packed LSB-first bitfield of
    /// piece completion in one native call. <paramref name="numBytes"/> must
    /// equal <c>ceil(numPieces / 8)</c>. No-op on null or invalid handle.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_get_piece_bitfield")]
    public static partial void GetPieceBitfield(IntPtr torrentSessionHandle, [Out] byte[] outBits, int numBytes);

    /// <summary>
    /// Queues an explicit peer connect attempt. <paramref name="ipv6Address"/>
    /// must be exactly 16 bytes (v4-mapped v6 for v4 peers). Returns true when
    /// the attempt was queued, false on invalid handle / port==0 / bad inputs.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_connect_peer")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool ConnectPeer(IntPtr torrentSessionHandle, byte[] ipv6Address, ushort port);

    /// <summary>
    /// Clears any sticky error state on the torrent — e.g. after an
    /// out-of-disk failure. No-op on invalid handle.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_clear_error")]
    public static partial void ClearError(IntPtr torrentSessionHandle);

    /// <summary>
    /// Renames a file inside the torrent. The rename is asynchronous — result
    /// fires via <c>file_renamed_alert</c> / <c>file_rename_failed_alert</c>.
    /// No-op on invalid handle / out-of-range index / pre-metadata / null or
    /// empty name.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_rename_file", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void RenameFile(IntPtr torrentSessionHandle, int fileIndex, [MarshalAs(UnmanagedType.LPUTF8Str)] string newName);

    /// <summary>
    /// Snapshot of port-forwarding mappings tracked on the session. Release via
    /// <see cref="FreePortMappings"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_get_port_mappings")]
    public static partial void GetPortMappings(IntPtr sessionHandle, out NativeStructs.PortMappingList mappings);

    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_port_mappings")]
    public static partial void FreePortMappings(ref NativeStructs.PortMappingList mappings);

    /// <summary>
    /// Replaces the session's IP filter with the supplied rule array. Pass
    /// <paramref name="length"/> = 0 (and <paramref name="rules"/> may be null)
    /// to clear the filter entirely.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_set_ip_filter")]
    public static unsafe partial void SetIpFilter(IntPtr sessionHandle, NativeStructs.IpFilterRule* rules, int length);

    /// <summary>
    /// Exports the session's current IP filter. Native-allocated; release via
    /// <see cref="FreeIpFilter"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_get_ip_filter")]
    public static partial void GetIpFilter(IntPtr sessionHandle, out NativeStructs.IpFilterRules rules);

    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_ip_filter")]
    public static partial void FreeIpFilter(ref NativeStructs.IpFilterRules rules);

    /// <summary>
    /// Triggers an asynchronous <c>dht_stats_alert</c> on the session. The alert
    /// surfaces routing-table totals (nodes, replacements, active requests).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_post_dht_stats")]
    public static partial void PostDhtStats(IntPtr sessionHandle);

    /// <summary>
    /// Triggers an asynchronous <c>session_stats_alert</c> on the session. The
    /// alert surfaces a flat <c>int64</c> counter array; the metric name → index
    /// mapping is queried via the SessionStatsMetric* methods below.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_post_session_stats")]
    public static partial void PostSessionStats(IntPtr sessionHandle);

    /// <summary>
    /// Total number of metrics in libtorrent's session-stats registry. The
    /// registry is build-static — same answer every call.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_session_stats_metric_count")]
    public static partial int SessionStatsMetricCount();

    /// <summary>
    /// Fills metric metadata for the entry at <paramref name="idx"/>. The name
    /// pointer references a libtorrent-owned static literal; the caller must
    /// not free it.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_session_stats_metric_at")]
    public static partial void SessionStatsMetricAt(int idx, out IntPtr outName, out int outValueIndex, out int outType);

    /// <summary>
    /// Resolves a metric name to its <c>value_index</c> (the position in
    /// <see cref="NativeEvents.SessionStatsAlert.counters"/>). Returns -1 when
    /// no metric matches.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_session_stats_find_metric_idx", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SessionStatsFindMetricIdx(string name);

    /// <summary>
    /// Captures the session's full state as a heap-allocated bencoded buffer.
    /// On success, <paramref name="outBuf"/> points at native memory the caller
    /// must release via <see cref="FreeSessionStateBuf"/>. On failure, both
    /// outputs are zeroed.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_session_save_state")]
    public static partial void SessionSaveState(IntPtr sessionHandle, out IntPtr outBuf, out int outLen);

    /// <summary>Releases a buffer returned by <see cref="SessionSaveState"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_session_state_buf")]
    public static partial void FreeSessionStateBuf(IntPtr buf);

    /// <summary>
    /// Constructs a new session from a previously-captured state buffer.
    /// Returns <see cref="IntPtr.Zero"/> on parse failure (malformed bencoding,
    /// invalid keys, etc.).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_create_session_from_state")]
    public static partial IntPtr CreateSessionFromState([In] byte[] buf, int length);

    /// <summary>Returns the TCP port the session is actually listening on, or 0 when not bound.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_session_listen_port")]
    public static partial ushort SessionListenPort(IntPtr sessionHandle);

    /// <summary>Returns the SSL/TLS port when SSL is enabled; 0 otherwise.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_session_ssl_listen_port")]
    public static partial ushort SessionSslListenPort(IntPtr sessionHandle);

    /// <summary>Returns 1 when the session has any open listen sockets, 0 otherwise.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_session_is_listening")]
    public static partial sbyte SessionIsListening(IntPtr sessionHandle);

    /// <summary>
    /// Reads back the session's currently-effective <c>listen_interfaces</c>
    /// setting string. <paramref name="outBuf"/> is heap-allocated on the
    /// native side; release via <see cref="FreeListenInterfacesBuf"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_session_get_listen_interfaces")]
    public static partial void SessionGetListenInterfaces(IntPtr sessionHandle, out IntPtr outBuf, out int outLen);

    /// <summary>Releases a buffer returned by <see cref="SessionGetListenInterfaces"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_listen_interfaces_buf")]
    public static partial void FreeListenInterfacesBuf(IntPtr buf);

    /// <summary>
    /// Stores an immutable BEP44 item (a binary string blob) on the DHT and
    /// writes the SHA-1 target the data will be retrievable under to the
    /// 20-byte <paramref name="outTargetSha1"/> buffer. The actual put is
    /// asynchronous; completion fires a <c>dht_put_alert</c> (currently
    /// surfaced only as a generic alert).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_dht_put_immutable_string")]
    public static partial void DhtPutImmutableString(
        IntPtr sessionHandle,
        [In] byte[] data,
        int dataLen,
        [Out] byte[] outTargetSha1);

    /// <summary>
    /// Issues an asynchronous DHT lookup for an immutable BEP44 item identified
    /// by <paramref name="targetSha1"/> (20 bytes). The result arrives as a
    /// <c>dht_immutable_item_alert</c>; misses time out internally without
    /// firing an alert.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_dht_get_immutable")]
    public static partial void DhtGetImmutable(
        IntPtr sessionHandle,
        [In] byte[] targetSha1);

    /// <summary>
    /// Issues an asynchronous DHT lookup for a mutable BEP44 item identified by
    /// an Ed25519 public key (32 bytes) and an optional salt. Pass salt=null,
    /// saltLen=0 when no salt was used. Result arrives as a
    /// <c>dht_mutable_item_alert</c>; misses time out internally without firing.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_dht_get_item_mutable")]
    public static partial void DhtGetItemMutable(
        IntPtr sessionHandle,
        [In] byte[] key32,
        [In] byte[] salt,
        int saltLen);

    /// <summary>Generates 32 random bytes suitable as an Ed25519 seed.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_ed25519_create_seed")]
    public static partial void Ed25519CreateSeed([Out] byte[] outSeed32);

    /// <summary>
    /// Derives a deterministic Ed25519 keypair from <paramref name="seed32"/>:
    /// <paramref name="outPublicKey32"/> = 32 bytes, <paramref name="outSecretKey64"/> = 64 bytes.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_ed25519_create_keypair")]
    public static partial void Ed25519CreateKeypair(
        [In] byte[] seed32,
        [Out] byte[] outPublicKey32,
        [Out] byte[] outSecretKey64);

    /// <summary>
    /// Signs <paramref name="message"/> with the keypair, writing 64 bytes to
    /// <paramref name="outSignature64"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_ed25519_sign")]
    public static partial void Ed25519Sign(
        [In] byte[] message,
        int messageLen,
        [In] byte[] publicKey32,
        [In] byte[] secretKey64,
        [Out] byte[] outSignature64);

    /// <summary>Returns 1 when the signature verifies, 0 otherwise.</summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_ed25519_verify")]
    public static partial sbyte Ed25519Verify(
        [In] byte[] signature64,
        [In] byte[] message,
        int messageLen,
        [In] byte[] publicKey32);

    /// <summary>
    /// Stores a mutable BEP44 item on the DHT. The shim signs the value
    /// internally via <c>lt::dht::sign_mutable_item</c>; the caller only
    /// supplies keys + salt + raw bytes + sequence number. Completion fires a
    /// <c>dht_put_alert</c> with the mutable envelope filled in.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_dht_put_item_mutable")]
    public static partial void DhtPutItemMutable(
        IntPtr sessionHandle,
        [In] byte[] publicKey32,
        [In] byte[] secretKey64,
        [In] byte[] salt,
        int saltLen,
        [In] byte[] value,
        int valueLen,
        long seq);

    /// <summary>
    /// Attaches a torrent from a previously-captured resume blob (bencoded
    /// add_torrent_params produced by <c>write_resume_data_buf</c>).
    /// </summary>
    /// <returns>A torrent-session handle, or <see cref="IntPtr.Zero"/> on parse failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "lts_add_torrent_with_resume", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr AddTorrentWithResume(IntPtr sessionHandle, byte[] resumeData, int length, [MarshalAs(UnmanagedType.LPUTF8Str)] string savePath);

    /// <summary>
    /// Gets the torrent info from the torrent handle.
    /// </summary>
    /// <param name="torrentHandle">The handle of the torrent</param>
    /// <returns>A handle to a <see cref="NativeStructs.TorrentFile"/> struct (torrent metadata handle)</returns>
    /// <remarks>A call to FreeTorrentInfo is needed to release unmanaged resources after usage has finished.</remarks>
    [LibraryImport(LibraryName, EntryPoint = "get_torrent_info")]
    public static partial IntPtr GetTorrentInfo(IntPtr torrentHandle);

    /// <summary>
    /// Destroys a torrent info handle.
    /// </summary>
    /// <param name="torrentInfoHandle">The handle to release</param>
    [LibraryImport(LibraryName, EntryPoint = "destroy_torrent_info")]
    public static partial void FreeTorrentInfo(IntPtr torrentInfoHandle);

    /// <summary>
    /// Uniform piece length in bytes. Returns 0 on invalid handle.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_piece_length")]
    public static partial int TorrentInfoPieceLength(IntPtr torrentInfoHandle);

    /// <summary>
    /// Returns the piece count for a torrent handle. Works for TorrentHandle and for
    /// resume-loaded MagnetHandle with embedded metadata. Returns 0 when metadata has
    /// not yet resolved (pre-metadata magnet).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_num_pieces")]
    public static partial int TorrentHandleNumPieces(IntPtr torrentHandle);

    /// <summary>
    /// Total size of all files in the torrent in bytes. Works for TorrentHandle and
    /// resume-loaded MagnetHandle with embedded metadata. Returns 0 when metadata
    /// has not yet resolved.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_total_size")]
    public static partial long TorrentHandleTotalSize(IntPtr torrentHandle);

    /// <summary>
    /// Gets the file list for a torrent handle directly. Works for TorrentHandle and
    /// resume-loaded MagnetHandle with embedded metadata. Caller must free with
    /// <see cref="FreeTorrentFileList"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_handle_file_list")]
    public static partial void GetTorrentHandleFileList(IntPtr torrentHandle, out NativeStructs.TorrentFileList files);

    /// <summary>
    /// Total number of pieces in the torrent. Returns 0 on invalid handle.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_num_pieces")]
    public static partial int TorrentInfoNumPieces(IntPtr torrentInfoHandle);

    /// <summary>
    /// Size of the piece at <paramref name="pieceIndex"/> in bytes. Equal to
    /// the uniform piece length for every piece except (potentially) the last.
    /// Returns 0 on invalid handle or out-of-range index.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_piece_size")]
    public static partial int TorrentInfoPieceSize(IntPtr torrentInfoHandle, int pieceIndex);

    /// <summary>
    /// Fills <paramref name="outHash20"/> with the V1 SHA-1 hash of the piece
    /// at <paramref name="pieceIndex"/>. Returns false (and leaves the buffer
    /// untouched) on invalid handle, out-of-range index, or V2-only torrent.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_hash_for_piece")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool TorrentInfoHashForPiece(IntPtr torrentInfoHandle, int pieceIndex, [Out] byte[] outHash20);

    /// <summary>
    /// True when the torrent carries BEP-52 v2 metadata (SHA-256 merkle trees
    /// — v2-only or hybrid v1+v2). Returns false on invalid handle.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_is_v2")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool TorrentInfoIsV2(IntPtr torrentInfoHandle);

    /// <summary>
    /// Per-file flag bits from <c>file_storage::file_flags_t</c>. Returns 0 on
    /// invalid handle or out-of-range index.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_file_flags")]
    public static partial byte TorrentInfoFileFlags(IntPtr torrentInfoHandle, int fileIndex);

    /// <summary>
    /// Fills <paramref name="outRoot32"/> with the V2 SHA-256 file root merkle
    /// hash. Returns false on invalid handle / out-of-range / non-V2 torrent
    /// / file without a stored root (all-zero hash surfaces as false).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_file_root")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool TorrentInfoFileRoot(IntPtr torrentInfoHandle, int fileIndex, [Out] byte[] outRoot32);

    /// <summary>
    /// Copies the UTF-8 symlink target for a file marked with the symlink flag
    /// into <paramref name="buffer"/>, NUL-terminated. Returns the number of
    /// bytes written (excluding NUL), or 0 on invalid handle / out-of-range /
    /// non-symlink file / empty target. Truncates to
    /// <paramref name="bufferSize"/> - 1 without failure.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_symlink")]
    public static partial int TorrentInfoSymlink(IntPtr torrentInfoHandle, int fileIndex, [Out] byte[] buffer, int bufferSize);

    /// <summary>
    /// Maps a byte offset in the torrent's virtual concatenated stream to the
    /// file index that contains it. Returns -1 on invalid handle, negative
    /// offset, or offset ≥ total size.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_file_index_at_offset")]
    public static partial int TorrentInfoFileIndexAtOffset(IntPtr torrentInfoHandle, long offset);

    /// <summary>
    /// Copies the V2 piece-layer bytes (concatenated SHA-256 leaves) for a
    /// single file into <paramref name="outBuffer"/>. Pass an empty buffer
    /// with <paramref name="bufferSize"/> = 0 to query the required byte
    /// count. Returns bytes written on fill, required count on query, or 0
    /// when the file has no piece layer (V1-only torrent, out-of-range, or
    /// invalid handle). Layer length is always a multiple of 32.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_piece_layer")]
    public static partial int TorrentInfoPieceLayer(IntPtr torrentInfoHandle, int fileIndex, [Out] byte[] outBuffer, int bufferSize);

    /// <summary>
    /// Number of pieces that overlap a file. Returns 0 on invalid handle or
    /// out-of-range file index.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_file_num_pieces")]
    public static partial int TorrentInfoFileNumPieces(IntPtr torrentInfoHandle, int fileIndex);

    /// <summary>
    /// Number of fixed-size 16 KiB blocks that overlap a file. Returns 0 on
    /// invalid handle or out-of-range file index.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_file_num_blocks")]
    public static partial int TorrentInfoFileNumBlocks(IntPtr torrentInfoHandle, int fileIndex);

    /// <summary>
    /// Piece extent for a file: writes the first piece overlapping the file
    /// and one-past-the-last into <paramref name="firstPiece"/> /
    /// <paramref name="endPiece"/>. Returns false on invalid handle, null
    /// out pointers, or out-of-range file index.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_file_piece_range")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool TorrentInfoFilePieceRange(IntPtr torrentInfoHandle, int fileIndex, out int firstPiece, out int endPiece);

    /// <summary>
    /// Maps a (file, byte-offset, size) tuple onto the piece containing the
    /// start of that range. Returns true on success and writes the piece
    /// index, byte offset within the piece, and capped length through the
    /// out parameters. Returns false (leaving out params zero) on invalid
    /// handle, negative inputs, or out-of-range file index.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_map_file")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool TorrentInfoMapFile(IntPtr torrentInfoHandle, int fileIndex, long offset, int size, out int pieceIndex, out int pieceOffset, out int length);

    /// <summary>
    /// Inverse of <see cref="TorrentInfoMapFile"/>: returns the list of file
    /// slices a piece-offset range spans. Populates <paramref name="list"/>
    /// with a native-allocated buffer; release via <see cref="FreeFileSliceList"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_map_block")]
    public static partial void TorrentInfoMapBlock(IntPtr torrentInfoHandle, int pieceIndex, long offset, int size, out NativeStructs.FileSliceList list);

    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_file_slice_list")]
    public static partial void FreeFileSliceList(ref NativeStructs.FileSliceList list);

    /// <summary>
    /// Per-file modification time as Unix epoch seconds. Returns 0 on invalid
    /// handle, out-of-range index, or when the .torrent didn't record an mtime
    /// for the file (most don't).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_torrent_info_file_mtime")]
    public static partial long TorrentInfoFileMtime(IntPtr torrentInfoHandle, int fileIndex);

    /// <summary>
    /// Request a list of files contained within a torrent.
    /// </summary>
    /// <param name="torrentHandle">Handle of the torrent file to get info for</param>
    /// <param name="files">Location of the <see cref="NativeStructs.TorrentFileList"/> to populate</param>
    [LibraryImport(LibraryName, EntryPoint = "get_torrent_file_list")]
    public static partial void GetTorrentFileList(IntPtr torrentHandle, out NativeStructs.TorrentFileList files);

    /// <summary>
    /// Release the unmanaged resources associated with a <see cref="files"/>.
    /// </summary>
    /// <param name="files">The file list to release</param>
    [LibraryImport(LibraryName, EntryPoint = "destroy_torrent_file_list")]
    public static partial void FreeTorrentFileList(ref NativeStructs.TorrentFileList files);

    /// <summary>
    /// Get the download priority of a file within a torrent.
    /// </summary>
    /// <param name="torrentSessionHandle">The torrent session handle</param>
    /// <param name="fileIndex">The index of the file to get the priority of</param>
    [LibraryImport(LibraryName, EntryPoint = "get_file_dl_priority")]
    public static partial FileDownloadPriority GetFilePriority(IntPtr torrentSessionHandle, int fileIndex);

    [LibraryImport(LibraryName, EntryPoint = "lts_file_progress")]
    public static partial void GetFileProgress(IntPtr torrentHandle, Span<long> outArray, int numFiles);

    /// <summary>
    /// Sets the download priority of a file within a torrent.
    /// </summary>
    /// <param name="torrentSessionHandle">The torrent session handle</param>
    /// <param name="fileIndex">The index of the file to set the priority of</param>
    /// <param name="priority">The download priority to apply</param>
    [LibraryImport(LibraryName, EntryPoint = "set_file_dl_priority")]
    public static partial void SetFilePriority(IntPtr torrentSessionHandle, int fileIndex, FileDownloadPriority priority);

    /// <summary>
    /// Starts or resumes a torrent download
    /// </summary>
    /// <param name="torrentSessionHandle">The torrent session handle to start</param>
    [LibraryImport(LibraryName, EntryPoint = "start_torrent")]
    public static partial void StartTorrent(IntPtr torrentSessionHandle);

    /// <summary>
    /// Stops a torrent download
    /// </summary>
    /// <param name="torrentSessionHandle">The torrent session handle to stop</param>
    [LibraryImport(LibraryName, EntryPoint = "stop_torrent")]
    public static partial void StopTorrent(IntPtr torrentSessionHandle);

    /// <summary>
    /// Re-announce the torrent to all trackers.
    /// </summary>
    /// <param name="torrentSessionHandle">The torrent session handle to apply the reannounce to</param>
    /// <param name="seconds">The delay, in seconds, before performing the reannouncement</param>
    /// <param name="force">Whether to ignore min times between reannounces</param>
    [LibraryImport(LibraryName, EntryPoint = "reannounce_torrent")]
    public static partial void ReannounceTorrent(IntPtr torrentSessionHandle, int seconds, [MarshalAs(UnmanagedType.I1)] bool force);

    /// <summary>
    /// Retrieves a heap-allocated status snapshot. Caller must release via
    /// <see cref="FreeTorrentStatus"/> after copying the fields into managed memory.
    /// Returns <see cref="IntPtr.Zero"/> when the handle is invalid.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_get_torrent_status")]
    public static partial IntPtr GetTorrentStatus(IntPtr torrentSessionHandle);

    /// <summary>
    /// Releases a status snapshot returned by <see cref="GetTorrentStatus"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_destroy_torrent_status")]
    public static partial void FreeTorrentStatus(IntPtr statusHandle);

    /// <summary>
    /// Builds a .torrent file from <paramref name="sourcePath"/> and writes the
    /// bencoded result to <paramref name="outputPath"/>. Hashes synchronously
    /// on the calling thread — managed callers wrap on a worker.
    /// <paramref name="trackers"/> is newline-separated tracker URLs (a blank line
    /// increments the tier; matches qBittorrent's flat representation).
    /// <paramref name="webSeeds"/> is newline-separated web-seed URLs.
    /// <paramref name="ignoreHidden"/> non-zero skips dotfile-named entries (Unix-hidden).
    /// <paramref name="progressCb"/> fires once with currentPiece=0 before hashing and once
    /// per piece — may be null. <paramref name="cancelFlag"/> is a pinned <c>int32</c> the
    /// caller flips to 1 to abort hashing — may be null. <paramref name="errorBuf"/>
    /// receives a UTF-8 NUL-terminated message on failure; untouched on success. Returns
    /// 0 on success; negative codes documented on the C ABI.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "lts_create_torrent", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int CreateTorrent(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sourcePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string outputPath,
        int pieceSize,
        int isPrivate,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? comment,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? createdBy,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? trackers,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? webSeeds,
        int ignoreHidden,
        [MarshalAs(UnmanagedType.FunctionPtr)] CreateTorrentProgressCallback? progressCb,
        IntPtr progressCtx,
        int* cancelFlag,
        byte* errorBuf,
        int errorBufSize);

    /// <summary>
    /// Creates an empty settings pack
    /// </summary>
    /// <remarks>
    /// This method is used to create a settings pack that can be customised and applied to a session.
    /// Add items using the SettingsPackAdd* methods, and apply it using either <see cref="ApplySettingsPack"/> or <see cref="CreateSession"/>.
    /// Note this handle needs to be manually freed, even after applying it to a session.
    /// </remarks>
    /// <returns>A handle to the created settings pack</returns>
    [LibraryImport(LibraryName, EntryPoint = "create_settings_pack")]
    public static partial IntPtr CreateSettingsPack();

    /// <summary>
    /// Frees a settings pack by its handle.
    /// </summary>
    /// <param name="settingsPack">The pack to free</param>
    [LibraryImport(LibraryName, EntryPoint = "destroy_settings_pack")]
    public static partial void FreeSettingsPack(IntPtr settingsPack);

    /// <summary>
    /// Adds an <see cref="int"/> value to the settings pack
    /// </summary>
    /// <param name="settingsPack">The pack handle</param>
    /// <param name="key">The configuration key to set</param>
    /// <param name="value">The integer value to apply</param>
    /// <returns>
    /// Whether the value was successfully added.
    /// If false, either the configuration does not exist or is the wrong type.
    /// </returns>
    [return: MarshalAs(UnmanagedType.I1)]
    [LibraryImport(LibraryName, EntryPoint = "settings_pack_set_int", StringMarshalling = StringMarshalling.Utf8)]
    public static partial bool SettingsPackSetInt(IntPtr settingsPack, string key, int value);

    /// <summary>
    /// Adds a <see cref="bool"/> value to the settings pack
    /// </summary>
    /// <param name="settingsPack">The pack handle</param>
    /// <param name="key">The configuration key to set</param>
    /// <param name="value">The boolean value to apply</param>
    /// <returns>
    /// Whether the value was successfully added.
    /// If false, either the configuration does not exist or is the wrong type.
    /// </returns>
    [return: MarshalAs(UnmanagedType.I1)]
    [LibraryImport(LibraryName, EntryPoint = "settings_pack_set_bool", StringMarshalling = StringMarshalling.Utf8)]
    public static partial bool SettingsPackSetBool(IntPtr settingsPack, string key, [MarshalAs(UnmanagedType.I1)] bool value);

    /// <summary>
    /// Adds a <see cref="string"/> to the settings pack
    /// </summary>
    /// <param name="settingsPack">The pack handle</param>
    /// <param name="key">The configuration key to set</param>
    /// <param name="value">The value to apply</param>
    /// <returns>
    /// Whether the value was successfully added.
    /// If false, either the configuration does not exist or is the wrong type.
    /// </returns>
    [return: MarshalAs(UnmanagedType.I1)]
    [LibraryImport(LibraryName, EntryPoint = "settings_pack_set_str", StringMarshalling = StringMarshalling.Utf8)]
    public static partial bool SettingsPackSetString(IntPtr settingsPack, string key, string value);

}