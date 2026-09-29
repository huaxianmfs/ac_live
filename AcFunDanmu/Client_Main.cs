﻿using System;
using AcFunDanmu.Im.Basic;
using AcFunDanmu.Models.Client;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Timers;
using static AcFunDanmu.ClientUtils;
using HeartbeatTimer = System.Timers.Timer;

namespace AcFunDanmu
{
    public partial class Client
    {
        public event SignalHandler? Handler;
        public event Initialize? OnInitialize;
        public event Start? OnStart;
        public event End? OnEnd;

        public async Task<bool> InitializeWithLogin(string username, string password, string uid)
        {
            await Login(username, password);
            return await Initialize(uid);
        }

        private async Task<bool> Initialize(string hostId)
        {
            if (long.TryParse(hostId, out var id)) return await Initialize(id);

            Logger.LogError("Invalid user id: {HostId}", hostId);
            return false;
        }

        private async Task<bool> Initialize(long hostId)
        {
            OnInitialize?.Invoke();
            HostId = hostId;
            Logger.LogInformation("Client initializing");
            try
            {
                Logger.LogInformation("STEP1: 开始初始化 HTTP 客户端");
                using var client = CreateHttpClient(LIVE_URI);

                // 先访问一次登录页，让服务器分配真实的 _did cookie
                try
                {
                    using var loginPage = await client.GetAsync(ACFUN_LOGIN_URI);
                }
                catch
                {
                    // 忽略，即使失败也继续尝试
                }

                Logger.LogInformation("STEP2: 检查登录状态 userId={UserId}", _userId);
                if (_userId == -1 || string.IsNullOrEmpty(_serviceToken) || _securityKey == null)
                {
                    if (_isSignIn)
                    {
                        using var getContent = new FormUrlEncodedContent(GetTokenForm);
                        using var get = await client.PostAsync(GET_TOKEN_URI, getContent);
                        if (!get.IsSuccessStatusCode)
                        {
                            Logger.LogError("Get token error: {Content}",
                                await get.Content.ReadAsStringAsync());
                            return false;
                        }

                        var token = await JsonSerializer.DeserializeAsync(await get.Content.ReadAsStreamAsync(),
                            ClientModelsContext.Default.MidgroundToken);
                        if (token == null)
                        {
                            Logger.LogError("Unable to deserialize MidgroundToken");
                            return false;
                        }

                        _userId = token.UserId;
                        _serviceToken = token.ServiceToken;
                        _securityKey = Convert.FromBase64String(token.SecurityKey);
                    }
                    else
                    {
                        using var loginContent = new FormUrlEncodedContent(LoginForm);
                        using var login = await client.PostAsync(LOGIN_URI, loginContent);
                        if (!login.IsSuccessStatusCode)
                        {
                            Logger.LogError("Get token error: {Content}",
                                await login.Content.ReadAsStringAsync());
                            return false;
                        }

                        var token = await JsonSerializer.DeserializeAsync(await login.Content.ReadAsStreamAsync(),
                            ClientModelsContext.Default.VisitorToken);
                        if (token == null)
                        {
                            Logger.LogError("Unable to deserialize VisitorToken");
                            return false;
                        }

                        _userId = token.UserId;
                        _serviceToken = token.ServiceToken;
                        _securityKey = Convert.FromBase64String(token.SecurityKey);
                    }
                }

                Logger.LogInformation("STEP3: 请求 startPlay authorId={AuthorId}", hostId);
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                    { { "authorId", $"{hostId}" }, { "pullStreamType", "FLV" } });
                using var play = await client.PostAsync(
                    string.Format(PLAY_URL, _userId, DeviceId, _isSignIn ? MIDGROUND_ST : VISITOR_ST,
                        _serviceToken), form);
                if (!play.IsSuccessStatusCode)
                {
                    Logger.LogError("Get play info error: {Content}",
                        await play.Content.ReadAsStringAsync());
                    return false;
                }

                var playData =
                    await JsonSerializer.DeserializeAsync(await play.Content.ReadAsStreamAsync(),
                        ClientModelsContext.Default.Play);
                if (playData == null)
                {
                    Logger.LogError("Unable to deserialize Play");
                    return false;
                }

                if (playData.Result > 1)
                {
                    Logger.LogError("PlayData error message: {Message}", playData.ErrorMsg);
                    return false;
                }

                _tickets = playData.Data?.AvailableTickets ?? Array.Empty<string>();
                _enterRoomAttach = playData.Data?.EnterRoomAttach ?? string.Empty;
                LiveId = playData.Data?.LiveId ?? string.Empty;

                if (Gifts.Count == 0) UpdateGiftList();

                Logger.LogInformation("STEP4: 初始化完成 liveId={LiveId} tickets={TicketCount}", LiveId, _tickets?.Length ?? 0);

                return true;
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError(ex, "Initialize exception");
                return await Initialize(hostId);
            }
            catch (TaskCanceledException ex)
            {
                Logger.LogError(ex, "Initialize exception");
                return await Initialize(hostId);
            }
        }

