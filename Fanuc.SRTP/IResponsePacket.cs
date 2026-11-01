namespace Fanuc.SRTP;

public interface IResponsePacket : IPacket
{
    byte[] ActualPayload { get; }
}