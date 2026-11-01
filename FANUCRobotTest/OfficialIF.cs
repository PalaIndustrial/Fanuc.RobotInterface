using CommunityToolkit.Mvvm.ComponentModel;
using Fanuc.RobotInterface;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FANUCRobotTest
{
    public partial class OfficialIF : ObservableObject, IRobotIF
    {
        static OfficialIF()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public const ushort DEFAULT_HTTP_PORT = 80;
        public const ushort DEFAULT_FTP_PORT = 22;
        public const ushort DEFAULT_ROBOTIF_PORT = 60008;
        public const int DEFAULT_TIMEOUT = 10000;

        private static HttpClient _HttpClient = new();

        private FRRJIf.Core _Core;
        private string _Host;

        public ushort HttpPort { get; set; } = DEFAULT_HTTP_PORT;
        private ushort FtpPort { get; set; } = DEFAULT_FTP_PORT;

        public bool IsConnected => _Core?.IsConnected ?? false;

        public void Connect(string host, ushort port = DEFAULT_ROBOTIF_PORT, int timeout = DEFAULT_TIMEOUT)
        {
            _Host = host;
            _Core = new FRRJIf.Core
            {
                TimeOutValue = timeout,
                PortNumber = port
            };

            if (!_Core.Connect(host))
                throw new IOException($"Connecting to {host}:{port} failed ({_Core.DebugMessage})");
            OnPropertyChanged(nameof(IsConnected));
        }

        public void Disconnect()
        {
            if (_Core?.Disconnect() == true)
            {
                try { _Core?.DataTable?.Clear(); } catch { }
                try { _Core?.DataTable2?.Clear(); } catch { }
                _Host = null;
                OnPropertyChanged(nameof(IsConnected));
            }
        }

        public void ClearAlarm()
        {
            if (_Core?.ClearAlarm(0) != true)
                throw new IOException($"{_Core.DebugMessage}");
        }

        public void ResetAlarm() => ExecuteKCL("reset");

        private static readonly Regex sKclSuccessPattern = new(
            @"<BODY.*>.*<XMP.*>\s*(?<XMP>.*)\s*</\s*XMP>.*</\s*BODY>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase
        );
        private static readonly Regex sKclFailPatter = new(
            @"<BODY.*>.*ERROR:\s*([^<]*)\s*.*</\s*BODY>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase
        );

        //private async Task<string> ExecKcl(string command)
        //{
        //    if (!IsConnected)
        //        throw new IOException("Not connected");

        //    var response = await _HttpClient.GetAsync($"http://{_Host}:{HttpPort}/KCL/{command}").ConfigureAwait(false);
        //    response.EnsureSuccessStatusCode();

        //    var content = await response.Content.ReadAsStringAsync();
        //    var m = sKclSuccessPattern.Match(content);
        //    if (m.Success)
        //    {
        //        return m.Groups["XMP"].Value;
        //    }
        //    else
        //    {
        //        var mm = sKclFailPatter.Match(content);
        //        if (m.Success)
        //            throw new IOException($"Executing KCL  \"{command}\" failed: {mm.Groups[1].Value}");
        //    }
        //    throw new IOException($"Executing KCL  \"{command}\" failed: {content}");
        //}
        //private async Task<string> ExecKarel(string command)
        //{
        //    if (!IsConnected)
        //        throw new IOException("Not connected");

        //    var response = await _HttpClient.GetAsync($"http://{_Host}:{HttpPort}/KAREL/{command}").ConfigureAwait(false);
        //    response.EnsureSuccessStatusCode();

        //    return await response.Content.ReadAsStringAsync();
        //}

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
        public bool[] ReadDI(int index, ushort count) => _ReadSDI(index, count);
        public bool[] ReadDO(int index, ushort count) => _ReadSDO(index, count);


        public bool[] _ReadUI(int index, ushort count)
        {
            Array buffer = new short[count];
            if (!_Core.ReadUI(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((short[])buffer).Select(v => v != 0).ToArray();
        }
        public bool[] _ReadUO(int index, ushort count)
        {
            Array buffer = new short[count];
            if (!_Core.ReadUO(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((short[])buffer).Select(v => v != 0).ToArray();
        }
        public bool[] _ReadSI(int index, ushort count)
        {
            Array buffer = new short[count];
            if (!_Core.ReadSI(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((short[])buffer).Select(v => v != 0).ToArray();
        }
        public bool[] _ReadSO(int index, ushort count)
        {
            Array buffer = new short[count];
            if (!_Core.ReadSO(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((short[])buffer).Select(v => v != 0).ToArray();
        }
        public bool[] _ReadSDI(int index, ushort count)
        {
            Array buffer = new short[count];
            if (!_Core.ReadSDI(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((short[])buffer).Select(v => v != 0).ToArray();
        }
        public bool[] _ReadSDO(int index, ushort count)
        {
            Array buffer = new short[count];
            if (!_Core.ReadSDO(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((short[])buffer).Select(v => v != 0).ToArray();
        }
        public bool[] _ReadRDI(int index, ushort count)
        {
            Array buffer = new short[count];
            if (!_Core.ReadRDI(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((short[])buffer).Select(v => v != 0).ToArray();
        }
        public bool[] _ReadRDO(int index, ushort count)
        {
            Array buffer = new short[count];
            if (!_Core.ReadRDO(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((short[])buffer).Select(v => v != 0).ToArray();
        }

        public ushort[] ReadGI(int index, ushort count) => _ReadGI(index, count);
        public ushort[] ReadGO(int index, ushort count) => _ReadGO(index, count);

        public ushort[] _ReadGI(int index, ushort count)
        {
            Array buffer = new int[count];
            if (!_Core.ReadGI(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((int[])buffer).Select(v => (ushort)(v & 0x0000ffff)).ToArray();
        }
        public ushort[] _ReadGO(int index, ushort count)
        {
            Array buffer = new int[count];
            if (!_Core.ReadGO(index, ref buffer, count))
                throw new IOException($"{_Core.DebugMessage}");
            return ((int[])buffer).Select(v => (ushort)(v & 0x0000ffff)).ToArray();
        }

        public void WriteDI(int index, params bool[] values) => _WriteSDI(index, values);
        public void WriteDO(int index, params bool[] values) => _WriteSDO(index, values);

        public void _WriteUI(int index, params bool[] values)
        {
            var v = values.Select(v => (short)(v ? 1 : 0)).ToArray();
            if (!_Core.WriteUI(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }
        public void _WriteUO(int index, params bool[] values)
        {
            var v = values.Select(v => (short)(v ? 1 : 0)).ToArray();
            if (!_Core.WriteUO(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }
        public void _WriteSI(int index, params bool[] values)
        {
            var v = values.Select(v => (short)(v ? 1 : 0)).ToArray();
            if (!_Core.WriteSI(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }
        public void _WriteSO(int index, params bool[] values)
        {
            var v = values.Select(v => (short)(v ? 1 : 0)).ToArray();
            if (!_Core.WriteSO(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }
        public void _WriteSDI(int index, params bool[] values)
        {
            var v = values.Select(v => (short)(v ? 1 : 0)).ToArray();
            if (!_Core.WriteSDI(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }
        public void _WriteSDO(int index, params bool[] values)
        {
            var v = values.Select(v => (short)(v ? 1 : 0)).ToArray();
            if (!_Core.WriteSDO(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }
        public void _WriteRDI(int index, params bool[] values)
        {
            var v = values.Select(v => (short)(v ? 1 : 0)).ToArray();
            if (!_Core.WriteRDI(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }
        public void _WriteRDO(int index, params bool[] values)
        {
            var v = values.Select(v => (short)(v ? 1 : 0)).ToArray();
            if (!_Core.WriteRDO(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }

        public void WriteGI(int index, params ushort[] values) => _WriteGI(index, values);
        public void WriteGO(int index, params ushort[] values) => _WriteGO(index, values);

        public void _WriteGI(int index, params ushort[] values)
        {
            var v = values.Select(v => (int)v).ToArray();
            if (!_Core.WriteGI(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }
        public void _WriteGO(int index, params ushort[] values)
        {
            var v = values.Select(v => (int)v).ToArray();
            if (!_Core.WriteGO(index, v, v.Length))
                throw new IOException($"{_Core.DebugMessage}");
        }
        #endregion

        #region DataTable
        public void Refresh()
        {
            if (!_Core.DataTable.Refresh())
                throw new IOException($"{_Core.DebugMessage}");
        }

        public void Refresh2()
        {
            if (!_Core.DataTable2.Refresh())
                throw new IOException($"{_Core.DebugMessage}");
        }

        // read / write are crappy because the lib left crappy interfaces.
        // ReadRShhort() and WriteRShort() doesn't read {count} shorts as it should.
        public void WriteCommand(string command)
        {
            var lib = _Core.GetLib();
            lib.SendCommand(command);
        }

        public void WriteSNPX(int index, params byte[] bytes)
        {
            var lib = _Core.GetLib();
            if (lib == null)
                return;

            var values = new short[bytes.Length / 2];
            for (int i = 0; i < values.Length; ++i)
                values[i] = (short)(bytes[i * 2] | bytes[i * 2 + 1] << 8);

            var begin = index;
            var end = index + values.Length;

            for (int i = begin; i < end; ++i)
            {
                var ret = lib.WriteRShort((short)i, ref values[i - begin], 1);
                if (ret < 0)
                    throw new Exception($"WriteRShort() returned {ret}");
            }
        }

        public byte[] ReadSNPX(int index, ushort count)
        {
            var lib = _Core.GetLib();
            if (lib == null)
                return new byte[count];

            var values = new short[count / 2];

            var begin = index;
            var end = index + count / 2;

            for (int i = begin; i < end; ++i)
            {
                var ret = lib.ReadRShort((short)i, ref values[i - begin], 1);
                if (ret < 0)
                    throw new Exception($"ReadRShort() returned {ret}");
            }

            var bytes = values
                .SelectMany(v => new byte[] { (byte)(v & 0xff), (byte)((v >> 8) & 0xff) })
                .ToArray();

            return bytes;
        }
        #endregion
    }
}
