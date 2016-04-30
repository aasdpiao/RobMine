/*
*** author：liangba
*** date: 2014.02.21
**  desc:网络层socket逻辑处理
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using UnityEngine;
using System.Net.Security;
using Msg;


namespace MSDMX.BQ
{
    static class Constants
    {
        public const int DEF_MAX_PACKET_SIZE = 1024 * 50;
        public const int DEF_MAX_IO_BUF = 1024 * 4;
    }

    //消息包处理委托
    public delegate void PacketProc(int id, Stream body);


    public class BQSocket
    {
        private Socket _socket = null;

        private IPEndPoint _ipEP = null;       //当前连接的ipep
        public string tgwaddr = "";
        private int Port = 0;

        //发送接收缓冲区
        private Sockbuff _sendBuf = new Sockbuff();
        private Sockbuff _recvBuf = new Sockbuff();

        public ulong m_flow = 0;
        //一次发送的数据
        private byte[] m_sendBody = new byte[Constants.DEF_MAX_IO_BUF];
        private int m_sendSize;

        //一次接收到的数据
        private byte[] m_recvBody = new byte[Constants.DEF_MAX_IO_BUF * 1024];
        //private byte[] m_recvBody = new byte[Constants.DefMaxIoBuf];

        //接收缓存pop出来要处理的数据
        private byte[] m_proxyData = new byte[Constants.DEF_MAX_PACKET_SIZE];
        //缓冲区锁
        private object _sendLock = new object();
        private object _recvLock = new object();

        private int m_recvSize = 0;
        //消息包分发处理 容器
        PacketProc _packetProcs;

        //HLA客户端处理
        //public CHlaRuntime_C hlaRuntime = new CHlaRuntime_C();

        //消息处理暂存变量（内存优化）
        byte[] recvlen = new byte[2];
        Stream recvMsg = new MemoryStream();

        byte[] recvID = new byte[2];
        byte[] msgBody = new byte[Constants.DEF_MAX_PACKET_SIZE];
        // Stream pdBody = new MemoryStream();

        private int trytimes = 0;
        //private float reconnecttime = 0;
        private static int maxtrytimes = 20;
        private bool bShutdown = false;

        public void SetIPEP(IPEndPoint ep)
        {
            _ipEP = ep;
        }
        public void Reset()
        {
            _recvBuf.Free();
            _sendBuf.Free();
            trytimes = 0;
            //reconnecttime = 0;
            m_recvSize = 0;

        }

        public void Proc()
        {
            ProcRecvBuffer();
            ProcSendBuffer();
        }
        //socke连接
        public bool Connect()
        {
            if (_ipEP == null)
            {
                Debug.Log("_ipEP  is null!!!");
                return false;
            }

            //连接重试 间隔
            //if (reconnecttime > Time.realtimeSinceStartup)
            //    return false;
            if (_socket != null )
            {
                Debug.Log("close  Connected:");
                CloseSocket(true);
               
            }

            try
            {                    
                NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_connecting);
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _socket.BeginConnect(_ipEP, new AsyncCallback(ConnectCallback), _socket);       
                return true;
            }
            catch (System.Exception ex)
            {
                // bConnecting = false;
                // networkmgr下一帧继续连接
                NetworkMgr.m_connet_time += 1;
                if (NetworkMgr.m_connet_time > 10)
                {
                    NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_disconnect);
                    NetworkMgr._online = false;
                }
                else
                {
                    NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_free);
                }
            }   
            return false;
        }
        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                Socket lsock = (Socket)ar.AsyncState;
                lsock.EndConnect(ar);
                if (lsock.Connected)
                {  

                    //端口复用设置
                    lsock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    //设置非阻塞
                    lsock.Blocking = false;
                    //去web服务器  拿取登录用户信息
                    lsock.BeginReceive(m_recvBody, 0, Constants.DEF_MAX_IO_BUF, SocketFlags.None, new AsyncCallback(ReceiveCallback), lsock);
                    NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_connect);
                    NetworkMgr.instance.EditorConnected();
                }
            }
            catch (System.Exception ex)
            {
                NetworkMgr.m_connet_time += 1;
                Debug.Log("Error-------->>>>>connect failure  +  " + ex.ToString());
                _socket = null;
                if (NetworkMgr.m_connet_time > 10)
                {
                    NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_disconnect);
                    NetworkMgr._online = false;
                }
                else
                {
                    NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_tryconnect);
                    //reconnecttime += Time.realtimeSinceStartup + 1.0f;
                }
            }

        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            if (bShutdown) return;
            try
            {
                Socket socket = (Socket)ar.AsyncState;//获得调用时传递的StateObject对象 
                int byteRead = socket.EndReceive(ar);

                if (byteRead > 0)//判断是否接受到了新信息 
                {
                    AddRecvData(m_recvBody, byteRead);
                    m_flow = m_flow + (ulong)byteRead;
                    socket.BeginReceive(m_recvBody, 0, Constants.DEF_MAX_IO_BUF, SocketFlags.None, new AsyncCallback(ReceiveCallback), socket);
                }
            }
            catch (Exception e)
            {
                //global::NetworkMgr.mConnectState = NetworkMgr.SockState.S_DisConnect;
                Debug.Log(e.ToString());
				NetworkMgr._online = false;
                CloseSocket(false);
                NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_disconnect);
            }
        }

        //发送消息包
        public bool Send()
        {
            if (bShutdown) return true;

            //先锁住缓冲区，再添加
            if (!_socket.Connected)
            {
                return false;
            }

            //  lock (_sendLock)
            {
                //应pop掉所有的一次性发送出去
                try
                {
                    int ret = _socket.Send(m_sendBody, m_sendSize, SocketFlags.None);
                    if (ret < 0)
                    {
                        //error!
                        Debug.Log("send error!break=" + ret);
                        NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_disconnect);
                        NetworkMgr._online = false;
                        return false;
                    }
                    m_flow = m_flow + (ulong)ret;
                }
                catch (Exception ex)
                {
                    //error!
                    Debug.Log(ex.ToString());
                    NetworkMgr._online = false;
                    NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_disconnect);
                }

                return true;
            }


        }

        public void ProcRecvBuffer()
        {
            if (bShutdown) return;
            //先加锁
            int cur_size = 0;
            int max_size = 0;

            while (true)
            {

                lock (_recvLock)
                {
                    cur_size = 0;
                    max_size = 0;
                    if (_socket == null || !_socket.Connected)
                    {
                        CloseSocket(false);
                        NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_disconnect);
                        break;
                    }

                    max_size = _recvBuf.GetUseBufSize();
                    if (max_size < sizeof(ushort)) break;

                    if (!_recvBuf.Pop(recvlen, 2, true))
                    {
                        break;
                    }

                    int net_size = (ushort)BitConverter.ToUInt16(recvlen, 0);
                    cur_size = IPAddress.NetworkToHostOrder(net_size << 16);
                    //NetworkMgr.Log("recv size: " + cur_size +"== "+recvlen[0]+" "+recvlen[1] +" "+IPAddress.NetworkToHostOrder(cur_size<<16));
                    if (cur_size <= 0 || cur_size > Constants.DEF_MAX_PACKET_SIZE)
                    {
                        _recvBuf.Free();
                        break;
                    }
                    if (max_size < cur_size + 2) break;
                    _recvBuf.Pop(recvlen, 2, false);
                    if (!_recvBuf.Pop(m_proxyData, cur_size, false))
                    {
                        break;
                    }
                    //TODO 转下类型再发送
                    //global::NetworkMgr.Log("接收到字节大小:" + max_size);
                    recvMsg.Position = 0;
                    recvMsg.Write(m_proxyData, 0, cur_size);
                    recvMsg.Position = 0;
                    ProcPacket(recvMsg, cur_size - 2);
                }
            }
        }

        public void ProcSendBuffer()
        {
            if (bShutdown) return;
            //先加锁
            //if (!m_bSend) return;
            if (_socket == null || !_socket.Connected)
            {
                //global::NetworkMgr.mConnectState = NetworkMgr.SockState.S_DisConnect;
                CloseSocket(false);
                NetworkMgr.instance.SetConState(NetworkMgr.SockState.e_disconnect);
                return;
            }

            lock (_sendLock)
            {
                int pop_size = _sendBuf.GetUseBufSize();
                if (pop_size < 1) return;
                if (pop_size <= Constants.DEF_MAX_PACKET_SIZE)
                {
                    m_sendSize = pop_size;
                }
                else
                    m_sendSize = Constants.DEF_MAX_PACKET_SIZE;

                if (!_sendBuf.Pop(m_sendBody, m_sendSize, false))
                {
                    return;
                }
                Send();
            }

        }

        public void ProcPacket(Stream msg, int size)
        {
            msg.Read(recvID, 0, 2);
            int net_id = (ushort)BitConverter.ToUInt16(recvID, 0);
            ushort msgID = (ushort)(IPAddress.NetworkToHostOrder(net_id << 16));


            msg.Position = 2;
            msg.Read(msgBody, 0, size);
            //NetworkMgr.Log("recv: " + msgID);
            Stream pdBody = new MemoryStream();
            pdBody.Write(msgBody, 0, size);
            pdBody.Position = 0;

            switch ((MsgId)msgID)
            {
                //这里要分消息模块来分

                case MsgId.ID_HeartBeat:
                    {
                        break;
                    }
                default:
                    {
                        _packetProcs(msgID, pdBody);
                        break;
                    }

            }
            //pdBody.Close();
            //pdBody.Dispose();
        }

        public void AddPacketProcs(PacketProc proc)
        {
            _packetProcs += proc;

        }

        public void DelPacketProcs(PacketProc proc)
        {
            _packetProcs -= proc;
        }

        public int AddSendData(byte[] pData, int nSize)
        {
            //先判断socket是否连接
            //加锁
            lock (_sendLock)
            {

                if (!_sendBuf.Put(pData, nSize))
                {
                    //Console.WriteLine("error!");
                    return 0;
                }
            }
            return nSize;
        }

        public int AddRecvData(byte[] pData, int nSize)
        {
            lock (_recvLock)
            {
                if (nSize <= 0)
                {
                    return 0;
                }

                //判断是否断开连接
                if (!_recvBuf.Put(pData, nSize))
                {
                    Debug.Log("AddRecvData:" + pData + nSize);
                    return 0;

                }
            }
            return nSize;

        }
        public Socket GetSocket()
        {
            return _socket;
        }
        public void CloseSocket(bool bshutdown)
        {
            try
            {
                if (_socket != null)
                {
                    _socket.Shutdown(SocketShutdown.Both);
                    _socket.Close();
                }
            }catch (Exception e)
            {
                Debug.Log("close socket error :" + e.ToString());
            }
            _socket = null;
            _sendBuf.Free();
            _recvBuf.Free();
        }

        public ulong GetFlow()
        {
            return m_flow;
        }
        public void SetFlow(ulong value)
        {
            m_flow = value;
        }

    }
}
