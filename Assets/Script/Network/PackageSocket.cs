using UnityEngine;
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using Msg;

public enum PackageSocketError
{
	None,
	SocketShutdown,
	RecviveBufferNotEnough,
	RecviveTimeout,
}

static class Constants
{
    public const int DefMaxPacketSize = 1024 * 50;
    public const int DefMaxIoBuf = 1024 * 4;
}

public class PackageSocket 
{
	enum State
	{
		Closed,
		Connecting,
		Connected,
		UserClosed,
		Error,
	}

    public delegate void PacketProc(int id, Stream body);
    public PacketProc PacketProcs;

    private byte[] _recvId = new byte[2];
    private byte[] _msgBody = new byte[Constants.DefMaxPacketSize];
    private Stream _recvMsg = new MemoryStream();

    public delegate void ConnectCallback(bool connected);
	public delegate void RecviveCallback(byte[] data, int start, int length);
	public delegate void DisconnectCallback(SocketError socketError, PackageSocketError packageSocketError);

	public ConnectCallback OnConnect;
	public RecviveCallback OnRecvive;
	public DisconnectCallback OnDisconnect;
	public int ConnectTimeoutSetting = 3000;
	public int ReceiveTimeoutSetting = 10000;
	public int AutoSendPingSetting = 10000 / 3;
	
	private State CurState;
	private DateTime CheckTimeout;
	
	private Socket CurSocket; 
	private SocketError LastSystemSocketError;
	private PackageSocketError LastPackageSocketError;

	private Queue<byte[]> SendQueue;
	private int SendQueueStartIndex;

	private byte[] RecvBuffer;
	private int RecvBufferBeginIndex;
	private int RecvBufferEndIndex;

	private byte[] PingBuffer;
	private DateTime NextSendPingTime;
	
	const int MaxSizePerSend = 1024 * 4;		
	
	public PackageSocket()
	{
		SendQueue = new Queue<byte[]>();
		RecvBuffer = new byte[(int)ushort.MaxValue + 2];
		Reset();
	}

	public void Connect(string server, int port)
	{
		Reset();

		CurSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		CurSocket.Blocking = false;
		
		try
		{
			var ip = GetIPAddress(server);
			CurSocket.Connect(ip, port);
		}
		catch (SocketException e)
		{
			if (e.SocketErrorCode != SocketError.WouldBlock && e.SocketErrorCode != SocketError.InProgress)
				throw e;
		}

		CurState = State.Connecting;
		CheckTimeout = DateTime.Now.AddMilliseconds(ConnectTimeoutSetting);
	}

	public void Close()
	{
		if (CurState != State.Closed)
		{
			CurState = State.UserClosed;
		}
	}

	public void Update()
	{
		switch (CurState) 
		{
		case State.Closed:
			return;

		case State.Connecting:
			ProcessConnect();
			break;

		case State.Connected:
			ProcessSend();
			ProcessReceive();
			break;

		case State.Error:
			Reset();
			OnDisconnect(LastSystemSocketError, LastPackageSocketError);
			break;

		case State.UserClosed:
			Reset();
			break;
		}
	}

	public void Send(Byte[] buffer, int start, int length)
	{
		var headerLen = GetHeaderLength();
		var data = new byte[headerLen + length];

		EncodeHeader(length, data, 0);
		Array.Copy(buffer, start, data, headerLen, length);
		SendQueue.Enqueue(data);

		ProcessSend();
	}

	public void SendPing()
	{
		if (PingBuffer == null) 
		{
			var headerLen = GetHeaderLength();
			PingBuffer = new byte[headerLen];
			EncodeHeader(0, PingBuffer, 0);
		}
		SendQueue.Enqueue(PingBuffer);
		ProcessSend();
	}

	void ProcessConnect()
	{
		var now = DateTime.Now;

		CurSocket.Poll(0, SelectMode.SelectWrite);

		if (!CurSocket.Connected && CheckTimeout < now) 
		{
			Reset();
			OnConnect(false);
		}
		else if (CurSocket.Connected)
		{
            OnConnect(true);
            CurState = State.Connected;
			CheckTimeout = now.AddMilliseconds(ReceiveTimeoutSetting);
        }
    }
    
	void ProcessSend()
	{
		if (!CurSocket.Poll(0, SelectMode.SelectWrite))
			return;

		while (CurState == State.Connected && SendQueue.Count > 0) 
		{
			var buffer = SendQueue.Peek();
			int leftLength = buffer.Length - SendQueueStartIndex;
			
			while (leftLength > 0)
			{
				var trySend = Math.Min(MaxSizePerSend, leftLength);
				SocketError error;
				
				var send = CurSocket.Send(buffer, SendQueueStartIndex, trySend, SocketFlags.None, out error); 
				if (error != SocketError.Success)
				{
					if (error != SocketError.WouldBlock && error != SocketError.Interrupted &&  error != SocketError.TryAgain)
					{
                        SetError(error, PackageSocketError.None);
                        return;
                    }
                    break;
                }
                
				SendQueueStartIndex += send;
				leftLength -= send;
			}

			if (leftLength == 0)
			{
				SendQueue.Dequeue();
				SendQueueStartIndex = 0;
			}
		}
	}
    
