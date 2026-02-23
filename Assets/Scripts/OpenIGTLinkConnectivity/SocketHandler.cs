// Code retrieved from: https://github.com/BIIG-UC3M/IGT-UltrARsound
/*
* Code created by Marius Krusen
* Modified by Niklas Kompe, Johann Engster, Phillip Overloeper
* Further modified to support Meta Quest 3 connectivity
*/
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
    // Objects for the tcp communication

    // Implementation with TcpClient
    /// <summary>
    /// Tcp client for server communication
    /// </summary>
    private TcpClient tcpClient;

    /// <summary>
    /// Stream to receive and send massages
    /// </summary>
    private NetworkStream clientStream;

    /// <summary>
    /// Connects socket to server.
    /// </summary>
    /// <param name="ip">Server ip</param>
    /// <param name="port">Server port</param>
    /// <returns>If socket connection was successfull.</returns>
    public bool Connect(string ip, int port)
    {
        try
        {
            // Create a TcpClient
            tcpClient = new TcpClient(ip, port);
            // Create clientStream for further communication
            clientStream = tcpClient.GetStream();
            return true;
        }
        catch (Exception e)
        {
            Debug.Log("Connecting exception " + e);
        }
        return false;
    }


    /// <summary>
    /// Method to send strings to the server.
    /// </summary>
    /// <param name="msg">Massage to be send.</param>
    public void Send(String msg)
    {
        byte[] msgAsByteArray = Encoding.ASCII.GetBytes(msg);
        Send(msgAsByteArray);
    }


    /// <summary>
    /// Method to send bytes to the server.
    /// </summary>
    /// <param name="msg">Massage to be send.</param>
    public void Send(byte[] msg)
    {
        if (clientStream.CanWrite)
        {
            clientStream.Write(msg, 0, msg.Length);
        }
    }


    /// <summary>
    /// Method to receive a byte array from the server.
    /// </summary>
    /// <returns>Massage the server has sent.</returns>
    public byte[] Listen(uint msgSize)
    {
        
        
        Byte[] bytes = new Byte[msgSize]; ////////////////////////////////////////////////////////////////// Size of transform message ///////////////////////////
        List<byte> byteList = new List<byte>();
        StringBuilder receivedMsg = new StringBuilder();
        int readBytes = 0;

        while (clientStream.CanRead && clientStream.DataAvailable)
        {
            readBytes = clientStream.Read(bytes, 0, bytes.Length);
            receivedMsg.AppendFormat("{0}", Encoding.ASCII.GetString(bytes, 0, readBytes));
            byteList.AddRange(bytes);
        }

        byte[] allBytes = new byte[byteList.Count];
        allBytes = byteList.ToArray();
        return allBytes;

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
