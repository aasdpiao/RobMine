/*
*** author：liangba
*** date: 2014.02.21
**  desc:发送消息包打包处理
 */
using System;
using System.Collections.Generic;
using System.IO;
using Msg;
using System.Net;
using UnityEngine;


namespace NetworkSendHandler
{

    public class SendHandler
    {
        //private MemoryStream ms = new MemoryStream();
        private MemoryStream zeroms = new MemoryStream();

        private static int CODESIZE = 256;
        private static string enkey = "woaidajiangyou";

        public static byte[] crypt(byte[] source)
        {
            int[] s = new int[CODESIZE], t = new int[CODESIZE];
            int keylen = enkey.Length;
            for (int i = 0; i < CODESIZE; i++)
            {
                s[i] = i;
                t[i] = enkey[i % keylen];
            }
            int j = 0;
            for (int i = 0; i < CODESIZE; i++)
            {
                j = (j + s[i] + t[i]) % CODESIZE;
                int tmp = s[i];
                s[i] = s[j];
                s[j] = tmp;
            }
            int[] key = new int[CODESIZE];
            int m, n, q;
            m = n = 0;
            uint ii = 0;
            byte[] outstr = new byte[source.Length];
            for (ii = 0; ii < source.Length; ii++)
            {
                m = (m + 1) % CODESIZE;
                n = (n + s[n]) % CODESIZE;
                int tmp = s[n];
                s[n] = s[m];
                s[m] = tmp;
                q = (s[m] + s[n]) % CODESIZE;
                key[ii] = s[q];
                outstr[ii] = (byte)(source[ii] ^ key[ii]);
            }
            return outstr;
        }

        // private bool isStreamUsing = false;
        private MemoryStream GetStream()
        {
            MemoryStream mslocal = new MemoryStream();
            return mslocal;
        }

        public void Send(MsgId id)
        {
            Send(id, zeroms);
        }

