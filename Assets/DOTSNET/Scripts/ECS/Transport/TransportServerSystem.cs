// Base class for all server transports.
using System;
using Unity.Entities;

namespace DOTSNET
{
    [ServerWorld]
    // TransportServerSystem should be updated AFTER all other server systems.
    // we need a guaranteed update order to avoid race conditions where it might
    // randomly be updated before other systems, causing all kinds of unexpected
    // effects. determinism is always a good idea!
    //
    // Note: we update AFTER everything else, not before. This way systems like
    //       NetworkServerSystem can apply configurations in OnStartRunning, and
    //       Transport OnStartRunning is called afterwards, not before. Other-
    //       wise the OnData etc. events wouldn't be hooked up.
    //
    // * [UpdateAfter(NetworkServerSystem)] won't work because in some cases
    //   like Pong, we inherit from NetworkServerSystem and the UpdateAfter tag
    //   won't find the inheriting class. see also:
    //   https://forum.unity.com/threads/updateafter-abstractsystemtype-wont-update-after-inheritingsystemtype-abstractsystemtype.915170/
    // * [UpdateInGroup(ApplyPhysicsGroup), OrderLast=true] would be fine, but
    //   it doesn't actually update last for some reason. could be our custom
    //   Bootstrap, or a Unity bug.
    //
    // The best solution is to update in LateSimulationSystemGroup. That's what
    // it's for anyway.
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public abstract class TransportServerSystem : TransportSystem
    {
        // events //////////////////////////////////////////////////////////////
        // NetworkServerSystem should hook into this to receive events.
        // Fallback/Multiplex transports could also hook/route those as needed.
        // => Data ArraySegments are only valid until next call, so process the
        //    events immediately!
        // => We don't call NetworkServerSystem.OnTransportConnected etc.
        //    directly. This way we have less dependencies, and it's easier to
        //    test!
        // IMPORTANT: call them from main thread!
        public Action<int> OnConnected;
        public Action<int, ArraySegment<byte>> OnData;
        public Action<int> OnDisconnected;
        // send event for statistics etc.
        public Action<int, ArraySegment<byte>> OnSend;

        // abstracts ///////////////////////////////////////////////////////////
        // check if server is running
        public abstract bool IsActive();

        // start listening
        public abstract void Start();

        // send ArraySegment to the client with connectionId
        // note: DOTSNET already packs messages. Transports don't need to.
        public abstract bool Send(int connectionId, ArraySegment<byte> segment, Channel channel);

        // disconnect one client from the server
        public abstract bool Disconnect(int connectionId);

        // get a connection's IP address
        public abstract string GetAddress(int connectionId);

        // stop the server
        public abstract void Stop();
    }
}