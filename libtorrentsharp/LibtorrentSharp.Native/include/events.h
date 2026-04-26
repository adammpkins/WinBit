// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
//
// events.hpp - ported event structures
// Created by Albie on 04/03/2024.
//

#ifndef CS_NATIVE_EVENTS_H
#define CS_NATIVE_EVENTS_H

#ifdef _MSC_VER
#define CALL_CONV __cdecl
#else
#define CALL_CONV
#endif

#include "lts_export.h"
#include "struct_align.h"

#include <ctime>
#include <libtorrent/alert.hpp>
#include <libtorrent/error_code.hpp>
#include <libtorrent/torrent_status.hpp>
#include <libtorrent/torrent_handle.hpp>

// used internally in main library, not intended for public use
typedef void (CALL_CONV *cs_alert_callback)(void *alert);

LTS_NO_EXPORT void on_events_available(lt::session *session, cs_alert_callback callback, bool include_unmapped);

// Session-scoped port-mapping tracker updated from portmap_alert /
// portmap_error_alert in events.cpp; snapshotted by lts_get_port_mappings.
LTS_NO_EXPORT void record_portmap_success(lt::session *session, int mapping, int external_port, uint8_t protocol, uint8_t transport);
LTS_NO_EXPORT void record_portmap_error(lt::session *session, int mapping, uint8_t transport, const char *error_message);
LTS_NO_EXPORT void forget_portmap_session(lt::session *session);

#ifdef __cplusplus
extern "C" {
#endif

enum cs_alert_type : int32_t {
    alert_generic = 0,
    alert_torrent_status = 1,
    alert_client_performance = 2,
    alert_peer_notification = 3,
    alert_torrent_removed = 4,
    alert_resume_data_ready = 5,
    alert_dht_stats = 6,
    alert_dht_put = 7,
    alert_dht_immutable_item = 8,
    alert_dht_mutable_item = 9,
    alert_session_stats = 10,
    alert_listen_succeeded = 11,
    alert_listen_failed = 12,
    alert_torrent_paused = 13,
    alert_torrent_resumed = 14,
    alert_torrent_finished = 15,
    alert_torrent_checked = 16,
    alert_storage_moved = 17,
    alert_storage_moved_failed = 18,
    alert_tracker_reply = 19,
    alert_tracker_error = 20,
    alert_scrape_reply = 21,
    alert_scrape_failed = 22,
    alert_tracker_announce = 23,
    alert_tracker_warning = 24,
    alert_file_renamed = 25,
    alert_file_rename_failed = 26,
    alert_fastresume_rejected = 27,
    alert_save_resume_data_failed = 28,
    alert_torrent_deleted = 29,
    alert_torrent_delete_failed = 30,
    alert_metadata_received = 31,
    alert_metadata_failed = 32,
    alert_torrent_error = 33,
    alert_file_error = 34,
    alert_udp_error = 35,
    alert_session_error = 36,
    alert_dht_error = 37,
    alert_lsd_error = 38,
    alert_hash_failed = 39,
    alert_external_ip = 40,
    alert_portmap = 41,
    alert_portmap_error = 42,
    alert_dht_bootstrap = 43,
    alert_dht_reply = 44,
    alert_trackerid = 45,
    alert_cache_flushed = 46,
    alert_dht_announce = 47,
    alert_dht_get_peers = 48,
    alert_dht_outgoing_get_peers = 49,
    alert_add_torrent = 50,
    alert_torrent_need_cert = 51,
    alert_torrent_conflict = 52,
    alert_file_completed = 53,
    alert_piece_finished = 54,
    alert_url_seed = 55,
    alert_block_finished = 56,
    alert_block_uploaded = 57,
    alert_peer_blocked = 58,
    alert_incoming_connection = 59,
    alert_block_timeout = 60,
    alert_block_downloading = 61,
    alert_unwanted_block = 62,
    alert_socks5 = 63,
    alert_i2p = 64,
    alert_torrent_log = 65,
    alert_log = 66,
    alert_dht_log = 67
};

// base format for all alerts
struct LTS_STRUCT cs_alert {
    cs_alert_type type;

    int32_t category;
    int64_t epoch;

    const char *message;
};

struct LTS_STRUCT cs_torrent_status_alert {
    cs_alert alert;

    uint32_t old_state;
    uint32_t new_state;

    char info_hash[20];
};

struct LTS_STRUCT cs_torrent_remove_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when a torrent transitions from active to paused in response to a
// pause request. Shape mirrors cs_torrent_remove_alert — info_hash identifies
// the torrent for managers that route alerts via an attached-manager map.
struct LTS_STRUCT cs_torrent_paused_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when a torrent transitions from paused to active in response to a
// resume request. Same shape as cs_torrent_paused_alert.
struct LTS_STRUCT cs_torrent_resumed_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when a torrent reaches 100% progress. Same shape as the other
// lifecycle alerts — info_hash identifies the torrent for routing. Note:
// fires once per download completion; a subsequent re-add of already-complete
// data also fires it during the initial hash check.
struct LTS_STRUCT cs_torrent_finished_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when a torrent completes its hash check (initial check on attach or
// a force_recheck). Same shape as the other lifecycle alerts. Always fires
// once per attach after the state transitions from checking_* to either
// downloading or seeding.
struct LTS_STRUCT cs_torrent_checked_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when a storage-move request completes successfully. `storage_path`
// is the new save path, `old_path` is where the data used to live. Both
// strings are dispatcher-owned for the duration of the callback — managed
// side copies before returning.
struct LTS_STRUCT cs_storage_moved_alert {
    cs_alert alert;

    char info_hash[20];

    const char* storage_path;
    const char* old_path;
};

// Fired when a storage-move request fails. `file_path` names the file that
// couldn't be moved; `error_message` is the human-readable OS error text;
// `error_code` is libtorrent's error_code::value(). Both string fields are
// dispatcher-owned for the duration of the callback.
struct LTS_STRUCT cs_storage_moved_failed_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;

    const char* file_path;
    const char* error_message;
};

// Fired when a tracker successfully replies to an announce. `num_peers` is
// the count of peers the tracker returned; `tracker_url` is the tracker the
// reply came from. The URL is dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_tracker_reply_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t num_peers;

    const char* tracker_url;
};

