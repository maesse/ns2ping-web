namespace NS2Ping;
public class NetworkStats
{
    private static DateTime lastReset = DateTime.Now;
    private static int sentBytes = 0;
    private static int receivedBytes = 0;
    private static int sentPackets = 0;
    private static int receivedPackets = 0;
    public static void Reset()
    {
        lastReset = DateTime.Now;
        sentBytes = 0;
        receivedBytes = 0;
        sentPackets = 0;
        receivedPackets = 0;
    }
    public static void SentBytes(int nBytes)
    {
        sentBytes += nBytes;
        sentPackets++;
    }
    public static void ReceivedBytes(int nBytes)
    {
        receivedBytes += nBytes;
        receivedPackets++;
    }
    public static string GetInformation()
    {
        TimeSpan span = DateTime.Now - lastReset;
        var kBpsSent = sentBytes / span.TotalSeconds / 1024;
        var kBpsReceived = receivedBytes / span.TotalSeconds / 1024;

        var sent = sentPackets / span.TotalSeconds;
        var received = receivedPackets / span.TotalSeconds;

        return string.Format("In: {0:0} packets/sec {1:0.##} kB/s -- Out: {2:0} packets/sec {3:0.##} kB/s", received, kBpsReceived, sent, kBpsSent);
    }
}