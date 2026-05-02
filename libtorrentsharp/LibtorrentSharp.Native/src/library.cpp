// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
//
// library.cpp
// Created by Albie on 29/02/2024.
//

#include "library.h"

#include <libtorrent/announce_entry.hpp>
#include <libtorrent/create_torrent.hpp>
#include <libtorrent/entry.hpp>
#include <libtorrent/fingerprint.hpp>
#include <libtorrent/ip_filter.hpp>
#include <libtorrent/bencode.hpp>
#include <libtorrent/kademlia/ed25519.hpp>
#include <libtorrent/kademlia/item.hpp>
#include <libtorrent/kademlia/types.hpp>
#include <libtorrent/magnet_uri.hpp>
#include <libtorrent/peer_info.hpp>
#include <libtorrent/read_resume_data.hpp>
#include <libtorrent/session_params.hpp>
#include <libtorrent/session_stats.hpp>
#include <libtorrent/settings_pack.hpp>
#include <libtorrent/span.hpp>
#include <libtorrent/torrent_handle.hpp>
#include <libtorrent/write_resume_data.hpp>
#include <libtorrent/extensions/ut_metadata.hpp>
#include <libtorrent/extensions/ut_pex.hpp>
#include <libtorrent/extensions/smart_ban.hpp>

#include <algorithm>
#include <cstring>

#include <chrono>
#include <map>
#include <mutex>
#include <string>
#include <vector>

// ---- port-mapping tracker ------------------------------------------------

namespace {

struct tracked_port_mapping
{
    int32_t mapping;
    int32_t external_port;
    uint8_t protocol;
    uint8_t transport;
    bool has_error;
    std::string error_message;
};

std::mutex g_portmap_mutex;
std::map<lt::session *, std::vector<tracked_port_mapping>> g_portmap_by_session;

} // namespace

void record_portmap_success(lt::session *session, int mapping, int external_port, uint8_t protocol, uint8_t transport)
{
    if (session == nullptr)
    {
        return;
    }

    std::lock_guard<std::mutex> lock(g_portmap_mutex);
    auto &entries = g_portmap_by_session[session];

    for (auto &entry : entries)
    {
        if (entry.mapping == mapping)
        {
            entry.external_port = external_port;
            entry.protocol = protocol;
            entry.transport = transport;
            entry.has_error = false;
            entry.error_message.clear();
            return;
        }
    }

    entries.push_back({mapping, external_port, protocol, transport, false, std::string()});
}

void record_portmap_error(lt::session *session, int mapping, uint8_t transport, const char *error_message)
{
    if (session == nullptr)
    {
        return;
    }

    std::lock_guard<std::mutex> lock(g_portmap_mutex);
    auto &entries = g_portmap_by_session[session];
    const std::string msg(error_message == nullptr ? "" : error_message);

    for (auto &entry : entries)
    {
        if (entry.mapping == mapping)
        {
            entry.transport = transport;
            entry.has_error = true;
            entry.error_message = msg;
            return;
        }
    }

    entries.push_back({mapping, -1, 0, transport, true, msg});
}

void forget_portmap_session(lt::session *session)
{
    if (session == nullptr)
    {
        return;
    }

    std::lock_guard<std::mutex> lock(g_portmap_mutex);
    g_portmap_by_session.erase(session);
}

extern "C" {

lt::session* create_session(lt::settings_pack* pack)
{
    lt::session_params params;

    if (pack != nullptr)
    {
        params.settings = *pack;
    }

    auto* session = new lt::session(params);
    session->add_extension(lt::create_ut_metadata_plugin);
    session->add_extension(lt::create_ut_pex_plugin);
    session->add_extension(lt::create_smart_ban_plugin);
    return session;
}

void destroy_session(lt::session* session)
{
    if (session == nullptr)
    {
        return;
    }

    forget_portmap_session(session);

    session->abort();
    delete session;
}

void apply_settings(lt::session* session, lt::settings_pack* settings)
{
    if (session == nullptr || settings == nullptr)
    {
        return;
    }

    session->apply_settings(*settings);
}

void set_event_callback(lt::session* session, cs_alert_callback callback, bool include_unmapped_events)
{
    if (session == nullptr)
    {
        return;
    }

    if (callback == nullptr)
    {
        clear_event_callback(session);
        return;
    }

    auto session_callback = [session, callback, include_unmapped_events]() -> void
    {
        std::thread(on_events_available, session, callback, include_unmapped_events).detach();
    };

    session->set_alert_notify(session_callback);
}

void clear_event_callback(lt::session* session)
{
    if (session == nullptr)
    {
        return;
    }

    session->set_alert_notify(nullptr);
}

lt::torrent_info* create_torrent_bytes(const char* data, long length)
{
    const lt::span buffer(data, length);
    const lt::load_torrent_limits cfg;

    return new lt::torrent_info(buffer, cfg, lt::from_span);
}

lt::torrent_info* create_torrent_file(const char* file_path)
{
    return new lt::torrent_info(std::string(file_path));
}

void destroy_torrent(lt::torrent_info* torrent)
{
    delete torrent;
}

// attach a torrent to the session, returning a handle that can be used to control the download.
// the torrent info handle is copied, and can be freed after the call to attach_torrent with a call to destroy_torrent_info.
lt::torrent_handle* attach_torrent(lt::session* session, lt::torrent_info* torrent, const char* save_path)
{
    if (session == nullptr || torrent == nullptr)
    {
        return nullptr;
    }

    lt::add_torrent_params params;
    std::string save_path_copy(save_path);

    if (!save_path_copy.empty())
    {
        params.save_path = save_path_copy;
    }

    // enable paused-by-default, disable auto-management
    params.flags |= lt::torrent_flags::paused;
    params.flags &= ~lt::torrent_flags::auto_managed;

    // set torrent info - make_shared creates a copy
    params.ti = std::make_shared<lt::torrent_info>(*torrent);
    const auto handle = new lt::torrent_handle(session->add_torrent(params));

    if (handle->is_valid())
    {
        return handle;
    }

    delete handle;
    return nullptr;
}

lt::torrent_handle* lts_add_magnet(lt::session* session, const char* magnet_uri, const char* save_path)
{
    if (session == nullptr || magnet_uri == nullptr)
    {
        return nullptr;
    }

    lt::error_code ec;
    lt::add_torrent_params params = lt::parse_magnet_uri(magnet_uri, ec);
    if (ec)
    {
        return nullptr;
    }

    if (save_path != nullptr)
    {
        std::string save_path_copy(save_path);
        if (!save_path_copy.empty())
        {
            params.save_path = save_path_copy;
        }
    }

    // match attach_torrent: paused by default, disable auto-management
    params.flags |= lt::torrent_flags::paused;
    params.flags &= ~lt::torrent_flags::auto_managed;

    const auto handle = new lt::torrent_handle(session->add_torrent(std::move(params)));

    if (handle->is_valid())
    {
        return handle;
    }

    delete handle;
    return nullptr;
}

void lts_request_resume_data(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    // save_info_dict so magnet-added torrents round-trip their metadata too
    torrent->save_resume_data(lt::torrent_handle::save_info_dict);
}

void lts_force_recheck(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    torrent->force_recheck();
}

void lts_pause_torrent(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    torrent->set_flags(lt::torrent_flags::paused | lt::torrent_flags::auto_managed);
}

void lts_resume_torrent(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    torrent->unset_flags(lt::torrent_flags::paused);
    torrent->set_flags(lt::torrent_flags::auto_managed);
}

void lts_force_start_torrent(lt::torrent_handle* torrent, bool force_start)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    if (force_start)
    {
        torrent->unset_flags(lt::torrent_flags::auto_managed);
        torrent->resume();
    }
    else
    {
        torrent->set_flags(lt::torrent_flags::auto_managed);
    }
}

void lts_move_storage(lt::torrent_handle* torrent, const char* new_path, int32_t flags)
{
    if (torrent == nullptr || !torrent->is_valid() || new_path == nullptr)
    {
        return;
    }

    torrent->move_storage(std::string(new_path), static_cast<lt::move_flags_t>(flags));
}

// Fills the 16-byte v6 buffer with a v4-mapped representation of a v4 address,
// or the raw v6 bytes for a v6 address.
static void fill_v6_mapped(const boost::asio::ip::address& addr, uint8_t* buffer)
{
    if (addr.is_v6())
    {
        auto bytes = addr.to_v6().to_bytes();
        std::copy(bytes.begin(), bytes.end(), buffer);
        return;
    }

    // v4 → v4-mapped v6 (::ffff:0:0/96)
    auto v4_bytes = addr.to_v4().to_bytes();
    std::fill(buffer, buffer + 10, 0);
    buffer[10] = 0xff;
    buffer[11] = 0xff;
    std::copy(v4_bytes.begin(), v4_bytes.end(), buffer + 12);
}

