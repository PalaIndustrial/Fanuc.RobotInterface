using Fanuc.SRTP;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Schedulers;

namespace Fanuc.RobotInterface
{
    public partial class RobotIF : IRobotIF, INotifyPropertyChanged
    {
        public const int DEFAULT_PORT = 60008;
        public const int DEFAULT_TIMEOUT = 10000;

        private TcpClient _Client;

        public bool IsConnected => _Client?.Connected ?? false;

        private byte _SequenceNumber;
        private string _Host;

        public RobotIF()
        {
        }

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

                _Client = client;
                _Host = host;

                _SequenceNumber = 0;

                {
                    var rr = _SendReceive(new ShortRequestPacket()
                    {
                        SequenceNumber = _SequenceNumber++,
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
                    var rr = _SendReceive(new ShortRequestPacket()
                    {
                        PacketType = PacketType.UNKNOWN_8,
                        SequenceNumber = _SequenceNumber++,

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
                _Client?.Close();
                _Client?.Dispose();
                _Client = null;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
            }
        }

        public void ClearAlarm() => WriteCommand("CLRALM");
        public void ResetAlarm() => ExecuteKCL("reset");

        private static readonly Regex sKclSuccessPattern = new Regex(@"<BODY.*>.*<XMP.*>\s*(?<XMP>.*)\s*</\s*XMP>.*</\s*BODY>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex sKclFailPatter = new Regex(@"<BODY.*>.*ERROR:\s*([^<]*)\s*.*</\s*BODY>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private HttpClient _HttpClient = new HttpClient();

        public string ExecuteKCL(string command)
        {
            var response = _HttpClient.GetAsync($"http://{_Host}/KCL/{command}").Result;
            response.EnsureSuccessStatusCode();

            var content = response.Content.ReadAsStringAsync().Result;
            var m = sKclSuccessPattern.Match(content);
            if (m.Success)
            {
                return m.Groups["XMP"].Value;
            }
            else
            {
                var mm = sKclFailPatter.Match(content);
                if (m.Success)
                    throw new IOException($"Executing KCL \"{command}\" failed: {mm.Groups[1].Value}");
            }
            throw new IOException($"Executing KCL \"{command}\" failed: {content}");
        }

        public string ExecuteKarel(string program)
        {
            var response = _HttpClient.GetAsync($"http://{_Host}/KAREL/{program}").Result;
            response.EnsureSuccessStatusCode();

            return response.Content.ReadAsStringAsync().Result;
        }

        #region IO
        private bool[] _ReadDIO(SegmentSelector selector, int index, ushort count)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");
            if (count < 1)
                return Array.Empty<bool>();

            --index;
            var first = index / 8 * 8;
            var last = (index + count + 7) / 8 * 8;
            var len = last - first;

            if (len > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(count), $"Cannot read {count} inputs at {index}");

            var rr = _SendReceive(new ShortRequestPacket()
            {
                SequenceNumber = _SequenceNumber++,

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
        public bool[] ReadDI(int index, ushort count) => _ReadDIO(SegmentSelector.BIT_Q, index, count);
        public bool[] ReadDO(int index, ushort count) => _ReadDIO(SegmentSelector.BIT_I, index, count);

        private ushort[] _ReadGIO(SegmentSelector selector, int index, ushort count)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");
            if (count < 1)
                return Array.Empty<ushort>();

            --index;
            var rr = _SendReceive(new ShortRequestPacket()
            {
                SequenceNumber = _SequenceNumber++,

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
        public ushort[] ReadGI(int index, ushort count) => _ReadGIO(SegmentSelector.WORD_AQ, index, count);
        public ushort[] ReadGO(int index, ushort count) => _ReadGIO(SegmentSelector.WORD_AI, index, count);

        private void _WriteDIO(SegmentSelector selector, int index, params bool[] values)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");

            var count = values.Length;
            if (count < 1)
                return; // nothing to write

            --index;
            var first = index / 8 * 8;
            var last = (index + count + 7) / 8 * 8;
            var len = last - first;

            var front = index - first;
            var back = len - count - front;

            var bools = new bool[front].Concat(values).Concat(new bool[back]).ToArray();
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

            var req = (bytes.Length <= 6) ?
                (IRequestPacket)new ShortRequestPacket()
                {
                    SequenceNumber = _SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = selector,

                    Index = (ushort)index,
                    Count = (ushort)count,

                    Payload = bytes,
                } :
                (IRequestPacket)new ExtendedRequestPacket()
                {
                    SequenceNumber = _SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = selector,

                    Index = (ushort)index,
                    Count = (ushort)count,

                    ExtraPayload = bytes,
                };
            var rr = _SendReceive(req);

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Write {selector} error! Wrong data received, check your request.");
        }

        public void WriteDI(int index, params bool[] values) => _WriteDIO(SegmentSelector.BIT_Q, index, values);
        public void WriteDO(int index, params bool[] values) => _WriteDIO(SegmentSelector.BIT_I, index, values);

        private void _WriteGIO(SegmentSelector selector, int index, params ushort[] values)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");

            var count = values.Length;
            if (count < 1)
                return; // nothing to write

            --index;
            var bytes = values.SelectMany(v => new byte[] { (byte)(v & 0xff), (byte)((v >> 8) & 0xff) }).ToArray();

            var req = (bytes.Length <= 6) ?
                (IRequestPacket)new ShortRequestPacket()
                {
                    SequenceNumber = _SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = selector,

                    Index = (ushort)index,
                    Count = (ushort)count,

                    Payload = bytes,
                } :
                (IRequestPacket)new ExtendedRequestPacket()
                {
                    SequenceNumber = _SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = selector,

                    Index = (ushort)index,
                    Count = (ushort)count,

                    ExtraPayload = bytes,
                };
            var rr = _SendReceive(req);

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Write {selector} error! Wrong data received, check your request.");
        }
        public void WriteGI(int index, params ushort[] values) => _WriteGIO(SegmentSelector.WORD_AQ, index, values);
        public void WriteGO(int index, params ushort[] values) => _WriteGIO(SegmentSelector.WORD_AI, index, values);
        #endregion

        #region DataTable
        public void Refresh() => throw new NotImplementedException();
        public void Refresh2() => throw new NotImplementedException();

        public void WriteCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
                throw new IOException("Cannot send empty command.");

            var bytes = Encoding.ASCII.GetBytes(command);
            var req = (bytes.Length <= 6) ?
                (IRequestPacket)new ShortRequestPacket()
                {
                    SequenceNumber = _SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = SegmentSelector.BYTE_G,

                    Index = 0,
                    Count = (ushort)bytes.Length,

                    Payload = bytes,
                } :
                (IRequestPacket)new ExtendedRequestPacket()
                {
                    SequenceNumber = _SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = SegmentSelector.BYTE_G,

                    Index = 0,
                    Count = (ushort)bytes.Length,

                    ExtraPayload = bytes,
                };
            var rr = _SendReceive(req);

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

            var req = (values.Length <= 6) ?
                (IRequestPacket)new ShortRequestPacket()
                {
                    SequenceNumber = _SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = SegmentSelector.WORD_R,

                    Index = (ushort)index,
                    Count = (ushort)(values.Length / 2),

                    Payload = values,
                } :
                (IRequestPacket)new ExtendedRequestPacket()
                {
                    SequenceNumber = _SequenceNumber++,

                    MailboxSource = 0x00000000,         // FIXME: magic!
                    MailboxDestination = 0x00000e10,    // FIXME: magic!
                    PacketNumber = 1,                   // always 1
                    TotalPacketNumber = 1,              // always 1

                    ServiceRequestCode = ServiceRequestCode.WRITE_SYS_MEM,
                    SegmentSelector = SegmentSelector.WORD_R,

                    Index = (ushort)index,
                    Count = (ushort)(values.Length / 2),

                    ExtraPayload = values,
                };
            var rr = _SendReceive(req);

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Write {SegmentSelector.BYTE_G} error! Wrong data received, check your request.");
        }

        public byte[] ReadSNPX(int index, ushort count)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be >= 1");
            if (count < 1)
                return Array.Empty<byte>();

            --index;

            var req = new ShortRequestPacket()
            {
                SequenceNumber = _SequenceNumber++,

                MailboxSource = 0x00000000,         // FIXME: magic!
                MailboxDestination = 0x00000e10,    // FIXME: magic!
                PacketNumber = 1,                   // always 1
                TotalPacketNumber = 1,              // always 1

                ServiceRequestCode = ServiceRequestCode.READ_SYS_MEM,
                SegmentSelector = SegmentSelector.WORD_R,

                Index = (ushort)index,
                Count = (ushort)(count / 2),
            };
            var rr = _SendReceive(req);

            // TODO: validate received packet
            if (rr.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Read {SegmentSelector.WORD_R} error! Wrong data received, check your request.");

            return rr.ActualPayload.Take(count).ToArray();
        }
        #endregion

        private void _Send(IRequestPacket packet) => _Client?.Client?.Send(packet.GetBytes());

        private byte[] _HeaderBuffer = new byte[PacketBase.HEADER_LENGTH];
        private byte[] _PayloadBuffer = Array.Empty<byte>();

        private byte[] _Receive()
        {
            // Header
            var hn = _Client.Client.Receive(_HeaderBuffer, PacketBase.HEADER_LENGTH, SocketFlags.None);
            if (hn != PacketBase.HEADER_LENGTH)
                throw new IOException($"Error receiving header, want {PacketBase.HEADER_LENGTH} bytes, got {hn} only.");

            // Payload
            var xlen = PacketBase.ExtraLengthFromHeaderBuffer(_HeaderBuffer);
            if (_PayloadBuffer.Length < xlen)
                _PayloadBuffer = new byte[xlen];

            var offset = 0;
            while (xlen != offset)
                offset += _Client.Client.Receive(_PayloadBuffer, offset, xlen - offset, SocketFlags.None);
            return _HeaderBuffer.Concat(_PayloadBuffer.Take(xlen)).ToArray();
        }

        private IResponsePacket _SendReceive(IRequestPacket packet)
        {
            //#if DEBUG
            //            var sw = Stopwatch.StartNew();
            //#endif
            _Send(packet);
            var r = _Receive();
            var rr = r.Length <= PacketBase.HEADER_LENGTH ? (IResponsePacket)new ShortResponsePacket(r) : (IResponsePacket)new ExtendedResponsePacket(r);
            //#if DEBUG
            //            sw.Stop();
            //            Debug.WriteLine($"{DateTime.Now:yyyy/MM/dd hh:mm:ss.fff}: Receiving {rr.Header.Length + rr.ExtraPayload.Length} took {sw.ElapsedMilliseconds}ms");
            //#endif
            return rr;
        }
    }
}