// Fired when a tracker announce / scrape fails. `times_in_row` is the
// consecutive-failure count for this tracker. `tracker_url` and
// `error_message` are dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_tracker_error_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;
    int32_t times_in_row;

    const char* tracker_url;
    const char* error_message;
};

// Fired when a tracker scrape request completes successfully. `incomplete`
// is the reported leecher count; `complete` is the reported seed count;
// `tracker_url` is the tracker the reply came from. The URL is dispatcher-
// owned for the callback duration. Only fires in response to an explicit
// `scrape_tracker()` call — scrape counts embedded in announce replies
// don't trigger this alert.
struct LTS_STRUCT cs_scrape_reply_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t incomplete;
    int32_t complete;

    const char* tracker_url;
};

// Fired when a tracker scrape request fails. Distinct from tracker_error_alert,
// which covers announce failures — scrape failures go through their own alert
// type. `error_code` is libtorrent's error_code::value(); `tracker_url` and
// `error_message` are dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_scrape_failed_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;

    const char* tracker_url;
    const char* error_message;
};

// Fired when libtorrent sends an announce request to a tracker (i.e. before
// the reply arrives). `event` mirrors `lt::event_t`: 0=none, 1=completed,
// 2=started, 3=stopped, 4=paused. `tracker_url` is dispatcher-owned for the
// callback duration.
struct LTS_STRUCT cs_tracker_announce_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t event;

    const char* tracker_url;
};

// Fired when a tracker includes a human-readable warning in its reply (RFC
// BEP 3 — trackers may attach advisory messages to successful replies).
// `warning_message` and `tracker_url` are dispatcher-owned for the callback
// duration.
struct LTS_STRUCT cs_tracker_warning_alert {
    cs_alert alert;

    char info_hash[20];

    const char* tracker_url;
    const char* warning_message;
};

// Fired when a rename_file() request completes successfully. `file_index`
// identifies which file was renamed (same index the caller passed to
// rename_file); `new_name` is the resolved path (may differ from the
// requested name if libtorrent normalized it). The string is dispatcher-
// owned for the callback duration — managed side copies before returning.
struct LTS_STRUCT cs_file_renamed_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t file_index;

    const char* new_name;
};

// Fired when a rename_file() request fails. `file_index` identifies the
// file whose rename failed; `error_code` is libtorrent's error_code::value();
// `error_message` is the human-readable OS error text. The string is
// dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_file_rename_failed_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t file_index;
    int32_t error_code;

    const char* error_message;
};

// Fired when libtorrent rejects resume data passed to add_torrent — either
// malformed bencoding, an info-hash mismatch, or a deeper semantic failure
// during apply. The torrent itself may still attach using the fallback
// source (TorrentInfo / magnet); this alert just flags that the resume
// portion was discarded. `error_message` is dispatcher-owned for the
// callback duration.
struct LTS_STRUCT cs_fastresume_rejected_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;

    const char* error_message;
};

// Fired when save_resume_data() fails — typically because the handle is
// invalid (torrent removed) or the torrent has no metadata yet. The
// companion success alert is cs_resume_data_alert. `error_message` is
// dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_save_resume_data_failed_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;

    const char* error_message;
};