void lts_get_peers(lt::torrent_handle* torrent, peer_list* out_list)
{
    if (out_list == nullptr)
    {
        return;
    }

    out_list->length = 0;
    out_list->peers = nullptr;

    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    std::vector<lt::peer_info> peers;
    torrent->get_peer_info(peers);

    const auto count = static_cast<int32_t>(peers.size());
    if (count == 0)
    {
        return;
    }

    const auto entries = new peer_information[count]();

    for (int32_t i = 0; i < count; i++)
    {
        const auto& p = peers[i];
        auto& entry = entries[i];

        fill_v6_mapped(p.ip.address(), entry.ipv6_address);
        entry.port = p.ip.port();

        const auto& client = p.client;
        entry.client = new char[client.size() + 1]();
        std::copy(client.begin(), client.end(), entry.client);

        entry.flags = static_cast<uint32_t>(p.flags);
        entry.source = static_cast<uint8_t>(p.source);
        entry.progress = p.progress;
        entry.up_rate = p.up_speed;
        entry.down_rate = p.down_speed;
        entry.total_uploaded = p.total_upload;
        entry.total_downloaded = p.total_download;
        entry.connection_type = static_cast<int32_t>(static_cast<std::uint8_t>(p.connection_type));
        entry.num_hashfails = p.num_hashfails;
        entry.downloading_piece_index = static_cast<int32_t>(static_cast<int>(p.downloading_piece_index));
        entry.downloading_block_index = p.downloading_block_index;
        entry.downloading_progress = p.downloading_progress;
        entry.downloading_total = p.downloading_total;
        entry.failcount = p.failcount;
        entry.payload_up_rate = p.payload_up_speed;
        entry.payload_down_rate = p.payload_down_speed;
        std::copy(p.pid.begin(), p.pid.end(), entry.pid);
    }

    out_list->length = count;
    out_list->peers = entries;
}

void lts_destroy_peers(peer_list* list)
{
    if (list == nullptr || list->peers == nullptr)
    {
        return;
    }

    for (int32_t i = 0; i < list->length; i++)
    {
        delete[] list->peers[i].client;
    }

    delete[] list->peers;
    list->peers = nullptr;
    list->length = 0;
}

static char* clone_cstr(const std::string& s)
{
    auto out = new char[s.size() + 1]();
    std::copy(s.begin(), s.end(), out);
    return out;
}

void lts_get_trackers(lt::torrent_handle* torrent, tracker_list* out_list)
{
    if (out_list == nullptr)
    {
        return;
    }

    out_list->length = 0;
    out_list->trackers = nullptr;

    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    auto entries = torrent->trackers();
    const auto count = static_cast<int32_t>(entries.size());
    if (count == 0)
    {
        return;
    }

    const auto items = new tracker_information[count]();

    for (int32_t i = 0; i < count; i++)
    {
        const auto& entry = entries[i];
        auto& out = items[i];

        out.url = clone_cstr(entry.url);
        out.tier = entry.tier;
        out.source = entry.source;
        out.verified = entry.verified;

        int32_t scrape_complete = -1;
        int32_t scrape_incomplete = -1;
        int32_t scrape_downloaded = -1;
        uint8_t fails = 0;
        bool updating = false;
        std::string first_error;
        lt::time_point32 earliest_next = lt::time_point32::max();
        std::string first_message;
        bool any_start_sent = false;
        bool any_complete_sent = false;
        lt::time_point32 earliest_min = lt::time_point32::max();

        for (const auto& ep : entry.endpoints)
        {
            for (auto version : {lt::protocol_version::V1, lt::protocol_version::V2})
            {
                const auto& ih = ep.info_hashes[version];
                if (ih.scrape_complete > scrape_complete) scrape_complete = ih.scrape_complete;
                if (ih.scrape_incomplete > scrape_incomplete) scrape_incomplete = ih.scrape_incomplete;
                if (ih.scrape_downloaded > scrape_downloaded) scrape_downloaded = ih.scrape_downloaded;
                if (ih.fails > fails) fails = ih.fails;
                if (ih.updating) updating = true;
                if (first_error.empty() && ih.last_error) first_error = ih.last_error.message();
                if (ih.next_announce < earliest_next) earliest_next = ih.next_announce;
                if (first_message.empty() && !ih.message.empty()) first_message = ih.message;
                if (ih.min_announce < earliest_min) earliest_min = ih.min_announce;
                if (ih.start_sent) any_start_sent = true;
                if (ih.complete_sent) any_complete_sent = true;
            }
        }

        out.scrape_complete = scrape_complete;
        out.scrape_incomplete = scrape_incomplete;
        out.scrape_downloaded = scrape_downloaded;
        out.fails = fails;
        out.updating = updating;
        out.last_error = clone_cstr(first_error);

        if (earliest_next == lt::time_point32::max())
        {
            out.next_announce_epoch = 0;
        }
        else
        {
            const auto now_steady = lt::clock_type::now();
            const auto delta_secs = std::chrono::duration_cast<std::chrono::seconds>(earliest_next - now_steady).count();
            const auto now_wall = std::chrono::system_clock::now();
            out.next_announce_epoch = std::chrono::duration_cast<std::chrono::seconds>(now_wall.time_since_epoch()).count() + delta_secs;
        }

        out.trackerid = clone_cstr(entry.trackerid);
        out.message = clone_cstr(first_message);
        out.start_sent = any_start_sent;
        out.complete_sent = any_complete_sent;
        if (earliest_min == lt::time_point32::max())
        {
            out.min_announce_epoch = 0;
        }
        else
        {
            const auto now_steady = lt::clock_type::now();
            const auto delta_secs = std::chrono::duration_cast<std::chrono::seconds>(earliest_min - now_steady).count();
            const auto now_wall = std::chrono::system_clock::now();
            out.min_announce_epoch = std::chrono::duration_cast<std::chrono::seconds>(now_wall.time_since_epoch()).count() + delta_secs;
        }
    }

    out_list->length = count;
    out_list->trackers = items;
}

void lts_destroy_trackers(tracker_list* list)
{
    if (list == nullptr || list->trackers == nullptr)
    {
        return;
    }

    for (int32_t i = 0; i < list->length; i++)
    {
        delete[] list->trackers[i].url;
        delete[] list->trackers[i].last_error;
        delete[] list->trackers[i].trackerid;
        delete[] list->trackers[i].message;
    }

    delete[] list->trackers;
    list->trackers = nullptr;
    list->length = 0;
}

void lts_add_tracker(lt::torrent_handle* torrent, const char* url, const int32_t tier)
{
    if (torrent == nullptr || url == nullptr) return;
    lt::announce_entry entry{std::string(url)};
    entry.tier = static_cast<uint8_t>(tier);
    torrent->add_tracker(entry);
}

void lts_remove_tracker(lt::torrent_handle* torrent, const char* url)
{
    if (torrent == nullptr || url == nullptr) return;
    auto trackers = torrent->trackers();
    trackers.erase(
        std::remove_if(trackers.begin(), trackers.end(),
            [url](const lt::announce_entry& t) { return t.url == url; }),
        trackers.end());
    torrent->replace_trackers(trackers);
}

void lts_edit_tracker(lt::torrent_handle* torrent, const char* old_url, const char* new_url, const int32_t new_tier)
{
    if (torrent == nullptr || old_url == nullptr || new_url == nullptr) return;
    auto trackers = torrent->trackers();
    for (auto& t : trackers)
    {
        if (t.url == old_url)
        {
            t.url = new_url;
            if (new_tier >= 0)
                t.tier = static_cast<uint8_t>(new_tier);
            break;
        }
    }
    torrent->replace_trackers(trackers);
}

void lts_get_web_seeds(lt::torrent_handle* torrent, web_seed_list* out_list)
{
    if (torrent == nullptr || out_list == nullptr) return;
    auto seeds = torrent->url_seeds();
    out_list->count = static_cast<int32_t>(seeds.size());
    if (seeds.empty())
    {
        out_list->items = nullptr;
        return;
    }
    out_list->items = new web_seed_information[seeds.size()];
    int i = 0;
    for (const auto& url : seeds)
    {
        out_list->items[i].url = clone_cstr(url);
        ++i;
    }
}

void lts_destroy_web_seeds(web_seed_list* list)
{
    if (list == nullptr || list->items == nullptr) return;
    for (int i = 0; i < list->count; ++i)
    {
        delete[] list->items[i].url;
    }
    delete[] list->items;
    list->items = nullptr;
    list->count = 0;
}

void lts_add_web_seed(lt::torrent_handle* torrent, const char* url)
{
    if (torrent == nullptr || url == nullptr) return;
    torrent->add_url_seed(url);
}

