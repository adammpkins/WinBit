// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
//
// structs.hpp
// Created by Albie on 29/02/2024.
//

#ifndef CSDL_STRUCTS_HPP
#define CSDL_STRUCTS_HPP

#include "struct_align.h"

#include <libtorrent/session.hpp>

#ifdef __cplusplus
extern "C" {
#endif

LTS_STRUCT typedef struct cs_torrent_file_information {
    int32_t index;

    int64_t offset;
    int64_t file_size;

    time_t modified_time;

    char* file_name;
    char* file_path;

    bool file_path_is_absolute;
    uint8_t flags;
} torrent_file_information;

LTS_STRUCT typedef struct cs_torrent_meta {
    char* name;
    char* creator;
    char* comment;

    int32_t total_files;
    int64_t total_size;

    time_t creation_date;

    uint8_t info_hash_v1[20];
    uint8_t info_hash_v2[32];
} torrent_metadata;

LTS_STRUCT typedef struct cs_torrent_file_list {
    int32_t length;
    torrent_file_information* files;
} torrent_file_list;

enum cs_torrent_state : int32_t {
    torrent_state_unknown = 0,
    torrent_checking = 1,
    torrent_checking_resume = 2,
    torrent_metadata_downloading = 3,
    torrent_downloading = 4,
    torrent_seeding = 5,
    torrent_finished = 6,
    torrent_error = 7
};

LTS_STRUCT typedef struct cs_torrent_status {
    cs_torrent_state state;

    float progress;

    int32_t count_peers;
    int32_t count_seeds;

    int64_t bytes_uploaded;    // this session
    int64_t bytes_downloaded;  // this session

    int64_t upload_rate;
    int64_t download_rate;

    int64_t all_time_upload;
    int64_t all_time_download;

    int64_t active_duration_seconds;
    int64_t finished_duration_seconds;
    int64_t seeding_duration_seconds;

    int64_t eta_seconds;       // -1 = unknown (no throughput or already complete)
    float ratio;               // -1 = unknown (nothing downloaded)

    uint64_t flags;            // libtorrent torrent_flags_t bitmask

    char* save_path;           // utf-8, may be empty
    char* error_string;        // utf-8, empty when no error
} torrent_status;

// Single peer view. `ipv6_address` is always v6; v4 addresses are v4-mapped
// (::ffff:0:0/96 prefix) so the managed side can canonicalize uniformly.
LTS_STRUCT typedef struct cs_peer_information {
    uint8_t ipv6_address[16];
    uint16_t port;

    char* client;

    uint32_t flags;
    uint8_t source;

    float progress;

    int32_t up_rate;
    int32_t down_rate;

    int64_t total_uploaded;
    int64_t total_downloaded;

    int32_t connection_type;   // 0=StandardBitTorrent, 1=WebSeed, 2=HttpSeed
    int32_t num_hashfails;
    int32_t downloading_piece_index;   // -1 when not actively downloading a block
    int32_t downloading_block_index;
    int32_t downloading_progress;      // bytes downloaded of current block
    int32_t downloading_total;         // total bytes of current block
    int32_t failcount;
    int32_t payload_up_rate;
    int32_t payload_down_rate;
    uint8_t pid[20];                   // peer ID (sha1_hash)
} peer_information;

LTS_STRUCT typedef struct cs_peer_list {
    int32_t length;
    peer_information* peers;
} peer_list;

// Per-tracker aggregate across endpoints × (v1, v2) hashes. `scrape_*` are -1
// when the tracker hasn't reported yet. `next_announce_epoch` is the earliest
// across endpoints (0 when none). `last_error` is the first non-empty message.
LTS_STRUCT typedef struct cs_tracker_information {
    char* url;
    uint8_t tier;
    uint8_t source;
    bool verified;

    int32_t scrape_complete;
    int32_t scrape_incomplete;
    int32_t scrape_downloaded;

    uint8_t fails;
    bool updating;

    char* last_error;
    int64_t next_announce_epoch;

    char* trackerid;            // tracker-returned session ID, may be empty
    char* message;              // first non-empty message across endpoints
    bool start_sent;            // any endpoint sent a start event
    bool complete_sent;         // any endpoint sent a complete event
    int64_t min_announce_epoch; // earliest min_announce as unix epoch, 0=unknown
} tracker_information;

LTS_STRUCT typedef struct cs_tracker_list {
    int32_t length;
    tracker_information* trackers;
} tracker_list;

// One active port-forwarding mapping tracked on the session (populated from
// portmap_alert / portmap_error_alert). `external_port` is -1 when the mapping
// errored before establishing.
LTS_STRUCT typedef struct cs_port_mapping {
    int32_t mapping;          // libtorrent port_mapping_t handle (internal index)
    int32_t external_port;
    uint8_t protocol;         // 0 = TCP, 1 = UDP
    uint8_t transport;        // 0 = NAT-PMP, 1 = UPnP
    bool has_error;
    char* error_message;      // empty when has_error is false
} port_mapping;

LTS_STRUCT typedef struct cs_port_mapping_list {
    int32_t length;
    port_mapping* mappings;
} port_mapping_list;

// One IP-range entry in the session's filter. Start/end are stored in v4-mapped
// v6 form (::ffff:0:0/96 prefix for IPv4) so v4 and v6 rules share the same
// struct. `flags` mirrors libtorrent's ip_filter flag bitmask (0 = allowed,
// 1 = blocked).
LTS_STRUCT typedef struct cs_ip_filter_rule {
    uint8_t start_ipv6[16];
    uint8_t end_ipv6[16];
    uint32_t flags;
} ip_filter_rule;

LTS_STRUCT typedef struct cs_ip_filter_rules {
    int32_t length;
    ip_filter_rule* rules;
} ip_filter_rules;

// Flat uint8_t array of per-piece download priorities (libtorrent's
// download_priority_t: 0=skip, 1=low, 4=default, 7=top). `length` equals the
// torrent's piece count; indexing mirrors piece_index_t. Caller owns the buffer
// and must release it via lts_destroy_piece_priorities.
LTS_STRUCT typedef struct cs_piece_priority_list {
    int32_t length;
    uint8_t* priorities;
} piece_priority_list;

// Mirrors libtorrent's `lt::file_slice`: the chunk of a file a piece-offset
// range overlaps. Returned in order by `map_block`, each entry covering a
// contiguous run within a single file.
LTS_STRUCT typedef struct cs_file_slice {
    int32_t file_index;
    int64_t offset;
    int64_t size;
} file_slice;

LTS_STRUCT typedef struct cs_file_slice_list {
    int32_t length;
    file_slice* slices;
} file_slice_list;

// A single web seed URL attached to a torrent (BEP-19 / BEP-17).
LTS_STRUCT typedef struct cs_web_seed_information {
    char* url;
} web_seed_information;

LTS_STRUCT typedef struct cs_web_seed_list {
    int32_t count;
    web_seed_information* items;
} web_seed_list;

#ifdef __cplusplus
}
#endif

#endif //CSDL_STRUCTS_HPP
