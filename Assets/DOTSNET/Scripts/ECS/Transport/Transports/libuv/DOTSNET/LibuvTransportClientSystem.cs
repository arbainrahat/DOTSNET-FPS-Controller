using System;
using System.Net;
using Unity.Entities;
using UnityEngine;
using libuv2k;
using libuv2k.Native;

namespace DOTSNET.Libuv
{
    [ClientWorld]
    // use SelectiveSystemAuthoring to create it selectively
    [DisableAutoCreation]
    public class LibuvTransportClientSystem : TransportClientSystem
    {
        // configuration
        public ushort Port = 7777;
        // libuv can be ticked multiple times per frame up to max so we don't
        // deadlock
        public const int LibuvMaxTicksPerFrame = 100;

        // Libuv state
        //
        // IMPORTANT: do NOT create new Loop & Server here, otherwise a loop is
        //            also allocated if we run a test while a scene with this
        //            component on a GameObject is openened.
        //
        //            we need to create it when needed and dispose when we are
        //            done, otherwise dispose isn't called until domain reload.
        //
        Loop loop;
        TcpStream client;

        public override bool Available()
        {
            // available only where we built the native library
            return Application.platform == RuntimePlatform.OSXEditor ||
                   Application.platform == RuntimePlatform.OSXPlayer ||
                   Application.platform == RuntimePlatform.WindowsEditor ||
                   Application.platform == RuntimePlatform.WindowsPlayer ||
                   Application.platform == RuntimePlatform.LinuxEditor ||
                   Application.platform == RuntimePlatform.LinuxPlayer;
        }

        public override int GetMaxPacketSize() => TcpStream.MaxMessageSize;

        public override bool IsConnected() =>
            client != null && client.IsActive;

        public override void Connect(string hostname)
        {
            if (client != null)
                return;

            // libuv doesn't resolve host name, and it needs ipv4.
            if (LibuvUtils.ResolveToIPV4(hostname, out IPAddress address))
            {
                // connect client
                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, IPEndPoint.MinPort);
                IPEndPoint remoteEndPoint = new IPEndPoint(address, Port);

                Debug.Log("Libuv connecting to: " + address + ":" + Port);
                client = new TcpStream(loop);
                client.onClientConnect = OnLibuvConnected;
                client.onMessage = OnLibuvMessage;
                client.onError = OnLibuvError;
                client.onClosed = OnLibuvClosed;
                client.NoDelay(true);
                client.ConnectTo(localEndPoint, remoteEndPoint);
            }
            else Debug.LogWarning("Libuv Connect: no IPv4 found for hostname: " + hostname);
        }

        public override bool Send(ArraySegment<byte> segment, Channel channel)
        {
            if (loop != null && client != null)
            {
                client.Send(segment);
                OnSend?.Invoke(segment); // statistics etc.
                return true;
            }
            return false;
        }

        public override void Disconnect()
        {
            // Dispose will disconnect, and OnLibuvClosed will clean up
            client?.Dispose();
            client = null;
        }

        // libuv callbacks /////////////////////////////////////////////////////
        void OnLibuvConnected(TcpStream handle, Exception exception)
        {
            // setup callbacks

            // close if errors (AFTER setting up onClosed callback!)
            if (exception != null)
            {
                Debug.Log($"libuv cl: client error {exception}");
                client.Dispose();
                return;
            }

            // dotsnet event
            base.OnConnected();

            Debug.Log($"libuv cl: client connected.");
        }

        void OnLibuvMessage(TcpStream handle, ArraySegment<byte> segment)
        {
            // DOTSNET event
            base.OnData(segment);
        }

        void OnLibuvError(TcpStream handle, Exception error)
        {
            // dotsnet event
            // (can be null if we are in the process of stopping the editor)
            Debug.LogWarning($"libuv cl: read error {error}");
            base.OnDisconnected?.Invoke();
        }

        void OnLibuvClosed(TcpStream handle)
        {
            Debug.Log("libuv cl: closed connection");

            // important: clear the connection BEFORE calling the DOTSNET event
            // otherwise DOTSNET OnDisconnected might try to send to a closed
            // connection which we didn't clear yet. do it first.
            client = null;

            // dotsnet event
            // (can be null if we are in the process of stopping the editor)
            base.OnDisconnected?.Invoke();
        }

        // ECS /////////////////////////////////////////////////////////////////
        // use OnCreate to set everything up.
        // NetworkServerSystem.OnStartRunning & headless start happen BEFORE
        // Transport.OnStartRunning, so by then it would be too late for
        // headless server starts.
        protected override void OnCreate()
        {
            // TODO sharp_uv buffer sizes aren't configurable yet because
            // calling SetSend/RecvBufferSize gives EBADF in TcpStream ctor.
            //
            // configure buffer sizes
            //Pipeline.ReceiveBufferSize = Pipeline.SendBufferSize = SendReceiveBufferSize;

            // initialize
            libuv2k.libuv2k.Initialize();

            // configure logging
            Log.Info = Debug.Log;
            Log.Warning = Debug.LogWarning;
            Log.Error = Debug.LogError;

            // IMPORTANT: create loop only once
            // DO NOT dispose in OnDisconnect, since that's called from within
            // a loop's uv_run. disposing while it's running could crash libuv.
            loop = new Loop();
        }

        protected override void OnUpdate()
        {
            // tick libuv while loop is valid
            // IMPORTANT: tick even if client is null.
            //            when disposing the server, uv_close callbacks are
            //            only called in the next loop update, so we need to
            //            keep updating the loop even if server was disposed.
            if (loop != null)
            {
                // Run with UV_RUN_NOWAIT returns 0 when nothing to do, but we
                // should avoid deadlocks via LibuvMaxTicksPerFrame
                for (int i = 0; i < LibuvMaxTicksPerFrame; ++i)
                {
                    if (loop.Run(uv_run_mode.UV_RUN_NOWAIT) == 0)
                    {
                        //Debug.Log("libuv cl ticked only " + i + " times");
                        break;
                    }
                }
            }
        }

        protected override void OnDestroy()
        {
            // IMPORTANT: dispose loop only once
            // DO NOT dispose in OnDisconnect, since that's called from within
            // a loop's uv_run. disposing while it's running could crash libuv.
            loop?.Dispose();
            loop = null;

            // clean up everything else properly
            libuv2k.libuv2k.Shutdown();
        }
    }
}