void lts_remove_web_seed(lt::torrent_handle* torrent, const char* url)
{
    if (torrent == nullptr || url == nullptr) return;
    torrent->remove_url_seed(url);
}

bool lts_export_torrent_to_bytes(lt::torrent_handle* torrent, uint8_t** out_data, int32_t* out_size)
{
    if (torrent == nullptr || out_data == nullptr || out_size == nullptr) return false;

    auto ti = torrent->torrent_file();
    if (!ti) return false;

    lt::create_torrent ct(*ti);
    lt::entry e = ct.generate();
    std::vector<char> buf;
    lt::bencode(std::back_inserter(buf), e);

    auto* heap = new uint8_t[buf.size()];
    std::memcpy(heap, buf.data(), buf.size());
    *out_data = heap;
    *out_size = static_cast<int32_t>(buf.size());
    return true;
}

void lts_free_bytes(uint8_t* data)
{
    delete[] data;
}

// Rate-limit pairs. libtorrent accepts 0 or any negative as "unlimited" and its
// getter returns -1 for the unlimited state (not 0) — callers should treat any
// value <= 0 as unlimited regardless of which sentinel libtorrent picks.
void lts_set_torrent_upload_limit(lt::torrent_handle* torrent, int32_t bytes_per_second)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    torrent->set_upload_limit(bytes_per_second);
}

int32_t lts_get_torrent_upload_limit(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }

    return torrent->upload_limit();
}

void lts_set_torrent_download_limit(lt::torrent_handle* torrent, int32_t bytes_per_second)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    torrent->set_download_limit(bytes_per_second);
}

int32_t lts_get_torrent_download_limit(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }

    return torrent->download_limit();
}

void lts_set_super_seeding(lt::torrent_handle* torrent, bool enabled)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    if (enabled)
    {
        torrent->set_flags(lt::torrent_flags::super_seeding);
    }
    else
    {
        torrent->unset_flags(lt::torrent_flags::super_seeding);
    }
}

uint64_t lts_get_torrent_flags(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    return static_cast<uint64_t>(static_cast<std::uint64_t>(torrent->flags()));
}

void lts_set_torrent_flags(lt::torrent_handle* torrent, uint64_t flags, uint64_t mask)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }
    // torrent_flags_t is a bitfield_flag<uint64_t>; reconstructing via the
    // from-integer ctor is the supported path in libtorrent 2.x.
    torrent->set_flags(lt::torrent_flags_t(flags), lt::torrent_flags_t(mask));
}

void lts_unset_torrent_flags(lt::torrent_handle* torrent, uint64_t flags)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }
    torrent->unset_flags(lt::torrent_flags_t(flags));
}

void lts_set_sequential(lt::torrent_handle* torrent, bool enabled)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    if (enabled)
    {
        torrent->set_flags(lt::torrent_flags::sequential_download);
    }
    else
    {
        torrent->unset_flags(lt::torrent_flags::sequential_download);
    }
}

void lts_get_port_mappings(lt::session* session, port_mapping_list* out_list)
{
    if (out_list == nullptr)
    {
        return;
    }

    out_list->length = 0;
    out_list->mappings = nullptr;

    if (session == nullptr)
    {
        return;
    }

    std::lock_guard<std::mutex> lock(g_portmap_mutex);
    const auto it = g_portmap_by_session.find(session);
    if (it == g_portmap_by_session.end() || it->second.empty())
    {
        return;
    }

    const auto count = static_cast<int32_t>(it->second.size());
    const auto items = new port_mapping[count]();

    for (int32_t i = 0; i < count; i++)
    {
        const auto& tracked = it->second[i];
        auto& entry = items[i];

        entry.mapping = tracked.mapping;
        entry.external_port = tracked.external_port;
        entry.protocol = tracked.protocol;
        entry.transport = tracked.transport;
        entry.has_error = tracked.has_error;
        entry.error_message = clone_cstr(tracked.error_message);
    }

    out_list->length = count;
    out_list->mappings = items;
}

void lts_destroy_port_mappings(port_mapping_list* list)
{
    if (list == nullptr || list->mappings == nullptr)
    {
        return;
    }

    for (int32_t i = 0; i < list->length; i++)
    {
        delete[] list->mappings[i].error_message;
    }

    delete[] list->mappings;
    list->mappings = nullptr;
    list->length = 0;
}

// ---- ip_filter ------------------------------------------------------------

// Inverse of fill_v6_mapped: parses a 16-byte buffer into an lt::address.
// v4-mapped prefixes (::ffff:a.b.c.d) resolve to address_v4 for symmetry with
// libtorrent's ip_filter, which stores v4 and v6 ranges in separate tables.
// Explicit extern "C++" avoids MSVC C4190 (without it the library.h extern "C"
// block leaks across the translation unit on MSVC).
extern "C++" {
static lt::address parse_v6_mapped(const uint8_t buf[16])
{
    bool is_v4_mapped = true;
    for (int i = 0; i < 10; i++)
    {
        if (buf[i] != 0)
        {
            is_v4_mapped = false;
            break;
        }
    }

    if (is_v4_mapped && buf[10] == 0xff && buf[11] == 0xff)
    {
        boost::asio::ip::address_v4::bytes_type v4{};
        v4[0] = buf[12];
        v4[1] = buf[13];
        v4[2] = buf[14];
        v4[3] = buf[15];
        return boost::asio::ip::address_v4(v4);
    }

    boost::asio::ip::address_v6::bytes_type v6{};
    std::copy(buf, buf + 16, v6.begin());
    return boost::asio::ip::address_v6(v6);
}
} // extern "C++"

void lts_set_ip_filter(lt::session* session, const ip_filter_rule* rules, int32_t length)
{
    if (session == nullptr)
    {
        return;
    }

    lt::ip_filter filter;
    if (rules != nullptr && length > 0)
    {
        for (int32_t i = 0; i < length; i++)
        {
            const auto start = parse_v6_mapped(rules[i].start_ipv6);
            const auto end = parse_v6_mapped(rules[i].end_ipv6);
            filter.add_rule(start, end, rules[i].flags);
        }
    }

    session->set_ip_filter(std::move(filter));
}

void lts_get_ip_filter(lt::session* session, ip_filter_rules* out_list)
{
    if (out_list == nullptr)
    {
        return;
    }

    out_list->length = 0;
    out_list->rules = nullptr;

    if (session == nullptr)
    {
        return;
    }

    const auto filter = session->get_ip_filter();
    auto exported = filter.export_filter();

    const auto& v4_ranges = std::get<0>(exported);
    const auto& v6_ranges = std::get<1>(exported);

    const auto count = static_cast<int32_t>(v4_ranges.size() + v6_ranges.size());
    if (count == 0)
    {
        return;
    }

    const auto entries = new ip_filter_rule[count]();
    int32_t idx = 0;

    for (const auto& r : v4_ranges)
    {
        fill_v6_mapped(boost::asio::ip::address(r.first), entries[idx].start_ipv6);
        fill_v6_mapped(boost::asio::ip::address(r.last), entries[idx].end_ipv6);
        entries[idx].flags = r.flags;
        idx++;
    }

    for (const auto& r : v6_ranges)
    {
        fill_v6_mapped(boost::asio::ip::address(r.first), entries[idx].start_ipv6);
        fill_v6_mapped(boost::asio::ip::address(r.last), entries[idx].end_ipv6);
        entries[idx].flags = r.flags;
        idx++;
    }

    out_list->length = count;
    out_list->rules = entries;
}

void lts_destroy_ip_filter(ip_filter_rules* list)
{
    if (list == nullptr || list->rules == nullptr)
    {
        return;
    }

    delete[] list->rules;
    list->rules = nullptr;
    list->length = 0;
}

void lts_post_dht_stats(lt::session* session)
{
    if (session == nullptr)
    {
        return;
    }

    session->post_dht_stats();
}

void lts_post_session_stats(lt::session* session)
{
    if (session == nullptr)
    {
        return;
    }

    session->post_session_stats();
}

namespace {

// libtorrent's metric registry is build-static — same vector every call. Cache
// once via a function-local static (thread-safe initialization per C++11
// magic statics). The returned `stats_metric::name` pointers are libtorrent-
// owned static literals, so they outlive any caller use.
const std::vector<lt::stats_metric>& cached_session_stats_metrics()
{
    static const std::vector<lt::stats_metric> cache = lt::session_stats_metrics();
    return cache;
}

} // namespace

int32_t lts_session_stats_metric_count()
{
    return static_cast<int32_t>(cached_session_stats_metrics().size());
}