// Fired when all of a torrent's files have been deleted — follows a
// remove with delete_files flag. Per-torrent, not per-file. Fires from the
// disk thread after the deletion completes; the handle may or may not still
// be in the attached-manager map (libtorrent doesn't guarantee ordering
// relative to torrent_removed_alert). Managed dispatch falls back to
// surfacing info_hash directly when the handle is gone.
struct LTS_STRUCT cs_torrent_deleted_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when file deletion during remove-with-delete_files fails. Per-torrent,
// not per-file — libtorrent aggregates any per-file delete failures into one
// alert with the first error. `error_message` is dispatcher-owned for the
// callback duration.
struct LTS_STRUCT cs_torrent_delete_failed_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;

    const char* error_message;
};

// Fired when a magnet-added torrent finishes fetching its metadata (info
// dict) from the swarm. The handle may be a magnet handle (not in the
// _attachedManagers map) so the managed wrapper surfaces the v1 info_hash
// directly rather than resolving a TorrentHandle Subject.
struct LTS_STRUCT cs_metadata_received_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when received metadata fails validation (malformed info dict,
// hash mismatch, etc.). The torrent continues attempting to fetch fresh
// metadata from other peers. `error_message` is dispatcher-owned for the
// callback duration.
struct LTS_STRUCT cs_metadata_failed_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;

    const char* error_message;
};

// Fired when a torrent enters a sticky error state — typically a disk I/O
// error that libtorrent couldn't recover from. The torrent is paused and
// requires manual intervention (clear_error + resume) to restart.
// `filename` may be empty when the error isn't file-specific; both strings
// are dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_torrent_error_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;

    const char* filename;
    const char* error_message;
};

// Fired when a specific file I/O operation fails (read, write, open, etc.).
// Transient — libtorrent may retry; the sticky form of this is
// torrent_error_alert. `op` mirrors libtorrent's `operation_t` (0 =
// unknown, 1 = bittorrent, 2 = iocontrol, 3 = getpeername, ...); callers
// that need the operation name should consult libtorrent docs. Both
// strings are dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_file_error_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;
    int32_t op;

    const char* filename;
    const char* error_message;
};

// Fired when a UDP socket operation fails — DHT, UPnP discovery, UDP
// trackers, uTP. Session-level; no torrent association. `endpoint_address`
// is v4-mapped v6 (16 bytes) matching listen_succeeded_alert; `operation`
// mirrors libtorrent's `operation_t`. `error_message` is dispatcher-owned.
struct LTS_STRUCT cs_udp_error_alert {
    cs_alert alert;

    char endpoint_address[16];
    int32_t endpoint_port;
    int32_t operation;
    int32_t error_code;

    const char* error_message;
};

// Fired when a session-level operation fails catastrophically — typically
// signals the session is in an unusable state. Session-level; no torrent
// association. `error_message` is dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_session_error_alert {
    cs_alert alert;

    int32_t error_code;

    const char* error_message;
};

// Fired when a DHT-subsystem operation fails — bootstrap, lookups, puts,
// etc. Session-level (no torrent association). `operation` mirrors
// libtorrent's `operation_t` to identify which DHT op failed.
// `error_message` is dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_dht_error_alert {
    cs_alert alert;

    int32_t operation;
    int32_t error_code;

    const char* error_message;
};

// Fired when Local Service Discovery (LSD) fails on a specific local
// interface — usually indicates the multicast socket couldn't be bound
// or the LAN discovery query failed. `local_address` is v4-mapped v6
// (16 bytes) matching listen_succeeded_alert. `error_message` is
// dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_lsd_error_alert {
    cs_alert alert;

    char local_address[16];
    int32_t error_code;

    const char* error_message;
};

// Fired when a piece fails hash verification during download (torrent-level,
// indicates corrupt data received from a peer). The piece is re-downloaded
// from a different peer. `piece_index` identifies which piece failed.
struct LTS_STRUCT cs_hash_failed_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t piece_index;
};

// Fired when libtorrent learns its machine's external IP address — either
// from a tracker, a peer via BEP 10, or another source. Session-level
// (no torrent association). `external_address` is v4-mapped v6 (16 bytes)
// matching listen_succeeded_alert.
struct LTS_STRUCT cs_external_ip_alert {
    cs_alert alert;

    char external_address[16];
};

