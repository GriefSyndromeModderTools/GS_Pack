using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GS_Pack
{
    interface IFileContentProvider
    {
        int Length { get; }
        void Read(byte[] data, int offset);
    }

    class Package : IDisposable
    {
        class EncryptArray
        {
            private UInt32 p;
            private UInt32 c;
            private UInt32[] arr;

            private UInt32 _Inner1(UInt32 a, UInt32 b, UInt32 m)
            {
                UInt32 i = (b ^ a) & 0x7FFFFFFE;
                i = (i ^ a) >> 1;
                return i ^ (((b & 1) != 0) ? 0x9908B0DF : 0) ^ m;
            }

            private UInt32 _Inner2()
            {
                if ((--c) == 0)
                {
                    p = 0;
                    c = 0x270;

                    int i = 0;
                    for (; i < 0xE3; ++i)
                    {
                        arr[i] = _Inner1(arr[i], arr[i + 1], arr[i + 0x18D]);
                    }
                    for (; i < 0x26F; ++i)
                    {
                        arr[i] = _Inner1(arr[i], arr[i + 1], arr[i - 0xE3]);
                    }
                    arr[i] = _Inner1(arr[i], arr[0], arr[i - 0xE3]);
                }
                return arr[p++];
            }

            public byte GetNextByte()
            {
                UInt32 ret = _Inner2();
                ret ^= (ret >> 0x0B);
		        ret ^= (ret & 0xFF3A58AD) << 7;
		        ret ^= (ret & 0xFFFFDF8C) << 0x0F;
		        return (byte)(ret ^ (ret >> 0x12));
            }

            public EncryptArray(int len_filetable)
            {
                p = 0;
                c = 1;
                arr = new UInt32[0x270];
                arr[0] = (UInt32)len_filetable + 6;
                for (UInt32 i = 1; i < 0x270; ++i)
                {
                    UInt32 medi = arr[i - 1];
                    arr[i] = ((medi >> 0x1E) ^ medi) * 0x6C078965 + i;
                }
            }
        }

        private static void DoEncryptFirst(byte[] data, int offset, int length)
        {
            EncryptArray arr = new EncryptArray(length);
            for (int i = 0; i < length; ++i)
            {
                data[i + offset] ^= arr.GetNextByte();
            }
        }

        private static void DoEncryptSecond(byte[] data, int offset, int length)
        {
            byte mcl = 0xC5;
            byte mdl = 0x89;
            for (int i = 0; i < length; ++i)
            {
                data[i + offset] ^= mcl;
                mcl += mdl;
                mdl += 0x49;
            }
        }

        private static void EncryptFileHeader(byte[] data, int offset, int length)
        {
            DoEncryptFirst(data, offset, length);
            DoEncryptSecond(data, offset, length);
        }

        class StreamDecryptProvider : IFileContentProvider
        {
            private Stream _Stream;
            private int _Offset, _Length;

            public StreamDecryptProvider(Stream stream, int offset, int length)
            {
                this._Stream = stream;
                this._Offset = offset;
                this._Length = length;
            }

            public int Length
            {
                get { return _Length; }
            }

            public void Read(byte[] data, int offset)
            {
                _Stream.Seek(_Offset, SeekOrigin.Begin);
                _Stream.Read(data, offset, _Length);

                byte mask = (byte)((_Offset >> 1) & 0xFF);
                mask |= 0x23;
                //for (int i = 0; i < _Length; ++i)
                //{
                //    data[offset + i] ^= mask;
                //}
                ByteArrayXor.Xor(data, offset, _Length, mask);
            }

        }

        static class ByteArrayXor
        {
            [StructLayout(LayoutKind.Explicit)]
            struct UnionArray
            {
                [FieldOffset(0)]
                public byte[] Bytes;

                [FieldOffset(0)]
                public int[] Ints;
            }

            public static void Xor(byte[] data, int offset, int length, byte val)
            {
                UnionArray arr = new UnionArray { Bytes = data };
                int val32 = (val << 24 | val << 16 | val << 8 | val);
                int offsetAlign = (offset + 3) / 4;
                int lengthAlign = (length + offset) / 4 - offsetAlign;
                for (int i = offset; i < offsetAlign * 4; ++i)
                {
                    data[i] ^= val;
                }
                for (int i = offsetAlign; i < offsetAlign + lengthAlign; ++i)
                {
                    arr.Ints[i] ^= val32;
                }
                for (int i = (offsetAlign + lengthAlign) * 4; i < offset + length; ++i)
                {
                    data[i] ^= val;
                }
            }
        }

        class MemoryProvider : IFileContentProvider
        {
            private byte[] _Data;
            private int _Offset, _Length;

            public MemoryProvider(byte[] data, int offset, int length, bool copy)
            {
                if (copy)
                {
                    _Data = new byte[length];
                    Array.Copy(data, offset, _Data, 0, length);

                    _Offset = 0;
                    _Length = length;
                }
                else
                {
                    _Data = data;
                    _Offset = offset;
                    _Length = length;
                }
            }

            public MemoryProvider(IFileContentProvider provider)
            {
                int length = provider.Length;

                _Data = new byte[length];
                provider.Read(_Data, 0);

                _Offset = 0;
                _Length = length;
            }

            public int Length
            {
                get { return _Length; }
            }

            public void Read(byte[] data, int offset)
            {
                Array.Copy(_Data, _Offset, data, offset, _Length);
            }
        }

        class FileNameProvider : IFileContentProvider
        {
            private FileInfo _FileInfo;

            public FileNameProvider(string filename)
            {
                this._FileInfo = new FileInfo(filename);
            }

            public int Length
            {
                get { return (int)_FileInfo.Length; }
            }

            public void Read(byte[] data, int offset)
            {
                using (var file = File.OpenRead(_FileInfo.FullName))
                {
                    file.Read(data, offset, Length);
                }
            }
        }

        public Dictionary<string, IFileContentProvider> FileList;
        private Action OnDispose;

        public static Package ReadPackageFile(string filename)
        {
            Stream file = File.OpenRead(filename);
            
            Package ret = new Package();
            ret.OnDispose = file.Dispose;
            
            byte[] buffer = new byte[6];
            file.Read(buffer, 0, 6);
            Int16 a = BitConverter.ToInt16(buffer, 0);
            Int32 b = BitConverter.ToInt32(buffer, 2);

            buffer = new byte[b];
            file.Read(buffer, 0, b);
            EncryptFileHeader(buffer, 0, b);

            ret.FileList = new Dictionary<string, IFileContentProvider>();

            var filenameEncoding = Encoding.GetEncoding(932);
            using (MemoryStream ms = new MemoryStream(buffer))
            {
                using (BinaryReader br = new BinaryReader(ms))
                {
                    for (int i = 0; i < a; ++i)
                    {
                        int offset = br.ReadInt32();
                        int length = br.ReadInt32();
                        byte strLength = br.ReadByte();
                        string str = filenameEncoding.GetString(br.ReadBytes(strLength));

                        ret.FileList.Add(str, new StreamDecryptProvider(file, offset, length));
                    }
                }
            }

            return ret;
        }
        
        public static Package CreateEmptyPackage()
        {
            Package ret = new Package();
            ret.FileList = new Dictionary<string, IFileContentProvider>();
            return ret;
        }

        public static IFileContentProvider CreateFileReference(string filename)
        {
            return new FileNameProvider(filename);
        }

        private Package() { }

        public void MergePackage(Package pkg)
        {
            foreach (var entry in pkg.FileList)
            {
                if (FileList.ContainsKey(entry.Key))
                {
                    throw new Exception("File name collision.");
                }
            }
            foreach (var entry in pkg.FileList)
            {
                FileList.Add(entry.Key, new MemoryProvider(entry.Value));
            }
        }

        public void SavePackageToFile(string filename)
        {
            var filenameEncoding = Encoding.GetEncoding(932);
            List<string> fileNameList = new List<string>();
            List<byte> maskList = new List<byte>();

            //make header
            byte[] headerData;
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms))
                {
                    int fileLength = 2 + 4 + FileList.Sum(file => 4 + 4 + 1 + filenameEncoding.GetByteCount(file.Key));

                    foreach (var file in FileList)
                    {
                        byte encrypt = (byte)((fileLength >> 1) & 0xFF);
                        encrypt |= 0x23;

                        fileNameList.Add(file.Key);
                        maskList.Add(encrypt);

                        bw.Write((int)fileLength);
                        bw.Write((int)file.Value.Length);
                        var strBytes = filenameEncoding.GetBytes(file.Key);
                        bw.Write((byte)strBytes.Length);
                        bw.Write(strBytes);

                        fileLength += file.Value.Length;
                    }

                    headerData = ms.ToArray();
                    EncryptFileHeader(headerData, 0, headerData.Length);
                }
            }

            using (var outputFile = File.OpenWrite(filename))
            {
                using (var bw = new BinaryWriter(outputFile))
                {
                    bw.Write((short)FileList.Count);
                    bw.Write((int)headerData.Length);

                    bw.Write(headerData);

                    int fileLengthMax = FileList.Max(file => file.Value.Length);
                    byte[] buffer = new byte[fileLengthMax];

                    for (int fileIndex = 0; fileIndex < fileNameList.Count; ++fileIndex)
                    {
                        var entry = FileList[fileNameList[fileIndex]];
                        entry.Read(buffer, 0);

                        var encrypt = maskList[fileIndex];
                        for (int i = 0; i < entry.Length; ++i)
                        {
                            buffer[i] ^= encrypt;
                        }

                        bw.Write(buffer, 0, entry.Length);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (OnDispose != null)
            {
                OnDispose();
            }
        }

        private static void Main()
        {
            var fn = @"E:\Games\[game]GRIEFSYNDROME\griefsyndrome\gs03.dat";
            var pkg = Package.ReadPackageFile(fn);
            var file = pkg.FileList.First();
            var str = file.Key;
            var data = file.Value;
            var len = data.Length;
            var bytes = new byte[len];
            data.Read(bytes, 0);
            File.WriteAllBytes(@"E:\__" + str, bytes);
        }
    }
}