void lts_session_stats_metric_at(
    int32_t idx,
    const char** out_name,
    int32_t* out_value_index,
    int32_t* out_type)
{
    const auto& metrics = cached_session_stats_metrics();
    if (idx < 0 || static_cast<size_t>(idx) >= metrics.size())
    {
        if (out_name != nullptr) *out_name = nullptr;
        if (out_value_index != nullptr) *out_value_index = -1;
        if (out_type != nullptr) *out_type = -1;
        return;
    }

    const auto& metric = metrics[static_cast<size_t>(idx)];
    if (out_name != nullptr) *out_name = metric.name;
    if (out_value_index != nullptr) *out_value_index = metric.value_index;
    if (out_type != nullptr) *out_type = static_cast<int32_t>(metric.type);
}

int32_t lts_session_stats_find_metric_idx(const char* name)
{
    if (name == nullptr) return -1;
    return lt::find_metric_idx(name);
}

void lts_session_save_state(lt::session* session, char** out_buf, int32_t* out_len)
{
    if (out_buf != nullptr) *out_buf = nullptr;
    if (out_len != nullptr) *out_len = 0;
    if (session == nullptr || out_buf == nullptr || out_len == nullptr)
    {
        return;
    }

    try
    {
        auto params = session->session_state();
        auto bytes = lt::write_session_params_buf(params);
        if (bytes.empty())
        {
            return;
        }

        auto* buffer = new char[bytes.size()];
        std::copy(bytes.begin(), bytes.end(), buffer);
        *out_buf = buffer;
        *out_len = static_cast<int32_t>(bytes.size());
    }
    catch (const std::exception&)
    {
        // leave outputs at their zeroed defaults set above
    }
}

void lts_destroy_session_state_buf(char* buf)
{
    delete[] buf;
}

lt::session* lts_create_session_from_state(const char* buf, int32_t length)
{
    if (buf == nullptr || length <= 0)
    {
        return nullptr;
    }

    try
    {
        auto params = lt::read_session_params(lt::span<char const>(buf, length));
        auto* session = new lt::session(params);
        session->add_extension(lt::create_ut_metadata_plugin);
        session->add_extension(lt::create_ut_pex_plugin);
        session->add_extension(lt::create_smart_ban_plugin);
        return session;
    }
    catch (const std::exception&)
    {
        return nullptr;
    }
}

uint16_t lts_session_listen_port(lt::session* session)
{
    if (session == nullptr)
    {
        return 0;
    }
    return session->listen_port();
}

uint16_t lts_session_ssl_listen_port(lt::session* session)
{
    if (session == nullptr)
    {
        return 0;
    }
    return session->ssl_listen_port();
}

int8_t lts_session_is_listening(lt::session* session)
{
    if (session == nullptr)
    {
        return 0;
    }
    return session->is_listening() ? int8_t{1} : int8_t{0};
}

void lts_session_get_listen_interfaces(lt::session* session, char** out_buf, int32_t* out_len)
{
    if (out_buf != nullptr) *out_buf = nullptr;
    if (out_len != nullptr) *out_len = 0;
    if (session == nullptr || out_buf == nullptr || out_len == nullptr)
    {
        return;
    }

    try
    {
        auto pack = session->get_settings();
        const auto& value = pack.get_str(lt::settings_pack::listen_interfaces);
        if (value.empty())
        {
            return;
        }

        auto* buffer = new char[value.size()];
        std::copy(value.begin(), value.end(), buffer);
        *out_buf = buffer;
        *out_len = static_cast<int32_t>(value.size());
    }
    catch (const std::exception&)
    {
        // leave outputs zeroed
    }
}

void lts_destroy_listen_interfaces_buf(char* buf)
{
    delete[] buf;
}

void lts_dht_put_immutable_string(
    lt::session* session,
    const uint8_t* data,
    int32_t data_len,
    uint8_t* out_target_sha1)
{
    if (out_target_sha1 == nullptr)
    {
        return;
    }
    std::fill(out_target_sha1, out_target_sha1 + 20, uint8_t{0});

    if (session == nullptr || data_len < 0 || (data == nullptr && data_len > 0))
    {
        return;
    }

    lt::entry e(std::string(reinterpret_cast<const char*>(data), static_cast<size_t>(data_len)));
    lt::sha1_hash target = session->dht_put_item(e);
    auto src = reinterpret_cast<const uint8_t*>(target.data());
    std::copy(src, src + 20, out_target_sha1);
}

void lts_dht_get_immutable(lt::session* session, const uint8_t* target_sha1)
{
    if (session == nullptr || target_sha1 == nullptr)
    {
        return;
    }

    lt::sha1_hash target;
    std::copy(target_sha1, target_sha1 + 20, reinterpret_cast<uint8_t*>(target.data()));
    session->dht_get_item(target);
}

void lts_dht_get_item_mutable(
    lt::session* session,
    const uint8_t* key32,
    const uint8_t* salt,
    int32_t salt_len)
{
    if (session == nullptr || key32 == nullptr || salt_len < 0 || (salt == nullptr && salt_len > 0))
    {
        return;
    }

    std::array<char, 32> key{};
    std::copy(key32, key32 + 32, reinterpret_cast<uint8_t*>(key.data()));

    std::string salt_str;
    if (salt != nullptr && salt_len > 0)
    {
        salt_str.assign(reinterpret_cast<const char*>(salt), static_cast<size_t>(salt_len));
    }

    session->dht_get_item(key, salt_str);
}

void lts_ed25519_create_seed(uint8_t* out_seed32)
{
    if (out_seed32 == nullptr) return;
    auto seed = lt::dht::ed25519_create_seed();
    auto src = reinterpret_cast<const uint8_t*>(seed.data());
    std::copy(src, src + 32, out_seed32);
}

void lts_ed25519_create_keypair(
    const uint8_t* seed32,
    uint8_t* out_public_key32,
    uint8_t* out_secret_key64)
{
    if (seed32 == nullptr || out_public_key32 == nullptr || out_secret_key64 == nullptr) return;

    std::array<char, 32> seed{};
    std::copy(seed32, seed32 + 32, reinterpret_cast<uint8_t*>(seed.data()));

    auto [pk, sk] = lt::dht::ed25519_create_keypair(seed);
    auto pk_src = reinterpret_cast<const uint8_t*>(pk.bytes.data());
    auto sk_src = reinterpret_cast<const uint8_t*>(sk.bytes.data());
    std::copy(pk_src, pk_src + 32, out_public_key32);
    std::copy(sk_src, sk_src + 64, out_secret_key64);
}

void lts_ed25519_sign(
    const uint8_t* message,
    int32_t message_len,
    const uint8_t* public_key32,
    const uint8_t* secret_key64,
    uint8_t* out_signature64)
{
    if (out_signature64 == nullptr) return;
    if (public_key32 == nullptr || secret_key64 == nullptr || message_len < 0
        || (message == nullptr && message_len > 0))
    {
        std::fill(out_signature64, out_signature64 + 64, uint8_t{0});
        return;
    }

    lt::dht::public_key pk(reinterpret_cast<const char*>(public_key32));
    lt::dht::secret_key sk(reinterpret_cast<const char*>(secret_key64));
    lt::span<char const> msg_span(reinterpret_cast<const char*>(message), static_cast<std::size_t>(message_len));

    auto sig = lt::dht::ed25519_sign(msg_span, pk, sk);
    auto src = reinterpret_cast<const uint8_t*>(sig.bytes.data());
    std::copy(src, src + 64, out_signature64);
}

int8_t lts_ed25519_verify(
    const uint8_t* signature64,
    const uint8_t* message,
    int32_t message_len,
    const uint8_t* public_key32)
{
    if (signature64 == nullptr || public_key32 == nullptr || message_len < 0
        || (message == nullptr && message_len > 0))
    {
        return int8_t{0};
    }

    lt::dht::signature sig(reinterpret_cast<const char*>(signature64));
    lt::dht::public_key pk(reinterpret_cast<const char*>(public_key32));
    lt::span<char const> msg_span(reinterpret_cast<const char*>(message), static_cast<std::size_t>(message_len));

    return lt::dht::ed25519_verify(sig, msg_span, pk) ? int8_t{1} : int8_t{0};
}

