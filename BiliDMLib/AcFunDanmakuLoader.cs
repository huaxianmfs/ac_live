using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using AcFunDanmu;
using AcFunDanmu.Enums;
using AcFunDanmu.Models.Client;
using BilibiliDM_PluginFramework;
using Google.Protobuf;

namespace BiliDMLib
{
    public class AcFunDanmakuLoader : IDisposable
    {
        private Client _client;
        private bool _connected;

        public event ReceivedDanmakuEvt ReceivedDanmaku;
        public event DisconnectEvt Disconnected;
        public event ReceivedRoomCountEvt ReceivedRoomCount;
        public event LogMessageEvt LogMessage;

        public bool Connected => _connected;
        public Exception Error;

        public async Task<bool> ConnectAsync(int roomId)
        {
            try
            {
                if (_connected) throw new InvalidOperationException("已连接");

                _client = new Client(new ForwardLogger(this));
                _client.Handler += OnSignal;
                _client.OnEnd += OnClientEnd;

                if (!await _client.InitializeAsync(roomId))
                {
                    Error = new Exception("初始化失败，可能直播间未开播或主播ID错误");
                    LogMessage?.Invoke(this, new LogMessageArgs { message = Error.Message });
                    return false;
                }

                _connected = true;
                _ = Task.Run(() => _client.Start(roomId));
                return true;
            }
            catch (Exception ex)
            {
                Error = ex;
                LogMessage?.Invoke(this, new LogMessageArgs { message = ex.ToString() });
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                _client?.Stop("User disconnected");
            }
            catch
            {
            }
            _client = null;
            _connected = false;
        }

        private void OnClientEnd()
        {
            if (!_connected) return;
            _connected = false;
            Disconnected?.Invoke(this, new DisconnectEvtArgs { Error = null });
        }

        private void OnSignal(Client sender, string messageType, ByteString payload)
        {
            try
            {
                switch (messageType)
                {
                    case PushMessage.ACTION_SIGNAL:
                        var action = ZtLiveScActionSignal.Parser.ParseFrom(payload);
                        foreach (var item in action.Item) ProcessActionSignal(item);
                        break;
                    case PushMessage.STATE_SIGNAL:
                        var state = ZtLiveScStateSignal.Parser.ParseFrom(payload);
                        foreach (var item in state.Item) ProcessStateSignal(item);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, new LogMessageArgs { message = "解析消息出错: " + ex.Message });
            }
        }

        private void ProcessActionSignal(ZtLiveActionSignalItem item)
        {
            foreach (var payload in item.Payload)
            {
                switch (item.SignalType)
                {
                    case PushMessage.ActionSignal.COMMENT:
                        try
                        {
                            var comment = CommonActionSignalComment.Parser.ParseFrom(payload);
                            var m = FromAcFunComment(comment);
                            RaiseDanmaku(m);
                        }
                        catch (Exception ex)
                        {
                            DbgFile("Comment case exception: " + ex);
                        }
                        break;
                    case PushMessage.ActionSignal.GIFT:
                        RaiseDanmaku(FromAcFunGift(CommonActionSignalGift.Parser.ParseFrom(payload)));
                        break;
                    case PushMessage.ActionSignal.ENTER_ROOM:
                        RaiseDanmaku(FromAcFunEnterRoom(CommonActionSignalUserEnterRoom.Parser.ParseFrom(payload)));
                        break;
                    case PushMessage.ActionSignal.FOLLOW:
                        RaiseDanmaku(FromAcFunFollow(CommonActionSignalUserFollowAuthor.Parser.ParseFrom(payload)));
                        break;
                    case PushMessage.ActionSignal.LIKE:
                        RaiseDanmaku(FromAcFunLike(CommonActionSignalLike.Parser.ParseFrom(payload)));
                        break;
                }
            }
        }

        private void ProcessStateSignal(ZtLiveStateSignalItem item)
        {
            switch (item.SignalType)
            {
                case PushMessage.StateSignal.DISPLAY_INFO:
                    // 人气值 -> WatchedBlock
                    var display = CommonStateSignalDisplayInfo.Parser.ParseFrom(item.Payload);
                    if (long.TryParse(display.WatchingCount, out var watched))
                    {
                        RaiseDanmaku(new DanmakuModel
                        {
                            MsgType = MsgTypeEnum.WatchedChange,
                            WatchedCount = watched
                        });
                    }
                    break;

                case PushMessage.StateSignal.ACFUN_DISPLAY_INFO:
                    // 在线人数 -> OnlineBlock
                    var acDisplay = CommonStateSignalDisplayInfo.Parser.ParseFrom(item.Payload);
                    if (uint.TryParse(acDisplay.WatchingCount, out var count))
                        ReceivedRoomCount?.Invoke(this, new ReceivedRoomCountArgs { UserCount = count });
                    break;
            }
        }

