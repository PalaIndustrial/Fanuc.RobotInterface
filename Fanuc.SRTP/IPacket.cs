namespace Fanuc.SRTP;

public interface IPacket
{
    MessageType MessageType { get; }

    byte[] Header { get; }
    byte[] ExtraPayload { get; }

    byte[] GetBytes();
}