// Fired when the router adds or updates a port mapping in response to a
// NAT-PMP or UPnP request. Session-level (no torrent association).
// `mapping` is libtorrent's stable per-mapping identifier;
// `external_port` is the port the router assigned. `map_protocol`
// mirrors `lt::portmap_protocol` (0 = TCP, 1 = UDP, 0xff = unknown);
// `map_transport` mirrors `lt::portmap_transport` (0 = NAT-PMP, 1 = UPnP).
// `local_address` is the v4-mapped v6 bound interface address.
struct LTS_STRUCT cs_portmap_alert {
    cs_alert alert;

    int32_t mapping;
    int32_t external_port;
    uint8_t map_protocol;
    uint8_t map_transport;

    char local_address[16];
};

// Fired when a port-mapping request fails. Session-level. `mapping` +
// `map_transport` + `local_address` match `cs_portmap_alert`. `error_code`
// is libtorrent's error_code::value(); `error_message` is dispatcher-owned
// for the callback duration — managed side copies before returning.
struct LTS_STRUCT cs_portmap_error_alert {
    cs_alert alert;

    int32_t mapping;
    uint8_t map_transport;

    char local_address[16];

    int32_t error_code;

    const char* error_message;
};

// Fired once when the initial DHT bootstrap finishes — the node is now
// considered capable of serving lookups. Session-level (no torrent
// association); no payload beyond the alert base.
struct LTS_STRUCT cs_dht_bootstrap_alert {
    cs_alert alert;
};

// Fired when a DHT node returns peers for a torrent's info_hash lookup.
// `num_peers` is the count of peers in this specific packet — the same
// lookup typically produces multiple alerts from multiple responding
// nodes. Torrent-level (routes via info_hash through the attached-manager
// map).
struct LTS_STRUCT cs_dht_reply_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t num_peers;
};

// Fired when a tracker response includes a `trackerid` (BEP 3). libtorrent
// stores the id internally and repeats it in subsequent announces; this
// alert surfaces the exchange for observability. `tracker_url` identifies
// the tracker that issued the id; `tracker_id` is the id itself. Both
// strings are dispatcher-owned for the callback duration.
struct LTS_STRUCT cs_trackerid_alert {
    cs_alert alert;

    char info_hash[20];

    const char* tracker_url;
    const char* tracker_id;
};

// Fired when torrent_handle::flush_cache() completes, or when a removed
// torrent's outstanding disk writes finish flushing. Torrent-level (routes
// via info_hash through the attached-manager map). No payload beyond the
// subject identifier.
struct LTS_STRUCT cs_cache_flushed_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when another peer announces itself to our DHT node for an info-hash.
// Session-level (incoming DHT traffic; the info_hash may be for a torrent
// we don't own). `ip_address` is v4-mapped v6 (16 bytes) matching the
// listen_succeeded / external_ip convention.
struct LTS_STRUCT cs_dht_announce_alert {
    cs_alert alert;

    char ip_address[16];
    int32_t port;

    char info_hash[20];
};

// Fired when another peer sends a get_peers query to our DHT node. Session-
// level (incoming DHT traffic; the info_hash is what the remote peer is
// asking about, not necessarily one of our torrents).
struct LTS_STRUCT cs_dht_get_peers_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when our DHT node sends an outgoing get_peers query to another
// node. Session-level (outgoing DHT traffic — pairs with cs_dht_reply_alert
// which carries the response). `obfuscated_info_hash` is the bit-masked
// target actually sent over the wire when obfuscated lookups are enabled;
// equal to `info_hash` when obfuscation is disabled. `endpoint_address` is
// v4-mapped v6 matching the listen_succeeded / dht_announce convention.
struct LTS_STRUCT cs_dht_outgoing_get_peers_alert {
    cs_alert alert;

    char info_hash[20];
    char obfuscated_info_hash[20];

    char endpoint_address[16];
    int32_t endpoint_port;
};

// Fired when a torrent has been added to the session — fires for both
// success and failure. `info_hash` identifies the torrent (zero on
// failure when the handle is invalid); `error_code` is libtorrent's
// error_code::value() (0 on success, non-zero on failure such as
// duplicate_torrent or malformed-resume); `error_message` is dispatcher-
// owned for the callback duration. The full add_torrent_params snapshot
// is intentionally omitted — it's a heavy struct that deserves its own
// slice if a consumer ever needs the original tracker_id / userdata /
// flags / etc.
struct LTS_STRUCT cs_add_torrent_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;

    const char* error_message;
};

// Fired for SSL torrents to remind the client that the torrent won't work
// until set_ssl_certificate() has been called with a valid cert. Torrent-
// level (routes via info_hash through the attached-manager map); no
// payload beyond the subject identifier.
struct LTS_STRUCT cs_torrent_need_cert_alert {
    cs_alert alert;

    char info_hash[20];
};

