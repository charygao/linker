﻿namespace cmonitor.plugins.tunnel.messenger
{
    public enum TunnelMessengerIds : ushort
    {
        Update = 2000,
        Info = 2001,
        InfoForward = 2002,
        Begin = 2003,
        BeginForward = 2004,

        Fail = 2005,
        FailForward = 2006,


        ExternalIP = 2007,

        None = 2099
    }
}