using System.Text;

namespace SerialFraming;

// Single consumer only. This teaching protocol permits printable ASCII payloads.
public sealed class CrlfFrameDecoder
{
    private readonly byte[] _payload;
    private int _count;
    private bool _afterCr;

    public CrlfFrameDecoder(int maxPayloadBytes = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayloadBytes);
        _payload = new byte[maxPayloadBytes];
    }

    public int BufferedBytes => _count + (_afterCr ? 1 : 0);

    public IReadOnlyList<string> Feed(ReadOnlySpan<byte> bytes)
    {
        var frames = new List<string>();
        try
        {
            foreach (byte value in bytes)
            {
                if (_afterCr)
                {
                    if (value != 0x0A || _count == 0)
                        throw new FormatException("期待非空 ASCII 帧与 CRLF 结束符。");
                    frames.Add(Encoding.ASCII.GetString(_payload, 0, _count));
                    Reset();
                }
                else if (value == 0x0D)
                {
                    _afterCr = true;
                }
                else
                {
                    if (value is < 0x20 or > 0x7E)
                        throw new FormatException("载荷只允许可打印 ASCII；不接受单独 LF。");
                    if (_count == _payload.Length)
                        throw new FormatException("帧载荷超过上限，终止本次会话。");
                    _payload[_count++] = value;
                }
            }
            return frames;
        }
        catch (FormatException)
        {
            Reset();
            throw;
        }
    }

    public void Reset()
    {
        _count = 0;
        _afterCr = false;
    }
}