// Fired when a v1+v2 hybrid torrent downloads metadata that collides with
// an already-running torrent. Both torrents enter a duplicate_torrent
// error state; callers observing this alert can resolve by removing
// both and re-adding via a clean source. `info_hash` is the torrent that
// downloaded the offending metadata (from the base torrent_alert handle);
// `conflicting_info_hash` is the torrent it collided with. The
// `shared_ptr<torrent_info>` metadata payload carried by the libtorrent
// alert is intentionally omitted here — re-adding via a fresh source is
// the canonical resolution path and keeps the C ABI free of shared-ptr
// ownership surfaces.
struct LTS_STRUCT cs_torrent_conflict_alert {
    cs_alert alert;

    char info_hash[20];
    char conflicting_info_hash[20];
};

// Fired when an individual file in a torrent finishes downloading — all
// pieces overlapping the file have passed hash verification. Torrent-level
// (routes via info_hash through the attached-manager map). `file_index`
// identifies which file. NOTE: requires `AlertCategories.FileProgress`
// (1 << 21) on the session's alert mask; not in the binding's default
// `RequiredAlertCategories` because the sibling `file_progress_alert`
// is high-rate during active downloads and would flood the unmapped
// fallback. Consumers that want this alert opt into FileProgress via
// `LibtorrentSessionConfig.AlertCategories`.
struct LTS_STRUCT cs_file_completed_alert {
    cs_alert alert;

    char info_hash[20];
    int32_t file_index;
};

// Fired when an individual piece finishes downloading and passes the hash
// check. Torrent-level (routes via info_hash through the attached-manager
// map). `piece_index` identifies which piece — useful for streaming UIs
// that want to track per-piece availability. NOTE: requires
// `AlertCategories.PieceProgress` (1 << 22) on the session's alert mask;
// not in the binding's default `RequiredAlertCategories` because the
// alert fires once per piece during active downloads (chatty for big
// torrents). Consumers opt into PieceProgress via
// `LibtorrentSessionConfig.AlertCategories`.
struct LTS_STRUCT cs_piece_finished_alert {
    cs_alert alert;

    char info_hash[20];
    int32_t piece_index;
};

// Fired when an HTTP/web seed lookup or response fails. Torrent-level
// (routes via info_hash through the attached-manager map). `error_code`
// is libtorrent's error_code::value() — zero when libtorrent itself
// didn't error and the failure was a server-sent message instead.
// `server_url` and `error_message` are dispatcher-owned for the callback
// duration. Under the Error alert category which is in the binding's
// default required mask.
struct LTS_STRUCT cs_url_seed_alert {
    cs_alert alert;

    char info_hash[20];

    int32_t error_code;

    const char* server_url;
    const char* error_message;
};

struct LTS_STRUCT cs_client_performance_alert {
    cs_alert alert;

    uint8_t warning_type;
};

// Fired when a single block (16 KiB sub-piece) finishes downloading.
// Torrent-level (routes via info_hash through the attached-manager map);
// `block_index` and `piece_index` together pinpoint the block within
// the torrent. `ipv6_address` carries the peer the block was received
// from (v4-mapped per the populate_peer_alert convention). Inherits
// peer_alert in libtorrent — under the BlockProgress alert category,
// which is NOT in the binding's default required mask (chatty: one
// alert per block, dozens per second on real swarms). Consumers opt in
// via `LibtorrentSessionConfig.AlertCategories |= BlockProgress`.
struct LTS_STRUCT cs_block_finished_alert {
    cs_alert alert;

    char info_hash[20];
    char ipv6_address[16];

    int32_t block_index;
    int32_t piece_index;
};

// Symmetric to cs_block_finished_alert but for the upload direction —
// fires when a single block has been written out to the wire to a
// specific peer. Same field shape as block_finished. Under the Upload
// alert category, which is NOT in the binding's default required mask
// (chatty for active seeds — one alert per block uploaded). Consumers
// opt in via `LibtorrentSessionConfig.AlertCategories |= Upload`.
struct LTS_STRUCT cs_block_uploaded_alert {
    cs_alert alert;

    char info_hash[20];
    char ipv6_address[16];

    int32_t block_index;
    int32_t piece_index;
};

// Fired when an incoming peer connection is filtered out before any
// payload is exchanged — IP filter, port filter, privileged-port
// restriction, uTP/TCP-disabled mismatch, etc. Torrent-level (routes
// via info_hash through the attached-manager map). `reason` mirrors
// libtorrent's `peer_blocked_alert::reason_t` enum (ip_filter=0,
// port_filter=1, i2p_mixed=2, privileged_ports=3, utp_disabled=4,
// tcp_disabled=5, invalid_local_interface=6, ssrf_mitigation=7).
// Under the IPBlock alert category, which is NOT in the binding's
// default required mask. Consumers opt in via
// `LibtorrentSessionConfig.AlertCategories |= IPBlock`.
struct LTS_STRUCT cs_peer_blocked_alert {
    cs_alert alert;