        public static async Task<bool> Login(string username, string password)
        {
            if (_isSignIn) return _isSignIn;
            Logger.LogInformation("Client signing in");
            try
            {
                using var client = CreateHttpClient(ACFUN_LOGIN_URI);
                using var login = await client.GetAsync(ACFUN_LOGIN_URI);
                if (!login.IsSuccessStatusCode)
                {
                    Logger.LogError("Get login error: {Content}", await login.Content.ReadAsStringAsync());
                    return false;
                }

                using var signinContent = new FormUrlEncodedContent(new Dictionary<string, string?>
                {
                    { "username", username },
                    { "password", password },
                    { "key", null },
                    { "captcha", null }
                });
                using var signin = await client.PostAsync(ACFUN_SIGN_IN_URI, signinContent);
                if (!signin.IsSuccessStatusCode)
                {
                    Logger.LogError("Post sign in error: {Content}",
                        await signin.Content.ReadAsStringAsync());
                    return false;
                }

                var user = await JsonSerializer.DeserializeAsync(await signin.Content.ReadAsStreamAsync(),
                    ClientModelsContext.Default.SignIn);
                if (user == null)
                {
                    Logger.LogError("Unable to deserialize SignIn");
                    return false;
                }

                using var sidContent =
                    new StringContent(string.Format(SAFETY_ID_CONTENT, user.UserId));
                using var sid = await client.PostAsync(ACFUN_SAFETY_ID_URI, sidContent);
                if (!sid.IsSuccessStatusCode)
                {
                    Logger.LogError("Post safety id error: {Content}",
                        await sid.Content.ReadAsStringAsync());
                    return false;
                }

                var safetyId =
                    await JsonSerializer.DeserializeAsync(await sid.Content.ReadAsStreamAsync(),
                        ClientModelsContext.Default.SafetyId);
                if (safetyId == null)
                {
                    Logger.LogError("Unable to deserialize SignIn");
                    return false;
                }

                CookieContainer.Add(new Cookie
                {
                    Domain = ".acfun.cn",
                    Name = "safety_id",
                    Value = safetyId.Id
                });

                _isSignIn = true;
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError(ex, "Login Exception");
                return await Login(username, password);
            }
            catch (TaskCanceledException ex)
            {
                Logger.LogError(ex, "Login Exception");
                return await Login(username, password);
            }

            return _isSignIn;
        }

        private async void UpdateGiftList()
        {
            if (_securityKey == null) return;

            try
            {
                using var client = CreateHttpClient(LIVE_URI);
                var sign = Sign(GiftAll, _securityKey);

                using var gift = await client.PostAsync($"{KuaishouZt}{GiftAll}?{Query}&__clientSign={sign}",
                    null);
                if (!gift.IsSuccessStatusCode) return;
                var giftList =
                    await JsonSerializer.DeserializeAsync(await gift.Content.ReadAsStreamAsync(),
                        ClientModelsContext.Default.GiftList);
                foreach (var item in giftList?.Data?.GiftList ?? Array.Empty<Gift>())
                {
                    var giftInfo = new GiftInfo
                    {
                        Name = item.GiftName,
                        Value = item.GiftPrice,
                        Pic = new Uri(item.WebpPicList[0].Url)
                    };
                    Gifts[item.GiftId] = giftInfo;
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError(ex, "Update gift list exception");
                UpdateGiftList();
            }
            catch (TaskCanceledException ex)
            {
                Logger.LogError(ex, "Update gift list exception");
                UpdateGiftList();
            }
        }

        public async Task<WatchingUser[]> WatchingList()
        {
            if (_userId == -1 || string.IsNullOrEmpty(_serviceToken) || string.IsNullOrEmpty(LiveId))
                return Array.Empty<WatchingUser>();

            try
            {
                using var client = CreateHttpClient(LIVE_URL);
                using var watchingContent = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "visitorId", $"{_userId}" },
                    { "uperId", LiveId }
                });
                using var watching = await client.PostAsync(
                    string.Format(WATCHING_URL, _userId, DeviceId, _isSignIn ? MIDGROUND_ST : VISITOR_ST,
                        _serviceToken), watchingContent);
                if (!watching.IsSuccessStatusCode) return Array.Empty<WatchingUser>();

