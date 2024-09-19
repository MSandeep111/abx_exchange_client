using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

class ABXClient
{
    private const string serverIp = "127.0.0.1";
    private const int serverPort = 3000;
    private static HashSet<int> receivedSequences = new HashSet<int>();
    private static List<int> missingSequences = new List<int>();
    private static int maxSequence = 0;

    static void Main(string[] args)
    {
        try
        {
            using (TcpClient client = new TcpClient(serverIp, serverPort))
            using (NetworkStream stream = client.GetStream())
            {
                Console.WriteLine("Client connected.");

                // Call Type 1: Stream All Packets
                byte[] streamAllPacketsRequest = CreateRequestPayload(1);
                stream.Write(streamAllPacketsRequest, 0, streamAllPacketsRequest.Length);
                Console.WriteLine("Stream All Packets request sent.");

                // Read response from server
                ReadAndProcessPackets(stream);

                // Identify missing sequences
                for (int i = 1; i <= maxSequence; i++)
                {
                    if (!receivedSequences.Contains(i))
                    {
                        missingSequences.Add(i);
                    }
                }

                // Request missing packets
                foreach (int seq in missingSequences)
                {
                    byte[] resendPacketRequest = CreateRequestPayload(2, (byte)seq);
                    stream.Write(resendPacketRequest, 0, resendPacketRequest.Length);
                    Console.WriteLine($"Resend Packet request sent for sequence {seq}.");

                    // Read response for the specific resend request
                    ReadAndProcessSinglePacket(stream, seq);
                }

                Console.WriteLine("Client disconnected.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }

    private static byte[] CreateRequestPayload(byte callType, byte resendSeq = 0)
    {
        byte[] payload = new byte[2];
        payload[0] = callType; // Call Type: 1 for Stream All Packets, 2 for Resend Packet
        payload[1] = resendSeq; // Sequence number for Call Type 2
        return payload;
    }

    private static void ReadAndProcessPackets(NetworkStream stream)
    {
        byte[] buffer = new byte[1024];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            ParseResponse(buffer, bytesRead);
        }
    }

    private static void ReadAndProcessSinglePacket(NetworkStream stream, int expectedSequence)
    {
        byte[] buffer = new byte[17]; // Each packet is 17 bytes
        int totalBytesRead = 0;

        while (totalBytesRead < buffer.Length)
        {
            int bytesRead = stream.Read(buffer, totalBytesRead, buffer.Length - totalBytesRead);
            if (bytesRead == 0)
            {
                Console.WriteLine("Server closed the connection unexpectedly.");
                return;
            }
            totalBytesRead += bytesRead;
        }

        // Now we have a complete packet
        ParseResponse(buffer, buffer.Length, expectedSequence);
    }

    private static void ParseResponse(byte[] buffer, int bytesRead, int? expectedSequence = null)
    {
        // Each packet is 17 bytes: 4 for Symbol, 1 for Buy/Sell Indicator, 4 for Quantity, 4 for Price, 4 for Packet Sequence
        const int packetSize = 17;

        for (int i = 0; i < bytesRead; i += packetSize)
        {
            if (i + packetSize > bytesRead)
                break;

            string symbol = Encoding.ASCII.GetString(buffer, i, 4);
            char buySellIndicator = (char)buffer[i + 4];
            int quantity = BitConverter.ToInt32(buffer, i + 5);
            int price = BitConverter.ToInt32(buffer, i + 9);
            int packetSequence = BitConverter.ToInt32(buffer, i + 13);

            // Adjust for big-endian byte order
            quantity = IPAddress.NetworkToHostOrder(quantity);
            price = IPAddress.NetworkToHostOrder(price);
            packetSequence = IPAddress.NetworkToHostOrder(packetSequence);

            // Ensure the received packet sequence matches the expected sequence for resend requests
            if (expectedSequence.HasValue && packetSequence != expectedSequence.Value)
            {
                Console.WriteLine($"Unexpected sequence number. Expected: {expectedSequence}, Received: {packetSequence}");
                continue;
            }

            Console.WriteLine($"Symbol: {symbol}, Buy/Sell: {buySellIndicator}, Quantity: {quantity}, Price: {price}, Sequence: {packetSequence}");

            // Track received sequences
            receivedSequences.Add(packetSequence);
            if (packetSequence > maxSequence)
            {
                maxSequence = packetSequence;
            }
        }
    }
}
