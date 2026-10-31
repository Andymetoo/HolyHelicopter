using System;
using System.IO;
using System.Text;

namespace FileDNALab.Core
{
    /// <summary>Versioned interpretation envelope around the original, unchanged FDNA-1 source-tape archive.</summary>
    public static class GenomeArchive
    {
        const string Envelope="FDNA-PROGRAM-2";
        public static void Save(Stream stream,Genome genome)
        {
            var limits=genome.ExpressionLimits??new DnaLimits();limits.Validate();
            using(var w=new BinaryWriter(stream,Encoding.UTF8,true))
            using(var nested=new MemoryStream())
            {
                DnaArchive.Save(nested,genome.Dna);
                w.Write(Envelope);w.Write((int)genome.Mode);w.Write(genome.Version);
                w.Write(limits.MaxFileBytes);w.Write(limits.MaxCodons);w.Write(limits.MaxSegments);w.Write(limits.MaxNodes);w.Write(limits.MaxWires);w.Write(limits.MaxMotors);w.Write(limits.MaxDevelopmentSteps);
                w.Write((int)nested.Length);w.Write(nested.ToArray());
            }
        }
        public static Genome Load(Stream stream)
        {
            if(!stream.CanSeek||stream.Length>2*1024*1024)throw new IOException("Genome archive exceeds 2 MiB.");
            long start=stream.Position;
            using(var r=new BinaryReader(stream,Encoding.UTF8,true))
            {
                string format=r.ReadString();stream.Position=start;
                if(format==DnaDocument.Version)
                {
                    // An old archive is always Legacy; opening it never opts into a new language.
                    return Genetics.Express(DnaArchive.Load(stream),GeneticsMode.Legacy);
                }
                if(r.ReadString()!=Envelope)throw new IOException("Unsupported genome archive format.");
                int mode=r.ReadInt32();string version=r.ReadString();
                if(mode<0||mode>1||version!=(mode==0?DnaDocument.Version:DevelopmentalInterpreter.Version))throw new IOException("Unknown interpreter version; this archive was not reinterpreted.");
                var limits=new DnaLimits {MaxFileBytes=r.ReadInt64(),MaxCodons=r.ReadInt32(),MaxSegments=r.ReadInt32(),MaxNodes=r.ReadInt32(),MaxWires=r.ReadInt32(),MaxMotors=r.ReadInt32(),MaxDevelopmentSteps=r.ReadInt32()};limits.Validate();
                int size=r.ReadInt32();if(size<0||size>1024*1024||size>stream.Length-stream.Position)throw new IOException("Invalid nested DNA archive size.");
                var data=r.ReadBytes(size);if(stream.Position!=stream.Length)throw new IOException("Unexpected trailing archive data.");
                using(var nested=new MemoryStream(data,false))return Genetics.Express(DnaArchive.Load(nested),(GeneticsMode)mode,limits);
            }
        }
    }
}
