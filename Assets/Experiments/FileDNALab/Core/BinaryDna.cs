using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace FileDNALab.Core
{
    /// <summary>One streaming pass over every byte. SHA identifies content; it never drives expression.</summary>
    public static class BinaryDna
    {
        public static DnaDocument ReadFile(string path, DnaLimits limits = null, CancellationToken token = default(CancellationToken))
        {
            path = (path ?? "").Trim().Trim('"');
            if (path.Length == 0) throw new IOException("Enter an absolute local file path.");
            if (!Path.IsPathRooted(path) || path.StartsWith(@"\\") || path.StartsWith("//") || path.IndexOf(':',2)>=0)
                throw new IOException("Choose an ordinary absolute local file path, without a network/device prefix or alternate data stream.");
            string full = Path.GetFullPath(path), root = Path.GetPathRoot(full), walk = full;
            while (!string.IsNullOrEmpty(walk) && walk != root)
            {
                if ((File.GetAttributes(walk) & FileAttributes.ReparsePoint) != 0) throw new IOException("Choose the original local file, not a link or cloud placeholder.");
                walk = Path.GetDirectoryName(walk);
            }
            using (var stream = new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.SequentialScan))
                return Read(stream,Path.GetFileName(full),limits,token);
        }
        public static DnaDocument ReadBytes(byte[] bytes, string name = "fixture.bin", DnaLimits limits = null)
        { using (var s = new MemoryStream(bytes,false)) return Read(s,name,limits); }

        public static DnaDocument Read(Stream stream, string name, DnaLimits limits = null, CancellationToken token = default(CancellationToken))
        {
            limits = limits ?? new DnaLimits(); limits.Validate();
            if (!stream.CanRead || !stream.CanSeek || stream.Position != 0) throw new IOException("DNA requires a readable, seekable stream at position zero.");
            long length = stream.Length;
            if (length > limits.MaxFileBytes) throw new IOException("File is "+length+" bytes; configured limit is "+limits.MaxFileBytes+". Nothing was truncated.");
            var d = new DnaDocument { Name = name, ByteCount = length, Folded = length > limits.MaxCodons*16L };
            int count = (int)Math.Min(limits.MaxCodons, Math.Max(1,(length+15)/16));
            var buffer = new byte[65536]; int available=0, position=0, previous=-1; long totalRepeats=0,totalDelta=0;
            using (var sha = SHA256.Create())
            {
                for (int region=0;region<count;region++)
                {
                    token.ThrowIfCancellationRequested();
                    long start = d.Folded ? length*region/count : region*16L;
                    long end = d.Folded ? length*(region+1)/count : Math.Min(length,start+16);
                    var r = new SourceRegion { Start=start, Length=end-start };
                    var sum=new long[16]; var slope=new long[16]; var laneCounts=new int[16]; var histogram=new long[256];
                    long repeats=0,delta=0; int localPrevious=-1;
                    for (long at=start;at<end;at++)
                    {
                        if (position==available)
                        {
                            token.ThrowIfCancellationRequested();
                            available=stream.Read(buffer,0,(int)Math.Min(buffer.Length,length-at)); position=0;
                            if(available==0) throw new EndOfStreamException("File changed during reading.");
                            sha.TransformBlock(buffer,0,available,buffer,0);
                        }
                        byte value=buffer[position++]; int lane=(int)((at-start)%16);
                        d.Frequencies[value]++; histogram[value]++; sum[lane]+=value; laneCounts[lane]++;
                        // Alternating row phase preserves order inside a region without a PRNG or hash avalanche.
                        slope[lane]+=(((at-start)/16)%2==0 ? value : -value);
                        if(previous>=0) { if(previous==value) totalRepeats++; totalDelta+=Math.Abs(value-previous); }
                        if(localPrevious>=0) { if(localPrevious==value) repeats++; delta+=Math.Abs(value-localPrevious); }
                        previous=localPrevious=value;
                        if(!d.Folded) r.Code[lane]=value;
                    }
                    if(d.Folded) for(int lane=0;lane<16;lane++)
                        r.Code[lane]=(byte)((sum[lane]/Math.Max(1,laneCounts[lane]) + slope[lane]%256 + 256)%256);
                    r.Entropy=Entropy(histogram,r.Length); r.Repetition=r.Length<2?0:(float)repeats/(r.Length-1);
                    r.Transition=r.Length<2?0:(float)delta/((r.Length-1)*255);
                    d.Regions.Add(r);
                }
                if(stream.Length!=length || stream.ReadByte()!=-1) throw new IOException("File changed during reading.");
                sha.TransformFinalBlock(new byte[0],0,0);
                d.Fingerprint=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
            }
            d.Entropy=Entropy(d.Frequencies,length); d.Repetition=length<2?0:(float)totalRepeats/(length-1);
            d.Transition=length<2?0:(float)totalDelta/((length-1)*255);
            return d;
        }
        static float Entropy(long[] histogram,long length)
        { double h=0; if(length>0) foreach(long n in histogram) if(n>0) { double p=(double)n/length; h-=p*Math.Log(p,2); } return (float)(h/8); }
    }
}
