/*
*** author：liangba
*** date: 2014.02.21
**  desc:消息缓存区处理
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEngine;
//********************************************************
//修正了读取和写入的边界条件
//去掉分片读取时的动态申请临时存储区
//   by Enic.P  20110329
//********************************************************


namespace MSDMX.BQ
{
    public class Sockbuff
    {
        //byte[] m_lpszBody;
        MemoryStream m_buff = new MemoryStream(1024*8*1024);
        public int m_max = 1024*8*1024;
        public int m_tail = 0;
        public int m_head = 0;
        public int m_use = 0;
        public void Free()
        {
            m_tail = 0;
            m_head = 0;
            m_use = 0;
            m_buff.Position = 0;
        }
        public bool Put(byte[] pData, int nSize)
        {
            if (nSize < 0)
            {
                //Debuger.Log("Put   if (nSize < 0)  " + nSize);
                return false;
            }
            if (nSize > m_max - m_use)
            {
                //Debuger.Log("Put   if (nSize > m_max - m_use)  " + nSize);
                return false;
            }
            else
            {
                //将流当前指针移动到 写位置 处
                m_buff.Position = m_head;
                //如果头在前面，则先填满再接尾
                if (m_head >= m_tail)       //注意边界条件     modify by Enic.P 2011.03.29
                {
                    if (nSize < m_max - m_head)
                    {
                        m_buff.Write(pData, 0, nSize);
                    }
                    else
                    {
                        int nSize0 = m_max - m_head;
                        int nSize1 = nSize - nSize0;
                        m_buff.Write(pData, 0, nSize0);

                        m_buff.Position = 0; //将指针移到缓冲区头
                        if (nSize1 > 0)
                        {
                            m_buff.Write(pData, nSize0, nSize1);
                        }
                    }

                }
                else
                {
                    m_buff.Write(pData, 0, nSize);
                }

                m_head = (int)m_buff.Position;
                m_use += nSize;

            }

            return true;

        }
        public bool Pop(byte[] pData, int nSize, bool bVirtual)
        {
            if (nSize <= 0)
            {
                //Debuger.Log("Pop   nSize <= 0)  " + nSize);
                return false; //注意边界
            }

            if (nSize > m_use)
            {
                //Debuger.Log("Pop   if (nSize > m_use))  " + nSize);
                return false;
            }
            else
            {
                int old_head = m_head;
                int old_tail = m_tail;
                int old_use = m_use;
                //将流当前指针移动到 读位置 处
                m_buff.Position = m_tail;
                if (m_head > m_tail)
                {

                    m_buff.Read(pData, 0, nSize);

                    m_tail += nSize;
                }
                else
                {
                    if (nSize < m_max - m_tail)
                    {
                        m_buff.Read(pData, 0, nSize);

                        m_tail += nSize;
                    }
                    else
                    {
                        int nSize0 = m_max - m_tail;
                        int nSize1 = nSize - nSize0;
                        //byte[] data0 = new byte[4096];
                        //byte[] data1 = new byte[4096];
                        //m_buff.Read(data0, 0, nSize0);
                        m_buff.Read(pData, 0, nSize0);

                        m_tail = 0;
                        //同步移动流当前指针
                        m_buff.Position = m_tail;

                        //m_buff.Read(data1, 0, nSize1);
                        m_buff.Read(pData, nSize0, nSize1);
                        m_tail += nSize1;
                        //Array.Copy(data0, 0, pData, 0, nSize0);
                        //Array.Copy(data1, 0, pData, nSize0, nSize1);
                    }
                }
                m_use -= nSize;
                if (bVirtual == true)
                {
                    m_use = old_use;
                    m_head = old_head;
                    m_tail = old_tail;

                }

                m_buff.Position = m_head;
            }
            return true;
        }
        public int GetUseBufSize()
        {
            return m_use;
        }
    }
}