void lts_dht_put_item_mutable(
    lt::session* session,
    const uint8_t* public_key32,
    const uint8_t* secret_key64,
    const uint8_t* salt,
    int32_t salt_len,
    const uint8_t* value,
    int32_t value_len,
    int64_t seq)
{
    if (session == nullptr || public_key32 == nullptr || secret_key64 == nullptr
        || value_len < 0 || salt_len < 0
        || (value == nullptr && value_len > 0)
        || (salt == nullptr && salt_len > 0))
    {
        return;
    }

    // Wrap the raw bytes as entry::string_t (matches DhtPutImmutable's shape).
    lt::entry value_entry(std::string(reinterpret_cast<const char*>(value), static_cast<std::size_t>(value_len)));
    std::vector<char> bencoded;
    lt::bencode(std::back_inserter(bencoded), value_entry);

    std::string salt_str = (salt != nullptr && salt_len > 0)
        ? std::string(reinterpret_cast<const char*>(salt), static_cast<std::size_t>(salt_len))
        : std::string();

    // Compute the BEP44 signature here (using the helper from slice 9's
    // sibling), so the std::function callback we install can be a pure
    // value-plug — no surprises on libtorrent's network thread.
    lt::dht::public_key pk(reinterpret_cast<const char*>(public_key32));
    lt::dht::secret_key sk(reinterpret_cast<const char*>(secret_key64));
    lt::dht::sequence_number seq_obj(seq);
    auto sig = lt::dht::sign_mutable_item(
        lt::span<char const>(bencoded.data(), bencoded.size()),
        lt::span<char const>(salt_str.data(), salt_str.size()),
        seq_obj, pk, sk);

    std::array<char, 32> pk_array{};
    std::copy_n(reinterpret_cast<const char*>(public_key32), 32, pk_array.begin());

    // The lambda fires on libtorrent's network thread when the put is ready
    // to be issued. Capture by value so the lambda owns its inputs.
    session->dht_put_item(
        pk_array,
        [value_entry, sig, seq](
            lt::entry& v_out,
            std::array<char, 64>& sig_out,
            std::int64_t& seq_out,
            std::string const& /*salt_unused*/) mutable
        {
            v_out = value_entry;
            std::copy(sig.bytes.begin(), sig.bytes.end(), sig_out.begin());
            seq_out = seq;
        },
        salt_str);
}

void lts_set_file_piece_priority(lt::torrent_handle* torrent, int32_t file_index, uint8_t priority)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    auto ti = torrent->torrent_file();
    if (!ti)
    {
        // Metadata not yet resolved (magnet pre-metadata). No-op.
        return;
    }

    const auto& files = ti->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return;
    }

    const auto file = static_cast<lt::file_index_t>(file_index);
    const auto file_offset = files.file_offset(file);
    const auto file_size = files.file_size(file);
    if (file_size <= 0)
    {
        return;
    }

    const auto piece_length = ti->piece_length();
    const auto first_piece = static_cast<int>(file_offset / piece_length);
    const auto last_piece = static_cast<int>((file_offset + file_size - 1) / piece_length);

    const auto p = static_cast<lt::download_priority_t>(priority);
    torrent->piece_priority(static_cast<lt::piece_index_t>(first_piece), p);

    if (last_piece != first_piece)
    {
        torrent->piece_priority(static_cast<lt::piece_index_t>(last_piece), p);
    }
}

// --- Per-piece priority surface -----------------------------------------
// libtorrent's `download_priority_t` applies equally to pieces (via
// torrent_handle::piece_priority) and files (file_priority). The priority
// byte values are identical: 0=skip, 1=low, 4=default, 7=top. We expose
// piece-level granularity here so consumers can drive streaming or selective
// downloads without going through the file-level rollup.

int32_t lts_num_pieces(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    const auto ti = torrent->torrent_file();
    if (!ti || !ti->is_valid())
    {
        // Pre-metadata magnet or failed parse — no piece count yet.
        return 0;
    }
    return static_cast<int32_t>(ti->num_pieces());
}

int64_t lts_total_size(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    const auto ti = torrent->torrent_file();
    if (!ti || !ti->is_valid())
    {
        return 0;
    }
    return static_cast<int64_t>(ti->total_size());
}

uint8_t lts_get_piece_priority(lt::torrent_handle* torrent, int32_t piece_index)
{
    const auto count = lts_num_pieces(torrent);
    if (count == 0 || piece_index < 0 || piece_index >= count)
    {
        return 0;
    }
    const auto p = torrent->piece_priority(static_cast<lt::piece_index_t>(piece_index));
    return static_cast<uint8_t>(static_cast<std::uint8_t>(p));
}

void lts_set_piece_priority(lt::torrent_handle* torrent, int32_t piece_index, uint8_t priority)
{
    const auto count = lts_num_pieces(torrent);
    if (count == 0 || piece_index < 0 || piece_index >= count)
    {
        return;
    }
    torrent->piece_priority(static_cast<lt::piece_index_t>(piece_index),
                            static_cast<lt::download_priority_t>(priority));
}

void lts_get_piece_priorities(lt::torrent_handle* torrent, piece_priority_list* out_list)
{
    if (out_list == nullptr)
    {
        return;
    }

    out_list->length = 0;
    out_list->priorities = nullptr;

    const auto count = lts_num_pieces(torrent);
    if (count == 0)
    {
        return;
    }

    // torrent_handle::get_piece_priorities() returns a vector<download_priority_t>.
    // download_priority_t is a std::uint8_t-backed strong enum — copy verbatim.
    const auto priorities = torrent->get_piece_priorities();
    const auto out_count = static_cast<int32_t>(priorities.size());
    if (out_count == 0)
    {
        return;
    }

    const auto buffer = new uint8_t[out_count];
    for (int32_t i = 0; i < out_count; i++)
    {
        buffer[i] = static_cast<uint8_t>(static_cast<std::uint8_t>(priorities[i]));
    }

    out_list->length = out_count;
    out_list->priorities = buffer;
}

void lts_destroy_piece_priorities(piece_priority_list* list)
{
    if (list == nullptr || list->priorities == nullptr)
    {
        return;
    }

    delete[] list->priorities;
    list->priorities = nullptr;
    list->length = 0;
}

void lts_set_piece_priorities(lt::torrent_handle* torrent, const uint8_t* priorities, int32_t count)
{
    const auto num_pieces = lts_num_pieces(torrent);
    if (num_pieces == 0 || priorities == nullptr || count <= 0)
    {
        return;
    }

    // Truncate silently: if the caller passed more than the torrent has, we
    // clamp to num_pieces. Fewer is acceptable — libtorrent uses the vector
    // length as the authoritative piece count for this call, so we size it
    // exactly to what the caller supplied.
    const auto effective = std::min(count, num_pieces);
    std::vector<lt::download_priority_t> vec(static_cast<std::size_t>(effective));
    for (int32_t i = 0; i < effective; i++)
    {
        vec[i] = static_cast<lt::download_priority_t>(priorities[i]);
    }

    torrent->prioritize_pieces(vec);
}

bool lts_have_piece(lt::torrent_handle* torrent, int32_t piece_index)
{
    const auto count = lts_num_pieces(torrent);
    if (count == 0 || piece_index < 0 || piece_index >= count)
    {
        return false;
    }
    return torrent->have_piece(static_cast<lt::piece_index_t>(piece_index));
}

void lts_get_piece_bitfield(lt::torrent_handle* torrent, uint8_t* out_bits, int32_t num_bytes)
{
    if (!torrent || !out_bits || num_bytes <= 0) return;
    auto st = torrent->status(lt::torrent_handle::query_pieces);
    const auto& pieces = st.pieces;
    int total = static_cast<int>(pieces.size());
    std::memset(out_bits, 0, static_cast<size_t>(num_bytes));
    for (int i = 0; i < total && (i / 8) < num_bytes; i++) {
        if (pieces[static_cast<lt::piece_index_t>(i)])
            out_bits[i / 8] |= static_cast<uint8_t>(1 << (i % 8));
    }
}

// --- Peer-level handle ops ---------------------------------------------
// connect_peer: explicit peer add. libtorrent's connect_peer takes a
// tcp::endpoint; we decode the v4-mapped v6 buffer to an address and build
// the endpoint inline. clear_error: no-arg reset of any sticky storage
// error. rename_file: async rename, result fires via alerts.

bool lts_connect_peer(lt::torrent_handle* torrent, const uint8_t ipv6_address[16], uint16_t port)
{
    if (torrent == nullptr || !torrent->is_valid() || ipv6_address == nullptr || port == 0)
    {
        return false;
    }

    try
    {
        const lt::address addr = parse_v6_mapped(ipv6_address);
        torrent->connect_peer(boost::asio::ip::tcp::endpoint(addr, port));
        return true;
    }
    catch (const std::exception&)
    {
        // libtorrent throws when the handle has no associated torrent (rare race
        // between add + detach). Treat as a silent failure — no crash, no queue.
        return false;
    }
}

void lts_clear_error(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }
    torrent->clear_error();
}

void lts_rename_file(lt::torrent_handle* torrent, int32_t file_index, const char* new_name)
{
    if (torrent == nullptr || !torrent->is_valid() || new_name == nullptr || new_name[0] == '\0')
    {
        return;
    }

    const auto ti = torrent->torrent_file();
    if (!ti || !ti->is_valid())
    {
        // Pre-metadata magnet — libtorrent has no file_storage yet.
        return;
    }

    const auto& files = ti->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return;
    }

    try
    {
        torrent->rename_file(static_cast<lt::file_index_t>(file_index), std::string(new_name));
    }
    catch (const std::exception&)
    {
        // Same rationale as connect_peer: silent fail on transient libtorrent
        // exceptions. The async alert pipeline is where failure status surfaces.
    }
}

