﻿using linker.client;
using linker.config;
using linker.server;
using linker.libs;
using MemoryPack;

namespace linker.plugins.signin.messenger
{
    public sealed class SignInClientMessenger : IMessenger
    {
        private readonly ConfigWrap config;
        private readonly ClientSignInTransfer clientSignInTransfer;
        public SignInClientMessenger(ConfigWrap config, ClientSignInTransfer clientSignInTransfer)
        {
            this.config = config;
            this.clientSignInTransfer = clientSignInTransfer;
        }

        [MessengerId((ushort)SignInMessengerIds.Name)]
        public void Name(IConnection connection)
        {
            ConfigSetNameInfo info = MemoryPackSerializer.Deserialize<ConfigSetNameInfo>(connection.ReceiveRequestWrap.Payload.Span);
            clientSignInTransfer.UpdateName(info.NewName);
        }

        [MessengerId((ushort)SignInMessengerIds.Servers)]
        public void Servers(IConnection connection)
        {
            ClientServerInfo[] servers = MemoryPackSerializer.Deserialize<ClientServerInfo[]>(connection.ReceiveRequestWrap.Payload.Span);
            clientSignInTransfer.UpdateServers(servers);
        }
    }

    public sealed class SignInServerMessenger : IMessenger
    {
        private readonly SignCaching signCaching;
        private readonly ConfigWrap config;
        private readonly MessengerSender messengerSender;

        public SignInServerMessenger(SignCaching signCaching, ConfigWrap config, MessengerSender messengerSender)
        {
            this.signCaching = signCaching;
            this.config = config;
            this.messengerSender = messengerSender;
        }

        [MessengerId((ushort)SignInMessengerIds.SignIn)]
        public void SignIn(IConnection connection)
        {
            SignInfo info = MemoryPackSerializer.Deserialize<SignInfo>(connection.ReceiveRequestWrap.Payload.Span);
            if (info.Version == config.Data.Version)
            {
                signCaching.Sign(connection, info);
                connection.Write(MemoryPackSerializer.Serialize(info.MachineId));
            }
            else
            {
                connection.Write(Helper.FalseArray);
            }
        }


        [MessengerId((ushort)SignInMessengerIds.List)]
        public void List(IConnection connection)
        {
            SignInListRequestInfo request = MemoryPackSerializer.Deserialize<SignInListRequestInfo>(connection.ReceiveRequestWrap.Payload.Span);

            if (signCaching.TryGet(connection.Id, out SignCacheInfo cache))
            {
                List<SignCacheInfo> list = signCaching.Get(cache.GroupId).OrderByDescending(c => c.MachineName).OrderByDescending(c => c.LastSignIn).OrderByDescending(c => c.Version).ToList();
                int count = list.Count;
                list = list.Skip((request.Page - 1) * request.Size).Take(request.Size).ToList();

                SignInListResponseInfo response = new SignInListResponseInfo { Request = request, Count = count, List = list };

                connection.Write(MemoryPackSerializer.Serialize(response));
            }
        }

        [MessengerId((ushort)SignInMessengerIds.Delete)]
        public void Delete(IConnection connection)
        {
            string name = MemoryPackSerializer.Deserialize<string>(connection.ReceiveRequestWrap.Payload.Span);
            if (signCaching.TryGet(name, out SignCacheInfo cache) && signCaching.TryGet(connection.Id, out SignCacheInfo cache1) && cache.GroupId == cache1.GroupId)
            {
                signCaching.TryRemove(name,out _);
            }
        }

        [MessengerId((ushort)SignInMessengerIds.NameForward)]
        public async Task NameForward(IConnection connection)
        {
            ConfigSetNameInfo info = MemoryPackSerializer.Deserialize<ConfigSetNameInfo>(connection.ReceiveRequestWrap.Payload.Span);
            if (signCaching.TryGet(info.Id, out SignCacheInfo cache) && signCaching.TryGet(connection.Id, out SignCacheInfo cache1) && cache.GroupId == cache1.GroupId)
            {
                if (info.Id != connection.Id)
                {
                    await messengerSender.SendOnly(new MessageRequestWrap
                    {
                        Connection = cache.Connection,
                        MessengerId = (ushort)SignInMessengerIds.Name,
                        Payload = connection.ReceiveRequestWrap.Payload,
                    });
                }
            }
        }


        [MessengerId((ushort)SignInMessengerIds.ServersForward)]
        public async Task ServersForward(IConnection connection)
        {
            if (signCaching.TryGet(connection.Id, out SignCacheInfo cache))
            {
                var clients = signCaching.Get(cache.GroupId);
                foreach (var info in clients)
                {
                    if (info.MachineId != connection.Id)
                    {
                        await messengerSender.SendOnly(new MessageRequestWrap
                        {
                            Connection = info.Connection,
                            MessengerId = (ushort)SignInMessengerIds.Servers,
                            Payload = connection.ReceiveRequestWrap.Payload,
                        });
                    }
                }
            }
        }


        [MessengerId((ushort)SignInMessengerIds.Version)]
        public void Version(IConnection connection)
        {
            connection.Write(MemoryPackSerializer.Serialize(config.Data.Version));
        }

    }

    [MemoryPackable]
    public sealed partial class SignInListRequestInfo
    {
        public int Page { get; set; } = 1;
        public int Size { get; set; } = 10;
        public string GroupId { get; set; }
    }

    [MemoryPackable]
    public sealed partial class SignInListResponseInfo
    {
        public SignInListRequestInfo Request { get; set; } = new SignInListRequestInfo();
        public int Count { get; set; }
        public List<SignCacheInfo> List { get; set; } = new List<SignCacheInfo>();
    }
}