	void ProcessReceive()
	{
		var now = DateTime.Now;
		if (!CurSocket.Poll(0, SelectMode.SelectRead)) 
		{
			if (NextSendPingTime < now)
			{
				SendPing();
				NextSendPingTime = now.AddMilliseconds(AutoSendPingSetting);
			}

            if (CheckTimeout < now)
				SetError(SocketError.Success, PackageSocketError.RecviveTimeout);
			return;
		}

		while (CurState == State.Connected) 
		{
			var leftBufferSize = RecvBuffer.Length - RecvBufferEndIndex;
			SocketError error;
			var receive = CurSocket.Receive(RecvBuffer, RecvBufferEndIndex, leftBufferSize, SocketFlags.None, out error);
			if (error != SocketError.Success)
			{
				if (error != SocketError.WouldBlock && error != SocketError.Interrupted &&  error != SocketError.TryAgain)
					SetError(error, PackageSocketError.None);
				break;
			}

			RecvBufferEndIndex += receive;
			ProcessPackage();
			
			if (receive == 0)
			{
				SetError(error, PackageSocketError.SocketShutdown);
				break;
			}

			CheckTimeout = now.AddMilliseconds(ReceiveTimeoutSetting);
			NextSendPingTime = now.AddMilliseconds(AutoSendPingSetting);
		}
	}

	void ProcessPackage()
	{
		while (CurState == State.Connected)
		{
			var bufferLen = RecvBufferEndIndex - RecvBufferBeginIndex;
			var dataLen = 0;
			var headerLen = DecodeHeader(RecvBuffer, RecvBufferBeginIndex, bufferLen, ref dataLen);
			if (headerLen == 0)
			{
				headerLen = GetHeaderLength();
				if (RecvBufferBeginIndex + headerLen > RecvBuffer.Length)
					MemmoveRecvBuffer();
				break;
			}

			var packageLen = headerLen + dataLen;
			if (packageLen > RecvBuffer.Length)
			{
				SetError(SocketError.Success, PackageSocketError.RecviveBufferNotEnough);
				break;
			}
			
			if (packageLen <= bufferLen)
			{
				if (dataLen > 0)
					OnRecvive(RecvBuffer, RecvBufferBeginIndex + headerLen, dataLen);
				RecvBufferBeginIndex += packageLen;
				continue;
			}

			if (RecvBufferBeginIndex + packageLen > RecvBuffer.Length)
				MemmoveRecvBuffer();
			break;
		}
	}

	public void MemmoveRecvBuffer()
	{
		var bufferLen = RecvBufferEndIndex - RecvBufferBeginIndex;
        _recvMsg.Write(RecvBuffer, 0, bufferLen);
        //Array.Copy(RecvBuffer, RecvBufferBeginIndex, RecvBuffer, 0, bufferLen);
		RecvBufferBeginIndex = 0;
		RecvBufferEndIndex = bufferLen;
        ProcPacket(_recvMsg, bufferLen-2);
    }

    public void ProcPacket(Stream msg, int size)
    {
        msg.Read(_recvId, 0, 2);
        int netId = BitConverter.ToUInt16(_recvId, 0);
        ushort msgId = (ushort)(IPAddress.NetworkToHostOrder(netId << 16));

        msg.Position = 2;
        msg.Read(_msgBody, 0, size);
        Stream pdBody = new MemoryStream();
        pdBody.Write(_msgBody, 0, size);
        pdBody.Position = 0;

        switch ((MsgId)msgId)
        {
            case MsgId.ID_HeartBeat:
                {
                    break;
                }
            default:
                {
                    PacketProcs(msgId, pdBody);
                    break;
                }
        }
    }

    public void AddPacketProcs(PacketProc proc)
    {
        PacketProcs += proc;
    }

    void SetError(SocketError socketError, PackageSocketError packageSocketError)
	{
		LastSystemSocketError = socketError;
		LastPackageSocketError = packageSocketError;
		CurState = State.Error;
	}
	
	IPAddress GetIPAddress(string server)
	{
		var hostInfo = Dns.GetHostEntry(server);
		foreach (var address in hostInfo.AddressList) 
		{
			if (address.AddressFamily == AddressFamily.InterNetwork)
				return address;
		}
		return null;
	}

	void Reset()
	{
		CurState = State.Closed;
		CurSocket = null;
		RecvBufferBeginIndex = 0;
		RecvBufferEndIndex = 0;
		SendQueue.Clear();
		SendQueueStartIndex = 0;
	}

	protected virtual int DecodeHeader(byte[] buffer, int start, int length, ref int dataLength)
	{
		if (length < 2)
			return 0;
		
		dataLength = buffer[start] << 8 | buffer[start + 1];
		return 2;
	}
	
	protected virtual void EncodeHeader(int length, byte[] buffer, int start)
	{
		buffer[start + 0] = (byte)((length >> 8) & 0xff);
		buffer[start + 1] = (byte)(length & 0xff);
    }

	protected virtual int GetHeaderLength()
	{
		return 2;
	}	

	public override string ToString ()
	{
		return string.Format("State:{0} SendQueue:{1}", CurState, SendQueue.Count);
    }


}
