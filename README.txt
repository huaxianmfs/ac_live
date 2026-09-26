A站弹幕姬
基于 copyliu/bililive_dm 修改的 AcFun 直播弹幕工具，将原版的 B 站连接替换为 A 站连接。

功能
接收 A 站直播间弹幕、礼物、进场、点赞、关注等消息

弹幕过滤（正则表达式）

侧边栏弹幕悬浮窗

投喂榜统计（礼物、弹幕、用户）

插件系统（兼容原版插件框架）

日志保存到本地


使用方法
打开 A 站直播间页面，URL 形如：

text
https://live.acfun.cn/live/62798201
其中 62798201 就是主播 ID。

启动 Bililive_dm.exe，在顶部输入框填入主播 ID，点击 连接。

弹幕会显示在"首页"选项卡，侧边栏悬浮窗会显示弹幕。


选项卡说明
首页	日志和收到的弹幕
投喂榜	礼物、弹幕、用户统计
侧边弹幕设置	悬浮窗样式（宽度、字号、速度等）
设置	显示选项、正则过滤、SSTP 联动等
插件	插件列表，右键启用/停用/管理
插件开发
插件继承 BilibiliDM_PluginFramework.DMPlugin，编译为 .dll 后放入 Plugins 文件夹，重启程序即可加载。

插件可使用的事件：

ReceivedDanmaku — 收到弹幕、礼物、进场等消息

ReceivedRoomCount — 在线人数变化

Connected / Disconnected — 连接状态

日志
程序运行日志和错误信息保存在 exe 同目录下的 logs\ 文件夹：

lastrun.txt — 最近一次运行日志

YYYY-MM-DD.txt — 按日期归档的完整日志

crash.txt — 崩溃报告（如果发生）

点击日志区的任意一行可复制到剪贴板。

已知限制
A 站弹幕连接依赖官方公开接口，如接口变动可能失效

仅支持 64 位 Windows 7 及以上系统

需要 .NET Framework 4.6.1

鸣谢
原版 bililive_dm by copyliu

A 站协议实现参考 AcFunDanmaku、acfun-live-danmaku
