using System;
using System.Text;
using UnityEngine;
using System.Collections.Generic;
using System.Net.Sockets;

/// <summary>
/// The class to communicate with the server socket.
/// </summary>
public class SocketHandler
{
    private TcpClient tcpClient;
    private NetworkStream clientStream;

    public bool Connect(string ip, int port)
    {
        try
        {
            tcpClient = new TcpClient(ip, port);
            clientStream = tcpClient.GetStream();
            return true;
        }
        catch (Exception e)
        {
            Debug.Log("Connecting exception " + e);
        }
        return false;
    }

    public void Send(String msg)
    {
        byte[] msgAsByteArray = Encoding.ASCII.GetBytes(msg);
        Send(msgAsByteArray);
    }

    public void Send(byte[] msg)
    {
        if (clientStream != null && clientStream.CanWrite)
        {
            clientStream.Write(msg, 0, msg.Length);
        }
    }

    public byte[] Listen(uint msgSize)
    {
        // 1. EARLY EXIT: Se non ci sono dati, non allochiamo NULLA. (0 Byte GC Alloc in idle)
        if (clientStream == null || !clientStream.CanRead || !clientStream.DataAvailable)
        {
            return null;
        }

        List<byte> byteList = new List<byte>();
        
        // 2. Usiamo un buffer ampio (es. 4KB) invece di leggere a pezzettini di 58 byte
        byte[] buffer = new byte[4096]; 
        int readBytes = 0;

        // 3. Rimosso l'inutile e pesantissimo StringBuilder
        while (clientStream.DataAvailable)
        {
            readBytes = clientStream.Read(buffer, 0, buffer.Length);
            
            // Copiamo solo i byte effettivamente letti in questo giro
            byte[] actualBytesRead = new byte[readBytes];
            Buffer.BlockCopy(buffer, 0, actualBytesRead, 0, readBytes);
            byteList.AddRange(actualBytesRead);
        }

        return byteList.ToArray();
    }

    public void Disconnect()
    {
        if (tcpClient != null)
        {
            clientStream.Close();
            tcpClient.Close();
            tcpClient = null;
        }
    }
}