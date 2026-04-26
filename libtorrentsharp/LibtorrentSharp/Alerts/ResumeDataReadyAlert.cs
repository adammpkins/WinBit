using System;
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a previously-requested <c>save_resume_data</c> completes and a resume blob is available.
/// Pass <see cref="ResumeData"/> back to <see cref="LibtorrentSession.Add"/> via
/// <see cref="AddTorrentParams.ResumeData"/> to skip the re-check step on a future session.
/// </summary>
public class ResumeDataReadyAlert : Alert
{
    internal ResumeDataReadyAlert(NativeEvents.ResumeDataAlert alert)
        : base(alert.info)
    {
        InfoHash = new Sha1Hash(alert.info_hash);

        if (alert.length > 0 && alert.data != IntPtr.Zero)
        {
            ResumeData = new byte[alert.length];
            Marshal.Copy(alert.data, ResumeData, 0, alert.length);
        }
        else
        {
            ResumeData = Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Info hash of the torrent this resume blob belongs to (SHA-1 / v1).
    /// </summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>
    /// Bencoded add_torrent_params buffer. Persist it and pass it back via
    /// <see cref="AddTorrentParams.ResumeData"/> on the next startup.
    /// </summary>
    public byte[] ResumeData { get; }
}
