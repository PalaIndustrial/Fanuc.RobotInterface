using Fanuc.SRTP;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace Fanuc.RobotInterface
{
    public partial class RobotIF : IRobotIF, INotifyPropertyChanged
    {
        public const int DEFAULT_PORT = 60008;
        public const int DEFAULT_TIMEOUT = 10000;

        private TcpClient TcpClient;

        public bool IsConnected => TcpClient?.Connected ?? false;

        private byte SequenceNumber;
        private string Host;
        string IRobotIF.Host => Host;

        public void Connect(string host, ushort port, int timeout)
        {
            if (IsConnected)
                throw new IOException("Already connected.");

            try
            {
                Disconnect(); // call disconnect to clean-up states before we connect.

                var client = new TcpClient
                {
                    ReceiveTimeout = timeout,
                    SendTimeout = timeout
                };
                client.Client.NoDelay = true;
                client.Connect(host, port);

                TcpClient = client;
                Host = host;

                SequenceNumber = 0;

                {
                    var rr = SendReceiveInternal(new ShortRequestPacket()
                    {
                        SequenceNumber = SequenceNumber++,
                        PacketType = PacketType.INIT,
                        MessageType = 0,
                        Unknown_8 = 0,
                        Unknown_16 = 0,
                    });

                    // TODO: validate received packet
                    if (rr.MessageType == MessageType.SHORT_FAILED)
                        throw new IOException($"Request {PacketType.INIT} error! Wrong data received.");
                }

                {
                    var rr = SendReceiveInternal(new ShortRequestPacket()
                    {
                        PacketType = PacketType.UNKNOWN_8,
                        SequenceNumber = SequenceNumber++,

                        MailboxSource = 0x00000000,         // FIXME: magic!
                        MailboxDestination = 0x00000e10,    // FIXME: magic!
                        PacketNumber = 1,                   // always 1
                        TotalPacketNumber = 1,              // always 1

                        ServiceRequestCode = ServiceRequestCode.INIT,
                        SegmentSelector = SegmentSelector.INIT,
                    });

                    // TODO: validate received packet
                    if (rr.MessageType == MessageType.SHORT_FAILED)
                        throw new IOException($"Request {PacketType.UNKNOWN_8} error! Wrong data received.");
                }

            }
            catch (Exception ex)
            {
                Disconnect();
                throw new IOException($"Connecting to {host}:{port} failed.", ex);
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void Disconnect()
        {
            if (IsConnected)
            {
                TcpClient?.Close();
                TcpClient?.Dispose();
                TcpClient = null;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
            }
        }

        public void ClearAlarm() => WriteCommand("CLRALM");

        #region IO
        internal bool[] ReadDIO(SegmentSelector selector, int index, ushort count)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");
            if (count < 1)
                return [];

            --index;
            var first = index / 8 * 8;
            var last = (index + count + 7) / 8 * 8;
            var len = last - first;

            if (len > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(count), $"Cannot read {count} inputs at {index}");

            var rr = SendReceiveInternal(new ShortRequestPacket()
            {
                SequenceNumber = SequenceNumber++,

                MailboxSource = 0x00000000,         // FIXME: magic!
                MailboxDestination = 0x00000e10,    // FIXME: magic!
                PacketNumber = 1,                   // always 1
                TotalPacketNumber = 1,              // always 1

                ServiceRequestCode = ServiceRequestCode.READ_SYS_MEM,
                SegmentSelector = selector,

                Index = (ushort)first,
                Count = (ushort)len,
            });

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Read {selector} error! Wrong data received, check your request.");

            var values = rr.ActualPayload
                .Take(len / 8)
                .SelectMany(v => new bool[]
                {
                    ((v >> 0) & 0x01) == 0x01, ((v >> 1) & 0x01) == 0x01, ((v >> 2) & 0x01) == 0x01, ((v >> 3) & 0x01) == 0x01,
                    ((v >> 4) & 0x01) == 0x01, ((v >> 5) & 0x01) == 0x01, ((v >> 6) & 0x01) == 0x01, ((v >> 7) & 0x01) == 0x01,
                })
                .Skip(index - first)
                .Take(count)
                .ToArray();

            return values;
        }
        public bool[] ReadDI(int index, ushort count) => ReadDIO(SegmentSelector.BIT_Q, index, count);
        public bool[] ReadDO(int index, ushort count) => ReadDIO(SegmentSelector.BIT_I, index, count);

        private ushort[] ReadGIO(SegmentSelector selector, int index, ushort count)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");
            if (count < 1)
                return [];

            --index;
            var rr = SendReceiveInternal(new ShortRequestPacket()
            {
                SequenceNumber = SequenceNumber++,

                MailboxSource = 0x00000000,         // FIXME: magic!
                MailboxDestination = 0x00000e10,    // FIXME: magic!
                PacketNumber = 1,                   // always 1
                TotalPacketNumber = 1,              // always 1

                ServiceRequestCode = ServiceRequestCode.READ_SYS_MEM,
                SegmentSelector = selector,

                Index = (ushort)index,
                Count = count,
            });

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Read {selector} error! Wrong data received, check your request.");

            var values = new ushort[count];
            var n = Math.Min(count, rr.ActualPayload.Length / 2);
            for (int i = 0; i < n; ++i)
                values[i] = (ushort)((rr.ActualPayload[i * 2 + 1] << 8) | rr.ActualPayload[i * 2]);
            return values;
        }
        public ushort[] ReadGI(int index, ushort count) => ReadGIO(SegmentSelector.WORD_AQ, index, count);
        public ushort[] ReadGO(int index, ushort count) => ReadGIO(SegmentSelector.WORD_AI, index, count);

        private void WriteDIO(SegmentSelector selector, int index, params bool[] values)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");

            var count = values.Length;
            if (count < 1)
                return; // nothing to write

            var start = (long)index - 1;
            var first = start / 8 * 8;
            var last = (start + count + 7) / 8 * 8;
            var len = last - first;
            if (last > (long)ushort.MaxValue + 1 || len > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(values), "The aligned write exceeds the addressable range.");

            var front = (int)(start - first);
            var back = (int)(last - start - count);
            var bools = new bool[(int)len];
            Array.Copy(values, 0, bools, front, count);
            if (front > 0)
            {
                var existing = ReadDIO(selector, (int)first + 1, 8);
                if (existing.Length != 8)
                    throw new IOException($"Read {selector} returned insufficient data for the write.");
                Array.Copy(existing, 0, bools, 0, front);
                if (back > 0 && len == 8)
                    Array.Copy(existing, 8 - back, bools, bools.Length - back, back);
            }
            if (back > 0 && (front == 0 || len != 8))
            {
                var existing = ReadDIO(selector, (int)last - 7, 8);
                if (existing.Length != 8)
                    throw new IOException($"Read {selector} returned insufficient data for the write.");
                Array.Copy(existing, 8 - back, bools, bools.Length - back, back);
            }
            var bytes = new byte[len / 8];
            for (int i = 0; i < bools.Length; i += 8)
                bytes[i / 8] = (byte)(
                    (bools[i + 0] ? 0x01 : 0x00) |
                    (bools[i + 1] ? 0x02 : 0x00) |
                    (bools[i + 2] ? 0x04 : 0x00) |
                    (bools[i + 3] ? 0x08 : 0x00) |
                    (bools[i + 4] ? 0x10 : 0x00) |
                    (bools[i + 5] ? 0x20 : 0x00) |
                    (bools[i + 6] ? 0x40 : 0x00) |
                    (bools[i + 7] ? 0x80 : 0x00)
                );

            var rr = SendReceiveInternal((bytes.Length <= 6)
                ? new ShortRequestPacket()
                {
                    SequenceNumber = SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = selector,

                    Index = (ushort)first,
                    Count = (ushort)len,

                    Payload = bytes,
                }
                : new ExtendedRequestPacket()
                {
                    SequenceNumber = SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = selector,

                    Index = (ushort)first,
                    Count = (ushort)len,

                    ExtraPayload = bytes,
                });

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Write {selector} error! Wrong data received, check your request.");
        }

        public void WriteDI(int index, params bool[] values) => WriteDIO(SegmentSelector.BIT_Q, index, values);
        public void WriteDO(int index, params bool[] values) => WriteDIO(SegmentSelector.BIT_I, index, values);

        private void WriteGIO(SegmentSelector selector, int index, params ushort[] values)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");

            var count = values.Length;
            if (count < 1)
                return; // nothing to write

            if ((long)index - 1 + count > (long)ushort.MaxValue + 1 || count > ushort.MaxValue / 2)
                throw new ArgumentOutOfRangeException(nameof(values), "The write exceeds the addressable range or payload size.");

            --index;
            var bytes = values.SelectMany(v => new byte[] { (byte)(v & 0xff), (byte)((v >> 8) & 0xff) }).ToArray();

            var rr = SendReceiveInternal((bytes.Length <= 6)
                ? new ShortRequestPacket()
                {
                    SequenceNumber = SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = selector,

                    Index = (ushort)index,
                    Count = (ushort)count,

                    Payload = bytes,
                }
                : new ExtendedRequestPacket()
                {
                    SequenceNumber = SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = selector,

                    Index = (ushort)index,
                    Count = (ushort)count,

                    ExtraPayload = bytes,
                });

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Write {selector} error! Wrong data received, check your request.");
        }
        public void WriteGI(int index, params ushort[] values) => WriteGIO(SegmentSelector.WORD_AQ, index, values);
        public void WriteGO(int index, params ushort[] values) => WriteGIO(SegmentSelector.WORD_AI, index, values);
        #endregion

        #region DataTable
        public void WriteCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
                throw new IOException("Cannot send empty command.");

            var bytes = Encoding.ASCII.GetBytes(command);
            var rr = SendReceiveInternal((bytes.Length <= 6)
                ? new ShortRequestPacket()
                {
                    SequenceNumber = SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = SegmentSelector.BYTE_G,

                    Index = 0,
                    Count = (ushort)bytes.Length,

                    Payload = bytes,
                }
                : new ExtendedRequestPacket()
                {
                    SequenceNumber = SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = SegmentSelector.BYTE_G,

                    Index = 0,
                    Count = (ushort)bytes.Length,

                    ExtraPayload = bytes,
                });

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Write {SegmentSelector.BYTE_G} error! Wrong data received, check your request.");
        }

        public void WriteSNPX(int index, params byte[] values)
        {
            var count = values.Length;
            if (count == 0)
                return; // nothing to write

            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");
            --index;

            var rr = SendReceiveInternal((values.Length <= 6)
                ? new ShortRequestPacket()
                {
                    SequenceNumber = SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = SegmentSelector.WORD_R,

                    Index = (ushort)index,
                    Count = (ushort)(values.Length / 2),

                    Payload = values,
                }
                : new ExtendedRequestPacket()
                {
                    SequenceNumber = SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = SegmentSelector.WORD_R,

                    Index = (ushort)index,
                    Count = (ushort)(values.Length / 2),

                    ExtraPayload = values,
                });

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Write {SegmentSelector.BYTE_G} error! Wrong data received, check your request.");
        }

        public byte[] ReadSNPX(int index, ushort count)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");
            if (count < 1)
                return [];

            --index;

            var rr = SendReceiveInternal(new ShortRequestPacket()
            {
                SequenceNumber = SequenceNumber++,

                MailboxSource = 0x00000000,         // FIXME: magic!
                MailboxDestination = 0x00000e10,    // FIXME: magic!
                PacketNumber = 1,                   // always 1
                TotalPacketNumber = 1,              // always 1

                ServiceRequestCode = ServiceRequestCode.READ_SYS_MEM,
                SegmentSelector = SegmentSelector.WORD_R,

                Index = (ushort)index,
                Count = (ushort)(count / 2),
            });

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Read {SegmentSelector.WORD_R} error! Wrong data received, check your request.");

            return [.. rr.ActualPayload.Take(count)];
        }
        #endregion

        private static void SendInternal(Socket socket, IRequestPacket packet)
        {
            var bytes = packet.GetBytes();
            var offset = 0;
            while (offset < bytes.Length)
            {
                var sent = socket.Send(bytes, offset, bytes.Length - offset, SocketFlags.None);
                if (sent == 0)
                    throw new IOException($"Connection closed while sending a request: {offset} of {bytes.Length} bytes sent.");
                offset += sent;
            }
        }

        private byte[] HeaderBuffer = new byte[PacketBase.HEADER_LENGTH];
        private byte[] PayloadBuffer = [];

        private static void ReceiveExactly(Socket socket, byte[] buffer, int count, string part)
        {
            var offset = 0;
            while (offset < count)
            {
                var received = socket.Receive(buffer, offset, count - offset, SocketFlags.None);
                if (received == 0)
                    throw new EndOfStreamException($"Connection closed while receiving {part}: {offset} of {count} bytes received.");
                offset += received;
            }
        }

        private byte[] ReceiveInternal(Socket socket)
        {
            // Header
            ReceiveExactly(socket, HeaderBuffer, PacketBase.HEADER_LENGTH, "header");

            // Payload
            var xlen = PacketBase.ExtraLengthFromHeaderBuffer(HeaderBuffer);
            if (PayloadBuffer.Length < xlen)
                PayloadBuffer = new byte[xlen];

            ReceiveExactly(socket, PayloadBuffer, xlen, "payload");
            return [.. HeaderBuffer, .. PayloadBuffer.Take(xlen)];
        }

        private IResponsePacket SendReceiveInternal(IRequestPacket packet)
        {
            var socket = TcpClient?.Client ?? throw new IOException("Cannot send a request when not connected.");
            SendInternal(socket, packet);
            var received = ReceiveInternal(socket);
            return received.Length <= PacketBase.HEADER_LENGTH
                ? new ShortResponsePacket(received)
                : new ExtendedResponsePacket(received);
        }
    }
}