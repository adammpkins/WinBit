CREATE TABLE schema_version (version INTEGER NOT NULL);
INSERT INTO schema_version VALUES (1);

CREATE TABLE torrent (
    info_hash     TEXT    PRIMARY KEY,
    name          TEXT    NOT NULL,
    save_path     TEXT    NOT NULL,
    category      TEXT,
    tags          TEXT,
    added_utc     TEXT    NOT NULL,
    completed_utc TEXT,
    is_sequential INTEGER NOT NULL DEFAULT 0,
    first_last    INTEGER NOT NULL DEFAULT 0,
    dl_limit_bps  INTEGER NOT NULL DEFAULT 0,
    ul_limit_bps  INTEGER NOT NULL DEFAULT 0,
    share_limits  TEXT,
    torrent_blob  BLOB,
    fast_resume   BLOB,
    resume_ver    INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE log_entry (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ts_utc    TEXT    NOT NULL,
    severity  INTEGER NOT NULL,
    message   TEXT    NOT NULL
);

CREATE TABLE peer_log (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ts_utc    TEXT    NOT NULL,
    ip        TEXT    NOT NULL,
    reason    TEXT    NOT NULL,
    blocked   INTEGER NOT NULL
);