lt::torrent_handle* lts_add_torrent_with_resume(lt::session* session, const char* resume_data, int32_t length, const char* save_path)
{
    if (session == nullptr || resume_data == nullptr || length <= 0)
    {
        return nullptr;
    }

    lt::error_code ec;
    lt::add_torrent_params params = lt::read_resume_data(lt::span<const char>(resume_data, length), ec);
    if (ec)
    {
        return nullptr;
    }

    if (save_path != nullptr)
    {
        std::string save_path_copy(save_path);
        if (!save_path_copy.empty())
        {
            params.save_path = save_path_copy;
        }
    }

    // paused by default, disable auto-management — caller drives start/stop
    params.flags |= lt::torrent_flags::paused;
    params.flags &= ~lt::torrent_flags::auto_managed;

    const auto handle = new lt::torrent_handle(session->add_torrent(std::move(params)));

    if (handle->is_valid())
    {
        return handle;
    }

    delete handle;
    return nullptr;
}

// after detaching the torrent, the torrent handle is no longer valid.
// additionally, a call to destroy_torrent is not needed. remove_flags mirrors
// libtorrent's remove_flags_t (0 = no delete, 1 = delete_files, 2 =
// delete_partfile, 3 = both).
void detach_torrent(lt::session* session, lt::torrent_handle* torrent, int32_t remove_flags)
{
    if (session == nullptr || torrent == nullptr)
    {
        return;
    }

    torrent->pause();

    const auto flags = static_cast<lt::remove_flags_t>(
        static_cast<std::uint8_t>(remove_flags & 0xFF));
    session->remove_torrent(*torrent, flags);

    delete torrent;
}

// get the info for a torrent.
// the torrent_info struct is allocated on the heap and must be freed with a call to destroy_torrent_info.
torrent_metadata* get_torrent_info(lt::torrent_info* torrent)
{
    if (torrent == nullptr)
    {
        return nullptr;
    }

    auto name = torrent->name();
    auto author = torrent->creator();
    auto comment = torrent->comment();

    const auto torrent_name = new char[name.size() + 1]();
    const auto torrent_author = new char[author.size() + 1]();
    const auto torrent_comment = new char[comment.size() + 1]();

    std::ranges::copy(name, torrent_name);
    std::ranges::copy(author, torrent_author);
    std::ranges::copy(comment, torrent_comment);

    const auto info = new torrent_metadata();

    info->name = torrent_name;
    info->creator = torrent_author;
    info->comment = torrent_comment;

    info->total_files = torrent->num_files();
    info->total_size = torrent->total_size();
    info->creation_date = torrent->creation_date();

    auto hash = torrent->info_hashes();

    // fill in the info hash
    if (hash.has_v1())
    {
        std::ranges::copy(hash.v1, info->info_hash_v1);
    }
    else
    {
        std::fill_n(info->info_hash_v1, 20, 0);
    }

    // fill in the info hash v2
    if (hash.has_v2())
    {
        std::ranges::copy(hash.v2, info->info_hash_v2);
    }
    else
    {
        std::fill_n(info->info_hash_v2, 32, 0);
    }

    return info;
}

void destroy_torrent_info(torrent_metadata* info)
{
    if (info == nullptr)
    {
        return;
    }

    delete[] info->name;
    delete[] info->creator;
    delete[] info->comment;

    delete info;
}

// given a torrent handle, get the list of files in the torrent.
void get_torrent_file_list(const lt::torrent_info* torrent, torrent_file_list* file_list)
{
    if (torrent == nullptr || file_list == nullptr)
    {
        return;
    }

    const auto& files = torrent->files();

    const auto num_files = files.num_files();
    const auto list = new torrent_file_information[num_files];

    for (lt::file_index_t i(0); i != files.end_file(); i++)
    {
        auto index = static_cast<int32_t>(i);

        auto name = files.file_name(i);
        auto path = files.file_path(i);

        auto file_name = new char[name.size() + 1]();
        auto file_path = new char[path.size() + 1]();

        list[index] = {
            index,
            files.file_offset(i),
            files.file_size(i),
            files.mtime(i),
            file_name,
            file_path,
            files.file_absolute_path(i),
            static_cast<uint8_t>(files.file_flags(static_cast<lt::file_index_t>(i)))
        };

        std::ranges::copy(name, file_name);
        std::ranges::copy(path, file_path);
    }

    file_list->files = list;
    file_list->length = num_files;
}

void lts_torrent_handle_file_list(lt::torrent_handle* torrent, torrent_file_list* file_list)
{
    if (torrent == nullptr || !torrent->is_valid() || file_list == nullptr)
    {
        return;
    }
    const auto ti = torrent->torrent_file();
    if (!ti || !ti->is_valid())
    {
        return;
    }
    get_torrent_file_list(ti.get(), file_list);
}

void destroy_torrent_file_list(torrent_file_list* file_list)
{
    if (file_list == nullptr || file_list->files == nullptr)
    {
        return;
    }

    for (int i = 0; i < file_list->length; i++)
    {
        delete[] file_list->files[i].file_name;
        delete[] file_list->files[i].file_path;
    }

    delete[] file_list->files;
}

// --- file_storage scalar accessors ---------------------------------------
// All three functions share the same guard pattern: null info, invalid info,
// or invalid file_storage collapse to 0. piece_size additionally guards on
// out-of-range index. Narrowing to int32_t is safe — libtorrent pieces are
// capped at 2^30 bytes by BEP-52 and piece counts fit comfortably in int.

int32_t lts_torrent_info_piece_length(lt::torrent_info* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    return static_cast<int32_t>(torrent->piece_length());
}

int32_t lts_torrent_info_num_pieces(lt::torrent_info* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    return static_cast<int32_t>(torrent->num_pieces());
}

int32_t lts_torrent_info_piece_size(lt::torrent_info* torrent, int32_t piece_index)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    const auto count = torrent->num_pieces();
    if (piece_index < 0 || piece_index >= count)
    {
        return 0;
    }
    return static_cast<int32_t>(torrent->piece_size(static_cast<lt::piece_index_t>(piece_index)));
}

bool lts_torrent_info_hash_for_piece(lt::torrent_info* torrent, int32_t piece_index, uint8_t* out_hash20)
{
    if (torrent == nullptr || !torrent->is_valid() || out_hash20 == nullptr)
    {
        return false;
    }
    if (piece_index < 0 || piece_index >= torrent->num_pieces())
    {
        return false;
    }
    // V2-only torrents store leaves under merkle_tree(file_index_t), not as
    // SHA-1 piece hashes — surface absence to the caller as a clean false.
    if (!torrent->info_hashes().has_v1())
    {
        return false;
    }

    const auto hash = torrent->hash_for_piece(static_cast<lt::piece_index_t>(piece_index));
    std::ranges::copy(hash, out_hash20);
    return true;
}

bool lts_torrent_info_is_v2(lt::torrent_info* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return false;
    }
    // torrent_info::v2() — true for v2-only and hybrid v1+v2 torrents.
    return torrent->v2();
}

uint8_t lts_torrent_info_file_flags(lt::torrent_info* torrent, int32_t file_index)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    const auto& files = torrent->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return 0;
    }
    // file_flags_t is bitfield_flag<uint8_t>; narrow directly.
    return static_cast<uint8_t>(static_cast<std::uint8_t>(
        files.file_flags(static_cast<lt::file_index_t>(file_index))));
}

bool lts_torrent_info_file_root(lt::torrent_info* torrent, int32_t file_index, uint8_t* out_root32)
{
    if (torrent == nullptr || !torrent->is_valid() || out_root32 == nullptr)
    {
        return false;
    }
    const auto& files = torrent->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return false;
    }

    // root_ptr returns nullptr on non-V2 torrents / files without a stored
    // root. root(idx) returns a zero sha256_hash in that case — we prefer
    // root_ptr for the branch and explicit zero check.
    const auto* ptr = files.root_ptr(static_cast<lt::file_index_t>(file_index));
    if (ptr == nullptr)
    {
        return false;
    }

    // Reject all-zero roots: libtorrent returns that when the file has no V2
    // metadata (either V1-only torrent or a hybrid v1+v2 file without a
    // stored leaf). Surface absence as a clean false.
    bool any_nonzero = false;
    for (int i = 0; i < 32; i++)
    {
        const auto b = static_cast<uint8_t>(ptr[i]);
        if (b != 0)
        {
            any_nonzero = true;
            break;
        }
    }
    if (!any_nonzero)
    {
        return false;
    }

    for (int i = 0; i < 32; i++)
    {
        out_root32[i] = static_cast<uint8_t>(ptr[i]);
    }
    return true;
}

