using System.Threading;

namespace Win10ExplorerKit.Features.FileHash
{
    internal enum HashState { Waiting, Reading, Complete, Failed, Canceled, Empty }

    internal sealed class HashItem
    {
        internal string Path { get; }
        internal bool IsFile { get; }
        // Workers publish the result before publishing the final state
        internal volatile HashState State;
        internal volatile string Value;
        internal volatile string Error;
        internal long BytesRead;
        internal long Length;

        /// <summary>Creates an immutable path identity with independently published progress</summary>
        /// <param name="path">Full filesystem path</param>
        /// <param name="isFile">Whether this row represents a hashable file</param>
        internal HashItem(string path, bool isFile = true)
        {
            Path = path;
            IsFile = isFile;
        }

        /// <summary>Reads progress atomically in both 32-bit and 64-bit processes</summary>
        /// <returns>Completed percentage for the current file</returns>
        internal int Percent()
        {
            long length = Interlocked.Read(ref Length);
            return length == 0 ? 0 : (int)System.Math.Min(100, Interlocked.Read(ref BytesRead) * 100.0 / length);
        }
    }
}