    char info_hash[20];
    char ipv6_address[16];

    int32_t reason;
};

// Fired when libtorrent's listen socket accepts an incoming peer
// connection — distinct from `peer_connect_alert` which fires per-
// torrent after the connection has been associated with a specific
// torrent. Session-level (no info_hash routing). Useful for surfacing
// inbound connection attempts in session-level diagnostics independent
// of which torrent ultimately receives them. `socket_type` mirrors
// lt::socket_type_t identically to cs_listen_succeeded_alert. Under
// the Peer alert category, which is NOT in the binding's default
// required mask. Consumers opt in via
// `LibtorrentSessionConfig.AlertCategories |= Peer`.
struct LTS_STRUCT cs_incoming_connection_alert {
    cs_alert alert;

    char endpoint_address[16];
    int32_t endpoint_port;
    uint8_t socket_type;
};

// Fired when a request for a single block (16 KiB sub-piece) times out
// without the remote peer delivering it. Same field shape as
// cs_block_finished_alert / cs_block_uploaded_alert — torrent-level
// (routes via info_hash through the attached-manager map), `block_index`
// + `piece_index` pinpoint the request, `ipv6_address` carries the peer
// the request was sent to (v4-mapped per the populate_peer_alert
// convention). Useful for "stalled download" diagnostics — repeated
// block_timeout from the same peer typically means that peer is dead
// or congested and libtorrent will eventually drop them. Inherits
// peer_alert in libtorrent — under the BlockProgress alert category,
// which is NOT in the binding's default required mask. Consumers opt in
// via `LibtorrentSessionConfig.AlertCategories |= BlockProgress`.
struct LTS_STRUCT cs_block_timeout_alert {
    cs_alert alert;

    char info_hash[20];
    char ipv6_address[16];

    int32_t block_index;
    int32_t piece_index;
};

// Fired when a request for a single block (16 KiB sub-piece) starts
// downloading from a peer — the request was sent and the peer began
// sending bytes. Same field shape as cs_block_finished_alert /
// cs_block_uploaded_alert / cs_block_timeout_alert — torrent-level
// (routes via info_hash through the attached-manager map), `block_index`
// + `piece_index` pinpoint the request, `ipv6_address` carries the peer
// the bytes are coming from (v4-mapped per the populate_peer_alert
// convention). Pairs with block_finished/timeout to give consumers an
// "in-flight blocks per peer" view. Inherits peer_alert in libtorrent —
// under the BlockProgress alert category, which is NOT in the binding's
// default required mask. Consumers opt in via
// `LibtorrentSessionConfig.AlertCategories |= BlockProgress`.
struct LTS_STRUCT cs_block_downloading_alert {
    cs_alert alert;

    char info_hash[20];
    char ipv6_address[16];

    int32_t block_index;
    int32_t piece_index;
};

// Fired when a peer sent us a block we never requested — typically a
// duplicate (a different peer already delivered the same block) or the
// sign of a buggy / hostile peer. Same field shape as the rest of the
// cs_block_*_alert family. Useful for "noisy peer" detection in a
// Peers tab. Inherits peer_alert in libtorrent — under the BlockProgress
// alert category, which is NOT in the binding's default required mask.
// Consumers opt in via `LibtorrentSessionConfig.AlertCategories |=
// BlockProgress`.
struct LTS_STRUCT cs_unwanted_block_alert {
    cs_alert alert;

    char info_hash[20];
    char ipv6_address[16];

    int32_t block_index;
    int32_t piece_index;
};

// Fired when a SOCKS5 proxy operation fails — handshake rejection,
// authentication failure, network unreachable through the proxy, etc.
// Session-level; no torrent association (the proxy itself is the
// failing resource, not a specific torrent). `endpoint_address` is the
// SOCKS5 proxy endpoint we tried to talk to (v4-mapped v6 matching
// listen_succeeded_alert). `operation` mirrors libtorrent's
// `operation_t` to identify which SOCKS5 op failed (typically
// connect or socks5_handshake). `error_message` is dispatcher-owned
// for the callback duration. Useful for proxy-using consumers
// diagnosing why outbound connections aren't getting through.
struct LTS_STRUCT cs_socks5_alert {
    cs_alert alert;

    char endpoint_address[16];
    int32_t endpoint_port;
    int32_t operation;
    int32_t error_code;

    const char* error_message;
};

