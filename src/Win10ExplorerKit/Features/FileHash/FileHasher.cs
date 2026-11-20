using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Win10ExplorerKit.Features.FileHash
{
    internal static class FileHasher
    {
        // One reusable data buffer per worker, measured in bytes
        internal const int BufferSize = 1024 * 1024;
        internal static readonly string[] Algorithms = { "MD5", "SHA-1", "SHA-256", "SHA-512" };

        /// <summary>Creates a distinct algorithm instance for each file</summary>
        /// <param name="algorithm">Supported display name</param>
        /// <returns>Disposable cryptographic hash implementation</returns>
        private static HashAlgorithm Create(string algorithm)
        {
            switch (algorithm)
            {
                case "MD5": return MD5.Create();
                case "SHA-1": return SHA1.Create();
                case "SHA-256": return SHA256.Create();
                case "SHA-512": return SHA512.Create();
                default: throw new ArgumentException("Unsupported hash algorithm", nameof(algorithm));
            }
        }

        /// <summary>Streams a file while rejecting concurrent writers and honoring cancellation between reads</summary>
        /// <param name="item">Result and progress destination</param>
        /// <param name="algorithm">Supported hash name</param>
        /// <param name="buffer">Worker-owned reusable buffer</param>
        /// <param name="token">Current round cancellation token</param>
        internal static void Compute(HashItem item, string algorithm, byte[] buffer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            item.State = HashState.Reading;
            using (var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.SequentialScan))
            using (HashAlgorithm hash = Create(algorithm))
            {
                Interlocked.Exchange(ref item.Length, stream.Length);
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    hash.TransformBlock(buffer, 0, count, null, 0);
                    Interlocked.Add(ref item.BytesRead, count);
                }
                token.ThrowIfCancellationRequested();
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                item.Value = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                item.State = HashState.Complete;
            }
        }

        /// <summary>Maps failures to localized categories without exposing raw exception payloads</summary>
        /// <param name="exception">File discovery or reading error</param>
        /// <returns>Locale key for the failure category</returns>
        internal static string ErrorKey(Exception exception)
        {
            if (exception is UnauthorizedAccessException || exception is System.Security.SecurityException) return "hash.denied";
            if (exception is FileNotFoundException || exception is DirectoryNotFoundException) return "hash.missing";
            if (exception is PathTooLongException) return "hash.longPath";
            if (exception is IOException && (exception.HResult & 0xffff) == 32) return "hash.locked";
            return "hash.readError";
        }
    }
}
