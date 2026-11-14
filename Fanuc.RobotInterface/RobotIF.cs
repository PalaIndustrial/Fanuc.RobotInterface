using Fanuc.SRTP;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fanuc.RobotInterface
{
    public partial class RobotIF : IRobotIF, INotifyPropertyChanged
    {
        public const int DEFAULT_PORT = 60008;
        public const int DEFAULT_HTTP_PORT = 80;
        public const int DEFAULT_TIMEOUT = 10000;

        /// <summary>
        /// Configures the controller host and its independent SRTP and HTTP ports.
        /// HTTP helpers can be used without connecting SRTP.
        /// </summary>
        public RobotIF(string host, ushort srtpPort = DEFAULT_PORT, ushort httpPort = DEFAULT_HTTP_PORT)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("A controller host is required.", nameof(host));
            if (srtpPort == 0)
                throw new ArgumentOutOfRangeException(nameof(srtpPort));
            if (httpPort == 0)
                throw new ArgumentOutOfRangeException(nameof(httpPort));
            Host = host;
            SrtpPort = srtpPort;
            HttpPort = httpPort;
        }

        public string Host { get; }
        public ushort SrtpPort { get; }
        public ushort HttpPort { get; }

        // Disconnect does not take OperationLock, so it can interrupt blocked I/O.
        private readonly object OperationLock = new object();
        private readonly object StateLock = new object();
        private readonly object ClientDisposeLock = new object();
        private TcpClient TcpClient;
        private bool ConnectionReady;
        private long ConnectionVersion;
        private bool FailureNotificationPending;

        public bool IsConnected
        {
            get
            {
                lock (StateLock)
                    return ConnectionReady && (TcpClient?.Connected ?? false);
            }
        }

        private byte SequenceNumber;

        /// <summary>
        /// Connects to the controller using the timeout in milliseconds for TCP
        /// connection and each socket send/receive. Zero disables these timeouts.
        /// The timeout is approximate and is not an overall handshake deadline;
        /// hostname resolution may continue after a timeout on older targets.
        /// </summary>
        public void Connect(int timeout = DEFAULT_TIMEOUT)
        {
            long version;
            lock (StateLock)
                version = ConnectionVersion;
            lock (OperationLock)
            {
                TcpClient client = null;
                TcpClient oldClient;
                lock (StateLock)
                {
                    if (version != ConnectionVersion)
                        throw new IOException("Connection changed while waiting to connect.");
                    if (ConnectionReady && (TcpClient?.Connected ?? false))
                        throw new IOException("Already connected.");
                    oldClient = TcpClient;
                    TcpClient = null;
                    ConnectionReady = false;
                    version = ++ConnectionVersion;
                }
                try
                {
                    DisposeClient(oldClient);

                    client = new TcpClient();
                    lock (StateLock)
                    {
                        if (version != ConnectionVersion)
                            throw new IOException("Connection was cancelled.");
                        TcpClient = client;
                    }
                    client.ReceiveTimeout = timeout;
                    client.SendTimeout = timeout;
                    client.Client.NoDelay = true;
                    ConnectWithTimeout(client, Host, SrtpPort, timeout);

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

                    lock (StateLock)
                    {
                        if (version != ConnectionVersion || TcpClient != client)
                            throw new IOException("Connection was cancelled.");
                        ConnectionReady = true;
                    }
                }
                catch (Exception ex)
                {
                    lock (StateLock)
                    {
                        if (TcpClient == client && client != null)
                        {
                            TcpClient = null;
                            ConnectionReady = false;
                            ++ConnectionVersion;
                        }
                    }
                    DisposeClient(client);
                    throw new IOException($"Connecting to {Host}:{SrtpPort} failed.", ex);
                }
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
        }

        private void DisposeClient(TcpClient client)
        {
            if (client == null)
                return;
            lock (ClientDisposeLock)
                client.Dispose();
        }

        private void ConnectWithTimeout(TcpClient client, string host, int port, int timeout)
        {
#if NET5_0_OR_GREATER
            using var cancellation = new CancellationTokenSource();
            if (timeout > 0)
                cancellation.CancelAfter(timeout);
            try
            {
                client.ConnectAsync(host, port, cancellation.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException ex) when (cancellation.IsCancellationRequested)
            {
                DisposeClient(client);
                throw new TimeoutException($"TCP connection timed out after {timeout} ms.", ex);
            }
#else
            var connect = client.ConnectAsync(host, port);
            if (timeout > 0)
            {
                using var cancellation = new CancellationTokenSource();
                var delay = Task.Delay(timeout, cancellation.Token);
                if (Task.WhenAny(connect, delay).GetAwaiter().GetResult() != connect)
                {
                    // Closing the client can fault the pending operation later.
                    _ = connect.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    DisposeClient(client);
                    throw new TimeoutException($"TCP connection timed out after {timeout} ms.");
                }
                cancellation.Cancel();
            }
            connect.GetAwaiter().GetResult();
#endif
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void Disconnect()
        {
            TcpClient client;
            bool notify;
            lock (StateLock)
            {
                client = TcpClient;
                TcpClient = null;
                notify = ConnectionReady;
                ConnectionReady = false;
                ++ConnectionVersion;
            }
            DisposeClient(client);
            if (notify)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
        }

        private T RunOperation<T>(Func<T> operation)
        {
            // A queued operation belongs to the connection observed when it was called.
            long version;
            bool ready;
            lock (StateLock)
            {
                version = ConnectionVersion;
                ready = ConnectionReady;
            }
            bool notify = false;
            Exception failure = null;
            try
            {
                lock (OperationLock)
                {
                    try
                    {
                        lock (StateLock)
                            if (version != ConnectionVersion || (!ready && ConnectionReady))
                                throw new IOException("Connection changed while waiting for the operation.");
                        return operation();
                    }
                    finally
                    {
                        notify = FailureNotificationPending;
                        FailureNotificationPending = false;
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                throw;
            }
            finally
            {
                if (notify)
                {
                    try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected))); }
                    catch (Exception ex)
                    {
                        if (failure != null)
                            throw new AggregateException("Transport operation and disconnect notification failed.", failure, ex);
                        throw;
                    }
                }
            }
        }

        private void RunOperation(Action operation) =>
            RunOperation(() => { operation(); return true; });

        public void ClearAlarm() => WriteCommand("CLRALM");

        private static void ValidateIndex(int index)
        {
            if (index < 1 || index > (int)ushort.MaxValue + 1)
                throw new ArgumentOutOfRangeException(nameof(index), "index must be between 1 and 65536.");
        }

        private static void ValidateIndexAndRange(int index, int count, string countParameter)
        {
            ValidateIndex(index);
            if ((long)index - 1 + count > (long)ushort.MaxValue + 1)
                throw new ArgumentOutOfRangeException(countParameter, "The range exceeds the addressable limit.");
        }

        #region IO
        internal bool[] ReadDIO(SegmentSelector selector, int index, ushort count)
            => RunOperation(() => ReadDIOCore(selector, index, count));

        private bool[] ReadDIOCore(SegmentSelector selector, int index, ushort count)
        {
            ValidateIndexAndRange(index, count, nameof(count));
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

            var values = ReadPayload(rr, len / 8, selector)
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
            => RunOperation(() => ReadGIOCore(selector, index, count));

        private ushort[] ReadGIOCore(SegmentSelector selector, int index, ushort count)
        {
            ValidateIndexAndRange(index, count, nameof(count));
            if (count < 1)
                return [];
            if (count > ushort.MaxValue / 2)
                throw new ArgumentOutOfRangeException(nameof(count), "The response exceeds the maximum payload size.");

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

            var payload = ReadPayload(rr, count * 2, selector);
            var values = new ushort[count];
            for (int i = 0; i < count; ++i)
                values[i] = (ushort)((payload[i * 2 + 1] << 8) | payload[i * 2]);
            return values;
        }
        public ushort[] ReadGI(int index, ushort count) => ReadGIO(SegmentSelector.WORD_AQ, index, count);
        public ushort[] ReadGO(int index, ushort count) => ReadGIO(SegmentSelector.WORD_AI, index, count);

        private void WriteDIO(SegmentSelector selector, int index, params bool[] values)
            => RunOperation(() => WriteDIOCore(selector, index, values));

        private void WriteDIOCore(SegmentSelector selector, int index, bool[] values)
        {
            ValidateIndex(index);

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
                var existing = ReadDIOCore(selector, (int)first + 1, 8);
                if (existing.Length != 8)
                    throw new IOException($"Read {selector} returned insufficient data for the write.");
                Array.Copy(existing, 0, bools, 0, front);
                if (back > 0 && len == 8)
                    Array.Copy(existing, 8 - back, bools, bools.Length - back, back);
            }
            if (back > 0 && (front == 0 || len != 8))
            {
                var existing = ReadDIOCore(selector, (int)last - 7, 8);
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
            => RunOperation(() => WriteGIOCore(selector, index, values));

        private void WriteGIOCore(SegmentSelector selector, int index, ushort[] values)
        {
            ValidateIndex(index);

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
            => RunOperation(() => WriteCommandCore(command));

        private void WriteCommandCore(string command)
        {
            if (string.IsNullOrEmpty(command))
                throw new IOException("Cannot send empty command.");

            if (Encoding.ASCII.GetByteCount(command) > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(command), "The command exceeds the maximum payload size.");

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
            => RunOperation(() => WriteSNPXCore(index, values));

        private void WriteSNPXCore(int index, byte[] values)
        {
            var count = values.Length;
            ValidateIndexAndRange(index, count / 2, nameof(values));
            if (count == 0)
                return; // nothing to write

            if (count > ushort.MaxValue || count % 2 != 0)
                throw new ArgumentOutOfRangeException(nameof(values), "The payload must contain an even number of bytes within the maximum payload size.");
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
                throw new IOException($"Write {SegmentSelector.WORD_R} error! Wrong data received, check your request.");
        }

        public byte[] ReadSNPX(int index, ushort count)
            => RunOperation(() => ReadSNPXCore(index, count));

        private byte[] ReadSNPXCore(int index, ushort count)
        {
            ValidateIndexAndRange(index, count / 2, nameof(count));
            if (count < 1)
                return [];
            if (count % 2 != 0)
                throw new ArgumentOutOfRangeException(nameof(count), "The payload length must be an even number of bytes.");

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

            return [.. ReadPayload(rr, count, SegmentSelector.WORD_R).Take(count)];
        }

        private static byte[] ReadPayload(IResponsePacket response, int requiredBytes, SegmentSelector selector)
        {
            if (response.MessageType == MessageType.SHORT_FAILED)
                throw new IOException($"Read {selector} error! Wrong data received, check your request.");

            byte[] payload;
            if (response.MessageType == MessageType.SHORT_ACK && response is ShortResponsePacket)
                payload = response.ActualPayload;
            else if (response.MessageType == MessageType.LONG_ACK)
                payload = response is ExtendedResponsePacket ? response.ActualPayload : Array.Empty<byte>();
            else
                throw new IOException($"Read {selector} returned unexpected response type 0x{(byte)response.MessageType:X2} for its payload format.");

            if (payload.Length < requiredBytes)
                throw new IOException($"Read {selector} returned {payload.Length} payload bytes; expected at least {requiredBytes}.");

            if (response is ExtendedResponsePacket && payload.Length > requiredBytes)
                Trace.TraceInformation($"Read {selector} returned {payload.Length} payload bytes; expected {requiredBytes}. Extra bytes were ignored.");

            return payload;
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
            TcpClient client;
            Socket socket;
            lock (StateLock)
            {
                client = TcpClient;
                socket = client?.Client ?? throw new IOException("Cannot send a request when not connected.");
            }
            byte[] received;
            try
            {
                SendInternal(socket, packet);
                received = ReceiveInternal(socket);
            }
            catch (Exception failure)
            {
                bool dispose = false;
                lock (StateLock)
                {
                    if (TcpClient == client)
                    {
                        TcpClient = null;
                        FailureNotificationPending = ConnectionReady;
                        ConnectionReady = false;
                        ++ConnectionVersion;
                        dispose = true;
                    }
                }
                if (dispose)
                {
                    try { DisposeClient(client); }
                    catch (Exception cleanup)
                    {
                        throw new AggregateException("Transport operation and client cleanup failed.", failure, cleanup);
                    }
                }
                throw;
            }
            return received.Length <= PacketBase.HEADER_LENGTH
                ? new ShortResponsePacket(received)
                : new ExtendedResponsePacket(received);
        }
    }
}