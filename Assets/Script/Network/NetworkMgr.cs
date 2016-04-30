using UnityEngine;
using System.Collections;

using System;
using System.Net;
using System.Net.Sockets;
using MSDMX.BQ;
using NetworkSendHandler;
using Msg;
using System.IO;
using System.Net.NetworkInformation;

public class NetworkMgr : MonoBehaviour {

    string GateIP = "47.88.213.1";

    public int GatePort = 5577;
    public int ServerID = 0;
    public string Account = "test1";
    public uint AccID = 0;         //游客登录时需填写mac地址
    public string PassWord = "";
    private string _GameIp = "";
    private int _GamePort = 0;

    //重连标识
    public uint _relogin = 0;
    //版本号标志
    public string _verson = "1.0.2015.8.10";
    public static BQSocket sock = new BQSocket();           //登陆的sock
    public static SendHandler m_pSend = new SendHandler();

    IPEndPoint epGate;  //网关地址
    IPEndPoint epAccount = null;  //后端地址
    IPEndPoint epGame;  //后端地址

    public static bool m_bUseTGW = false;
    public static bool m_IsConnectGameServer = false;
    public static string tgwGate = "";
    public static string tgwGame = "";

    public static int m_connet_time = 0;

    //发送信息的数据流
    public static byte[] bsout = new byte[1024];
    public static int bodyLen = 0;
    public static MsgId recMsgId;
    public static MsgId msgId;
    public bool relogin = false;           //重连登陆

    //是否连接上服务器
    public static bool _online = false;

    public string strMacAddress = "Not Mac";
#if UNITY_ANDROID
    AndroidJavaClass ajc;
    AndroidJavaObject ajo;
#endif

    public enum ServerType      //连接的服务器类型
    {
        Server_Gate,            //网关
        Server_Game,            //后端
    }

    public enum SockState     //连接状态
    {
        e_none,
        e_disconnect,
        e_tryconnect,
        e_connect,
        e_free,
        e_connecting,
    };

    private ServerType _curConnType = ServerType.Server_Gate;
    public ServerType curConnType
    {
        get
        {
            return _curConnType;
        }
    }
    private SockState _state;

    private float m_time = 0;   //心跳包发送间隔
    private bool m_bReconnect = false;


    private static NetworkMgr _instance = null;

    //重新连接
    public void SetReconnect()
    {
        m_bReconnect = true;
    }


    public static NetworkMgr instance
    {
        get
        {
            return _instance;
        }
    }

    void Awake()
    {
    }

    public static NetworkMgr GetInstance()
    {
        if (_instance) return _instance;
        var networkMgrObj = new GameObject("NetworkMgrObj");
        _instance = networkMgrObj.AddComponent<NetworkMgr>();
        DontDestroyOnLoad(networkMgrObj);
        networkMgrObj.hideFlags = HideFlags.HideInHierarchy;
        return _instance;
    }

    public void OnPreloadingResources()
    {
        AddDelegatePacketProc(AnsNetWork);
        sock.Reset();
    }

    public static void Log(object obj)
    {
        Debug.Log(obj);
    }

    public static void LogByte(byte[] data)
    {
        string pdata = "";
        for (int i = 0; i < data.Length; i++)
        {
            pdata += data[i] + ",";
        }
        Debug.Log(pdata);
    }
    public static void LogInt(int[] data)
    {
        string pdata = "";
        for (int i = 0; i < data.Length; i++)
        {
            pdata += data[i] + ",";
        }
        Debug.Log(pdata);
    }
    //设置连接参数  指定 网关还是游戏服 
    public bool SetServer(string ip, int port, ServerType type)
    {
        IPAddress IP;
        try
        {
            IP = IPAddress.Parse(ip);
        }
        catch (System.Exception ex)
        {
            //TODO: 弹框处理
            Debug.Log(ex.ToString());
            Debug.Log("地址错了");
            return false;
        }
        //设置要连接server的 ip,port
        //Log("Set =====>" + Type);
        if (type == ServerType.Server_Gate)
        {
            epGate = new IPEndPoint(IP, (int)port);
            epGate.Address = IP;
            epGate.Port = (int)port;
            //GameSDKConfig.GameTypeID = false;
        }
        if (type == ServerType.Server_Game)
        {
            epGame = new IPEndPoint(IP, (int)port);
            epGame.Address = IP;
            epGame.Port = (int)port;
            //GameSDKConfig.GameTypeID = true;
        }

        return true;
    }
    public void SetConType(ServerType conn)
    {
        _curConnType = conn;
    }
    public void SetConState(SockState state)
    {
        //Debug.Log("SetconState :" + _state + "--->" + state);
        _state = state;
    }

