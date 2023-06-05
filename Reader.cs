using System.Text;

namespace MyApp
{
    class Reader
    {
        byte[] data;
        int offset;
        public Reader(byte[] data)
        {
            this.data = data;
            this.offset = 0;
        }

        public int Remaining()
        {
            return data.Length - offset;
        }

        public void Skip(int bytes)
        {
            offset += bytes;
        }

        public byte ReadByte()
        {
            return data[offset++];
        }

        public byte[] ReadBytes(int count)
        {
            var dest = new byte[count];
            Array.Copy(data, offset, dest, 0, count);
            offset += count;
            return dest;
        }

        public string ReadString()
        {
            int len = 0;
            for (; len < data.Length; len++)
            {
                if (data[offset + len] == 0) break;
            }
            string str = Encoding.ASCII.GetString(data, offset, len);
            offset += len + 1;
            return str;
        }

        public string ReadUTF8String()
        {
            int len = 0;
            for (; len < data.Length; len++)
            {
                if (data[offset + len] == 0) break;
            }
            string str = Encoding.UTF8.GetString(data, offset, len);
            offset += len + 1;
            return str;
        }

        public short ReadShort()
        {
            short value = (short)(data[offset] + (data[offset + 1] << 8));
            offset += 2;
            return value;
        }

        public ushort ReadUShortBE()
        {
            ushort value = (ushort)(data[offset + 1] + (data[offset] << 8));
            offset += 2;
            return value;
        }

        public long ReadLong()
        {
            long value = (long)(data[offset] + (data[offset + 1] << 8) + (data[offset + 2] << 16) + (data[offset + 3] << 24)
             + (data[offset + 4] << 32) + (data[offset + 5] << 40) + (data[offset + 6] << 48) + (data[offset + 7] << 56));
            offset += 8;
            return value;
        }
    }
}