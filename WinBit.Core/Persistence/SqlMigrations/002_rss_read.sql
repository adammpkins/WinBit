CREATE TABLE rss_read (
    feed_url   TEXT NOT NULL,
    article_id TEXT NOT NULL,
    PRIMARY KEY (feed_url, article_id)
);

UPDATE schema_version SET version = 2;
