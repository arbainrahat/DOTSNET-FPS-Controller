// Base class for all client Transports.
using System;
using Unity.Entities;

namespace DOTSNET
{
    [ClientWorld]
    // TransportClientSystem should be updated AFTER all other client systems.
    // we need a guaranteed update order to avoid race conditions where it might
    // randomly be updated before other systems, causing all kinds of unexpected
    // effects. determinism is always a good idea!
    //
    // Note: we update AFTER everything else, not before. This way systems like
    //       NetworkClientSystem can apply configurations in OnStartRunning, and
    //       Transport OnStartRunning is called afterwards, not before. Other-
    //       wise the OnData etc. events wouldn't be hooked up.
    //
    // * [UpdateAfter(NetworkClientSystem)] won't work because in some cases
    //   like Pong, we inherit from NetworkClientSystem and the UpdateAfter tag
    //   won't find the inheriting class. see also:
    //   https://forum.unity.com/threads/updateafter-abstractsystemtype-wont-update-after-inheritingsystemtype-abstractsystemtype.915170/
    // * [UpdateInGroup(ApplyPhysicsGroup), OrderLast=true] would be fine, but
    //   it doesn't actually update last for some reason. could be our custom
    //   Bootstrap, or a Unity bug.
    //
    // The best solution is to update in LateSimulationSystemGroup. That's what
    // it's for anyway.
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public abstract class TransportClientSystem : TransportSystem
    {
        // events //////////////////////////////////////////////////////////////
        // NetworkClientSystem should hook into this to receive events.
        // Fallback/Multiplex transports could also hook/route those as needed.
        // => Data ArraySegments are only valid until next call, so process the
        //    events immediately!
        // => We don't call NetworkClientSystem.OnTransportConnected etc.
        //    directly. This way we have less dependencies, and it's easier to
        //    test!
        // IMPORTANT: call them from main thread!
        public Action OnConnected;
        public Action<ArraySegment<byte>> OnData;
        public Action OnDisconnected;
        // send event for statistics etc.
        public Action<ArraySegment<byte>> OnSend;

        // abstracts ///////////////////////////////////////////////////////////
        // check if client is connected
        public abstract bool IsConnected();

        // connect client to address
        public abstract void Connect(string address);

        // send ArraySegment via client. segment is only valid until returning.
        public abstract bool Send(ArraySegment<byte> segment, Channel channel);

        // disconnect the client
        public abstract void Disconnect();
    }
}