int32_t lts_torrent_info_symlink(lt::torrent_info* torrent, int32_t file_index, char* buffer, int32_t buffer_size)
{
    if (torrent == nullptr || !torrent->is_valid() || buffer == nullptr || buffer_size <= 0)
    {
        return 0;
    }
    const auto& files = torrent->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return 0;
    }

    // Only files with the symlink flag have a populated symlink target;
    // libtorrent returns an empty string otherwise.
    const auto flags = files.file_flags(static_cast<lt::file_index_t>(file_index));
    if (!(flags & lt::file_storage::flag_symlink))
    {
        return 0;
    }

    const auto target = files.symlink(static_cast<lt::file_index_t>(file_index));
    if (target.empty())
    {
        return 0;
    }

    // Truncate to buffer_size - 1 to leave room for the NUL terminator.
    const auto max_write = static_cast<int32_t>(buffer_size - 1);
    const auto len = static_cast<int32_t>(
        std::min(static_cast<std::size_t>(max_write), target.size()));
    std::copy(target.begin(), target.begin() + len, buffer);
    buffer[len] = '\0';
    return len;
}

int32_t lts_torrent_info_file_index_at_offset(lt::torrent_info* torrent, int64_t offset)
{
    if (torrent == nullptr || !torrent->is_valid() || offset < 0)
    {
        return -1;
    }
    const auto& files = torrent->files();
    if (offset >= files.total_size())
    {
        return -1;
    }
    return static_cast<int32_t>(files.file_index_at_offset(offset));
}

int32_t lts_torrent_info_file_num_pieces(lt::torrent_info* torrent, int32_t file_index)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    const auto& files = torrent->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return 0;
    }
    return files.file_num_pieces(static_cast<lt::file_index_t>(file_index));
}

int32_t lts_torrent_info_file_num_blocks(lt::torrent_info* torrent, int32_t file_index)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    const auto& files = torrent->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return 0;
    }
    return files.file_num_blocks(static_cast<lt::file_index_t>(file_index));
}

bool lts_torrent_info_file_piece_range(lt::torrent_info* torrent, int32_t file_index,
    int32_t* out_first_piece, int32_t* out_end_piece)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return false;
    }
    if (out_first_piece == nullptr || out_end_piece == nullptr)
    {
        return false;
    }
    const auto& files = torrent->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return false;
    }

    const auto range = files.file_piece_range(static_cast<lt::file_index_t>(file_index));
    *out_first_piece = static_cast<int32_t>(range._begin);
    *out_end_piece = static_cast<int32_t>(range._end);
    return true;
}

bool lts_torrent_info_map_file(lt::torrent_info* torrent, int32_t file_index, int64_t offset, int32_t size,
    int32_t* out_piece_index, int32_t* out_piece_offset, int32_t* out_length)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return false;
    }
    if (out_piece_index == nullptr || out_piece_offset == nullptr || out_length == nullptr)
    {
        return false;
    }
    if (offset < 0 || size < 0)
    {
        return false;
    }
    const auto& files = torrent->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return false;
    }

    const auto request = files.map_file(static_cast<lt::file_index_t>(file_index), offset, size);
    *out_piece_index = static_cast<int32_t>(request.piece);
    *out_piece_offset = request.start;
    *out_length = request.length;
    return true;
}

void lts_torrent_info_map_block(lt::torrent_info* torrent, int32_t piece_index, int64_t offset, int32_t size, file_slice_list* out_list)
{
    if (out_list == nullptr)
    {
        return;
    }

    out_list->length = 0;
    out_list->slices = nullptr;

    if (torrent == nullptr || !torrent->is_valid() || offset < 0 || size < 0)
    {
        return;
    }
    const auto& files = torrent->files();
    if (piece_index < 0 || piece_index >= files.num_pieces())
    {
        return;
    }

    const auto result = files.map_block(static_cast<lt::piece_index_t>(piece_index), offset, size);
    const auto count = static_cast<int32_t>(result.size());
    if (count <= 0)
    {
        return;
    }

    auto* buffer = new file_slice[count]();
    for (int32_t i = 0; i < count; ++i)
    {
        buffer[i].file_index = static_cast<int32_t>(result[i].file_index);
        buffer[i].offset = result[i].offset;
        buffer[i].size = result[i].size;
    }

    out_list->length = count;
    out_list->slices = buffer;
}

void lts_destroy_file_slice_list(file_slice_list* list)
{
    if (list == nullptr || list->slices == nullptr)
    {
        return;
    }
    delete[] list->slices;
    list->slices = nullptr;
    list->length = 0;
}

int32_t lts_torrent_info_piece_layer(lt::torrent_info* torrent, int32_t file_index, uint8_t* out_buffer, int32_t buffer_size)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    const auto& files = torrent->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return 0;
    }

    const auto layer = torrent->piece_layer(static_cast<lt::file_index_t>(file_index));
    const auto needed = static_cast<int32_t>(layer.size());
    if (needed <= 0)
    {
        return 0;
    }

    if (out_buffer == nullptr || buffer_size <= 0)
    {
        return needed;
    }

    const auto to_write = std::min(needed, buffer_size);
    std::memcpy(out_buffer, layer.data(), static_cast<std::size_t>(to_write));
    return to_write;
}

int64_t lts_torrent_info_file_mtime(lt::torrent_info* torrent, int32_t file_index)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return 0;
    }
    const auto& files = torrent->files();
    if (file_index < 0 || file_index >= files.num_files())
    {
        return 0;
    }
    // file_storage::mtime returns std::time_t — Unix epoch seconds, 0 when
    // the .torrent didn't store an mtime for this file.
    return static_cast<int64_t>(files.mtime(static_cast<lt::file_index_t>(file_index)));
}

// set the download priority for a file in a torrent.
void set_file_dl_priority(lt::torrent_handle* torrent, const int32_t file_index, const uint8_t priority)
{
    if (torrent == nullptr)
    {
        return;
    }

    torrent->file_priority(static_cast<lt::file_index_t>(file_index), static_cast<lt::download_priority_t>(priority));
}

// get the download priority for a file in a torrent.
uint8_t get_file_dl_priority(lt::torrent_handle* torrent, const int32_t file_index)
{
    if (torrent == nullptr)
    {
        return 0;
    }

    return static_cast<uint8_t>(torrent->file_priority(static_cast<lt::file_index_t>(file_index)));
}

// fills out_array with bytes downloaded for each file (indexed by file_index).
void lts_file_progress(lt::torrent_handle* torrent, int64_t* out_array, const int32_t num_files)
{
    if (torrent == nullptr || out_array == nullptr || num_files <= 0)
        return;
    std::vector<std::int64_t> progress;
    torrent->file_progress(progress);
    const auto count = static_cast<int32_t>(std::min(static_cast<std::size_t>(num_files), progress.size()));
    std::copy(progress.begin(), progress.begin() + count, out_array);
    if (count < num_files)
        std::fill(out_array + count, out_array + num_files, std::int64_t{0});
}

// start and stop the download of a torrent.
void start_torrent(lt::torrent_handle* torrent)
{
    if (torrent == nullptr)
    {
        return;
    }

    torrent->resume();
}

// start and stop the download of a torrent.
void stop_torrent(lt::torrent_handle* torrent)
{
    if (torrent == nullptr)
    {
        return;
    }

    torrent->pause();
}

void reannounce_torrent(lt::torrent_handle* torrent, const int32_t seconds, const uint8_t ignore_min_interval)
{
    if (torrent == nullptr)
    {
        return;
    }

    lt::reannounce_flags_t flags = {};

    if (ignore_min_interval)
    {
        flags |= lt::torrent_handle::ignore_min_interval;
    }

    torrent->force_reannounce(seconds, -1, flags);
}

// Sends a scrape request to all of the torrent's trackers. Completes asynchronously:
// success fires cs_scrape_reply_alert, failure fires cs_scrape_failed_alert.
// The second parameter (`idx`) defaults to -1 = all trackers.
void lts_scrape_tracker(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return;
    }

    torrent->scrape_tracker();
}