        //构造消息包：  消息头（共4字节）+ 消息体  
        //               消息头内容：消息体长度[16字节]  +  消息ID [16字节] 
        //               erlang 服务器以 bigending 的方式解析，注意消息头的字节序转换
        public void Send(MsgId id, MemoryStream body)
        {
            byte[] bsout = new byte[1024];

            int bodyLen = 0;
            byte[] bodyOut = null;

            // 消息体 base64 编码
            if (body.Length > 0)
            {
                string encodeStr = Convert.ToBase64String(body.ToArray());
                bodyOut = System.Text.Encoding.UTF8.GetBytes(encodeStr);
                bodyLen = bodyOut.Length;
            }

            byte[] bs1 = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)(bodyLen << 16)));
            Array.Copy(bs1, 0, bsout, 0, 2);    // 消息包长度

            bs1 = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)((int)id << 16)));
            Array.Copy(bs1, 0, bsout, 2, 2);    // 消息ID

            if (bodyLen > 0)
            {
                Array.Copy(bodyOut, 0, bsout, 4, bodyLen);  //消息体
            }
            Debug.Log("MsgId:" + id);
            //Debug.Log("_online:" + NetworkMgr._online);
            global::NetworkMgr.sock.AddSendData(bsout, 4 + bodyLen);

        }

        //单发送消息头，没有消息体
        public void SEND_CG_REQ_MSGID(MsgId id)
        {
            Send(id);
        }


        //以下是
        //需要发送消息体和消息头的

        //登陆申请：提供账号和密码
        public void SEND_CG_REQ_LOGIN()
        {
            Msg.Login login = new Msg.Login();
            login.Account = NetworkMgr.instance.Account;
            login.Password = NetworkMgr.instance.PassWord;
            //login.Pf = (uint)ResManager.GetInstance().GetOperatingPlatform();
            //login.Pf = 0;
            MemoryStream ms = GetStream();
            Msg.Login.Serialize(ms, login);
            Send(MsgId.ID_Login, ms);
        }
        //角色信息请求，向服务器发送账号序列id
        public void SEND_CG_REQ_PLAYER_DATA(uint accid,uint relogin)
        {
            PlayerInfo ip = new Msg.PlayerInfo();
            ip.Accid = accid;
            ip.Relogin = relogin;
            MemoryStream ms = GetStream();
            PlayerInfo.Serialize(ms, ip);
            Send(MsgId.ID_PlayerInfo, ms);
        }
        //读邮件
        public void SEND_CG_REQ_READ_EMAIL(uint id)
        {
            ReadEmail ip = new Msg.ReadEmail();
            ip.Id = id;
            MemoryStream ms = GetStream();
            ReadEmail.Serialize(ms, ip);
            Send(MsgId.ID_ReadEmail, ms);
        }
        //领取邮件附件
        public void SEND_CG_REQ_GET_EMAIL_ITEM(uint id)
        {
            GetEmailItem ip = new Msg.GetEmailItem();
            ip.Id = id;
            MemoryStream ms = GetStream();
            GetEmailItem.Serialize(ms, ip);
            Send(MsgId.ID_GetEmailItem, ms);
        }
        //更换头像
        public void SEND_CG_REQ_MODIFY_HEAD(uint id)
        {
            ModifyHead ip = new Msg.ModifyHead();
            ip.Id = id;
            MemoryStream ms = GetStream();
            ModifyHead.Serialize(ms, ip);
            Send(MsgId.ID_ModifyHead, ms);
        }
        //更换名字
        public void SEND_CG_REQ_MODIFY_NICKNAME(string name)
        {
            ModifyNickName ip = new Msg.ModifyNickName();
            ip.Name = name;
            MemoryStream ms = GetStream();
            ModifyNickName.Serialize(ms, ip);
            Send(MsgId.ID_ModifyNickName, ms);
        }
        //使用物品
        public void SEND_CG_REQ_USE_ITEM(uint id,uint nCount = 1)
        {
            UseItem ip = new UseItem();
            ip.Id = id;
            ip.Num = nCount;
            MemoryStream ms = GetStream();
            UseItem.Serialize(ms,ip);
            Send(MsgId.ID_UseItem, ms);
            //Debug.Log("id:"+id+",nCount:"+nCount);
        }
        //商店购买道具
        public void SEND_CG_REQ_BUY_ITEM(uint id)
        {
            BuyItem ip = new Msg.BuyItem();
            ip.Id = id;
            MemoryStream ms = GetStream();
            BuyItem.Serialize(ms, ip);
            Send(MsgId.ID_BuyItem, ms);
        }
        //添加道具，调试用
        //id 为道具id  num道具数量
        public void SEND_CG_REQ_ADD_ITEM(uint id,uint num)
        {
            AddItem ip = new Msg.AddItem();
            ip.Id = id;
            ip.Num = num;
            MemoryStream ms = GetStream();
            AddItem.Serialize(ms, ip);
            Send(MsgId.ID_AddItem, ms);
        }
        //投币
        //Type =1投篮
        //Type = 2砸箱子
        public void SEND_CG_REQ_SHOT_GOLD(uint type,Msg.Coin coin)
        {
            ShotGold ip = new Msg.ShotGold();
            ip.Coin = coin;
            ip.Type = type;
            MemoryStream ms = GetStream();
            ShotGold.Serialize(ms, ip);
            Send(MsgId.ID_ShotGold, ms);
        }
        //投中的金币
        //Type =1投篮
        //Type = 2砸箱子
        public void SEND_CG_REQ_SHOT_RESULT(uint type,Msg.Coin coin)
        {
            ShotResult ip = new Msg.ShotResult();
            ip.Coin = coin;
            ip.Type = type;
            MemoryStream ms = GetStream();
            ShotResult.Serialize(ms, ip);
            Send(MsgId.ID_ShotResult, ms);
        }
        //进入地图
        public void SEND_CG_REQ_ENTER_MAP(uint id)
        {
            EnterMap ip = new Msg.EnterMap();
            ip.Id = id;
            MemoryStream ms = GetStream();
            EnterMap.Serialize(ms, ip);
            Send(MsgId.ID_EnterMap, ms);
        }
        //桌面金币减少type=1获得金币
        //Type=2 流失金币
        public void SEND_CG_REQ_REDUCE_GOLD(List<RGold> coins)
        {
            ReduceGold ip = new Msg.ReduceGold();
            ip.Coins = coins;
            MemoryStream ms = GetStream();
            ReduceGold.Serialize(ms, ip);
            Send(MsgId.ID_ReduceGold, ms);
        }
        //摇奖
        //way:1自动   2手动
        public void SEND_CG_REQ_ROTATE_START(uint way,uint id)
        {
            //LuckyDrawManager.GetInstance().SetLuckyDrawStatus(LuckyDrawManager.LuckyDrawState.LuckyDrawRoll);
            GameRotateStart ip = new Msg.GameRotateStart();
            ip.Way = way;
            ip.Id = id;
            MemoryStream ms = GetStream();
            GameRotateStart.Serialize(ms, ip);
            Send(MsgId.ID_GameRotateStart, ms);
        }
        public void SEND_CG_REQ_TEST_START(uint id)
        {
            GameRotateResult ip = new Msg.GameRotateResult();
            ip.Id = id;
            ip.Power = 0;
            MemoryStream ms = GetStream();
            GameRotateResult.Serialize(ms, ip);
            Send(MsgId.ID_GameRotateResult, ms);
        }
        //增加经验
        public void SEND_CG_REQ_ADD_EXP(uint exp)
        {
            AddExp ip = new Msg.AddExp();
            ip.Exp = exp;
            MemoryStream ms = GetStream();
            AddExp.Serialize(ms, ip);
            Send(MsgId.ID_AddExp, ms);
        }

        public void SEND_CG_REQ_FINISH_ACHIEVEMENT(int index)
        {
            FinishAchievement ip = new FinishAchievement();
            ip.Id = (uint)index;
            MemoryStream ms = GetStream();
            FinishAchievement.Serialize(ms, ip);
            Send(MsgId.ID_FinishAchievement, ms);
        }
        //领取地图解锁奖励
        public void SEND_CG_REQ_GET_MAP_REWARD(uint id)
        {
            GetMapReward ip = new GetMapReward();
            ip.Id = id;
            MemoryStream ms = GetStream();
            GetMapReward.Serialize(ms, ip);
            Send(MsgId.ID_GetMapReward, ms);
            Debug.Log("SEND_CG_REQ_GET_MAP_REWARD"+ id);
        }

        public void SEND_CG_REQ_LUCKY_DRAW_COMPLETE()
        {
            Send(MsgId.ID_GameRotateOver);
            //LuckyDrawManager.GetInstance().SetLuckyDrawComplete();
        }

        //获取当前定时活动进度信息
        public void SEND_CG_REQ_GET_ACTIVITY_INFO()
        {
            Send(MsgId.ID_Activity);
        }

        //领取定时活动奖励
        public void SEND_CG_REQ_GET_ACTIVITY_REWARD()
        {
            Send(MsgId.ID_FinishActivity);
        }
        //完成指定引导
        public void SEND_CG_REQ_FINISH_GUIDE(uint id)
        {
            FinishGuide ip = new FinishGuide();
            ip.Id = id;
            MemoryStream ms = GetStream();
            FinishGuide.Serialize(ms, ip);
            Send(MsgId.ID_FinishGuide, ms);
        }
        //领取激活码礼包奖励
        public void SEND_CG_REQ_GIFT_WORD(string word)
        {
            GetGiftWord ip = new GetGiftWord();
            ip.Word = word;
            MemoryStream ms = GetStream();
            GetGiftWord.Serialize(ms, ip);
            Send(MsgId.ID_GetGiftWord, ms);
        }
    }
}