﻿using link.libs.web;

namespace link.plugins.capi
{
    public interface IWebClientServer : IWebServer
    {
    }

    /// <summary>
    /// 本地web管理端服务器
    /// </summary>
    public sealed class WebClientServer : WebServer, IWebClientServer
    {
    }

}