// get the progress of a torrent. Heap-allocated — caller releases via lts_destroy_torrent_status.
torrent_status* lts_get_torrent_status(lt::torrent_handle* torrent)
{
    if (torrent == nullptr || !torrent->is_valid())
    {
        return nullptr;
    }

    const auto s = torrent->status();
    const auto out = new torrent_status{};

    if (s.errc != lt::error_code())
    {
        out->state = cs_torrent_state::torrent_error;
    }
    else
    {
        switch (s.state)
        {
        case lt::torrent_status::state_t::checking_files:
            out->state = cs_torrent_state::torrent_checking;
            break;

        case lt::torrent_status::state_t::checking_resume_data:
            out->state = cs_torrent_state::torrent_checking_resume;
            break;

        case lt::torrent_status::state_t::downloading_metadata:
            out->state = cs_torrent_state::torrent_metadata_downloading;
            break;

        case lt::torrent_status::state_t::downloading:
            out->state = cs_torrent_state::torrent_downloading;
            break;

        case lt::torrent_status::state_t::seeding:
            out->state = cs_torrent_state::torrent_seeding;
            break;

        case lt::torrent_status::state_t::finished:
            out->state = cs_torrent_state::torrent_finished;
            break;

        default:
            out->state = cs_torrent_state::torrent_state_unknown;
            break;
        }
    }

    out->progress = s.progress;

    out->count_peers = s.num_peers;
    out->count_seeds = s.num_seeds;

    out->bytes_uploaded = s.total_payload_upload;
    out->bytes_downloaded = s.total_payload_download;

    out->upload_rate = s.upload_payload_rate;
    out->download_rate = s.download_payload_rate;

    out->all_time_upload = s.all_time_upload;
    out->all_time_download = s.all_time_download;

    out->active_duration_seconds = std::chrono::duration_cast<std::chrono::seconds>(s.active_duration).count();
    out->finished_duration_seconds = std::chrono::duration_cast<std::chrono::seconds>(s.finished_duration).count();
    out->seeding_duration_seconds = std::chrono::duration_cast<std::chrono::seconds>(s.seeding_duration).count();

    const auto remaining = s.total_wanted - s.total_wanted_done;
    if (s.download_payload_rate > 0 && remaining > 0)
    {
        out->eta_seconds = remaining / s.download_payload_rate;
    }
    else
    {
        out->eta_seconds = -1;
    }

    if (s.all_time_download > 0)
    {
        out->ratio = static_cast<float>(s.all_time_upload) / static_cast<float>(s.all_time_download);
    }
    else
    {
        out->ratio = -1.0f;
    }

    out->flags = static_cast<uint64_t>(static_cast<std::uint64_t>(s.flags));

    out->save_path = clone_cstr(s.save_path);
    out->error_string = clone_cstr(s.errc ? s.errc.message() : std::string());

    return out;
}

void lts_destroy_torrent_status(torrent_status* status)
{
    if (status == nullptr)
    {
        return;
    }

    delete[] status->save_path;
    delete[] status->error_string;
    delete status;
}

namespace {

// Sentinel exception used to bail out of lt::set_piece_hashes when the managed
// side flips the cancel flag mid-hash. We catch it explicitly at the top of
// lts_create_torrent and translate to the cancelled return code; the .torrent
// file is never written, so the caller-visible state is "as if you never
// called us".
struct create_torrent_cancelled : std::exception
{
    const char* what() const noexcept override { return "cancelled"; }
};

void copy_error(char* error_buf, int32_t error_buf_size, const std::string& message)
{
    if (error_buf == nullptr || error_buf_size <= 0)
    {
        return;
    }

    const std::size_t cap = static_cast<std::size_t>(error_buf_size - 1);
    const std::size_t n = std::min(cap, message.size());
    std::memcpy(error_buf, message.data(), n);
    error_buf[n] = '\0';
}

// Filters hidden files and directories (names starting with '.') as libtorrent's
// ignore_hidden convention defines.
bool not_hidden_filter(const std::string& path)
{
    if (path.empty()) return true;
    auto sep = path.find_last_of("/\\");
    const std::string name = (sep == std::string::npos) ? path : path.substr(sep + 1);
    return name.empty() || name.front() != '.';
}

// Splits trackers wire format (newline-separated URLs, blank line = tier++)
// directly into create_torrent.add_tracker calls. Trims CR for CRLF inputs.
void apply_trackers(lt::create_torrent& ct, const char* trackers)
{
    if (trackers == nullptr) return;
    int tier = 0;
    const char* p = trackers;
    while (true)
    {
        const char* eol = std::strchr(p, '\n');
        const std::size_t len = (eol == nullptr) ? std::strlen(p) : static_cast<std::size_t>(eol - p);
        std::string line(p, len);
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty())
        {
            ++tier;
        }
        else
        {
            ct.add_tracker(line, tier);
        }
        if (eol == nullptr) break;
        p = eol + 1;
    }
}

void apply_web_seeds(lt::create_torrent& ct, const char* web_seeds)
{
    if (web_seeds == nullptr) return;
    const char* p = web_seeds;
    while (true)
    {
        const char* eol = std::strchr(p, '\n');
        const std::size_t len = (eol == nullptr) ? std::strlen(p) : static_cast<std::size_t>(eol - p);
        std::string line(p, len);
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (!line.empty()) ct.add_url_seed(line);
        if (eol == nullptr) break;
        p = eol + 1;
    }
}

// Resolves the parent directory of source_path. lt::set_piece_hashes wants the
// directory CONTAINING the torrent root (file or folder). Strips the trailing
// separator and last path component. Falls back to "." when the input has no
// separator (relative basename like "foo.bin").
std::string parent_path_of(const std::string& source_path)
{
    if (source_path.empty()) return ".";
    std::string s = source_path;
    while (!s.empty() && (s.back() == '/' || s.back() == '\\'))
    {
        s.pop_back();
    }
    auto sep = s.find_last_of("/\\");
    if (sep == std::string::npos) return ".";
    if (sep == 0) return s.substr(0, 1);
    return s.substr(0, sep);
}

} // namespace

int32_t lts_create_torrent(
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
    int32_t error_buf_size)
{
    if (source_path == nullptr || output_path == nullptr ||
        source_path[0] == '\0' || output_path[0] == '\0')
    {
        copy_error(error_buf, error_buf_size, "source_path and output_path are required");
        return -1;
    }

    try
    {
        lt::file_storage fs;
        if (ignore_hidden)
        {
            lt::add_files(fs, source_path, not_hidden_filter);
        }
        else
        {
            lt::add_files(fs, source_path);
        }

        if (fs.num_files() == 0)
        {
            copy_error(error_buf, error_buf_size, "source path contains no files");
            return -2;
        }

        lt::create_torrent ct(fs, piece_size);

        apply_trackers(ct, trackers);
        apply_web_seeds(ct, web_seeds);

        if (comment != nullptr && comment[0] != '\0')
        {
            ct.set_comment(comment);
        }
        if (created_by != nullptr && created_by[0] != '\0')
        {
            ct.set_creator(created_by);
        }
        if (is_private)
        {
            ct.set_priv(true);
        }

        // Initial 0/N progress fire so the managed side can populate OverallSize
        // before any hashing starts. piece_size + total_size are immutable for
        // the run, so a single emission is enough.
        if (progress_cb != nullptr)
        {
            progress_cb(0, ct.num_pieces(), ct.piece_length(), fs.total_size(), progress_ctx);
        }

        const std::string parent = parent_path_of(source_path);

        lt::set_piece_hashes(ct, parent,
            [&](lt::piece_index_t p) {
                if (cancel_flag != nullptr && *cancel_flag != 0)
                {
                    throw create_torrent_cancelled{};
                }
                if (progress_cb != nullptr)
                {
                    // p is a strong int; static_cast extracts the underlying value.
                    const int64_t current = static_cast<int32_t>(p) + 1;
                    progress_cb(current, ct.num_pieces(), ct.piece_length(), fs.total_size(), progress_ctx);
                }
            });

        if (cancel_flag != nullptr && *cancel_flag != 0)
        {
            return -5;
        }

        lt::entry e = ct.generate();
        std::vector<char> buf;
        lt::bencode(std::back_inserter(buf), e);

        // Only touch the filesystem AFTER hashing + bencoding succeed — that way
        // a cancellation or libtorrent throw leaves no partial output behind, which
        // the cancellation contract depends on.
        FILE* f = nullptr;
#if defined(_WIN32)
        if (fopen_s(&f, output_path, "wb") != 0) f = nullptr;
#else
        f = std::fopen(output_path, "wb");
#endif
        if (f == nullptr)
        {
            copy_error(error_buf, error_buf_size, "could not open output_path for writing");
            return -4;
        }

        const std::size_t written = std::fwrite(buf.data(), 1, buf.size(), f);
        const int close_rc = std::fclose(f);
        if (written != buf.size() || close_rc != 0)
        {
            std::remove(output_path);
            copy_error(error_buf, error_buf_size, "failed to write .torrent file");
            return -4;
        }

        return 0;
    }
    catch (const create_torrent_cancelled&)
    {
        return -5;
    }
    catch (const std::exception& ex)
    {
        copy_error(error_buf, error_buf_size, ex.what());
        return -3;
    }
    catch (...)
    {
        copy_error(error_buf, error_buf_size, "unknown error");
        return -3;
    }
}

}
