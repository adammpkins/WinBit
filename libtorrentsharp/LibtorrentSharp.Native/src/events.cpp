// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
//
// events.cpp - handles alert callbacks
// Created by Albie on 04/03/2024.
//

#include "events.h"
#include "locks.hpp"

#include <ctime>
#include <mutex>
#include <vector>
#include <libtorrent/session.hpp>
#include <libtorrent/alert_types.hpp>
#include <libtorrent/write_resume_data.hpp>

// Mirrors library.cpp's helper of the same shape — kept local to events.cpp
// to avoid a header just for a 12-line conversion. v4 addresses become
// v4-mapped v6 (::ffff:0:0/96); v6 addresses are copied through verbatim.
static void fill_v6_mapped_addr(const boost::asio::ip::address& addr, char* buffer) {
    if (addr.is_v6()) {
        auto bytes = addr.to_v6().to_bytes();
        std::copy(bytes.begin(), bytes.end(), buffer);
        return;
    }
    auto v4_bytes = addr.to_v4().to_bytes();
    std::fill(buffer, buffer + 10, char{0});
    buffer[10] = static_cast<char>(0xff);
    buffer[11] = static_cast<char>(0xff);
    std::copy(v4_bytes.begin(), v4_bytes.end(), buffer + 12);
}

void fill_info_hash(const lt::info_hash_t &hashes, char* buffer) {
    // fill in the info hash
    if (hashes.has_v1()) {
        std::copy(hashes.v1.begin(), hashes.v1.end(), buffer);
    } else {
        std::fill(buffer, buffer + 20, 0xFF);
    }
}

void fill_event_info(cs_alert* alert, lt::alert* lt_alert, cs_alert_type alert_type, std::string* message_temp) {
    alert->type = alert_type;

    alert->epoch = time(nullptr);
    alert->category = (int32_t) static_cast<uint32_t>(lt_alert->category());

    message_temp->append(lt_alert->message());
    alert->message = message_temp->c_str();
}

void populate_peer_alert(cs_peer_alert* peer_alert, lt::peer_alert* alert, cs_peer_alert_type alert_type, std::string* message) {
    fill_event_info(&peer_alert->alert, alert, cs_alert_type::alert_peer_notification, message);

    peer_alert->type = alert_type;
    peer_alert->handle = &alert->handle;

    // address::to_v6() throws bad_address_cast when the address is v4 — and
    // every loopback or LAN-routed peer is v4. The throw escapes this
    // callback into libtorrent's dispatcher thread and reliably crashes
    // the process. fill_v6_mapped_addr takes the safe path: v4 addresses
    // become ::ffff:a.b.c.d, v6 are copied through verbatim.
    fill_v6_mapped_addr(alert->endpoint.address(), peer_alert->ipv6_address);

    fill_info_hash(alert->handle.info_hashes(), peer_alert->info_hash);

    // peer_id::pid is a 20-byte identifier the peer chose itself (often
    // the first 8 bytes are the client's signature, e.g. "-qB4660-..."),
    // distinct from the SHA-1 info_hash. Surfacing it lets UI columns
    // attribute traffic to specific clients/users.
    std::memcpy(peer_alert->peer_id, alert->pid.data(), 20);
}

