using System.Text;

public class Writer
{
    public static byte[] Append(byte[] source, byte[] append)
    {
        byte[] dest = new byte[source.Length + append.Length];
        Buffer.BlockCopy(source, 0, dest, 0, source.Length);
        Buffer.BlockCopy(append, 0, dest, source.Length, append.Length);
        return dest;
    }

    public static byte[] AppendCString(byte[] source, string str)
    {
        byte[] append = Encoding.ASCII.GetBytes(str);
        byte[] dest = new byte[source.Length + str.Length + 1];
        Buffer.BlockCopy(source, 0, dest, 0, source.Length);
        Buffer.BlockCopy(append, 0, dest, source.Length, append.Length);
        dest[source.Length + append.Length] = 0;
        return dest;
    }

}