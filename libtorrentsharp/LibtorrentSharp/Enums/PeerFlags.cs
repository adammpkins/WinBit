namespace LibtorrentSharp.Enums;

/// <summary>Typed mirror of libtorrent's <c>peer_info::flags_t</c> bitmask.</summary>
[Flags]
public enum PeerFlags : uint
{
    /// <summary>No flags set.</summary>
    None = 0,
    /// <summary>We are interested in at least one piece this peer has.</summary>
    Interesting = 0x1,
    /// <summary>We are choked by this peer and cannot request pieces.</summary>
    Choked = 0x2,
    /// <summary>This peer is interested in at least one piece we have.</summary>
    RemoteInterested = 0x4,
    /// <summary>We have choked this peer and will not respond to requests.</summary>
    RemoteChoked = 0x8,
    /// <summary>This peer supports the libtorrent extension protocol (BEP-10).</summary>
    SupportsExtensions = 0x10,
    /// <summary>We initiated this connection outbound; false means the peer connected to us.</summary>
    OutgoingConnection = 0x20,
    /// <summary>The connection is currently performing the BitTorrent handshake.</summary>
    Handshake = 0x40,
    /// <summary>The TCP connection is being established and is not yet usable.</summary>
    Connecting = 0x80,
    /// <summary>This peer has been put on parole (it sent a corrupt piece and must redeem itself).</summary>
    OnParole = 0x200,
    /// <summary>This peer has all pieces (is a seeder).</summary>
    Seed = 0x400,
    /// <summary>This peer is being optimistically unchoked despite not being the highest-upload-rate peer.</summary>
    OptimisticUnchoke = 0x800,
    /// <summary>This peer has been snubbed: it has not sent data fast enough and we have stopped requesting from it.</summary>
    Snubbed = 0x1000,
    /// <summary>This peer is in upload-only mode and will not request pieces from us.</summary>
    UploadOnly = 0x2000,
    /// <summary>Endgame mode: the same pieces are being requested from multiple peers to minimize the tail latency near completion.</summary>
    EndgameMode = 0x4000,
    /// <summary>This connection was established via UDP hole punching.</summary>
    Holepunched = 0x8000,
    /// <summary>Connected through an I2P proxy.</summary>
    I2PSocket = 0x10000,
    /// <summary>Connected over uTP (Micro Transport Protocol / uTorrent Transport Protocol).</summary>
    UtpSocket = 0x20000,
    /// <summary>Connected over an SSL/TLS stream.</summary>
    SslSocket = 0x40000,
    /// <summary>Stream is RC4-encrypted via the Message Stream Encryption protocol.</summary>
    Rc4Encrypted = 0x100000,
    /// <summary>Stream uses MSE plaintext obfuscation (header encrypted, body not).</summary>
    PlaintextEncrypted = 0x200000,
}