void on_events_available(lt::session* session, cs_alert_callback callback, bool include_unmapped) {
    // Process-wide blocking mutex serializes ALL alert dispatches. The
    // original csdl code used `try_lock`, which silently DROPPED alerts
    // whenever two concurrent dispatchers (two LibtorrentSession instances
    // in the same process — loopback test fixtures, multi-session apps)
    // raced. Blocking serializes them instead, so no alerts are lost.
    // Per-session mutexes were tried but caused process-exit races due to
    // the registry map's destruction order.
    static std::mutex dispatch_mutex;
    std::lock_guard<std::mutex> dispatch_guard(dispatch_mutex);

    std::vector<lt::alert*> events;
    std::string message_temp;

    session->pop_alerts(&events);

    handle_events:
    for (auto &alert: events) {
        switch (alert->type()) {

            // torrent state changed
            case lt::state_changed_alert::alert_type: {
                auto state_alert = lt::alert_cast<lt::state_changed_alert>(alert);
                cs_torrent_status_alert status_alert{};

                status_alert.new_state = state_alert->state;
                status_alert.old_state = state_alert->prev_state;

                fill_info_hash(state_alert->handle.info_hashes(), status_alert.info_hash);
                fill_event_info(&status_alert.alert, alert, cs_alert_type::alert_torrent_status, &message_temp);
                callback(&status_alert);
                break;
            }

                // torrent removed
            case lt::torrent_removed_alert::alert_type: {
                auto removed_alert = lt::alert_cast<lt::torrent_removed_alert>(alert);
                cs_torrent_remove_alert removed_torrent{};

                // can't use handle as it's most likely been invalidated.
                fill_info_hash(removed_alert->info_hashes, removed_torrent.info_hash);
                fill_event_info(&removed_torrent.alert, alert, cs_alert_type::alert_torrent_removed, &message_temp);
                callback(&removed_torrent);
                break;
            }

                // torrent paused
            case lt::torrent_paused_alert::alert_type: {
                auto paused_alert = lt::alert_cast<lt::torrent_paused_alert>(alert);
                cs_torrent_paused_alert paused{};

                fill_info_hash(paused_alert->handle.info_hashes(), paused.info_hash);
                fill_event_info(&paused.alert, alert, cs_alert_type::alert_torrent_paused, &message_temp);
                callback(&paused);
                break;
            }

                // torrent resumed
            case lt::torrent_resumed_alert::alert_type: {
                auto resumed_alert = lt::alert_cast<lt::torrent_resumed_alert>(alert);
                cs_torrent_resumed_alert resumed{};

                fill_info_hash(resumed_alert->handle.info_hashes(), resumed.info_hash);
                fill_event_info(&resumed.alert, alert, cs_alert_type::alert_torrent_resumed, &message_temp);
                callback(&resumed);
                break;
            }

                // torrent finished
            case lt::torrent_finished_alert::alert_type: {
                auto finished_alert = lt::alert_cast<lt::torrent_finished_alert>(alert);
                cs_torrent_finished_alert finished{};

                fill_info_hash(finished_alert->handle.info_hashes(), finished.info_hash);
                fill_event_info(&finished.alert, alert, cs_alert_type::alert_torrent_finished, &message_temp);
                callback(&finished);
                break;
            }

                // torrent checked (initial hash check or force_recheck)
            case lt::torrent_checked_alert::alert_type: {
                auto checked_alert = lt::alert_cast<lt::torrent_checked_alert>(alert);
                cs_torrent_checked_alert checked{};

                fill_info_hash(checked_alert->handle.info_hashes(), checked.info_hash);
                fill_event_info(&checked.alert, alert, cs_alert_type::alert_torrent_checked, &message_temp);
                callback(&checked);
                break;
            }

                // storage_moved — move_storage completed
            case lt::storage_moved_alert::alert_type: {
                auto moved_alert = lt::alert_cast<lt::storage_moved_alert>(alert);

                std::string path_str = moved_alert->storage_path();
                std::string old_path_str = moved_alert->old_path();

                cs_storage_moved_alert moved{};
                fill_info_hash(moved_alert->handle.info_hashes(), moved.info_hash);
                moved.storage_path = path_str.c_str();
                moved.old_path = old_path_str.c_str();

                fill_event_info(&moved.alert, alert, cs_alert_type::alert_storage_moved, &message_temp);
                callback(&moved);
                break;
            }

                // storage_moved_failed — move_storage failed
            case lt::storage_moved_failed_alert::alert_type: {
                auto fail_alert = lt::alert_cast<lt::storage_moved_failed_alert>(alert);

                std::string file_str = fail_alert->file_path();
                std::string error_msg = fail_alert->error.message();

                cs_storage_moved_failed_alert failed{};
                fill_info_hash(fail_alert->handle.info_hashes(), failed.info_hash);
                failed.error_code = fail_alert->error.value();
                failed.file_path = file_str.c_str();
                failed.error_message = error_msg.c_str();

                fill_event_info(&failed.alert, alert, cs_alert_type::alert_storage_moved_failed, &message_temp);
                callback(&failed);
                break;
            }

                // tracker_reply — announce succeeded
            case lt::tracker_reply_alert::alert_type: {
                auto reply = lt::alert_cast<lt::tracker_reply_alert>(alert);

                std::string url_str = reply->tracker_url() != nullptr ? reply->tracker_url() : "";

                cs_tracker_reply_alert ta{};
                fill_info_hash(reply->handle.info_hashes(), ta.info_hash);
                ta.num_peers = reply->num_peers;
                ta.tracker_url = url_str.c_str();

                fill_event_info(&ta.alert, alert, cs_alert_type::alert_tracker_reply, &message_temp);
                callback(&ta);
                break;
            }

                // tracker_error — announce failed (scrape failures go through scrape_failed_alert)
            case lt::tracker_error_alert::alert_type: {
                auto err = lt::alert_cast<lt::tracker_error_alert>(alert);

                std::string url_str = err->tracker_url() != nullptr ? err->tracker_url() : "";
                std::string err_msg = err->error_message() != nullptr ? err->error_message() : "";

                cs_tracker_error_alert ta{};
                fill_info_hash(err->handle.info_hashes(), ta.info_hash);
                ta.error_code = err->error.value();
                ta.times_in_row = err->times_in_row;
                ta.tracker_url = url_str.c_str();
                ta.error_message = err_msg.c_str();

                fill_event_info(&ta.alert, alert, cs_alert_type::alert_tracker_error, &message_temp);
                callback(&ta);
                break;
            }

                // scrape_reply — scrape succeeded
            case lt::scrape_reply_alert::alert_type: {
                auto reply = lt::alert_cast<lt::scrape_reply_alert>(alert);

                std::string url_str = reply->tracker_url() != nullptr ? reply->tracker_url() : "";

                cs_scrape_reply_alert sa{};
                fill_info_hash(reply->handle.info_hashes(), sa.info_hash);
                sa.incomplete = reply->incomplete;
                sa.complete = reply->complete;
                sa.tracker_url = url_str.c_str();

                fill_event_info(&sa.alert, alert, cs_alert_type::alert_scrape_reply, &message_temp);
                callback(&sa);
                break;
            }

                // scrape_failed — scrape request failed
            case lt::scrape_failed_alert::alert_type: {
                auto fail = lt::alert_cast<lt::scrape_failed_alert>(alert);

                std::string url_str = fail->tracker_url() != nullptr ? fail->tracker_url() : "";
                std::string err_msg = fail->error_message() != nullptr ? fail->error_message() : "";

                cs_scrape_failed_alert sa{};
                fill_info_hash(fail->handle.info_hashes(), sa.info_hash);
                sa.error_code = fail->error.value();
                sa.tracker_url = url_str.c_str();
                sa.error_message = err_msg.c_str();

                fill_event_info(&sa.alert, alert, cs_alert_type::alert_scrape_failed, &message_temp);
                callback(&sa);
                break;
            }

                // tracker_announce — an announce request was sent to a tracker
            case lt::tracker_announce_alert::alert_type: {
                auto announce = lt::alert_cast<lt::tracker_announce_alert>(alert);

                std::string url_str = announce->tracker_url() != nullptr ? announce->tracker_url() : "";

                cs_tracker_announce_alert ta{};
                fill_info_hash(announce->handle.info_hashes(), ta.info_hash);
                ta.event = static_cast<int32_t>(announce->event);
                ta.tracker_url = url_str.c_str();

                fill_event_info(&ta.alert, alert, cs_alert_type::alert_tracker_announce, &message_temp);
                callback(&ta);
                break;
            }

                // tracker_warning — tracker attached an advisory message to its reply
            case lt::tracker_warning_alert::alert_type: {
                auto warn = lt::alert_cast<lt::tracker_warning_alert>(alert);

                std::string url_str = warn->tracker_url() != nullptr ? warn->tracker_url() : "";
                std::string msg_str = warn->warning_message() != nullptr ? warn->warning_message() : "";

                cs_tracker_warning_alert ta{};
                fill_info_hash(warn->handle.info_hashes(), ta.info_hash);
                ta.tracker_url = url_str.c_str();
                ta.warning_message = msg_str.c_str();

                fill_event_info(&ta.alert, alert, cs_alert_type::alert_tracker_warning, &message_temp);
                callback(&ta);
                break;
            }

                // file_renamed — rename_file() succeeded
            case lt::file_renamed_alert::alert_type: {
                auto renamed = lt::alert_cast<lt::file_renamed_alert>(alert);

                std::string new_name_str = renamed->new_name() != nullptr ? renamed->new_name() : "";

                cs_file_renamed_alert fr{};
                fill_info_hash(renamed->handle.info_hashes(), fr.info_hash);
                fr.file_index = static_cast<int32_t>(renamed->index);
                fr.new_name = new_name_str.c_str();

                fill_event_info(&fr.alert, alert, cs_alert_type::alert_file_renamed, &message_temp);
                callback(&fr);
                break;
            }

                // file_rename_failed — rename_file() failed
            case lt::file_rename_failed_alert::alert_type: {
                auto fail = lt::alert_cast<lt::file_rename_failed_alert>(alert);

                std::string err_msg = fail->error.message();

                cs_file_rename_failed_alert fr{};
                fill_info_hash(fail->handle.info_hashes(), fr.info_hash);
                fr.file_index = static_cast<int32_t>(fail->index);
                fr.error_code = fail->error.value();
                fr.error_message = err_msg.c_str();

                fill_event_info(&fr.alert, alert, cs_alert_type::alert_file_rename_failed, &message_temp);
                callback(&fr);
                break;
            }

                // fastresume_rejected — add_torrent resume data was rejected
            case lt::fastresume_rejected_alert::alert_type: {
                auto rej = lt::alert_cast<lt::fastresume_rejected_alert>(alert);

                std::string err_msg = rej->error.message();

                cs_fastresume_rejected_alert fr{};
                fill_info_hash(rej->handle.info_hashes(), fr.info_hash);
                fr.error_code = rej->error.value();
                fr.error_message = err_msg.c_str();

                fill_event_info(&fr.alert, alert, cs_alert_type::alert_fastresume_rejected, &message_temp);
                callback(&fr);
                break;
            }

                // save_resume_data_failed — save_resume_data() failed
            case lt::save_resume_data_failed_alert::alert_type: {
                auto fail = lt::alert_cast<lt::save_resume_data_failed_alert>(alert);

                std::string err_msg = fail->error.message();

                cs_save_resume_data_failed_alert sr{};
                fill_info_hash(fail->handle.info_hashes(), sr.info_hash);
                sr.error_code = fail->error.value();
                sr.error_message = err_msg.c_str();

                fill_event_info(&sr.alert, alert, cs_alert_type::alert_save_resume_data_failed, &message_temp);
                callback(&sr);
                break;
            }

                // torrent_deleted — remove-with-delete_files completed. The
                // handle is invalid by this point; the info_hash copy comes
                // from the alert's own member (libtorrent holds it even after
                // the torrent object is gone).
            case lt::torrent_deleted_alert::alert_type: {
                auto del = lt::alert_cast<lt::torrent_deleted_alert>(alert);

                cs_torrent_deleted_alert td{};
                std::copy(del->info_hashes.v1.begin(), del->info_hashes.v1.end(), td.info_hash);

                fill_event_info(&td.alert, alert, cs_alert_type::alert_torrent_deleted, &message_temp);
                callback(&td);
                break;
            }

                // torrent_delete_failed — remove-with-delete_files failed.
                // Same handle-is-gone caveat as torrent_deleted_alert — copy
                // info_hash from the alert's own member rather than the handle.
            case lt::torrent_delete_failed_alert::alert_type: {
                auto fail = lt::alert_cast<lt::torrent_delete_failed_alert>(alert);

                std::string err_msg = fail->error.message();

                cs_torrent_delete_failed_alert tdf{};
                std::copy(fail->info_hashes.v1.begin(), fail->info_hashes.v1.end(), tdf.info_hash);
                tdf.error_code = fail->error.value();
                tdf.error_message = err_msg.c_str();

                fill_event_info(&tdf.alert, alert, cs_alert_type::alert_torrent_delete_failed, &message_temp);
                callback(&tdf);
                break;
            }

                // metadata_received — magnet metadata fetched from the swarm
            case lt::metadata_received_alert::alert_type: {
                auto recv = lt::alert_cast<lt::metadata_received_alert>(alert);

                cs_metadata_received_alert mr{};
                fill_info_hash(recv->handle.info_hashes(), mr.info_hash);

                fill_event_info(&mr.alert, alert, cs_alert_type::alert_metadata_received, &message_temp);
                callback(&mr);
                break;
            }

                // metadata_failed — received metadata was malformed / rejected
            case lt::metadata_failed_alert::alert_type: {
                auto fail = lt::alert_cast<lt::metadata_failed_alert>(alert);

                std::string err_msg = fail->error.message();

                cs_metadata_failed_alert mf{};
                fill_info_hash(fail->handle.info_hashes(), mf.info_hash);
                mf.error_code = fail->error.value();
                mf.error_message = err_msg.c_str();

                fill_event_info(&mf.alert, alert, cs_alert_type::alert_metadata_failed, &message_temp);
                callback(&mf);
                break;
            }

                // torrent_error — sticky torrent-level error
            case lt::torrent_error_alert::alert_type: {
                auto err = lt::alert_cast<lt::torrent_error_alert>(alert);

                std::string filename_str = err->filename() != nullptr ? err->filename() : "";
                std::string err_msg = err->error.message();

                cs_torrent_error_alert te{};
                fill_info_hash(err->handle.info_hashes(), te.info_hash);
                te.error_code = err->error.value();
                te.filename = filename_str.c_str();
                te.error_message = err_msg.c_str();

                fill_event_info(&te.alert, alert, cs_alert_type::alert_torrent_error, &message_temp);
                callback(&te);
                break;
            }

                // file_error — per-file I/O error (transient)
            case lt::file_error_alert::alert_type: {
                auto err = lt::alert_cast<lt::file_error_alert>(alert);

                std::string filename_str = err->filename() != nullptr ? err->filename() : "";
                std::string err_msg = err->error.message();

                cs_file_error_alert fe{};
                fill_info_hash(err->handle.info_hashes(), fe.info_hash);
                fe.error_code = err->error.value();
                fe.op = static_cast<int32_t>(err->op);
                fe.filename = filename_str.c_str();
                fe.error_message = err_msg.c_str();

                fill_event_info(&fe.alert, alert, cs_alert_type::alert_file_error, &message_temp);
                callback(&fe);
                break;
            }

                // udp_error — UDP socket failure (DHT / UPnP / UDP tracker / uTP)
            case lt::udp_error_alert::alert_type: {
                auto err = lt::alert_cast<lt::udp_error_alert>(alert);

                std::string err_msg = err->error.message();

                cs_udp_error_alert ue{};
                fill_v6_mapped_addr(err->endpoint.address(), ue.endpoint_address);
                ue.endpoint_port = err->endpoint.port();
                ue.operation = static_cast<int32_t>(err->operation);
                ue.error_code = err->error.value();
                ue.error_message = err_msg.c_str();

                fill_event_info(&ue.alert, alert, cs_alert_type::alert_udp_error, &message_temp);
                callback(&ue);
                break;
            }

                // session_error — catastrophic session-level failure
            case lt::session_error_alert::alert_type: {
                auto err = lt::alert_cast<lt::session_error_alert>(alert);

                std::string err_msg = err->error.message();

                cs_session_error_alert se{};
                se.error_code = err->error.value();
                se.error_message = err_msg.c_str();

                fill_event_info(&se.alert, alert, cs_alert_type::alert_session_error, &message_temp);
                callback(&se);
                break;
            }

                // dht_error — DHT subsystem failure
            case lt::dht_error_alert::alert_type: {
                auto err = lt::alert_cast<lt::dht_error_alert>(alert);

                std::string err_msg = err->error.message();

                cs_dht_error_alert de{};
                de.operation = static_cast<int32_t>(err->op);
                de.error_code = err->error.value();
                de.error_message = err_msg.c_str();

                fill_event_info(&de.alert, alert, cs_alert_type::alert_dht_error, &message_temp);
                callback(&de);
                break;
            }

                // lsd_error — Local Service Discovery failure on an interface
            case lt::lsd_error_alert::alert_type: {
                auto err = lt::alert_cast<lt::lsd_error_alert>(alert);

                std::string err_msg = err->error.message();

                cs_lsd_error_alert le{};
                fill_v6_mapped_addr(err->local_address, le.local_address);
                le.error_code = err->error.value();
                le.error_message = err_msg.c_str();

                fill_event_info(&le.alert, alert, cs_alert_type::alert_lsd_error, &message_temp);
                callback(&le);
                break;
            }

                // hash_failed — piece hash verification failed during download
            case lt::hash_failed_alert::alert_type: {
                auto fail = lt::alert_cast<lt::hash_failed_alert>(alert);

                cs_hash_failed_alert hf{};
                fill_info_hash(fail->handle.info_hashes(), hf.info_hash);
                hf.piece_index = static_cast<int32_t>(fail->piece_index);

                fill_event_info(&hf.alert, alert, cs_alert_type::alert_hash_failed, &message_temp);
                callback(&hf);
                break;
            }

                // external_ip — libtorrent learned the machine's public IP
            case lt::external_ip_alert::alert_type: {
                auto ip = lt::alert_cast<lt::external_ip_alert>(alert);

                cs_external_ip_alert ea{};
                fill_v6_mapped_addr(ip->external_address, ea.external_address);

                fill_event_info(&ea.alert, alert, cs_alert_type::alert_external_ip, &message_temp);
                callback(&ea);
                break;
            }

                // performance warning
            case lt::performance_alert::alert_type: {
                auto perf_alert = lt::alert_cast<lt::performance_alert>(alert);
                cs_client_performance_alert perf_warning{};

                perf_warning.warning_type = perf_alert->warning_code;

                fill_event_info(&perf_warning.alert, alert, cs_alert_type::alert_client_performance, &message_temp);
                callback(&perf_warning);
                break;
            }

                // peer connected
            case lt::peer_connect_alert::alert_type: {
                auto peer_alert = lt::alert_cast<lt::peer_connect_alert>(alert);
                if (!peer_alert) { break; }
                auto direction = (peer_alert->direction == lt::peer_connect_alert::direction_t::in) ? cs_peer_alert_type::connected_in : cs_peer_alert_type::connected_out;

                cs_peer_alert peer_connected{};

                populate_peer_alert(&peer_connected, peer_alert, direction, &message_temp);
                callback(&peer_connected);
                break;
            }

                // peer disconnected
            case lt::peer_disconnected_alert::alert_type: {
                auto peer_alert = lt::alert_cast<lt::peer_disconnected_alert>(alert);
                if (!peer_alert) { break; }
                cs_peer_alert peer_disconnected{};

                populate_peer_alert(&peer_disconnected, peer_alert, cs_peer_alert_type::disconnected, &message_temp);
                callback(&peer_disconnected);
                break;
            }

                // peer banned
            case lt::peer_ban_alert::alert_type: {
                auto peer_alert = lt::alert_cast<lt::peer_ban_alert>(alert);
                if (!peer_alert) { break; }
                cs_peer_alert peer_banned{};

                populate_peer_alert(&peer_banned, peer_alert, cs_peer_alert_type::banned, &message_temp);
                callback(&peer_banned);
                break;
            }

                // peer snubbed
            case lt::peer_snubbed_alert::alert_type: {
                auto peer_alert = lt::alert_cast<lt::peer_snubbed_alert>(alert);
                if (!peer_alert) { break; }
                cs_peer_alert peer_snubbed{};

                populate_peer_alert(&peer_snubbed, peer_alert, cs_peer_alert_type::snubbed, &message_temp);
                callback(&peer_snubbed);
                break;
            }

                // peer unsnubbed
            case lt::peer_unsnubbed_alert::alert_type: {
                auto peer_alert = lt::alert_cast<lt::peer_unsnubbed_alert>(alert);
                if (!peer_alert) { break; }
                cs_peer_alert peer_unsnubbed{};

                populate_peer_alert(&peer_unsnubbed, peer_alert, cs_peer_alert_type::unsnubbed, &message_temp);
                callback(&peer_unsnubbed);
                break;
            }

                // peer errored
            case lt::peer_error_alert::alert_type: {
                auto peer_alert = lt::alert_cast<lt::peer_error_alert>(alert);
                if (!peer_alert) { break; }
                cs_peer_alert peer_errored{};

                populate_peer_alert(&peer_errored, peer_alert, cs_peer_alert_type::errored, &message_temp);
                callback(&peer_errored);
                break;
            }

                // port-mapping added / updated
            case lt::portmap_alert::alert_type: {
                auto pm_alert = lt::alert_cast<lt::portmap_alert>(alert);

                uint8_t protocol = pm_alert->map_protocol == lt::portmap_protocol::tcp ? 0
                                 : pm_alert->map_protocol == lt::portmap_protocol::udp ? 1
                                 : 0xff;
                uint8_t transport = pm_alert->map_transport == lt::portmap_transport::natpmp ? 0 : 1;

                record_portmap_success(session, static_cast<int>(pm_alert->mapping), pm_alert->external_port, protocol, transport);

                cs_portmap_alert pm{};
                pm.mapping = static_cast<int32_t>(pm_alert->mapping);
                pm.external_port = pm_alert->external_port;
                pm.map_protocol = protocol;
                pm.map_transport = transport;
                fill_v6_mapped_addr(pm_alert->local_address, pm.local_address);

                fill_event_info(&pm.alert, alert, cs_alert_type::alert_portmap, &message_temp);
                callback(&pm);
                break;
            }

                // port-mapping failed
            case lt::portmap_error_alert::alert_type: {
                auto pm_err = lt::alert_cast<lt::portmap_error_alert>(alert);

                uint8_t transport = pm_err->map_transport == lt::portmap_transport::natpmp ? 0 : 1;
                std::string err_msg = pm_err->error.message();

                record_portmap_error(session, static_cast<int>(pm_err->mapping), transport, err_msg.c_str());

                cs_portmap_error_alert pe{};
                pe.mapping = static_cast<int32_t>(pm_err->mapping);
                pe.map_transport = transport;
                fill_v6_mapped_addr(pm_err->local_address, pe.local_address);
                pe.error_code = pm_err->error.value();
                pe.error_message = err_msg.c_str();

                fill_event_info(&pe.alert, alert, cs_alert_type::alert_portmap_error, &message_temp);
                callback(&pe);
                break;
            }

                // dht_bootstrap — session-level, fires once when the initial
                // DHT bootstrap completes
            case lt::dht_bootstrap_alert::alert_type: {
                cs_dht_bootstrap_alert db{};
                fill_event_info(&db.alert, alert, cs_alert_type::alert_dht_bootstrap, &message_temp);
                callback(&db);
                break;
            }

                // dht_reply — a DHT node returned peers for a torrent lookup
            case lt::dht_reply_alert::alert_type: {
                auto reply = lt::alert_cast<lt::dht_reply_alert>(alert);

                cs_dht_reply_alert dr{};
                fill_info_hash(reply->handle.info_hashes(), dr.info_hash);
                dr.num_peers = reply->num_peers;

                fill_event_info(&dr.alert, alert, cs_alert_type::alert_dht_reply, &message_temp);
                callback(&dr);
                break;
            }

                // trackerid — a tracker announce response included a
                // trackerid cookie (BEP 3)
            case lt::trackerid_alert::alert_type: {
                auto tid = lt::alert_cast<lt::trackerid_alert>(alert);

                std::string tracker_url = tid->tracker_url();
                std::string tracker_id = tid->tracker_id();

                cs_trackerid_alert ta{};
                fill_info_hash(tid->handle.info_hashes(), ta.info_hash);
                ta.tracker_url = tracker_url.c_str();
                ta.tracker_id = tracker_id.c_str();

                fill_event_info(&ta.alert, alert, cs_alert_type::alert_trackerid, &message_temp);
                callback(&ta);
                break;
            }

                // cache_flushed — a torrent's outstanding disk writes have
                // been flushed (via flush_cache() or on torrent removal)
            case lt::cache_flushed_alert::alert_type: {
                auto flush = lt::alert_cast<lt::cache_flushed_alert>(alert);

                cs_cache_flushed_alert cf{};
                fill_info_hash(flush->handle.info_hashes(), cf.info_hash);

                fill_event_info(&cf.alert, alert, cs_alert_type::alert_cache_flushed, &message_temp);
                callback(&cf);
                break;
            }

                // dht_announce — a peer announced to our DHT node for an
                // info-hash. Session-level (no torrent handle).
            case lt::dht_announce_alert::alert_type: {
                auto ann = lt::alert_cast<lt::dht_announce_alert>(alert);

                cs_dht_announce_alert da{};
                fill_v6_mapped_addr(ann->ip, da.ip_address);
                da.port = ann->port;
                std::copy(ann->info_hash.begin(), ann->info_hash.end(), da.info_hash);

                fill_event_info(&da.alert, alert, cs_alert_type::alert_dht_announce, &message_temp);
                callback(&da);
                break;
            }

                // dht_get_peers — a peer sent us a get_peers query via DHT
            case lt::dht_get_peers_alert::alert_type: {
                auto gp = lt::alert_cast<lt::dht_get_peers_alert>(alert);

                cs_dht_get_peers_alert dgp{};
                std::copy(gp->info_hash.begin(), gp->info_hash.end(), dgp.info_hash);

                fill_event_info(&dgp.alert, alert, cs_alert_type::alert_dht_get_peers, &message_temp);
                callback(&dgp);
                break;
            }

                // dht_outgoing_get_peers — our DHT node sent a get_peers
                // query to another node
            case lt::dht_outgoing_get_peers_alert::alert_type: {
                auto ogp = lt::alert_cast<lt::dht_outgoing_get_peers_alert>(alert);

                cs_dht_outgoing_get_peers_alert dogp{};
                std::copy(ogp->info_hash.begin(), ogp->info_hash.end(), dogp.info_hash);
                std::copy(ogp->obfuscated_info_hash.begin(), ogp->obfuscated_info_hash.end(), dogp.obfuscated_info_hash);
                fill_v6_mapped_addr(ogp->endpoint.address(), dogp.endpoint_address);
                dogp.endpoint_port = ogp->endpoint.port();

                fill_event_info(&dogp.alert, alert, cs_alert_type::alert_dht_outgoing_get_peers, &message_temp);
                callback(&dogp);
                break;
            }

                // add_torrent — fires after add_torrent / async_add_torrent
                // with the result (success or failure) of the add operation
            case lt::add_torrent_alert::alert_type: {
                auto add = lt::alert_cast<lt::add_torrent_alert>(alert);

                std::string err_msg = add->error.message();

                cs_add_torrent_alert at{};
                if (add->handle.is_valid()) {
                    fill_info_hash(add->handle.info_hashes(), at.info_hash);
                } else {
                    std::fill(at.info_hash, at.info_hash + 20, char{0});
                }
                at.error_code = add->error.value();
                at.error_message = err_msg.c_str();

                fill_event_info(&at.alert, alert, cs_alert_type::alert_add_torrent, &message_temp);
                callback(&at);
                break;
            }

                // torrent_need_cert — SSL torrent requires a cert before it
                // can operate
            case lt::torrent_need_cert_alert::alert_type: {
                auto need = lt::alert_cast<lt::torrent_need_cert_alert>(alert);

                cs_torrent_need_cert_alert nc{};
                fill_info_hash(need->handle.info_hashes(), nc.info_hash);

                fill_event_info(&nc.alert, alert, cs_alert_type::alert_torrent_need_cert, &message_temp);
                callback(&nc);
                break;
            }

                // torrent_conflict — hybrid torrent metadata collision
            case lt::torrent_conflict_alert::alert_type: {
                auto conflict = lt::alert_cast<lt::torrent_conflict_alert>(alert);

                cs_torrent_conflict_alert tc{};
                fill_info_hash(conflict->handle.info_hashes(), tc.info_hash);
                if (conflict->conflicting_torrent.is_valid()) {
                    fill_info_hash(conflict->conflicting_torrent.info_hashes(), tc.conflicting_info_hash);
                } else {
                    std::fill(tc.conflicting_info_hash, tc.conflicting_info_hash + 20, char{0});
                }

                fill_event_info(&tc.alert, alert, cs_alert_type::alert_torrent_conflict, &message_temp);
                callback(&tc);
                break;
            }

                // file_completed — an individual file finished downloading
                // (all overlapping pieces passed hash check). Requires the
                // FileProgress alert category — consumers opt in via session
                // config; not in the binding's required mask.
            case lt::file_completed_alert::alert_type: {
                auto fc = lt::alert_cast<lt::file_completed_alert>(alert);

                cs_file_completed_alert fca{};
                fill_info_hash(fc->handle.info_hashes(), fca.info_hash);
                fca.file_index = static_cast<int32_t>(fc->index);

                fill_event_info(&fca.alert, alert, cs_alert_type::alert_file_completed, &message_temp);
                callback(&fca);
                break;
            }

                // piece_finished — a single piece finished downloading and
                // passed hash check. Requires PieceProgress alert category —
                // consumers opt in via session config.
            case lt::piece_finished_alert::alert_type: {
                auto pf = lt::alert_cast<lt::piece_finished_alert>(alert);

                cs_piece_finished_alert pfa{};
                fill_info_hash(pf->handle.info_hashes(), pfa.info_hash);
                pfa.piece_index = static_cast<int32_t>(pf->piece_index);

                fill_event_info(&pfa.alert, alert, cs_alert_type::alert_piece_finished, &message_temp);
                callback(&pfa);
                break;
            }

                // url_seed — HTTP/web seed lookup or response failed
            case lt::url_seed_alert::alert_type: {
                auto us = lt::alert_cast<lt::url_seed_alert>(alert);

                std::string url = us->server_url();
                std::string err_msg = us->error_message();

                cs_url_seed_alert usa{};
                fill_info_hash(us->handle.info_hashes(), usa.info_hash);
                usa.error_code = us->error.value();
                usa.server_url = url.c_str();
                usa.error_message = err_msg.c_str();

                fill_event_info(&usa.alert, alert, cs_alert_type::alert_url_seed, &message_temp);
                callback(&usa);
                break;
            }

                // block_finished — a single 16 KiB block finished downloading
                // from a specific peer. Requires BlockProgress alert category —
                // consumers opt in via session config (chatty for big swarms).
            case lt::block_finished_alert::alert_type: {
                auto bf = lt::alert_cast<lt::block_finished_alert>(alert);

                cs_block_finished_alert bfa{};
                fill_info_hash(bf->handle.info_hashes(), bfa.info_hash);
                fill_v6_mapped_addr(bf->endpoint.address(), bfa.ipv6_address);
                bfa.block_index = bf->block_index;
                bfa.piece_index = static_cast<int32_t>(bf->piece_index);

                fill_event_info(&bfa.alert, alert, cs_alert_type::alert_block_finished, &message_temp);
                callback(&bfa);
                break;
            }

                // block_uploaded — a single 16 KiB block has been written
                // out to a specific peer. Symmetric to block_finished but
                // for the upload direction. Requires Upload alert category
                // — consumers opt in via session config (chatty for active
                // seeds).
            case lt::block_uploaded_alert::alert_type: {
                auto bu = lt::alert_cast<lt::block_uploaded_alert>(alert);

                cs_block_uploaded_alert bua{};
                fill_info_hash(bu->handle.info_hashes(), bua.info_hash);
                fill_v6_mapped_addr(bu->endpoint.address(), bua.ipv6_address);
                bua.block_index = bu->block_index;
                bua.piece_index = static_cast<int32_t>(bu->piece_index);

                fill_event_info(&bua.alert, alert, cs_alert_type::alert_block_uploaded, &message_temp);
                callback(&bua);
                break;
            }

                // block_timeout — a previously-issued block request did not
                // get filled before its deadline. Same field shape as
                // block_finished/block_uploaded. Requires BlockProgress
                // alert category — consumers opt in via session config.
            case lt::block_timeout_alert::alert_type: {
                auto bt = lt::alert_cast<lt::block_timeout_alert>(alert);

                cs_block_timeout_alert bta{};
                fill_info_hash(bt->handle.info_hashes(), bta.info_hash);
                fill_v6_mapped_addr(bt->endpoint.address(), bta.ipv6_address);
                bta.block_index = bt->block_index;
                bta.piece_index = static_cast<int32_t>(bt->piece_index);

                fill_event_info(&bta.alert, alert, cs_alert_type::alert_block_timeout, &message_temp);
                callback(&bta);
                break;
            }

                // block_downloading — a single 16 KiB block has started
                // arriving from a peer (request issued, bytes flowing).
                // Same field shape as block_finished/timeout/uploaded.
                // Requires BlockProgress alert category — consumers opt
                // in via session config (chatty for active downloads).
            case lt::block_downloading_alert::alert_type: {
                auto bd = lt::alert_cast<lt::block_downloading_alert>(alert);

                cs_block_downloading_alert bda{};
                fill_info_hash(bd->handle.info_hashes(), bda.info_hash);
                fill_v6_mapped_addr(bd->endpoint.address(), bda.ipv6_address);
                bda.block_index = bd->block_index;
                bda.piece_index = static_cast<int32_t>(bd->piece_index);

                fill_event_info(&bda.alert, alert, cs_alert_type::alert_block_downloading, &message_temp);
                callback(&bda);
                break;
            }

                // unwanted_block — a peer sent us a block we never
                // requested. Same field shape as the rest of the Block*
                // family. Requires BlockProgress alert category —
                // consumers opt in via session config.
            case lt::unwanted_block_alert::alert_type: {
                auto ub = lt::alert_cast<lt::unwanted_block_alert>(alert);

                cs_unwanted_block_alert uba{};
                fill_info_hash(ub->handle.info_hashes(), uba.info_hash);
                fill_v6_mapped_addr(ub->endpoint.address(), uba.ipv6_address);
                uba.block_index = ub->block_index;
                uba.piece_index = static_cast<int32_t>(ub->piece_index);

                fill_event_info(&uba.alert, alert, cs_alert_type::alert_unwanted_block, &message_temp);
                callback(&uba);
                break;
            }

                // socks5 — a SOCKS5 proxy operation failed. Session-level
                // (the proxy itself is the failing resource, not a torrent).
                // `error_message` is dispatcher-owned for the callback
                // duration (held alive by `err_msg`).
            case lt::socks5_alert::alert_type: {
                auto s5 = lt::alert_cast<lt::socks5_alert>(alert);

                std::string err_msg = s5->error.message();

                cs_socks5_alert s5a{};
                fill_v6_mapped_addr(s5->ip.address(), s5a.endpoint_address);
                s5a.endpoint_port = s5->ip.port();
                s5a.operation = static_cast<int32_t>(s5->op);
                s5a.error_code = s5->error.value();
                s5a.error_message = err_msg.c_str();

                fill_event_info(&s5a.alert, alert, cs_alert_type::alert_socks5, &message_temp);
                callback(&s5a);
                break;
            }

                // i2p — an I2P-router operation failed (SAM bridge,
                // destination resolution, etc.). Session-level. Smaller
                // shape than socks5 — libtorrent's i2p_alert exposes
                // only the error_code (no endpoint or op).
                // `error_message` is dispatcher-owned for the callback
                // duration (held alive by `err_msg`).
            case lt::i2p_alert::alert_type: {
                auto i2p = lt::alert_cast<lt::i2p_alert>(alert);

                std::string err_msg = i2p->error.message();

                cs_i2p_alert i2pa{};
                i2pa.error_code = i2p->error.value();
                i2pa.error_message = err_msg.c_str();

                fill_event_info(&i2pa.alert, alert, cs_alert_type::alert_i2p, &message_temp);
                callback(&i2pa);
                break;
            }

                // torrent_log — verbose torrent-scoped log message.
                // Requires TorrentLog alert category — consumers opt in
                // via session config (high-volume, debug-tier).
                // `log_message` is dispatcher-owned for the callback
                // duration (held alive by `tl_msg`).
            case lt::torrent_log_alert::alert_type: {
                auto tl = lt::alert_cast<lt::torrent_log_alert>(alert);

                std::string tl_msg = tl->message();

                cs_torrent_log_alert tla{};
                fill_info_hash(tl->handle.info_hashes(), tla.info_hash);
                tla.log_message = tl_msg.c_str();

                fill_event_info(&tla.alert, alert, cs_alert_type::alert_torrent_log, &message_temp);
                callback(&tla);
                break;
            }

                // log — session-level verbose log message. Sibling to
                // torrent_log but no torrent association. Requires
                // SessionLog alert category — consumers opt in via
                // session config (high-volume, debug-tier).
                // `log_message` is dispatcher-owned for the callback
                // duration (held alive by `lg_msg`).
            case lt::log_alert::alert_type: {
                auto lg = lt::alert_cast<lt::log_alert>(alert);

                std::string lg_msg = lg->message();

                cs_log_alert lga{};
                lga.log_message = lg_msg.c_str();

                fill_event_info(&lga.alert, alert, cs_alert_type::alert_log, &message_temp);
                callback(&lga);
                break;
            }

                // dht_log — DHT-subsystem verbose log message. Carries
                // a typed `module` (tracker/node/routing_table/
                // rpc_manager/traversal) so consumers can filter by
                // which DHT subsystem emitted the line. Requires
                // DHTLog alert category — consumers opt in via session
                // config (high-volume, debug-tier). `log_message` is
                // dispatcher-owned for the callback duration (held
                // alive by `dl_msg`).
            case lt::dht_log_alert::alert_type: {
                auto dl = lt::alert_cast<lt::dht_log_alert>(alert);

                std::string dl_msg = dl->message();

                cs_dht_log_alert dla{};
                dla.module = static_cast<int32_t>(dl->module);
                dla.log_message = dl_msg.c_str();

                fill_event_info(&dla.alert, alert, cs_alert_type::alert_dht_log, &message_temp);
                callback(&dla);
                break;
            }

                // peer_blocked — incoming peer was filtered before any
                // payload was exchanged. Requires IPBlock alert category —
                // consumers opt in via session config.
            case lt::peer_blocked_alert::alert_type: {
                auto pb = lt::alert_cast<lt::peer_blocked_alert>(alert);

                cs_peer_blocked_alert pba{};
                fill_info_hash(pb->handle.info_hashes(), pba.info_hash);
                fill_v6_mapped_addr(pb->endpoint.address(), pba.ipv6_address);
                pba.reason = pb->reason;

                fill_event_info(&pba.alert, alert, cs_alert_type::alert_peer_blocked, &message_temp);
                callback(&pba);
                break;
            }

                // incoming_connection — session-level accept of a peer
                // connection. Fires before the connection is associated
                // with a specific torrent (no info_hash routing). Requires
                // Peer alert category — consumers opt in via session config.
            case lt::incoming_connection_alert::alert_type: {
                auto ic = lt::alert_cast<lt::incoming_connection_alert>(alert);

                cs_incoming_connection_alert ica{};
                fill_v6_mapped_addr(ic->endpoint.address(), ica.endpoint_address);
                ica.endpoint_port = ic->endpoint.port();
                ica.socket_type = static_cast<uint8_t>(ic->socket_type);

                fill_event_info(&ica.alert, alert, cs_alert_type::alert_incoming_connection, &message_temp);
                callback(&ica);
                break;
            }

                // save_resume_data completed
            case lt::save_resume_data_alert::alert_type: {
                auto resume_alert = lt::alert_cast<lt::save_resume_data_alert>(alert);
                auto buffer = lt::write_resume_data_buf(resume_alert->params);

                cs_resume_data_alert resume_data{};

                fill_info_hash(resume_alert->handle.info_hashes(), resume_data.info_hash);
                resume_data.data = buffer.data();
                resume_data.length = static_cast<int32_t>(buffer.size());

                fill_event_info(&resume_data.alert, alert, cs_alert_type::alert_resume_data_ready, &message_temp);
                callback(&resume_data);
                break;
            }

                // post_dht_stats completed — totals + per-bucket detail.
                // Lookup detail (active_requests) stays a count only for now;
                // it lands alongside dht_put / dht_get items in a later slice.
                // The buckets vector lives on the stack — its data() is valid
                // for the duration of the callback, the managed side copies.
            case lt::dht_stats_alert::alert_type: {
                auto stats_alert = lt::alert_cast<lt::dht_stats_alert>(alert);

                int32_t total_nodes = 0;
                int32_t total_replacements = 0;
                std::vector<cs_dht_routing_bucket> buckets;
                buckets.reserve(stats_alert->routing_table.size());
                for (const auto& bucket : stats_alert->routing_table) {
                    total_nodes += bucket.num_nodes;
                    total_replacements += bucket.num_replacements;
                    buckets.push_back({bucket.num_nodes, bucket.num_replacements, bucket.last_active});
                }

                std::vector<cs_dht_lookup> lookups;
                lookups.reserve(stats_alert->active_requests.size());
                for (const auto& l : stats_alert->active_requests) {
                    cs_dht_lookup csl{};
                    csl.outstanding_requests = l.outstanding_requests;
                    csl.timeouts = l.timeouts;
                    csl.responses = l.responses;
                    csl.branch_factor = l.branch_factor;
                    csl.nodes_left = l.nodes_left;
                    csl.last_sent = l.last_sent;
                    csl.first_timeout = l.first_timeout;
                    std::copy(l.target.begin(), l.target.end(), csl.target);
                    csl.type = l.type;  // static-lifetime literal, no copy
                    lookups.push_back(csl);
                }

                cs_dht_stats_alert stats{};
                stats.total_nodes = total_nodes;
                stats.total_replacements = total_replacements;
                stats.active_requests = static_cast<int32_t>(stats_alert->active_requests.size());
                stats.bucket_count = static_cast<int32_t>(buckets.size());
                stats.buckets = buckets.data();
                stats.lookup_count = static_cast<int32_t>(lookups.size());
                stats.lookups = lookups.data();

                fill_event_info(&stats.alert, alert, cs_alert_type::alert_dht_stats, &message_temp);
                callback(&stats);
                break;
            }

                // dht_put_item completed (immutable or mutable). For an
                // immutable put the mutable-side fields are zero/empty; for a
                // mutable put they carry the BEP44 envelope. salt is dispatcher-
                // owned for the duration of the callback.
            case lt::dht_put_alert::alert_type: {
                auto put_alert = lt::alert_cast<lt::dht_put_alert>(alert);

                cs_dht_put_alert put{};
                std::copy(put_alert->target.begin(), put_alert->target.end(), put.target);
                put.num_success = put_alert->num_success;
                std::copy(put_alert->public_key.begin(), put_alert->public_key.end(), put.public_key);
                std::copy(put_alert->signature.begin(), put_alert->signature.end(), put.signature);
                put.seq = put_alert->seq;
                put.salt = put_alert->salt.data();
                put.salt_len = static_cast<int32_t>(put_alert->salt.size());

                fill_event_info(&put.alert, alert, cs_alert_type::alert_dht_put, &message_temp);
                callback(&put);
                break;
            }

                // dht_get_item (mutable) completed successfully. Misses time
                // out internally without firing. Same lifetime contract as the
                // immutable item alert: data + salt are dispatcher-owned.
            case lt::dht_mutable_item_alert::alert_type: {
                auto get_alert = lt::alert_cast<lt::dht_mutable_item_alert>(alert);

                cs_dht_mutable_item_alert mut{};
                std::copy(get_alert->key.begin(), get_alert->key.end(), mut.public_key);
                std::copy(get_alert->signature.begin(), get_alert->signature.end(), mut.signature);
                mut.seq = get_alert->seq;
                mut.salt = get_alert->salt.data();
                mut.salt_len = static_cast<int32_t>(get_alert->salt.size());
                mut.authoritative = get_alert->authoritative ? int8_t{1} : int8_t{0};

                std::string body;
                if (get_alert->item.type() == lt::entry::string_t) {
                    body = get_alert->item.string();
                }
                mut.data = body.data();
                mut.data_len = static_cast<int32_t>(body.size());

                fill_event_info(&mut.alert, alert, cs_alert_type::alert_dht_mutable_item, &message_temp);
                callback(&mut);
                break;
            }

                // post_session_stats completed — flat int64 counter array.
                // counters() is a span into the alert's own storage which is
                // valid for the duration of the callback; the managed side
                // copies before returning.
            case lt::session_stats_alert::alert_type: {
                auto stats_alert = lt::alert_cast<lt::session_stats_alert>(alert);
                auto span = stats_alert->counters();

                cs_session_stats_alert stats{};
                stats.counters_count = static_cast<int32_t>(span.size());
                stats.counters = span.data();

                fill_event_info(&stats.alert, alert, cs_alert_type::alert_session_stats, &message_temp);
                callback(&stats);
                break;
            }

                // listen socket successfully bound — typed for f-session-listen.
                // Address is converted to v4-mapped v6 to mirror peer/ip-filter
                // marshalling; managed side reads and demaps as needed.
            case lt::listen_succeeded_alert::alert_type: {
                auto succ = lt::alert_cast<lt::listen_succeeded_alert>(alert);

                cs_listen_succeeded_alert ls{};
                fill_v6_mapped_addr(succ->address, ls.address);
                ls.port = succ->port;
                ls.socket_type = static_cast<uint8_t>(succ->socket_type);

                fill_event_info(&ls.alert, alert, cs_alert_type::alert_listen_succeeded, &message_temp);
                callback(&ls);
                break;
            }

                // listen socket bind failed. listen_interface() and
                // error.message() are kept alive in stack-scoped strings for
                // the duration of the callback; managed side copies the bytes.
            case lt::listen_failed_alert::alert_type: {
                auto fail = lt::alert_cast<lt::listen_failed_alert>(alert);

                std::string iface_str = fail->listen_interface() != nullptr ? fail->listen_interface() : "";
                std::string error_msg = fail->error.message();

                cs_listen_failed_alert lf{};
                fill_v6_mapped_addr(fail->address, lf.address);
                lf.port = fail->port;
                lf.socket_type = static_cast<uint8_t>(fail->socket_type);
                lf.op = static_cast<uint8_t>(fail->op);
                lf.error_code = fail->error.value();
                lf.listen_interface = iface_str.c_str();
                lf.error_message = error_msg.c_str();

                fill_event_info(&lf.alert, alert, cs_alert_type::alert_listen_failed, &message_temp);
                callback(&lf);
                break;
            }

                // dht_get_item (immutable) completed successfully. Misses time
                // out internally without firing an alert. The bencoded entry is
                // unwrapped to the contained string; non-string entries surface
                // as empty bytes for now (mutable items + structured payloads
                // would need a full entry marshaller — deferred).
            case lt::dht_immutable_item_alert::alert_type: {
                auto get_alert = lt::alert_cast<lt::dht_immutable_item_alert>(alert);

                cs_dht_immutable_item_alert get{};
                std::copy(get_alert->target.begin(), get_alert->target.end(), get.target);

                std::string body;
                if (get_alert->item.type() == lt::entry::string_t) {
                    body = get_alert->item.string();
                }
                get.data = body.data();
                get.length = static_cast<int32_t>(body.size());

                fill_event_info(&get.alert, alert, cs_alert_type::alert_dht_immutable_item, &message_temp);
                callback(&get);
                break;
            }

            default: {
                if (!include_unmapped) {
                    break;
                }

                cs_alert generic_alert{};

                fill_event_info(&generic_alert, alert, cs_alert_type::alert_generic, &message_temp);
                callback(&generic_alert);
                break;
            }
        }
    }

    events.clear();
    session->pop_alerts(&events);

    if (!events.empty()) {
        goto handle_events;
    }
}
