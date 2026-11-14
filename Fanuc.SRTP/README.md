# SRTP packet layout

The tables show the layouts implemented by this library, not a complete SRTP
specification. Offsets are **decimal byte offsets**, starting at zero. Multi-byte
integers are little-endian. Unknown fields are not necessarily unused.

## Frame

| Offset | Field | Size |
|---|---|---|
| 0-55 | Header | 56 bytes |
| 56 onward | Extra payload | Length declared in header bytes 4-5 |

The complete frame is read using the declared extra length, regardless of the
message type. Short packets carry data inside the header; extended packets
carry data after it.

`ExtraPayload` sets the declared byte length and accepts at most 65,535 bytes.
An extended request's `Count` is independent of that length: for example, a
word write may have a count of 2 and a payload length of 4 bytes.

## Common header (bytes 0-39)

 | Offset  | Field
 |---------|----------------------
 |  `0- 1` | Packet type
 |  `2`    | Sequence number
 |  `3`    | Unknown
 |  `4- 5` | Extra payload length
 |  `6- 7` | Unknown
 |  `8-11` | Unknown_8
 | `12-15` | Unknown
 | `16-19` | Unknown_16
 | `20-25` | Unknown
 | `26-28` | Second, minute, hour
 | `29`    | Unknown
 | `30`    | Second sequence byte
 | `31`    | Message type
 | `32-35` | Mailbox source
 | `36-39` | Mailbox destination

Setting `SequenceNumber` writes both bytes 2 and 30; the library does not
validate their relationship in responses.

## Header tail (bytes 40-55)

| Offset  | Short request                | Short response               |
|---------|------------------------------|------------------------------|
| `40`    | Packet number                | Packet number                |
| `41`    | Total packet number          | Total packet number          |
| `42`    | Service request code         | Unknown                      |
| `43`    | Segment selector             | Unknown                      |
| `44-45` | Index                        | Inline payload (bytes 44-49) |
| `46-47` | Count                        | Inline payload (continued)   |
| `48-49` | Inline payload (bytes 48-53) | Inline payload (continued)   |
| `50-53` | Inline payload (continued)   | Unknown                      |
| `54-55` | Unknown                      | Unknown                      |

| Offset  | Extended request         | Extended response   |
|---------|--------------------------|---------------------|
| `40`    | Packet number            | Packet number       |
| `41`    | Total packet number      | Total packet number |
| `42-47` | Unknown                  | Other header fields |
| `48`    | Packet number copy       | Other header fields |
| `49`    | Total packet number copy | Other header fields |
| `50`    | Service request code     | Other header fields |
| `51`    | Segment selector         | Other header fields |
| `52-53` | Index                    | Other header fields |
| `54-55` | Count                    | Other header fields |
| `56+++` | Extra payload            | Extra payload       |

For extended packets, the packet number setters also write bytes 48 and 49;
the relationship of response fields is not validated. Short response payloads
always expose six bytes, so padding cannot always be distinguished from data.

## Message types

| Value  | Name           | Typical use            |
|--------|----------------|------------------------|
| `0xC0` | `SHORT`        | Short request          |
| `0x80` | `LONG`         | Extended request       |
| `0xD4` | `SHORT_ACK`    | Short read response    |
| `0x94` | `LONG_ACK`     | Extended read response |
| `0xD1` | `SHORT_FAILED` | Failure response       |

A short request can receive an extended response. Handshake and write response
semantics have not been fully verified; see the project-level `TODO.md`.