                var watchingList =
                    await JsonSerializer.DeserializeAsync(await watching.Content.ReadAsStreamAsync(),
                        ClientModelsContext.Default.WatchingList);

                return watchingList?.Data?.List ?? Array.Empty<WatchingUser>();
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError(ex, "Watching list exception");
                return await WatchingList();
            }
            catch (TaskCanceledException ex)
            {
                Logger.LogError(ex, "Watching list exception");
                return await WatchingList();
            }
        }

        public void Start(long hostId)
        {
            _ = Task.Run(() => StartInternal(hostId));
        }

        private async Task StartInternal(long hostId)
        {
            if (string.IsNullOrEmpty(LiveId) || string.IsNullOrEmpty(_enterRoomAttach) || _tickets == null ||
                _tickets.Length == 0 ||
                HostId != hostId)
            {
                if (!await Initialize(hostId))
                {
                    Logger.LogInformation("Client initialize failed, maybe live is end");
                    return;
                }
            }

            // 关闭旧 WebSocket
            if (CurrentWs != null)
            {
                try
                {
                    if (CurrentWs.State == WebSocketState.Open)
                        await CurrentWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect",
                            CancellationToken.None);
                    CurrentWs.Dispose();
                }
                catch { }
                CurrentWs = null;
                GC.Collect();
            }

            try
            {
                _ws = new ClientWebSocket();
                try
                {
                    _ws.Options.SetRequestHeader("User-Agent", USER_AGENT);
                }
                catch
                {
                    // SetRequestHeader 在部分平台可能失败，忽略
                }
                CurrentWs = _ws;
            }
            catch
            {
                return;
            }

            using var heartbeatTimer = new HeartbeatTimer();
            heartbeatTimer.Elapsed += Heartbeat;
            heartbeatTimer.AutoReset = true;

            try
            {
                IsRunning = true;
                OnStart?.Invoke();

                // 建立 WebSocket 连接
                await _ws.ConnectAsync(new Uri(WSS_HOST), CancellationToken.None);

                // WebSocket 打开后直接发 Register（不需要 TCP Handshake）
                RegisterRequest(null);

                var owner = ArrayPool<byte>.Shared;
                var buffer = owner.Rent(BUFFER_SIZE);

                while (_ws.State == WebSocketState.Open)
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        var segment = new ArraySegment<byte>(buffer, 0, buffer.Length);
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _ws.ReceiveAsync(segment, CancellationToken.None);
                            if (result.MessageType == WebSocketMessageType.Close) break;
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close) break;

                        var data = ms.ToArray();

                        var downstream = Decode(DownstreamPayload.Parser, data, _securityKey, _sessionKey,
                            out var header);

                        if (downstream == null)
                        {
                            Logger.LogError("Downstream is null");
                            continue;
                        }

                        HandleCommand(header, downstream, heartbeatTimer);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug(ex, "Main");
                        heartbeatTimer.Stop();
                        break;
                    }
                }

                owner.Return(buffer);

                Logger.LogDebug("Client disconnected");
                heartbeatTimer.Stop();
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "Start fatal");
            }
            finally
            {
                IsRunning = false;
                try { OnEnd?.Invoke(); } catch { }
            }
        }

        public void Stop(string? reason = null)
        {
            Logger.LogInformation("Stopping client, reason: {Reason}", reason);
            var ws = CurrentWs;
            try
            {
                if (ws != null && ws.State == WebSocketState.Open)
                {
                    // 尽力发送优雅退出消息，但不等待
                    try { UserExitRequest(null); } catch { }
                    try { UnRegisterRequest(null); } catch { }

                    // 不等待关闭握手，直接强制中止，避免 UI 卡顿
                    try { ws.Abort(); } catch { }
                    try { ws.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Stop");
            }
            finally
            {
                CurrentWs = null;
                _ws = null;
                IsRunning = false;
                _enterRoomAttach = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Heartbeat(object sender, ElapsedEventArgs e)
        {
            var ws = CurrentWs;
            if (ws != null && ws.State == WebSocketState.Open)
            {
                Logger.LogTrace("HEARTBEAT");
                try
                {
                    HeartbeatRequest(null);

                    if (_heartbeatSeqId % 5 == 4)
                        KeepAliveRequest(null);
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "Heartbeat");
                    (sender as HeartbeatTimer)?.Stop();
                }
            }
            else
            {
                (sender as HeartbeatTimer)?.Stop();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleHandshake(DownstreamPayload payload)
        {
            var handshake = HandshakeResponse.Parser.ParseFrom(payload.PayloadData);
            Logger.LogTrace("\t{HandShake}", handshake);

            RegisterRequest(null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void HandleKeepAlive(DownstreamPayload payload)
        {
            var keepAlive = KeepAliveResponse.Parser.ParseFrom(payload.PayloadData);
            Logger.LogTrace("\t{KeepAlive}", keepAlive);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void HandlePing(DownstreamPayload payload)
        {
            var ping = PingResponse.Parser.ParseFrom(payload.PayloadData);
            Logger.LogTrace("\t{Ping}", ping);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleRegister(int appId, DownstreamPayload payload)
        {
            var register = RegisterResponse.Parser.ParseFrom(payload.PayloadData);
            Register(appId, register);
            Logger.LogTrace("\t{Register}", register);

            try
            {
                KeepAliveRequest(null);
                EnterRoomRequest(null);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Register response");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SendPushMessageResponse(PacketHeader header)
        {
            try
            {
                PushMessageResponse(null, header.SeqId);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Push message response");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleTicketInvalid(ByteString payload)
        {
            var ticketInvalid = ZtLiveScTicketInvalid.Parser.ParseFrom(payload);
            Logger.LogTrace("\t\t{TicketInvalid}", ticketInvalid);

            NextTicket();
            try
            {
                EnterRoomRequest(null);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Ticket invalid request");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static HttpClient CreateHttpClient(Uri referer)
        {
            var client = new HttpClient(
                new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    UseCookies = true,
                    CookieContainer = CookieContainer
                });
            client.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd(ACCEPTED_ENCODING);
            client.DefaultRequestHeaders.Referrer = referer;
            return client;
        }

        internal static ILogger<Client> Logger { get; private set; } = new NullLogger<Client>();

        public Client(ILogger<Client>? logger = null)
        {
            if (logger != null)
                Logger = logger;
        }

        public Client(long userId, string serviceToken, byte[]? securityKey, string[]? tickets, string? enterRoomAttach,
            string liveId, ILogger<Client>? logger = null) : this(logger)
        {
            _userId = userId;
            _serviceToken = serviceToken;
            _securityKey = securityKey;
            _tickets = tickets;
            _enterRoomAttach = enterRoomAttach;
            LiveId = liveId;
        }

        public string LiveId { get; private set; } = string.Empty;

        private string _serviceToken = string.Empty;
        private byte[]? _securityKey;
        private string? _enterRoomAttach;
        private string[]? _tickets;

        private TcpClient? _tcpClient;
        private NetworkStream? _tcpStream;
        private ClientWebSocket? _ws;

        /// <summary>
        /// 调试日志开关，默认关闭。排查问题时改成 true 并重新编译即可。
        /// </summary>
        public static bool DebugLogEnabled = false;

        private static void Dbg(string msg)
        {
            if (!DebugLogEnabled) return;
            try
            {
                var dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "acfun_debug.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}