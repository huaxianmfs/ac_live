using System;
using BilibiliDM_PluginFramework;
using BiliDMLib;

namespace Bililive_dm
{
    public class FuckMePlugin : DMPlugin
    {
        private readonly AcFunDanmakuLoader b = new AcFunDanmakuLoader();

        public FuckMePlugin()
        {
            b.Disconnected += B_Disconnected;
            b.ReceivedDanmaku += B_ReceivedDanmaku;
        }

        public override async void Start()
        {
            base.Start();
            var result = await b.ConnectAsync(5051);
        }

        private async void B_Disconnected(object sender, DisconnectEvtArgs e)
        {
            await b.ConnectAsync(5051);
        }

        private void B_ReceivedDanmaku(object sender, ReceivedDanmakuArgs e)
        {
            if (e.Danmaku.MsgType == MsgTypeEnum.LiveStart) AddDM("黑猫老爷的直播间5051打开了!");
        }
    }
}