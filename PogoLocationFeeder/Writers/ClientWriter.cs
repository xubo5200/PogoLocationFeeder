﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PogoLocationFeeder.Config;
using PogoLocationFeeder.Helper;

namespace PogoLocationFeeder.Writers
{
    public class ClientWriter
    {
        private readonly List<TcpClient> _arrSocket = new List<TcpClient>();
        private TcpListener _listener;
        private readonly MessageCache _messageCache = new MessageCache();


        public void StartNet(int port)
        {
            Log.Plain("PogoLocationFeeder is brought to you via https://github.com/5andr0/PogoLocationFeeder");
            Log.Plain("This software is 100% free and open-source.\n");

            Log.Info("Application starting...");
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
            }
            catch (SocketException e)
            {
                Log.Fatal($"Port {port} is already in use!", e);
                throw e;
            }

            Log.Info("Connecting to feeder service pogo-feed.mmoex.com");

            StartAccept();
        }

        private void StartAccept()
        {
            _listener.BeginAcceptTcpClient(HandleAsyncConnection, _listener);
        }

        private void HandleAsyncConnection(IAsyncResult res)
        {
            StartAccept();
            var client = _listener.EndAcceptTcpClient(res);
            if (client != null && IsConnected(client.Client))
            {
                _arrSocket.Add(client);
                Log.Info($"New connection from {GetIp(client.Client)}");
            }
        }

        // A socket is still connected if a nonblocking, zero-byte Send call either:
        // 1) returns successfully or 
        // 2) throws a WAEWOULDBLOCK error code(10035)
        public static bool IsConnected(Socket client)
        {
            // This is how you can determine whether a socket is still connected.
            var blockingState = client.Blocking;

            try
            {
                var tmp = new byte[1];

                client.Blocking = false;
                client.Send(tmp, 0, 0);
                return true;
            }
            catch (SocketException e)
            {
                // 10035 == WSAEWOULDBLOCK
                return e.NativeErrorCode.Equals(10035);
            }
            finally
            {
                client.Blocking = blockingState;
            }
        }

        private static string GetIp(Socket s)
        {
            var remoteIpEndPoint = s.RemoteEndPoint as IPEndPoint;
            return remoteIpEndPoint?.ToString();
        }

        public async Task FeedToClients(List<SniperInfo> snipeList, ChannelInfo channelInfo)
        {
            // Remove any clients that have disconnected
            if (GlobalSettings.ThreadPause) return;
            _arrSocket.RemoveAll(x => !IsConnected(x.Client));
            var unsentMessages = _messageCache.FindUnSentMessages(snipeList);
            foreach (var target in unsentMessages)
            {
                foreach (var socket in _arrSocket) // Repeat for each connected client (socket held in a dynamic array)
                {
                    try
                    {
                        var networkStream = socket.GetStream();
                        var s = new StreamWriter(networkStream);

                        s.WriteLine(JsonConvert.SerializeObject(target));
                        s.Flush();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Caught exception", e);
                    }
                }
                // debug output
                if (GlobalSettings.Output != null)
                    GlobalSettings.Output.PrintPokemon(target, channelInfo);

                const string timeFormat = "HH:mm:ss";
                Log.Pokemon($"{channelInfo}: {target.Id} at {target.Latitude.ToString(CultureInfo.InvariantCulture)},{target.Longitude.ToString(CultureInfo.InvariantCulture)}"
                            + " with " + (!target.IV.Equals(default(double)) ? $"{target.IV}% IV" : "unknown IV")
                            +
                            (target.ExpirationTimestamp != default(DateTime)
                                ? $" until {target.ExpirationTimestamp.ToString(timeFormat)}"
                                : ""));
            }
        }
    }
}