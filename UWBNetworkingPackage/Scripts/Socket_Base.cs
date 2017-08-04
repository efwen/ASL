﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Net.Sockets;

namespace UWBNetworkingPackage
{
    public class Socket_Base
    {

        public static void SendFile(string filepath, Socket socket)
        {
            SendFiles(new string[1] { filepath }, socket);
        }

        public static void SendFiles(string[] filepaths, Socket socket)
        {
            // Needs to tell the client socket what the server's ip is
            //string configString = IPManager.CompileNetworkConfigString(Config.Ports.ClientServerConnection);

            foreach(string filepath in filepaths)
            {
                Debug.Log("Sending " + Path.GetFileName(filepath));
            }

            MemoryStream ms = new MemoryStream();
            PrepSocketData(filepaths, ref ms);
            socket.Send(ms.ToArray());
            ms.Close();
            ms.Dispose();
            socket.Shutdown(SocketShutdown.Both);
        }

        public static void PrepSocketData(string[] filepaths, ref MemoryStream ms)
        {
            string header = BuildSocketHeader(filepaths);

            byte[] headerData = System.Text.Encoding.UTF8.GetBytes(header);
            // Add header data length
            ms.Write(System.BitConverter.GetBytes(headerData.Length), 0, System.BitConverter.GetBytes(headerData.Length).Length);
            // Add header data
            ms.Write(headerData, 0, headerData.Length);

            foreach (string filepath in filepaths)
            {
                byte[] fileData = File.ReadAllBytes(filepath);
                // Add file data length
                ms.Write(System.BitConverter.GetBytes(fileData.Length), 0, System.BitConverter.GetBytes(fileData.Length).Length);
                // Add file data
                ms.Write(fileData, 0, fileData.Length);
            }
        }

        public static string BuildSocketHeader(string[] filepaths)
        {
            System.Text.StringBuilder headerBuilder = new System.Text.StringBuilder();

            foreach (string filepath in filepaths)
            {
                headerBuilder.Append(Path.GetFileName(filepath));
                headerBuilder.Append(';');
            }
            headerBuilder.Remove(headerBuilder.Length - 1, 1); // Remove the last separator (';')

            return headerBuilder.ToString();
        }

        public static void ReceiveFiles(Socket socket, string receiveDirectory)
        {
            int bufferLength = 1024;
            byte[] data = new byte[bufferLength];
            int numBytesReceived = 0;

            //using (MemoryStream fileStream = new MemoryStream())
            //{
            MemoryStream fileStream = new MemoryStream();

            int headerIndex = 0;
            int dataLengthIndex = 0;
            string dataHeader = string.Empty;

            // Get directory to save it to
            //string receiveDirectory = "C:\\Users\\Thomas\\Documents\\tempwritefile";

            do
            {
                // Get the first receive from the socket
                numBytesReceived = socket.Receive(data, bufferLength, SocketFlags.None);
                int numBytesAvailable = numBytesReceived;
                int dataIndex = 0;
                    
                // If there are any bytes that continue a file from the last buffer read, handle that here
                if (dataLengthIndex > 0 && dataLengthIndex < numBytesReceived)
                {
                    fileStream.Write(data, 0, dataLengthIndex);
                    string filename = dataHeader.Split(';')[headerIndex++];
                    File.WriteAllBytes(Path.Combine(receiveDirectory, filename), fileStream.ToArray());
                    // MemoryStream flush does literally nothing.
                    fileStream.Close();
                    fileStream.Dispose();
                    fileStream = new MemoryStream();
                }
                else if(numBytesReceived <= 0)
                {
                    string filename = dataHeader.Split(';')[headerIndex++];
                    File.WriteAllBytes(Path.Combine(receiveDirectory, filename), fileStream.ToArray());
                    // MemoryStream flush does literally nothing.
                    fileStream.Close();
                    fileStream.Dispose();
                    fileStream = new MemoryStream();
                }

                // While there are file pieces we can get from the gathered data,
                // determine where the bytes designating the lengths of files about to be
                // transferred over are and then grab the file lengths and file bytes
                    
                //while (dataLengthIndex < bufferLength)
                while(dataLengthIndex >= 0 && dataLengthIndex < numBytesReceived)
                {
                    // Get the 4 bytes indicating the length
                    int dataLength = 0;
                    if (dataLengthIndex <= numBytesReceived - 4)
                    {
                        // If length is shown fully within the buffer (i.e. length bytes aren't split between reads)...
                        dataLength = System.BitConverter.ToInt32(data, dataLengthIndex);

                        if(dataLength <= 0)
                        {
                            // Handle case where end of stream is reached
                            break;
                        }
                    }
                    else
                    {
                        // Else length bytes are split between reads...
                        byte[] dataLengthBuffer = new byte[4];
                        int numDataLengthBytesCopied = numBytesReceived - dataLengthIndex;
                        System.Buffer.BlockCopy(data, dataLengthIndex, dataLengthBuffer, 0, numDataLengthBytesCopied);
                        numBytesReceived = socket.Receive(data, bufferLength, SocketFlags.None);
                        System.Buffer.BlockCopy(data, 0, dataLengthBuffer, numDataLengthBytesCopied, 4 - numDataLengthBytesCopied);

                        dataLength = System.BitConverter.ToInt32(dataLengthBuffer, 0);
                        dataLengthIndex -= numBytesReceived;

                        //numBytesReceived = socket.Receive(data, bufferLength, SocketFlags.None);
                        //System.Buffer.BlockCopy(data, 0, dataLengthBuffer, numDataLengthBytesCopied, 4 - numDataLengthBytesCopied);

                        //dataLength = System.BitConverter.ToInt32(dataLengthBuffer, 0);
                        //dataLengthIndex -= bufferLength;
                    }
                    dataIndex = dataLengthIndex + 4;
                    dataLengthIndex = dataIndex + dataLength; // Update the data length index for the while loop check
                    numBytesAvailable = numBytesReceived - dataIndex;

                    // Handle instances where whole file is contained in part of buffer
                    if (dataIndex + dataLength < numBytesAvailable)
                    {
                        byte[] fileData = new byte[dataLength];
                        System.Buffer.BlockCopy(data, dataIndex, fileData, 0, dataLength);
                        if (dataHeader.Equals(string.Empty))
                        {
                            // If the header hasn't been received yet
                            dataHeader = System.Text.Encoding.UTF8.GetString(fileData);
                        }
                        else
                        {
                            // If the header's been received, that means we're looking at actual file data
                            string filename = dataHeader.Split(';')[headerIndex++];
                            File.WriteAllBytes(Path.Combine(receiveDirectory, filename), fileData);
                        }
                    }
                }

                // Write remainder of bytes in buffer to the file memory stream to store for the next buffer read
                if (numBytesAvailable < 0)
                {
                    break;
                }
                else
                {
                    fileStream.Write(data, dataIndex, numBytesAvailable);
                    dataLengthIndex -= bufferLength;
                }
                // continue;

            } while (numBytesReceived > 0);
            //} while (numBytesReceived == bufferLength);

            fileStream.Close();
            fileStream.Dispose();
            //}
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif

        }

    }
}