// Fired when an I2P-router operation fails — typically the SAM bridge
// rejecting a session, the I2P daemon being unreachable, or destination
// resolution failing. Session-level (no torrent association — the I2P
// transport itself is the failing resource). Smaller field set than
// cs_socks5_alert because libtorrent's i2p_alert exposes only the
// error_code (no endpoint or operation_t — I2P destinations are opaque
// hashes, not IP endpoints, and the failing operation is implicit in
// the error code itself). `error_message` is dispatcher-owned for the
// callback duration.
struct LTS_STRUCT cs_i2p_alert {
    cs_alert alert;

    int32_t error_code;

    const char* error_message;
};

// Fired for libtorrent's verbose torrent-scoped log messages — things
// like "tried peer X, got error Y" / "piece N hash failed, redownload
// from peer Z". Torrent-level (routes via info_hash through the
// attached-manager map). `log_message` is dispatcher-owned for the
// callback duration. Useful for diagnosing why a specific torrent is
// struggling (stalled, failing hashes, dropping peers) — surfaces the
// same strings the libtorrent log would emit, at the torrent level.
// Under the TorrentLog alert category, which is NOT in the binding's
// default required mask (high-volume, debug-tier). Consumers opt in
// via `LibtorrentSessionConfig.AlertCategories |= TorrentLog`.
struct LTS_STRUCT cs_torrent_log_alert {
    cs_alert alert;

    char info_hash[20];

    const char* log_message;
};

// Fired for libtorrent's session-level verbose log lines — things like
// "DHT bootstrap complete" / "uTP socket warning: ..." / "starting
// session listen on port X". Session-scoped (no info_hash, no peer
// endpoint — these messages aren't tied to a specific torrent or
// connection). Sibling to cs_torrent_log_alert but a smaller shape.
// `log_message` is dispatcher-owned for the callback duration. Useful
// for diagnosing session-wide issues (DHT bootstrap failures, listen
// socket problems, session-config warnings) when the structured
// session-level alerts don't carry enough context. Under the SessionLog
// alert category, which is NOT in the binding's default required mask
// (high-volume, debug-tier). Consumers opt in via
// `LibtorrentSessionConfig.AlertCategories |= SessionLog`.
struct LTS_STRUCT cs_log_alert {
    cs_alert alert;

    const char* log_message;
};

// Fired for libtorrent's DHT-subsystem verbose log lines — things like
// "node X added to bucket Y" / "RPC timeout from peer Z" / "traversal
// X completed in N hops". Session-scoped (DHT is a session-wide
// service, not per-torrent). `module` mirrors libtorrent's
// `dht_log_alert::dht_module_t` (tracker=0, node=1, routing_table=2,
// rpc_manager=3, traversal=4) so consumers can filter by which DHT
// subsystem emitted the line. `log_message` is dispatcher-owned for
// the callback duration. Useful for diagnosing DHT-specific problems
// (slow lookups, bucket churn, RPC misbehavior). Under the DHTLog
// alert category, which is NOT in the binding's default required mask
// (high-volume, debug-tier). Consumers opt in via
// `LibtorrentSessionConfig.AlertCategories |= DHTLog`.
struct LTS_STRUCT cs_dht_log_alert {
    cs_alert alert;

    int32_t module;

    const char* log_message;
};

enum cs_peer_alert_type : uint8_t {
    connected_in = 0,
    connected_out = 1,
    disconnected = 2,
    banned = 3,
    snubbed = 4,
    unsnubbed = 5,
    errored = 6
};

struct LTS_STRUCT cs_peer_alert {
    cs_alert alert;

    lt::torrent_handle *handle;
    cs_peer_alert_type type;

    char info_hash[20];
    char ipv6_address[16];
    char peer_id[20];
};

// Fired when a torrent's save_resume_data request completes successfully.
// `data` points to a bencoded buffer owned by the native event dispatcher;
// the managed side MUST copy it before the callback returns.
struct LTS_STRUCT cs_resume_data_alert {
    cs_alert alert;

    char info_hash[20];

    const char* data;
    int32_t length;
};

// One bucket of the DHT routing table. The `buckets` array on cs_dht_stats_alert
// contains `bucket_count` of these in declaration order (closest first).
struct LTS_STRUCT cs_dht_routing_bucket {
    int32_t num_nodes;            // currently-live nodes in the bucket
    int32_t num_replacements;     // pending replacement candidates
    int32_t last_active;          // seconds since last activity
};

// One outstanding DHT lookup. The `lookups` array on cs_dht_stats_alert
// contains `lookup_count` of these. `type` is a libtorrent string literal
// ("get_peers" / "announce" / "put" / "get") — static lifetime, no copy
// required by the dispatcher.
struct LTS_STRUCT cs_dht_lookup {
    int32_t outstanding_requests;
    int32_t timeouts;
    int32_t responses;
    int32_t branch_factor;
    int32_t nodes_left;
    int32_t last_sent;
    int32_t first_timeout;
    char target[20];              // sha1; node-id / info-hash being looked up
    const char* type;             // null-terminated; static-lifetime literal
};

