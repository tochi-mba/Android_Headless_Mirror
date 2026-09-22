using System.Security.Cryptography;
using System.Text;

namespace Rex.Core;

/// <summary>
/// Serializes read-modify-write transactions for a file across REX processes.
/// AtomicFile protects the destination from torn writes; this lock protects the
/// transaction from lost updates between the GUI, CLI, and other local callers.
/// </summary>
public sealed class CrossProcessFileLock : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private CrossProcessFileLock(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public static CrossProcessFileLock Acquire(string path, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var canonical = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var mutex = new Mutex(initiallyOwned: false, @"Local\Rex.File." + hash);

        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(timeout ?? DefaultTimeout);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died while holding the mutex. Windows transfers
                // ownership to this thread, so the protected file can be recovered.
                acquired = true;
            }

            if (!acquired)
            {
                throw new IOException($"Timed out waiting to update {Path.GetFileName(path)}.");
            }

            return new CrossProcessFileLock(mutex, ownsMutex: true);
        }
        catch
        {
            if (!acquired)
            {
                mutex.Dispose();
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _ownsMutex = false;
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