    //连接 指定是连接 网关还是后端
    public bool Connect()
    {
        if (_curConnType == ServerType.Server_Gate)
        {
            //Debug.Log("gate connetct.....");
            sock.SetIPEP(epGate);
            m_pSend.SEND_CG_REQ_MSGID(MsgId.ID_GameServer);

        }
        else if (_curConnType == ServerType.Server_Game)
        {
            //Debug.Log("game server connetct.....");
            sock.SetIPEP(epGame);
        }
        bool Res = sock.Connect();

        return Res;
    }

    public bool EditorConnected()
    {
        if (_curConnType == ServerType.Server_Gate)
        {
            //版本通知
            //m_pSend.SEND_CG_REQ_SERVER_VERSION(_verson);
        }
        else if (_curConnType == ServerType.Server_Game)
        {
            _online = true;
            if (_relogin==1)
            {
                m_pSend.SEND_CG_REQ_LOGIN();
            }
        }
        return true;
    }

    public void Reconnect(ServerType type)
    {
        //设置重新连接
        Debug.Log("Reconnect");
        m_bReconnect = true;
        SetConType(type);
        sock.CloseSocket(false);
        SetConState(SockState.e_disconnect);
        Debug.Log(_state);
    }

    public void AddDelegatePacketProc(PacketProc pktproc)
    {
        sock.AddPacketProcs(pktproc);
    }
    public void DelDelegatePacketProc(PacketProc pktproc)
    {
        sock.DelPacketProcs(pktproc);
    }
    void Update()
    {
        if (sock != null)
        {
            switch (_state)
            {

                case SockState.e_none:
                    {
                        break;
                    }
                case SockState.e_tryconnect:
                    {
                        Connect();
                        break;
                    }
                case SockState.e_connecting:
                    {
                        //todo 计时
                        break;
                    }
                case SockState.e_connect:
                    {
                        sock.Proc();
                        break;
                    }
                case SockState.e_disconnect:
                    {
                        sock.Reset();
                        if (m_bReconnect)
                        {
                            m_bReconnect = false;

                            SetConState(SockState.e_tryconnect);
                        }
                        else
                        {
                            SetConState(SockState.e_free);
                            //Debug.Log("lost server");
                            //UIGlobal.isReconnect = true;
                            //掉线处理 弹出掉线提示
                            //UIManager.GetInstance().ShowNotice(UIManager.NoticeType.ReconnectNotice);
                            //掉线客户端逻辑
                            //Time.timeScale = 0;
                            _online = false;
                        }
                        break;
                    }
                case SockState.e_free:
                    {

                        if (m_bReconnect)
                        {
                            SetConState(SockState.e_disconnect);
                        }
                        break;
                    }
            }
        }
        //进入后台服务器后监测心跳包，服务端监测心跳包超时间隔 10s，
        if (_curConnType == ServerType.Server_Game && m_time < Time.realtimeSinceStartup)
        {
            if (_online)
            {
                m_time = Time.realtimeSinceStartup + 10.0f;
                m_pSend.SEND_CG_REQ_MSGID(MsgId.ID_HeartBeat);
            }
        }
    }

    public void connectgameServer()
    {
        SetServer(_GameIp, _GamePort, ServerType.Server_Game);
        Reconnect(ServerType.Server_Game);
    }

    void OnApplicationQuit()
    {
        ////游戏退出，处理线程终止和一些必要的clear
        if (sock != null)
        {
            Debug.Log("Application quit ,clean socket resource");
            sock.CloseSocket(true);
        }
        _instance = null;
    }
    public void ReconnectGateServer()
    {
        SetServer(GateIP, GatePort, ServerType.Server_Gate);
        Reconnect(ServerType.Server_Gate);
    }

    public string GetServerInfo()
    {
        return "serverIp:" + _GameIp + ",Port:" + _GamePort;
    }
    public void AnsNetWork(int id, Stream body)
    {
        //Debug.Log("Recv Msg from server: id=" + id);
        switch ((MsgId)id)
        {
            case MsgId.ID_GameServer:
                {
                    GameServer msg = new GameServer();
                    GameServer.Deserialize(body, msg);
                    Debug.Log("gameserver ip = " + msg.Ip);
                    Debug.Log("gameserver port = " + msg.Port);
                    _GameIp = msg.Ip;
                    _GamePort = (int)msg.Port;
                    //重连到游戏服务器 
                    SetServer(_GameIp, _GamePort, ServerType.Server_Game);
                    Reconnect(ServerType.Server_Game);
                    Debug.Log("ID_GameServer");
                    break;
                }
        }
    }


}
