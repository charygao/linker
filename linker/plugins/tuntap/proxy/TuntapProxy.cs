﻿using linker.client.config;
using linker.config;
using linker.plugins.relay;
using linker.plugins.tuntap.vea;
using linker.tunnel;
using linker.tunnel.connection;
using linker.tunnel.proxy;
using linker.libs;
using linker.libs.extends;
using linker.libs.socks5;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace linker.plugins.tuntap.proxy
{
    public sealed class TuntapProxy : TunnelProxy
    {
        private readonly TunnelTransfer tunnelTransfer;
        private readonly RelayTransfer relayTransfer;
        private readonly RunningConfig runningConfig;
        private readonly ConfigWrap config;

        private IPEndPoint proxyEP;
        public override IPAddress UdpBindAdress { get; set; }

        private uint maskValue = NetworkHelper.MaskValue(24);
        private readonly ConcurrentDictionary<uint, string> dic = new ConcurrentDictionary<uint, string>();
        private readonly ConcurrentDictionary<string, ITunnelConnection> connections = new ConcurrentDictionary<string, ITunnelConnection>();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> dicLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public TuntapProxy(TunnelTransfer tunnelTransfer, RelayTransfer relayTransfer, RunningConfig runningConfig, ConfigWrap config)
        {
            this.tunnelTransfer = tunnelTransfer;
            this.relayTransfer = relayTransfer;
            this.runningConfig = runningConfig;
            this.config = config;

            Start();
            LoggerHelper.Instance.Info($"start tuntap proxy, listen port : {LocalEndpoint}");

            //监听打洞连接成功
            tunnelTransfer.SetConnectedCallback("tuntap", OnConnected);
            //监听中继连接成功
            relayTransfer.SetConnectedCallback("tuntap", OnConnected);
        }
        public void Start()
        {
            if (proxyEP != null)
            {
                Stop(proxyEP.Port);
            }
            Start(new IPEndPoint(IPAddress.Any, 0));
            proxyEP = new IPEndPoint(IPAddress.Any, LocalEndpoint.Port);
        }

        /// <summary>
        /// 将隧道拦截成功的连接对象缓存起来，下次有连接需要隧道就直接拿，不用再去打洞或中继
        /// </summary>
        /// <param name="connection"></param>
        private void OnConnected(ITunnelConnection connection)
        {
            if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                LoggerHelper.Instance.Warning($"tuntap add connection {connection.GetHashCode()} {connection.ToJson()}");
            connections.AddOrUpdate(connection.RemoteMachineId, connection, (a, b) => connection);
            BindConnectionReceive(connection);
        }

        /// <summary>
        /// 设置IP，等下有连接进来，用IP匹配，才能知道这个连接是要连谁
        /// </summary>
        /// <param name="ips"></param>
        public void SetIPs(List<TuntapVeaLanIPAddressList> ips)
        {
            dic.Clear();
            foreach (var item in ips)
            {
                foreach (var ip in item.IPS)
                {
                    dic.AddOrUpdate(ip.NetWork, item.MachineId, (a, b) => item.MachineId);
                }
            }
            UdpBindAdress = runningConfig.Data.Tuntap.IP;
        }
        public void SetIP(string machineId, uint ip)
        {
            dic.AddOrUpdate(ip, machineId, (a, b) => machineId);

            UdpBindAdress = runningConfig.Data.Tuntap.IP;
        }

        /// <summary>
        /// tcp连接隧道
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        protected override async ValueTask<bool> ConnectTunnelConnection(AsyncUserToken token)
        {
            token.Proxy.TargetEP = null;
            token.Proxy.Rsv = (byte)Socks5EnumStep.Request;

            //步骤，request
            bool result = await ReceiveCommandData(token);
            if (result == false) return true;
            await token.Socket.SendAsync(new byte[] { 0x05, 0x00 });
            token.Proxy.Rsv = (byte)Socks5EnumStep.Command;
            token.Proxy.Data = Helper.EmptyArray;

            //步骤，command
            result = await ReceiveCommandData(token);
            if (result == false)
            {
                return true;
            }
            Socks5EnumRequestCommand command = (Socks5EnumRequestCommand)token.Proxy.Data.Span[1];

            //获取远端地址
            ReadOnlyMemory<byte> ipArray = Socks5Parser.GetRemoteEndPoint(token.Proxy.Data, out Socks5EnumAddressType addressType, out ushort port, out int index);
            //不支持域名 和 IPV6
            if (addressType == Socks5EnumAddressType.Domain || addressType == Socks5EnumAddressType.IPV6)
            {
                byte[] response1 = Socks5Parser.MakeConnectResponse(proxyEP, (byte)Socks5EnumResponseCommand.AddressNotAllow);
                await token.Socket.SendAsync(response1.AsMemory());
                return true;
            }


            token.Proxy.Data = token.Proxy.Data.Slice(index);
            token.TargetIP = BinaryPrimitives.ReadUInt32BigEndian(ipArray.Span);
            //是UDP中继，不做连接操作，等UDP数据过去的时候再绑定
            if (token.TargetIP == 0 || command == Socks5EnumRequestCommand.UdpAssociate)
            {
                await token.Socket.SendAsync(Socks5Parser.MakeConnectResponse(proxyEP, (byte)Socks5EnumResponseCommand.ConnecSuccess).AsMemory());
                return false;
            }

            token.Proxy.TargetEP = new IPEndPoint(new IPAddress(ipArray.Span), port);
            token.Connection = await ConnectTunnel(token.TargetIP);

            Socks5EnumResponseCommand resp = token.Connection != null && token.Connection.Connected ? Socks5EnumResponseCommand.ConnecSuccess : Socks5EnumResponseCommand.NetworkError;
            byte[] response = Socks5Parser.MakeConnectResponse(proxyEP, (byte)resp);
            await token.Socket.SendAsync(response.AsMemory());

            return true;
        }
        /// <summary>
        /// udp连接隧道
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        protected override async ValueTask ConnectTunnelConnection(AsyncUserUdpToken token)
        {
            ReadOnlyMemory<byte> ipArray = Socks5Parser.GetRemoteEndPoint(token.Proxy.Data, out Socks5EnumAddressType addressType, out ushort port, out int index);
            if (addressType == Socks5EnumAddressType.IPV6)
            {
                return;
            }

            token.TargetIP = BinaryPrimitives.ReadUInt32BigEndian(ipArray.Span);
            //只支持本IP段的广播
            if ((token.TargetIP | (~maskValue)) != (runningConfig.Data.Tuntap.IpInt | (~maskValue)))
            {
                return;
            }
            token.Proxy.TargetEP = new IPEndPoint(new IPAddress(ipArray.Span), port);
            //解析出udp包的数据部分
            token.Proxy.Data = Socks5Parser.GetUdpData(token.Proxy.Data);
            /*
            if (ipArray.Span[3] == 255)
            {
                token.Connections = new List<ITunnelConnection>();
                foreach (var item in dic.Values)
                {
                    ITunnelConnection cinnection = await ConnectTunnel(item);
                    if (cinnection != null)
                    {
                        token.Connections.Add(cinnection);
                    }
                }
            }
            else
            {
                
            }
            */

            token.Connection = await ConnectTunnel(token.TargetIP);
        }

        /// <summary>
        /// udp回复消息自定义处理，因为socks5，要打包一些格式才能返回
        /// </summary>
        /// <param name="token"></param>
        /// <param name="asyncUserUdpToken"></param>
        /// <returns></returns>
        protected override async ValueTask<bool> ConnectionReceiveUdp(AsyncUserTunnelToken token, AsyncUserUdpToken asyncUserUdpToken)
        {
            byte[] data = Socks5Parser.MakeUdpResponse(token.Proxy.TargetEP, token.Proxy.Data, out int length);
            try
            {
                await asyncUserUdpToken.SourceSocket.SendToAsync(data.AsMemory(0, length), token.Proxy.SourceEP);
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Error(ex);
                }
            }
            finally
            {
                Socks5Parser.Return(data);
            }
            return true;
        }


        SemaphoreSlim slimGlobal = new SemaphoreSlim(1);
        /// <summary>
        /// 打洞或者中继
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        private async ValueTask<ITunnelConnection> ConnectTunnel(uint ip)
        {
            uint network = ip & maskValue;
            if (dic.TryGetValue(ip, out string machineId) == false && dic.TryGetValue(network, out machineId) == false)
            {
                return null;
            }
            return await ConnectTunnel(machineId);
        }
        /// <summary>
        /// 打洞或者中继
        /// </summary>
        /// <param name="machineId"></param>
        /// <returns></returns>
        private async ValueTask<ITunnelConnection> ConnectTunnel(string machineId)
        {
            if (config.Data.Client.Id == machineId)
            {
                return null;
            }

            if (connections.TryGetValue(machineId, out ITunnelConnection connection) && connection.Connected)
            {
                return connection;
            }

            await slimGlobal.WaitAsync();
            if (dicLocks.TryGetValue(machineId, out SemaphoreSlim slim) == false)
            {
                slim = new SemaphoreSlim(1);
                dicLocks.TryAdd(machineId, slim);
            }
            slimGlobal.Release();

            await slim.WaitAsync();

            try
            {

                if (connections.TryGetValue(machineId, out connection) && connection.Connected)
                {
                    return connection;
                }

                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG) LoggerHelper.Instance.Debug($"tuntap tunnel to {machineId}");

                connection = await tunnelTransfer.ConnectAsync(machineId, "tuntap");
                if (connection != null)
                {
                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG) LoggerHelper.Instance.Debug($"tuntap tunnel success,{connection.ToString()}");
                }
                if (connection == null)
                {
                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG) LoggerHelper.Instance.Debug($"tuntap relay to {machineId}");

                    connection = await relayTransfer.ConnectAsync(config.Data.Client.Id, machineId, "tuntap");
                    if (connection != null)
                    {
                        if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG) LoggerHelper.Instance.Debug($"tuntap relay success,{connection.ToString()}");
                    }
                }
                if (connection != null)
                {
                    connections.AddOrUpdate(machineId, connection, (a, b) => connection);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                slim.Release();
            }
            return connection;
        }

        /// <summary>
        /// 接收socks5完整数据包
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<bool> ReceiveCommandData(AsyncUserToken token)
        {
            int totalLength = token.Proxy.Data.Length;
            EnumProxyValidateDataResult validate = ValidateData(token.Proxy);
            if ((validate & EnumProxyValidateDataResult.TooShort) == EnumProxyValidateDataResult.TooShort)
            {
                //太短
                while ((validate & EnumProxyValidateDataResult.TooShort) == EnumProxyValidateDataResult.TooShort)
                {
                    totalLength += await token.Socket.ReceiveAsync(token.Saea.Buffer.AsMemory(token.Saea.Offset + totalLength), SocketFlags.None);
                    token.Proxy.Data = token.Saea.Buffer.AsMemory(token.Saea.Offset, totalLength);
                    validate = ValidateData(token.Proxy);
                }
            }

            //不短，又不相等，直接关闭连接
            if ((validate & EnumProxyValidateDataResult.Equal) != EnumProxyValidateDataResult.Equal)
            {
                return false;
            }
            return true;
        }
        /// <summary>
        /// 验证socks5数据包完整性
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        private EnumProxyValidateDataResult ValidateData(ProxyInfo info)
        {
            return (Socks5EnumStep)info.Rsv switch
            {
                Socks5EnumStep.Request => Socks5Parser.ValidateRequestData(info.Data),
                Socks5EnumStep.Command => Socks5Parser.ValidateCommandData(info.Data),
                Socks5EnumStep.Auth => Socks5Parser.ValidateAuthData(info.Data, Socks5EnumAuthType.Password),
                Socks5EnumStep.Forward => EnumProxyValidateDataResult.Equal,
                Socks5EnumStep.ForwardUdp => EnumProxyValidateDataResult.Equal,
                _ => EnumProxyValidateDataResult.Equal
            };
        }



        public ConcurrentDictionary<string, ITunnelConnection> GetConnections()
        {
            return connections;
        }
        public void RemoveConnection(string machineId)
        {
            if (connections.TryRemove(machineId, out ITunnelConnection _connection))
            {
                try
                {
                    _connection.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