        private void RaiseDanmaku(DanmakuModel model)
        {
            if (model == null)
            {
                DbgFile("RaiseDanmaku: null model");
                return;
            }
            DbgFile("RaiseDanmaku: " + model.MsgType + " text=" + model.CommentText + " subscribers=" + (ReceivedDanmaku != null));
            try
            {
                ReceivedDanmaku?.Invoke(this, new ReceivedDanmakuArgs { Danmaku = model });
                DbgFile("RaiseDanmaku: event fired OK");
            }
            catch (Exception ex)
            {
                DbgFile("RaiseDanmaku exception: " + ex);
            }
        }

        private static DanmakuModel FromAcFunComment(CommonActionSignalComment comment)
        {
            var user = comment.UserInfo;
            return new DanmakuModel
            {
                MsgType = MsgTypeEnum.Comment,
                CommentText = comment.Content,
                UserName = user?.Nickname ?? "",
                UserID_str = (user?.UserId ?? 0).ToString(),
                UserID_long = user?.UserId ?? 0,
                isAdmin = user?.UserIdentity?.ManagerType == ZtLiveUserIdentity.Types.ManagerType.Normal,
                isVIP = false,
                UserGuardLevel = 0
            };
        }

        private static DanmakuModel FromAcFunGift(CommonActionSignalGift gift)
        {
            var user = gift.UserInfo;
            var giftName = Client.Gifts.TryGetValue(gift.GiftId, out var info) ? info.Name : "礼物";
            return new DanmakuModel
            {
                MsgType = MsgTypeEnum.GiftSend,
                GiftName = giftName,
                GiftCount = gift.BatchSize > 0 ? gift.BatchSize : 1,
                UserName = user?.Nickname ?? "",
                UserID_str = (user?.UserId ?? 0).ToString(),
                UserID_long = user?.UserId ?? 0
            };
        }

        private static DanmakuModel FromAcFunEnterRoom(CommonActionSignalUserEnterRoom enter)
        {
            var user = enter.UserInfo;
            return new DanmakuModel
            {
                MsgType = MsgTypeEnum.Interact,
                InteractType = InteractTypeEnum.Enter,
                UserName = user?.Nickname ?? "",
                UserID_str = (user?.UserId ?? 0).ToString(),
                UserID_long = user?.UserId ?? 0
            };
        }

        private static DanmakuModel FromAcFunFollow(CommonActionSignalUserFollowAuthor follow)
        {
            var user = follow.UserInfo;
            return new DanmakuModel
            {
                MsgType = MsgTypeEnum.Interact,
                InteractType = InteractTypeEnum.Follow,
                UserName = user?.Nickname ?? "",
                UserID_str = (user?.UserId ?? 0).ToString(),
                UserID_long = user?.UserId ?? 0
            };
        }

        private static DanmakuModel FromAcFunLike(CommonActionSignalLike like)
        {
            var user = like.UserInfo;
            return new DanmakuModel
            {
                MsgType = MsgTypeEnum.Interact,
                InteractType = InteractTypeEnum.Like,
                UserName = user?.Nickname ?? "",
                UserID_str = (user?.UserId ?? 0).ToString(),
                UserID_long = user?.UserId ?? 0
            };
        }

        public void Dispose()
        {
            Disconnect();
        }

        private static void DbgFile(string msg)
        {
            if (!AcFunDanmu.Client.DebugLogEnabled) return;
            try
            {
                var dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "acfun_debug.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " [Loader] " + msg + Environment.NewLine);
            }
            catch { }
        }

        private class ForwardLogger : ILogger<Client>
        {
            private readonly AcFunDanmakuLoader _owner;
            public ForwardLogger(AcFunDanmakuLoader owner) { _owner = owner; }

            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                // 只转发 Error 及以上级别，避免刷屏
                if (logLevel < LogLevel.Error) return;
                try
                {
                    var msg = formatter(state, exception);
                    if (!string.IsNullOrEmpty(msg))
                        _owner.LogMessage?.Invoke(_owner, new LogMessageArgs { message = "[AcFun] " + msg });
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// A站单机版：OpenDanmakuLoader 空壳，只保留类型兼容主程序，不做任何实际操作
    /// </summary>
    public class OpenDanmakuLoader : IDisposable
    {
        public event ReceivedDanmakuEvt ReceivedDanmaku;
        public event DisconnectEvt Disconnected;
        public event ReceivedRoomCountEvt ReceivedRoomCount;
        public event LogMessageEvt LogMessage;

        public bool Connected { get; private set; }
        public string GameId { get; set; }

        public OpenDanmakuLoader(string auth, string[] server, string gameId)
        {
            GameId = gameId;
        }

        public Task<bool> ConnectAsync()
        {
            return Task.FromResult(false);
        }

        public void Disconnect() { }

        public void ForceDisconnect() { }

        public void PlatformHeartBeatOk() { }

        public void Dispose() { }
    }

    public delegate void LogMessageEvt(object sender, LogMessageArgs e);

    public class LogMessageArgs
    {
        public string message = string.Empty;
    }
}