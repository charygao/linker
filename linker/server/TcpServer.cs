﻿using linker.libs;
using linker.libs.extends;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace linker.server
{
    public sealed class TcpServer
    {
        private Socket socket;
        private Socket socketUdp;
        private CancellationTokenSource cancellationTokenSource;
        private X509Certificate serverCertificate;

        private readonly IConnectionReceiveCallback connectionReceiveCallback;
        public TcpServer(MessengerResolver connectionReceiveCallback)
        {
            cancellationTokenSource = new CancellationTokenSource();
            this.connectionReceiveCallback = connectionReceiveCallback;
        }

        public void Init(string certificate, string password)
        {
            string path = Path.GetFullPath(certificate);
            if (File.Exists(path))
            {
                serverCertificate = new X509Certificate(path, password);
            }
            else
            {
                LoggerHelper.Instance.Error($"file {path} not found");
                Environment.Exit(0);
            }
        }

        public void Start(int port)
        {
            if (socket == null)
            {
                socket = BindAccept(port);
            }
        }
        private Socket BindAccept(int port)
        {
            IPEndPoint localEndPoint = new IPEndPoint(NetworkHelper.IPv6Support ? IPAddress.IPv6Any : IPAddress.Any, port);
            Socket socket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.IPv6Only(localEndPoint.AddressFamily, false);
            socket.ReuseBind(localEndPoint);
            socket.Listen(int.MaxValue);

            SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs
            {
                UserToken = socket,
                SocketFlags = SocketFlags.None,
            };
            acceptEventArg.Completed += IO_Completed;
            StartAccept(acceptEventArg);

            _ = BindUdp(port);
            return socket;
        }



        private async Task BindUdp(int port)
        {
            socketUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socketUdp.Bind(new IPEndPoint(IPAddress.Any, port));
            socketUdp.WindowsUdpBug();
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, IPEndPoint.MinPort);
            byte[] buffer = new byte[1024];
            byte[] sendData = new byte[20];
            while (true)
            {
                try
                {
                    SocketReceiveFromResult result = await socketUdp.ReceiveFromAsync(buffer, SocketFlags.None, endPoint);
                    IPEndPoint ep = result.RemoteEndPoint as IPEndPoint;
                    try
                    {
                        //给客户端返回他的IP+端口
                        sendData[0] = (byte)ep.AddressFamily;
                        ep.Address.TryWriteBytes(sendData.AsSpan(1), out int length);
                        ((ushort)ep.Port).ToBytes(sendData.AsMemory(1 + length));

                        //防止一些网关修改掉它的外网IP
                        for (int i = 0; i < 1 + length + 2; i++)
                        {
                            sendData[i] = (byte)(sendData[i] ^ byte.MaxValue);
                        }

                        await socketUdp.SendToAsync(sendData.AsMemory(0, 1 + length + 2), ep);
                    }
                    catch (Exception)
                    {
                    }
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        private void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            acceptEventArg.AcceptSocket = null;
            Socket token = (Socket)acceptEventArg.UserToken;
            try
            {
                if (token.AcceptAsync(acceptEventArg) == false)
                {
                    ProcessAccept(acceptEventArg);
                }
            }
            catch (Exception)
            {
                token?.SafeClose();
            }
        }
        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Accept:
                    ProcessAccept(e);
                    break;
                default:
                    break;
            }
        }
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (e.AcceptSocket != null)
            {
                _ = BeginReceiveServer(e.AcceptSocket);
                StartAccept(e);
            }
        }
        private async Task<IConnection> BeginReceiveServer(Socket socket)
        {
            try
            {
                if (socket == null || socket.RemoteEndPoint == null)
                {
                    return null;
                }
                socket.KeepAlive();
                NetworkStream networkStream = new NetworkStream(socket, false);
                SslStream sslStream = new SslStream(networkStream, true);
                await sslStream.AuthenticateAsServerAsync(serverCertificate, false, SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13, false);
                IConnection connection = CreateConnection(sslStream, networkStream, socket, socket.LocalEndPoint as IPEndPoint, socket.RemoteEndPoint as IPEndPoint);


                connection.BeginReceive(connectionReceiveCallback, null, true);
                return connection;
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Error(ex);
            }
            return null;
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
        public async Task<IConnection> BeginReceive(Socket socket)
        {
            try
            {
                if (socket == null || socket.RemoteEndPoint == null)
                {
                    return null;
                }
                socket.KeepAlive();
                NetworkStream networkStream = new NetworkStream(socket, false);
                SslStream sslStream = new SslStream(networkStream, true, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    AllowRenegotiation = true,
                    EnabledSslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13
                });
                IConnection connection = CreateConnection(sslStream, networkStream, socket, socket.LocalEndPoint as IPEndPoint, socket.RemoteEndPoint as IPEndPoint);

                connection.BeginReceive(connectionReceiveCallback, null, true);

                return connection;
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Error(ex);
            }
            return null;
        }

        public IConnection CreateConnection(SslStream stream, NetworkStream networkStream, Socket socket, IPEndPoint local, IPEndPoint remote)
        {
            return new TcpConnection(stream, networkStream, socket, local, remote)
            {
                ReceiveRequestWrap = new MessageRequestWrap(),
                ReceiveResponseWrap = new MessageResponseWrap()
            };
        }

        public void Stop()
        {
            cancellationTokenSource?.Cancel();
            socket?.SafeClose();
            socket = null;
        }
        public void Disponse()
        {
            Stop();
        }
    }
}
