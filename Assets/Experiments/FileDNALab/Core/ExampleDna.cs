using System;
using System.Collections.Generic;
using System.Text;

namespace FileDNALab.Core
{
    /// <summary>Raw test inputs. Any pseudorandomness lives in fixtures, never in the decoder.</summary>
    public static class ExampleDna
    {
        public static readonly string[] Names={ "Recipe.txt", "Articulated tape.bin", "Branch circuit.bin", "Zero byte.bin", "Incremental.bin", "Noise.bin", "Empty.bin" };
        public static byte[] Get(int index)
        {
            switch(index%Names.Length)
            {
                case 0:return Encoding.UTF8.GetBytes("CROCKPOT RECIPE\nPut onions, carrots, potatoes and beans into the pot.\nStir slowly. Wait. Do absolutely nothing for six hours.\nSeason with salt. This recipe does not know it has become an organism.\n"+
                    "Every byte is an instruction or an argument. No legs are promised.\nRepeat the instructions until dinner arrives.\n");
                case 1:return Tape(false);
                case 2:return Tape(true);
                case 3:return new byte[] { 0 };
                case 4:var increment=new byte[3072];for(int i=0;i<increment.Length;i++)increment[i]=(byte)i;return increment;
                case 5:return Noise(2048,715);
                default:return new byte[0];
            }
        }
        public static byte[] Noise(int length,uint seed)
        { var a=new byte[length];uint state=seed+1;for(int i=0;i<length;i++) { state^=state<<13;state^=state>>17;state^=state<<5;a[i]=(byte)state; }return a; }
        static byte[] Tape(bool branch)
        {
            var result=new List<byte>();
            // These are explicitly authored grammar demonstrations, not creature templates in the interpreter.
            result.AddRange(Codon(0,0,0,110,100,80,160,4,10,0,150,127,127,150,250,0));
            for(int i=0;i<9;i++)
            {
                result.AddRange(Codon(branch?2:1,branch?0:i,1,100,35+i*4,75,190,12,20,0,branch?i*27:135,127,127,200,250,0));
                result.AddRange(Codon(6,i+1,80+i*9,120,127,0,0,0,0,0,0,0,0,0,0,0));
                result.AddRange(Codon(8,i+1,0,127,28,20*i,90,0,0,0,0,0,0,0,0,0));
                result.AddRange(Codon(11,i,i,230,0,0,0,0,0,0,0,0,0,0,0,0));
            }
            return result.ToArray();
        }
        public static byte[] Codon(params int[] values)
        {var b=new byte[16];for(int i=0;i<Math.Min(16,values.Length);i++)b[i]=(byte)values[i];return b;}
        public static IEnumerable<KeyValuePair<string,byte[]>> Corpus(int count=224)
        {
            for(int i=0;i<Names.Length;i++)yield return new KeyValuePair<string,byte[]>(Names[i],Get(i));
            for(int i=Names.Length;i<count;i++)
            {
                int[] lengths={1,7,15,16,17,63,128,511,1024,4096,8192,8193,65536,1048576};int size=lengths[i%lengths.Length];byte[] b;
                switch(i%7)
                {
                    case 0:b=new byte[size];for(int j=0;j<size;j++)b[j]=(byte)(i*3);break;
                    case 1:b=new byte[size];for(int j=0;j<size;j++)b[j]=(byte)(j+i);break;
                    case 2:b=new byte[size];for(int j=0;j<size;j++)b[j]=(byte)((j%11)*17+i);break;
                    case 3:b=new byte[size];var text=Encoding.UTF8.GetBytes("FILE DNA laboratory, sequence "+i+"; gravity has no opinions.\n");for(int j=0;j<size;j++)b[j]=text[j%text.Length];break;
                    case 4:b=Noise(size,(uint)i*99);for(int j=0;j<size;j++)if(j%128<112)b[j]=0;break;
                    default:b=Noise(size,(uint)i*77);break;
                }
                yield return new KeyValuePair<string,byte[]>("corpus-"+i+".bin",b);
            }
        }
    }
}