// DHT routing-table summary, fired in response to lts_post_dht_stats.
// `buckets` and `lookups` are owned by the dispatcher and remain valid only
// for the duration of the alert callback — the managed side MUST copy before
// returning. active_requests still mirrors lookup_count for source-compat.
struct LTS_STRUCT cs_dht_stats_alert {
    cs_alert alert;

    int32_t total_nodes;          // sum across routing_table buckets
    int32_t total_replacements;   // sum of bucket replacement-cache entries
    int32_t active_requests;      // outstanding lookups (== lookup_count)

    int32_t bucket_count;
    cs_dht_routing_bucket* buckets;

    int32_t lookup_count;
    cs_dht_lookup* lookups;
};

// Fired when a DHT put operation completes. For an immutable item only
// `target` + `num_success` are populated; the mutable-side fields are zeroed.
// For a mutable item all fields are populated and `target` is
// SHA1(public_key + salt). `salt` is dispatcher-owned for the duration of the
// callback — managed side must copy before returning.
struct LTS_STRUCT cs_dht_put_alert {
    cs_alert alert;

    char target[20];          // sha1; immutable target OR SHA1(pk + salt)
    int32_t num_success;      // peers the put landed on; may be 0

    char public_key[32];      // ed25519 pk (mutable only; zero for immutable)
    char signature[64];       // ed25519 sig (mutable only; zero for immutable)
    int64_t seq;              // sequence number (mutable only; 0 for immutable)
    const char* salt;         // mutable salt bytes (empty for immutable)
    int32_t salt_len;
};

// Fired when a DHT lookup for a mutable BEP44 item completes successfully.
// `data` and `salt` are dispatcher-owned for the duration of the callback;
// managed side must copy before returning. `data` carries the bytes for
// string-typed entries (the common case); empty for other entry types until
// a full entry marshaller lands.
struct LTS_STRUCT cs_dht_mutable_item_alert {
    cs_alert alert;

    char public_key[32];
    char signature[64];
    int64_t seq;

    const char* salt;
    int32_t salt_len;

    const char* data;
    int32_t data_len;

    int8_t authoritative;     // bool; 1 if the response was authoritative
};

// Fired when a DHT lookup for an immutable BEP44 item completes successfully.
// `data` points to a string-typed entry's bytes (string_t case only) and is
// owned by the dispatcher; the managed side MUST copy before returning. For
// non-string entries (rare in practice) `length` is 0.
struct LTS_STRUCT cs_dht_immutable_item_alert {
    cs_alert alert;

    char target[20];

    const char* data;
    int32_t length;
};

// Fired in response to lts_post_session_stats. `counters` is dispatcher-owned
// for the duration of the callback — the managed side MUST copy before
// returning. The mapping from metric name to index into this array is queried
// separately via the session_stats_metrics surface (a follow-up slice); the
// index layout is stable for a given libtorrent build.
struct LTS_STRUCT cs_session_stats_alert {
    cs_alert alert;

    int32_t counters_count;
    const int64_t* counters;
};

// Fired when libtorrent successfully opens a listen socket. `address` is the
// bound local IP in v4-mapped v6 form (16 bytes); `port` is the bound port;
// `socket_type` mirrors lt::socket_type_t (TCP=0, SOCKS5=1, HTTP=2, UTP=3,
// I2P=4, TCP_SSL=5, SOCKS5_SSL=6, HTTP_SSL=7, UTP_SSL=8).
struct LTS_STRUCT cs_listen_succeeded_alert {
    cs_alert alert;

    char address[16];
    int32_t port;
    uint8_t socket_type;
};

// Fired when libtorrent fails to open a listen socket. `listen_interface` and
// `error_message` are dispatcher-owned for the duration of the callback —
// the managed side MUST copy before returning. `socket_type` mirrors
// lt::socket_type_t identically to cs_listen_succeeded_alert. `op` mirrors
// lt::operation_t — identifies which step of the bind sequence failed
// (sock_open / sock_bind / sock_listen / etc.); useful for diagnosing the
// failure beyond what the error message conveys.
struct LTS_STRUCT cs_listen_failed_alert {
    cs_alert alert;

    char address[16];
    int32_t port;
    uint8_t socket_type;
    uint8_t op;
    int32_t error_code;

    const char* listen_interface;
    const char* error_message;
};

#ifdef __cplusplus
}
#endif
#endif //CS_NATIVE_EVENTS_H
