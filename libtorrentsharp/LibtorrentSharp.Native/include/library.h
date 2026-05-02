// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
//
// library.hpp
// Created by Albie on 29/02/2024.
//

#ifndef CSDL_LIBRARY_HPP
#define CSDL_LIBRARY_HPP

#include "events.h"
#include "structs.h"
#include "lts_export.h"

#include <libtorrent/torrent_handle.hpp>

#ifdef __cplusplus
extern "C" {
#endif

    // session control
    LTS_EXPORT lt::session* create_session(lt::settings_pack* pack);
    LTS_EXPORT void destroy_session(lt::session* session);

    LTS_EXPORT void set_event_callback(lt::session* session, cs_alert_callback callback, bool include_unmapped_events);
    LTS_EXPORT void clear_event_callback(lt::session* session);

    LTS_EXPORT void apply_settings(lt::session* session, lt::settings_pack* settings);

    // torrent control
    LTS_EXPORT lt::torrent_info* create_torrent_file(const char* file_path);
    LTS_EXPORT lt::torrent_info* create_torrent_bytes(const char* data, long length);
    LTS_EXPORT void destroy_torrent(lt::torrent_info* torrent);

    LTS_EXPORT lt::torrent_handle* attach_torrent(lt::session* session, lt::torrent_info* torrent, const char* save_path);
    // Detach a torrent from the session. `remove_flags` mirrors libtorrent's
    // `remove_flags_t`:
    //   0 = no delete (default)
    //   1 = delete_files
    //   2 = delete_partfile
    //   3 = both
    // After returning, the torrent handle is no longer valid and must not be
    // passed to any other lts_ call. delete_files triggers file_deleted_alert
    // or file_delete_failed_alert, then torrent_removed_alert.
    LTS_EXPORT void detach_torrent(lt::session* session, lt::torrent_handle* torrent, int32_t remove_flags);

    // Parses a magnet URI and attaches the resulting torrent to the session. Metadata
    // arrives asynchronously via alerts; the handle is usable for status/control as soon
    // as this call returns. Returns nullptr on parse failure or invalid handle.
    LTS_EXPORT lt::torrent_handle* lts_add_magnet(lt::session* session, const char* magnet_uri, const char* save_path);

    // Requests resume data for the torrent. Completes asynchronously — the resume blob
    // arrives via cs_resume_data_alert on the event callback.
    LTS_EXPORT void lts_request_resume_data(lt::torrent_handle* torrent);

    // Discards existing piece-state for the torrent and re-hashes the on-disk files
    // from scratch. Returns without effect if `torrent` is null or invalid.
    LTS_EXPORT void lts_force_recheck(lt::torrent_handle* torrent);

    // Queue-aware pause: sets paused + enables auto_managed so libtorrent can
    // resume the torrent when queue slots free up. Distinct from stop_torrent,
    // which is a manual force-pause that bypasses the queue.
    LTS_EXPORT void lts_pause_torrent(lt::torrent_handle* torrent);

    // Queue-aware resume: clears paused + enables auto_managed so libtorrent's
    // queue governs running/pausing. Distinct from start_torrent, which forces
    // an immediate unpause ignoring queue limits.
    LTS_EXPORT void lts_resume_torrent(lt::torrent_handle* torrent);

    // Force-start: bypasses the download queue limit. When force_start=true,
    // clears auto_managed (so the queue cannot re-pause it) and resumes the
    // torrent. When force_start=false, re-enables auto_managed so the queue
    // resumes normal governance.
    LTS_EXPORT void lts_force_start_torrent(lt::torrent_handle* torrent, bool force_start);

    // Relocates the torrent's data to `new_path`. Completes asynchronously;
    // success/failure surface via storage_moved_alert / storage_moved_failed_alert
    // (alert plumbing pending). `flags` maps to libtorrent's move_flags_t:
    //   0 = always_replace_files (default)
    //   1 = fail_if_exist
    //   2 = dont_replace
    //   3 = reset_save_path (deprecated; updates save path without moving data)
    LTS_EXPORT void lts_move_storage(lt::torrent_handle* torrent, const char* new_path, int32_t flags);

    // Snapshot of the torrent's connected peers. Populates `out_list` with an
    // array the caller owns; release it via lts_destroy_peers.
    LTS_EXPORT void lts_get_peers(lt::torrent_handle* torrent, peer_list* out_list);
    LTS_EXPORT void lts_destroy_peers(peer_list* list);

    // Snapshot of the torrent's trackers. Per-tracker aggregate across endpoints
    // and v1/v2 info hashes. Release via lts_destroy_trackers.
    LTS_EXPORT void lts_get_trackers(lt::torrent_handle* torrent, tracker_list* out_list);
    LTS_EXPORT void lts_destroy_trackers(tracker_list* list);

    // Tracker mutation: add, remove, or edit a tracker URL on a running torrent.
    // Changes take effect immediately on the in-memory announce list; they are
    // not persisted to the .torrent file.
    LTS_EXPORT void lts_add_tracker(lt::torrent_handle* torrent, const char* url, int32_t tier);
    LTS_EXPORT void lts_remove_tracker(lt::torrent_handle* torrent, const char* url);
    LTS_EXPORT void lts_edit_tracker(lt::torrent_handle* torrent, const char* old_url, const char* new_url, int32_t new_tier);

    // Web seed (BEP-19 / BEP-17) snapshot and mutation. Changes apply immediately
    // to the in-memory URL set; they are not persisted to the .torrent file.
    LTS_EXPORT void lts_get_web_seeds(lt::torrent_handle* torrent, web_seed_list* out_list);
    LTS_EXPORT void lts_destroy_web_seeds(web_seed_list* list);
    LTS_EXPORT void lts_add_web_seed(lt::torrent_handle* torrent, const char* url);
    LTS_EXPORT void lts_remove_web_seed(lt::torrent_handle* torrent, const char* url);

    // Serialises the torrent's current metadata to a bencoded .torrent byte
    // buffer using libtorrent's create_torrent / bencode surface. Returns true
    // and fills *out_data / *out_size on success; returns false when the
    // torrent has no metadata yet (pre-metadata magnet). The caller is
    // responsible for releasing the buffer via lts_free_bytes.
    LTS_EXPORT bool lts_export_torrent_to_bytes(lt::torrent_handle* torrent, uint8_t** out_data, int32_t* out_size);

    // Releases a byte buffer previously returned by lts_export_torrent_to_bytes.
    // No-op on nullptr.
    LTS_EXPORT void lts_free_bytes(uint8_t* data);

    // Per-torrent rate caps in bytes/sec. libtorrent treats <= 0 as unlimited; the
    // getter may return 0 or -1 for the unlimited state depending on internal bookkeeping.
    LTS_EXPORT void lts_set_torrent_upload_limit(lt::torrent_handle* torrent, int32_t bytes_per_second);
    LTS_EXPORT int32_t lts_get_torrent_upload_limit(lt::torrent_handle* torrent);
    LTS_EXPORT void lts_set_torrent_download_limit(lt::torrent_handle* torrent, int32_t bytes_per_second);
    LTS_EXPORT int32_t lts_get_torrent_download_limit(lt::torrent_handle* torrent);

    // Toggle libtorrent's super-seeding mode for the torrent. Only has an effect
    // once the torrent is in the seeding state.
    LTS_EXPORT void lts_set_super_seeding(lt::torrent_handle* torrent, bool enabled);

    // Generic torrent_flags_t accessors. `flags` and `mask` carry libtorrent's
    // torrent_flags::* bits (see include/libtorrent/torrent_flags.hpp). `set` only
    // rewrites bits where `mask` is 1 — leaves the rest untouched. `unset` clears
    // every bit set in `flags`. `get` returns the current bitset; 0 when invalid.
    LTS_EXPORT uint64_t lts_get_torrent_flags(lt::torrent_handle* torrent);
    LTS_EXPORT void lts_set_torrent_flags(lt::torrent_handle* torrent, uint64_t flags, uint64_t mask);
    LTS_EXPORT void lts_unset_torrent_flags(lt::torrent_handle* torrent, uint64_t flags);

    // Per-piece download priority surface. `priority` mirrors libtorrent's
    // download_priority_t (0=skip, 1=low, 4=default, 7=top). Indices are
    // piece_index_t. Invalid handles or out-of-range indices no-op on set and
    // return 0 on get.
    LTS_EXPORT uint8_t lts_get_piece_priority(lt::torrent_handle* torrent, int32_t piece_index);
    LTS_EXPORT void lts_set_piece_priority(lt::torrent_handle* torrent, int32_t piece_index, uint8_t priority);

    // Bulk accessors. `get` returns the torrent's full piece_priorities vector;
    // release via lts_destroy_piece_priorities. `set` replaces the vector
    // wholesale — count exceeding the torrent's piece count is silently truncated.
    LTS_EXPORT void lts_get_piece_priorities(lt::torrent_handle* torrent, piece_priority_list* out_list);
    LTS_EXPORT void lts_destroy_piece_priorities(piece_priority_list* list);
    LTS_EXPORT void lts_set_piece_priorities(lt::torrent_handle* torrent, const uint8_t* priorities, int32_t count);

    // Whether the torrent has the given piece fully downloaded + verified on disk.
    // Returns false on invalid handle or out-of-range index.
    LTS_EXPORT bool lts_have_piece(lt::torrent_handle* torrent, int32_t piece_index);

    // Fills out_bits with a packed LSB-first bitfield of piece completion.
    // Byte k, bit j represents piece (k*8 + j). Caller must allocate
    // ceil(num_pieces/8) bytes; num_bytes must equal that allocation. No-op on
    // null or invalid inputs.
    LTS_EXPORT void lts_get_piece_bitfield(lt::torrent_handle* torrent, uint8_t* out_bits, int32_t num_bytes);

    // Explicitly queues a peer to try on the torrent. `ipv6_address` is always
    // v6; v4 addresses must be passed in v4-mapped form (::ffff:0:0/96 prefix)
    // to match the peer_information layout. Connect is asynchronous; libtorrent
    // surfaces the result via the peer-connect alerts. Returns true when the
    // connect was queued, false on invalid handle or port==0.
    LTS_EXPORT bool lts_connect_peer(lt::torrent_handle* torrent, const uint8_t ipv6_address[16], uint16_t port);

    // Clears any sticky error state on the torrent — e.g. after an out-of-disk
    // hash-check failure. Lets the torrent retry once the underlying problem
    // has been resolved. No-op on invalid handle.
    LTS_EXPORT void lts_clear_error(lt::torrent_handle* torrent);

    // Renames file at `file_index` to `new_name` (UTF-8). `new_name` may be a
    // relative path inside the torrent but must not be absolute or empty. The
    // rename is asynchronous — libtorrent surfaces the outcome via
    // file_renamed_alert / file_rename_failed_alert. No-op on invalid handle,
    // out-of-range index, pre-metadata, or null/empty name.
    LTS_EXPORT void lts_rename_file(lt::torrent_handle* torrent, int32_t file_index, const char* new_name);

    // Snapshot of port-forwarding mappings the session has registered (UPnP +
    // NAT-PMP). Populated from alerts — libtorrent 2.x has no direct
    // enumeration API. Release via lts_destroy_port_mappings.
    LTS_EXPORT void lts_get_port_mappings(lt::session* session, port_mapping_list* out_list);
    LTS_EXPORT void lts_destroy_port_mappings(port_mapping_list* list);

    // Replaces the session's IP filter with the supplied rules. Pass length=0
    // (rules may be null) to clear the filter. Start/end IPs use the same
    // v4-mapped v6 representation as peer_information; flags carry libtorrent's
    // ip_filter bitmask (0 = allowed, 1 = blocked).
    LTS_EXPORT void lts_set_ip_filter(lt::session* session, const ip_filter_rule* rules, int32_t length);

    // Exports the session's current IP filter as a flat rules list (v4 ranges
    // are v4-mapped into the 16-byte v6 buffer). Release via lts_destroy_ip_filter.
    LTS_EXPORT void lts_get_ip_filter(lt::session* session, ip_filter_rules* out_list);
    LTS_EXPORT void lts_destroy_ip_filter(ip_filter_rules* list);

    // Triggers an asynchronous dht_stats_alert. The alert surfaces the routing-
    // table totals (nodes, replacements, active requests) as a cs_dht_stats_alert.
    LTS_EXPORT void lts_post_dht_stats(lt::session* session);

    // Triggers an asynchronous session_stats_alert. The alert surfaces a flat
    // int64 counter array (~140 metrics in current libtorrent). The mapping
    // from metric name → array index is queried separately via the
    // session_stats_metrics surface below; the index layout is stable for a
    // given libtorrent build.
    LTS_EXPORT void lts_post_session_stats(lt::session* session);

    // Static metric metadata surface. Returned values reflect libtorrent's
    // built-in registry (lt::session_stats_metrics()) which is independent of
    // any session — the binding caches the vector on first call. Names are
    // libtorrent-owned static literals; the caller does NOT free them.
    //
    // The returned `value_index` is the position in the
    // session_stats_alert::counters() array — i.e. the index consumers should
    // use against `cs_session_stats_alert::counters` from slice 1.
    // `type` is libtorrent's metric_type_t: 0 = counter, 1 = gauge.
    LTS_EXPORT int32_t lts_session_stats_metric_count();

    LTS_EXPORT void lts_session_stats_metric_at(
        int32_t idx,
        const char** out_name,
        int32_t* out_value_index,
        int32_t* out_type);

    // Resolves a metric name to its value_index (the position in the
    // session_stats_alert::counters() array). Returns -1 if no metric matches.
    // Forwards to lt::find_metric_idx, which uses an O(log N) lookup.
    LTS_EXPORT int32_t lts_session_stats_find_metric_idx(const char* name);

    // Captures the session's full state as a bencoded byte buffer suitable for
    // restoring via lts_create_session_from_state. Wraps libtorrent's modern
    // surface (`session_handle::session_state()` + `write_session_params_buf`)
    // — the deprecated save_state(entry&) / load_state(bdecode_node&) pair is
    // not exposed.
    //
    // On success, *out_buf is a heap-allocated buffer the caller owns; release
    // it with lts_destroy_session_state_buf. On failure (null session, OOM,
    // libtorrent throws), *out_buf is set to nullptr and *out_len to 0.
    LTS_EXPORT void lts_session_save_state(lt::session* session, char** out_buf, int32_t* out_len);

    LTS_EXPORT void lts_destroy_session_state_buf(char* buf);

    // Constructs a new session from a previously-captured state buffer.
    // Returns nullptr on parse failure (malformed bencoding, invalid keys,
    // etc.). The returned session is owned by the caller — destroy via
    // destroy_session as usual. Note that the saved alert_mask is preserved;
    // managed callers force-apply their required categories via UpdateSettings
    // after construction.
    LTS_EXPORT lt::session* lts_create_session_from_state(const char* buf, int32_t length);

    // Returns the TCP port the session is actually listening on. May differ
    // from the requested port when the configured `listen_interfaces` asked
    // for port 0 (OS-assigned) or when the requested port was already in use
    // and libtorrent fell back. Returns 0 when the session has no listen
    // sockets open (e.g. libtorrent failed to bind).
    LTS_EXPORT uint16_t lts_session_listen_port(lt::session* session);

    // Returns the SSL/TLS port when SSL is enabled; 0 otherwise.
    LTS_EXPORT uint16_t lts_session_ssl_listen_port(lt::session* session);

    // Returns 1 when the session has at least one open listen socket, 0
    // otherwise. Pair with the typed listen_succeeded / listen_failed alerts
    // for event-driven state tracking.
    LTS_EXPORT int8_t lts_session_is_listening(lt::session* session);

    // Reads back the session's currently-effective `listen_interfaces` setting
    // string. On success, *out_buf is a heap-allocated buffer (caller releases
    // via lts_destroy_listen_interfaces_buf); on empty / failure, both outputs
    // are zeroed.
    LTS_EXPORT void lts_session_get_listen_interfaces(lt::session* session, char** out_buf, int32_t* out_len);

    LTS_EXPORT void lts_destroy_listen_interfaces_buf(char* buf);

    // Stores an immutable BEP44 item (a binary string blob, treated as
    // libtorrent's entry::string_t) on the DHT and writes the SHA-1 target the
    // data will be retrievable under to out_target_sha1 (20 bytes). The actual
    // network put is asynchronous; completion fires a dht_put_alert which is
    // not yet exposed as a typed alert (subscribe with include_unmapped=true to
    // receive it as a generic alert until the typed wrapper lands).
    LTS_EXPORT void lts_dht_put_immutable_string(
        lt::session* session,
        const uint8_t* data,
        int32_t data_len,
        uint8_t* out_target_sha1);

    // Issues an asynchronous DHT lookup for an immutable BEP44 item identified
    // by target_sha1 (20 bytes). The result arrives as a
    // dht_immutable_item_alert carrying the bytes. Misses (no peer holds the
    // item) currently fire no alert — they time out internally.
    LTS_EXPORT void lts_dht_get_immutable(
        lt::session* session,
        const uint8_t* target_sha1);

    // Issues an asynchronous DHT lookup for a mutable BEP44 item identified by
    // an Ed25519 public key (`key32`, 32 bytes) and an optional salt. The
    // result arrives as a dht_mutable_item_alert. Pass salt=nullptr / salt_len=0
    // when no salt was used. Misses time out internally without firing.
    LTS_EXPORT void lts_dht_get_item_mutable(
        lt::session* session,
        const uint8_t* key32,
        const uint8_t* salt,
        int32_t salt_len);

    // Stores a mutable BEP44 item on the DHT. The signature is computed inside
    // the shim via lt::dht::sign_mutable_item; the caller only provides the
    // keypair, salt, raw value bytes (treated as entry::string_t), and sequence
    // number. The actual put is asynchronous; completion fires a dht_put_alert
    // (typed as DhtPutAlert with the mutable envelope filled in).
    LTS_EXPORT void lts_dht_put_item_mutable(
        lt::session* session,
        const uint8_t* public_key32,
        const uint8_t* secret_key64,
        const uint8_t* salt,
        int32_t salt_len,
        const uint8_t* value,
        int32_t value_len,
        int64_t seq);

    // Ed25519 helpers backing BEP44 mutable items. All key/seed/signature
    // buffers are caller-owned; sizes are fixed (seed=32, public=32, secret=64,
    // signature=64). The implementations forward to libtorrent's bundled
    // ed25519 reference (lt::dht::ed25519_*). These don't touch the session;
    // they live here so the binding ships a complete BEP44 toolkit.
    LTS_EXPORT void lts_ed25519_create_seed(uint8_t* out_seed32);

    LTS_EXPORT void lts_ed25519_create_keypair(
        const uint8_t* seed32,
        uint8_t* out_public_key32,
        uint8_t* out_secret_key64);

    LTS_EXPORT void lts_ed25519_sign(
        const uint8_t* message,
        int32_t message_len,
        const uint8_t* public_key32,
        const uint8_t* secret_key64,
        uint8_t* out_signature64);

    // Returns 1 if the signature verifies, 0 otherwise (or on null inputs).
    LTS_EXPORT int8_t lts_ed25519_verify(
        const uint8_t* signature64,
        const uint8_t* message,
        int32_t message_len,
        const uint8_t* public_key32);

    // Returns the total piece count for a torrent handle (works for both fully-
    // added TorrentHandle and resume-loaded MagnetHandle with embedded metadata).
    // Returns 0 when metadata hasn't resolved yet (pre-metadata magnet).
    LTS_EXPORT int32_t lts_num_pieces(lt::torrent_handle* torrent);

    // Total size of all files in the torrent in bytes. Works for TorrentHandle and
    // resume-loaded MagnetHandle with embedded metadata. Returns 0 when metadata
    // hasn't resolved yet (pre-metadata magnet).
    LTS_EXPORT int64_t lts_total_size(lt::torrent_handle* torrent);

    // Gets the file list for a torrent handle directly. Works for TorrentHandle and
    // resume-loaded MagnetHandle with embedded metadata. Caller must free with
    // destroy_torrent_file_list.
    LTS_EXPORT void lts_torrent_handle_file_list(lt::torrent_handle* torrent, torrent_file_list* file_list);

    // Toggle sequential download mode. When enabled, libtorrent requests pieces
    // in order rather than rarest-first — useful for streaming / progressive UX.
    LTS_EXPORT void lts_set_sequential(lt::torrent_handle* torrent, bool enabled);

    // Sets the priority of a file's first and last piece. Resolves the piece
    // indices from the torrent's file_storage and writes both with piece_priority.
    // No-op when metadata hasn't resolved yet (magnet pre-metadata case).
    // `priority` maps to libtorrent's download_priority_t (0 = skip, 1 = low,
    // 4 = default, 7 = top).
    LTS_EXPORT void lts_set_file_piece_priority(lt::torrent_handle* torrent, int32_t file_index, uint8_t priority);

    // Attaches a torrent using a previously-captured resume blob. `resume_data` must
    // point to a bencoded add_torrent_params buffer (as produced by
    // write_resume_data_buf). `save_path` overrides the path embedded in the resume
    // blob when non-null and non-empty. Returns nullptr on parse or add failure.
    LTS_EXPORT lt::torrent_handle* lts_add_torrent_with_resume(lt::session* session, const char* resume_data, int32_t length, const char* save_path);

    // torrent info
    LTS_EXPORT torrent_metadata* get_torrent_info(lt::torrent_info* torrent);
    LTS_EXPORT void destroy_torrent_info(torrent_metadata* info);

    // file listing
    LTS_EXPORT void get_torrent_file_list(const lt::torrent_info* torrent, torrent_file_list* file_list);
    LTS_EXPORT void destroy_torrent_file_list(torrent_file_list* file_list);

    // file_storage scalar accessors. All return 0 on null / invalid inputs.
    // piece_length / num_pieces are torrent-wide constants; piece_size varies
    // for the last piece (trailing remainder when total_size is not a multiple
    // of piece_length). Exposed separately from torrent_metadata so callers
    // don't have to heap-allocate + free the bigger metadata struct for a
    // single integer.
    LTS_EXPORT int32_t lts_torrent_info_piece_length(lt::torrent_info* torrent);
    LTS_EXPORT int32_t lts_torrent_info_num_pieces(lt::torrent_info* torrent);
    LTS_EXPORT int32_t lts_torrent_info_piece_size(lt::torrent_info* torrent, int32_t piece_index);

    // V1 SHA-1 piece hash lookup. Fills the 20-byte buffer with the V1 piece
    // hash for `piece_index` and returns true. Returns false (and leaves the
    // buffer untouched) on null / invalid handle, out-of-range index, or
    // V2-only torrent (no V1 piece hashes — info_hashes::has_v1() is false).
    // The leaves of the V1 piece tree are the analogue of V2 merkle leaves.
    LTS_EXPORT bool lts_torrent_info_hash_for_piece(lt::torrent_info* torrent, int32_t piece_index, uint8_t* out_hash20);

    // True when the torrent carries BEP-52 v2 metadata (SHA-256 merkle trees).
    // A torrent can be v1-only, v2-only, or hybrid — this returns true for v2
    // and hybrid. Returns false on null / invalid.
    LTS_EXPORT bool lts_torrent_info_is_v2(lt::torrent_info* torrent);

    // Per-file flag bits (file_storage::file_flags_t). Bit assignment:
    //   bit 0 = pad_file, bit 1 = hidden, bit 2 = executable, bit 3 = symlink.
    // Returns 0 on null / invalid / out-of-range index.
    LTS_EXPORT uint8_t lts_torrent_info_file_flags(lt::torrent_info* torrent, int32_t file_index);

    // V2 per-file merkle root (SHA-256, 32 bytes). Fills `out_root32` with the
    // root hash and returns true. Returns false (and leaves the buffer
    // untouched) on null / invalid / out-of-range / non-V2 torrents or files
    // whose root is all-zero (libtorrent returns a zero hash when unavailable).
    LTS_EXPORT bool lts_torrent_info_file_root(lt::torrent_info* torrent, int32_t file_index, uint8_t* out_root32);

    // Symlink target for a file marked with file_flags::symlink. Writes at
    // most `buffer_size` bytes (UTF-8, NUL-terminated) into `buffer` and
    // returns the number of bytes written excluding the NUL. Returns 0 on
    // null / invalid / out-of-range / non-symlink files, or when
    // `buffer_size <= 0`. Truncates without error when the symlink target is
    // longer than the buffer.
    LTS_EXPORT int32_t lts_torrent_info_symlink(lt::torrent_info* torrent, int32_t file_index, char* buffer, int32_t buffer_size);

    // Maps a byte offset in the torrent's virtual concatenated stream to the
    // index of the file that contains it. Returns -1 on null / invalid
    // handle, negative offset, or offset >= total_size. Useful for streaming
    // scenarios: given a byte position, find the backing file.
    LTS_EXPORT int32_t lts_torrent_info_file_index_at_offset(lt::torrent_info* torrent, int64_t offset);

    // Number of pieces that overlap `file_index` (last - first + 1 of the
    // half-open range that file_piece_range returns). Returns 0 on null /
    // invalid / out-of-range file_index.
    LTS_EXPORT int32_t lts_torrent_info_file_num_pieces(lt::torrent_info* torrent, int32_t file_index);

    // Number of fixed-size 16 KiB blocks that overlap `file_index`. Blocks
    // are libtorrent's sub-piece unit for the wire protocol — they're the
    // granularity of request messages and partial-piece progress. Returns 0
    // on null / invalid / out-of-range file_index.
    LTS_EXPORT int32_t lts_torrent_info_file_num_blocks(lt::torrent_info* torrent, int32_t file_index);

    // Piece extent for a file: the first piece that overlaps the file and
    // one-past-the-last. Matches libtorrent's half-open index_range convention
    // (out_end is exclusive). Returns false (leaving out params untouched) on
    // null / invalid / out-of-range file_index or null out pointers.
    LTS_EXPORT bool lts_torrent_info_file_piece_range(lt::torrent_info* torrent, int32_t file_index, int32_t* out_first_piece, int32_t* out_end_piece);

    // Maps a (file, byte-offset, size) tuple into the piece that contains the
    // start of that range. Writes the piece index, the byte offset inside the
    // piece, and the number of contiguous bytes available from that offset
    // (capped by the piece boundary and the file's remaining bytes). Useful
    // for random-access reads / streaming. Returns false on null / invalid /
    // out-of-range inputs or null out pointers; the out parameters are left
    // untouched on failure.
    LTS_EXPORT bool lts_torrent_info_map_file(lt::torrent_info* torrent, int32_t file_index, int64_t offset, int32_t size, int32_t* out_piece_index, int32_t* out_piece_offset, int32_t* out_length);

    // Inverse of map_file: given a piece index, a byte offset within that
    // piece, and a size, returns the contiguous run of file_slice entries
    // each range overlaps. `out_list` is populated with a caller-owned array
    // — release via lts_destroy_file_slice_list. On null / invalid / negative
    // / out-of-range inputs the list is zeroed (length=0, slices=nullptr).
    LTS_EXPORT void lts_torrent_info_map_block(lt::torrent_info* torrent, int32_t piece_index, int64_t offset, int32_t size, file_slice_list* out_list);
    LTS_EXPORT void lts_destroy_file_slice_list(file_slice_list* list);

    // V2 per-file piece-layer bytes: the concatenated SHA-256 leaves for the
    // file's pieces, exactly `num_pieces_in_file * 32` bytes. Pair with
    // lts_torrent_info_file_root to verify the layer against the tree root.
    //
    // Two-call idiom: pass `out_buffer == nullptr` or `buffer_size <= 0` to
    // query the required byte count; pass a real buffer to fill it. Returns
    //   0  on null / invalid / out-of-range / V1-only / empty layer.
    //   N  = required byte count  (query mode)
    //   N' = bytes actually written  (fill mode, = min(required, buffer_size))
    // Short writes are not an error — callers that ask for a partial copy get
    // a truncated prefix. The layer is always a multiple of 32 bytes.
    LTS_EXPORT int32_t lts_torrent_info_piece_layer(lt::torrent_info* torrent, int32_t file_index, uint8_t* out_buffer, int32_t buffer_size);

    // Per-file modification time (Unix epoch seconds) as stored in the .torrent.
    // Returns 0 on null / invalid / out-of-range, or when the file_storage entry
    // lacks a stored mtime (most torrents don't set this — it's optional per
    // BEP-3). Caller distinguishes "file 0 has no mtime" from "epoch 0" by
    // treating any 0 return as absent.
    LTS_EXPORT int64_t lts_torrent_info_file_mtime(lt::torrent_info* torrent, int32_t file_index);

    // priority control
    LTS_EXPORT uint8_t get_file_dl_priority(lt::torrent_handle* torrent, int32_t file_index);
    LTS_EXPORT void set_file_dl_priority(lt::torrent_handle* torrent, int32_t file_index, uint8_t priority);
    LTS_EXPORT void lts_file_progress(lt::torrent_handle* torrent, int64_t* out_array, int32_t num_files);

    // download control
    LTS_EXPORT void start_torrent(lt::torrent_handle* torrent);
    LTS_EXPORT void stop_torrent(lt::torrent_handle* torrent);
    LTS_EXPORT void reannounce_torrent(lt::torrent_handle* torrent, const int32_t seconds, const uint8_t ignore_min_interval);

    // Sends a scrape request to the torrent's trackers. Completes asynchronously;
    // success fires cs_scrape_reply_alert, failure fires cs_scrape_failed_alert.
    // No-op when `torrent` is null or invalid.
    LTS_EXPORT void lts_scrape_tracker(lt::torrent_handle* torrent);

    // Heap-allocated status snapshot. Release via lts_destroy_torrent_status.
    // Replaces csdl's stack-based get_torrent_status — needed because save_path
    // and error_string are heap-allocated char*.
    LTS_EXPORT torrent_status* lts_get_torrent_status(lt::torrent_handle* torrent);
    LTS_EXPORT void lts_destroy_torrent_status(torrent_status* status);

    // Synchronous progress callback fired once before hashing starts (current_piece=0,
    // total_pieces≥0, piece_size, total_size) and once per piece during hashing. The
    // initial fire delivers piece_size + total_size to the managed side so it can
    // populate the OverallSize field of its progress event without a separate query.
    // ctx is the opaque pointer the caller passed into lts_create_torrent.
    typedef void (*lts_create_torrent_progress_cb)(
        int64_t current_piece,
        int64_t total_pieces,
        int64_t piece_size,
        int64_t total_size,
        void* ctx);

    // Builds a .torrent file from a source path and writes the bencoded result to
    // output_path. Hashing is synchronous on the calling thread — wrap on a worker
    // in managed code. Returns 0 on success, a negative code on failure:
    //   -1 = invalid argument (null required path / bad params)
    //   -2 = source path missing or unreadable
    //   -3 = hashing / generate / bencode threw (libtorrent error)
    //   -4 = file write failed
    //   -5 = cancelled via cancel_flag (no output file written)
    //
    // Tracker wire format (mirrors qBittorrent's flat-list-with-blank-tier-boundary
    // representation):
    // newline-separated URLs; a blank line increments the tier counter. The first
    // tier is 0 and is implicit — only emit a blank line to start tier 1+.
    //
    // web_seeds is newline-separated URLs (no tier concept). comment / created_by
    // are nullable. piece_size = 0 lets libtorrent pick. ignore_hidden = 1 skips
    // dotfile-named entries (Unix-hidden convention) via lt::add_files's filter.
    //
    // progress_cb fires once with current_piece=0 before hashing, then once per
    // piece. May be nullptr to skip progress reporting. cancel_flag is a pointer
    // to an int32_t the managed side can set to 1 from another thread; the native
    // side polls it in the per-piece progress callback and aborts cleanly. May be
    // nullptr when cancellation isn't needed. error_buf, when non-null, receives
    // a UTF-8 message (NUL-terminated, truncated to error_buf_size - 1) describing
    // a failure; on success the buffer is left untouched.
    LTS_EXPORT int32_t lts_create_torrent(
        const char* source_path,
        const char* output_path,
        int32_t piece_size,
        int32_t is_private,
        const char* comment,
        const char* created_by,
        const char* trackers,
        const char* web_seeds,
        int32_t ignore_hidden,
        lts_create_torrent_progress_cb progress_cb,
        void* progress_ctx,
        int32_t* cancel_flag,
        char* error_buf,
        int32_t error_buf_size);

#ifdef __cplusplus
}
#endif
#endif //CSDL_LIBRARY_HPP
