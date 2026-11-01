namespace Fanuc.SRTP;

public enum ServiceRequestCode : byte
{
    PLC_SHORT_STATUS = 0x00,       // PLC short status request
    GET_PROGNAME = 0x03,           // get control program names
    READ_SYS_MEM = 0x04,           // read system memory
    READ_TASK_MEM = 0x05,          // read task memroy
    READ_PROG_MEM = 0x06,          // read program memory
    WRITE_SYS_MEM = 0x07,          // write system memory
    WRITE_TASK_MEM = 0x08,         // write task memory
    WRITE_PROG_MEM = 0x09,         // write program block memory
    PROG_LOGON = 0x20,             // programmer logon
    CHANGE_PRIV = 0x21,            // change PLC CPU privilege level
    SET_CPU_ID = 0x22,             // set control ID (CPU ID)
    SET_PLC_RUN = 0x23,            // set PLC (run vs stop)
    SET_PLC_TIME = 0x24,           // set PLC time / date
    GET_PKC_TIME = 0x25,           // get PLC time / data
    GET_FAULT = 0x38,              // get fault table
    CLR_FAULT = 0x39,              // clear fault table
    PROG_STORE = 0x3f,             // program store (upload from PLC)
    PROG_LOAD = 0x40,              // program load (download to PLC)
    GET_INFO = 0x43,               // get controller type and id information
    TOGGLE_FORCE_SYS_MEM = 0x44,   // toggle force system memory

    INIT = 0x4f,                   // Unknown request used in init
}