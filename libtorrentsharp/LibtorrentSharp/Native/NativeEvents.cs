// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

using System;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp.Native;

internal static class NativeEvents
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct AlertBase
    {
        [MarshalAs(UnmanagedType.I4)]
        public AlertType type;

        public int category;
        public long timestamp;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentStatusAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public TorrentState old_state;
        public TorrentState new_state;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentRemovedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentPausedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentResumedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentFinishedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentCheckedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct StorageMovedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        // Native dispatcher-owned UTF-8 strings; managed side reads via
        // Marshal.PtrToStringUTF8 before the callback returns.
        public IntPtr storage_path;
        public IntPtr old_path;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct StorageMovedFailedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;

        public IntPtr file_path;
        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TrackerReplyAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int num_peers;

        // Native dispatcher-owned UTF-8 string; managed side reads via
        // Marshal.PtrToStringUTF8 before the callback returns.
        public IntPtr tracker_url;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TrackerErrorAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;
        public int times_in_row;

        public IntPtr tracker_url;
        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ScrapeReplyAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int incomplete;
        public int complete;

        public IntPtr tracker_url;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ScrapeFailedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;

        public IntPtr tracker_url;
        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TrackerAnnounceAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int @event;

        public IntPtr tracker_url;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TrackerWarningAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public IntPtr tracker_url;
        public IntPtr warning_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct FileRenamedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int file_index;

        public IntPtr new_name;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct FileRenameFailedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int file_index;
        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct FastresumeRejectedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct SaveResumeDataFailedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentDeletedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentDeleteFailedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct MetadataReceivedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct MetadataFailedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentErrorAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;

        public IntPtr filename;
        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct FileErrorAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;
        public int op;

        public IntPtr filename;
        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct UdpErrorAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] endpoint_address;

        public int endpoint_port;
        public int operation;
        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct SessionErrorAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtErrorAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public int operation;
        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct LsdErrorAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] local_address;

        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct HashFailedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int piece_index;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ExternalIpAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] external_address;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PortmapAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public int mapping;
        public int external_port;
        public byte map_protocol;
        public byte map_transport;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] local_address;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PortmapErrorAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public int mapping;
        public byte map_transport;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] local_address;

        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtBootstrapAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtReplyAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int num_peers;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TrackeridAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public IntPtr tracker_url;
        public IntPtr tracker_id;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct CacheFlushedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtAnnounceAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ip_address;

        public int port;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtGetPeersAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtOutgoingGetPeersAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] obfuscated_info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] endpoint_address;

        public int endpoint_port;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct AddTorrentAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentNeedCertAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentConflictAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] conflicting_info_hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct FileCompletedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int file_index;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PieceFinishedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int piece_index;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct UrlSeedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public int error_code;

        public IntPtr server_url;
        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PerformanceWarningAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public PerformanceWarningType warning_code;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct BlockFinishedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] v6_address;

        public int block_index;
        public int piece_index;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct BlockUploadedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] v6_address;

        public int block_index;
        public int piece_index;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PeerBlockedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] v6_address;

        public int reason;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct IncomingConnectionAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] endpoint_address;

        public int endpoint_port;
        public byte socket_type;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct BlockTimeoutAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] v6_address;

        public int block_index;
        public int piece_index;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct BlockDownloadingAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] v6_address;

        public int block_index;
        public int piece_index;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct UnwantedBlockAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] v6_address;

        public int block_index;
        public int piece_index;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct Socks5Alert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] endpoint_address;

        public int endpoint_port;
        public int operation;
        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct I2pAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public int error_code;

        public IntPtr error_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TorrentLogAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        public IntPtr log_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct LogAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public IntPtr log_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtLogAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public int module;

        public IntPtr log_message;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PeerAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public IntPtr handle;
        public PeerAlertType alert_type;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] v6_address;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] peer_id;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ResumeDataAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] info_hash;

        // native buffer; length bytes. Caller must copy before returning from the callback.
        public IntPtr data;
        public int length;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtStatsAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        public int total_nodes;
        public int total_replacements;
        public int active_requests;

        // bucket_count + pointer to bucket_count cs_dht_routing_bucket structs.
        // The buffer lives only for the duration of the alert callback — the
        // managed wrapper MUST copy before returning to the dispatcher.
        public int bucket_count;
        public IntPtr buckets;

        // lookup_count + pointer to lookup_count cs_dht_lookup structs.
        // Same callback-scoped lifetime as buckets.
        public int lookup_count;
        public IntPtr lookups;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtRoutingBucketStruct
    {
        public int num_nodes;
        public int num_replacements;
        public int last_active;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtLookupStruct
    {
        public int outstanding_requests;
        public int timeouts;
        public int responses;
        public int branch_factor;
        public int nodes_left;
        public int last_sent;
        public int first_timeout;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] target;

        public IntPtr type;  // null-terminated UTF-8 string literal (static lifetime)
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtPutAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] target;

        public int num_success;

        // Mutable BEP44 envelope. Zero / empty for immutable puts.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] public_key;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] signature;

        public long seq;

        public IntPtr salt;
        public int salt_len;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtMutableItemAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] public_key;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] signature;

        public long seq;

        public IntPtr salt;
        public int salt_len;

        public IntPtr data;
        public int data_len;

        public sbyte authoritative;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DhtImmutableItemAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] target;

        // Native dispatcher-owned bytes; must be copied before the callback
        // returns. Empty (length 0) for non-string entries.
        public IntPtr data;
        public int length;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct SessionStatsAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        // counters_count + pointer to counters_count int64 values. The buffer
        // lives only for the duration of the alert callback — the managed
        // wrapper MUST copy before returning to the dispatcher.
        public int counters_count;
        public IntPtr counters;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ListenSucceededAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        // v4-mapped v6 representation of the bound local IP. Demap on the
        // managed side via System.Net.IPAddress.IsIPv4MappedToIPv6 / MapToIPv4.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] address;

        public int port;
        public byte socket_type;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ListenFailedAlert
    {
        [MarshalAs(UnmanagedType.Struct)]
        public AlertBase info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] address;

        public int port;
        public byte socket_type;
        public byte op;
        public int error_code;

        // Native dispatcher-owned UTF-8 strings; managed side reads via
        // Marshal.PtrToStringUTF8 before the callback returns.
        public IntPtr listen_interface;
        public IntPtr error_message;
    }
}