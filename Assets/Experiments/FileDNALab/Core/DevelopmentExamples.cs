using System.Collections.Generic;

namespace FileDNALab.Core
{
    /// <summary>Explicit grammar fixtures, never selected inside the interpreter. Both modes receive these identical raw bytes.</summary>
    public static class DevelopmentExamples
    {
        public static readonly string[] Names={"Undivided sphere.bytes","Local rhythm tape.bytes","Branch return tape.bytes","Asymmetric growth.bytes","Obstructed curl.bytes","Elastic modules.bytes","Silent material.bytes"};
        public static byte[] C(int opcode,params int[] arguments)
        {var b=new byte[16];for(int i=1;i<15;i++)b[i]=128;b[0]=(byte)opcode;b[15]=0;for(int i=0;i<arguments.Length&&i<14;i++)b[i+1]=(byte)arguments[i];return b;}
        static byte[] Root(int shape=1,int width=100,int height=30)
        {return ExampleDna.Codon(0,shape,width,height,100,190,10,128,0,70,90,180,50,60,128,0);}
        static void Add(List<byte> tape,params byte[][] instructions) {foreach(var i in instructions)tape.AddRange(i);}
        public static byte[] Get(int index)
        {
            var tape=new List<byte>();
            switch(index%Names.Length)
            {
                case 0:Add(tape,Root(0,255,255));break;
                case 1:
                    Add(tape,Root(),C(7,190,180,128,0),C(9,0,54,0,166,127,32),C(11,0,128,230),C(1));
                    Add(tape,C(13,0,3),C(13,1,0));break;
                case 2:
                    Add(tape,Root(2,115,65),C(8,0,2,128,128,127,50));
                    foreach(int angle in new[]{60,190,255,128})Add(tape,C(2,angle,150,150),C(1),C(1),C(3));
                    break;
                case 3:
                    Add(tape,Root(0,200,200),C(5,170,200,100,180,145),C(4,160,70,2),C(2,160,130,110),C(7,245,210,240,0),C(1),C(1),C(3),C(4,70,180,3),C(2,15,150,150),C(1));break;
                case 4:
                    Add(tape,Root());for(int i=0;i<18;i++)Add(tape,C(1,128,128,255,255));break;
                case 5:
                    Add(tape,Root(0,110,110),C(6,1,128,100,160,150,190,0,90),C(7,80,130,128,0),C(9,0,35,20,140,127,100),C(11,0,128,230));
                    foreach(int angle in new[]{70,190,255})Add(tape,C(2,angle,170,170),C(1),C(1),C(3));break;
                default:return new byte[512];
            }
            return tape.ToArray();
        }
        public static int SampleCount {get {return ExampleDna.Names.Length+Names.Length;}}
        public static string SampleName(int index) {return index<ExampleDna.Names.Length?ExampleDna.Names[index]:Names[index-ExampleDna.Names.Length];}
        public static byte[] SampleBytes(int index) {return index<ExampleDna.Names.Length?ExampleDna.Get(index):Get(index-ExampleDna.Names.Length);}
    }
}
