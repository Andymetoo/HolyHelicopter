using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NullMenagerie
{
    /// <summary>Read-only, format-agnostic local file sampling. Never decodes or executes imported content.</summary>
    public static class FileAnalyzer
    {
        public const int SampleBudget = 256 * 1024;
        public const int WindowBytes = 16 * 1024;
        public const int WindowCount = SampleBudget / WindowBytes;

        public static FileProfile Analyze(string path, CancellationToken token = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a local file first.");
            path = ValidateLocalPath(path.Trim().Trim('"'));
            token.ThrowIfCancellationRequested();
            var before = new FileInfo(path);
            if (!before.Exists) throw new FileNotFoundException("This file is missing or cannot be read.");
            DateTime modified = before.LastWriteTimeUtc;
            long size = before.Length;
            FileProfile result;
            // Sharing reads only also prevents a concurrent writer from changing the specimen while sampled.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, WindowBytes, FileOptions.RandomAccess))
            {
                if (stream.Length != size) throw new IOException("The file changed before sampling. Try again when it is idle.");
                byte[] sample = Sample(stream, size, token);
                token.ThrowIfCancellationRequested();
                result = Inspect(Path.GetFileName(path), size, sample);
                before.Refresh();
                if (stream.Length != size || before.Length != size || before.LastWriteTimeUtc != modified)
                    throw new IOException("The file changed during sampling. Try again when it is idle.");
            }
            return result;
        }

        public static FileProfile AnalyzeBytes(string name, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            using (var stream = new MemoryStream(bytes, false))
                return Inspect(Path.GetFileName(name ?? "untitled.bin"), bytes.LongLength, Sample(stream, bytes.LongLength, CancellationToken.None));
        }

        static string ValidateLocalPath(string path)
        {
            if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                throw new IOException("Choose a local disk file. Network and device paths are not imported.");
            string full = Path.GetFullPath(path);
            // Windows alternate data streams and device aliases are outside the ordinary-file contract.
            int colon = full.IndexOf(':', 2);
            if (colon >= 0) throw new IOException("Alternate data streams are not imported.");
            string root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network)
                throw new IOException("Copy this file to a local disk before importing.");
            string current = full;
            while (!string.IsNullOrEmpty(current) && !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Import the original local file rather than a symbolic link or cloud placeholder.");
                current = Path.GetDirectoryName(current);
            }
            if (Directory.Exists(full)) throw new IOException("Select a file, rather than a folder.");
            string stem = Path.GetFileNameWithoutExtension(full).ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3])))
                throw new IOException("Device names cannot become specimens.");
            return full;
        }

        static byte[] Sample(Stream stream, long size, CancellationToken token)
        {
            byte[] data = new byte[(int)Math.Min(size, SampleBudget)];
            if (size <= SampleBudget)
            {
                ReadExactly(stream, data, 0, data.Length, token);
                return data;
            }
            // Integer arithmetic keeps offsets stable even for very large files, without multiplying size.
            long span = size - WindowBytes;
            for (int i = 0; i < WindowCount; i++)
            {
                token.ThrowIfCancellationRequested();
                long offset = (span / (WindowCount - 1)) * i + (span % (WindowCount - 1)) * i / (WindowCount - 1);
                stream.Seek(offset, SeekOrigin.Begin);
                ReadExactly(stream, data, i * WindowBytes, WindowBytes, token);
            }
            return data;
        }

        static void ReadExactly(Stream stream, byte[] data, int offset, int length, CancellationToken token)
        {
            int total = 0;
            while (total < length)
            {
                token.ThrowIfCancellationRequested();
                int n = stream.Read(data, offset + total, Math.Min(WindowBytes, length - total));
                if (n == 0) throw new EndOfStreamException("The file became shorter during sampling.");
                total += n;
            }
        }

        static FileProfile Inspect(string name, long size, byte[] bytes)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            byte[] digest;
            // Explicit UTF-8 metadata avoids platform endianness and runtime-dependent hash implementations.
            byte[] header = Encoding.UTF8.GetBytes("NULL-MENAGERIE:1\n" + size.ToString(CultureInfo.InvariantCulture) + "\n" + ext + "\n");
            using (var sha = SHA256.Create())
            {
                sha.TransformBlock(header, 0, header.Length, header, 0);
                sha.TransformFinalBlock(bytes, 0, bytes.Length);
                digest = sha.Hash;
            }
            var fingerprint = new StringBuilder(64);
            foreach (byte b in digest) fingerprint.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            var profile = new FileProfile
            {
                FileName = Sanitize(name), Extension = ext, Size = size,
                SampleBytes = bytes.Length, Sampled = size > SampleBudget,
                Fingerprint = fingerprint.ToString(),
                Seed = (uint)digest[0] | ((uint)digest[1] << 8) | ((uint)digest[2] << 16) | ((uint)digest[3] << 24)
            };
            int[] distribution = new int[256];
            int print = 0, lines = 0, transitions = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                distribution[b]++;
                if ((b >= 32 && b <= 126) || b == 9 || b == 10 || b == 13) print++;
                if (b == 10) lines++;
                if (i > 0 && Math.Abs((int)b - bytes[i - 1]) > 32) transitions++;
            }
            if (bytes.Length > 0)
            {
                double entropy = 0;
                foreach (int count in distribution)
                {
                    if (count == 0) continue;
                    profile.DistinctBytes++;
                    double probability = (double)count / bytes.Length;
                    entropy -= probability * Math.Log(probability, 2);
                }
                profile.Entropy = (float)(entropy / 8.0);
                profile.Printable = (float)print / bytes.Length;
                profile.ZeroFraction = (float)distribution[0] / bytes.Length;
                profile.LineDensity = Math.Min(1f, (float)lines * 40 / bytes.Length);
                profile.Transitions = bytes.Length < 2 ? 0 : (float)transitions / (bytes.Length - 1);
                // Search short repeat periods: repeated code/text differs from truly chaotic compressed data.
                int[] lags = { 1, 2, 4, 8, 16, 32, 64 };
                float repeat = 0;
                foreach (int lag in lags)
                {
                    if (bytes.Length <= lag * 2) continue;
                    int equal = 0;
                    for (int i = lag; i < bytes.Length; i++) if (bytes[i] == bytes[i - lag]) equal++;
                    repeat = Math.Max(repeat, (float)equal / (bytes.Length - lag));
                }
                profile.Repetition = repeat;
                using (var sink = new CountingStream())
                {
                    using (var compressor = new DeflateStream(sink, CompressionLevel.Fastest, true))
                        compressor.Write(bytes, 0, bytes.Length);
                    profile.Compressibility = Math.Max(0, Math.Min(1, 1f - (float)sink.Count / bytes.Length));
                }
            }
            profile.Family = Family(ext, profile.Printable);
            return profile;
        }

        static FileFamily Family(string extension, float printable)
        {
            switch (extension)
            {
                case ".txt": case ".md": case ".cs": case ".cpp": case ".c": case ".h": case ".py": case ".js": case ".ts": case ".json": case ".xml": case ".csv": case ".html": case ".css": case ".log": case ".shader": case ".yaml": case ".yml": case ".ini": case ".sql": return FileFamily.Text;
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": case ".tga": case ".webp": case ".tif": case ".tiff": case ".svg": case ".psd": return FileFamily.Image;
                case ".mp3": case ".wav": case ".ogg": case ".flac": case ".aac": case ".m4a": case ".aiff": case ".mid": case ".midi": return FileFamily.Audio;
                case ".mp4": case ".avi": case ".mkv": case ".mov": case ".webm": case ".wmv": case ".m4v": return FileFamily.Video;
                case ".exe": case ".dll": case ".com": case ".msi": case ".so": case ".dylib": case ".sys": return FileFamily.Executable;
                case ".zip": case ".7z": case ".rar": case ".gz": case ".tar": case ".bz2": case ".xz": case ".pak": return FileFamily.Archive;
                case ".pdf": case ".doc": case ".docx": case ".xls": case ".xlsx": case ".ppt": case ".pptx": case ".odt": case ".ods": case ".rtf": return FileFamily.Document;
                default: return printable > .88f ? FileFamily.Text : FileFamily.Unknown;
            }
        }

        static string Sanitize(string name)
        {
            var builder = new StringBuilder();
            foreach (char c in name)
            {
                if (builder.Length == 96) break;
                builder.Append(char.IsControl(c) || c == '<' || c == '>' ? '_' : c);
            }
            return builder.Length == 0 ? "untitled" : builder.ToString();
        }

        sealed class CountingStream : Stream
        {
            public long Count;
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { return Count; } }
            public override long Position { get { return Count; } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override void Write(byte[] buffer, int offset, int count) { Count += count; }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
        }
    }
}
