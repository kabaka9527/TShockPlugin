﻿using On.OTAPI;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;
using PacketWriter = CaiBot.PacketWriter;

namespace CaiBotLite;

internal static class Login
{
    
    internal static bool MessageBuffer_InvokeGetData(Hooks.MessageBuffer.orig_InvokeGetData orig,
        MessageBuffer instance, ref byte packetId, ref int readOffset, ref int start, ref int length,
        ref int messageType, int maxPackets)
    {
        if (!Config.Settings.WhiteList)
        {
            return orig(instance, ref packetId, ref readOffset, ref start, ref length, ref messageType, maxPackets);
        }

        try
        {
            if (packetId == (byte) PacketTypes.ClientUUID)
            {
                var player = TShock.Players[instance.whoAmI];
                
                instance.ResetReader();
                instance.reader.BaseStream.Position = readOffset;
                
                var uuid = instance.reader.ReadString();
                
                if (string.IsNullOrEmpty(player.Name))
                {
                    player.Kick("[Cai白名单]玩家名获取失败!");
                    return false;
                }
                
                if (CaiBotApi.WhiteListCaches.TryGetValue(player.Name, out var whiteListCache))
                {
                    if (DateTime.Now - whiteListCache.Item1 <= TimeSpan.FromSeconds(10))
                    {
                        if (!CheckWhite(player.Name,whiteListCache.Item2))
                        {
                            return false;
                        }
                        HandleLogin(player);
                        
                        return orig(instance, ref packetId, ref readOffset, ref start, ref length, ref messageType, maxPackets);
                    }
                }

                
                
                if (!CaiBotApi.IsWebsocketConnected)
                {
                    if (CaiBotApi.WhiteListCaches.TryGetValue(player.Name, out var whiteListCache2)) //从缓存处读取白名单
                    {
                        TShock.Log.ConsoleWarn("[CaiBotLite]正在使用白名单缓存验证玩家...");
                        if (!CheckWhite(player.Name, whiteListCache2.Item2))
                        {
                            return false;
                        }
                        HandleLogin(player);
                    }
                    else
                    {
                        TShock.Log.ConsoleError("[CaiBotLite]机器人处于未连接状态, 玩家无法加入。\n" +
                                                "如果你不想使用Cai白名单，可以在tshock/CaiBot.json中将其关闭。");
                        player.Disconnect("[CaiBotLite]机器人处于未连接状态, 玩家无法加入。");
                        return false;
                    }

                    
                }
                else
                {
                    var packetWriter = new PacketWriter();
                    packetWriter.SetType("whitelistV2")
                        .Write("name", player.Name)
                        .Write("uuid", uuid )
                        .Write("ip", player.IP)
                        .Send();
                }
                
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError(ex.ToString());
        }

        return orig(instance, ref packetId, ref readOffset, ref start, ref length, ref messageType, maxPackets);
    }

    internal static void OnGetData(GetDataEventArgs args)
    {
        if (!Config.Settings.WhiteList)
        {
            return;
        }

        var type = args.MsgID;

        var player = TShock.Players[args.Msg.whoAmI];
        if (player is not { ConnectionAlive: true })
        {
            args.Handled = true;
            return;
        }

        if (player.State < (int)ConnectionState.Complete
            && type > PacketTypes.PlayerSpawn 
            && type != PacketTypes.PlayerMana
            && type != PacketTypes.PlayerHp
            && type != PacketTypes.PlayerBuff
            && type != PacketTypes.ItemOwner
            && type != PacketTypes.SyncLoadout
            && type != PacketTypes.Placeholder //维度数据包
            )
        {
            args.Handled = true;
            return;
        }

        try
        {
            if (type == PacketTypes.ContinueConnecting2)
            {
                player.DataWhenJoined = new PlayerData();
                player.DataWhenJoined.CopyCharacter(player);
                args.Handled = true;
            }
        }
        catch (Exception e)
        {
            TShock.Log.ConsoleError(e.ToString());
        }
    }

    internal static bool CheckWhite(string name, int code)
    {
        var player = TShock.Players.FirstOrDefault(x=> x?.Name == name);
        
        var groupId = Config.Settings.GroupNumber.ToString();
        if (Config.Settings.GroupNumber == 0)
        {
            groupId = "";
        }
        if (player==null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(name))
        {
            TShock.Log.ConsoleInfo($"[Cai白名单]玩家[{name}](IP: {player.IP})版本可能过低...");
            player.Disconnect("你的游戏版本可能过低,\n" +
                              "请使用Terraria1.4.4+游玩");
            return false;
        }

        try
        {
            switch ((WhiteListStatus)code)
            {
                case WhiteListStatus.Success:
                {
                    TShock.Log.ConsoleInfo($"[Cai白名单]玩家[{name}](IP: {player.IP})已通过白名单验证...");
                    break;
                }
                case WhiteListStatus.NotInWhiteList:
                {
                    TShock.Log.ConsoleInfo($"[Cai白名单]玩家[{name}](IP: {player.IP})没有添加白名单...");
                    player.SilentKickInProgress = true;
                    player.Disconnect($"[Cai白名单]没有添加白名单!\n" +
                                      $"请在群{groupId}内发送'/添加白名单 角色名字'");
                    return false;
                }
                case WhiteListStatus.Banned:
                {
                    TShock.Log.ConsoleInfo($"[Cai白名单]玩家[{name}](IP: {player.IP})被屏蔽，处于群封禁名单中...");
                    player.SilentKickInProgress = true;
                    player.Disconnect("[Cai白名单]你已被服务器屏蔽,\n" +
                                      "你处于本群黑名单中!");
                    return false;
                }
                //case WhiteListStatus.NotInGroup:
                // {
                //     TShock.Log.ConsoleInfo($"[Cai白名单]玩家[{name}](IP: {plr.IP})不在本群内...");
                //     plr.SilentKickInProgress = true;
                //     plr.Disconnect($"[Cai白名单]你不在服务器群内!\n" +
                //                    $"请加入服务器群: {number}");
                //     return false;
                // }
                case WhiteListStatus.Unauthorized:
                {
                    TShock.Log.ConsoleInfo($"[Cai白名单]玩家[{name}](IP: {player.IP})使用未授权的设备...");
                    player.SilentKickInProgress = true;
                    player.Disconnect($"[Cai白名单]未授权设备!\n" +
                                      $"在群{groupId}内发送'/登录'\n" +
                                      $"以批准此设备登录");

                    return false;
                }
                default:
                {
                    TShock.Log.ConsoleInfo($"[Cai白名单]玩家[{name}](IP: {player.IP})无效登录状态码[{code}]...");
                    player.SilentKickInProgress = true;
                    player.Disconnect($"[Cai白名单]登录出错!" +
                                      $"无法处理状态码: {code}");

                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleInfo($"[Cai白名单]玩家[{name}](IP: {player.IP})验证白名单时出现错误...\n" +
                                   $"{ex}");
            player.SilentKickInProgress = true;
            player.Disconnect($"[Cai白名单]服务器发生错误无法处理该请求!\n" +
                              $"请尝试重新加入游戏或者联系服务器群{groupId}管理员");
            return false;
        }

        return true;
    }

    internal static void HandleLogin(TSPlayer player)
    {
        var password = Guid.NewGuid().ToString();
        var account = TShock.UserAccounts.GetUserAccountByName(player.Name);
        if (account != null)
        {
            player.RequiresPassword = false;
            player.PlayerData = TShock.CharacterDB.GetPlayerData(player, account.ID);

            if (player.State == (int)ConnectionState.AssigningPlayerSlot)
            {
                player.State = (int)ConnectionState.AwaitingPlayerInfo;
            }
            

            NetMessage.SendData((int) PacketTypes.WorldInfo, player.Index);

            var group = TShock.Groups.GetGroupByName(account.Group);

            player.Group = group;
            player.tempGroup = null;
            player.Account = account;
            player.IsLoggedIn = true;
            player.IsDisabledForSSC = false;

            if (Main.ServerSideCharacter)
            {
                if (player.HasPermission(Permissions.bypassssc))
                {
                    player.PlayerData.CopyCharacter(player);
                    TShock.CharacterDB.InsertPlayerData(player);
                }

                player.PlayerData.RestoreCharacter(player);
            }

            player.LoginFailsBySsi = false;

            if (player.HasPermission(Permissions.ignorestackhackdetection))
            {
                player.IsDisabledForStackDetection = false;
            }

            if (player.HasPermission(Permissions.usebanneditem))
            {
                player.IsDisabledForBannedWearable = false;
            }

            player.SendSuccessMessage($"[CaiBotLite]已经验证{account.Name}登录完毕。");
            TShock.Log.ConsoleInfo(player.Name + "成功验证登录。");
            TShock.UserAccounts.SetUserAccountUUID(account, player.UUID);
            PlayerHooks.OnPlayerPostLogin(player);
            return;
        }

        if (player.Name != TSServerPlayer.AccountName)
        {
            account = new UserAccount { Name = player.Name, Group = TShock.Config.Settings.DefaultRegistrationGroupName, UUID = player.UUID };
            try
            {
                account.CreateBCryptHash(password);
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }

            player.SendSuccessMessage("[CaiBotLite]账户{0}注册成功。", account.Name);
            TShock.UserAccounts.AddUserAccount(account);
            TShock.Log.ConsoleInfo("玩家{0}注册了新账户：{1}", player.Name, account.Name);
            player.PlayerData = TShock.CharacterDB.GetPlayerData(player, account.ID);

            if (player.State == (int)ConnectionState.AssigningPlayerSlot)
            {
                player.State = (int)ConnectionState.AwaitingPlayerInfo;
            }

            NetMessage.SendData((int) PacketTypes.WorldInfo, player.Index);

            var group = TShock.Groups.GetGroupByName(account.Group);

            player.Group = group;
            player.tempGroup = null;
            player.Account = account;
            player.IsLoggedIn = true;
            player.IsDisabledForSSC = false;

            if (Main.ServerSideCharacter)
            {
                if (player.HasPermission(Permissions.bypassssc))
                {
                    player.PlayerData.CopyCharacter(player);
                    TShock.CharacterDB.InsertPlayerData(player);
                }

                player.PlayerData.RestoreCharacter(player);
            }

            player.LoginFailsBySsi = false;

            if (player.HasPermission(Permissions.ignorestackhackdetection))
            {
                player.IsDisabledForStackDetection = false;
            }

            if (player.HasPermission(Permissions.usebanneditem))
            {
                player.IsDisabledForBannedWearable = false;
            }

            player.SendSuccessMessage($"[CaiBotLite]已经验证{account.Name}登录完毕.");
            TShock.Log.ConsoleInfo(player.Name + "成功验证登录.");
            TShock.UserAccounts.SetUserAccountUUID(account, player.UUID);
            PlayerHooks.OnPlayerPostLogin(player);
            return;
        }

        player.SilentKickInProgress = true;
        player.Disconnect("[CaiBotLite]此名字不可用!");
    }
}
