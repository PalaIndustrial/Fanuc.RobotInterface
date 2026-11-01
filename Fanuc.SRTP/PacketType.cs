using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Fanuc.SRTP;

public enum PacketType : ushort
{
    INIT = 0x00,

    REQUEST = 0x02,
    ACK = 0x03,

    UNKNOWN_8 = 0x08,
}