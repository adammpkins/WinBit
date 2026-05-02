// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

using System;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp.Native;

internal static class NativeStructs
{
    /// <summary>
    /// Native status snapshot. Matches native <c>cs_torrent_status</c>. Heap-allocated on
    /// the native side; release via <see cref="NativeMethods.FreeTorrentStatus"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct TorrentStatus
    {
        public readonly TorrentState state;
        public readonly float progress;

        public readonly int count_peers;
        public readonly int count_seeds;

        public readonly long bytes_uploaded;
        public readonly long bytes_downloaded;

        public readonly long upload_rate;
        public readonly long download_rate;

        public readonly long all_time_upload;
        public readonly long all_time_download;

        public readonly long active_duration_seconds;
        public readonly long finished_duration_seconds;
        public readonly long seeding_duration_seconds;

        public readonly long eta_seconds;
        public readonly float ratio;

        public readonly ulong flags;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string save_path;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string error_string;
    }

    /// <summary>
    /// Represents a file contained within a torrent.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct TorrentFile
    {
        public readonly int index;

        public readonly long offset;
        public readonly long file_size;

        public readonly long modified_time;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string file_name;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string file_path;

        [MarshalAs(UnmanagedType.I1)]
        public readonly bool file_path_is_absolute;

        public readonly byte flags;
    }

    /// <summary>
    /// Represents a list of files contained within a torrent.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct TorrentFileList
    {
        public readonly int length;
        public readonly IntPtr items;
    }

    /// <summary>
    /// Represents a single file contained within a torrent.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct TorrentMetadata
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string name;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string creator;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string comment;

        public readonly int total_files;
        public readonly long total_size;

        public readonly long creation_epoch;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public readonly byte[] info_hash_sha1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public readonly byte[] info_hash_sha256;
    }

    /// <summary>
    /// Single peer view. Matches native <c>peer_information</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct Peer
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public readonly byte[] ipv6_address;

        public readonly ushort port;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string client;

        public readonly uint flags;
        public readonly byte source;

        public readonly float progress;

        public readonly int up_rate;
        public readonly int down_rate;

        public readonly long total_uploaded;
        public readonly long total_downloaded;

        public readonly int connection_type;
        public readonly int num_hashfails;
        public readonly int downloading_piece_index;
        public readonly int downloading_block_index;
        public readonly int downloading_progress;
        public readonly int downloading_total;
        public readonly int failcount;
        public readonly int payload_up_rate;
        public readonly int payload_down_rate;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public readonly byte[] pid;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct PeerList
    {
        public readonly int length;
        public readonly IntPtr items;
    }

    /// <summary>
    /// Per-tracker aggregate. Matches native <c>tracker_information</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct Tracker
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string url;

        public readonly byte tier;
        public readonly byte source;

        [MarshalAs(UnmanagedType.I1)]
        public readonly bool verified;

        public readonly int scrape_complete;
        public readonly int scrape_incomplete;
        public readonly int scrape_downloaded;

        public readonly byte fails;

        [MarshalAs(UnmanagedType.I1)]
        public readonly bool updating;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string last_error;

        public readonly long next_announce_epoch;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string trackerid;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string message;

        [MarshalAs(UnmanagedType.I1)]
        public readonly bool start_sent;

        [MarshalAs(UnmanagedType.I1)]
        public readonly bool complete_sent;

        public readonly long min_announce_epoch;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct TrackerList
    {
        public readonly int length;
        public readonly IntPtr items;
    }

    /// <summary>
    /// Matches native <c>port_mapping</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct PortMappingEntry
    {
        public readonly int mapping;
        public readonly int external_port;
        public readonly byte protocol;
        public readonly byte transport;

        [MarshalAs(UnmanagedType.I1)]
        public readonly bool has_error;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct PortMappingList
    {
        public readonly int length;
        public readonly IntPtr items;
    }

    /// <summary>
    /// IP-range filter rule. Mirrors native <c>ip_filter_rule</c>: 16 bytes of
    /// v4-mapped v6 address for both endpoints, plus a libtorrent flags bitmask
    /// (0 = allowed, 1 = blocked). Fixed buffers keep the struct blittable for
    /// array marshalling to/from the native side.
    /// </summary>
    /// <remarks>
    /// <c>Size = 40</c> pins the stride to 40 bytes, matching MSVC's tail padding
    /// for the native <c>__declspec(align(8))</c> struct. Managed's natural size
    /// without the attribute is 36, and a smaller stride corrupts every element
    /// past index 0 when marshalling arrays.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 40)]
    public unsafe struct IpFilterRule
    {
        public fixed byte start_ipv6[16];
        public fixed byte end_ipv6[16];
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct IpFilterRules
    {
        public readonly int length;
        public readonly IntPtr items;
    }

    /// <summary>
    /// Matches native <c>piece_priority_list</c>: flat <c>uint8_t</c> array of
    /// libtorrent <c>download_priority_t</c> values. <c>length</c> equals the
    /// torrent's piece count; each byte maps 1:1 to a piece_index_t.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct PiecePriorityList
    {
        public readonly int length;
        public readonly IntPtr priorities;
    }

    /// <summary>
    /// Mirrors native <c>cs_file_slice</c>: one contiguous run within a single
    /// file returned by <c>map_block</c>. int32 file index + int64 offset +
    /// int64 size.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct FileSliceStruct
    {
        public readonly int file_index;
        public readonly long offset;
        public readonly long size;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct FileSliceList
    {
        public readonly int length;
        public readonly IntPtr slices;
    }

    /// <summary>
    /// Single web seed URL entry. Matches native <c>cs_web_seed_information</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct WebSeed
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public readonly string url;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct WebSeedList
    {
        public readonly int count;
        public readonly IntPtr items;
    }
}
